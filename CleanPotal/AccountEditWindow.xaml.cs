using System.Windows;

namespace CleanPotal
{
    public partial class AccountEditWindow : Window
    {
        public AccountEditWindow()
        {
            InitializeComponent();
            TxtEditId.Text = SessionManager.CurrentUsername;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string newId = TxtEditId.Text.Trim();
            string curPw = TxtCurrentPw.Password;
            string newPw = TxtNewPw.Password;
            string cfPw = TxtNewPwConfirm.Password;

            // 🔥 아이디 최소 4글자 이상 제한 로직 추가
            if (newId.Length < 4)
            {
                MessageBox.Show("아이디는 영문/숫자 상관없이 최소 4글자 이상이어야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var user = AuthDatabaseHelper.ValidateUserObject(SessionManager.CurrentUsername, curPw);
            if (user == null)
            {
                MessageBox.Show("현재 비밀번호가 일치하지 않습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!string.IsNullOrEmpty(newPw) && newPw != cfPw)
            {
                MessageBox.Show("새 비밀번호와 확인 입력이 일치하지 않습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string finalPw = string.IsNullOrEmpty(newPw) ? curPw : newPw;

            var allUsers = AuthDatabaseHelper.GetAllUsers();
            var target = allUsers.Find(u => u.Username == SessionManager.CurrentUsername);
            if (target != null)
            {
                target.Username = newId;
                target.Password = finalPw;
                AuthDatabaseHelper.SaveAllUsers(allUsers);

                SessionManager.CurrentUsername = newId;
                SessionManager.SaveAutoLogin(newId, finalPw);

                MessageBox.Show("계정 정보가 성공적으로 변경되었습니다.", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("사용자 정보를 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}