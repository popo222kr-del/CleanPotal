using System.Windows;
using CleanPotal.FieldInspection.Models;

namespace CleanPotal
{
    public partial class FieldLocationEditWindow : Window
    {
        public FieldLocation Result { get; private set; }
        private readonly bool _isEdit;

        public FieldLocationEditWindow(FieldLocation? source = null)
        {
            InitializeComponent();
            _isEdit = source != null;
            Result = source != null
                ? new FieldLocation
                {
                    LocationId = source.LocationId,
                    Code = source.Code,
                    Name = source.Name,
                    Zone = source.Zone,
                    Equipment = source.Equipment,
                    Memo = source.Memo,
                    IsActive = source.IsActive,
                    CreatedAt = source.CreatedAt
                }
                : new FieldLocation();

            HeaderText.Text = _isEdit ? "위치 수정" : "새 위치 등록";
            TxtCode.Text = Result.Code;
            TxtName.Text = Result.Name;
            TxtZone.Text = Result.Zone;
            TxtEquipment.Text = Result.Equipment;
            TxtMemo.Text = Result.Memo;
            ChkActive.IsChecked = Result.IsActive;

            if (_isEdit) TxtCode.IsEnabled = false;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string code = TxtCode.Text.Trim();
            string name = TxtName.Text.Trim();

            if (string.IsNullOrEmpty(code))
            {
                MessageBox.Show("위치 코드를 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCode.Focus();
                return;
            }

            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("위치명을 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtName.Focus();
                return;
            }

            Result.Code = code;
            Result.Name = name;
            Result.Zone = TxtZone.Text.Trim();
            Result.Equipment = TxtEquipment.Text.Trim();
            Result.Memo = TxtMemo.Text;
            Result.IsActive = ChkActive.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
