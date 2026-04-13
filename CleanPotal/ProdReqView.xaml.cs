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
        public DateTime? DueDate { get => _dueDate; set { _dueDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); OnPropertyChanged(nameof(IsDelayed)); } }

        private string _status = "진행";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); OnPropertyChanged(nameof(IsDelayed)); } }

        private string _category = "";
        public string Category { get => _category; set { _category = value; OnPropertyChanged(); } }

        private string _location = "";
        public string Location { get => _location; set { _location = value; OnPropertyChanged(); } }

        private string _requestDetail = "";
        public string RequestDetail { get => _requestDetail; set { _requestDetail = value; OnPropertyChanged(); } }

        private string _requester = "";
        public string Requester { get => _requester; set { _requester = value; OnPropertyChanged(); } }

        private DateTime? _actionDate;
        public DateTime? ActionDate { get => _actionDate; set { _actionDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); OnPropertyChanged(nameof(IsDelayed)); } }

        private string _actionDetail = "";
        public string ActionDetail { get => _actionDetail; set { _actionDetail = value; OnPropertyChanged(); } }

        private string _assignee = "";
        public string Assignee { get => _assignee; set { _assignee = value; OnPropertyChanged(); } }

        private string _requestMemo = "";
        public string RequestMemo { get => _requestMemo; set { _requestMemo = value; OnPropertyChanged(); OnPropertyChanged(nameof(RequestFirstImagePath)); OnPropertyChanged(nameof(HasRequestImage)); } }

        private string _actionMemo = "";
        public string ActionMemo { get => _actionMemo; set { _actionMemo = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActionFirstImagePath)); OnPropertyChanged(nameof(HasActionImage)); } }

        public string DurationText
        {
            get
            {
                if (Status == "완료") return "완료";
                if (!DueDate.HasValue) return "-";

                int days = (DueDate.Value.Date - DateTime.Today).Days;
                if (days == 0) return "D-Day";
                if (days > 0) return $"D-{days}";
                return $"지연 +{-days}일";
            }
        }

        public bool IsDelayed => Status != "완료" && DueDate.HasValue && (DueDate.Value.Date - DateTime.Today).Days < 0;

        private string ExtractFirstImage(string memoText)
        {
            if (string.IsNullOrWhiteSpace(memoText)) return "";
            int start = memoText.IndexOf("[[PRODREQ_IMAGES]]", StringComparison.Ordinal);
            int end = memoText.IndexOf("[[/PRODREQ_IMAGES]]", StringComparison.Ordinal);
            if (start < 0 || end < 0 || end <= start) return "";

            string block = memoText.Substring(start + 18, end - (start + 18));
            var lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    string root = Path.Combine(AppPaths.DataRoot, "prodreq_images");
                    return Path.GetFullPath(Path.Combine(root, trimmed));
                }
            }
            return "";
        }

        public string RequestFirstImagePath => ExtractFirstImage(RequestMemo);
        public bool HasRequestImage => !string.IsNullOrEmpty(RequestFirstImagePath);

        public string ActionFirstImagePath => ExtractFirstImage(ActionMemo);
        public bool HasActionImage => !string.IsNullOrEmpty(ActionFirstImagePath);

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

        private bool _isRegisterModalOpen;
        public bool IsRegisterModalOpen { get => _isRegisterModalOpen; set { _isRegisterModalOpen = value; OnPropertyChanged(); } }

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
        private bool _isActionModalOpen;
        public bool IsActionModalOpen { get => _isActionModalOpen; set { _isActionModalOpen = value; OnPropertyChanged(); } }

        public string ReadCategoryLoc => _currentEditItem != null ? $"[{_currentEditItem.Category}] {_currentEditItem.Location}" : "";
        public string ReadDates => _currentEditItem != null ? $"요청일: {_currentEditItem.RequestDate:yyyy-MM-dd}" : "";
        public string ReadRequester => _currentEditItem != null ? $"요청자: {_currentEditItem.Requester}" : "";
        public string ReadRequestDetail => _currentEditItem?.RequestDetail ?? "";
        public ObservableCollection<string> ReadRequestImages { get; } = new();

        public string ActionStatus
        {
            get => _actionStatus;
            set
            {
                _actionStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionDateDisplay));
            }
        }
        private string _actionStatus = "진행";

        public DateTime? ActionDueDate { get => _actionDueDate; set { _actionDueDate = value; OnPropertyChanged(); } }
        private DateTime? _actionDueDate;

        public string ActionDateDisplay
        {
            get
            {
                if (ActionStatus == "완료") return DateTime.Today.ToString("yyyy-MM-dd");
                return _currentEditItem?.ActionDate?.ToString("yyyy-MM-dd") ?? "-";
            }
        }

        public string ActionAssignee { get => _actionAssignee; set { _actionAssignee = value; OnPropertyChanged(); } }
        private string _actionAssignee = "";
        public string ActionDetail { get => _actionDetail; set { _actionDetail = value; OnPropertyChanged(); } }
        private string _actionDetail = "";


        public ProdReqView()
        {
            InitializeComponent();
            DataContext = this;

            // 🔥 누락되었던 핵심 코드 복구: 화면의 DataGrid와 백그라운드 리스트를 결속시킵니다.
            ReqGrid.ItemsSource = RequestList;

            LoadDataFromDB();

            UpdateSubLocations();
            EditRequester = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : "알수없음";

            HookManageCheckedEvents();
        }

        private void LoadDataFromDB()
        {
            RequestList.Clear();
            try
            {
                DatabaseHelper.CreateProdReqTable();

                var items = DatabaseHelper.GetAllProdReqs();
                foreach (var item in items)
                {
                    RequestList.Add(item);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"DB 데이터를 불러오지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
        }

        private void RequestItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProdReqItem.ManageChecked)) UpdateManageColumnVisibility();
        }

        private void UpdateManageColumnVisibility()
        {
            bool show = RequestList.Any(x => x.ManageChecked);
            if (ManageColumn != null) ManageColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ArchiveDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ProdReqItem item)
            {
                if (item.Status != "완료")
                {
                    MessageBox.Show("완료 처리된 항목만 삭제(엑셀 보관)할 수 있습니다.", "보관 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                    item.ManageChecked = false;
                    return;
                }

                if (MessageBox.Show("선택한 완료 항목을 리스트에서 지우고 엑셀 서식파일(.xltx)에 영구 보관하시겠습니까?", "삭제 및 보관", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try
                    {
                        ArchiveToExcel(new List<ProdReqItem> { item });
                        DatabaseHelper.DeleteProdReq(item.Id);
                        RequestList.Remove(item);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"엑셀 저장 및 삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
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
                ws.Cell(1, 1).Value = "보관일시"; ws.Cell(1, 2).Value = "요청일"; ws.Cell(1, 3).Value = "예정일";
                ws.Cell(1, 4).Value = "구분"; ws.Cell(1, 5).Value = "세부위치"; ws.Cell(1, 6).Value = "요청사항";
                ws.Cell(1, 7).Value = "요청자"; ws.Cell(1, 8).Value = "조치일"; ws.Cell(1, 9).Value = "조치내용";
                ws.Cell(1, 10).Value = "담당자";
                ws.Range(1, 1, 1, 10).Style.Font.Bold = true; ws.Range(1, 1, 1, 10).Style.Fill.BackgroundColor = XLColor.LightGray;
                ws.Range(1, 1, 1, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
            foreach (var item in items)
            {
                ws.Cell(row, 1).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                ws.Cell(row, 2).Value = item.RequestDate?.ToString("yyyy-MM-dd") ?? "";
                ws.Cell(row, 3).Value = item.DueDate?.ToString("yyyy-MM-dd") ?? "";
                ws.Cell(row, 4).Value = item.Category;
                ws.Cell(row, 5).Value = item.Location;
                ws.Cell(row, 6).Value = item.RequestDetail;
                ws.Cell(row, 7).Value = item.Requester;
                ws.Cell(row, 8).Value = item.ActionDate?.ToString("yyyy-MM-dd") ?? "";
                ws.Cell(row, 9).Value = item.ActionDetail;
                ws.Cell(row, 10).Value = item.Assignee;
                row++;
            }

            ws.Columns().AdjustToContents();
            wb.SaveAs(path);
        }

        private void BtnOpenRegister_Click(object sender, RoutedEventArgs e) { BtnResetRegister_Click(null, null); IsRegisterModalOpen = true; }
        private void CloseRegisterModal_Click(object sender, RoutedEventArgs e) => IsRegisterModalOpen = false;

        private void BtnResetRegister_Click(object? sender, RoutedEventArgs? e)
        {
            EditLocation = "METAL"; EditContentType = "소모품";
            EditRequestDate = DateTime.Today;
            EditRequester = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : "알수없음";
            EditRequestDetail = ""; RegisterModalAttachmentPaths.Clear();
        }

        private void BtnSaveRegister_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EditRequestDetail))
            {
                MessageBox.Show("상세 요청사항을 입력해 주세요.", "입력 누락", MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }

            var newItem = new ProdReqItem
            {
                Id = Guid.NewGuid(),
                RequestDate = DateTime.Today,
                DueDate = null,
                Status = "진행",
                Category = EditLocation,
                Location = EditSubLocation,
                RequestDetail = $"[{EditContentType}] {EditRequestDetail}",
                Requester = EditRequester,
                Assignee = "",
                RequestMemo = BuildMemo("", RegisterModalAttachmentPaths),
                ActionMemo = "",
                ManageChecked = false
            };

            try
            {
                DatabaseHelper.InsertProdReq(newItem);
                RequestList.Insert(0, newItem);
                IsRegisterModalOpen = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"DB 저장 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReqGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ReqGrid.SelectedItem is ProdReqItem item)
            {
                _currentEditItem = item;

                OnPropertyChanged(nameof(ReadCategoryLoc)); OnPropertyChanged(nameof(ReadDates));
                OnPropertyChanged(nameof(ReadRequester)); OnPropertyChanged(nameof(ReadRequestDetail));

                ReadRequestImages.Clear();
                foreach (var path in ExtractPathsFromMemo(item.RequestMemo)) ReadRequestImages.Add(path);

                ActionStatus = item.Status;
                ActionDueDate = item.DueDate ?? DateTime.Today.AddDays(1);
                ActionAssignee = string.IsNullOrWhiteSpace(item.Assignee) && SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : item.Assignee;
                ActionDetail = item.ActionDetail;

                ActionModalAttachmentPaths.Clear();
                foreach (var path in ExtractPathsFromMemo(item.ActionMemo)) ActionModalAttachmentPaths.Add(path);

                IsActionModalOpen = true;
            }
        }

        private void CloseActionModal_Click(object sender, RoutedEventArgs e) => IsActionModalOpen = false;

        private void BtnSaveAction_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEditItem == null) return;

            _currentEditItem.Status = ActionStatus;
            _currentEditItem.DueDate = ActionDueDate;

            if (ActionStatus == "완료")
            {
                _currentEditItem.ActionDate = DateTime.Today;
            }
            else
            {
                _currentEditItem.ActionDate = null;
            }

            _currentEditItem.Assignee = ActionAssignee;
            _currentEditItem.ActionDetail = ActionDetail;
            _currentEditItem.ActionMemo = BuildMemo("", ActionModalAttachmentPaths);

            try
            {
                DatabaseHelper.UpdateProdReq(_currentEditItem);
                IsActionModalOpen = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"DB 업데이트 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GridThumbnail_Click(object sender, MouseButtonEventArgs e) { if (sender is FrameworkElement element && element.DataContext is ProdReqItem item && item.HasRequestImage) { ShowImagePopup(item.RequestFirstImagePath); e.Handled = true; } }
        private void GridActionThumbnail_Click(object sender, MouseButtonEventArgs e) { if (sender is FrameworkElement element && element.DataContext is ProdReqItem item && item.HasActionImage) { ShowImagePopup(item.ActionFirstImagePath); e.Handled = true; } }
        private void ModalThumbnail_Click(object sender, MouseButtonEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string path) { string root = Path.Combine(AppPaths.DataRoot, "prodreq_images"); ShowImagePopup(Path.GetFullPath(Path.Combine(root, path))); e.Handled = true; } }

        private void ShowImagePopup(string imagePath)
        {
            try
            {
                var window = new Window { Title = "이미지 뷰어", Width = 1000, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)) };
                var img = new Image { Source = new BitmapImage(new Uri(imagePath)), Stretch = Stretch.Uniform, Margin = new Thickness(20) };
                window.Content = img; window.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show($"이미지를 불러올 수 없습니다.\n{ex.Message}", "오류"); }
        }

        private List<string> ExtractPathsFromMemo(string memo)
        {
            var res = new List<string>(); if (string.IsNullOrWhiteSpace(memo)) return res;
            int start = memo.IndexOf(ImageBlockStart, StringComparison.Ordinal); int end = memo.IndexOf(ImageBlockEnd, StringComparison.Ordinal);
            if (start < 0 || end < 0) return res;
            string block = memo.Substring(start + 18, end - (start + 18));
            foreach (var line in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) { if (!string.IsNullOrWhiteSpace(line.Trim())) res.Add(line.Trim()); }
            return res;
        }

        private static bool HasImageFiles(IDataObject data) { if (!data.GetDataPresent(DataFormats.FileDrop)) return false; var files = data.GetData(DataFormats.FileDrop) as string[]; return files != null && files.Any(f => AllowedImageExtensions.Any(x => f.EndsWith(x, StringComparison.OrdinalIgnoreCase))); }
        private static string GetImageRootDirectory() { string dir = Path.Combine(AppPaths.DataRoot, "prodreq_images"); Directory.CreateDirectory(dir); return dir; }
        private static string BuildMemo(string text, IEnumerable<string> paths) { var cleanPaths = paths.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(); if (cleanPaths.Count == 0) return text; var sb = new StringBuilder(text); sb.AppendLine(); sb.AppendLine(ImageBlockStart); foreach (var p in cleanPaths) sb.AppendLine(p); sb.Append(ImageBlockEnd); return sb.ToString(); }

        private static List<string> SaveDroppedImages(Guid id, IEnumerable<string> files)
        {
            string root = GetImageRootDirectory(); string itemDir = Path.Combine(root, id.ToString("N")); Directory.CreateDirectory(itemDir);
            var saved = new List<string>(); foreach (var f in files) { string dest = Path.Combine(itemDir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}{Path.GetExtension(f)}"); File.Copy(f, dest, true); saved.Add(Path.GetRelativePath(root, dest)); }
            return saved;
        }

        private static List<string> SaveClipboardImages(Guid id)
        {
            var saved = new List<string>(); if (!Clipboard.ContainsImage()) return saved; var img = Clipboard.GetImage(); if (img == null) return saved;
            string root = GetImageRootDirectory(); string itemDir = Path.Combine(root, id.ToString("N")); Directory.CreateDirectory(itemDir);
            string dest = Path.Combine(itemDir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png");
            using (var fs = new FileStream(dest, FileMode.Create)) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img)); enc.Save(fs); }
            saved.Add(Path.GetRelativePath(root, dest)); return saved;
        }

        private void RegisterModalMemo_PreviewDragOver(object sender, DragEventArgs e) { e.Effects = HasImageFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
        private void RegisterModalMemo_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { var saved = SaveDroppedImages(Guid.NewGuid(), files.Where(f => AllowedImageExtensions.Any(x => f.EndsWith(x, StringComparison.OrdinalIgnoreCase)))); foreach (var s in saved) RegisterModalAttachmentPaths.Add(s); } e.Handled = true; }
        private void RegisterModalMemo_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Clipboard.ContainsImage()) { var saved = SaveClipboardImages(Guid.NewGuid()); foreach (var s in saved) RegisterModalAttachmentPaths.Add(s); e.Handled = true; } }
        private void RegisterDeleteAttachment_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string path) RegisterModalAttachmentPaths.Remove(path); }

        private void ActionModalMemo_PreviewDragOver(object sender, DragEventArgs e) { e.Effects = HasImageFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
        private void ActionModalMemo_Drop(object sender, DragEventArgs e) { if (_currentEditItem != null && e.Data.GetData(DataFormats.FileDrop) is string[] files) { var saved = SaveDroppedImages(_currentEditItem.Id, files.Where(f => AllowedImageExtensions.Any(x => f.EndsWith(x, StringComparison.OrdinalIgnoreCase)))); foreach (var s in saved) ActionModalAttachmentPaths.Add(s); } e.Handled = true; }
        private void ActionModalMemo_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Clipboard.ContainsImage() && _currentEditItem != null) { var saved = SaveClipboardImages(_currentEditItem.Id); foreach (var s in saved) ActionModalAttachmentPaths.Add(s); e.Handled = true; } }
        private void ActionDeleteAttachment_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is string path) ActionModalAttachmentPaths.Remove(path); }
    }
}