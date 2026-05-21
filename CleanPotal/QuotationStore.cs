using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace CleanPotal
{
    public static class QuotationStore
    {
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        // %APPDATA%\CleanPotal\ — 패치·재배포와 무관하게 유지되는 경로
        private static string DataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CleanPotal");

        private static string QuotationPath     => Path.Combine(DataDir, "quotations.json");
        private static string ProductMasterPath => Path.Combine(DataDir, "product_master.json");
        private static string ConfigPath        => Path.Combine(DataDir, "quotation_config.json");

        /// <summary>
        /// 앱 시작 시 한 번 호출. 구 경로(bin/Data, 네트워크)에 파일이 있으면 APPDATA로 복사.
        /// </summary>
        public static void MigrateFromLocalIfNeeded()
        {
            try
            {
                Directory.CreateDirectory(DataDir);

                // 후보 경로: 구 bin/Data → 네트워크 DataRoot 순으로 확인
                string[] candidateDirs =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"),
                    AppPaths.DataRoot
                };

                foreach (string src in candidateDirs)
                {
                    TryCopyFile(Path.Combine(src, "quotations.json"),     QuotationPath);
                    TryCopyFile(Path.Combine(src, "product_master.json"), ProductMasterPath);
                    TryCopyFile(Path.Combine(src, "quotation_config.json"), ConfigPath);
                }
            }
            catch { }
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
            Directory.CreateDirectory(DataDir);
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
            Directory.CreateDirectory(DataDir);
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
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(ProductMasterPath, JsonSerializer.Serialize(list, _opts));
        }
    }
}
