using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CleanPotal
{
    public partial class VendorManagerWindow : Window
    {
        public ObservableCollection<VendorModel> Vendors { get; set; } = new();
        private ICollectionView _vendorView;
        private string _currentFilter = "전체";
        private bool _isUpdatingUI = false;

        public VendorManagerWindow()
        {
            InitializeComponent();
            Vendors = VendorStore.Load();

            _vendorView = CollectionViewSource.GetDefaultView(Vendors);
            _vendorView.Filter = VendorFilter;
            VendorListBox.ItemsSource = _vendorView;

            UpdateSummary();

            if (Vendors.Count > 0)
                VendorListBox.SelectedIndex = 0;
        }

        private void FilterTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Content != null)
            {
                _currentFilter = rb.Content.ToString();
                _vendorView?.Refresh();
                UpdateSummary();

                if (VendorListBox != null && VendorListBox.SelectedItem == null && VendorListBox.Items.Count > 0)
                {
                    VendorListBox.SelectedIndex = 0;
                }
            }
        }

        private bool VendorFilter(object item)
        {
            if (item is VendorModel v)
            {
                if (_currentFilter == "전체") return true;
                string cat = v.Category?.Trim() ?? "";
                return cat == _currentFilter;
            }
            return false;
        }

        private void VendorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _isUpdatingUI = true;
            if (VendorListBox.SelectedItem is VendorModel selected)
            {
                TxtVendorName.Text = selected.VendorName;

                string cat = selected.Category?.Trim() ?? "";
                CmbCategory.SelectedItem = null; // 초기화
                foreach (ComboBoxItem item in CmbCategory.Items)
                {
                    if (item.Content.ToString() == cat)
                    {
                        CmbCategory.SelectedItem = item;
                        break;
                    }
                }

                DetailGrid.ItemsSource = selected.Addresses;
                ManagersDataGrid.ItemsSource = selected.Managers;
            }
            else
            {
                TxtVendorName.Text = string.Empty;
                if (CmbCategory != null) CmbCategory.SelectedIndex = -1;
                DetailGrid.ItemsSource = null;
                ManagersDataGrid.ItemsSource = null;
            }
            _isUpdatingUI = false;
        }

        private void TxtVendorName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI) return;
            if (VendorListBox.SelectedItem is not VendorModel selected) return;
            selected.VendorName = TxtVendorName.Text;
            VendorListBox.Items.Refresh();
            UpdateSummary();
        }

        private void CmbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUI) return;
            if (VendorListBox.SelectedItem is VendorModel selected && CmbCategory.SelectedItem is ComboBoxItem cbi)
            {
                string newCategory = cbi.Content?.ToString() ?? "";
                if (selected.Category != newCategory)
                {
                    selected.Category = newCategory;
                    _vendorView?.Refresh();
                    UpdateSummary();
                }
            }
        }

        private void AddVendor_Click(object sender, RoutedEventArgs e)
        {
            var baseName = "새 업체";
            var name = baseName;
            int suffix = 1;
            while (Vendors.Any(v => v.VendorName == name))
                name = $"{baseName} {suffix++}";

            // 전체 탭에서 생성 시 미분류("") 처리, 특정 탭에서 생성 시 해당 탭 이름 할당
            string defaultCat = _currentFilter == "전체" ? "" : _currentFilter;

            var vendor = new VendorModel
            {
                VendorName = name,
                Category = defaultCat,
                Addresses = new ObservableCollection<AddressModel>
                {
                    new AddressModel { IsMain = true, LocationName = "본사", FullAddress = string.Empty }
                },
                Managers = new ObservableCollection<ManagerModel>
                {
                    new ManagerModel { ManagerName = string.Empty, ContactNumber = string.Empty }
                }
            };

            Vendors.Add(vendor);
            VendorListBox.SelectedItem = vendor;
            UpdateSummary();
        }

        private void DeleteVendor_Click(object sender, RoutedEventArgs e)
        {
            if (VendorListBox.SelectedItem is not VendorModel selected) return;
            if (MessageBox.Show($"{selected.VendorName} 업체를 삭제하시겠습니까?", "확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            Vendors.Remove(selected);
            if (VendorListBox.Items.Count > 0) VendorListBox.SelectedIndex = 0;
            UpdateSummary();
        }

        private void AddAddress_Click(object sender, RoutedEventArgs e)
        {
            if (VendorListBox.SelectedItem is not VendorModel selected) return;
            selected.Addresses.Add(new AddressModel { IsMain = selected.Addresses.Count == 0, LocationName = "사업장", FullAddress = string.Empty });
            DetailGrid.Items.Refresh();
            UpdateSummary();
        }

        private void DeleteAddress_Click(object sender, RoutedEventArgs e)
        {
            if (VendorListBox.SelectedItem is not VendorModel selected) return;
            if (DetailGrid.SelectedItem is not AddressModel target) return;
            selected.Addresses.Remove(target);
            if (selected.Addresses.Count == 1) selected.Addresses[0].IsMain = true;
            DetailGrid.Items.Refresh();
            UpdateSummary();
        }

        private void AddManager_Click(object sender, RoutedEventArgs e)
        {
            if (VendorListBox.SelectedItem is not VendorModel selected) return;
            selected.Managers.Add(new ManagerModel { ManagerName = string.Empty, ContactNumber = string.Empty });
            ManagersDataGrid.Items.Refresh();
            UpdateSummary();
        }

        private void DeleteManager_Click(object sender, RoutedEventArgs e)
        {
            if (VendorListBox.SelectedItem is not VendorModel selected) return;
            if (ManagersDataGrid.SelectedItem is not ManagerModel target) return;
            selected.Managers.Remove(target);
            ManagersDataGrid.Items.Refresh();
            UpdateSummary();
        }

        private void SaveVendor_Click(object sender, RoutedEventArgs e)
        {
            NormalizeData();
            VendorStore.Save(Vendors);
            UpdateSummary();
            MessageBox.Show("업체 기준 정보가 저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ImportExcel_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "업체/주소록 엑셀 업로드",
                Filter = "Excel Files|*.xlsx",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                using var workbook = new XLWorkbook(dlg.FileName);
                var worksheet = workbook.Worksheet(1);
                var rows = worksheet.RangeUsed().RowsUsed().Skip(1);

                int processedCount = 0;

                foreach (var row in rows)
                {
                    string vendorName = row.Cell(1).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(vendorName)) continue;

                    string locName = row.Cell(2).GetString().Trim();
                    string address = row.Cell(3).GetString().Trim();
                    string mgrName = row.Cell(4).GetString().Trim();
                    string contact = row.Cell(5).GetString().Trim();

                    var vendor = Vendors.FirstOrDefault(v => string.Equals(v.VendorName, vendorName, StringComparison.OrdinalIgnoreCase));
                    if (vendor == null)
                    {
                        vendor = new VendorModel
                        {
                            VendorName = vendorName,
                            Category = "", // 엑셀 등록 시 기본값 없음
                            Addresses = new ObservableCollection<AddressModel>(),
                            Managers = new ObservableCollection<ManagerModel>()
                        };
                        Vendors.Add(vendor);
                    }

                    if (!string.IsNullOrWhiteSpace(address))
                    {
                        if (string.IsNullOrWhiteSpace(locName)) locName = "사업장";
                        bool addrExists = vendor.Addresses.Any(a => string.Equals(a.FullAddress, address, StringComparison.OrdinalIgnoreCase));
                        if (!addrExists)
                        {
                            vendor.Addresses.Add(new AddressModel
                            {
                                IsMain = vendor.Addresses.Count == 0,
                                LocationName = locName,
                                FullAddress = address
                            });
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(mgrName) || !string.IsNullOrWhiteSpace(contact))
                    {
                        bool mgrExists = vendor.Managers.Any(m => m.ManagerName == mgrName && m.ContactNumber == contact);
                        if (!mgrExists)
                        {
                            vendor.Managers.Add(new ManagerModel
                            {
                                ManagerName = mgrName,
                                ContactNumber = contact
                            });
                        }
                    }
                    processedCount++;
                }

                NormalizeData();
                VendorStore.Save(Vendors);
                UpdateSummary();

                MessageBox.Show($"엑셀 데이터 일괄 등록이 완료되었습니다.\n총 {processedCount}건의 데이터가 성공적으로 병합되었습니다.", "엑셀 업로드 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "엑셀 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NormalizeData()
        {
            foreach (var vendor in Vendors)
            {
                vendor.VendorName = (vendor.VendorName ?? string.Empty).Trim();

                // 기존 '일반' 데이터 청소 로직
                if (vendor.Category == "일반") vendor.Category = "";
                else vendor.Category = vendor.Category?.Trim() ?? "";

                var addresses = vendor.Addresses
                    .Where(a => !string.IsNullOrWhiteSpace(a.LocationName) || !string.IsNullOrWhiteSpace(a.FullAddress))
                    .Select(a => new AddressModel
                    {
                        IsMain = a.IsMain,
                        LocationName = (a.LocationName ?? string.Empty).Trim(),
                        FullAddress = (a.FullAddress ?? string.Empty).Trim()
                    })
                    .ToList();

                if (addresses.Count == 0)
                    addresses.Add(new AddressModel { IsMain = true, LocationName = "본사", FullAddress = string.Empty });

                if (!addresses.Any(a => a.IsMain))
                    addresses[0].IsMain = true;
                else
                {
                    bool firstMain = true;
                    foreach (var address in addresses.Where(a => a.IsMain))
                    {
                        if (firstMain) firstMain = false;
                        else address.IsMain = false;
                    }
                }

                vendor.Addresses = new ObservableCollection<AddressModel>(addresses);

                var managers = vendor.Managers
                    .Where(m => !string.IsNullOrWhiteSpace(m.ManagerName) || !string.IsNullOrWhiteSpace(m.ContactNumber))
                    .Select(m => new ManagerModel
                    {
                        ManagerName = (m.ManagerName ?? string.Empty).Trim(),
                        ContactNumber = (m.ContactNumber ?? string.Empty).Trim()
                    })
                    .ToList();

                vendor.Managers = new ObservableCollection<ManagerModel>(managers);
            }

            var cleaned = Vendors.Where(v => !string.IsNullOrWhiteSpace(v.VendorName)).ToList();
            Vendors = new ObservableCollection<VendorModel>(cleaned);

            _vendorView = CollectionViewSource.GetDefaultView(Vendors);
            _vendorView.Filter = VendorFilter;
            VendorListBox.ItemsSource = _vendorView;

            if (VendorListBox.Items.Count > 0 && VendorListBox.SelectedItem == null)
                VendorListBox.SelectedIndex = 0;
            else if (VendorListBox.SelectedItem is VendorModel selected)
            {
                DetailGrid.ItemsSource = selected.Addresses;
                ManagersDataGrid.ItemsSource = selected.Managers;
            }
        }

        private void UpdateSummary()
        {
            if (_vendorView == null || Vendors == null) return;

            int addressCount = Vendors.Sum(v => v.Addresses.Count);
            int managerCount = Vendors.Sum(v => v.Managers.Count);
            int visibleCount = _vendorView.Cast<object>().Count();

            TxtSummary.Text = $"전체 {Vendors.Count}개 (현재 탭 {visibleCount}개) · 주소 {addressCount}개 · 담당자 {managerCount}명";
        }
    }
}