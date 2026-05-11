using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CleanPotal
{
    public partial class UserManagementWindow : Window
    {
        private ObservableCollection<UserModel> _users;
        private ICollectionView _view;

        public UserManagementWindow()
        {
            InitializeComponent();
            LoadUsers();
        }

        private void LoadUsers()
        {
            var userList = AuthDatabaseHelper.GetAllUsers();
            _users = new ObservableCollection<UserModel>(userList);
            _view = CollectionViewSource.GetDefaultView(_users);
            _view.Filter = FilterUser;
            UsersGrid.ItemsSource = _view;

            var teams = new[] { "전체 팀" }.Concat(_users.Select(u => u.TeamName).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t)).ToList();
            CmbTeamFilter.ItemsSource = teams;
            CmbTeamFilter.SelectedIndex = 0;
        }

        private bool FilterUser(object obj)
        {
            if (obj is not UserModel u) return false;
            string keyword = TxtSearch?.Text?.Trim() ?? "";
            string team = CmbTeamFilter?.SelectedItem as string ?? "전체 팀";

            bool nameMatch = string.IsNullOrEmpty(keyword)
                || u.RealName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || u.Username.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            bool teamMatch = team == "전체 팀" || u.TeamName == team;

            return nameMatch && teamMatch;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

        private void CmbTeamFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => _view?.Refresh();

        private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UsersGrid.SelectedItem is UserModel selectedUser)
            {
                TxtNewId.Text = selectedUser.Username;
                TxtNewPw.Text = selectedUser.Password;
                TxtNewName.Text = selectedUser.RealName;
                TxtNewTeam.Text = selectedUser.TeamName;
                TxtNewTitle.Text = selectedUser.JobTitle;
                TxtNewEmail.Text = selectedUser.Email;
                TxtNewPhone.Text = selectedUser.PhoneNumber;

                // 권한 체크박스 반영
                ChkManageFiles.IsChecked = selectedUser.CanManageFiles;
                ChkManageNotices.IsChecked = selectedUser.CanManageNotices;
                ChkManageVendors.IsChecked = selectedUser.CanManageVendors;
                ChkManageSchedule.IsChecked = selectedUser.CanManageSchedule;
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearInputFields();
            UsersGrid.SelectedItem = null;
        }

        private void ClearInputFields()
        {
            TxtNewId.Clear();
            TxtNewPw.Clear();
            TxtNewName.Clear();
            TxtNewTeam.Clear();
            TxtNewTitle.Clear();
            TxtNewEmail.Clear();
            TxtNewPhone.Clear();

            ChkManageFiles.IsChecked = false;
            ChkManageNotices.IsChecked = false;
            ChkManageVendors.IsChecked = false;
            ChkManageSchedule.IsChecked = false;
        }

        // 🔥 엑셀 붙여넣기 기능 완전 삭제됨

        private void BtnAddUser_Click(object sender, RoutedEventArgs e)
        {
            string id = TxtNewId.Text.Trim();
            string pw = TxtNewPw.Text.Trim();
            string name = TxtNewName.Text.Trim();

            if (id.Length < 4)
            {
                MessageBox.Show("아이디는 영문/숫자 상관없이 최소 4글자 이상이어야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pw) || string.IsNullOrEmpty(name))
            {
                MessageBox.Show("사번, 비밀번호, 이름은 필수 입력 항목입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_users.Any(u => u.Username == id))
            {
                MessageBox.Show("이미 존재하는 사번입니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var newUser = new UserModel
            {
                Username = id,
                Password = pw,
                RealName = name,
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

            ClearInputFields();
            UsersGrid.SelectedItem = null;

            MessageBox.Show("새 사용자가 추가되었습니다.", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnEditUser_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is UserModel selectedUser)
            {
                string id = TxtNewId.Text.Trim();
                string pw = TxtNewPw.Text.Trim();
                string name = TxtNewName.Text.Trim();

                if (id.Length < 4)
                {
                    MessageBox.Show("아이디는 영문/숫자 상관없이 최소 4글자 이상이어야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pw) || string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("사번, 비밀번호, 이름은 필수 입력 항목입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (id != selectedUser.Username && _users.Any(u => u.Username == id))
                {
                    MessageBox.Show("이미 사용 중인 사번입니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 1004 최고 관리자 아이디 변경 방지
                if (selectedUser.Username == "1004" && id != "1004")
                {
                    MessageBox.Show("최고 관리자(1004)의 아이디는 변경할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Stop);
                    return;
                }

                selectedUser.Username = id;
                selectedUser.Password = pw;
                selectedUser.RealName = name;
                selectedUser.TeamName = TxtNewTeam.Text.Trim();
                selectedUser.JobTitle = TxtNewTitle.Text.Trim();
                selectedUser.Email = TxtNewEmail.Text.Trim();
                selectedUser.PhoneNumber = TxtNewPhone.Text.Trim();

                selectedUser.CanManageFiles = ChkManageFiles.IsChecked == true;
                selectedUser.CanManageNotices = ChkManageNotices.IsChecked == true;
                selectedUser.CanManageVendors = ChkManageVendors.IsChecked == true;
                selectedUser.CanManageSchedule = ChkManageSchedule.IsChecked == true;

                AuthDatabaseHelper.SaveAllUsers(_users.ToList());
                UsersGrid.Items.Refresh();

                MessageBox.Show("사용자 정보 및 권한이 수정되었습니다.", "성공", MessageBoxButton.OK, MessageBoxImage.Information);

                ClearInputFields();
                UsersGrid.SelectedItem = null;
            }
            else
            {
                MessageBox.Show("수정할 사용자를 표에서 먼저 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnDeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is UserModel selectedUser)
            {
                // 1004 최고 관리자 삭제 방지
                if (selectedUser.Username == "1004")
                {
                    MessageBox.Show("최고 관리자(1004) 계정은 삭제할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Stop);
                    return;
                }
                var result = MessageBox.Show($"'{selectedUser.RealName}' 사용자를 정말 삭제하시겠습니까?", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _users.Remove(selectedUser);
                    AuthDatabaseHelper.SaveAllUsers(_users.ToList());
                    ClearInputFields();
                }
            }
        }
    }
}