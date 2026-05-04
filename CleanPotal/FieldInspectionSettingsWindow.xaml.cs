using System.Windows;
using CleanPotal.FieldInspection.Services;

namespace CleanPotal
{
    public partial class FieldInspectionSettingsWindow : Window
    {
        public FieldInspectionSettingsWindow()
        {
            InitializeComponent();

            var s = FieldInspectionSettings.Current;
            TxtBaseUrl.Text = s.BaseUrl ?? "";

            string secret = s.ServerSecret ?? "";
            string masked = secret.Length > 8
                ? secret.Substring(0, 4) + new string('•', 8) + secret.Substring(secret.Length - 4)
                : "(미설정)";
            TokenInfoText.Text = $"서버 비밀키(자동 생성): {masked}\n비밀키가 바뀌면 모든 NFC/QR 토큰을 재발급해야 합니다.";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtBaseUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("서버 주소를 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtBaseUrl.Focus();
                return;
            }

            var s = FieldInspectionSettings.Current;
            s.BaseUrl = url;
            FieldInspectionSettings.Save(s);

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
