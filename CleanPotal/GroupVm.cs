using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CleanPotal
{
    public class GroupVm : INotifyPropertyChanged
    {
        private string _group = "";
        public string Group
        {
            get => _group;
            set { _group = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Group))); }
        }

        public ObservableCollection<ItemVm> Items { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
