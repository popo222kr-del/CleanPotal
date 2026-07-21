using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CleanPotal
{
    internal static class VendorStore
    {
        // 🔥 [버그 해결 1] 한글 및 네트워크 경로(\\) 특수문자를 엄격하게 변환하지 않고 원형 그대로 저장/파싱하여 파일 깨짐 원천 차단
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static string GlobalTemplatesFilePath => AppPaths.GlobalTemplatesPath;
        // 업체 목록·전역 템플릿은 이제 SQLite(dispatch.db) 의 AppData 에 저장(파일 경쟁/손상 방지).
        private const string VendorsKey = "vendors";
        private const string GlobalTemplatesKey = "global_templates";

        public static ObservableCollection<VendorModel> Load()
        {
            ObservableCollection<VendorModel> list;
            try
            {
                string? json = AppDataRepository.Get(VendorsKey);
                var items = string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<List<VendorModel>>(json, JsonOptions);
                list = items == null
                    ? new ObservableCollection<VendorModel>()
                    : new ObservableCollection<VendorModel>(items.Select(CloneVendor));
            }
            catch { list = new ObservableCollection<VendorModel>(); }

            MergeVendorsFromBackupOnce(list);

            // 최종 안전망: 비어 있으면 .migrated 백업에서 복구. 그래도 없으면 시드를 '표시만' 한다.
            // ⚠️ 과거에는 읽기 실패 시 시드(우암 1개)를 즉시 저장해 전체 업체가 덮어써지는 사고가 있었다.
            //    어떤 경우에도 읽기 실패가 저장으로 이어지면 안 된다.
            if (list.Count == 0)
            {
                var restored = TryLoadVendorBackup();
                if (restored != null && restored.Count > 0)
                {
                    foreach (var v in restored) list.Add(CloneVendor(v));
                    Save(list);
                }
                else return CreateSeedData();   // 저장하지 않고 표시만
            }
            return list;
        }

        // 배포 전 테스트/일시 오류로 사라진 업체를 .migrated 백업에서 1회 병합 복구.
        // 업체명 기준으로 현재 목록에 없는 업체만 추가 → 이후 등록·수정분은 그대로 보존.
        // 완료 후 플래그를 남겨 재실행하지 않음(이후 삭제한 업체가 되살아나는 것 방지).
        private const string VendorsMergeFlagKey = "vendors_backup_merged_v1";
        private static void MergeVendorsFromBackupOnce(ObservableCollection<VendorModel> list)
        {
            if (!AppPaths.DbMigrationEnabled) return;
            try
            {
                if (AppDataRepository.Get(VendorsMergeFlagKey) == "1") return;

                var backup = TryLoadVendorBackup();
                if (backup != null && backup.Count > 0)
                {
                    var names = new HashSet<string>(
                        list.Select(v => (v.VendorName ?? "").Trim()),
                        StringComparer.OrdinalIgnoreCase);
                    int added = 0;
                    foreach (var v in backup)
                    {
                        string name = (v.VendorName ?? "").Trim();
                        if (name.Length == 0 || names.Contains(name)) continue;
                        list.Add(CloneVendor(v));
                        names.Add(name);
                        added++;
                    }
                    if (added > 0) Save(list);
                }
                AppDataRepository.Set(VendorsMergeFlagKey, "1");
            }
            catch { }
        }

        private static List<VendorModel>? TryLoadVendorBackup()
        {
            try
            {
                string bak = Path.Combine(AppPaths.DataRoot, "vendors.json.migrated");
                if (!File.Exists(bak)) return null;
                return JsonSerializer.Deserialize<List<VendorModel>>(File.ReadAllText(bak), JsonOptions);
            }
            catch { return null; }
        }

        public static void Save(IEnumerable<VendorModel> vendors)
        {
            var normalized = vendors.Select(CloneVendor).OrderBy(v => v.VendorName, StringComparer.OrdinalIgnoreCase).ToList();
            string json = JsonSerializer.Serialize(normalized, JsonOptions);
            AppDataRepository.Set(VendorsKey, json);
        }

        public static VendorModel? FindByName(string vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName)) return null;
            return Load().FirstOrDefault(v => string.Equals(v.VendorName?.Trim(), vendorName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static ObservableCollection<GlobalTemplateModel> LoadGlobalTemplates()
        {
            try
            {
                ObservableCollection<GlobalTemplateModel> templates;
                string? json = AppDataRepository.Get(GlobalTemplatesKey);
                if (string.IsNullOrWhiteSpace(json))
                {
                    templates = CreateDefaultGlobalTemplates();
                }
                else
                {
                    var items = JsonSerializer.Deserialize<List<GlobalTemplateModel>>(json, JsonOptions);
                    templates = items == null || items.Count == 0 ? CreateDefaultGlobalTemplates() : new ObservableCollection<GlobalTemplateModel>(items);
                }

                // 기존 파일에 D 항목이 없으면 무조건 리스트에 추가하고 파일까지 즉시 갱신
                if (!templates.Any(t => t.ProductCode?.ToUpper() == "D"))
                {
                    templates.Add(new GlobalTemplateModel { ProductCode = "D", ProductName = "PLATE, DISK" });
                    SaveGlobalTemplates(templates);
                }

                return templates;
            }
            catch
            {
                // 에러 발생 시 데이터가 날아가는 것을 방지하기 위해, 가능한 한 기본 폼만 반환하고 기존 파일은 덮어쓰지 않음
                return CreateDefaultGlobalTemplates();
            }
        }

        public static void SaveGlobalTemplates(IEnumerable<GlobalTemplateModel> templates)
        {
            AppDataRepository.Set(GlobalTemplatesKey, JsonSerializer.Serialize(templates, JsonOptions));
        }

        private static ObservableCollection<GlobalTemplateModel> CreateDefaultGlobalTemplates()
        {
            return new ObservableCollection<GlobalTemplateModel>
            {
                new GlobalTemplateModel { ProductCode = "U", ProductName = "OUTER" },
                new GlobalTemplateModel { ProductCode = "I", ProductName = "INNER" },
                new GlobalTemplateModel { ProductCode = "B", ProductName = "BOAT" },
                new GlobalTemplateModel { ProductCode = "S", ProductName = "SiC BOAT" },
                new GlobalTemplateModel { ProductCode = "P", ProductName = "PEDESTAL" },
                new GlobalTemplateModel { ProductCode = "A", ProductName = "ACC" },
                new GlobalTemplateModel { ProductCode = "D", ProductName = "PLATE, DISK" }
            };
        }

        private static ObservableCollection<VendorModel> CreateSeedData()
        {
            return new ObservableCollection<VendorModel>
            {
                new VendorModel
                {
                    VendorName = "우암", Category = "", BasePath = "",
                    Addresses = new ObservableCollection<AddressModel> { new AddressModel { IsMain = true, LocationName = "본사", FullAddress = "경기 안성시 미양면 강덕1길 138-4" } },
                    Managers = new ObservableCollection<ManagerModel> { new ManagerModel { ManagerName = "최남용", ContactNumber = "010-9008-3089" } }
                }
            };
        }

        private static VendorModel CloneVendor(VendorModel source)
        {
            // 🔥 [버그 해결 2] 주소나 담당자가 없을 경우(null) Select 함수가 터지면서 데이터를 싹 날려버리던 치명적 버그 수정
            var safeAddresses = source.Addresses ?? new ObservableCollection<AddressModel>();
            var safeManagers = source.Managers ?? new ObservableCollection<ManagerModel>();

            return new VendorModel
            {
                VendorName = source.VendorName?.Trim() ?? string.Empty,
                Category = source.Category?.Trim() == "일반" ? "" : (source.Category?.Trim() ?? ""),
                BasePath = source.BasePath?.Trim() ?? string.Empty,
                IsWeekly = source.IsWeekly,
                IsFavorite = source.IsFavorite,
                Addresses = new ObservableCollection<AddressModel>(safeAddresses.Select(a => new AddressModel { IsMain = a.IsMain, LocationName = a.LocationName?.Trim() ?? "", FullAddress = a.FullAddress?.Trim() ?? "" })),
                Managers = new ObservableCollection<ManagerModel>(safeManagers.Select(m => new ManagerModel { ManagerName = m.ManagerName?.Trim() ?? "", ContactNumber = m.ContactNumber?.Trim() ?? "" }))
            };
        }
    }
}