using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    public partial class EduCopyPickerWindow : Window
    {
        private readonly List<WorkAssignmentMember> _allMembers;
        public WorkAssignmentMember? SelectedMember { get; private set; }

        public EduCopyPickerWindow(List<WorkAssignmentMember> candidates, string targetName)
        {
            InitializeComponent();
            _allMembers = candidates;
            TxtTargetName.Text = $"'{targetName}' 인원에게 교육 기록을 복사합니다.";
            Refresh("");
        }

        private void Refresh(string kw)
        {
            var list = string.IsNullOrEmpty(kw)
                ? _allMembers
                : _allMembers.Where(m =>
                    m.RealName.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.TeamName.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            MemberPickerList.ItemsSource = list;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
            => Refresh(TxtSearch.Text.Trim());

        private void MemberPickerList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => Confirm();

        private void BtnConfirm_Click(object sender, RoutedEventArgs e) => Confirm();
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Confirm()
        {
            if (MemberPickerList.SelectedItem is not WorkAssignmentMember m) return;
            SelectedMember = m;
            DialogResult = true;
        }
    }
}
