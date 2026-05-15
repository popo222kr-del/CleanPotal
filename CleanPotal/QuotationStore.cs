using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace CleanPotal
{
    public static class QuotationStore
    {
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };
        private static string DataDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        private static string QuotationPath => Path.Combine(DataDir, "quotations.json");
        private static string ProductMasterPath => Path.Combine(DataDir, "product_master.json");

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

        public static void SaveProductMaster(ObservableCollection<ProductMasterItem> list)
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(ProductMasterPath, JsonSerializer.Serialize(list, _opts));
        }
    }
}
