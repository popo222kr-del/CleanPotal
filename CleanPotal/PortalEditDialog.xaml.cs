using System.Windows;
using Microsoft.Win32; // 파일 대화상자용

namespace CleanPotal
{
    public partial class PortalEditDialog : Window
    {
        public string EditTitle { get; private set; } = "";
        public string EditPath { get; private set; } = "";

        public PortalEditDialog(string currentTitle, string currentPath)
        {
            InitializeComponent();
            TitleBox.Text = currentTitle;
            PathBox.Text = currentPath;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            // 찾아보기 버튼 누르면 파일 선택 창 띄우기
            OpenFileDialog dlg = new OpenFileDialog { Title = "실행할 파일 선택", Filter = "모든 파일 (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
            {
                PathBox.Text = dlg.FileName;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            EditTitle = TitleBox.Text.Trim();
            EditPath = PathBox.Text.Trim();
            DialogResult = true; // 저장 완료
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}