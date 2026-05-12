using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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

            var combined = new ObservableCollection<EduCombinedRow>();
            var eduItems = DatabaseHelper.GetEduBasicItems(m.Username);
            foreach (var item in eduItems)
                combined.Add(new EduCombinedRow
                {
                    IsManual = true,
                    Username = item.Username,
                    EduName = item.EduName,
                    EduDate = item.EduDate,
                    Instructor = item.Instructor,
                    Note = item.Note
                });
            var extEdu = DatabaseHelper.GetEducationPlansByMember(m.RealName);
            foreach (var e in extEdu.OrderByDescending(x => x.StartDate))
                combined.Add(new EduCombinedRow
                {
                    IsManual = false,
                    EduId = e.Id,
                    EduName = e.CourseName,
                    EduDate = e.StartDate.ToString("yyyy-MM-dd"),
                    EndDate = e.EndDate.ToString("yyyy-MM-dd"),
                    Instructor = e.EduMethod ?? "",
                    Status = e.Status ?? ""
                });
            EduCombinedGrid.ItemsSource = combined;

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

        private void BtnAddEduRow_Click(object sender, RoutedEventArgs e)
        {
            if (EduCombinedGrid.ItemsSource is ObservableCollection<EduCombinedRow> list)
                list.Add(new EduCombinedRow { IsManual = true, Username = _selected?.Username ?? "" });
        }

        private void BtnSaveEdu_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            EduCombinedGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var rows = EduCombinedGrid.ItemsSource as ObservableCollection<EduCombinedRow> ?? new();
            var manualItems = new ObservableCollection<EduBasicItem>(
                rows.Where(r => r.IsManual).Select(r => new EduBasicItem
                {
                    Username = r.Username,
                    EduName = r.EduName,
                    EduDate = r.EduDate,
                    Instructor = r.Instructor,
                    Note = r.Note
                }));
            DatabaseHelper.SaveEduBasicItems(_selected.Username, manualItems);
            MessageBox.Show("기본 교육 기록이 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDeleteEduRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is EduCombinedRow item &&
                EduCombinedGrid.ItemsSource is ObservableCollection<EduCombinedRow> list)
                list.Remove(item);
        }

        private void EduCombinedGrid_BeginningEdit(object sender, System.Windows.Controls.DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is EduCombinedRow row && !row.IsManual)
                e.Cancel = true;
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
