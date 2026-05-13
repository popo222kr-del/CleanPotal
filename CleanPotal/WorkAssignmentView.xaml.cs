using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CleanPotal
{
    public partial class WorkAssignmentView : UserControl
    {
        private ObservableCollection<WorkAssignmentMember> _members = new();
        private WorkAssignmentMember? _selected;
        private enum SortMode { NameAsc, NameDesc, Team }
        private SortMode _sortMode = SortMode.NameAsc;

        public WorkAssignmentView()
        {
            InitializeComponent();
            LoadMembers(null);
        }

        // 새로고침 시 선택된 인원 유지 — 폴링 타이머가 호출해도 화면 이탈 없음
        public void TryRefresh()
        {
            string? selectedUsername = _selected?.Username;
            LoadMembers(selectedUsername);
        }

        private void LoadMembers(string? restoreUsername)
        {
            var usernames = DatabaseHelper.GetWorkAssignmentUsernames();
            var allUsers = AuthDatabaseHelper.GetAllUsers();

            _members.Clear();
            foreach (var uname in usernames)
            {
                var u = allUsers.FirstOrDefault(x => x.Username == uname);
                if (u != null)
                    _members.Add(new WorkAssignmentMember
                    {
                        Username = u.Username,
                        RealName = u.RealName,
                        TeamName = u.TeamName,
                        JobTitle = u.JobTitle,
                        HireDate = u.HireDate,
                        Email = u.Email,
                        PhoneNumber = u.PhoneNumber,
                        EmployeeNumber = u.EmployeeNumber
                    });
            }

            TxtMemberCount.Text = $"{_members.Count}명";
            ApplySearchSort();

            // 이전 선택 복원
            if (restoreUsername != null)
            {
                var restore = _members.FirstOrDefault(m => m.Username == restoreUsername);
                if (restore != null)
                {
                    MemberList.SelectedItem = restore;
                    return;
                }
            }

            // 복원할 항목이 없으면 빈 상태
            _selected = null;
            PanelEmpty.Visibility = Visibility.Visible;
            PanelDetail.Visibility = Visibility.Collapsed;
        }

        private void MemberList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MemberList.SelectedItem is WorkAssignmentMember m)
            {
                _selected = m;
                ShowDetail(_selected);
            }
            else
            {
                _selected = null;
                PanelEmpty.Visibility = Visibility.Visible;
                PanelDetail.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowDetail(WorkAssignmentMember m)
        {
            PanelEmpty.Visibility = Visibility.Collapsed;
            PanelDetail.Visibility = Visibility.Visible;

            InfoRealName.Text = m.RealName;
            InfoEmployeeNumber.Text = string.IsNullOrEmpty(m.EmployeeNumber) ? m.Username : m.EmployeeNumber;
            InfoTeam.Text = m.TeamName;
            InfoJobTitle.Text = m.JobTitle;
            InfoHireDate.Text = string.IsNullOrEmpty(m.HireDate) ? "-" : m.HireDate;
            InfoCareer.Text = m.CareerStr;
            InfoEmail.Text = string.IsNullOrEmpty(m.Email) ? "-" : m.Email;
            InfoPhone.Text = string.IsNullOrEmpty(m.PhoneNumber) ? "-" : m.PhoneNumber;

            var eduItems = DatabaseHelper.GetEduBasicItems(m.Username);
            EduBasicGrid.ItemsSource = new ObservableCollection<EduBasicItem>(eduItems);

            var extEdu = DatabaseHelper.GetEducationPlansByMember(m.RealName);
            ExtEduGrid.ItemsSource = extEdu.OrderByDescending(x => x.StartDate).ToList();

            var accountItems = DatabaseHelper.GetAccountItems(m.Username);
            AccountGrid.ItemsSource = new ObservableCollection<AccountItem>(accountItems);
        }

        private void TxtMemberSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplySearchSort();

        private void BtnSortName_Click(object sender, RoutedEventArgs e)
        {
            _sortMode = _sortMode == SortMode.NameAsc ? SortMode.NameDesc : SortMode.NameAsc;
            BtnSortName.Content = _sortMode == SortMode.NameAsc ? "가나다↑" : "가나다↓";
            SetSortButtonStyles();
            ApplySearchSort();
        }
        private void BtnSortTeam_Click(object sender, RoutedEventArgs e) { _sortMode = SortMode.Team; SetSortButtonStyles(); ApplySearchSort(); }

        private void SetSortButtonStyles()
        {
            var activeColor = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EFF6FF"));
            var normalColor = System.Windows.Media.Brushes.White;
            BtnSortName.Background = (_sortMode == SortMode.NameAsc || _sortMode == SortMode.NameDesc) ? activeColor : normalColor;
            BtnSortTeam.Background = _sortMode == SortMode.Team ? activeColor : normalColor;
        }

        private void ApplySearchSort()
        {
            string kw = TxtMemberSearch?.Text?.Trim() ?? "";
            var filtered = string.IsNullOrEmpty(kw)
                ? _members.ToList()
                : _members.Where(m =>
                    m.RealName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.TeamName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            var sorted = _sortMode switch
            {
                SortMode.NameDesc => filtered.OrderByDescending(m => m.RealName).ToList(),
                SortMode.Team     => filtered.OrderBy(m => m.TeamName).ThenBy(m => m.RealName).ToList(),
                _                 => filtered.OrderBy(m => m.RealName).ToList()
            };

            MemberList.ItemsSource = sorted;
        }

        private void BtnAddMember_Click(object sender, RoutedEventArgs e)
        {
            var allUsers = AuthDatabaseHelper.GetAllUsers();
            var existing = _members.Select(m => m.Username).ToHashSet();
            var available = allUsers.Where(u => !existing.Contains(u.Username) && !string.IsNullOrEmpty(u.RealName)).ToList();

            if (available.Count == 0)
            {
                MessageBox.Show("추가 가능한 인원이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var win = new WorkAssignmentAddMemberWindow(available) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true && win.SelectedUsernames?.Count > 0)
            {
                foreach (var uname in win.SelectedUsernames)
                    DatabaseHelper.AddWorkAssignmentMember(uname);

                string? firstAdded = win.SelectedUsernames.First();
                LoadMembers(firstAdded);
            }
        }

        private void BtnRemoveMember_Click(object sender, RoutedEventArgs e)
        {
            var toDelete = MemberList.SelectedItems.Cast<WorkAssignmentMember>().ToList();
            if (toDelete.Count == 0) return;

            string names = string.Join(", ", toDelete.Select(m => m.RealName));
            string msg = toDelete.Count == 1
                ? $"'{names}' 인원을 목록에서 삭제하시겠습니까?\n관련 기본 교육 기록 및 기관 계정 데이터도 함께 삭제됩니다."
                : $"선택한 {toDelete.Count}명({names})을 목록에서 삭제하시겠습니까?\n관련 데이터도 모두 삭제됩니다.";

            if (MessageBox.Show(msg, "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            foreach (var m in toDelete)
                DatabaseHelper.RemoveWorkAssignmentMember(m.Username);

            LoadMembers(null);
        }

        private void EduBasicGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
            if (_selected == null) return;
            if (EduBasicGrid.ItemsSource is not ObservableCollection<EduBasicItem> list) return;

            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var cols = line.Split('\t');
                var item = new EduBasicItem { Username = _selected.Username };
                if (cols.Length >= 1) item.EduName = cols[0].Trim();
                if (cols.Length >= 2) ParseEduDateIntoItem(cols[1].Trim(), item);
                list.Add(item);
            }
            e.Handled = true;
        }

        private static void ParseEduDateIntoItem(string raw, EduBasicItem item)
        {
            if (string.IsNullOrEmpty(raw)) return;
            var tildeIdx = raw.IndexOf('~');
            if (tildeIdx < 0)
            {
                if (DateTime.TryParse(raw, out var d)) item.StartDate = d.ToString("yyyy-MM-dd");
                return;
            }

            var startStr = raw[..tildeIdx].Trim();
            var endStr   = raw[(tildeIdx + 1)..].Trim();
            if (!DateTime.TryParse(startStr, out var start)) return;
            item.StartDate = start.ToString("yyyy-MM-dd");

            // 종료 부분: 완전한 날짜 / MM-dd / dd 세 가지 처리
            if (DateTime.TryParse(endStr, out var fullEnd))
            {
                item.EndDate = fullEnd.ToString("yyyy-MM-dd");
            }
            else if (endStr.Contains('-') && DateTime.TryParse($"{start.Year}-{endStr}", out var monthEnd))
            {
                item.EndDate = monthEnd.ToString("yyyy-MM-dd");
            }
            else if (int.TryParse(endStr, out int day) && day >= 1 && day <= 31)
            {
                var lastOfMonth = DateTime.DaysInMonth(start.Year, start.Month);
                item.EndDate = new DateTime(start.Year, start.Month, Math.Min(day, lastOfMonth)).ToString("yyyy-MM-dd");
            }
        }

        private void BtnAddEduRow_Click(object sender, RoutedEventArgs e)
        {
            if (EduBasicGrid.ItemsSource is ObservableCollection<EduBasicItem> list)
                list.Add(new EduBasicItem { Username = _selected?.Username ?? "" });
        }

        private void BtnSaveEdu_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            EduBasicGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var items = EduBasicGrid.ItemsSource as ObservableCollection<EduBasicItem> ?? new();
            DatabaseHelper.SaveEduBasicItems(_selected.Username, items);
            MessageBox.Show("기본 교육 기록이 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDeleteEduRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is EduBasicItem item &&
                EduBasicGrid.ItemsSource is ObservableCollection<EduBasicItem> list)
                list.Remove(item);
        }

        private void BtnAddAccountRow_Click(object sender, RoutedEventArgs e)
        {
            if (AccountGrid.ItemsSource is ObservableCollection<AccountItem> list)
                list.Add(new AccountItem { Username = _selected?.Username ?? "" });
        }

        private void BtnSaveAccounts_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            AccountGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var items = (AccountGrid.ItemsSource as ObservableCollection<AccountItem>) ?? new ObservableCollection<AccountItem>();
            DatabaseHelper.SaveAccountItems(_selected.Username, items);
            MessageBox.Show("기관 계정 정보가 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDeleteAccountRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is AccountItem item &&
                AccountGrid.ItemsSource is ObservableCollection<AccountItem> list)
                list.Remove(item);
        }
    }
}
