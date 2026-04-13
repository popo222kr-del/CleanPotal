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

        // 🔥 누락되었던 탭 분류용 속성 (SEMES, QTZ)
        private string _category = "QTZ";
        public string Category { get => _category; set { _category = value; OnPropertyChanged(); } }

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
                        // 공용 네트워크 폴더 참조
                        string root = System.IO.Path.Combine(AppPaths.DataRoot, "handover_images");
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

        private string _creatorName = "";
        public string CreatorName { get => _creatorName; set { _creatorName = value; OnPropertyChanged(); OnPropertyChanged(nameof(CreatorText)); OnPropertyChanged(nameof(IsNewUpdate)); } }

        private DateTime _createDate = DateTime.Now;
        public DateTime CreateDate { get => _createDate; set { _createDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(CreatorText)); OnPropertyChanged(nameof(IsNewUpdate)); } }

        private string _modifierName = "";
        public string ModifierName { get => _modifierName; set { _modifierName = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModifierText)); OnPropertyChanged(nameof(HasModifier)); OnPropertyChanged(nameof(IsNewUpdate)); } }

        private DateTime _modifyDate = DateTime.Now;
        public DateTime ModifyDate { get => _modifyDate; set { _modifyDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModifierText)); OnPropertyChanged(nameof(IsNewUpdate)); } }

        // 🔥 누락되었던 개별 사용자 읽음 처리 기록 속성
        private string _readBy = "";
        public string ReadBy { get => _readBy; set { _readBy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNewUpdate)); } }

        public string CreatorText => string.IsNullOrEmpty(CreatorName) || CreatorName == "알수없음" ? "" : $"✍️ 등록: {CreatorName} ({CreateDate:yy.MM.dd HH:mm})";
        public string ModifierText => string.IsNullOrEmpty(ModifierName) ? "" : $"🔄 수정: {ModifierName} ({ModifyDate:yy.MM.dd HH:mm})";
        public bool HasModifier => !string.IsNullOrEmpty(ModifierName);

        // 🔥 누락되었던 알림/레드닷 표시 계산 로직 (새 글 + 수정 글 모두 포함)
        public bool IsNewUpdate
        {
            get
            {
                if (!SessionManager.IsLoggedIn) return false;
                string currentUser = SessionManager.CurrentRealName;

                // 1. 내가 이미 클릭해서 읽은 항목이라면 빨간 점 즉시 소멸
                if (!string.IsNullOrEmpty(ReadBy) && ReadBy.Contains(currentUser)) return false;

                // 2. 다른 사람이 24시간 이내에 새로 등록한 경우
                bool isNewlyCreated = !string.IsNullOrEmpty(CreatorName) &&
                                      CreatorName != currentUser &&
                                      (DateTime.Now - CreateDate).TotalHours <= 24;

                // 3. 다른 사람이 24시간 이내에 수정한 경우
                bool isRecentlyModified = !string.IsNullOrEmpty(ModifierName) &&
                                          ModifierName != currentUser &&
                                          (DateTime.Now - ModifyDate).TotalHours <= 24;

                return isNewlyCreated || isRecentlyModified;
            }
        }
    }
}