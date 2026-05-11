using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    public partial class UserManagementWindow : Window
    {
        private ObservableCollection<UserModel> _users = new();
        private UserModel? _selectedUser = null;
        private bool _isAddMode = false;

        public UserManagementWindow()
        {
            InitializeComponent();
            LoadUsers();
        }

        private void LoadUsers()
        {
            var list = AuthDatabaseHelper.GetAllUsers();
            _users = new ObservableCollection<UserModel>(list);
            UserListBox.ItemsSource = _users;
            UserCountText.Text = $"{_users.Count}명";
        }

        // ── 목록 선택 ──────────────────────────────────────────────────
        private void UserListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UserListBox.SelectedItem is UserModel user)
            {
                _selectedUser = user;
                _isAddMode = false;
                ShowDetailPanel(user);
                LoadEduHistory(user.RealName);
            }
        }

        // ── 신규 추가 버튼 ─────────────────────────────────────────────
        private void BtnNewUser_Click(object sender, RoutedEventArgs e)
        {
            _isAddMode = true;
            _selectedUser = null;
            UserListBox.SelectedItem = null;

            // 빈 폼 표시
            ClearInputFields();
            DetailName.Text = "신규 사용자";
            DetailTeam.Text = "";
            DetailTeamBadge.Visibility = Visibility.Collapsed;
            NewModeBadge.Visibility = Visibility.Visible;
            BtnDeleteUser.Visibility = Visibility.Collapsed;

            // 교육 이력 탭 — 신규 모드 안내
            EduSubtitle.Text = "사용자를 저장한 후 교육 이력을 확인할 수 있습니다.";
            EduHistoryGrid.ItemsSource = null;
            EduHistoryGrid.Visibility = Visibility.Collapsed;
            EduEmptyPlaceholder.Visibility = Visibility.Visible;
            EduEmptyText.Text = "사용자를 저장한 후 교육 이력을 확인할 수 있습니다.";

            EmptyState.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            MainTabControl.SelectedIndex = 0;
            TxtNewId.Focus();
        }

        // ── 상세 패널 표시 ─────────────────────────────────────────────
        private void ShowDetailPanel(UserModel user)
        {
            DetailName.Text = user.RealName;
            DetailTeam.Text = user.TeamName;
            DetailTeamBadge.Visibility = string.IsNullOrEmpty(user.TeamName)
                ? Visibility.Collapsed : Visibility.Visible;
            NewModeBadge.Visibility = Visibility.Collapsed;
            BtnDeleteUser.Visibility = user.Username == "1004"
                ? Visibility.Collapsed : Visibility.Visible;

            TxtNewId.Text = user.Username;
            TxtNewPw.Text = user.Password;
            TxtNewName.Text = user.RealName;
            TxtNewTitle.Text = user.JobTitle;
            TxtNewTeam.Text = user.TeamName;
            TxtNewEmail.Text = user.Email;
            TxtNewPhone.Text = user.PhoneNumber;

            ChkManageFiles.IsChecked = user.CanManageFiles;
            ChkManageNotices.IsChecked = user.CanManageNotices;
            ChkManageVendors.IsChecked = user.CanManageVendors;
            ChkManageSchedule.IsChecked = user.CanManageSchedule;

            EmptyState.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
        }

        // ── 교육 이력 로드 ─────────────────────────────────────────────
        private void LoadEduHistory(string memberName)
        {
            var history = DatabaseHelper.GetEducationPlansByMember(memberName);
            EduHistoryGrid.ItemsSource = history;

            bool hasData = history.Count > 0;
            EduSubtitle.Text = hasData
                ? $"총 {history.Count}건의 교육 이력이 등록되어 있습니다."
                : "등록된 교육 이력이 없습니다.";
            EduHistoryGrid.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
            EduEmptyText.Text = "등록된 교육 이력이 없습니다.";
            EduEmptyPlaceholder.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        }

        // ── 저장 ───────────────────────────────────────────────────────
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string id = TxtNewId.Text.Trim();
            string pw = TxtNewPw.Text.Trim();
            string name = TxtNewName.Text.Trim();

            if (id.Length < 4)
            {
                MessageBox.Show("아이디는 최소 4글자 이상이어야 합니다.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(pw) || string.IsNullOrEmpty(name))
            {
                MessageBox.Show("사번, 비밀번호, 이름은 필수 입력 항목입니다.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isAddMode)
            {
                // ── 신규 추가 ──
                if (_users.Any(u => u.Username == id))
                {
                    MessageBox.Show("이미 존재하는 사번입니다.", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var newUser = new UserModel
                {
                    Username = id, Password = pw, RealName = name,
                    TeamName = TxtNewTeam.Text.Trim(),
                    JobTitle = TxtNewTitle.Text.Trim(),
                    Email = TxtNewEmail.Text.Trim(),
                    PhoneNumber = TxtNewPhone.Text.Trim(),
                    CanManageFiles = ChkManageFiles.IsChecked == true,
                    CanManageNotices = ChkManageNotices.IsChecked == true,
                    CanManageVendors = ChkManageVendors.IsChecked == true,
                    CanManageSchedule = ChkManageSchedule.IsChecked == true
                };

                _users.Add(newUser);
                AuthDatabaseHelper.SaveAllUsers(_users.ToList());
                UserCountText.Text = $"{_users.Count}명";

                MessageBox.Show("새 사용자가 추가되었습니다.", "성공",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // 추가 완료 후 해당 사용자 선택 상태로 전환
                _isAddMode = false;
                _selectedUser = newUser;
                UserListBox.SelectedItem = newUser;
            }
            else
            {
                // ── 정보 수정 ──
                if (_selectedUser == null) return;

                if (id != _selectedUser.Username && _users.Any(u => u.Username == id))
                {
                    MessageBox.Show("이미 사용 중인 사번입니다.", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (_selectedUser.Username == "1004" && id != "1004")
                {
                    MessageBox.Show("최고 관리자(1004)의 아이디는 변경할 수 없습니다.", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Stop);
                    return;
                }

                _selectedUser.Username = id;
                _selectedUser.Password = pw;
                _selectedUser.RealName = name;
                _selectedUser.TeamName = TxtNewTeam.Text.Trim();
                _selectedUser.JobTitle = TxtNewTitle.Text.Trim();
                _selectedUser.Email = TxtNewEmail.Text.Trim();
                _selectedUser.PhoneNumber = TxtNewPhone.Text.Trim();
                _selectedUser.CanManageFiles = ChkManageFiles.IsChecked == true;
                _selectedUser.CanManageNotices = ChkManageNotices.IsChecked == true;
                _selectedUser.CanManageVendors = ChkManageVendors.IsChecked == true;
                _selectedUser.CanManageSchedule = ChkManageSchedule.IsChecked == true;

                AuthDatabaseHelper.SaveAllUsers(_users.ToList());
                UserListBox.Items.Refresh();

                // 헤더 갱신
                DetailName.Text = _selectedUser.RealName;
                DetailTeam.Text = _selectedUser.TeamName;
                DetailTeamBadge.Visibility = string.IsNullOrEmpty(_selectedUser.TeamName)
                    ? Visibility.Collapsed : Visibility.Visible;

                MessageBox.Show("사용자 정보가 수정되었습니다.", "성공",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ── 취소 ───────────────────────────────────────────────────────
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _isAddMode = false;
            _selectedUser = null;
            UserListBox.SelectedItem = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }

        // ── 삭제 ───────────────────────────────────────────────────────
        private void BtnDeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null) return;
            if (_selectedUser.Username == "1004")
            {
                MessageBox.Show("최고 관리자(1004) 계정은 삭제할 수 없습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (MessageBox.Show($"'{_selectedUser.RealName}' 사용자를 정말 삭제하시겠습니까?",
                "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _users.Remove(_selectedUser);
                AuthDatabaseHelper.SaveAllUsers(_users.ToList());
                UserCountText.Text = $"{_users.Count}명";

                _selectedUser = null;
                UserListBox.SelectedItem = null;
                DetailPanel.Visibility = Visibility.Collapsed;
                EmptyState.Visibility = Visibility.Visible;
            }
        }

        // ── 입력 초기화 ────────────────────────────────────────────────
        private void ClearInputFields()
        {
            TxtNewId.Clear(); TxtNewPw.Clear(); TxtNewName.Clear();
            TxtNewTitle.Clear(); TxtNewTeam.Clear(); TxtNewEmail.Clear(); TxtNewPhone.Clear();
            ChkManageFiles.IsChecked = false; ChkManageNotices.IsChecked = false;
            ChkManageVendors.IsChecked = false; ChkManageSchedule.IsChecked = false;
        }
    }
}
