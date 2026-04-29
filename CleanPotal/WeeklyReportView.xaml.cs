using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CleanPotal
{
    public class WeeklyGroupModel
    {
        public string MonthTitle { get; set; } = "";
        public ObservableCollection<WeeklyReportModel> Reports { get; set; } = new();
    }

    public class WeeklyReportModel : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string ShortTitle { get; set; } = "";
        public string DateRange { get; set; } = "";

        private string _memo = "";
        public string Memo { get => _memo; set { if (_memo == value) return; _memo = value; OnPropertyChanged(); } }

        public ObservableCollection<WeeklyBlockModel> Blocks { get; set; } = new();
        public ObservableCollection<WeeklyAttachmentModel> MemoAttachments { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class WeeklyAttachmentModel
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public bool IsImage => IsImagePath(FilePath);

        // 🔥 파일 종류별 이모지 아이콘 (PPT, Excel, Word, PDF 등)
        public string FileIcon => GetFileIcon(FilePath);

        // 🔥 대문자 확장자 라벨 (XLSX, PPTX, PDF 등)
        public string FileTypeLabel => GetFileTypeLabel(FilePath);

        // 🔥 썸네일 표시용: 이미지 확장자 + 파일 실제 존재 여부까지 체크
        // (파일이 없으면 아이콘으로 폴백하기 위함)
        public bool IsDisplayableImage
        {
            get
            {
                if (!IsImage) return false;
                if (string.IsNullOrWhiteSpace(FilePath)) return false;
                if (File.Exists(FilePath)) return true;
                try
                {
                    string candidateInCurrent = Path.Combine(AppPaths.DataRoot, "weekly_attachments", Path.GetFileName(FilePath));
                    if (File.Exists(candidateInCurrent)) return true;

                    string candidateInLegacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "weekly_attachments", Path.GetFileName(FilePath));
                    if (File.Exists(candidateInLegacy)) return true;
                }
                catch { }
                return false;
            }
        }

        public string AbsolutePath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilePath)) return "";
                if (File.Exists(FilePath)) return FilePath;
                try
                {
                    string candidateInCurrent = Path.Combine(AppPaths.DataRoot, "weekly_attachments", Path.GetFileName(FilePath));
                    if (File.Exists(candidateInCurrent)) return candidateInCurrent;

                    string candidateInLegacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "weekly_attachments", Path.GetFileName(FilePath));
                    if (File.Exists(candidateInLegacy)) return candidateInLegacy;
                }
                catch { }
                return FilePath;
            }
        }

        public static bool IsImagePath(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
        }

        private static string GetFileIcon(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".ppt" or ".pptx" => "📊",
                ".xls" or ".xlsx" => "📗",
                ".doc" or ".docx" => "📘",
                ".pdf" => "📕",
                _ => "📄"
            };
        }

        private static string GetFileTypeLabel(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
            return string.IsNullOrEmpty(ext) ? "FILE" : ext.ToUpper();
        }
    }

    public class WeeklyBlockModel : INotifyPropertyChanged
    {
        private static readonly Regex NumberPrefixRegex = new(@"^\s*\d+\.\s*", RegexOptions.Compiled);

        private int _number;
        public int Number { get => _number; set { if (_number == value) return; _number = value; OnPropertyChanged(); } }

        private string _category = "";
        public string Category
        {
            get => _category;
            set
            {
                string sanitized = NumberPrefixRegex.Replace(value ?? "", "");
                if (_category == sanitized) return;
                _category = sanitized;
                OnPropertyChanged();
            }
        }

        private string _parentReportTitle = "";
        [System.Text.Json.Serialization.JsonIgnore]
        public string ParentReportTitle
        {
            get => _parentReportTitle;
            set
            {
                if (_parentReportTitle == value) return;
                _parentReportTitle = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ParentTitleVisibility));
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public Visibility ParentTitleVisibility => string.IsNullOrEmpty(ParentReportTitle) ? Visibility.Collapsed : Visibility.Visible;

        public ObservableCollection<WeeklyAttachmentModel> FollowUpAttachments { get; } = new();
        public bool HasAttachment => FollowUpAttachments.Count > 0;

        private string _content = "";
        public string Content { get => _content; set { if (_content == value) return; _content = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedContent)); } }

        private string _followUp = "";
        public string FollowUp { get => _followUp; set { if (_followUp == value) return; _followUp = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedContent)); } }

        public string _status = "진행 중";
        public string Status { get => _status; set { if (_status == value) return; _status = value; OnPropertyChanged(); } }

        public string FormattedContent
        {
            get
            {
                var lines = new List<string>();
                if (!string.IsNullOrWhiteSpace(Content))
                {
                    lines.Add("【기존 내용】");
                    foreach (var line in Content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Where(x => !string.IsNullOrWhiteSpace(x))) lines.Add($"• {line.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(FollowUp))
                {
                    if (lines.Count > 0) lines.Add("");
                    lines.Add("【팔로업】");
                    foreach (var line in FollowUp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Where(x => !string.IsNullOrWhiteSpace(x))) lines.Add($"→ {line.Trim()}");
                }
                return string.Join("\n", lines).Trim();
            }
        }

        public WeeklyBlockModel()
        {
            FollowUpAttachments.CollectionChanged += (_, __) => { OnPropertyChanged(nameof(HasAttachment)); OnPropertyChanged(nameof(FormattedContent)); };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class WeeklyReportView : UserControl
    {
        public ObservableCollection<WeeklyGroupModel> GroupedHistory { get; set; } = new();
        private WeeklyReportModel? _currentReport;
        private WeeklyReportModel? _draftReport;
        private ObservableCollection<WeeklyBlockModel>? _subscribedBlocks;
        private ObservableCollection<WeeklyAttachmentModel>? _subscribedMemoAttachments;
        private bool _isDirty = false;
        private bool _isNavigating = false; // 프로그램 충돌(뻗음)을 막아주는 생명줄
        private WeeklyBlockModel? _draggedItem;

        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".pdf", ".xlsx", ".xls", ".doc", ".docx", ".ppt", ".pptx" };
        private static readonly string AttachmentStorageRoot = Path.Combine(AppPaths.DataRoot, "weekly_attachments");

        public WeeklyReportView()
        {
            InitializeComponent();
            GroupedHistoryControl.ItemsSource = GroupedHistory;
            InitCreateModal();
            LoadFromStorage();
        }

        // 🔥 새 창(Window) 호출 로직. 모달 대신 WeeklyReportTableWindow를 띄움
        public void ShowReportTable()
        {
            if (_draftReport == null) return;
            var tableWin = new WeeklyReportTableWindow(_draftReport);
            tableWin.Owner = Window.GetWindow(this);
            tableWin.Show();
        }

        // 🔥 오류 원인해결: XAML에 바인딩된 검색/필터 이벤트 정의
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();
        private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            if (_isNavigating) return;

            string keyword = SearchBox?.Text?.Trim() ?? "";
            bool showInProg = ToggleInProgress?.IsChecked == true;
            bool showPend = TogglePending?.IsChecked == true;
            bool showClosed = ToggleClosed?.IsChecked == true;
            bool noToggles = !showInProg && !showPend && !showClosed;

            if (string.IsNullOrEmpty(keyword))
            {
                BtnAddBlock.Visibility = Visibility.Visible;
                if (_draftReport == null)
                {
                    ReportBlocksControl.ItemsSource = null;
                    return;
                }

                TxtCurrentReportTitle.Text = _draftReport.Title;

                foreach (var block in _draftReport.Blocks) block.ParentReportTitle = "";

                if (ReportBlocksControl.ItemsSource != _draftReport.Blocks)
                    ReportBlocksControl.ItemsSource = _draftReport.Blocks;

                var view = CollectionViewSource.GetDefaultView(_draftReport.Blocks);
                view.Filter = obj =>
                {
                    if (obj is WeeklyBlockModel block)
                        return noToggles || (showInProg && block.Status == "진행 중") || (showPend && block.Status == "보류") || (showClosed && block.Status == "종결");
                    return false;
                };
                view.Refresh();
            }
            else
            {
                BtnAddBlock.Visibility = Visibility.Collapsed;
                TxtCurrentReportTitle.Text = $"'{keyword}' 검색 결과";

                // 🔥 뻗음 방지: 새로운 리스트 생성 후 할당
                var safeSearchResults = new ObservableCollection<WeeklyBlockModel>();

                foreach (var group in GroupedHistory)
                {
                    foreach (var report in group.Reports)
                    {
                        foreach (var block in report.Blocks)
                        {
                            bool matchKeyword = (block.Category != null && block.Category.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                (block.Content != null && block.Content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                (block.FollowUp != null && block.FollowUp.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

                            bool matchStatus = noToggles || (showInProg && block.Status == "진행 중") || (showPend && block.Status == "보류") || (showClosed && block.Status == "종결");

                            if (matchKeyword && matchStatus)
                            {
                                block.ParentReportTitle = $"[{report.ShortTitle}] ";
                                safeSearchResults.Add(block);
                            }
                            else
                            {
                                block.ParentReportTitle = "";
                            }
                        }
                    }
                }
                ReportBlocksControl.ItemsSource = safeSearchResults;
            }
            UpdateOverviewStats();
        }

        private void Card_MouseMove(object sender, MouseEventArgs e)
        {
            // 🔥 검색이나 필터 중 드래그 시 뻗는 현상 완벽 방어
            if (!string.IsNullOrEmpty(SearchBox.Text) || ToggleInProgress?.IsChecked == true || TogglePending?.IsChecked == true || ToggleClosed?.IsChecked == true) return;

            // 🔥 ComboBox, TextBox, Button 등 대화형 컨트롤 위에서는 드래그 시작하지 않음
            // (이게 없으면 ComboBox 클릭 시 드래그가 시작되면서 드롭다운이 즉시 닫힘)
            if (e.OriginalSource is DependencyObject source)
            {
                if (FindAncestor<ComboBox>(source) != null ||
                    FindAncestor<TextBox>(source) != null ||
                    FindAncestor<Button>(source) != null ||
                    FindAncestor<ToggleButton>(source) != null)
                {
                    return;
                }
            }

            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement element)
            {
                _draggedItem = element.Tag as WeeklyBlockModel;
                if (_draggedItem != null) DragDrop.DoDragDrop(element, _draggedItem, DragDropEffects.Move);
            }
        }

        // 🔥 시각 트리(Visual Tree)에서 특정 타입의 조상을 찾는 헬퍼
        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void Card_Drop(object sender, DragEventArgs e)
        {
            var targetItem = (sender as FrameworkElement)?.Tag as WeeklyBlockModel;
            var droppedItem = e.Data.GetData(typeof(WeeklyBlockModel)) as WeeklyBlockModel;

            if (targetItem != null && droppedItem != null && targetItem != droppedItem && _draftReport != null)
            {
                var list = _draftReport.Blocks;
                int oldIndex = list.IndexOf(droppedItem);
                int newIndex = list.IndexOf(targetItem);

                if (oldIndex >= 0 && newIndex >= 0)
                {
                    list.Move(oldIndex, newIndex);
                    RenumberBlocks(_draftReport);
                    UpdateOverviewStats();
                    _isDirty = true;
                }
            }
        }

        private void Card_PreviewDragOver(object sender, DragEventArgs e) { e.Effects = DragDropEffects.Move; e.Handled = true; }

        private WeeklyReportModel CloneReport(WeeklyReportModel original)
        {
            var clone = new WeeklyReportModel { Id = original.Id, Title = original.Title, ShortTitle = original.ShortTitle, DateRange = original.DateRange, Memo = original.Memo };
            foreach (var block in original.Blocks)
            {
                var clonedBlock = new WeeklyBlockModel { Number = block.Number, Category = block.Category, Status = block.Status, Content = block.Content, FollowUp = block.FollowUp };
                foreach (var att in block.FollowUpAttachments) clonedBlock.FollowUpAttachments.Add(new WeeklyAttachmentModel { FilePath = att.FilePath });
                clone.Blocks.Add(clonedBlock);
            }
            foreach (var attachment in original.MemoAttachments) clone.MemoAttachments.Add(new WeeklyAttachmentModel { FilePath = attachment.FilePath });
            return clone;
        }

        private void SetCurrentReport(WeeklyReportModel report)
        {
            if (_draftReport != null) _draftReport.PropertyChanged -= DraftReport_PropertyChanged;
            UnsubscribeBlockCollection();
            UnsubscribeMemoAttachments();

            _currentReport = report;
            _draftReport = CloneReport(report);
            _draftReport.PropertyChanged += DraftReport_PropertyChanged;

            MemoArea.DataContext = _draftReport;
            SubscribeBlockCollection(_draftReport.Blocks);
            SubscribeMemoAttachments(_draftReport.MemoAttachments);
            RenumberBlocks(_draftReport);

            _isNavigating = true;
            SearchBox.Text = "";
            if (ToggleInProgress != null) ToggleInProgress.IsChecked = false;
            if (TogglePending != null) TogglePending.IsChecked = false;
            if (ToggleClosed != null) ToggleClosed.IsChecked = false;
            _isNavigating = false;

            ApplyFilters();
            _isDirty = false;
        }

        private void DraftReport_PropertyChanged(object? sender, PropertyChangedEventArgs e) => _isDirty = true;
        private void Block_PropertyChanged(object? sender, PropertyChangedEventArgs e) { _isDirty = true; UpdateOverviewStats(); }

        private void SubscribeBlockCollection(ObservableCollection<WeeklyBlockModel> blocks)
        {
            _subscribedBlocks = blocks;
            _subscribedBlocks.CollectionChanged += Blocks_CollectionChanged;
            foreach (var b in blocks) b.PropertyChanged += Block_PropertyChanged;
        }

        private void UnsubscribeBlockCollection()
        {
            if (_subscribedBlocks == null) return;
            _subscribedBlocks.CollectionChanged -= Blocks_CollectionChanged;
            foreach (var b in _subscribedBlocks) b.PropertyChanged -= Block_PropertyChanged;
            _subscribedBlocks = null;
        }

        private void SubscribeMemoAttachments(ObservableCollection<WeeklyAttachmentModel> attachments)
        {
            _subscribedMemoAttachments = attachments;
            _subscribedMemoAttachments.CollectionChanged += MemoAttachments_CollectionChanged;
        }

        private void UnsubscribeMemoAttachments()
        {
            if (_subscribedMemoAttachments == null) return;
            _subscribedMemoAttachments.CollectionChanged -= MemoAttachments_CollectionChanged;
            _subscribedMemoAttachments = null;
        }

        private void MemoAttachments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { _isDirty = true; UpdateOverviewStats(); }

        private void Blocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _isDirty = true;
            if (e.NewItems != null) foreach (WeeklyBlockModel item in e.NewItems) item.PropertyChanged += Block_PropertyChanged;
            if (e.OldItems != null) foreach (WeeklyBlockModel item in e.OldItems) item.PropertyChanged -= Block_PropertyChanged;
            if (_draftReport != null) RenumberBlocks(_draftReport);
            UpdateOverviewStats();
        }

        public void SaveReportChanges() => BtnSaveContent_Click(this, new RoutedEventArgs());

        private void BtnSaveContent_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReport == null || _draftReport == null) return;
            CommitActiveEditorChanges();

            if (string.IsNullOrEmpty(SearchBox.Text))
            {
                _currentReport.Memo = _draftReport.Memo;
                _currentReport.Blocks.Clear();
                foreach (var block in _draftReport.Blocks)
                {
                    var clonedBlock = new WeeklyBlockModel { Number = block.Number, Category = block.Category, Status = block.Status, Content = block.Content, FollowUp = block.FollowUp };
                    foreach (var att in block.FollowUpAttachments) clonedBlock.FollowUpAttachments.Add(new WeeklyAttachmentModel { FilePath = att.FilePath });
                    _currentReport.Blocks.Add(clonedBlock);
                }
                _currentReport.MemoAttachments.Clear();
                foreach (var attachment in _draftReport.MemoAttachments) _currentReport.MemoAttachments.Add(new WeeklyAttachmentModel { FilePath = attachment.FilePath });
            }

            _isDirty = false;
            SaveToStorage();
            MessageBox.Show("변경사항이 안전하게 원본에 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CommitActiveEditorChanges()
        {
            var focusedElement = Keyboard.FocusedElement as DependencyObject;
            if (focusedElement is TextBox textBox) textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            else if (focusedElement is ComboBox comboBox)
            {
                comboBox.GetBindingExpression(ComboBox.SelectedValueProperty)?.UpdateSource();
                comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
            }
            Keyboard.ClearFocus();
        }

        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isNavigating) return;
            if (sender is ListBox lb && lb.SelectedItem is WeeklyReportModel selected)
            {
                if (_isDirty)
                {
                    var result = MessageBox.Show("저장되지 않은 변경사항이 있습니다. 무시하고 이동하시겠습니까?\n(아니오를 누르면 현재 화면에 머무릅니다)", "확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.No)
                    {
                        _isNavigating = true;
                        lb.SelectedItem = _currentReport;
                        _isNavigating = false;
                        return;
                    }
                }
                SetCurrentReport(selected);
            }
        }

        private void InitCreateModal()
        {
            // 🔥 콤보박스 충돌 원천 해결: ItemsSource 바인딩 사용
            ComboYear.ItemsSource = Enumerable.Range(2024, DateTime.Now.Year - 2024 + 2).ToList();
            ComboMonth.ItemsSource = Enumerable.Range(1, 12).ToList();

            ComboYear.SelectedItem = DateTime.Now.Year;
            ComboMonth.SelectedItem = DateTime.Now.Month;
            ComboWeek.SelectedIndex = GetCurrentWeekOfMonth(DateTime.Now) - 1;

            ComboYear.SelectionChanged += (s, e) => UpdateDatePreview();
            ComboMonth.SelectionChanged += (s, e) => UpdateDatePreview();
            ComboWeek.SelectionChanged += (s, e) => UpdateDatePreview();
            UpdateDatePreview();
        }

        private int GetCurrentWeekOfMonth(DateTime date)
        {
            DateTime firstDay = new DateTime(date.Year, date.Month, 1);
            int firstDayOfWeek = (int)firstDay.DayOfWeek;
            if (firstDayOfWeek == 0) firstDayOfWeek = 7;
            return (date.Day + firstDayOfWeek - 2) / 7 + 1;
        }

        private string GetDateRangeForWeek(int year, int month, int week)
        {
            DateTime firstDay = new DateTime(year, month, 1);
            int firstDayOfWeek = (int)firstDay.DayOfWeek;
            if (firstDayOfWeek == 0) firstDayOfWeek = 7;

            DateTime targetMonday = firstDay.AddDays((week - 1) * 7 - (firstDayOfWeek - 1));
            DateTime targetFriday = targetMonday.AddDays(4);

            return $"{targetMonday:yyyy.MM.dd} ~ {targetFriday:yyyy.MM.dd}";
        }

        private void UpdateDatePreview()
        {
            if (ComboYear.SelectedItem == null || ComboMonth.SelectedItem == null || ComboWeek.SelectedIndex < 0) return;
            TxtAutoDatePreview.Text = "자동 계산 날짜: " + GetDateRangeForWeek((int)ComboYear.SelectedItem, (int)ComboMonth.SelectedItem, ComboWeek.SelectedIndex + 1);
        }

        private void BtnCreateNewReport_Click(object sender, RoutedEventArgs e)
        {
            if (_isDirty)
            {
                var result = MessageBox.Show("작성 중인 내용이 있습니다. 저장하지 않고 새 보고서를 만드시겠습니까?", "확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No) return;
            }

            ComboYear.SelectedItem = DateTime.Now.Year;
            ComboMonth.SelectedItem = DateTime.Now.Month;
            ComboWeek.SelectedIndex = GetCurrentWeekOfMonth(DateTime.Now) - 1;
            CreateReportModal.Visibility = Visibility.Visible;
        }

        private void BtnConfirmCreate_Click(object sender, RoutedEventArgs e)
        {
            int year = (int)ComboYear.SelectedItem;
            int month = (int)ComboMonth.SelectedItem;
            int week = ComboWeek.SelectedIndex + 1;
            string title = $"{year % 100}년 {month}월 {week}주차";
            string dateRange = GetDateRangeForWeek(year, month, week);

            var existing = GroupedHistory.SelectMany(g => g.Reports).FirstOrDefault(r => r.Title == title);
            if (existing != null)
            {
                MessageBox.Show($"이미 '{title}' 보고서가 존재합니다.\n해당 보고서로 이동합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                SetCurrentReport(existing);
                CreateReportModal.Visibility = Visibility.Collapsed;
                return;
            }

            string monthGroupTitle = $"{year}년 {month}월";
            var group = GroupedHistory.FirstOrDefault(g => g.MonthTitle == monthGroupTitle);
            if (group == null)
            {
                group = new WeeklyGroupModel { MonthTitle = monthGroupTitle };
                GroupedHistory.Add(group);
                var sorted = GroupedHistory.OrderByDescending(g => g.MonthTitle).ToList();
                GroupedHistory.Clear();
                foreach (var s in sorted) GroupedHistory.Add(s);
            }

            var newReport = new WeeklyReportModel { Title = title, ShortTitle = $"{week}주차", DateRange = dateRange };

            var lastReport = GroupedHistory.SelectMany(g => g.Reports).OrderByDescending(r => r.DateRange).FirstOrDefault();
            if (lastReport != null)
            {
                foreach (var b in lastReport.Blocks.Where(x => x.Status != "종결"))
                {
                    var copied = new WeeklyBlockModel { Category = b.Category, Content = b.Content, FollowUp = b.FollowUp, Status = b.Status };
                    foreach (var att in b.FollowUpAttachments) copied.FollowUpAttachments.Add(new WeeklyAttachmentModel { FilePath = att.FilePath });
                    newReport.Blocks.Add(copied);
                }
            }

            group.Reports.Add(newReport);
            var sortedReports = group.Reports.OrderByDescending(r => r.Title).ToList();
            group.Reports.Clear();
            foreach (var r in sortedReports) group.Reports.Add(r);

            RenumberBlocks(newReport);
            SetCurrentReport(newReport);
            SaveToStorage();
            CreateReportModal.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelCreate_Click(object sender, RoutedEventArgs e) => CreateReportModal.Visibility = Visibility.Collapsed;

        private void BtnAddBlock_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;

            // 항목 추가 시 필터를 끕니다.
            _isNavigating = true;
            if (ToggleInProgress != null) ToggleInProgress.IsChecked = false;
            if (TogglePending != null) TogglePending.IsChecked = false;
            if (ToggleClosed != null) ToggleClosed.IsChecked = false;
            SearchBox.Text = "";
            _isNavigating = false;

            _draftReport.Blocks.Add(new WeeklyBlockModel { Category = "신규 업무" });
            RenumberBlocks(_draftReport);
            ApplyFilters();
        }

        private void BtnDeleteBlock_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            if (sender is Button { Tag: WeeklyBlockModel block })
            {
                var result = MessageBox.Show("해당 업무 항목을 정말 삭제하시겠습니까?", "항목 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    if (string.IsNullOrEmpty(SearchBox.Text))
                    {
                        _draftReport.Blocks.Remove(block);
                        RenumberBlocks(_draftReport);
                    }
                    else
                    {
                        var ownerReport = GroupedHistory.SelectMany(g => g.Reports).FirstOrDefault(r => r.Blocks.Contains(block));
                        ownerReport?.Blocks.Remove(block);
                        if (ownerReport != null) RenumberBlocks(ownerReport);
                        SaveToStorage();
                    }
                    _isDirty = true;
                    ApplyFilters();
                }
            }
        }

        private void BtnClearSelection_Click(object sender, RoutedEventArgs e) => ClearCurrentSelection();

        private void BtnDeleteReport_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReport == null) return;
            var result = MessageBox.Show($"'{_currentReport.Title}' 보고서를 삭제하시겠습니까?\n삭제 후에는 복구할 수 없습니다.", "보고서 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var ownerGroup = GroupedHistory.FirstOrDefault(g => g.Reports.Contains(_currentReport));
            ownerGroup?.Reports.Remove(_currentReport);
            if (ownerGroup != null && ownerGroup.Reports.Count == 0) GroupedHistory.Remove(ownerGroup);
            ClearCurrentSelection();
            SaveToStorage();
        }

        private void BtnAddMemoAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "지원 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.pdf;*.xlsx;*.xls;*.doc;*.docx;*.ppt;*.pptx|모든 파일|*.*" };
            if (dialog.ShowDialog() != true) return;
            AddMemoAttachments(dialog.FileNames);
        }

        private void BtnRemoveMemoAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WeeklyAttachmentModel attachment } || _draftReport == null) return;
            _draftReport.MemoAttachments.Remove(attachment);
            _isDirty = true;
            UpdateOverviewStats();
        }

        private void BtnAddAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WeeklyBlockModel block }) return;
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "지원 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.pdf;*.xlsx;*.xls;*.doc;*.docx;*.ppt;*.pptx|모든 파일|*.*" };
            if (dialog.ShowDialog() == true) AddAttachments(block, dialog.FileNames);
        }

        private void BtnRemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WeeklyAttachmentModel attachment } || _draftReport == null) return;

            if (string.IsNullOrEmpty(SearchBox.Text))
            {
                var owner = _draftReport.Blocks.FirstOrDefault(b => b.FollowUpAttachments.Contains(attachment));
                owner?.FollowUpAttachments.Remove(attachment);
            }
            else
            {
                var globalOwner = GroupedHistory.SelectMany(g => g.Reports).SelectMany(r => r.Blocks).FirstOrDefault(b => b.FollowUpAttachments.Contains(attachment));
                globalOwner?.FollowUpAttachments.Remove(attachment);
                SaveToStorage();
                ApplyFilters();
            }
        }

        private void AttachmentPreview_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: WeeklyAttachmentModel attachment }) return;
            string resolvedPath = ResolveAttachmentPath(attachment.FilePath);
            if (!File.Exists(resolvedPath))
            {
                MessageBox.Show("첨부 파일을 찾을 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (WeeklyAttachmentModel.IsImagePath(resolvedPath)) ShowImagePopup(resolvedPath);
            else Process.Start(new ProcessStartInfo(resolvedPath) { UseShellExecute = true });
        }

        private void ReportImage_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WeeklyAttachmentModel att)
            {
                string resolvedPath = ResolveAttachmentPath(att.FilePath);
                if (File.Exists(resolvedPath)) ShowImagePopup(resolvedPath);
                e.Handled = true;
            }
        }

        private void AttachmentPanel_PreviewDragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }

        private void AttachmentPanel_Drop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: WeeklyBlockModel block }) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            AddAttachments(block, files);
            e.Handled = true;
        }

        private void AttachmentPanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: WeeklyBlockModel block }) return;
            if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
            if (!Clipboard.ContainsImage()) return;
            string saved = SaveClipboardImage();
            if (!string.IsNullOrWhiteSpace(saved)) AddAttachments(block, new[] { saved });
            e.Handled = true;
        }

        private static void AddAttachments(WeeklyBlockModel block, IEnumerable<string> files)
        {
            foreach (var file in files.Where(File.Exists))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (!AllowedExtensions.Contains(ext)) continue;
                string persistedPath = PersistAttachment(file);
                if (string.IsNullOrWhiteSpace(persistedPath)) continue;
                if (block.FollowUpAttachments.Any(a => string.Equals(a.FilePath, persistedPath, StringComparison.OrdinalIgnoreCase))) continue;
                block.FollowUpAttachments.Add(new WeeklyAttachmentModel { FilePath = persistedPath });
            }
        }

        private static string SaveClipboardImage()
        {
            var image = Clipboard.GetImage();
            if (image == null) return "";
            EnsureAttachmentStorageDirectory();
            string path = Path.Combine(AttachmentStorageRoot, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png");
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(fs);
            return path;
        }

        private void MemoAttachmentPanel_PreviewDragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }

        private void MemoAttachmentPanel_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            AddMemoAttachments(files);
            e.Handled = true;
        }

        private void MemoAttachmentPanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;

            if (Clipboard.ContainsImage())
            {
                string saved = SaveClipboardImage();
                if (!string.IsNullOrWhiteSpace(saved)) AddMemoAttachments(new[] { saved });
                e.Handled = true;
                return;
            }

            if (!Clipboard.ContainsFileDropList()) return;
            var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
            if (files.Length == 0) return;
            AddMemoAttachments(files);
            e.Handled = true;
        }

        private void AddMemoAttachments(IEnumerable<string> files)
        {
            if (_draftReport == null) return;
            var added = files
                .Where(File.Exists)
                .Where(file => AllowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (added.Count == 0) return;
            foreach (var file in added)
            {
                string persistedPath = PersistAttachment(file);
                if (string.IsNullOrWhiteSpace(persistedPath)) continue;
                if (_draftReport.MemoAttachments.Any(a => string.Equals(a.FilePath, persistedPath, StringComparison.OrdinalIgnoreCase))) continue;
                _draftReport.MemoAttachments.Add(new WeeklyAttachmentModel { FilePath = persistedPath });
            }
            _isDirty = true;
            UpdateOverviewStats();
        }

        private static void EnsureAttachmentStorageDirectory()
        {
            if (!Directory.Exists(AttachmentStorageRoot))
            {
                Directory.CreateDirectory(AttachmentStorageRoot);
            }
        }

        private static string PersistAttachment(string sourcePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return "";
                string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
                if (!AllowedExtensions.Contains(ext)) return "";

                EnsureAttachmentStorageDirectory();
                string fullSourcePath = Path.GetFullPath(sourcePath);
                if (fullSourcePath.StartsWith(AttachmentStorageRoot, StringComparison.OrdinalIgnoreCase)) return fullSourcePath;

                string destinationName = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}{ext}";
                string destinationPath = Path.Combine(AttachmentStorageRoot, destinationName);
                File.Copy(fullSourcePath, destinationPath, overwrite: false);
                return destinationPath;
            }
            catch
            {
                return "";
            }
        }

        private static string ResolveAttachmentPath(string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return "";
            if (File.Exists(storedPath)) return storedPath;

            string fileName = Path.GetFileName(storedPath);
            if (string.IsNullOrWhiteSpace(fileName)) return storedPath;

            string candidateInCurrent = Path.Combine(AttachmentStorageRoot, fileName);
            if (File.Exists(candidateInCurrent)) return candidateInCurrent;

            string candidateInLegacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "weekly_attachments", fileName);
            if (File.Exists(candidateInLegacy)) return candidateInLegacy;

            return storedPath;
        }

        private void ClearCurrentSelection()
        {
            _isNavigating = true;

            _currentReport = null;
            if (_draftReport != null) _draftReport.PropertyChanged -= DraftReport_PropertyChanged;
            _draftReport = null;
            UnsubscribeBlockCollection();
            UnsubscribeMemoAttachments();

            TxtCurrentReportTitle.Text = "보고서를 선택하세요";
            TxtCurrentReportDate.Text = "작성 기간이 표시됩니다.";
            ReportBlocksControl.ItemsSource = null;
            MemoArea.DataContext = null;
            _isDirty = false;

            SearchBox.Text = "";
            if (ToggleInProgress != null) ToggleInProgress.IsChecked = false;
            if (TogglePending != null) TogglePending.IsChecked = false;
            if (ToggleClosed != null) ToggleClosed.IsChecked = false;

            _isNavigating = false;
            UpdateOverviewStats();
        }

        private void UpdateOverviewStats()
        {
            IEnumerable<WeeklyBlockModel> sourceBlocks;
            int memoAttCount = 0;

            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                string keyword = SearchBox.Text.Trim();
                var globalMatches = new List<WeeklyBlockModel>();
                foreach (var group in GroupedHistory)
                    foreach (var report in group.Reports)
                        foreach (var block in report.Blocks)
                            if ((block.Category != null && block.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                                (block.Content != null && block.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                                (block.FollowUp != null && block.FollowUp.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                                globalMatches.Add(block);

                sourceBlocks = globalMatches;
                memoAttCount = 0;
            }
            else
            {
                if (_draftReport == null)
                {
                    if (TxtStatTotal != null) TxtStatTotal.Text = "총 0건";
                    if (TxtStatInProgress != null) TxtStatInProgress.Text = "진행 0";
                    if (TxtStatPending != null) TxtStatPending.Text = "보류 0";
                    if (TxtStatClosed != null) TxtStatClosed.Text = "종결 0";
                    if (TxtStatAttachments != null) TxtStatAttachments.Text = "첨부 0";
                    return;
                }
                sourceBlocks = _draftReport.Blocks;
                memoAttCount = _draftReport.MemoAttachments.Count;
            }

            int total = sourceBlocks.Count();
            int inProgress = sourceBlocks.Count(b => b.Status == "진행 중");
            int pending = sourceBlocks.Count(b => b.Status == "보류");
            int closed = sourceBlocks.Count(b => b.Status == "종결");
            int attachments = sourceBlocks.Sum(b => b.FollowUpAttachments.Count) + memoAttCount;

            if (TxtStatTotal != null) TxtStatTotal.Text = $"총 {total}건";
            if (TxtStatInProgress != null) TxtStatInProgress.Text = $"진행 {inProgress}";
            if (TxtStatPending != null) TxtStatPending.Text = $"보류 {pending}";
            if (TxtStatClosed != null) TxtStatClosed.Text = $"종결 {closed}";
            if (TxtStatAttachments != null) TxtStatAttachments.Text = $"첨부 {attachments}";
        }

        private static void RenumberBlocks(WeeklyReportModel report)
        {
            for (int i = 0; i < report.Blocks.Count; i++) report.Blocks[i].Number = i + 1;
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static string StoragePath => AppPaths.WeeklyReportsFilePath;

        private void NormalizeGroups()
        {
            var normalized = GroupedHistory
                .Where(g => !string.IsNullOrWhiteSpace(g.MonthTitle))
                .Select(g =>
                {
                    var validReports = g.Reports.Where(r => !string.IsNullOrWhiteSpace(r.Title)).OrderByDescending(r => r.Title).ToList();
                    var group = new WeeklyGroupModel { MonthTitle = g.MonthTitle };
                    foreach (var report in validReports) group.Reports.Add(report);
                    return group;
                })
                .Where(g => g.Reports.Count > 0)
                .OrderByDescending(g => g.MonthTitle)
                .ToList();

            GroupedHistory.Clear();
            foreach (var group in normalized) GroupedHistory.Add(group);
        }

        private void LoadFromStorage()
        {
            try
            {
                if (!File.Exists(StoragePath)) return;
                var json = File.ReadAllText(StoragePath);
                var data = JsonSerializer.Deserialize<List<PersistedGroup>>(json);
                if (data == null) return;

                GroupedHistory.Clear();
                foreach (var group in data)
                {
                    var mappedGroup = new WeeklyGroupModel { MonthTitle = group.MonthTitle ?? "" };
                    foreach (var report in group.Reports ?? new())
                    {
                        var mappedReport = new WeeklyReportModel { Id = string.IsNullOrWhiteSpace(report.Id) ? Guid.NewGuid().ToString() : report.Id, Title = report.Title ?? "", ShortTitle = report.ShortTitle ?? "", DateRange = report.DateRange ?? "", Memo = report.Memo ?? "" };
                        foreach (var block in report.Blocks ?? new())
                        {
                            var mappedBlock = new WeeklyBlockModel { Number = block.Number, Category = block.Category ?? "", Status = string.IsNullOrWhiteSpace(block.Status) ? "진행 중" : block.Status, Content = block.Content ?? "", FollowUp = block.FollowUp ?? "" };
                            foreach (var att in block.FollowUpAttachments ?? new())
                                if (!string.IsNullOrWhiteSpace(att.FilePath)) mappedBlock.FollowUpAttachments.Add(new WeeklyAttachmentModel { FilePath = att.FilePath });
                            mappedReport.Blocks.Add(mappedBlock);
                        }
                        foreach (var att in report.MemoAttachments ?? new())
                            if (!string.IsNullOrWhiteSpace(att.FilePath)) mappedReport.MemoAttachments.Add(new WeeklyAttachmentModel { FilePath = att.FilePath });

                        mappedGroup.Reports.Add(mappedReport);
                    }
                    if (!string.IsNullOrWhiteSpace(mappedGroup.MonthTitle) && mappedGroup.Reports.Count > 0) GroupedHistory.Add(mappedGroup);
                }
                NormalizeGroups();
            }
            catch { }
        }

        private void SaveToStorage()
        {
            try
            {
                NormalizeGroups();
                Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
                var data = GroupedHistory.Select(g => new PersistedGroup
                {
                    MonthTitle = g.MonthTitle,
                    Reports = g.Reports.Select(r => new PersistedReport
                    {
                        Id = r.Id,
                        Title = r.Title,
                        ShortTitle = r.ShortTitle,
                        DateRange = r.DateRange,
                        Memo = r.Memo,
                        Blocks = r.Blocks.Select(b => new PersistedBlock { Number = b.Number, Category = b.Category, Status = b.Status, Content = b.Content, FollowUp = b.FollowUp, FollowUpAttachments = b.FollowUpAttachments.Select(a => new PersistedAttachment { FilePath = a.FilePath }).ToList() }).ToList(),
                        MemoAttachments = r.MemoAttachments.Select(a => new PersistedAttachment { FilePath = a.FilePath }).ToList()
                    }).ToList()
                }).ToList();
                File.WriteAllText(StoragePath, JsonSerializer.Serialize(data, JsonOptions));
            }
            catch { }
        }

        private class PersistedGroup { public string? MonthTitle { get; set; } public List<PersistedReport> Reports { get; set; } = new(); }
        private class PersistedReport { public string? Id { get; set; } public string? Title { get; set; } public string? ShortTitle { get; set; } public string? DateRange { get; set; } public string? Memo { get; set; } public List<PersistedBlock> Blocks { get; set; } = new(); public List<PersistedAttachment> MemoAttachments { get; set; } = new(); }
        private class PersistedBlock { public int Number { get; set; } public string? Category { get; set; } public string? Status { get; set; } public string? Content { get; set; } public string? FollowUp { get; set; } public List<PersistedAttachment> FollowUpAttachments { get; set; } = new(); }
        private class PersistedAttachment { public string? FilePath { get; set; } }

        private static void ShowImagePopup(string imagePath)
        {
            try
            {
                var window = new Window { Title = "첨부 이미지 보기", Width = 1000, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)) };
                var img = new Image { Source = new BitmapImage(new Uri(imagePath)), Stretch = Stretch.Uniform, Margin = new Thickness(20) };
                window.Content = img;
                window.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show($"이미지를 불러올 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }
    }
}
