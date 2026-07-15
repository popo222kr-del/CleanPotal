using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace CleanPotal
{
    public static class QuotationStore
    {
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        // 견적서·단가표는 네트워크 공유폴더 — 모든 사용자가 동일한 데이터를 봄
        private static string SharedDir => AppPaths.DataRoot;

        // 사용자별 설정(사업자번호 등)은 로컬 APPDATA — 패치와 무관하게 유지
        private static string LocalConfigDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CleanPotal");

        private static string QuotationPath     => Path.Combine(SharedDir,     "quotations.json");
        private static string ProductMasterPath => Path.Combine(SharedDir,     "product_master.json");
        private static string ConfigPath        => Path.Combine(SharedDir,     "quotation_config.json");

        // 견적/단가표/설정은 이제 SQLite(dispatch.db) 의 AppData 에 저장(파일 경쟁/손상 방지).
        private const string QuotationsKey = "quotations";
        private const string ProductMasterKey = "product_master";
        private const string ConfigKey = "quotation_config";

        /// <summary>
        /// 앱 시작 시 한 번 호출.
        /// - 구 경로(bin/Data)나 로컬 APPDATA에 파일이 있으면 네트워크 공유폴더로 이전.
        /// - 설정 파일은 로컬 APPDATA로 이전.
        /// </summary>
        public static void MigrateFromLocalIfNeeded()
        {
            // 🚧 DB 이관 완료(플래그 ON) 후에는 공유폴더에 json 이 없는 게 정상 상태.
            //    이때 옛 로컬 파일을 NAS로 복사하면 오래된 데이터가 DB를 덮어쓰게 되므로 전체 스킵.
            if (AppPaths.DbMigrationEnabled) return;

            try { Directory.CreateDirectory(SharedDir); }     catch { }
            try { Directory.CreateDirectory(LocalConfigDir); } catch { }

            // 견적서·단가표: 구 bin/Data → 로컬 APPDATA 순으로 확인해 네트워크로 복사
            string[] sharedCandidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"),
                LocalConfigDir,
            };

            foreach (string src in sharedCandidates)
            {
                TryCopyFile(Path.Combine(src, "quotations.json"),     QuotationPath);
                TryCopyFile(Path.Combine(src, "product_master.json"), ProductMasterPath);
            }

            // 설정: 구 bin/Data나 로컬 APPDATA에 있으면 네트워크로 복사
            TryCopyFile(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "quotation_config.json"),
                ConfigPath);
            TryCopyFile(
                Path.Combine(LocalConfigDir, "quotation_config.json"),
                ConfigPath);
        }

        private static void TryCopyFile(string src, string dst)
        {
            try
            {
                if (File.Exists(src) && !File.Exists(dst))
                    File.Copy(src, dst);
            }
            catch { }
        }

        public static QuotationConfig LoadConfig()
        {
            try
            {
                string? json = AppDataRepository.Get(ConfigKey);
                if (string.IsNullOrWhiteSpace(json)) return new();
                return JsonSerializer.Deserialize<QuotationConfig>(json) ?? new();
            }
            catch { return new(); }
        }

        public static void SaveConfig(QuotationConfig config)
        {
            AppDataRepository.Set(ConfigKey, JsonSerializer.Serialize(config, _opts));
        }

        public static ObservableCollection<QuotationModel> LoadQuotations()
        {
            ObservableCollection<QuotationModel> list;
            try
            {
                string? json = AppDataRepository.Get(QuotationsKey);
                list = string.IsNullOrWhiteSpace(json)
                    ? new()
                    : JsonSerializer.Deserialize<ObservableCollection<QuotationModel>>(json) ?? new();
            }
            catch { list = new(); }

            MergeQuotationsFromBackupOnce(list);

            // 최종 안전망: 그래도 비어 있으면 .migrated 백업을 그대로 표시(플래그·병합 이력과 무관하게 무조건).
            if (list.Count == 0)
            {
                try
                {
                    string bak = QuotationPath + ".migrated";
                    if (File.Exists(bak))
                    {
                        var backup = JsonSerializer.Deserialize<ObservableCollection<QuotationModel>>(File.ReadAllText(bak));
                        if (backup != null && backup.Count > 0)
                        {
                            foreach (var q in backup) list.Add(q);
                            SaveQuotations(list);   // DB에 고정 시도(실패해도 화면에는 표시됨)
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        // 배포 전 테스트로 .migrated 백업에만 남은 견적을 1회 병합 복구.
        // Id 기준으로 DB에 없는 항목만 추가 → 배포 후 새로 만든 견적은 그대로 보존.
        // 완료 후 플래그를 남겨 재실행하지 않음(이후 삭제한 견적이 되살아나는 것 방지).
        private const string QuotationsMergeFlagKey = "quotations_backup_merged_v1";
        private static void MergeQuotationsFromBackupOnce(ObservableCollection<QuotationModel> list)
        {
            if (!AppPaths.DbMigrationEnabled) return;
            try
            {
                if (AppDataRepository.Get(QuotationsMergeFlagKey) == "1") return;

                string bak = QuotationPath + ".migrated";
                if (File.Exists(bak))
                {
                    var backup = JsonSerializer.Deserialize<ObservableCollection<QuotationModel>>(File.ReadAllText(bak));
                    if (backup != null && backup.Count > 0)
                    {
                        var ids = new System.Collections.Generic.HashSet<string>();
                        foreach (var q in list) ids.Add(q.Id);
                        int added = 0;
                        foreach (var q in backup)
                            if (!string.IsNullOrEmpty(q.Id) && !ids.Contains(q.Id)) { list.Add(q); added++; }
                        if (added > 0) SaveQuotations(list);
                    }
                }
                AppDataRepository.Set(QuotationsMergeFlagKey, "1");
            }
            catch { }
        }

        public static void SaveQuotations(ObservableCollection<QuotationModel> list)
        {
            AppDataRepository.Set(QuotationsKey, JsonSerializer.Serialize(list, _opts));
        }

        public static ObservableCollection<ProductMasterItem> LoadProductMaster()
        {
            ObservableCollection<ProductMasterItem> list;
            try
            {
                string? json = AppDataRepository.Get(ProductMasterKey);
                list = string.IsNullOrWhiteSpace(json)
                    ? new()
                    : JsonSerializer.Deserialize<ObservableCollection<ProductMasterItem>>(json) ?? new();
            }
            catch { list = new(); }

            MergeMasterFromBackupOnce(list);

            // 최종 안전망: 그래도 비어 있으면 .migrated 백업을 그대로 표시
            if (list.Count == 0)
            {
                try
                {
                    string bak = ProductMasterPath + ".migrated";
                    if (File.Exists(bak))
                    {
                        var backup = JsonSerializer.Deserialize<ObservableCollection<ProductMasterItem>>(File.ReadAllText(bak));
                        if (backup != null && backup.Count > 0)
                        {
                            foreach (var m in backup) list.Add(m);
                            SaveProductMaster(list);
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        // 단가표도 동일하게 .migrated 백업에서 1회 병합(업체+품명+코드+규격 기준 중복 제외)
        private const string MasterMergeFlagKey = "product_master_backup_merged_v1";
        private static void MergeMasterFromBackupOnce(ObservableCollection<ProductMasterItem> list)
        {
            if (!AppPaths.DbMigrationEnabled) return;
            try
            {
                if (AppDataRepository.Get(MasterMergeFlagKey) == "1") return;

                string bak = ProductMasterPath + ".migrated";
                if (File.Exists(bak))
                {
                    var backup = JsonSerializer.Deserialize<ObservableCollection<ProductMasterItem>>(File.ReadAllText(bak));
                    if (backup != null && backup.Count > 0)
                    {
                        static string KeyOf(ProductMasterItem m) =>
                            $"{m.VendorName}|{m.ProductName}|{m.PartCode}|{m.Spec}".ToLowerInvariant();
                        var keys = new System.Collections.Generic.HashSet<string>();
                        foreach (var m in list) keys.Add(KeyOf(m));
                        int added = 0;
                        foreach (var m in backup)
                            if (!keys.Contains(KeyOf(m))) { list.Add(m); keys.Add(KeyOf(m)); added++; }
                        if (added > 0) SaveProductMaster(list);
                    }
                }
                AppDataRepository.Set(MasterMergeFlagKey, "1");
            }
            catch { }
        }

        public static void SaveProductMaster(IEnumerable<ProductMasterItem> list)
        {
            AppDataRepository.Set(ProductMasterKey, JsonSerializer.Serialize(list, _opts));
        }
    }
}
