using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    public partial class WorkAssignmentAddMemberWindow : Window
    {
        private List<UserModel> _allAvailable;
        public List<string>? SelectedUsernames { get; private set; }

        public WorkAssignmentAddMemberWindow(List<UserModel> available)
        {
            InitializeComponent();
            _allAvailable = available;
            UserList.ItemsSource = _allAvailable;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string kw = SearchBox.Text.Trim();
            UserList.ItemsSource = string.IsNullOrEmpty(kw)
                ? _allAvailable
                : _allAvailable.Where(u => u.RealName.Contains(kw) || u.TeamName.Contains(kw)).ToList();
        }

        private void UserList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Confirm();

        private void BtnConfirm_Click(object sender, RoutedEventArgs e) => Confirm();

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Confirm()
        {
            var selected = UserList.SelectedItems.Cast<UserModel>().ToList();
            if (selected.Count == 0) return;
            SelectedUsernames = selected.Select(u => u.Username).ToList();
            DialogResult = true;
        }
    }
}
