using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CleanPotal
{
    public class TeamStatusGroup
    {
        public string TeamName { get; set; } = "";
        public ObservableCollection<TodayStatusItem> StatusList { get; set; } = new();
    }

    public partial class HandoverView : UserControl, INotifyPropertyChanged
    {
        private const string ImageBlockStart = "[[HANDOVER_IMAGES]]";
        private const string ImageBlockEnd = "[[/HANDOVER_IMAGES]]";
        private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

        public class NoticeItem : INotifyPropertyChanged
        {
            public Guid Id { get; set; } = Guid.NewGuid();
            private string _text = "";
            public string Text
            {
                get => _text;
                set { _text = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        public class UpcomingEduItem
        {
            public string DaysLabel { get; set; } = "";
            public System.Windows.Media.Brush DaysBg { get; set; } = System.Windows.Media.Brushes.Transparent;
            public System.Windows.Media.Brush DaysFg { get; set; } = System.Windows.Media.Brushes.Black;
            public string MemberName { get; set; } = "";
            public string TeamName { get; set; } = "";
            public string CourseName { get; set; } = "";
            public string DateRangeStr { get; set; } = "";
            public string EduMethod { get; set; } = "";
        }

        public class TeamEventNoticeItem
        {
            public string Content { get; set; } = "";
            public string Detail { get; set; } = "";
            public string DateLabel { get; set; } = "";
            public System.Windows.Media.Brush StatusBg { get; set; } = System.Windows.Media.Brushes.Transparent;
            public System.Windows.Media.Brush StatusFg { get; set; } = System.Windows.Media.Brushes.Black;
            public string StatusLabel { get; set; } = "";
            public string FullContent => string.IsNullOrWhiteSpace(Detail) ? Content : $"{Content}  -  {Detail}";
        }

        private static System.Windows.Media.SolidColorBrush HexBrush(string hex)
            => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));


        private readonly bool _weeklyMode;
        private HashSet<string> _weeklyVendorNames = new(StringComparer.OrdinalIgnoreCase);

        private AutoSyncManager _noticeSyncManager;
        private AutoSyncManager _dbSyncManager;
        private AutoSyncManager _vendorSyncManager;

        private ICollectionView _activeView;
        private ICollectionView _doneView;

        private string _currentFilter = "전체";
        private string _activeSearchKeyword = "";
        private string _doneSearchKeyword = "";

        public ObservableCollection<string> VendorSuggestions { get; } = new();
        public ObservableCollection<string> OwnerSuggestions { get; } = new();

        private List<VendorModel> _cachedVendors = new();

        public HandoverView(bool weeklyMode = false)
        {
            _weeklyMode = weeklyMode;
            InitializeComponent();
            DataContext = this;

            StatusOptions = new ObservableCollection<string> { "진행", "포장", "완료" };
            DeliveryOptions = new ObservableCollection<string> { "➖ 미정", "🚚 배차", "📦 택배", "🏢 업체 회수" };

            TodayText = DateTime.Now.ToString("yyyy-MM-dd dddd");
            EditInDate = DateTime.Today;
            EditOutDate = DateTime.Today;
            EditStatus = "진행";
            EditDeliveryMethod = "➖ 미정";

            if (_weeklyMode)
            {
                TopDashboardArea.Visibility = Visibility.Collapsed;
                DashboardToggleRow.Visibility = Visibility.Collapsed;
            }

            HookManageCheckedEvents();
            HookDoneCheckedEvents();

            LoadNotices();
            LoadHandoverAll();
            RefreshVendorSuggestions();
            if (!_weeklyMode) { LoadTodayStatus(); LoadUpcomingEdu(); LoadUpcomingTeamEvents(); }

            _activeView = CollectionViewSource.GetDefaultView(ActiveItems);
            _activeView.Filter = FilterActiveItem;
            ActiveGrid.ItemsSource = _activeView;

            _doneView = CollectionViewSource.GetDefaultView(DoneItems);
            _doneView.Filter = FilterDoneItem;
            DoneGrid.ItemsSource = _doneView;

            UpdateManageColumnVisibility();
            RefreshTopProgressPreview();

            _noticeSyncManager = new AutoSyncManager(() => { Dispatcher.Invoke(() => { LoadNotices(); }); }, Path.Combine(AppPaths.DataRoot, "office_notice.json"));
            _dbSyncManager = new AutoSyncManager(() => { Dispatcher.Invoke(() => { LoadHandoverAll(); LoadTodayStatus(); LoadUpcomingEdu(); LoadUpcomingTeamEvents(); }); }, Path.Combine(AppPaths.DataRoot, "dispatch.db"));
            _vendorSyncManager = new AutoSyncManager(() => { Dispatcher.Invoke(() => { LoadHandoverAll(); RefreshVendorSuggestions(); }); }, AppPaths.VendorsFilePath);

            this.Loaded += (s, e) => { _noticeSyncManager.Start(); _dbSyncManager.Start(); _vendorSyncManager.Start(); };
            this.Unloaded += (s, e) => { _noticeSyncManager.Stop(); _dbSyncManager.Stop(); _vendorSyncManager.Stop(); };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ObservableCollection<HandoverItem> ActiveItems { get; } = new();
        public ObservableCollection<HandoverItem> DoneItems { get; } = new();
        public ObservableCollection<string> StatusOptions { get; }
        public ObservableCollection<string> DeliveryOptions { get; }
        public ObservableCollection<NoticeItem> NoticeItems { get; } = new();
        public ObservableCollection<UpcomingEduItem> UpcomingEduItems { get; } = new();
        public bool HasUpcomingEdu => UpcomingEduItems.Count > 0;
        public ObservableCollection<TeamEventNoticeItem> TeamEventNoticeItems { get; } = new();
        public bool HasTeamEventNotice => TeamEventNoticeItems.Count > 0;
        public ObservableCollection<TeamStatusGroup> TeamStatusGroups { get; } = new();
        public ObservableCollection<string> RegisterModalAttachmentPaths { get; } = new();
        public ObservableCollection<string> EditModalAttachmentPaths { get; } = new();

        private string _todayText = "";
        public string TodayText { get => _todayText; set { _todayText = value; OnPropertyChanged(); } }

        private bool _isRegisterModalOpen;
        public bool IsRegisterModalOpen { get => _isRegisterModalOpen; set { _isRegisterModalOpen = value; OnPropertyChanged(); } }

        private bool _isDoneModalOpen;
        public bool IsDoneModalOpen { get => _isDoneModalOpen; set { _isDoneModalOpen = value; OnPropertyChanged(); } }

        private bool _isEditModalOpen;
        public bool IsEditModalOpen { get => _isEditModalOpen; set { _isEditModalOpen = value; OnPropertyChanged(); } }

        private bool _isNoticeModalOpen;
        public bool IsNoticeModalOpen { get => _isNoticeModalOpen; set { _isNoticeModalOpen = value; OnPropertyChanged(); } }

        private bool? _doneSelectAll;
        public bool? DoneSelectAll
        {
            get => _doneSelectAll;
            set { _doneSelectAll = value; OnPropertyChanged(); if (value.HasValue) foreach (var item in DoneItems) item.ManageChecked = value.Value; }
        }

        private HandoverItem? _currentEditItem;

        private string _editVendor = "";
        public string EditVendor
        {
            get => _editVendor;
            set
            {
                _editVendor = value;
                OnPropertyChanged();
                RefreshOwnerSuggestions(value);
            }
        }

        private string _editOwner = "";
        public string EditOwner { get => _editOwner; set { _editOwner = value; OnPropertyChanged(); } }

        private void RefreshVendorSuggestions()
        {
            _cachedVendors = VendorStore.Load().ToList();
            VendorSuggestions.Clear();
            foreach (var v in _cachedVendors.Where(v => !string.IsNullOrWhiteSpace(v.VendorName)).OrderBy(v => v.VendorName))
                VendorSuggestions.Add(v.VendorName!);
            RefreshOwnerSuggestions(_editVendor);
        }

        private void RefreshOwnerSuggestions(string vendorName)
        {
            OwnerSuggestions.Clear();
            var match = _cachedVendors.FirstOrDefault(v =>
                string.Equals(v.VendorName, vendorName?.Trim(), StringComparison.OrdinalIgnoreCase));

            IEnumerable<string> names;
            if (match != null && match.Managers.Count > 0)
                names = match.Managers.Select(m => m.ManagerName).Where(n => !string.IsNullOrWhiteSpace(n));
            else
                names = _cachedVendors.SelectMany(v => v.Managers)
                    .Select(m => m.ManagerName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n);

            foreach (var name in names)
                OwnerSuggestions.Add(name);
        }

        private string _editContent = "";
        public string EditContent { get => _editContent; set { _editContent = value; OnPropertyChanged(); } }

        private DateTime? _editInDate;
        public DateTime? EditInDate { get => _editInDate; set { _editInDate = value; OnPropertyChanged(); RefreshTopProgressPreview(); } }

        private DateTime? _editOutDate;
        public DateTime? EditOutDate { get => _editOutDate; set { _editOutDate = value; OnPropertyChanged(); RefreshTopProgressPreview(); } }

        private string _editStatus = "진행";
        public string EditStatus { get => _editStatus; set { _editStatus = value; OnPropertyChanged(); RefreshTopProgressPreview(); } }

        private string _editDeliveryMethod = "➖ 미정";
        public string EditDeliveryMethod { get => _editDeliveryMethod; set { _editDeliveryMethod = value; OnPropertyChanged(); } }

        private string _editMemo = "";
        public string EditMemo { get => _editMemo; set { _editMemo = value; OnPropertyChanged(); } }

        private int _editProgressPercent;
        public int EditProgressPercent { get => _editProgressPercent; set { _editProgressPercent = value; OnPropertyChanged(); } }

        private string _editProgressText = "0%";
        public string EditProgressText { get => _editProgressText; set { _editProgressText = value; OnPropertyChanged(); } }

        private void BtnToggleDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (TopDashboardArea.Visibility == Visibility.Visible) { TopDashboardArea.Visibility = Visibility.Collapsed; BtnToggleDashboard.Content = "대시보드 펴기 ▼"; }
            else { TopDashboardArea.Visibility = Visibility.Visible; BtnToggleDashboard.Content = "대시보드 접기 ▲"; }
        }

        private void LoadTodayStatus()
        {
            TeamStatusGroups.Clear();
            try
            {
                DateTime today = DateTime.Today;
                var shifts = DatabaseHelper.GetShiftSchedulesByDate(today);
                var edus = DatabaseHelper.GetEducationPlansByDate(today);
                var allUsers = AuthDatabaseHelper.GetAllUsers();
                string[] targetTeams = { "김팀", "장팀", "주간팀", "Office" };

                foreach (var tName in targetTeams)
                {
                    var group = new TeamStatusGroup { TeamName = tName };
                    var teamUsers = allUsers.Where(u => u.TeamName == tName).Select(u => u.RealName).ToList();
                    var tShifts = shifts.Where(s => teamUsers.Contains(s.MemberName) || s.TeamGroup == tName).ToList();
                    var tEdus = edus.Where(e => teamUsers.Contains(e.MemberName)).ToList();

                    var dayShifts = tShifts.Where(s => s.ShiftType == "주간" || s.ShiftType == "예상:주간").Select(s => s.MemberName).Distinct().ToList();
                    if (dayShifts.Count > 0) group.StatusList.Add(new TodayStatusItem { BadgeText = $"주간 ({dayShifts.Count})", BackgroundBrush = new SolidColorBrush(Color.FromRgb(254, 243, 199)), TextBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6)), MembersText = FormatMembersText(dayShifts) });

                    var nightShifts = tShifts.Where(s => s.ShiftType == "야간" || s.ShiftType == "예상:야간").Select(s => s.MemberName).Distinct().ToList();
                    if (nightShifts.Count > 0) group.StatusList.Add(new TodayStatusItem { BadgeText = $"야간 ({nightShifts.Count})", BackgroundBrush = new SolidColorBrush(Color.FromRgb(224, 231, 255)), TextBrush = new SolidColorBrush(Color.FromRgb(67, 56, 202)), MembersText = FormatMembersText(nightShifts) });

                    var offShifts = tShifts.Where(s => s.ShiftType.Contains("휴무") || s.ShiftType.Contains("연차") || s.ShiftType.Contains("반차")).ToList();
                    if (offShifts.Count > 0) group.StatusList.Add(new TodayStatusItem { BadgeText = $"휴무 ({offShifts.Count})", BackgroundBrush = new SolidColorBrush(Color.FromRgb(243, 232, 255)), TextBrush = new SolidColorBrush(Color.FromRgb(126, 34, 206)), MembersText = FormatMembersText(offShifts.Select(s => s.ShiftType == "연차" || s.ShiftType == "휴무" ? s.MemberName : $"{s.MemberName} ({s.ShiftType})").ToList()) });

                    if (tEdus.Count > 0) group.StatusList.Add(new TodayStatusItem { BadgeText = $"교육 ({tEdus.Count})", BackgroundBrush = new SolidColorBrush(Color.FromRgb(236, 252, 203)), TextBrush = new SolidColorBrush(Color.FromRgb(101, 163, 13)), MembersText = FormatMembersText(tEdus.Select(e => $"{e.MemberName} ({e.CourseName})").ToList()) });

                    if (group.StatusList.Count == 0) group.StatusList.Add(new TodayStatusItem { BadgeText = "상태", BackgroundBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249)), TextBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139)), MembersText = "일정 없음" });
                    TeamStatusGroups.Add(group);
                }
                if (TodayStatusList != null) TodayStatusList.ItemsSource = TeamStatusGroups;
            }
            catch { }
        }

        private string FormatMembersText(List<string> members) { if (members.Count == 0) return ""; if (members.Count <= 4) return string.Join(", ", members); return $"{string.Join(", ", members.Take(3))} 외 {members.Count - 3}명"; }

        private void SearchVendorBox_TextChanged(object sender, TextChangedEventArgs e) { _activeSearchKeyword = SearchVendorBox.Text?.Trim() ?? ""; _activeView?.Refresh(); }
        private void DoneSearchVendorBox_TextChanged(object sender, TextChangedEventArgs e) { _doneSearchKeyword = DoneSearchVendorBox.Text?.Trim() ?? ""; _doneView?.Refresh(); }

        private void FilterTab_Checked(object sender, RoutedEventArgs e) { if (sender is RadioButton rb && rb.Content != null) _currentFilter = rb.Content.ToString() ?? "전체"; _activeView?.Refresh(); _doneView?.Refresh(); }

        private void ToggleDueToday_Click(object sender, RoutedEventArgs e) { if (ToggleDueToday.IsChecked == true) ToggleDueTomorrow.IsChecked = false; _activeView?.Refresh(); _doneView?.Refresh(); }
        private void ToggleDueTomorrow_Click(object sender, RoutedEventArgs e) { if (ToggleDueTomorrow.IsChecked == true) ToggleDueToday.IsChecked = false; _activeView?.Refresh(); _doneView?.Refresh(); }

        private bool FilterActiveItem(object obj)
        {
            if (obj is HandoverItem item)
            {
                bool categoryMatch = _currentFilter == "전체" || string.Equals(item.Category, _currentFilter, StringComparison.OrdinalIgnoreCase) || (_currentFilter == "삼성" && item.Vendor.Contains("삼성", StringComparison.OrdinalIgnoreCase));
                bool filterByToday = ToggleDueToday?.IsChecked == true;
                bool filterByTomorrow = ToggleDueTomorrow?.IsChecked == true;
                bool dateMatch = true;
                if (filterByToday || filterByTomorrow) { dateMatch = false; if (item.OutDate.HasValue) { if (filterByToday && item.OutDate.Value.Date <= DateTime.Today) dateMatch = true; if (filterByTomorrow && item.OutDate.Value.Date == DateTime.Today.AddDays(1)) dateMatch = true; } }
                bool searchMatch = string.IsNullOrWhiteSpace(_activeSearchKeyword) || item.Vendor.Contains(_activeSearchKeyword, StringComparison.OrdinalIgnoreCase);
                return categoryMatch && dateMatch && searchMatch;
            }
            return false;
        }

        private bool FilterDoneItem(object obj) { if (obj is HandoverItem item) { bool categoryMatch = _currentFilter == "전체" || string.Equals(item.Category, _currentFilter, StringComparison.OrdinalIgnoreCase) || (_currentFilter == "삼성" && item.Vendor.Contains("삼성", StringComparison.OrdinalIgnoreCase)); bool searchMatch = string.IsNullOrWhiteSpace(_doneSearchKeyword) || item.Vendor.Contains(_doneSearchKeyword, StringComparison.OrdinalIgnoreCase); return categoryMatch && searchMatch; } return false; }

        private string DetermineCategory(string vendorName, string? dbCategory) { if (!string.IsNullOrWhiteSpace(dbCategory)) return dbCategory; if (vendorName.Contains("삼성", StringComparison.OrdinalIgnoreCase)) return "삼성"; if (vendorName.Contains("SEMES", StringComparison.OrdinalIgnoreCase) || vendorName.Contains("세메스", StringComparison.OrdinalIgnoreCase)) return "SEMES"; return "QTZ"; }

        private void LoadNotices()
        {
            NoticeItems.Clear();
            try { string path = Path.Combine(AppPaths.DataRoot, "office_notice.json"); if (File.Exists(path)) { string json = File.ReadAllText(path, Encoding.UTF8); var list = JsonSerializer.Deserialize<List<NoticeItem>>(json); if (list != null) foreach (var item in list) NoticeItems.Add(item); } }
            catch { }
        }

        private void LoadUpcomingEdu()
        {
            UpcomingEduItems.Clear();
            var today = DateTime.Today;
            try
            {
                var plans = DatabaseHelper.GetEducationPlansInRange(today, today.AddDays(7));
                var users = AuthDatabaseHelper.GetAllUsers();
                foreach (var e in plans
                    .Where(e => e.Status != "완료" && e.Status != "취소" && e.StartDate.Date >= today)
                    .OrderBy(e => e.StartDate))
                {
                    int d = (e.StartDate.Date - today).Days;
                    string label = d == 0 ? "D-Day" : $"D-{d}";
                    System.Windows.Media.Brush bg, fg;
                    if (d == 0)      { bg = HexBrush("#FEE2E2"); fg = HexBrush("#DC2626"); }
                    else if (d <= 2) { bg = HexBrush("#FEF3C7"); fg = HexBrush("#D97706"); }
                    else if (d <= 5) { bg = HexBrush("#DBEAFE"); fg = HexBrush("#2563EB"); }
                    else             { bg = HexBrush("#DCFCE7"); fg = HexBrush("#16A34A"); }

                    var korCul = new System.Globalization.CultureInfo("ko-KR");
                    string startDow = korCul.DateTimeFormat.GetAbbreviatedDayName(e.StartDate.DayOfWeek);
                    string endDow   = korCul.DateTimeFormat.GetAbbreviatedDayName(e.EndDate.DayOfWeek);
                    string dateRange = e.StartDate.Date == e.EndDate.Date
                        ? $"{e.StartDate:MM-dd} ({startDow})"
                        : $"{e.StartDate:MM-dd} ({startDow}) ~ {e.EndDate:MM-dd} ({endDow})";

                    var user = users.FirstOrDefault(u => u.RealName == e.MemberName);
                    UpcomingEduItems.Add(new UpcomingEduItem
                    {
                        DaysLabel    = label,
                        DaysBg       = bg,
                        DaysFg       = fg,
                        MemberName   = e.MemberName,
                        TeamName     = user?.TeamName ?? "-",
                        CourseName   = e.CourseName,
                        DateRangeStr = dateRange,
                        EduMethod    = e.EduMethod ?? ""
                    });
                }
            }
            catch { }
            OnPropertyChanged(nameof(HasUpcomingEdu));
        }

        private void SaveNotices() { try { Directory.CreateDirectory(AppPaths.DataRoot); string path = Path.Combine(AppPaths.DataRoot, "office_notice.json"); string json = JsonSerializer.Serialize(NoticeItems.ToList(), new JsonSerializerOptions { WriteIndented = true }); File.WriteAllText(path, json, Encoding.UTF8); } catch { } }

        public void TryRefresh() { if (IsRegisterModalOpen || IsEditModalOpen) return; LoadHandoverAll(); RefreshVendorSuggestions(); if (!_weeklyMode) { LoadUpcomingEdu(); LoadUpcomingTeamEvents(); } }

        private void LoadUpcomingTeamEvents()
        {
            TeamEventNoticeItems.Clear();
            var today = DateTime.Today;
            try
            {
                var events = DatabaseHelper.GetTeamEventsInRange(today.AddMonths(-1), today.AddYears(1));
                var korCulture = new System.Globalization.CultureInfo("ko-KR");
                foreach (var te in events.OrderBy(t => t.StartDate))
                {
                    DateTime start = DateTime.Parse(te.StartDate);
                    DateTime end = DateTime.Parse(te.EndDate);

                    string startDow = korCulture.DateTimeFormat.GetAbbreviatedDayName(start.DayOfWeek);
                    string endDow   = korCulture.DateTimeFormat.GetAbbreviatedDayName(end.DayOfWeek);
                    string dateLabel = start.Date == end.Date
                        ? $"{start:MM-dd} ({startDow})"
                        : $"{start:MM-dd} ({startDow}) ~ {end:MM-dd} ({endDow})";

                    System.Windows.Media.Brush bg, fg;
                    int daysUntil = (start.Date - today).Days;
                    if (daysUntil < 0)      { bg = HexBrush("#F3F4F6"); fg = HexBrush("#9CA3AF"); }
                    else if (daysUntil == 0) { bg = HexBrush("#FEE2E2"); fg = HexBrush("#DC2626"); }
                    else if (daysUntil <= 3) { bg = HexBrush("#FEF3C7"); fg = HexBrush("#D97706"); }
                    else                     { bg = HexBrush("#DBEAFE"); fg = HexBrush("#1D4ED8"); }

                    string statusLabel = daysUntil < 0 ? "완료" : daysUntil == 0 ? "오늘" : $"D-{daysUntil}";

                    TeamEventNoticeItems.Add(new TeamEventNoticeItem
                    {
                        Content = te.Content,
                        Detail = te.Detail ?? "",
                        DateLabel = dateLabel,
                        StatusBg = bg,
                        StatusFg = fg,
                        StatusLabel = statusLabel
                    });
                }
            }
            catch { }
            OnPropertyChanged(nameof(HasTeamEventNotice));
        }
        public void OpenNoticeModal() { if (AuthManager.CheckAuth(PermissionType.Notices)) IsNoticeModalOpen = true; }
        private void CloseNoticeModal_Click(object sender, RoutedEventArgs e) => IsNoticeModalOpen = false;
        private void AddNotice_Click(object sender, RoutedEventArgs e) { string text = NoticeInputBox?.Text?.Trim() ?? ""; if (string.IsNullOrWhiteSpace(text)) return; NoticeItems.Add(new NoticeItem { Text = text }); if (NoticeInputBox != null) NoticeInputBox.Text = ""; SaveNotices(); }
        private void DeleteNotice_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is NoticeItem item) { NoticeItems.Remove(item); SaveNotices(); } }
        private void NoticeInputBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { e.Handled = true; AddNotice_Click(sender, e); } }

        private void RefreshTopProgressPreview() { if (string.Equals(EditStatus, "완료", StringComparison.OrdinalIgnoreCase)) { EditProgressPercent = 100; EditProgressText = "100%"; return; } EditProgressPercent = HandoverItem.CalcProgressPercent(DateTime.Today, EditInDate, EditOutDate); EditProgressText = $"{EditProgressPercent}%"; }

        private void HookManageCheckedEvents() { ActiveItems.CollectionChanged -= ActiveItems_CollectionChanged; ActiveItems.CollectionChanged += ActiveItems_CollectionChanged; }
        private void ActiveItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { if (e.OldItems != null) foreach (HandoverItem item in e.OldItems) item.PropertyChanged -= ActiveItem_PropertyChanged; if (e.NewItems != null) foreach (HandoverItem item in e.NewItems) item.PropertyChanged += ActiveItem_PropertyChanged; UpdateManageColumnVisibility(); }
        private void ActiveItem_PropertyChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(HandoverItem.ManageChecked)) UpdateManageColumnVisibility(); }
        private void UpdateManageColumnVisibility() { bool show = ActiveItems.Any(x => x.ManageChecked); if (ManageColumn != null) ManageColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed; }

        private void HookDoneCheckedEvents() { DoneItems.CollectionChanged -= DoneItems_CollectionChanged; DoneItems.CollectionChanged += DoneItems_CollectionChanged; foreach (var item in DoneItems) { item.PropertyChanged -= DoneItem_PropertyChanged; item.PropertyChanged += DoneItem_PropertyChanged; } }
        private void DoneItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { if (e.OldItems != null) foreach (HandoverItem item in e.OldItems) item.PropertyChanged -= DoneItem_PropertyChanged; if (e.NewItems != null) foreach (HandoverItem item in e.NewItems) item.PropertyChanged += DoneItem_PropertyChanged; UpdateDoneSelectAllState(); }
        private void DoneItem_PropertyChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(HandoverItem.ManageChecked)) UpdateDoneSelectAllState(); }
        private void UpdateDoneSelectAllState() { if (DoneItems.Count == 0) { DoneSelectAll = false; return; } bool all = DoneItems.All(x => x.ManageChecked); bool none = DoneItems.All(x => !x.ManageChecked); _doneSelectAll = all ? true : (none ? false : null); OnPropertyChanged(nameof(DoneSelectAll)); }

        private void LoadHandoverAll()
        {
            foreach (var it in ActiveItems) it.PropertyChanged -= ActiveItem_PropertyChanged;
            ActiveItems.Clear(); DoneItems.Clear();
            try
            {
                var vendors = VendorStore.Load();
                _weeklyVendorNames = new HashSet<string>(
                    vendors.Where(v => v.IsWeekly).Select(v => v.VendorName ?? ""),
                    StringComparer.OrdinalIgnoreCase);

                var dbItems = DatabaseHelper.GetAllHandovers();
                foreach (var item in dbItems)
                {
                    bool isWeekly = _weeklyVendorNames.Contains(item.Vendor);
                    if (_weeklyMode != isWeekly) continue; // weekly view shows only weekly; normal view excludes weekly

                    var match = vendors.FirstOrDefault(v => string.Equals(v.VendorName, item.Vendor, StringComparison.OrdinalIgnoreCase));
                    item.Category = DetermineCategory(item.Vendor, match?.Category);
                    item.ManageChecked = false;
                    item.NotifyProgress();
                    if (item.IsDone) DoneItems.Add(item); else ActiveItems.Add(item);
                }
                ReorderDone(); _activeView?.Refresh(); _doneView?.Refresh();
            }
            catch (Exception ex) { MessageBox.Show($"DB 데이터 로드 실패: {ex.Message}", "오류"); }
        }
        private void ReorderDone() { var ordered = DoneItems.OrderByDescending(x => x.OutDate ?? DateTime.MinValue).ToList(); DoneItems.Clear(); foreach (var item in ordered) DoneItems.Add(item); }

        private void ActiveGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ActiveGrid.SelectedItem is HandoverItem item) MarkAsRead(item); }
        private void DoneGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (DoneGrid.SelectedItem is HandoverItem item) MarkAsRead(item); }
        private void MarkAsRead(HandoverItem item) { if (!SessionManager.IsLoggedIn) return; string currentUser = SessionManager.CurrentRealName; if (item.IsNewUpdate) { string currentReadBy = item.ReadBy ?? ""; if (!currentReadBy.Contains(currentUser)) { item.ReadBy = currentReadBy + currentUser + ","; DatabaseHelper.UpdateHandoverReadBy(item.Id, item.ReadBy); } } }

        private void AppendDoneDeletedToExcel(HandoverItem item) { string path = AppPaths.DoneDeletedExcelPath; using var wb = File.Exists(path) ? new XLWorkbook(path) : new XLWorkbook(); var ws = wb.Worksheets.FirstOrDefault(x => x.Name == AppPaths.DoneDeletedSheet) ?? wb.AddWorksheet(AppPaths.DoneDeletedSheet); if (ws.LastRowUsed() == null) { ws.Cell(1, 1).Value = "삭제일시"; ws.Cell(1, 2).Value = "업체"; ws.Cell(1, 3).Value = "내용"; ws.Cell(1, 4).Value = "입고일"; ws.Cell(1, 5).Value = "출고일"; ws.Cell(1, 6).Value = "상태"; ws.Cell(1, 7).Value = "메모"; ws.Cell(1, 8).Value = "담당자"; ws.Row(1).Style.Font.Bold = true; } int row = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1; ws.Cell(row, 1).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); ws.Cell(row, 2).Value = item.Vendor; ws.Cell(row, 3).Value = item.Content; ws.Cell(row, 4).Value = item.InDate?.ToString("yyyy-MM-dd") ?? ""; ws.Cell(row, 5).Value = item.OutDate?.ToString("yyyy-MM-dd") ?? ""; ws.Cell(row, 6).Value = item.Status; ws.Cell(row, 7).Value = item.Memo; ws.Cell(row, 8).Value = item.Owner; ws.Columns().AdjustToContents(); wb.SaveAs(path); }

        private void BtnOpenRegister_Click(object sender, RoutedEventArgs e)
        {
            OpenRegisterModal();
        }

        public void OpenRegisterModal() { HandoverReset_Click(this, new RoutedEventArgs()); IsRegisterModalOpen = true; }
        public void OpenDoneModal() { UpdateDoneSelectAllState(); IsDoneModalOpen = true; }

        private void HandoverSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(EditVendor) && string.IsNullOrWhiteSpace(EditContent)) { MessageBox.Show("업체 또는 내용을 입력하세요.", "알림"); return; }
                var vendors = VendorStore.Load(); var match = vendors.FirstOrDefault(v => string.Equals(v.VendorName, EditVendor?.Trim(), StringComparison.OrdinalIgnoreCase));
                string category = DetermineCategory(EditVendor?.Trim() ?? "", match?.Category);

                var item = new HandoverItem
                {
                    Id = Guid.NewGuid(),
                    Vendor = EditVendor?.Trim() ?? "",
                    Owner = EditOwner?.Trim() ?? "",
                    Content = EditContent ?? "",
                    InDate = EditInDate,
                    OutDate = EditOutDate,
                    Status = EditStatus ?? "진행",
                    DeliveryMethod = EditDeliveryMethod ?? "➖ 미정",
                    Memo = BuildMemo(EditMemo ?? "", RegisterModalAttachmentPaths, EditDeliveryMethod ?? "➖ 미정"),
                    ManageChecked = false,
                    CreatorName = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : "알수없음",
                    CreateDate = DateTime.Now,
                    Category = category,
                    ReadBy = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName + "," : ""
                };

                item.NotifyProgress(); if (item.IsDone) DoneItems.Insert(0, item); else ActiveItems.Insert(0, item);
                DatabaseHelper.InsertHandover(item); _activeView?.Refresh(); _doneView?.Refresh(); HandoverReset_Click(sender, e); IsRegisterModalOpen = false;
            }
            catch (Exception ex) { MessageBox.Show($"저장 실패: {ex.Message}", "오류"); }
        }

        private void HandoverReset_Click(object sender, RoutedEventArgs e) { EditVendor = ""; EditOwner = ""; EditContent = ""; EditInDate = DateTime.Today; EditOutDate = DateTime.Today; EditStatus = "진행"; EditDeliveryMethod = "➖ 미정"; EditMemo = ""; RegisterModalAttachmentPaths.Clear(); EditModalAttachmentPaths.Clear(); RefreshTopProgressPreview(); }

        private void OpenEditModal(HandoverItem item)
        {
            _currentEditItem = item; EditVendor = item.Vendor; EditOwner = item.Owner; EditContent = item.Content; EditInDate = item.InDate; EditOutDate = item.OutDate; EditStatus = item.Status;
            EditDeliveryMethod = item.DeliveryMethod ?? "➖ 미정";
            EditMemo = ExtractMemoText(item.Memo); EditModalAttachmentPaths.Clear(); foreach (var path in ExtractAttachmentPaths(item.Memo)) EditModalAttachmentPaths.Add(path); RefreshTopProgressPreview(); IsEditModalOpen = true;
        }

        private void CloseRegisterModal_Click(object sender, RoutedEventArgs e) => IsRegisterModalOpen = false;
        private void CloseEditModal_Click(object sender, RoutedEventArgs e) => IsEditModalOpen = false;
        private void CloseDoneModal_Click(object sender, RoutedEventArgs e) => IsDoneModalOpen = false;

        private void ActiveGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if ((e.OriginalSource as DependencyObject) is DependencyObject source) { var row = FindAncestor<DataGridRow>(source); if (row?.Item is HandoverItem item) { OpenEditModal(item); e.Handled = true; } } }
        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject { while (current != null) { if (current is T target) return target; current = VisualTreeHelper.GetParent(current); } return null; }

        private void HandoverDelete_Click(object sender, RoutedEventArgs e) { var item = (sender as FrameworkElement)?.DataContext as HandoverItem; if (item == null) return; if (!item.ManageChecked) { MessageBox.Show("체크한 항목만 삭제할 수 있습니다.", "알림"); return; } if (MessageBox.Show("선택한 항목을 삭제할까요?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { ActiveItems.Remove(item); DatabaseHelper.DeleteHandover(item.Id); UpdateManageColumnVisibility(); } }

        private static bool IsImageFile(string path) { string ext = Path.GetExtension(path) ?? ""; return AllowedImageExtensions.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase)); }
        private static string GetImageRootDirectory() { string dir = Path.Combine(AppPaths.DataRoot, "handover_images"); Directory.CreateDirectory(dir); return dir; }

        // 🔥 수정: 정규식을 활용하여 에러 없이 깔끔하게 숨겨진 태그 제거 및 텍스트 추출
        private static string ExtractMemoText(string? memo)
        {
            if (string.IsNullOrWhiteSpace(memo)) return "";
            string text = memo;
            text = Regex.Replace(text, @"\[\[DELIVERY\]\].*?\[\[/DELIVERY\]\]\r?\n?", "");

            int iStart = text.IndexOf(ImageBlockStart, StringComparison.Ordinal);
            if (iStart >= 0) text = text.Substring(0, iStart);

            return text.Trim();
        }

        private static string BuildMemo(string? plainText, IEnumerable<string>? paths, string deliveryMethod)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(plainText)) sb.AppendLine(plainText.TrimEnd());

            sb.AppendLine($"[[DELIVERY]]{deliveryMethod}[[/DELIVERY]]");

            var cleanPaths = (paths ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (cleanPaths.Count > 0)
            {
                sb.AppendLine(ImageBlockStart);
                foreach (var path in cleanPaths) sb.AppendLine(path);
                sb.Append(ImageBlockEnd);
            }
            return sb.ToString();
        }

        private static List<string> ExtractAttachmentPaths(string? memo) { var result = new List<string>(); if (string.IsNullOrWhiteSpace(memo)) return result; int start = memo.IndexOf(ImageBlockStart, StringComparison.Ordinal); int end = memo.IndexOf(ImageBlockEnd, StringComparison.Ordinal); if (start < 0 || end < 0 || end <= start) return result; string block = memo.Substring(start + ImageBlockStart.Length, end - (start + ImageBlockStart.Length)); foreach (var line in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) { string trimmed = line.Trim(); if (!string.IsNullOrWhiteSpace(trimmed)) result.Add(trimmed); } return result; }
        private static List<string> SaveDroppedImages(HandoverItem item, IEnumerable<string>? sourceFiles) { string root = GetImageRootDirectory(); string itemKey = item.Id == Guid.Empty ? Guid.NewGuid().ToString("N") : item.Id.ToString("N"); string itemDir = Path.Combine(root, itemKey); Directory.CreateDirectory(itemDir); var saved = new List<string>(); if (sourceFiles == null) return saved; foreach (var source in sourceFiles.Where(IsImageFile)) { string ext = Path.GetExtension(source); string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}{ext}"; string dest = Path.Combine(itemDir, fileName); File.Copy(source, dest, true); saved.Add(Path.GetRelativePath(root, dest)); } return saved; }

        private static bool ClipboardHasSupportedImage() => Clipboard.ContainsImage();
        private static List<string> SaveClipboardImages(HandoverItem item) { var saved = new List<string>(); if (!ClipboardHasSupportedImage()) return saved; BitmapSource? image = Clipboard.GetImage(); if (image == null) return saved; string root = GetImageRootDirectory(); string itemKey = item.Id == Guid.Empty ? Guid.NewGuid().ToString("N") : item.Id.ToString("N"); string itemDir = Path.Combine(root, itemKey); Directory.CreateDirectory(itemDir); string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png"; string dest = Path.Combine(itemDir, fileName); using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(fs); } saved.Add(Path.GetRelativePath(root, dest)); return saved; }

        private void RegisterModalMemo_PreviewDragOver(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; e.Effects = (files != null && files.Any(IsImageFile)) ? DragDropEffects.Copy : DragDropEffects.None; } else { e.Effects = DragDropEffects.None; } e.Handled = true; }
        private void RegisterModalMemo_Drop(object sender, DragEventArgs e) { if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return; var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files == null) return; var tempItem = new HandoverItem { Id = Guid.NewGuid() }; var saved = SaveDroppedImages(tempItem, files.Where(IsImageFile)); foreach (var s in saved) RegisterModalAttachmentPaths.Add(s); e.Handled = true; }
        private void RegisterModalMemo_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return; if (!ClipboardHasSupportedImage()) return; var tempItem = new HandoverItem { Id = Guid.NewGuid() }; var saved = SaveClipboardImages(tempItem); foreach (var s in saved) RegisterModalAttachmentPaths.Add(s); e.Handled = true; }
        private void RegisterDeleteAttachment_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string path) RegisterModalAttachmentPaths.Remove(path); }

        private void DoneDeleteSelected_Click(object sender, RoutedEventArgs e) { var targets = DoneItems.Where(x => x.ManageChecked).ToList(); if (targets.Count == 0) return; if (MessageBox.Show($"선택한 완료 항목 {targets.Count}건을 삭제할까요?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { try { foreach (var item in targets) { AppendDoneDeletedToExcel(item); DoneItems.Remove(item); DatabaseHelper.DeleteHandover(item.Id); } UpdateDoneSelectAllState(); } catch (Exception ex) { MessageBox.Show($"삭제 실패: {ex.Message}", "오류"); } } }

        private void EditModalSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditItem == null) return;
            try
            {
                if (string.IsNullOrWhiteSpace(EditVendor) && string.IsNullOrWhiteSpace(EditContent)) { MessageBox.Show("업체 또는 내용을 입력하세요.", "알림"); return; }
                var vendors = VendorStore.Load(); var match = vendors.FirstOrDefault(v => string.Equals(v.VendorName, EditVendor?.Trim(), StringComparison.OrdinalIgnoreCase));
                _currentEditItem.Category = DetermineCategory(EditVendor?.Trim() ?? "", match?.Category);
                _currentEditItem.Vendor = EditVendor?.Trim() ?? ""; _currentEditItem.Owner = EditOwner?.Trim() ?? ""; _currentEditItem.Content = EditContent ?? "";
                _currentEditItem.InDate = EditInDate; _currentEditItem.OutDate = EditOutDate; _currentEditItem.Status = EditStatus ?? "진행";

                _currentEditItem.DeliveryMethod = EditDeliveryMethod ?? "➖ 미정";

                _currentEditItem.Memo = BuildMemo(EditMemo ?? "", EditModalAttachmentPaths, EditDeliveryMethod ?? "➖ 미정"); _currentEditItem.NotifyProgress();
                _currentEditItem.ModifierName = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : "알수없음"; _currentEditItem.ModifyDate = DateTime.Now; _currentEditItem.ReadBy = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName + "," : "";
                DatabaseHelper.UpdateHandover(_currentEditItem);
                if (_currentEditItem.IsDone) { if (ActiveItems.Contains(_currentEditItem)) ActiveItems.Remove(_currentEditItem); _currentEditItem.ManageChecked = false; if (!DoneItems.Contains(_currentEditItem)) DoneItems.Insert(0, _currentEditItem); ReorderDone(); }
                else { if (DoneItems.Contains(_currentEditItem)) DoneItems.Remove(_currentEditItem); if (!ActiveItems.Contains(_currentEditItem)) ActiveItems.Insert(0, _currentEditItem); }
                UpdateManageColumnVisibility(); _activeView?.Refresh(); _doneView?.Refresh(); IsEditModalOpen = false;
            }
            catch (Exception ex) { MessageBox.Show($"수정 실패: {ex.Message}", "오류"); }
        }

        private void EditModalMemo_PreviewDragOver(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; e.Effects = (files != null && files.Any(IsImageFile)) ? DragDropEffects.Copy : DragDropEffects.None; } else { e.Effects = DragDropEffects.None; } e.Handled = true; }
        private void EditModalMemo_Drop(object sender, DragEventArgs e) { if (_currentEditItem == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return; var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files == null) return; var saved = SaveDroppedImages(_currentEditItem, files.Where(IsImageFile)); foreach (var s in saved) EditModalAttachmentPaths.Add(s); e.Handled = true; }
        private void EditModalMemo_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return; if (_currentEditItem == null || !ClipboardHasSupportedImage()) return; var saved = SaveClipboardImages(_currentEditItem); foreach (var s in saved) EditModalAttachmentPaths.Add(s); e.Handled = true; }
        private void EditDeleteAttachment_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string path) EditModalAttachmentPaths.Remove(path); }
        private void DoneDelete_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is HandoverItem item) { if (MessageBox.Show("완료 항목을 삭제하면 Excel에 기록 후 제거됩니다. 삭제할까요?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { try { AppendDoneDeletedToExcel(item); DoneItems.Remove(item); DatabaseHelper.DeleteHandover(item.Id); } catch (Exception ex) { MessageBox.Show($"엑셀 저장 실패: {ex.Message}", "오류"); } } } }

        private void ModalThumbnail_Click(object sender, MouseButtonEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string path) { string root = System.IO.Path.Combine(AppPaths.DataRoot, "handover_images"); ShowImagePopup(System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path))); e.Handled = true; } }
        private void GridThumbnail_Click(object sender, MouseButtonEventArgs e) { if (sender is FrameworkElement element && element.DataContext is HandoverItem item && item.HasImage) { ShowImagePopup(item.FirstImagePath); e.Handled = true; } }
        private void ShowImagePopup(string imagePath) { try { var window = new Window { Title = "이미지 뷰어", Width = 1000, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)) }; var img = new Image { Source = new BitmapImage(new Uri(imagePath)), Stretch = Stretch.Uniform, Margin = new Thickness(20) }; window.Content = img; window.ShowDialog(); } catch (Exception ex) { MessageBox.Show($"이미지를 불러올 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error); } }

        private void BtnViewHistory_Click(object sender, RoutedEventArgs e) { var historyWin = new DispatchHistoryWindow { Owner = Window.GetWindow(this) }; historyWin.ShowDialog(); }

        private void BtnCreateDispatch_Click(object sender, RoutedEventArgs e)
        {
            var selectedHandoverItems = ActiveItems.Where(i => i.ManageChecked).ToList();
            if (selectedHandoverItems.Count == 0) { MessageBox.Show("배차할 항목을 진행 목록에서 먼저 선택(체크)해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var newDispatchItems = new ObservableCollection<DispatchItemModel>();
            foreach (var handoverItem in selectedHandoverItems) { var dispatchItem = new DispatchItemModel { VendorName = handoverItem.Vendor, IncomingDetails = handoverItem.Content, OutgoingDetails = "-", Note = "" }; dispatchItem.LoadComboboxData(); newDispatchItems.Add(dispatchItem); }
            var dispatchWin = new DispatchWindow(newDispatchItems) { Owner = Window.GetWindow(this) }; dispatchWin.ShowDialog();
            foreach (var item in selectedHandoverItems) { item.ManageChecked = false; }
            UpdateManageColumnVisibility();
        }
    }
}