using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    public partial class WorkAssignmentAddMemberWindow : Window
    {
        private List<UserModel> _allAvailable;
        public string? SelectedUsername { get; private set; }

        public WorkAssignmentAddMemberWindow(List<UserModel> available)
        {
            InitializeComponent();
            _allAvailable = available;
            UserList.ItemsSource = _allAvailable;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string kw = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(kw))
                UserList.ItemsSource = _allAvailable;
            else
                UserList.ItemsSource = _allAvailable
                    .Where(u => u.RealName.Contains(kw) || u.TeamName.Contains(kw))
                    .ToList();
        }

        private void UserList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Confirm();

        private void BtnConfirm_Click(object sender, RoutedEventArgs e) => Confirm();

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Confirm()
        {
            if (UserList.SelectedItem is UserModel u)
            {
                SelectedUsername = u.Username;
                DialogResult = true;
            }
        }
    }
}
