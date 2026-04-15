using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CleanPotal
{
    // ==========================================
    // 데이터 모델 정의 (누락 방지)
    // ==========================================
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
        public string Memo
        {
            get => _memo;
            set { if (_memo == value) return; _memo = value; OnPropertyChanged(); }
        }

        public ObservableCollection<WeeklyBlockModel> Blocks { get; set; } = new();
        public ObservableCollection<WeeklyAttachmentModel> MemoAttachments { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class WeeklyAttachmentModel
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public bool IsImage => IsImageFile(FilePath);

        private static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
        }
    }

    public class WeeklyBlockModel : INotifyPropertyChanged
    {
        private static readonly Regex NumberPrefixRegex = new(@"^\s*\d+\.\s*", RegexOptions.Compiled);

        private int _number;
        public int Number
        {
            get => _number;
            set { if (_number == value) return; _number = value; OnPropertyChanged(); }
        }

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

        public ObservableCollection<WeeklyAttachmentModel> FollowUpAttachments { get; } = new();
        public bool HasAttachment => FollowUpAttachments.Count > 0;

        private string _content = "";
        public string Content
        {
            get => _content;
            set { if (_content == value) return; _content = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedContent)); }
        }

        private string _followUp = "";
        public string FollowUp
        {
            get => _followUp;
            set { if (_followUp == value) return; _followUp = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedContent)); }
        }

        private string _status = "진행 중";
        public string Status
        {
            get => _status;
            set { if (_status == value) return; _status = value; OnPropertyChanged(); }
        }

        public string FormattedContent
        {
            get
            {
                string result = "";
                if (!string.IsNullOrWhiteSpace(Content)) result += $"· {Content}\n";
                if (!string.IsNullOrWhiteSpace(FollowUp)) result += $"→ {FollowUp}";
                if (FollowUpAttachments.Count > 0)
                {
                    if (result.Length > 0) result += "\n";
                    result += $"📎 첨부 {FollowUpAttachments.Count}건";
                }
                return result.TrimEnd();
            }
        }

        public WeeklyBlockModel()
        {
            FollowUpAttachments.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(HasAttachment));
                OnPropertyChanged(nameof(FormattedContent));
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ==========================================
    // 메인 컨트롤 로직
    // ==========================================
    public partial class WeeklyReportView : UserControl
    {
        public ObservableCollection<WeeklyGroupModel> GroupedHistory { get; set; } = new();

        private WeeklyReportModel? _currentReport; // 실제 저장되어 있는 원본 데이터
        private WeeklyReportModel? _draftReport;   // 화면에 띄워두고 작업하는 임시 복사본 (Clone)
        private ObservableCollection<WeeklyBlockModel>? _subscribedBlocks;
        private ObservableCollection<WeeklyAttachmentModel>? _subscribedMemoAttachments;

        private bool _isDirty = false; // 변경사항 발생 여부 체크
        private bool _isNavigating = false; // 리스트박스 꼬임 방지용

        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".pdf", ".xlsx", ".xls", ".doc", ".docx", ".ppt", ".pptx" };

        public WeeklyReportView()
        {
            InitializeComponent();
            GroupedHistoryControl.ItemsSource = GroupedHistory;
            InitCreateModal();
            // 빈 화면 유지 (더미 데이터 삭제)
        }

        // ==========================================
        // 1. 임시 작업장(Clone) 및 데이터 연동 로직
        // ==========================================
        private WeeklyReportModel CloneReport(WeeklyReportModel original)
        {
            var clone = new WeeklyReportModel
            {
                Id = original.Id,
                Title = original.Title,
                ShortTitle = original.ShortTitle,
                DateRange = original.DateRange,
                Memo = original.Memo
            };
            foreach (var block in original.Blocks)
            {
                var clonedBlock = new WeeklyBlockModel
                {
                    Number = block.Number,
                    Category = block.Category,
                    Status = block.Status,
                    Content = block.Content,
                    FollowUp = block.FollowUp
                };
                foreach (var att in block.FollowUpAttachments)
                {
                    clonedBlock.FollowUpAttachments.Add(new WeeklyAttachmentModel { FilePath = att.FilePath });
                }
                clone.Blocks.Add(clonedBlock);
            }
            foreach (var attachment in original.MemoAttachments)
            {
                clone.MemoAttachments.Add(new WeeklyAttachmentModel { FilePath = attachment.FilePath });
            }
            return clone;
        }

        private void SetCurrentReport(WeeklyReportModel report)
        {
            if (_draftReport != null) _draftReport.PropertyChanged -= DraftReport_PropertyChanged;
            UnsubscribeBlockCollection();
            UnsubscribeMemoAttachments();

            _currentReport = report;

            // 🔥 원본 데이터를 보호하기 위해 Clone(복사본)을 떠서 화면에 바인딩합니다.
            _draftReport = CloneReport(report);
            _draftReport.PropertyChanged += DraftReport_PropertyChanged;

            MemoArea.DataContext = _draftReport;
            SubscribeBlockCollection(_draftReport.Blocks);
            SubscribeMemoAttachments(_draftReport.MemoAttachments);
            RenumberBlocks(_draftReport);

            TxtCurrentReportTitle.Text = _draftReport.Title;
            TxtCurrentReportDate.Text = _draftReport.DateRange;
            ReportBlocksControl.ItemsSource = _draftReport.Blocks;

            _isDirty = false; // 새로 불러왔으므로 깨끗한 상태
            UpdateOverviewStats();
        }

        // ==========================================
        // 2. 변경사항 감지 및 [저장] 로직 (가장 중요한 부분)
        // ==========================================
        private void DraftReport_PropertyChanged(object? sender, PropertyChangedEventArgs e) => _isDirty = true;
        private void Block_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            _isDirty = true;
            UpdateOverviewStats();
        }

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

        private void MemoAttachments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _isDirty = true;
            UpdateOverviewStats();
        }

        private void Blocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _isDirty = true;
            if (e.NewItems != null) foreach (WeeklyBlockModel item in e.NewItems) item.PropertyChanged += Block_PropertyChanged;
            if (e.OldItems != null) foreach (WeeklyBlockModel item in e.OldItems) item.PropertyChanged -= Block_PropertyChanged;
            if (_draftReport != null) RenumberBlocks(_draftReport);
            UpdateOverviewStats();
        }

        // 메인 윈도우에서 버튼 클릭 시 실행되는 진짜 저장 로직
        public void SaveReportChanges()
        {
            BtnSaveContent_Click(this, new RoutedEventArgs());
        }

        private void BtnSaveContent_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReport == null || _draftReport == null) return;
            CommitActiveEditorChanges();

            // 🔥 임시 복사본(_draftReport)의 모든 변경사항을 원본(_currentReport)으로 덮어씌웁니다.
            _currentReport.Memo = _draftReport.Memo;
            _currentReport.Blocks.Clear();

            foreach (var block in _draftReport.Blocks)
            {
                var clonedBlock = new WeeklyBlockModel
                {
                    Number = block.Number,
                    Category = block.Category,
                    Status = block.Status,
                    Content = block.Content,
                    FollowUp = block.FollowUp
                };
                foreach (var att in block.FollowUpAttachments)
                {
                    clonedBlock.FollowUpAttachments.Add(new WeeklyAttachmentModel { FilePath = att.FilePath });
                }
                _currentReport.Blocks.Add(clonedBlock);
            }
            _currentReport.MemoAttachments.Clear();
            foreach (var attachment in _draftReport.MemoAttachments)
            {
                _currentReport.MemoAttachments.Add(new WeeklyAttachmentModel { FilePath = attachment.FilePath });
            }

            _isDirty = false; // 원본에 반영되었으므로 다시 깨끗한 상태
            MessageBox.Show("변경사항이 안전하게 원본에 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CommitActiveEditorChanges()
        {
            var focusedElement = Keyboard.FocusedElement as DependencyObject;
            if (focusedElement is TextBox textBox)
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
            else if (focusedElement is ComboBox comboBox)
            {
                comboBox.GetBindingExpression(ComboBox.SelectedValueProperty)?.UpdateSource();
                comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
            }
            Keyboard.ClearFocus();
        }

        // ==========================================
        // 3. 네비게이션 시 안전장치 (실수 방지용 팝업)
        // ==========================================
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
                        // 이동을 취소하고 선택을 원래대로 되돌림
                        _isNavigating = true;
                        lb.SelectedItem = _currentReport;
                        _isNavigating = false;
                        return;
                    }
                }
                // 이동을 승인했거나 변경사항이 없으면 새로운 리스트 로드
                SetCurrentReport(selected);
            }
        }

        // ==========================================
        // 4. 새 보고서 생성 (스마트 달력 로직)
        // ==========================================
        private void InitCreateModal()
        {
            for (int y = 2024; y <= DateTime.Now.Year + 1; y++) ComboYear.Items.Add(y);
            for (int m = 1; m <= 12; m++) ComboMonth.Items.Add(m);

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
            CreateReportModal.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelCreate_Click(object sender, RoutedEventArgs e) => CreateReportModal.Visibility = Visibility.Collapsed;

        // ==========================================
        // 5. 기타 블록 및 첨부파일 제어 (Draft 기반)
        // ==========================================
        private void BtnAddBlock_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            _draftReport.Blocks.Add(new WeeklyBlockModel { Category = "신규 업무" });
            RenumberBlocks(_draftReport);
            UpdateOverviewStats();
        }

        private void BtnDeleteBlock_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            if (sender is Button { Tag: WeeklyBlockModel block })
            {
                // 🔥 삭제 전 2중 안전장치 (팝업창) 추가
                var result = MessageBox.Show("해당 업무 항목을 정말 삭제하시겠습니까?", "항목 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                // 사용자가 '예'를 눌렀을 때만 삭제 진행
                if (result == MessageBoxResult.Yes)
                {
                    _draftReport.Blocks.Remove(block);
                    RenumberBlocks(_draftReport);
                    UpdateOverviewStats();
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

        public void ShowReportTable() => BtnShowTable_Click(this, new RoutedEventArgs());
        private void BtnCloseTable_Click(object sender, RoutedEventArgs e) => TableModalOverlay.Visibility = Visibility.Collapsed;

        private void BtnShowTable_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            TxtModalTitle.Text = $"{_draftReport.Title} 주간보고";
            ReportDataGrid.ItemsSource = null;
            RenumberBlocks(_draftReport);
            ReportDataGrid.ItemsSource = _draftReport.Blocks; // 팝업에는 현재 편집중인 내용을 띄움
            TableModalOverlay.Visibility = Visibility.Visible;
        }

        private void BtnAddAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WeeklyBlockModel block }) return;
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "지원 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.pdf;*.xlsx;*.xls;*.doc;*.docx;*.ppt;*.pptx|모든 파일|*.*" };
            if (dialog.ShowDialog() != true) return;
            AddAttachments(block, dialog.FileNames);
        }

        private void BtnRemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WeeklyAttachmentModel attachment } || _draftReport == null) return;
            var owner = _draftReport.Blocks.FirstOrDefault(b => b.FollowUpAttachments.Contains(attachment));
            owner?.FollowUpAttachments.Remove(attachment);
        }

        private void AttachmentPreview_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: WeeklyAttachmentModel attachment }) return;
            if (!File.Exists(attachment.FilePath))
            {
                MessageBox.Show("첨부 파일을 찾을 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (attachment.IsImage) ShowImagePopup(attachment.FilePath);
            else Process.Start(new ProcessStartInfo(attachment.FilePath) { UseShellExecute = true });
        }

        private void AttachmentPanel_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

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
                if (block.FollowUpAttachments.Any(a => string.Equals(a.FilePath, file, StringComparison.OrdinalIgnoreCase))) continue;
                block.FollowUpAttachments.Add(new WeeklyAttachmentModel { FilePath = file });
            }
        }

        private static string SaveClipboardImage()
        {
            var image = Clipboard.GetImage();
            if (image == null) return "";
            string root = "";
            try { root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "weekly_attachments"); }
            catch { root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CleanPotal", "weekly_attachments"); }
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png");
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(fs);
            return path;
        }

        private void MemoAttachmentPanel_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

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
                if (_draftReport.MemoAttachments.Any(a => string.Equals(a.FilePath, file, StringComparison.OrdinalIgnoreCase))) continue;
                _draftReport.MemoAttachments.Add(new WeeklyAttachmentModel { FilePath = file });
            }
            _isDirty = true;
            UpdateOverviewStats();
        }

        private void ClearCurrentSelection()
        {
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
            UpdateOverviewStats();
        }

        private void UpdateOverviewStats()
        {
            var report = _draftReport;
            if (report == null)
            {
                TxtStatTotal.Text = "총 0건";
                TxtStatInProgress.Text = "진행 0";
                TxtStatPending.Text = "보류 0";
                TxtStatClosed.Text = "종결 0";
                TxtStatAttachments.Text = "첨부 0";
                return;
            }

            int total = report.Blocks.Count;
            int inProgress = report.Blocks.Count(b => b.Status == "진행 중");
            int pending = report.Blocks.Count(b => b.Status == "보류");
            int closed = report.Blocks.Count(b => b.Status == "종결");
            int attachments = report.Blocks.Sum(b => b.FollowUpAttachments.Count) + report.MemoAttachments.Count;

            TxtStatTotal.Text = $"총 {total}건";
            TxtStatInProgress.Text = $"진행 {inProgress}";
            TxtStatPending.Text = $"보류 {pending}";
            TxtStatClosed.Text = $"종결 {closed}";
            TxtStatAttachments.Text = $"첨부 {attachments}";
        }

        private static void RenumberBlocks(WeeklyReportModel report)
        {
            for (int i = 0; i < report.Blocks.Count; i++) report.Blocks[i].Number = i + 1;
        }

        private static void ShowImagePopup(string imagePath)
        {
            try
            {
                var window = new Window { Title = "첨부 이미지 보기", Width = 1000, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)) };
                var img = new Image { Source = new BitmapImage(new Uri(imagePath)), Stretch = Stretch.Uniform, Margin = new Thickness(20) };
                window.Content = img;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"이미지를 불러올 수 없습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}