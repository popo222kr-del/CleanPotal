using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;

namespace CleanPotal
{
    public partial class UserManagementWindow : Window
    {
        private List<UserModel> _allUsers = new();
        private ObservableCollection<UserModel> _users = new();
        private UserModel? _selectedUser = null;
        private bool _isAddMode = false;
        private bool _sortByName = true;

        public UserManagementWindow()
        {
            InitializeComponent();
            LoadUsers();
        }

        private void LoadUsers()
        {
            _allUsers = AuthDatabaseHelper.GetAllUsers();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string keyword = TxtSearch?.Text?.Trim() ?? "";
            var filtered = string.IsNullOrEmpty(keyword)
                ? _allUsers
                : _allUsers.Where(u =>
                    u.RealName.Contains(keyword) ||
                    u.Username.Contains(keyword) ||
                    (u.TeamName ?? "").Contains(keyword)).ToList();

            var sorted = _sortByName
                ? filtered.OrderBy(u => u.RealName).ToList()
                : filtered.OrderBy(u => u.TeamName).ThenBy(u => u.RealName).ToList();

            _users = new ObservableCollection<UserModel>(sorted);
            UserListBox.ItemsSource = _users;
            UserCountText.Text = $"{_allUsers.Count}명";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void TxtHireDate_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtCareer != null)
                TxtCareer.Text = CalcCareerStr(TxtHireDate.Text.Trim());
        }

        private static string CalcCareerStr(string hireDate)
        {
            if (string.IsNullOrEmpty(hireDate) || !DateTime.TryParse(hireDate, out var hire)) return "-";
            var today = DateTime.Today;
            int years = today.Year - hire.Year;
            int months = today.Month - hire.Month;
            if (months < 0) { years--; months += 12; }
            if (years < 0) return "-";
            if (years == 0) return $"{months}개월";
            if (months == 0) return $"{years}년";
            return $"{years}년 {months}개월";
        }

        private void BtnSortName_Click(object sender, RoutedEventArgs e)
        {
            _sortByName = true;
            BtnSortName.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DBEAFE"));
            BtnSortTeam.Background = System.Windows.Media.Brushes.White;
            ApplyFilter();
        }

        private void BtnSortTeam_Click(object sender, RoutedEventArgs e)
        {
            _sortByName = false;
            BtnSortTeam.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DBEAFE"));
            BtnSortName.Background = System.Windows.Media.Brushes.White;
            ApplyFilter();
        }

