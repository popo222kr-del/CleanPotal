using System.ComponentModel;

namespace CleanPotal
{
    public class ItemVm : INotifyPropertyChanged
    {
        private string _title = "";
        public string Title
        {
            get => _title;
            set { _title = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title))); }
        }

        private string _path = "";
        public string Path
        {
            get => _path;
            set { _path = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Path))); }
        }

        private string _type = "file"; // file / folder
        public string Type
        {
            get => _type;
            set { _type = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
