using System.Windows;
using System.Windows.Input;

namespace CleanPotal
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 저장된 자동 로그인 정보가 있으면 체크박스를 켜고 자동 로그인 진행
            var (savedId, savedPw) = SessionManager.LoadAutoLogin();
            if (!string.IsNullOrEmpty(savedId) && !string.IsNullOrEmpty(savedPw))
            {
                ChkAutoLogin.IsChecked = true;
                await System.Threading.Tasks.Task.Delay(500);
                DoLogin(savedId, savedPw, true);
            }
            else
            {
                TxtUsername.Focus();
            }
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            DoLogin(TxtUsername.Text.Trim(), TxtPassword.Password.Trim(), false);
        }

        private void DoLogin(string id, string pw, bool isAuto)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pw))
            {
                if (!isAuto) MessageBox.Show("아이디와 패스워드를 모두 입력해주세요.", "로그인 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var user = AuthDatabaseHelper.ValidateUserObject(id, pw);
            if (user != null)
            {
                SessionManager.CurrentUsername = user.Username;
                SessionManager.CurrentRealName = user.RealName;
                SessionManager.CurrentTeamName = user.TeamName;
                SessionManager.CanManageFiles = user.CanManageFiles;
                SessionManager.CanManageNotices = user.CanManageNotices;
                SessionManager.CanManageVendors = user.CanManageVendors;
                SessionManager.CanManageSchedule = user.CanManageSchedule;

                // 자동 로그인 체크 시 저장, 해제 시 기존 저장 정보 삭제
                if (ChkAutoLogin.IsChecked == true) SessionManager.SaveAutoLogin(id, pw);
                else SessionManager.Logout();

                MainWindow mainWin = new MainWindow();
                mainWin.Show();
                this.Close();
            }
            else
            {
                if (isAuto)
                {
                    SessionManager.Logout(); // 만약 비번이 그사이 바뀌어서 실패했다면 저장된 티켓 파기
                }
                else
                {
                    MessageBox.Show("아이디 또는 패스워드가 일치하지 않습니다.", "로그인 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                    TxtPassword.Clear();
                    TxtPassword.Focus();
                }
            }
        }

        private void Input_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) BtnLogin_Click(sender, e); }

        private void BtnUserManager_Click(object sender, RoutedEventArgs e)
        {
            var user = AuthDatabaseHelper.ValidateUserObject(TxtUsername.Text.Trim(), TxtPassword.Password.Trim());
            if (user != null && user.Username == "1004") { new UserManagementWindow { Owner = this }.ShowDialog(); }
            else { MessageBox.Show("사용자 관리는 '최고 관리자(1004)' 전용 메뉴입니다.\n아이디와 비밀번호를 올바르게 입력 후 클릭하세요.", "권한 필요", MessageBoxButton.OK, MessageBoxImage.Information); }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}