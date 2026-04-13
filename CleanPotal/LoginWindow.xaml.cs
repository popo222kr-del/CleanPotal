using System.Windows;
using System.Windows.Input;

namespace CleanPotal
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            TxtUsername.Focus();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            string password = TxtPassword.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("아이디와 패스워드를 모두 입력해주세요.", "로그인 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var user = AuthDatabaseHelper.ValidateUserObject(username, password);
            if (user != null)
            {
                SessionManager.CurrentUsername = user.Username;
                SessionManager.CurrentRealName = user.RealName;
                SessionManager.CurrentTeamName = user.TeamName;

                SessionManager.CanManageFiles = user.CanManageFiles;
                SessionManager.CanManageNotices = user.CanManageNotices;
                SessionManager.CanManageVendors = user.CanManageVendors;
                SessionManager.CanManageSchedule = user.CanManageSchedule; // 🔥 권한 저장

                MainWindow mainWin = new MainWindow();
                mainWin.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("아이디 또는 패스워드가 일치하지 않습니다.", "로그인 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtPassword.Clear();
                TxtPassword.Focus();
            }
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnLogin_Click(sender, e);
        }

        private void BtnUserManager_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            string password = TxtPassword.Password.Trim();

            var user = AuthDatabaseHelper.ValidateUserObject(username, password);

            // 🔥 admin 대신 1004 (박주언) 계정인지 확인!
            if (user != null && user.Username == "1004")
            {
                var userWin = new UserManagementWindow { Owner = this };
                userWin.ShowDialog();
            }
            else
            {
                MessageBox.Show("사용자 관리는 '최고 관리자(1004)' 전용 메뉴입니다.\n아이디와 비밀번호를 올바르게 입력 후 클릭하세요.", "권한 필요", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}