        // ── 목록 선택 ────────────────────────────────────────────
        private void UserListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UserListBox.SelectedItem is UserModel user)
            {
                _selectedUser = user;
                _isAddMode = false;
                ShowDetailPanel(user);
            }
        }

        // ── 신규 추가 버튼 ───────────────────────────────────────
        private void BtnNewUser_Click(object sender, RoutedEventArgs e)
        {
            _isAddMode = true;
            _selectedUser = null;
            UserListBox.SelectedItem = null;

            ClearInputFields();
            DetailName.Text = "신규 사용자";
            DetailTeam.Text = "";
            DetailTeamBadge.Visibility = Visibility.Collapsed;
            NewModeBadge.Visibility = Visibility.Visible;
            BtnDeleteUser.Visibility = Visibility.Collapsed;

            EmptyState.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            MainTabControl.SelectedIndex = 0;
            TxtNewId.Focus();
        }

        // ── 상세 패널 표시 ───────────────────────────────────────
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
            TxtEmployeeNumber.Text = string.IsNullOrEmpty(user.EmployeeNumber) ? user.Username : user.EmployeeNumber;
            TxtHireDate.Text = user.HireDate;
            TxtCareer.Text = CalcCareerStr(user.HireDate);
            TxtNewEmail.Text = user.Email;
            TxtNewPhone.Text = user.PhoneNumber;

            ChkManageFiles.IsChecked = user.CanManageFiles;
            ChkManageNotices.IsChecked = user.CanManageNotices;
            ChkManageVendors.IsChecked = user.CanManageVendors;
            ChkManageSchedule.IsChecked = user.CanManageSchedule;

            EmptyState.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
        }

        // ── 저장 ────────────────────────────────────────────────
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
                MessageBox.Show("아이디, 비밀번호, 이름은 필수 입력 항목입니다.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isAddMode)
            {
                if (_allUsers.Any(u => u.Username == id))
                {
                    MessageBox.Show("이미 존재하는 아이디입니다.", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string empNum = TxtEmployeeNumber.Text.Trim();
                var newUser = new UserModel
                {
                    Username = id, Password = pw, RealName = name,
                    TeamName = TxtNewTeam.Text.Trim(),
                    JobTitle = TxtNewTitle.Text.Trim(),
                    EmployeeNumber = string.IsNullOrEmpty(empNum) ? id : empNum,
                    HireDate = TxtHireDate.Text.Trim(),
                    Email = TxtNewEmail.Text.Trim(),
                    PhoneNumber = TxtNewPhone.Text.Trim(),
                    CanManageFiles = ChkManageFiles.IsChecked == true,
                    CanManageNotices = ChkManageNotices.IsChecked == true,
                    CanManageVendors = ChkManageVendors.IsChecked == true,
                    CanManageSchedule = ChkManageSchedule.IsChecked == true
                };

                _allUsers.Add(newUser);
                AuthDatabaseHelper.SaveAllUsers(_allUsers);
                ApplyFilter();
                UserCountText.Text = $"{_allUsers.Count}명";

                MessageBox.Show("새 사용자가 추가되었습니다.", "성공",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                _isAddMode = false;
                _selectedUser = newUser;
                UserListBox.SelectedItem = _users.FirstOrDefault(u => u.Username == newUser.Username);
            }
            else
            {
                if (_selectedUser == null) return;

                if (id != _selectedUser.Username && _allUsers.Any(u => u.Username == id))
                {
                    MessageBox.Show("이미 사용 중인 아이디입니다.", "오류",
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
                _selectedUser.EmployeeNumber = TxtEmployeeNumber.Text.Trim();
                _selectedUser.HireDate = TxtHireDate.Text.Trim();
                _selectedUser.Email = TxtNewEmail.Text.Trim();
                _selectedUser.PhoneNumber = TxtNewPhone.Text.Trim();
                _selectedUser.CanManageFiles = ChkManageFiles.IsChecked == true;
                _selectedUser.CanManageNotices = ChkManageNotices.IsChecked == true;
                _selectedUser.CanManageVendors = ChkManageVendors.IsChecked == true;
                _selectedUser.CanManageSchedule = ChkManageSchedule.IsChecked == true;

                AuthDatabaseHelper.SaveAllUsers(_allUsers);
                ApplyFilter();

                DetailName.Text = _selectedUser.RealName;
                DetailTeam.Text = _selectedUser.TeamName;
                DetailTeamBadge.Visibility = string.IsNullOrEmpty(_selectedUser.TeamName)
                    ? Visibility.Collapsed : Visibility.Visible;

                MessageBox.Show("사용자 정보가 수정되었습니다.", "성공",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ── 취소 ────────────────────────────────────────────────
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _isAddMode = false;
            _selectedUser = null;
            UserListBox.SelectedItem = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }

        // ── 삭제 ────────────────────────────────────────────────
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
                _allUsers.Remove(_selectedUser);
                AuthDatabaseHelper.SaveAllUsers(_allUsers);
                ApplyFilter();
                UserCountText.Text = $"{_allUsers.Count}명";

                _selectedUser = null;
                UserListBox.SelectedItem = null;
                DetailPanel.Visibility = Visibility.Collapsed;
                EmptyState.Visibility = Visibility.Visible;
            }
        }

        // ── 엑셀 다운로드 ─────────────────────────────────────────
        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title    = "사용자 목록 엑셀 저장",
                Filter   = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"사용자목록_{DateTime.Now:yyyyMMdd}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("사용자 목록");

                string[] headers =
                {
                    "이름", "아이디", "소속팀", "직위", "사번",
                    "입사일", "근속", "이메일", "전화번호",
                    "파일관리", "공지관리", "업체관리", "일정관리"
                };

                for (int c = 0; c < headers.Length; c++)
                {
                    var hCell = ws.Cell(1, c + 1);
                    hCell.Value = headers[c];
                    hCell.Style.Font.Bold = true;
                    hCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
                    hCell.Style.Font.FontColor = XLColor.White;
                    hCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                var users = _allUsers.OrderBy(u => u.RealName).ToList();
                for (int r = 0; r < users.Count; r++)
                {
                    var u = users[r];
                    int row = r + 2;
                    ws.Cell(row, 1).Value  = u.RealName;
                    ws.Cell(row, 2).Value  = u.Username;
                    ws.Cell(row, 3).Value  = u.TeamName ?? "";
                    ws.Cell(row, 4).Value  = u.JobTitle ?? "";
                    ws.Cell(row, 5).Value  = string.IsNullOrEmpty(u.EmployeeNumber) ? u.Username : u.EmployeeNumber;
                    ws.Cell(row, 6).Value  = u.HireDate ?? "";
                    ws.Cell(row, 7).Value  = CalcCareerStr(u.HireDate ?? "");
                    ws.Cell(row, 8).Value  = u.Email ?? "";
                    ws.Cell(row, 9).Value  = u.PhoneNumber ?? "";
                    ws.Cell(row, 10).Value = u.CanManageFiles     ? "O" : "";
                    ws.Cell(row, 11).Value = u.CanManageNotices   ? "O" : "";
                    ws.Cell(row, 12).Value = u.CanManageVendors   ? "O" : "";
                    ws.Cell(row, 13).Value = u.CanManageSchedule  ? "O" : "";

                    // 짝수 행 연한 배경 — 행 전체가 아닌 데이터 범위 셀만 적용
                    if (row % 2 == 0)
                        ws.Range(row, 1, row, headers.Length)
                          .Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                }

                // 열 너비를 데이터 범위만 기준으로 조정
                ws.Range(1, 1, users.Count + 1, headers.Length).Columns().AdjustToContents();
                // 권한 열은 좁게 고정
                foreach (int col in new[] { 10, 11, 12, 13 })
                    ws.Column(col).Width = 8;

                wb.SaveAs(dlg.FileName);

                if (MessageBox.Show("엑셀 파일이 저장되었습니다.\n바로 열어보시겠습니까?",
                    "완료", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (IOException)
            {
                MessageBox.Show("파일이 다른 프로그램에서 열려 있습니다.\n파일을 닫고 다시 시도하세요.",
                    "저장 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("엑셀 저장 오류: " + ex.Message);
            }
        }

        private void ClearInputFields()
        {
            TxtNewId.Clear(); TxtNewPw.Clear(); TxtNewName.Clear();
            TxtNewTitle.Clear(); TxtNewTeam.Clear();
            TxtEmployeeNumber.Clear(); TxtHireDate.Clear(); TxtNewEmail.Clear(); TxtNewPhone.Clear();
            ChkManageFiles.IsChecked = false; ChkManageNotices.IsChecked = false;
            ChkManageVendors.IsChecked = false; ChkManageSchedule.IsChecked = false;
        }
    }
}
