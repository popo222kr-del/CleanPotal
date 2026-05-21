using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace CleanPotal
{
    public static class QuotationStore
    {
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        // 네트워크 DataRoot 사용 — 게시(패치)해도 데이터 유지
        private static string DataDir => AppPaths.DataRoot;
        private static string QuotationPath    => Path.Combine(DataDir, "quotations.json");
        private static string ProductMasterPath => Path.Combine(DataDir, "product_master.json");
        private static string ConfigPath        => Path.Combine(DataDir, "quotation_config.json");

        // 구 로컬 경로 (마이그레이션 전용)
        private static string OldDataDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

        /// <summary>
        /// 구 로컬 Data 폴더에 파일이 있으면 네트워크 경로로 복사 후 로컬 파일 삭제.
        /// 앱 시작 시 한 번 호출.
        /// </summary>
        public static void MigrateFromLocalIfNeeded()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                MigrateFile(
                    Path.Combine(OldDataDir, "quotations.json"),    QuotationPath);
                MigrateFile(
                    Path.Combine(OldDataDir, "product_master.json"), ProductMasterPath);
                MigrateFile(
                    Path.Combine(OldDataDir, "quotation_config.json"), ConfigPath);
            }
            catch { }
        }

        private static void MigrateFile(string oldPath, string newPath)
        {
            if (File.Exists(oldPath) && !File.Exists(newPath))
                File.Copy(oldPath, newPath);
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
