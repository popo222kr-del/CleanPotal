using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CleanPotal
{
    public class HandoverItem : INotifyPropertyChanged
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        private string _vendor = "";
        public string Vendor { get => _vendor; set { _vendor = value; OnPropertyChanged(); } }

        private string _owner = "";
        public string Owner { get => _owner; set { _owner = value; OnPropertyChanged(); } }

        private string _content = "";
        public string Content { get => _content; set { _content = value; OnPropertyChanged(); } }

        private DateTime? _inDate;
        public DateTime? InDate { get => _inDate; set { _inDate = value; OnPropertyChanged(); NotifyProgress(); } }

        private DateTime? _outDate;
        public DateTime? OutDate { get => _outDate; set { _outDate = value; OnPropertyChanged(); NotifyProgress(); } }

        private string _status = "진행";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); NotifyProgress(); } }

        private string _memo = "";
        public string Memo
        {
            get => _memo;
            set
            {
                _memo = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayMemoText));
                OnPropertyChanged(nameof(FirstImagePath));
                OnPropertyChanged(nameof(HasImage));
                OnPropertyChanged(nameof(HasText));
            }
        }

        // 🔥 이미지 썸네일과 텍스트를 자동 분리하는 스마트 속성
        public string DisplayMemoText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Memo)) return "";
                int start = Memo.IndexOf("[[HANDOVER_IMAGES]]", StringComparison.Ordinal);
                if (start < 0) return Memo;
                return Memo.Substring(0, start).TrimEnd();
            }
        }

        public string FirstImagePath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Memo)) return "";
                int start = Memo.IndexOf("[[HANDOVER_IMAGES]]", StringComparison.Ordinal);
                int end = Memo.IndexOf("[[/HANDOVER_IMAGES]]", StringComparison.Ordinal);
                if (start < 0 || end < 0 || end <= start) return "";

                string block = Memo.Substring(start + 19, end - (start + 19));
                var lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "handover_images");
                        return System.IO.Path.GetFullPath(System.IO.Path.Combine(root, trimmed));
                    }
                }
                return "";
            }
        }

        public bool HasImage => !string.IsNullOrEmpty(FirstImagePath);
        public bool HasText => !string.IsNullOrWhiteSpace(DisplayMemoText);

        private bool _manageChecked;
        public bool ManageChecked { get => _manageChecked; set { _manageChecked = value; OnPropertyChanged(); } }

        public bool IsDone => string.Equals(Status, "완료", StringComparison.OrdinalIgnoreCase);

        public int ProgressPercent => IsDone ? 100 : CalcProgressPercent(DateTime.Today, InDate, OutDate);
        public string ProgressText => $"{ProgressPercent}%";

        public void NotifyProgress()
        {
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(ProgressText));
        }

        public static int CalcProgressPercent(DateTime today, DateTime? inDate, DateTime? outDate)
        {
            if (inDate == null || outDate == null) return 0;
            var start = inDate.Value.Date;
            var end = outDate.Value.Date;
            if (end <= start) return today >= end ? 100 : 0;
            var p = (int)Math.Round((today - start).TotalDays / (end - start).TotalDays * 100.0, MidpointRounding.AwayFromZero);
            return p < 0 ? 0 : (p > 100 ? 100 : p);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}