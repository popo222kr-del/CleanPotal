using System.Windows;
using System.Windows.Input;

namespace CleanPotal
{
    public partial class PasswordDialog : Window
    {
        public string InputPassword { get; private set; } = "";

        public PasswordDialog()
        {
            InitializeComponent();
            PwBox.Focus(); // 창이 뜨자마자 바로 입력할 수 있게 커서 포커스
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            InputPassword = PwBox.Password;
            DialogResult = true; // 확인 버튼을 누르면 true 반환
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; // 취소
        }

        private void PwBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirm_Click(sender, e);
            }
        }
    }
}