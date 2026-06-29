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

        /// <summary>
        /// 앱 시작 시 한 번 호출.
        /// - 구 경로(bin/Data)나 로컬 APPDATA에 파일이 있으면 네트워크 공유폴더로 이전.
        /// - 설정 파일은 로컬 APPDATA로 이전.
        /// </summary>
        public static void MigrateFromLocalIfNeeded()
        {
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
                if (!File.Exists(ConfigPath)) return new();
                return JsonSerializer.Deserialize<QuotationConfig>(File.ReadAllText(ConfigPath)) ?? new();
            }
            catch { return new(); }
        }

        public static void SaveConfig(QuotationConfig config)
        {
            if (SessionManager.GuestWriteBlocked) return;
            try { Directory.CreateDirectory(SharedDir); } catch { }
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, _opts));
        }

        public static ObservableCollection<QuotationModel> LoadQuotations()
        {
            try
            {
                if (!File.Exists(QuotationPath)) return new();
                return JsonSerializer.Deserialize<ObservableCollection<QuotationModel>>(
                    File.ReadAllText(QuotationPath)) ?? new();
            }
            catch { return new(); }
        }

        public static void SaveQuotations(ObservableCollection<QuotationModel> list)
        {
            if (SessionManager.GuestWriteBlocked) return;
            try { Directory.CreateDirectory(SharedDir); } catch { }
            File.WriteAllText(QuotationPath, JsonSerializer.Serialize(list, _opts));
        }

        public static ObservableCollection<ProductMasterItem> LoadProductMaster()
        {
            try
            {
                if (!File.Exists(ProductMasterPath)) return new();
                return JsonSerializer.Deserialize<ObservableCollection<ProductMasterItem>>(
                    File.ReadAllText(ProductMasterPath)) ?? new();
            }
            catch { return new(); }
        }

        public static void SaveProductMaster(IEnumerable<ProductMasterItem> list)
        {
            if (SessionManager.GuestWriteBlocked) return;
            try { Directory.CreateDirectory(SharedDir); } catch { }
            File.WriteAllText(ProductMasterPath, JsonSerializer.Serialize(list, _opts));
        }
    }
}
