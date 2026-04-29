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

        private static string GlobalTemplatesFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "global_templates.json");

        public static ObservableCollection<VendorModel> Load()
        {
            try
            {
                if (!File.Exists(AppPaths.VendorsFilePath))
                {
                    var seeded = CreateSeedData();
                    Save(seeded);
                    return seeded;
                }
                string json = File.ReadAllText(AppPaths.VendorsFilePath);
                var items = JsonSerializer.Deserialize<List<VendorModel>>(json, JsonOptions);
                if (items == null || items.Count == 0) return CreateSeedData();
                return new ObservableCollection<VendorModel>(items.Select(CloneVendor));
            }
            catch
            {
                return CreateSeedData();
            }
        }

        public static void Save(IEnumerable<VendorModel> vendors)
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            var normalized = vendors.Select(CloneVendor).OrderBy(v => v.VendorName, StringComparer.OrdinalIgnoreCase).ToList();
            string json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(AppPaths.VendorsFilePath, json);
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
                if (!File.Exists(GlobalTemplatesFilePath))
                {
                    templates = CreateDefaultGlobalTemplates();
                }
                else
                {
                    string json = File.ReadAllText(GlobalTemplatesFilePath);
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
            Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"));
            string json = JsonSerializer.Serialize(templates, JsonOptions);
            File.WriteAllText(GlobalTemplatesFilePath, json);
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
                Addresses = new ObservableCollection<AddressModel>(safeAddresses.Select(a => new AddressModel { IsMain = a.IsMain, LocationName = a.LocationName?.Trim() ?? "", FullAddress = a.FullAddress?.Trim() ?? "" })),
                Managers = new ObservableCollection<ManagerModel>(safeManagers.Select(m => new ManagerModel { ManagerName = m.ManagerName?.Trim() ?? "", ContactNumber = m.ContactNumber?.Trim() ?? "" }))
            };
        }
    }
}