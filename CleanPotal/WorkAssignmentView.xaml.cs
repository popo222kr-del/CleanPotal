using System;
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

        public WorkAssignmentView()
        {
            InitializeComponent();
            LoadMembers();
        }

        public void TryRefresh() => LoadMembers();

        private void LoadMembers()
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
                        PhoneNumber = u.PhoneNumber
                    });
            }

            MemberList.ItemsSource = _members;
            TxtMemberCount.Text = $"{_members.Count}명";
        }

        private void MemberList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selected = MemberList.SelectedItem as WorkAssignmentMember;
            if (_selected == null)
            {
                PanelEmpty.Visibility = Visibility.Visible;
                PanelDetail.Visibility = Visibility.Collapsed;
                return;
            }
            ShowDetail(_selected);
        }

        private void ShowDetail(WorkAssignmentMember m)
        {
            PanelEmpty.Visibility = Visibility.Collapsed;
            PanelDetail.Visibility = Visibility.Visible;

            InfoRealName.Text = m.RealName;
            InfoUsername.Text = m.Username;
            InfoTeam.Text = m.TeamName;
            InfoJobTitle.Text = m.JobTitle;
            InfoHireDate.Text = string.IsNullOrEmpty(m.HireDate) ? "-" : m.HireDate;
            InfoCareer.Text = m.CareerStr;
            InfoEmail.Text = string.IsNullOrEmpty(m.Email) ? "-" : m.Email;
            InfoPhone.Text = string.IsNullOrEmpty(m.PhoneNumber) ? "-" : m.PhoneNumber;

            var eduItems = DatabaseHelper.GetEduBasicItems(m.Username);
            EduBasicGrid.ItemsSource = new ObservableCollection<EduBasicItem>(eduItems);

            var accountItems = DatabaseHelper.GetAccountItems(m.Username);
            AccountGrid.ItemsSource = new ObservableCollection<AccountItem>(accountItems);

            var extEdu = DatabaseHelper.GetEducationPlansByMember(m.RealName);
            ExtEduGrid.ItemsSource = extEdu.OrderByDescending(x => x.StartDate).ToList();
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
            if (win.ShowDialog() == true && win.SelectedUsername != null)
            {
                DatabaseHelper.AddWorkAssignmentMember(win.SelectedUsername);
                LoadMembers();
                var added = _members.FirstOrDefault(m => m.Username == win.SelectedUsername);
                if (added != null) MemberList.SelectedItem = added;
            }
        }

        private void BtnRemoveMember_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            var result = MessageBox.Show($"'{_selected.RealName}' 인원을 목록에서 삭제하시겠습니까?\n관련 기본 교육 기록 및 기관 계정 데이터도 함께 삭제됩니다.",
                "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            DatabaseHelper.RemoveWorkAssignmentMember(_selected.Username);
            LoadMembers();
            PanelEmpty.Visibility = Visibility.Visible;
            PanelDetail.Visibility = Visibility.Collapsed;
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
            var items = (EduBasicGrid.ItemsSource as ObservableCollection<EduBasicItem>) ?? new ObservableCollection<EduBasicItem>();
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
