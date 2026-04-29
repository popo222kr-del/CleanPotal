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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CleanPotal
{
    public class ProdReqItem : INotifyPropertyChanged
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        private bool _manageChecked;
        public bool ManageChecked { get => _manageChecked; set { _manageChecked = value; OnPropertyChanged(); } }

        private DateTime? _requestDate;
        public DateTime? RequestDate { get => _requestDate; set { _requestDate = value; OnPropertyChanged(); } }

        private DateTime? _dueDate;
        public DateTime? DueDate { get => _dueDate; set { _dueDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); OnPropertyChanged(nameof(IsDelayed)); OnPropertyChanged(nameof(StatusForeground)); OnPropertyChanged(nameof(StatusBackground)); } }

        private string _status = "진행";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); OnPropertyChanged(nameof(IsDelayed)); OnPropertyChanged(nameof(StatusForeground)); OnPropertyChanged(nameof(StatusBackground)); } }

        private string _category = "";
        public string Category { get => _category; set { _category = value; OnPropertyChanged(); } }

        private string _location = "";
        public string Location { get => _location; set { _location = value; OnPropertyChanged(); } }

        private string _requestDetail = "";
        public string RequestDetail
        {
            get => _requestDetail;
            set
            {
                _requestDetail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RequestContentTypeTag));
                OnPropertyChanged(nameof(RequestDetailText));
            }
        }

        public string RequestContentTypeTag
        {
            get
            {
                if (string.IsNullOrEmpty(RequestDetail)) return "";
                int idx = RequestDetail.IndexOf(']');
                if (RequestDetail.StartsWith("[") && idx > 0) return RequestDetail.Substring(0, idx + 1);
                return "";
            }
        }

        public string RequestDetailText
        {
            get
            {
                if (string.IsNullOrEmpty(RequestDetail)) return "";
                int idx = RequestDetail.IndexOf(']');
                if (RequestDetail.StartsWith("[") && idx > 0) return RequestDetail.Substring(idx + 1).TrimStart();
                return RequestDetail;
            }
        }

        private string _requester = "";
        public string Requester { get => _requester; set { _requester = value; OnPropertyChanged(); } }

        private DateTime? _actionDate;
        public DateTime? ActionDate { get => _actionDate; set { _actionDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); OnPropertyChanged(nameof(IsDelayed)); } }

        private string _actionDetail = "";
        public string ActionDetail { get => _actionDetail; set { _actionDetail = value; OnPropertyChanged(); } }

        private string _assignee = "";
        public string Assignee { get => _assignee; set { _assignee = value; OnPropertyChanged(); } }

        private string _requestMemo = "";
        public string RequestMemo { get => _requestMemo; set { _requestMemo = value; OnPropertyChanged(); OnPropertyChanged(nameof(RequestImagePaths)); } }

        private string _actionMemo = "";
        public string ActionMemo { get => _actionMemo; set { _actionMemo = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActionImagePaths)); } }

        public string DurationText
        {
            get
            {
                if (Status == "완료") return "완료";
                if (Status == "보류") return "보류"; // 🔥 보류 상태 명시
                if (!DueDate.HasValue) return "-";

                int days = (DueDate.Value.Date - DateTime.Today).Days;
                if (days == 0) return "D-Day";
                if (days > 0) return $"D-{days}";
                return $"지연 +{-days}일";
            }
        }

        // 🎨 상태 표시용 색상 (Foreground/Background)
        public string StatusForeground
        {
            get
            {
                if (Status == "완료") return "#475569";
                if (Status == "보류") return "#B45309";
                if (IsDelayed) return "#DC2626";
                if (DueDate.HasValue && (DueDate.Value.Date - DateTime.Today).Days == 0) return "#D97706";
                return "#15803D";
            }
        }

        public string StatusBackground
        {
            get
            {
                if (Status == "완료") return "#F1F5F9";
                if (Status == "보류") return "#FEF3C7";
                if (IsDelayed) return "#FEE2E2";
                if (DueDate.HasValue && (DueDate.Value.Date - DateTime.Today).Days == 0) return "#FFEDD5";
                return "#DCFCE7";
            }
        }

        public bool IsDelayed => Status != "완료" && DueDate.HasValue && (DueDate.Value.Date - DateTime.Today).Days < 0;

        public ObservableCollection<string> RequestImagePaths
        {
            get
            {
                var paths = new ObservableCollection<string>();
                if (string.IsNullOrWhiteSpace(RequestMemo)) return paths;
                int start = RequestMemo.IndexOf("[[PRODREQ_IMAGES]]", StringComparison.Ordinal);
                int end = RequestMemo.IndexOf("[[/PRODREQ_IMAGES]]", StringComparison.Ordinal);
                if (start < 0 || end < 0 || end <= start) return paths;

                string block = RequestMemo.Substring(start + 18, end - (start + 18));
                var lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                string root = Path.Combine(AppPaths.DataRoot, "prodreq_images");

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed)) paths.Add(Path.GetFullPath(Path.Combine(root, trimmed)));
                }
                return paths;
            }
        }

        public ObservableCollection<string> ActionImagePaths
        {
            get
            {
                var paths = new ObservableCollection<string>();
                if (string.IsNullOrWhiteSpace(ActionMemo)) return paths;
                int start = ActionMemo.IndexOf("[[PRODREQ_IMAGES]]", StringComparison.Ordinal);
                int end = ActionMemo.IndexOf("[[/PRODREQ_IMAGES]]", StringComparison.Ordinal);
                if (start < 0 || end < 0 || end <= start) return paths;

                string block = ActionMemo.Substring(start + 18, end - (start + 18));
                var lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                string root = Path.Combine(AppPaths.DataRoot, "prodreq_images");

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed)) paths.Add(Path.GetFullPath(Path.Combine(root, trimmed)));
                }
                return paths;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class ProdReqView : UserControl, INotifyPropertyChanged
    {
        private const string ImageBlockStart = "[[PRODREQ_IMAGES]]";
        private const string ImageBlockEnd = "[[/PRODREQ_IMAGES]]";
        private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

        public ObservableCollection<ProdReqItem> RequestList { get; set; } = new();
        public ObservableCollection<string> ReqLocations { get; set; } = new() { "METAL", "N-METAL", "레이저실", "기타" };
        public ObservableCollection<string> ReqSubLocations { get; set; } = new();
        public ObservableCollection<string> ReqContentTypes { get; set; } = new() { "소모품", "수리", "내용", "기타" };
        public ObservableCollection<string> ActionStatusOptions { get; } = new() { "진행", "보류", "완료" };

        public ObservableCollection<string> RegisterModalAttachmentPaths { get; } = new();
        public ObservableCollection<string> ActionModalAttachmentPaths { get; } = new();
        public ObservableCollection<string> RequestEditAttachmentPaths { get; } = new();

        private bool _isRegisterModalOpen;
        public bool IsRegisterModalOpen { get => _isRegisterModalOpen; set { _isRegisterModalOpen = value; OnPropertyChanged(); } }

        private bool _isActionModalOpen;
        public bool IsActionModalOpen { get => _isActionModalOpen; set { _isActionModalOpen = value; OnPropertyChanged(); } }

        public string EditLocation { get { return _editLocation; } set { _editLocation = value; OnPropertyChanged(); UpdateSubLocations(); } }
        private string _editLocation = "METAL";
        public string EditSubLocation { get => _editSubLocation; set { _editSubLocation = value; OnPropertyChanged(); } }
        private string _editSubLocation = "";
        public string EditContentType { get => _editContentType; set { _editContentType = value; OnPropertyChanged(); } }
        private string _editContentType = "소모품";
        public DateTime? EditRequestDate { get => _editRequestDate; set { _editRequestDate = value; OnPropertyChanged(); } }
        private DateTime? _editRequestDate = DateTime.Today;
        public string EditRequester { get => _editRequester; set { _editRequester = value; OnPropertyChanged(); } }
        private string _editRequester = "";
        public string EditRequestDetail { get => _editRequestDetail; set { _editRequestDetail = value; OnPropertyChanged(); } }
        private string _editRequestDetail = "";

        private ProdReqItem? _currentEditItem;
        public string ReadCategoryLoc => _currentEditItem != null ? $"[{_currentEditItem.Category}] {_currentEditItem.Location}" : "";
        public string ReadDates => _currentEditItem != null ? $"요청일: {_currentEditItem.RequestDate:yyyy-MM-dd}" : "";
        public string ReadRequester => _currentEditItem != null ? $"요청자: {_currentEditItem.Requester}" : "";

        public string EditableRequestDetail { get => _editableRequestDetail; set { _editableRequestDetail = value; OnPropertyChanged(); } }
        private string _editableRequestDetail = "";
        public string ActionDetail { get => _actionDetail; set { _actionDetail = value; OnPropertyChanged(); } }
        private string _actionDetail = "";
        public string ActionAssignee { get => _actionAssignee; set { _actionAssignee = value; OnPropertyChanged(); } }
        private string _actionAssignee = "";
        public string ActionStatus { get => _actionStatus; set { _actionStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActionDateDisplay)); } }
        private string _actionStatus = "진행";
        public DateTime? ActionDueDate { get => _actionDueDate; set { _actionDueDate = value; OnPropertyChanged(); } }
        private DateTime? _actionDueDate;
        public string ActionDateDisplay { get { if (ActionStatus == "완료") return DateTime.Today.ToString("yyyy-MM-dd"); return _currentEditItem?.ActionDate?.ToString("yyyy-MM-dd") ?? "-"; } }

        private bool _canEditRequestInfo = false;
        public bool CanEditRequestInfo { get => _canEditRequestInfo; set { _canEditRequestInfo = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRequestInfoReadOnly)); } }
        public bool IsRequestInfoReadOnly => !CanEditRequestInfo;

        public ProdReqView()
        {
            InitializeComponent();
            DataContext = this;
            LoadDataFromDB();
            ApplyFilters();
            UpdateSubLocations();
            EditRequester = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : "알수없음";
            HookManageCheckedEvents();
        }

        private void LoadDataFromDB()
        {
            RequestList.Clear();
            try { DatabaseHelper.CreateProdReqTable(); foreach (var item in DatabaseHelper.GetAllProdReqs()) RequestList.Add(item); } catch { }
        }

        private void FilterTab_Checked(object sender, RoutedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            bool showAll = TabAll?.IsChecked == true;
            bool showInProg = TabInProgress?.IsChecked == true;
            bool showPending = TabPending?.IsChecked == true;

            var activeView = CollectionViewSource.GetDefaultView(RequestList);
            activeView.Filter = obj =>
            {
                if (obj is ProdReqItem item && item.Status != "완료")
                {
                    if (showAll) return true;
                    if (showInProg && item.Status == "진행") return true;
                    if (showPending && item.Status == "보류") return true;
                }
                return false;
            };
            if (ReqGrid != null) ReqGrid.ItemsSource = activeView;
            activeView.Refresh();

            var historyView = new CollectionViewSource { Source = RequestList }.View;
            historyView.Filter = obj => (obj as ProdReqItem)?.Status == "완료";
            if (HistoryGrid != null) HistoryGrid.ItemsSource = historyView;
            historyView.Refresh();

            UpdateTabCounts(); // 🎨 탭 카운트 뱃지 갱신
        }

        // 🎨 탭별 건수를 계산하여 뱃지에 반영
        private void UpdateTabCounts()
        {
            int inProg = RequestList.Count(x => x.Status == "진행");
            int pending = RequestList.Count(x => x.Status == "보류");
            int total = inProg + pending; // "진행 및 보류 중인 요청"의 합계

            if (CountAll != null) CountAll.Text = total.ToString();
            if (CountInProgress != null) CountInProgress.Text = inProg.ToString();
            if (CountPending != null) CountPending.Text = pending.ToString();
        }

        private void UpdateSubLocations()
        {
            ReqSubLocations.Clear();
            if (EditLocation == "METAL" || EditLocation == "N-METAL") { ReqSubLocations.Add("입고실"); ReqSubLocations.Add("출고실"); ReqSubLocations.Add("세정실"); ReqSubLocations.Add("반입구"); }
            else if (EditLocation == "레이저실") { ReqSubLocations.Add("LASER"); ReqSubLocations.Add("CO2"); ReqSubLocations.Add("각인기"); ReqSubLocations.Add("기타"); }
            else ReqSubLocations.Add("기타");
            EditSubLocation = ReqSubLocations.FirstOrDefault() ?? "";
        }

        private void HookManageCheckedEvents()
        {
            RequestList.CollectionChanged -= RequestList_CollectionChanged;
            RequestList.CollectionChanged += RequestList_CollectionChanged;
            foreach (var item in RequestList)
            {
                item.PropertyChanged -= RequestItem_PropertyChanged;
                item.PropertyChanged += RequestItem_PropertyChanged;
            }
        }

        private void RequestList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (ProdReqItem item in e.OldItems) item.PropertyChanged -= RequestItem_PropertyChanged;
            if (e.NewItems != null) foreach (ProdReqItem item in e.NewItems) item.PropertyChanged += RequestItem_PropertyChanged;
            UpdateManageColumnVisibility();
            UpdateTabCounts(); // 🎨 항목 추가/삭제 시 카운트 갱신
        }

        private void RequestItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProdReqItem.ManageChecked)) UpdateManageColumnVisibility();
            if (e.PropertyName == nameof(ProdReqItem.Status)) UpdateTabCounts(); // 🎨 상태 변경 시 카운트 갱신
        }

        private void UpdateManageColumnVisibility()
        {
            bool show = RequestList.Any(x => x.ManageChecked);
            if (ReqGrid_ManageColumn != null) ReqGrid_ManageColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (HistoryGrid_ManageColumn != null) HistoryGrid_ManageColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnToggleTop_Click(object sender, RoutedEventArgs e)
        {
            // 🎨 BtnToggleTop 체크 = 상단(진행 목록) 접기
            if (BtnToggleTop.IsChecked == true)
            {
                BtnToggleBottom.IsChecked = false;
                TopRow.Height = new GridLength(0);
                BottomRow.Height = new GridLength(1, GridUnitType.Star);
                if (ToggleTopText != null) ToggleTopText.Text = "진행 목록 펼치기";
                if (ToggleTopIcon != null) ToggleTopIcon.Text = "▼";
                if (ToggleBottomText != null) ToggleBottomText.Text = "조치 완료 접기";
                if (ToggleBottomIcon != null) ToggleBottomIcon.Text = "▼";
            }
            else
            {
                ResetLayout();
            }
        }

        private void BtnToggleBottom_Click(object sender, RoutedEventArgs e)
        {
            // 🎨 BtnToggleBottom 체크 = 하단(조치 완료) 접기
            if (BtnToggleBottom.IsChecked == true)
            {
                BtnToggleTop.IsChecked = false;
                BottomRow.Height = new GridLength(0);
                TopRow.Height = new GridLength(1, GridUnitType.Star);
                if (ToggleBottomText != null) ToggleBottomText.Text = "조치 완료 펼치기";
                if (ToggleBottomIcon != null) ToggleBottomIcon.Text = "▲";
                if (ToggleTopText != null) ToggleTopText.Text = "진행 목록 접기";
                if (ToggleTopIcon != null) ToggleTopIcon.Text = "▲";
            }
            else
            {
                ResetLayout();
            }
        }

        private void ResetLayout()
        {
            // 🎨 기본 비율 60:40 (상단 우선)
            TopRow.Height = new GridLength(6, GridUnitType.Star);
            BottomRow.Height = new GridLength(4, GridUnitType.Star);
            if (ToggleTopText != null) ToggleTopText.Text = "진행 목록 접기";
            if (ToggleTopIcon != null) ToggleTopIcon.Text = "▲";
            if (ToggleBottomText != null) ToggleBottomText.Text = "조치 완료 접기";
            if (ToggleBottomIcon != null) ToggleBottomIcon.Text = "▼";
        }

        private void ArchiveDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ProdReqItem item)
            {
                string msg = item.Status == "완료" ? "완료된 항목을 엑셀에 보관하고 삭제하시겠습니까?" : "진행 중인 요청입니다. 정말로 삭제하시겠습니까?";
                if (MessageBox.Show(msg, "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    if (item.Status == "완료") ArchiveToExcel(new List<ProdReqItem> { item });
                    DatabaseHelper.DeleteProdReq(item.Id);
                    RequestList.Remove(item);
                }
            }
        }

        private void ArchiveToExcel(List<ProdReqItem> items)
        {
            string path = Path.Combine(AppPaths.DataRoot, "생산팀_조치이력.xltx");
            using var wb = File.Exists(path) ? new XLWorkbook(path) : new XLWorkbook();
            var ws = wb.Worksheets.FirstOrDefault(x => x.Name == "조치이력") ?? wb.AddWorksheet("조치이력");
            if (ws.LastRowUsed() == null)
            {
                ws.Cell(1, 1).Value = "보관일시"; ws.Cell(1, 2).Value = "요청일"; ws.Cell(1, 3).Value = "예정일"; ws.Cell(1, 4).Value = "구분"; ws.Cell(1, 5).Value = "세부위치"; ws.Cell(1, 6).Value = "요청사항"; ws.Cell(1, 7).Value = "요청자"; ws.Cell(1, 8).Value = "조치일"; ws.Cell(1, 9).Value = "조치내용"; ws.Cell(1, 10).Value = "담당자";
                ws.Range(1, 1, 1, 10).Style.Font.Bold = true; ws.Range(1, 1, 1, 10).Style.Fill.BackgroundColor = XLColor.LightGray; ws.Range(1, 1, 1, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            int row = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
            foreach (var item in items)
            {
                ws.Cell(row, 1).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); ws.Cell(row, 2).Value = item.RequestDate?.ToString("yyyy-MM-dd") ?? ""; ws.Cell(row, 3).Value = item.DueDate?.ToString("yyyy-MM-dd") ?? ""; ws.Cell(row, 4).Value = item.Category; ws.Cell(row, 5).Value = item.Location; ws.Cell(row, 6).Value = item.RequestDetail; ws.Cell(row, 7).Value = item.Requester; ws.Cell(row, 8).Value = item.ActionDate?.ToString("yyyy-MM-dd") ?? ""; ws.Cell(row, 9).Value = item.ActionDetail; ws.Cell(row, 10).Value = item.Assignee;
                row++;
            }
            ws.Columns().AdjustToContents();
            wb.SaveAs(path);
        }

        public void OpenRegisterModal()
        {
            BtnResetRegister_Click(null, null);
            IsRegisterModalOpen = true;
        }

        private void CloseRegisterModal_Click(object sender, RoutedEventArgs e) => IsRegisterModalOpen = false;
        private void BtnResetRegister_Click(object? sender, RoutedEventArgs? e) { EditLocation = "METAL"; EditContentType = "소모품"; EditRequestDate = DateTime.Today; EditRequester = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : "알수없음"; EditRequestDetail = ""; RegisterModalAttachmentPaths.Clear(); }

        private void BtnSaveRegister_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EditRequestDetail)) { MessageBox.Show("상세 요청사항을 입력해 주세요.", "입력 누락", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var newItem = new ProdReqItem { Id = Guid.NewGuid(), RequestDate = EditRequestDate ?? DateTime.Today, Status = "진행", Category = EditLocation, Location = EditSubLocation, RequestDetail = $"[{EditContentType}] {EditRequestDetail}", Requester = EditRequester, RequestMemo = BuildMemo(RegisterModalAttachmentPaths) };
            DatabaseHelper.InsertProdReq(newItem); RequestList.Insert(0, newItem); IsRegisterModalOpen = false;
        }

        private void ReqGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as DataGrid)?.SelectedItem is ProdReqItem item)
            {
                _currentEditItem = item;
                OnPropertyChanged(nameof(ReadCategoryLoc)); OnPropertyChanged(nameof(ReadDates)); OnPropertyChanged(nameof(ReadRequester));

                EditableRequestDetail = item.RequestDetail;
                ActionDetail = item.ActionDetail;
                ActionStatus = item.Status;
                ActionDueDate = item.DueDate ?? DateTime.Today.AddDays(1);
                ActionAssignee = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : (item.Assignee ?? "");
                CanEditRequestInfo = SessionManager.IsLoggedIn && string.Equals(item.Requester, SessionManager.CurrentRealName, StringComparison.OrdinalIgnoreCase);

                RequestEditAttachmentPaths.Clear(); foreach (var p in ExtractPaths(item.RequestMemo)) RequestEditAttachmentPaths.Add(p);
                ActionModalAttachmentPaths.Clear(); foreach (var p in ExtractPaths(item.ActionMemo)) ActionModalAttachmentPaths.Add(p);
                IsActionModalOpen = true;
            }
        }

        private void CloseActionModal_Click(object sender, RoutedEventArgs e) => IsActionModalOpen = false;

        private void BtnSaveAction_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditItem == null) return;
            if (CanEditRequestInfo) { _currentEditItem.RequestDetail = EditableRequestDetail; _currentEditItem.RequestMemo = BuildMemo(RequestEditAttachmentPaths); }

            // 🔥 B안: 담당자는 현재 로그인 사용자로 무조건 자동 기록 (감사 추적)
            string finalAssignee = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : (_currentEditItem.Assignee ?? "");

            _currentEditItem.Status = ActionStatus; _currentEditItem.DueDate = ActionDueDate; _currentEditItem.Assignee = finalAssignee; _currentEditItem.ActionDetail = ActionDetail; _currentEditItem.ActionMemo = BuildMemo(ActionModalAttachmentPaths);
            if (ActionStatus == "완료") _currentEditItem.ActionDate = DateTime.Today; else _currentEditItem.ActionDate = null;
            DatabaseHelper.UpdateProdReq(_currentEditItem); IsActionModalOpen = false; ApplyFilters();
        }

        private void GridThumbnail_Click(object sender, MouseButtonEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string p) ShowImagePopup(p); }
        private void GridActionThumbnail_Click(object sender, MouseButtonEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string p) ShowImagePopup(p); }
        private void ModalThumbnail_Click(object sender, MouseButtonEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string p) ShowImagePopup(p); }
        private void ShowImagePopup(string path) { if (!File.Exists(path)) return; var win = new Window { Width = 1000, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterScreen }; var img = new Image { Source = new BitmapImage(new Uri(path)), Stretch = Stretch.Uniform, Margin = new Thickness(20) }; win.Content = img; win.ShowDialog(); }

        private List<string> ExtractPaths(string memo) { var res = new List<string>(); if (string.IsNullOrEmpty(memo)) return res; int s = memo.IndexOf(ImageBlockStart); int e = memo.IndexOf(ImageBlockEnd); if (s < 0 || e < 0) return res; var block = memo.Substring(s + 18, e - (s + 18)); return block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(x => Path.GetFullPath(Path.Combine(AppPaths.DataRoot, "prodreq_images", x.Trim()))).ToList(); }
        private string BuildMemo(IEnumerable<string> paths) { if (!paths.Any()) return ""; var sb = new StringBuilder(); sb.AppendLine(ImageBlockStart); foreach (var p in paths) sb.AppendLine(Path.GetFileName(p)); sb.Append(ImageBlockEnd); return sb.ToString(); }

        private bool IsImageFile(string path) => AllowedImageExtensions.Contains(Path.GetExtension(path).ToLower());

        private string SaveImage(string src)
        {
            string root = Path.Combine(AppPaths.DataRoot, "prodreq_images");
            Directory.CreateDirectory(root);
            string fn = Guid.NewGuid() + Path.GetExtension(src);
            string dst = Path.Combine(root, fn);
            File.Copy(src, dst, true);
            return dst;
        }

        private string SaveClipboard()
        {
            string root = Path.Combine(AppPaths.DataRoot, "prodreq_images");
            Directory.CreateDirectory(root);
            string fn = Guid.NewGuid() + ".png";
            string dst = Path.Combine(root, fn);
            using (var fs = new FileStream(dst, FileMode.Create))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(Clipboard.GetImage()));
                enc.Save(fs);
            }
            return dst;
        }

        private void RegisterModalMemo_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) foreach (var f in files.Where(IsImageFile)) RegisterModalAttachmentPaths.Add(SaveImage(f)); }
        private void RegisterModalMemo_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Clipboard.ContainsImage()) RegisterModalAttachmentPaths.Add(SaveClipboard()); }
        private void RegisterDeleteAttachment_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string p) RegisterModalAttachmentPaths.Remove(p); }
        private void ActionModalMemo_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) foreach (var f in files.Where(IsImageFile)) ActionModalAttachmentPaths.Add(SaveImage(f)); }
        private void ActionModalMemo_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Clipboard.ContainsImage()) ActionModalAttachmentPaths.Add(SaveClipboard()); }
        private void ActionDeleteAttachment_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string p) ActionModalAttachmentPaths.Remove(p); }
        private void RequestEditMemo_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) foreach (var f in files.Where(IsImageFile)) RequestEditAttachmentPaths.Add(SaveImage(f)); }
        private void RequestEditMemo_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Clipboard.ContainsImage()) RequestEditAttachmentPaths.Add(SaveClipboard()); }
        private void RequestEditDeleteAttachment_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string p) RequestEditAttachmentPaths.Remove(p); }

        private void RegisterModalMemo_PreviewDragOver(object sender, DragEventArgs e) { e.Handled = true; }
        private void ActionModalMemo_PreviewDragOver(object sender, DragEventArgs e) { e.Handled = true; }
        private void RequestEditMemo_PreviewDragOver(object sender, DragEventArgs e) { e.Handled = true; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}