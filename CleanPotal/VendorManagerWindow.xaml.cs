using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace CleanPotal
{
    public partial class VendorManagerWindow : Window
    {
        public ObservableCollection<VendorModel> Vendors { get; set; } = new();
        private ICollectionView _vendorView;
        private string _currentFilter = "전체";
        private bool _isUpdatingUI = false;

        private string ConfigFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "master_db_config.txt");

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

        // =========================================================================
        // 🔥 버그 완벽 해결: 마우스 클릭 시 포커스를 강제로 가져와 무조건 원클릭에 작동하게 만듭니다.
        // =========================================================================
        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);

            if (e.OriginalSource is DependencyObject depObj)
            {
                // 입력창(TextBox)이나 스크롤 조작이 아닐 경우, 무조건 화면 전체로 포커스를 
                // 강제 회수하여 다음 클릭(버튼 등)이 씹히지 않고 한 번에 작동하도록 합니다.
                if (!(depObj is System.Windows.Controls.Primitives.TextBoxBase) &&
                    !(depObj is System.Windows.Controls.Primitives.Thumb))
                {
                    Keyboard.Focus(this);
                }
            }
        }
        // =========================================================================

        // --- 필터 및 검색 로직 ---
        private void FilterTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Content != null)
            {
                _currentFilter = rb.Content.ToString();
                _vendorView?.Refresh();
                UpdateSummary();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _vendorView?.Refresh();
            UpdateSummary();
        }

        private bool VendorFilter(object obj)
        {
            if (obj is VendorModel vendor)
            {
                bool catMatch = _currentFilter == "전체" || string.Equals(vendor.Category, _currentFilter, StringComparison.OrdinalIgnoreCase);
                string keyword = SearchBox?.Text?.Trim() ?? "";
                bool searchMatch = string.IsNullOrEmpty(keyword) || vendor.VendorName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                return catMatch && searchMatch;
            }
            return false;
        }

        private void VendorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VendorListBox.SelectedItem is VendorModel selected)
            {
                DetailRootPanel.DataContext = selected;
                DetailGrid.ItemsSource = selected.Addresses;
                ManagersDataGrid.ItemsSource = selected.Managers;
                TemplatesDataGrid.ItemsSource = selected.Templates;
            }
        }

        // --- 업체 추가/삭제 ---
        private void AddVendor_Click(object sender, RoutedEventArgs e)
        {
            var newVendor = new VendorModel { VendorName = "새 거래처" };
            Vendors.Add(newVendor);
            _vendorView.Refresh();
            VendorListBox.SelectedItem = newVendor;
            UpdateSummary();
        }

        private void DeleteVendor_Click(object sender, RoutedEventArgs e)
        {
            if (VendorListBox.SelectedItem is VendorModel selected)
            {
                if (MessageBox.Show($"'{selected.VendorName}' 업체를 정말 삭제하시겠습니까?", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    Vendors.Remove(selected);
                    _vendorView.Refresh();
                    UpdateSummary();
                }
            }
        }

        // --- 주소, 담당자, 템플릿 행 제어 ---
        private void AddAddress_Click(object sender, RoutedEventArgs e) => (VendorListBox.SelectedItem as VendorModel)?.Addresses.Add(new AddressModel { LocationName = "신규" });
        private void DeleteAddress_Click(object sender, RoutedEventArgs e) { if (DetailGrid.SelectedItem is AddressModel addr) (VendorListBox.SelectedItem as VendorModel)?.Addresses.Remove(addr); }
        private void AddManager_Click(object sender, RoutedEventArgs e) => (VendorListBox.SelectedItem as VendorModel)?.Managers.Add(new ManagerModel { ManagerName = "이름" });
        private void DeleteManager_Click(object sender, RoutedEventArgs e) { if (ManagersDataGrid.SelectedItem is ManagerModel mgr) (VendorListBox.SelectedItem as VendorModel)?.Managers.Remove(mgr); }

        private void AddTemplate_Click(object sender, RoutedEventArgs e) => (VendorListBox.SelectedItem as VendorModel)?.Templates.Add(new VendorTemplateModel { ItemCode = "도면명" });
        private void DeleteTemplate_Click(object sender, RoutedEventArgs e) { if (TemplatesDataGrid.SelectedItem is VendorTemplateModel tpl) (VendorListBox.SelectedItem as VendorModel)?.Templates.Remove(tpl); }

        // --- 파일/폴더 찾기 ---
        private void BrowseTemplate_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is VendorTemplateModel tpl)
            {
                var dialog = new OpenFileDialog { Filter = "Excel 파일|*.xltx;*.xlsx" };
                if (dialog.ShowDialog() == true) tpl.TemplatePath = dialog.FileName;
            }
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is VendorTemplateModel tpl)
            {
                var dialog = new OpenFolderDialog();
                if (dialog.ShowDialog() == true) tpl.BasePath = dialog.FolderName;
            }
        }

        // --- 설정 팝업 제어 ---
        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(ConfigFilePath)) TxtMasterDbPath.Text = File.ReadAllText(ConfigFilePath);
            SettingsOverlay.Visibility = Visibility.Visible;
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            Keyboard.Focus(this); // 🔥 팝업이 닫힐 때 포커스가 증발해서 씹히는 현상 방지
        }

        private void BtnChangeMasterDb_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Excel 파일|*.xlsx;*.xlsm" };
            if (dialog.ShowDialog() == true)
            {
                TxtMasterDbPath.Text = dialog.FileName;
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
                File.WriteAllText(ConfigFilePath, dialog.FileName);
            }
        }

        // --- 엑셀 마이그레이션 (기존 데이터 일괄 이관) ---
        private void BtnMigrateExcel_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "기준정보 엑셀 선택", Filter = "Excel 파일|*.xlsx;*.xlsm" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook(dialog.FileName);
                var ws = wb.Worksheets.FirstOrDefault(x => x.Name == "기준정보");
                if (ws == null) { MessageBox.Show("'기준정보' 시트를 찾을 수 없습니다."); return; }

                int count = 0;
                foreach (var row in ws.RowsUsed().Skip(1))
                {
                    string use = row.Cell("A").GetString();
                    string com = row.Cell("B").GetString();
                    string code = row.Cell("E").GetString();
                    string tpl = row.Cell("F").GetString();
                    string dir = row.Cell("G").GetString();
                    string rule = row.Cell("I").GetString();

                    if (use != "Y" || string.IsNullOrEmpty(com)) continue;

                    var v = Vendors.FirstOrDefault(x => x.VendorName == com);
                    if (v == null) { v = new VendorModel { VendorName = com }; Vendors.Add(v); }

                    if (!v.Templates.Any(x => x.ItemCode == code))
                    {
                        v.Templates.Add(new VendorTemplateModel { ItemCode = code, TemplatePath = tpl, BasePath = dir, FileNameRule = rule });
                        count++;
                    }
                }
                VendorStore.Save(Vendors);
                UpdateSummary();
                MessageBox.Show($"{count}개의 기준정보를 성공적으로 가져왔습니다!");
            }
            catch (Exception ex) { MessageBox.Show("이관 오류: " + ex.Message); }
        }

        // --- 최종 저장 및 요약 ---
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Keyboard.Focus(this); // 🔥 저장 시에도 확실하게 포커스 정리
                VendorStore.Save(Vendors);
                UpdateSummary();
                MessageBox.Show("안전하게 저장되었습니다.");
            }
            catch (Exception ex) { MessageBox.Show("저장 오류: " + ex.Message); }
        }

        private void UpdateSummary()
        {
            if (_vendorView == null || Vendors == null) return;
            TxtSummary.Text = $"전체 {Vendors.Count}개 · 주소 {Vendors.Sum(v => v.Addresses.Count)}개 · 담당자 {Vendors.Sum(v => v.Managers.Count)}명 · 템플릿 {Vendors.Sum(v => v.Templates.Count)}건";
        }
    }
}