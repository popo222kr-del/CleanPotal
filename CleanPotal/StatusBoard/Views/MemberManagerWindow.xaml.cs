using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CleanPotal.StatusBoard.Views
{
    // 인원 항목 (이름 인라인 수정 가능)
    public class MemberEntry : INotifyPropertyChanged
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class MemberManagerWindow : Window
    {
        private readonly ObservableCollection<MemberEntry> _members = new();

        // 저장 시 결과(순서 반영된 이름 목록)
        public List<string> ResultMembers { get; private set; } = new();

        public MemberManagerWindow(IEnumerable<string> initial)
        {
            InitializeComponent();
            foreach (var n in initial)
                if (!string.IsNullOrWhiteSpace(n)) _members.Add(new MemberEntry { Name = n.Trim() });
            LstMembers.ItemsSource = _members;
        }

        private void AddName()
        {
            string name = (TxtNewName.Text ?? "").Trim();
            if (name.Length == 0) return;
            if (_members.Any(m => m.Name == name))
            {
                MessageBox.Show("이미 있는 이름입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _members.Add(new MemberEntry { Name = name });
            TxtNewName.Text = "";
            TxtNewName.Focus();
            LstMembers.SelectedIndex = _members.Count - 1;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e) => AddName();

        private void TxtNewName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddName();
        }

        // 목록 항목의 이름 편집 박스가 포커스되면 그 행을 선택 상태로
        private void MemberBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(LstMembers, (System.Windows.DependencyObject)sender) is ListBoxItem item)
                item.IsSelected = true;
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            int i = LstMembers.SelectedIndex;
            if (i < 0) return;
            _members.RemoveAt(i);
            if (_members.Count > 0) LstMembers.SelectedIndex = System.Math.Min(i, _members.Count - 1);
        }

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            int i = LstMembers.SelectedIndex;
            if (i <= 0) return;
            _members.Move(i, i - 1);
            LstMembers.SelectedIndex = i - 1;
        }

        private void BtnDown_Click(object sender, RoutedEventArgs e)
        {
            int i = LstMembers.SelectedIndex;
            if (i < 0 || i >= _members.Count - 1) return;
            _members.Move(i, i + 1);
            LstMembers.SelectedIndex = i + 1;
        }

        private void BtnSaveMembers_Click(object sender, RoutedEventArgs e)
        {
            ResultMembers = _members.Select(m => (m.Name ?? "").Trim())
                                    .Where(n => n.Length > 0).ToList();
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
