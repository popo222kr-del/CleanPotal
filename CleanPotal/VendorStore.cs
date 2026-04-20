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
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

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
                if (items == null || items.Count == 0)
                {
                    var seeded = CreateSeedData();
                    Save(seeded);
                    return seeded;
                }

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
            var normalized = vendors
                .Select(CloneVendor)
                .OrderBy(v => v.VendorName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(AppPaths.VendorsFilePath, json);
        }

        public static VendorModel? FindByName(string vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName)) return null;
            return Load().FirstOrDefault(v => string.Equals(v.VendorName?.Trim(), vendorName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static ObservableCollection<VendorModel> CreateSeedData()
        {
            return new ObservableCollection<VendorModel>
            {
                new VendorModel
                {
                    VendorName = "우암",
                    Category = "",
                    Addresses = new ObservableCollection<AddressModel>
                    {
                        new AddressModel { IsMain = true, LocationName = "본사", FullAddress = "경기 안성시 미양면 강덕1길 138-4" }
                    },
                    Managers = new ObservableCollection<ManagerModel>
                    {
                        new ManagerModel { ManagerName = "최남용", ContactNumber = "010-9008-3089" }
                    },
                    Templates = new ObservableCollection<VendorTemplateModel>()
                },
                new VendorModel
                {
                    VendorName = "영신",
                    Category = "",
                    Addresses = new ObservableCollection<AddressModel>
                    {
                        new AddressModel { IsMain = true, LocationName = "본사", FullAddress = "충북 진천군 광혜원면 용소1길 10" }
                    },
                    Managers = new ObservableCollection<ManagerModel>
                    {
                        new ManagerModel { ManagerName = "정성수", ContactNumber = "010-2375-1930" }
                    },
                    Templates = new ObservableCollection<VendorTemplateModel>()
                }
            };
        }

        private static VendorModel CloneVendor(VendorModel source)
        {
            string cat = source.Category?.Trim() ?? "";
            if (cat == "일반") cat = "";

            return new VendorModel
            {
                VendorName = source.VendorName?.Trim() ?? string.Empty,
                Category = cat,
                Addresses = new ObservableCollection<AddressModel>(source.Addresses.Select(a => new AddressModel
                {
                    IsMain = a.IsMain,
                    LocationName = a.LocationName?.Trim() ?? string.Empty,
                    FullAddress = a.FullAddress?.Trim() ?? string.Empty
                })),
                Managers = new ObservableCollection<ManagerModel>(source.Managers.Select(m => new ManagerModel
                {
                    ManagerName = m.ManagerName?.Trim() ?? string.Empty,
                    ContactNumber = m.ContactNumber?.Trim() ?? string.Empty
                })),
                // 🔥 템플릿 복제 추가
                Templates = new ObservableCollection<VendorTemplateModel>((source.Templates ?? new()).Select(t => new VendorTemplateModel
                {
                    IsUsed = t.IsUsed,
                    ItemCode = t.ItemCode?.Trim() ?? string.Empty,
                    TemplatePath = t.TemplatePath?.Trim() ?? string.Empty,
                    BasePath = t.BasePath?.Trim() ?? string.Empty,
                    FileNameRule = t.FileNameRule?.Trim() ?? string.Empty
                }))
            };
        }
    }
}