using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    public partial class VendorManagerWindow : Window
    {
        // 🔥 프로젝트 공용 모델을 사용한다고 가정 (임의로 옮기지 않음)
        public ObservableCollection<VendorModel> Vendors { get; set; } = new ObservableCollection<VendorModel>();

        public VendorManagerWindow()
        {
            InitializeComponent();
            LoadVendors();
        }

        private void LoadVendors()
        {
            VendorListBox.ItemsSource = Vendors;
        }

        private void VendorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VendorListBox.SelectedItem is VendorModel selected)
            {
                // 🔥 복구된 컨트롤 명칭 사용
                TxtVendorName.Text = selected.VendorName;
                DetailGrid.ItemsSource = selected.Addresses;
                ManagersDataGrid.ItemsSource = selected.Managers;
            }
        }

        private void AddVendor_Click(object sender, RoutedEventArgs e)
        {
            var newVendor = new VendorModel { VendorName = "새 업체" };
            Vendors.Add(newVendor);
            VendorListBox.SelectedItem = newVendor;
        }

        private void DeleteVendor_Click(object sender, RoutedEventArgs e)
        {
            if (VendorListBox.SelectedItem is VendorModel selected)
            {
                if (MessageBox.Show($"{selected.VendorName} 업체를 삭제하시겠습니까?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    Vendors.Remove(selected);
            }
        }

        private void SaveVendor_Click(object sender, RoutedEventArgs e)
        {
            if (VendorListBox.SelectedItem is VendorModel selected)
            {
                selected.VendorName = TxtVendorName.Text;
                MessageBox.Show("저장되었습니다.");
            }
        }
    }
}