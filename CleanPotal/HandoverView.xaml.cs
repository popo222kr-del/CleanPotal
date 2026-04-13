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

        private AutoSyncManager _noticeSyncManager;
        private AutoSyncManager _dbSyncManager;
        private AutoSyncManager _vendorSyncManager;

        private ICollectionView _activeView;
        private ICollectionView _doneView;
        private string _currentFilter = "전체";

        public HandoverView()
        {
            InitializeComponent();
            DataContext = this;
            StatusOptions = new ObservableCollection<string> { "진행", "보류", "완료" };
            TodayText = DateTime.Now.ToString("yyyy-MM-dd dddd");

            EditInDate = DateTime.Today;
            EditOutDate = DateTime.Today;
            EditStatus = "진행";

            HookManageCheckedEvents();
            HookDoneCheckedEvents();

            LoadNotices();
            LoadHandoverAll();
            LoadTodayStatus();

            _activeView = CollectionViewSource.GetDefaultView(ActiveItems);
            _activeView.Filter = FilterHandoverItem;
            ActiveGrid.ItemsSource = _activeView;

            _doneView = CollectionViewSource.GetDefaultView(DoneItems);
            _doneView.Filter = FilterHandoverItem;
            DoneGrid.ItemsSource = _doneView;

            UpdateManageColumnVisibility();
            RefreshTopProgressPreview();

            _noticeSyncManager = new AutoSyncManager(() => { Dispatcher.Invoke(() => { LoadNotices(); }); }, Path.Combine(AppPaths.DataRoot, "office_notice.json"));
            _dbSyncManager = new AutoSyncManager(() => { Dispatcher.Invoke(() => { LoadHandoverAll(); LoadTodayStatus(); }); }, Path.Combine(AppPaths.DataRoot, "dispatch.db"));
            _vendorSyncManager = new AutoSyncManager(() => { Dispatcher.Invoke(() => { LoadHandoverAll(); }); }, AppPaths.VendorsFilePath);

            this.Loaded += (s, e) => { _noticeSyncManager.Start(); _dbSyncManager.Start(); _vendorSyncManager.Start(); };
            this.Unloaded += (s, e) => { _noticeSyncManager.Stop(); _dbSyncManager.Stop(); _vendorSyncManager.Stop(); };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ObservableCollection<HandoverItem> ActiveItems { get; } = new();
        public ObservableCollection<HandoverItem> DoneItems { get; } = new();
        public ObservableCollection<string> StatusOptions { get; }
        public ObservableCollection<NoticeItem> NoticeItems { get; } = new();

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
            set
            {
                _doneSelectAll = value;
                OnPropertyChanged();
                if (value.HasValue)
                    foreach (var item in DoneItems) item.ManageChecked = value.Value;
            }
        }

        private HandoverItem? _currentEditItem;

        private string _editVendor = "";
        public string EditVendor { get => _editVendor; set { _editVendor = value; OnPropertyChanged(); } }

        private string _editOwner = "";
        public string EditOwner { get => _editOwner; set { _editOwner = value; OnPropertyChanged(); } }

        private string _editContent = "";
        public string EditContent { get => _editContent; set { _editContent = value; OnPropertyChanged(); } }

        private DateTime? _editInDate;
        public DateTime? EditInDate { get => _editInDate; set { _editInDate = value; OnPropertyChanged(); RefreshTopProgressPreview(); } }

        private DateTime? _editOutDate;
        public DateTime? EditOutDate { get => _editOutDate; set { _editOutDate = value; OnPropertyChanged(); RefreshTopProgressPreview(); } }

        private string _editStatus = "진행";
        public string EditStatus { get => _editStatus; set { _editStatus = value; OnPropertyChanged(); RefreshTopProgressPreview(); } }

        private string _editMemo = "";
        public string EditMemo { get => _editMemo; set { _editMemo = value; OnPropertyChanged(); } }

        private int _editProgressPercent;
        public int EditProgressPercent { get => _editProgressPercent; set { _editProgressPercent = value; OnPropertyChanged(); } }

        private string _editProgressText = "0%";
        public string EditProgressText { get => _editProgressText; set { _editProgressText = value; OnPropertyChanged(); } }

        // 🔥 신규: 대시보드 접기/펴기 로직
        private void BtnToggleDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (TopDashboardArea.Visibility == Visibility.Visible)
            {
                TopDashboardArea.Visibility = Visibility.Collapsed;
                BtnToggleDashboard.Content = "대시보드 펴기 ▼";
            }
            else
            {
                TopDashboardArea.Visibility = Visibility.Visible;
                BtnToggleDashboard.Content = "대시보드 접기 ▲";
            }
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

                string GetNameOnly(string name)
                {
                    return name;
                }

                string[] targetTeams = { "김팀", "장팀", "주간팀", "Office" };

                foreach (var tName in targetTeams)
                {
                    var group = new TeamStatusGroup { TeamName = tName };
                    var teamUsers = allUsers.Where(u => u.TeamName == tName).Select(u => u.RealName).ToList();

                    var tShifts = shifts.Where(s => teamUsers.Contains(s.MemberName) || s.TeamGroup == tName).ToList();
                    var tEdus = edus.Where(e => teamUsers.Contains(e.MemberName)).ToList();

                    var dayShifts = tShifts.Where(s => s.ShiftType == "주간" || s.ShiftType == "예상:주간").Select(s => s.MemberName).Distinct().ToList();
                    if (dayShifts.Count > 0)
                    {
                        var formattedNames = dayShifts.Select(GetNameOnly).ToList();
                        group.StatusList.Add(new TodayStatusItem
                        {
                            BadgeText = $"주간 ({dayShifts.Count})",
                            BackgroundBrush = new SolidColorBrush(Color.FromRgb(254, 243, 199)),
                            TextBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                            MembersText = FormatMembersText(formattedNames)
                        });
                    }

                    var nightShifts = tShifts.Where(s => s.ShiftType == "야간" || s.ShiftType == "예상:야간").Select(s => s.MemberName).Distinct().ToList();
                    if (nightShifts.Count > 0)
                    {
                        var formattedNames = nightShifts.Select(GetNameOnly).ToList();
                        group.StatusList.Add(new TodayStatusItem
                        {
                            BadgeText = $"야간 ({nightShifts.Count})",
                            BackgroundBrush = new SolidColorBrush(Color.FromRgb(224, 231, 255)),
                            TextBrush = new SolidColorBrush(Color.FromRgb(67, 56, 202)),
                            MembersText = FormatMembersText(formattedNames)
                        });
                    }

                    var offShifts = tShifts.Where(s => s.ShiftType.Contains("휴무") || s.ShiftType.Contains("연차") || s.ShiftType.Contains("반차")).ToList();
                    if (offShifts.Count > 0)
                    {
                        var offNames = offShifts.Select(s => s.ShiftType == "연차" || s.ShiftType == "휴무"
                            ? GetNameOnly(s.MemberName)
                            : $"{GetNameOnly(s.MemberName)} ({s.ShiftType})").ToList();

                        group.StatusList.Add(new TodayStatusItem
                        {
                            BadgeText = $"휴무 ({offShifts.Count})",
                            BackgroundBrush = new SolidColorBrush(Color.FromRgb(243, 232, 255)),
                            TextBrush = new SolidColorBrush(Color.FromRgb(126, 34, 206)),
                            MembersText = FormatMembersText(offNames)
                        });
                    }

                    if (tEdus.Count > 0)
                    {
                        var eduNames = tEdus.Select(e => $"{GetNameOnly(e.MemberName)} ({e.CourseName})").ToList();
                        group.StatusList.Add(new TodayStatusItem
                        {
                            BadgeText = $"교육 ({tEdus.Count})",
                            BackgroundBrush = new SolidColorBrush(Color.FromRgb(236, 252, 203)),
                            TextBrush = new SolidColorBrush(Color.FromRgb(101, 163, 13)),
                            MembersText = FormatMembersText(eduNames)
                        });
                    }

                    if (group.StatusList.Count == 0)
                    {
                        group.StatusList.Add(new TodayStatusItem
                        {
                            BadgeText = "상태",
                            BackgroundBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                            TextBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                            MembersText = "일정 없음"
                        });
                    }

                    TeamStatusGroups.Add(group);
                }

                if (TodayStatusList != null)
                {
                    TodayStatusList.ItemsSource = TeamStatusGroups;
                }
            }
            catch { }
        }

        private string FormatMembersText(List<string> members)
        {
            if (members.Count == 0) return "";
            if (members.Count <= 4) return string.Join(", ", members);
            return $"{string.Join(", ", members.Take(3))} 외 {members.Count - 3}명";
        }

        // 🔥 변경: 라디오 버튼 및 퀵 필터(토글 버튼) 이벤트 병합
        private void FilterTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Content != null)
            {
                _currentFilter = rb.Content.ToString() ?? "전체";
            }
            _activeView?.Refresh();
            _doneView?.Refresh();
        }

        // 🔥 변경: 오늘 마감 퀵 필터 로직 추가
        private bool FilterHandoverItem(object obj)
        {
            if (obj is HandoverItem item)
            {
                bool categoryMatch = _currentFilter == "전체" || string.Equals(item.Category, _currentFilter, StringComparison.OrdinalIgnoreCase);

                bool dueTodayMatch = true;
                if (ToggleDueToday?.IsChecked == true)
                {
                    // 오늘 출고 예정이거나 이미 지난(지연된) 건 모두 표출
                    dueTodayMatch = item.OutDate.HasValue && item.OutDate.Value.Date <= DateTime.Today;
                }

                return categoryMatch && dueTodayMatch;
            }
            return false;
        }

        private void LoadNotices()
        {
            NoticeItems.Clear();
            try
            {
                string path = Path.Combine(AppPaths.DataRoot, "office_notice.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var list = JsonSerializer.Deserialize<List<NoticeItem>>(json);
                    if (list != null) foreach (var item in list) NoticeItems.Add(item);
                }
                else
                {
                    NoticeItems.Add(new NoticeItem { Text = "포장 시 RUNSHEET, 라벨, 성적서 확인 후 작업 진행." });
                    NoticeItems.Add(new NoticeItem { Text = "A급 제품 스토커 적재 전 클리닝 후 적재." });
                }
            }
            catch { }
        }

        private void SaveNotices()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataRoot);
                string path = Path.Combine(AppPaths.DataRoot, "office_notice.json");
                string json = JsonSerializer.Serialize(NoticeItems.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch { }
        }

        public void OpenNoticeModal()
        {
            if (AuthManager.CheckAuth(PermissionType.Notices))
            {
                IsNoticeModalOpen = true;
            }
        }

        private void CloseNoticeModal_Click(object sender, RoutedEventArgs e) => IsNoticeModalOpen = false;

        private void AddNotice_Click(object sender, RoutedEventArgs e)
        {
            string text = NoticeInputBox?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text)) return;
            NoticeItems.Add(new NoticeItem { Text = text });
            if (NoticeInputBox != null) NoticeInputBox.Text = "";
            SaveNotices();
        }

        private void DeleteNotice_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is NoticeItem item)
            {
                NoticeItems.Remove(item);
                SaveNotices();
            }
        }

        private void NoticeInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                AddNotice_Click(sender, e);
            }
        }

        private void RefreshTopProgressPreview()
        {
            if (string.Equals(EditStatus, "완료", StringComparison.OrdinalIgnoreCase))
            {
                EditProgressPercent = 100;
                EditProgressText = "100%";
                return;
            }
            EditProgressPercent = HandoverItem.CalcProgressPercent(DateTime.Today, EditInDate, EditOutDate);
            EditProgressText = $"{EditProgressPercent}%";
        }

        private void HookManageCheckedEvents()
        {
            ActiveItems.CollectionChanged -= ActiveItems_CollectionChanged;
            ActiveItems.CollectionChanged += ActiveItems_CollectionChanged;
        }

        private void ActiveItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (HandoverItem item in e.OldItems) item.PropertyChanged -= ActiveItem_PropertyChanged;
            if (e.NewItems != null) foreach (HandoverItem item in e.NewItems) item.PropertyChanged += ActiveItem_PropertyChanged;
            UpdateManageColumnVisibility();
        }

        private void ActiveItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HandoverItem.ManageChecked)) UpdateManageColumnVisibility();
        }

        private void UpdateManageColumnVisibility()
        {
            bool show = ActiveItems.Any(x => x.ManageChecked);
            if (ManageColumn != null) ManageColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HookDoneCheckedEvents()
        {
            DoneItems.CollectionChanged -= DoneItems_CollectionChanged;
            DoneItems.CollectionChanged += DoneItems_CollectionChanged;
            foreach (var item in DoneItems)
            {
                item.PropertyChanged -= DoneItem_PropertyChanged;
                item.PropertyChanged += DoneItem_PropertyChanged;
            }
        }

        private void DoneItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (HandoverItem item in e.OldItems) item.PropertyChanged -= DoneItem_PropertyChanged;
            if (e.NewItems != null) foreach (HandoverItem item in e.NewItems) item.PropertyChanged += DoneItem_PropertyChanged;
            UpdateDoneSelectAllState();
        }

        private void DoneItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HandoverItem.ManageChecked)) UpdateDoneSelectAllState();
        }

        private void UpdateDoneSelectAllState()
        {
            if (DoneItems.Count == 0) { DoneSelectAll = false; return; }
            bool all = DoneItems.All(x => x.ManageChecked);
            bool none = DoneItems.All(x => !x.ManageChecked);
            _doneSelectAll = all ? true : (none ? false : null);
            OnPropertyChanged(nameof(DoneSelectAll));
        }

        private void LoadHandoverAll()
        {
            foreach (var it in ActiveItems) it.PropertyChanged -= ActiveItem_PropertyChanged;
            ActiveItems.Clear();
            DoneItems.Clear();

            try
            {
                var vendors = VendorStore.Load();
                var dbItems = DatabaseHelper.GetAllHandovers();

                foreach (var item in dbItems)
                {
                    var match = vendors.FirstOrDefault(v => string.Equals(v.VendorName, item.Vendor, StringComparison.OrdinalIgnoreCase));
                    item.Category = (match != null && string.Equals(match.Category, "SEMES", StringComparison.OrdinalIgnoreCase)) ? "SEMES" : "QTZ";

                    item.ManageChecked = false;
                    item.NotifyProgress();
                    if (item.IsDone) DoneItems.Add(item);
                    else ActiveItems.Add(item);
                }
                ReorderDone();

                _activeView?.Refresh();
                _doneView?.Refresh();
            }
            catch (Exception ex) { MessageBox.Show($"DB 데이터 로드 실패: {ex.Message}", "오류"); }
        }

        private void ReorderDone()
        {
            var ordered = DoneItems.OrderByDescending(x => x.OutDate ?? DateTime.MinValue).ToList();
            DoneItems.Clear();
            foreach (var item in ordered) DoneItems.Add(item);
        }

        private void ActiveGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ActiveGrid.SelectedItem is HandoverItem item)
            {
                MarkAsRead(item);
            }
        }

        private void DoneGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DoneGrid.SelectedItem is HandoverItem item)
            {
                MarkAsRead(item);
            }
        }

        private void MarkAsRead(HandoverItem item)
        {
            if (!SessionManager.IsLoggedIn) return;
            string currentUser = SessionManager.CurrentRealName;

            if (item.IsNewUpdate)
            {
                string currentReadBy = item.ReadBy ?? "";
                if (!currentReadBy.Contains(currentUser))
                {
                    item.ReadBy = currentReadBy + currentUser + ",";
                    DatabaseHelper.UpdateHandoverReadBy(item.Id, item.ReadBy);
                }
            }
        }

        private void AppendDoneDeletedToExcel(HandoverItem item)
        {
            string path = AppPaths.DoneDeletedExcelPath;
            using var wb = File.Exists(path) ? new XLWorkbook(path) : new XLWorkbook();
            var ws = wb.Worksheets.FirstOrDefault(x => x.Name == AppPaths.DoneDeletedSheet) ?? wb.AddWorksheet(AppPaths.DoneDeletedSheet);
            if (ws.LastRowUsed() == null)
            {
                ws.Cell(1, 1).Value = "삭제일시"; ws.Cell(1, 2).Value = "업체"; ws.Cell(1, 3).Value = "내용";
                ws.Cell(1, 4).Value = "입고일"; ws.Cell(1, 5).Value = "출고일"; ws.Cell(1, 6).Value = "상태";
                ws.Cell(1, 7).Value = "메모"; ws.Cell(1, 8).Value = "담당자"; ws.Row(1).Style.Font.Bold = true;
            }
            int row = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
            ws.Cell(row, 1).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cell(row, 2).Value = item.Vendor; ws.Cell(row, 3).Value = item.Content;
            ws.Cell(row, 4).Value = item.InDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 5).Value = item.OutDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 6).Value = item.Status; ws.Cell(row, 7).Value = item.Memo;
            ws.Cell(row, 8).Value = item.Owner;
            ws.Columns().AdjustToContents();
            wb.SaveAs(path);
        }

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
                if (string.IsNullOrWhiteSpace(EditVendor) && string.IsNullOrWhiteSpace(EditContent))
                {
                    MessageBox.Show("업체 또는 내용을 입력하세요.", "알림"); return;
                }

                var vendors = VendorStore.Load();
                var match = vendors.FirstOrDefault(v => string.Equals(v.VendorName, EditVendor?.Trim(), StringComparison.OrdinalIgnoreCase));
                string category = (match != null && string.Equals(match.Category, "SEMES", StringComparison.OrdinalIgnoreCase)) ? "SEMES" : "QTZ";

                var item = new HandoverItem
                {
                    Id = Guid.NewGuid(),
                    Vendor = EditVendor?.Trim() ?? "",
                    Owner = EditOwner?.Trim() ?? "",
                    Content = EditContent ?? "",
                    InDate = EditInDate,
                    OutDate = EditOutDate,
                    Status = EditStatus ?? "진행",
                    Memo = BuildMemo(EditMemo ?? "", RegisterModalAttachmentPaths),
                    ManageChecked = false,
                    CreatorName = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : "알수없음",
                    CreateDate = DateTime.Now,
                    Category = category,

                    ReadBy = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName + "," : ""
                };

                item.NotifyProgress();
                if (item.IsDone) DoneItems.Insert(0, item);
                else ActiveItems.Insert(0, item);

                DatabaseHelper.InsertHandover(item);

                _activeView?.Refresh();
                _doneView?.Refresh();

                HandoverReset_Click(sender, e);
                IsRegisterModalOpen = false;
            }
            catch (Exception ex) { MessageBox.Show($"저장 실패: {ex.Message}", "오류"); }
        }

        private void HandoverReset_Click(object sender, RoutedEventArgs e)
        {
            EditVendor = ""; EditOwner = ""; EditContent = ""; EditInDate = DateTime.Today; EditOutDate = DateTime.Today; EditStatus = "진행"; EditMemo = "";
            RegisterModalAttachmentPaths.Clear(); EditModalAttachmentPaths.Clear(); RefreshTopProgressPreview();
        }

        private void OpenEditModal(HandoverItem item)
        {
            _currentEditItem = item;
            EditVendor = item.Vendor; EditOwner = item.Owner; EditContent = item.Content;
            EditInDate = item.InDate; EditOutDate = item.OutDate; EditStatus = item.Status; EditMemo = ExtractMemoText(item.Memo);
            EditModalAttachmentPaths.Clear(); foreach (var path in ExtractAttachmentPaths(item.Memo)) EditModalAttachmentPaths.Add(path);
            RefreshTopProgressPreview(); IsEditModalOpen = true;
        }

        private void CloseRegisterModal_Click(object sender, RoutedEventArgs e) => IsRegisterModalOpen = false;
        private void CloseEditModal_Click(object sender, RoutedEventArgs e) => IsEditModalOpen = false;
        private void CloseDoneModal_Click(object sender, RoutedEventArgs e) => IsDoneModalOpen = false;

        private void ActiveGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as DependencyObject) is DependencyObject source)
            {
                var row = FindAncestor<DataGridRow>(source);
                if (row?.Item is HandoverItem item) { OpenEditModal(item); e.Handled = true; }
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null) { if (current is T target) return target; current = VisualTreeHelper.GetParent(current); }
            return null;
        }

        private void HandoverDelete_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as HandoverItem;
            if (item == null) return;
            if (!item.ManageChecked) { MessageBox.Show("체크한 항목만 삭제할 수 있습니다.", "알림"); return; }
            if (MessageBox.Show("선택한 항목을 삭제할까요?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                ActiveItems.Remove(item); DatabaseHelper.DeleteHandover(item.Id); UpdateManageColumnVisibility();
            }
        }

        private static bool HasImageFiles(IDataObject data) { if (!data.GetDataPresent(DataFormats.FileDrop)) return false; var files = data.GetData(DataFormats.FileDrop) as string[]; return files != null && files.Any(IsImageFile); }
        private static bool IsImageFile(string path) { string ext = Path.GetExtension(path) ?? ""; return AllowedImageExtensions.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase)); }

        private static string GetImageRootDirectory() { string dir = Path.Combine(AppPaths.DataRoot, "handover_images"); Directory.CreateDirectory(dir); return dir; }

        private static List<string> ExtractAttachmentPaths(string? memo)
        {
            var result = new List<string>(); if (string.IsNullOrWhiteSpace(memo)) return result;
            int start = memo.IndexOf(ImageBlockStart, StringComparison.Ordinal); int end = memo.IndexOf(ImageBlockEnd, StringComparison.Ordinal);
            if (start < 0 || end < 0 || end <= start) return result;
            string block = memo.Substring(start + ImageBlockStart.Length, end - (start + ImageBlockStart.Length));
            foreach (var line in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim(); if (!string.IsNullOrWhiteSpace(trimmed)) result.Add(trimmed);
            }
            return result;
        }

        private static string ExtractMemoText(string? memo) { if (string.IsNullOrWhiteSpace(memo)) return ""; int start = memo.IndexOf(ImageBlockStart, StringComparison.Ordinal); if (start < 0) return memo; return memo.Substring(0, start).TrimEnd(); }
        private static string BuildMemo(string? plainText, IEnumerable<string>? paths) { string text = plainText ?? ""; var cleanPaths = (paths ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); if (cleanPaths.Count == 0) return text; var sb = new StringBuilder(); sb.Append(text.TrimEnd()); if (sb.Length > 0) sb.AppendLine(); sb.AppendLine(ImageBlockStart); foreach (var path in cleanPaths) sb.AppendLine(path); sb.Append(ImageBlockEnd); return sb.ToString(); }

        private static List<string> SaveDroppedImages(HandoverItem item, IEnumerable<string> sourceFiles)
        {
            string root = GetImageRootDirectory(); string itemKey = item.Id == Guid.Empty ? Guid.NewGuid().ToString("N") : item.Id.ToString("N");
            string itemDir = Path.Combine(root, itemKey); Directory.CreateDirectory(itemDir);
            var saved = new List<string>();
            foreach (var source in sourceFiles.Where(IsImageFile)) { string ext = Path.GetExtension(source); string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}{ext}"; string dest = Path.Combine(itemDir, fileName); File.Copy(source, dest, true); saved.Add(Path.GetRelativePath(root, dest)); }
            return saved;
        }

        private static bool ClipboardHasSupportedImage() => Clipboard.ContainsImage();
        private static List<string> SaveClipboardImages(HandoverItem item)
        {
            var saved = new List<string>(); if (!Clipboard.ContainsImage()) return saved; BitmapSource? image = Clipboard.GetImage(); if (image == null) return saved;
            string root = GetImageRootDirectory(); string itemKey = item.Id == Guid.Empty ? Guid.NewGuid().ToString("N") : item.Id.ToString("N");
            string itemDir = Path.Combine(root, itemKey); Directory.CreateDirectory(itemDir); string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png"; string dest = Path.Combine(itemDir, fileName);
            using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(fs); }
            saved.Add(Path.GetRelativePath(root, dest)); return saved;
        }

        private void RegisterModalMemo_PreviewDragOver(object sender, DragEventArgs e) { e.Effects = HasImageFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
        private void RegisterModalMemo_Drop(object sender, DragEventArgs e) { if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return; var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files == null) return; var tempItem = new HandoverItem { Id = Guid.NewGuid() }; var saved = SaveDroppedImages(tempItem, files.Where(IsImageFile)); foreach (var s in saved) RegisterModalAttachmentPaths.Add(s); e.Handled = true; }
        private void RegisterModalMemo_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return; if (!ClipboardHasSupportedImage()) return; var tempItem = new HandoverItem { Id = Guid.NewGuid() }; var saved = SaveClipboardImages(tempItem); foreach (var s in saved) RegisterModalAttachmentPaths.Add(s); e.Handled = true; }
        private void RegisterDeleteAttachment_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string path) RegisterModalAttachmentPaths.Remove(path); }

        private void DoneDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var targets = DoneItems.Where(x => x.ManageChecked).ToList(); if (targets.Count == 0) return;
            if (MessageBox.Show($"선택한 완료 항목 {targets.Count}건을 삭제할까요?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try { foreach (var item in targets) { AppendDoneDeletedToExcel(item); DoneItems.Remove(item); DatabaseHelper.DeleteHandover(item.Id); } UpdateDoneSelectAllState(); }
                catch (Exception ex) { MessageBox.Show($"삭제 실패: {ex.Message}", "오류"); }
            }
        }

        private void EditModalSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditItem == null) return;
            try
            {
                if (string.IsNullOrWhiteSpace(EditVendor) && string.IsNullOrWhiteSpace(EditContent)) { MessageBox.Show("업체 또는 내용을 입력하세요.", "알림"); return; }

                var vendors = VendorStore.Load();
                var match = vendors.FirstOrDefault(v => string.Equals(v.VendorName, EditVendor?.Trim(), StringComparison.OrdinalIgnoreCase));
                _currentEditItem.Category = (match != null && string.Equals(match.Category, "SEMES", StringComparison.OrdinalIgnoreCase)) ? "SEMES" : "QTZ";

                _currentEditItem.Vendor = EditVendor?.Trim() ?? ""; _currentEditItem.Owner = EditOwner?.Trim() ?? ""; _currentEditItem.Content = EditContent ?? "";
                _currentEditItem.InDate = EditInDate; _currentEditItem.OutDate = EditOutDate; _currentEditItem.Status = EditStatus ?? "진행";
                _currentEditItem.Memo = BuildMemo(EditMemo ?? "", EditModalAttachmentPaths); _currentEditItem.NotifyProgress();

                _currentEditItem.ModifierName = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : "알수없음";
                _currentEditItem.ModifyDate = DateTime.Now;

                _currentEditItem.ReadBy = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName + "," : "";

                DatabaseHelper.UpdateHandover(_currentEditItem);

                if (_currentEditItem.IsDone) { if (ActiveItems.Contains(_currentEditItem)) ActiveItems.Remove(_currentEditItem); _currentEditItem.ManageChecked = false; if (!DoneItems.Contains(_currentEditItem)) DoneItems.Insert(0, _currentEditItem); ReorderDone(); }
                else { if (DoneItems.Contains(_currentEditItem)) DoneItems.Remove(_currentEditItem); if (!ActiveItems.Contains(_currentEditItem)) ActiveItems.Insert(0, _currentEditItem); }

                UpdateManageColumnVisibility();

                _activeView?.Refresh();
                _doneView?.Refresh();

                IsEditModalOpen = false;
            }
            catch (Exception ex) { MessageBox.Show($"수정 실패: {ex.Message}", "오류"); }
        }

        private void EditModalMemo_PreviewDragOver(object sender, DragEventArgs e) { e.Effects = HasImageFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
        private void EditModalMemo_Drop(object sender, DragEventArgs e) { if (_currentEditItem == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return; var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files == null) return; var saved = SaveDroppedImages(_currentEditItem, files.Where(IsImageFile)); foreach (var s in saved) EditModalAttachmentPaths.Add(s); e.Handled = true; }
        private void EditModalMemo_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return; if (_currentEditItem == null || !ClipboardHasSupportedImage()) return; var saved = SaveClipboardImages(_currentEditItem); foreach (var s in saved) EditModalAttachmentPaths.Add(s); e.Handled = true; }
        private void EditDeleteAttachment_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string path) EditModalAttachmentPaths.Remove(path); }
        private void DoneDelete_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is HandoverItem item) { if (MessageBox.Show("완료 항목을 삭제하면 Excel에 기록 후 제거됩니다. 삭제할까요?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { try { AppendDoneDeletedToExcel(item); DoneItems.Remove(item); DatabaseHelper.DeleteHandover(item.Id); } catch (Exception ex) { MessageBox.Show($"엑셀 저장 실패: {ex.Message}", "오류"); } } } }

        private void ModalThumbnail_Click(object sender, MouseButtonEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string path) { string root = System.IO.Path.Combine(AppPaths.DataRoot, "handover_images"); ShowImagePopup(System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path))); e.Handled = true; } }
        private void GridThumbnail_Click(object sender, MouseButtonEventArgs e) { if (sender is FrameworkElement element && element.DataContext is HandoverItem item && item.HasImage) { ShowImagePopup(item.FirstImagePath); e.Handled = true; } }
        private void ShowImagePopup(string imagePath) { try { var window = new Window { Title = "이미지 뷰어", Width = 1000, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)) }; var img = new Image { Source = new BitmapImage(new Uri(imagePath)), Stretch = Stretch.Uniform, Margin = new Thickness(20) }; window.Content = img; window.ShowDialog(); } catch (Exception ex) { MessageBox.Show($"이미지를 불러올 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error); } }

        private void BtnViewHistory_Click(object sender, RoutedEventArgs e)
        {
            var historyWin = new DispatchHistoryWindow { Owner = Window.GetWindow(this) };
            historyWin.ShowDialog();
        }

        private void BtnCreateDispatch_Click(object sender, RoutedEventArgs e)
        {
            var selectedHandoverItems = ActiveItems.Where(i => i.ManageChecked).ToList();
            if (selectedHandoverItems.Count == 0)
            {
                MessageBox.Show("배차할 항목을 진행 목록에서 먼저 선택(체크)해 주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newDispatchItems = new ObservableCollection<DispatchItemModel>();
            foreach (var handoverItem in selectedHandoverItems)
            {
                var dispatchItem = new DispatchItemModel
                {
                    VendorName = handoverItem.Vendor,
                    IncomingDetails = handoverItem.Content,
                    OutgoingDetails = "-",
                    Note = ""
                };
                dispatchItem.LoadComboboxData();
                newDispatchItems.Add(dispatchItem);
            }

            var dispatchWin = new DispatchWindow(newDispatchItems) { Owner = Window.GetWindow(this) };
            dispatchWin.ShowDialog();

            foreach (var item in selectedHandoverItems)
            {
                item.ManageChecked = false;
            }

            UpdateManageColumnVisibility();
        }
    }
}