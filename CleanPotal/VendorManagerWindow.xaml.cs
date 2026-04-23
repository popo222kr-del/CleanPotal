using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace CleanPotal
{
    public partial class VendorManagerWindow : Window
    {
        public ObservableCollection<VendorModel> Vendors { get; set; } = new();
        public ObservableCollection<GlobalTemplateModel> GlobalTemplates { get; set; } = new();
        private ICollectionView _vendorView;
        private string _currentFilter = "전체";
        private string ConfigFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "master_db_config.txt");

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int WNetGetConnection([MarshalAs(UnmanagedType.LPTStr)] string localName, [MarshalAs(UnmanagedType.LPTStr)] StringBuilder remoteName, ref int length);

        public VendorManagerWindow()
        {
            InitializeComponent();
            Vendors = VendorStore.Load();
            GlobalTemplates = VendorStore.LoadGlobalTemplates();
            _vendorView = CollectionViewSource.GetDefaultView(Vendors);
            if (_vendorView != null) _vendorView.Filter = VendorFilter;
            VendorListBox.ItemsSource = _vendorView;
            GlobalTemplatesGrid.ItemsSource = GlobalTemplates;
            UpdateSummary();
            if (Vendors.Count > 0) VendorListBox.SelectedIndex = 0;
        }

        // 🔥 윈도우가 던지는 Z:\ 등 맵핑 드라이브를 강제로 절대주소(UNC)로 변환하는 방어 로직
        private string GetUNCPath(string originalPath)
        {
            if (string.IsNullOrWhiteSpace(originalPath)) return originalPath ?? "";
            if (originalPath.StartsWith(@"\\")) return originalPath; // 이미 절대주소면 패스

            if (originalPath.Length >= 2 && originalPath[1] == ':' && char.IsLetter(originalPath[0]))
            {
                string driveLetter = originalPath.Substring(0, 2);
                var sb = new StringBuilder(512);
                int size = sb.Capacity;
                if (WNetGetConnection(driveLetter, sb, ref size) == 0)
                {
                    return sb.ToString().TrimEnd('\\') + originalPath.Substring(2);
                }
            }
            return originalPath;
        }

        private void BtnChangeMasterDb_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Excel 파일|*.xlsx;*.xlsm" };
            if (dialog.ShowDialog() == true)
            {
                // 🔥 변환된 절대주소를 변수에 담아 화면과 텍스트 파일에 동시 강제 덮어쓰기
                string uncPath = GetUNCPath(dialog.FileName ?? "");
                TxtMasterDbPath.Text = uncPath;
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath)!);
                File.WriteAllText(ConfigFilePath, uncPath);
            }
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(ConfigFilePath)) TxtMasterDbPath.Text = File.ReadAllText(ConfigFilePath).Trim();
            SettingsOverlay.Visibility = Visibility.Visible;
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            try { VendorStore.SaveGlobalTemplates(GlobalTemplates); SettingsOverlay.Visibility = Visibility.Collapsed; Keyboard.Focus(this); }
            catch (Exception ex) { MessageBox.Show("템플릿 저장 오류: " + ex.Message); }
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e) { base.OnPreviewMouseDown(e); if (e.OriginalSource is DependencyObject depObj && !(depObj is System.Windows.Controls.Primitives.TextBoxBase) && !(depObj is System.Windows.Controls.Primitives.Thumb)) Keyboard.Focus(this); }
        private void FilterTab_Checked(object sender, RoutedEventArgs e) { if (sender is RadioButton rb && rb.Content != null) { _currentFilter = rb.Content.ToString() ?? "전체"; _vendorView?.Refresh(); UpdateSummary(); } }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { _vendorView?.Refresh(); UpdateSummary(); }
        private bool VendorFilter(object obj) { if (obj is VendorModel v) { bool catMatch = _currentFilter == "전체" || string.Equals(v.Category, _currentFilter, StringComparison.OrdinalIgnoreCase); string kw = SearchBox?.Text?.Trim() ?? ""; bool sMatch = string.IsNullOrEmpty(kw) || (v.VendorName?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true); return catMatch && sMatch; } return false; }
        private void VendorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (VendorListBox.SelectedItem is VendorModel s) { DetailRootPanel.DataContext = s; DetailGrid.ItemsSource = s.Addresses; ManagersDataGrid.ItemsSource = s.Managers; } }
        private void AddVendor_Click(object sender, RoutedEventArgs e) { var v = new VendorModel { VendorName = "새 거래처" }; Vendors.Add(v); _vendorView?.Refresh(); VendorListBox.SelectedItem = v; UpdateSummary(); }
        private void DeleteVendor_Click(object sender, RoutedEventArgs e) { if (VendorListBox.SelectedItem is VendorModel s && MessageBox.Show($"'{s.VendorName}' 삭제하시겠습니까?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { Vendors.Remove(s); _vendorView?.Refresh(); UpdateSummary(); } }
        private void AddAddress_Click(object sender, RoutedEventArgs e) => (VendorListBox.SelectedItem as VendorModel)?.Addresses.Add(new AddressModel { LocationName = "신규" });
        private void DeleteAddress_Click(object sender, RoutedEventArgs e) { if (DetailGrid.SelectedItem is AddressModel a) (VendorListBox.SelectedItem as VendorModel)?.Addresses.Remove(a); }
        private void AddManager_Click(object sender, RoutedEventArgs e) => (VendorListBox.SelectedItem as VendorModel)?.Managers.Add(new ManagerModel { ManagerName = "이름" });
        private void DeleteManager_Click(object sender, RoutedEventArgs e) { if (ManagersDataGrid.SelectedItem is ManagerModel m) (VendorListBox.SelectedItem as VendorModel)?.Managers.Remove(m); }
        private void BrowseFolder_Click(object sender, RoutedEventArgs e) { if (VendorListBox.SelectedItem is VendorModel v) { var d = new Microsoft.Win32.OpenFolderDialog(); if (d.ShowDialog() == true) v.BasePath = GetUNCPath(d.FolderName ?? ""); } }

        // 템플릿 경로 설정 시에도 절대주소 변환 로직 탑재 유지
        private void BrowseGlobalTemplate_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is GlobalTemplateModel t) { var d = new OpenFileDialog { Filter = "Excel 서식 파일|*.xltx;*.xlsx" }; if (d.ShowDialog() == true) t.TemplatePath = GetUNCPath(d.FileName ?? ""); } }
        private void BtnSave_Click(object sender, RoutedEventArgs e) { try { Keyboard.Focus(this); VendorStore.Save(Vendors); UpdateSummary(); MessageBox.Show("저장되었습니다."); } catch (Exception ex) { MessageBox.Show("오류: " + ex.Message); } }
        private void UpdateSummary() { if (_vendorView != null && Vendors != null) TxtSummary.Text = $"전체 {Vendors.Count}개 · 주소 {Vendors.Sum(v => v.Addresses?.Count ?? 0)}개 · 담당자 {Vendors.Sum(v => v.Managers?.Count ?? 0)}명"; }
    }
}