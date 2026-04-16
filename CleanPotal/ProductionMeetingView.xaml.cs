using ClosedXML.Excel;
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
using System.Text.Json;
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
    public class ProductionMeetingGroupModel
    {
        public string MonthTitle { get; set; } = "";
        public ObservableCollection<ProductionMeetingReportModel> Reports { get; set; } = new();
    }

    public class ProductionMeetingReportModel : INotifyPropertyChanged
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

        public ObservableCollection<ProductionMeetingBlockModel> Blocks { get; set; } = new();
        public ObservableCollection<ProductionMeetingAttachmentModel> MemoAttachments { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ProductionMeetingAttachmentModel
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public bool IsImage => IsImagePath(FilePath);

        public static bool IsImagePath(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
        }
    }

    public class ProductionMeetingBlockModel : INotifyPropertyChanged
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

        public ObservableCollection<ProductionMeetingAttachmentModel> FollowUpAttachments { get; } = new();
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

        public ProductionMeetingBlockModel()
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
    public partial class ProductionMeetingView : UserControl
    {
        public ObservableCollection<ProductionMeetingGroupModel> GroupedHistory { get; set; } = new();

        private ProductionMeetingReportModel? _currentReport; // 실제 저장되어 있는 원본 데이터
        private ProductionMeetingReportModel? _draftReport;   // 화면에 띄워두고 작업하는 임시 복사본 (Clone)
        private ObservableCollection<ProductionMeetingBlockModel>? _subscribedBlocks;
        private ObservableCollection<ProductionMeetingAttachmentModel>? _subscribedMemoAttachments;

        private bool _isDirty = false; // 변경사항 발생 여부 체크
        private bool _isNavigating = false; // 리스트박스 꼬임 방지용

        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".pdf", ".xlsx", ".xls", ".doc", ".docx", ".ppt", ".pptx" };
        private static readonly string AttachmentStorageRoot = Path.Combine(AppPaths.DataRoot, "production_meeting_attachments");

        public ProductionMeetingView()
        {
            InitializeComponent();
            GroupedHistoryControl.ItemsSource = GroupedHistory;
            InitCreateModal();
            LoadFromStorage();
            // 빈 화면 유지 (더미 데이터 삭제)
        }

        // ==========================================
        // 1. 임시 작업장(Clone) 및 데이터 연동 로직
        // ==========================================
        private ProductionMeetingReportModel CloneReport(ProductionMeetingReportModel original)
        {
            var clone = new ProductionMeetingReportModel
            {
                Id = original.Id,
                Title = original.Title,
                ShortTitle = original.ShortTitle,
                DateRange = original.DateRange,
                Memo = original.Memo
            };
            foreach (var block in original.Blocks)
            {
                var clonedBlock = new ProductionMeetingBlockModel
                {
                    Number = block.Number,
                    Category = block.Category,
                    Status = block.Status,
                    Content = block.Content,
                    FollowUp = block.FollowUp
                };
                foreach (var att in block.FollowUpAttachments)
                {
                    clonedBlock.FollowUpAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
                }
                clone.Blocks.Add(clonedBlock);
            }
            foreach (var attachment in original.MemoAttachments)
            {
                clone.MemoAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = attachment.FilePath });
            }
            return clone;
        }

        private void SetCurrentReport(ProductionMeetingReportModel report)
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

        private void SubscribeBlockCollection(ObservableCollection<ProductionMeetingBlockModel> blocks)
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

        private void SubscribeMemoAttachments(ObservableCollection<ProductionMeetingAttachmentModel> attachments)
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
            if (e.NewItems != null) foreach (ProductionMeetingBlockModel item in e.NewItems) item.PropertyChanged += Block_PropertyChanged;
            if (e.OldItems != null) foreach (ProductionMeetingBlockModel item in e.OldItems) item.PropertyChanged -= Block_PropertyChanged;
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
                var clonedBlock = new ProductionMeetingBlockModel
                {
                    Number = block.Number,
                    Category = block.Category,
                    Status = block.Status,
                    Content = block.Content,
                    FollowUp = block.FollowUp
                };
                foreach (var att in block.FollowUpAttachments)
                {
                    clonedBlock.FollowUpAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
                }
                _currentReport.Blocks.Add(clonedBlock);
            }
            _currentReport.MemoAttachments.Clear();
            foreach (var attachment in _draftReport.MemoAttachments)
            {
                _currentReport.MemoAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = attachment.FilePath });
            }

            _isDirty = false; // 원본에 반영되었으므로 다시 깨끗한 상태
            SaveToStorage();
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

            if (sender is ListBox lb && lb.SelectedItem is ProductionMeetingReportModel selected)
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
            DpMeetingDate.SelectedDate = DateTime.Today;
            DpMeetingDate.SelectedDateChanged += (s, e) => UpdateDatePreview();
            UpdateDatePreview();
        }

        private void UpdateDatePreview()
        {
            if (DpMeetingDate.SelectedDate == null) return;
            var selectedDate = DpMeetingDate.SelectedDate.Value;
            TxtAutoDatePreview.Text = $"선택 날짜: {selectedDate:yyyy.MM.dd (ddd)}";
        }

        private void BtnCreateNewReport_Click(object sender, RoutedEventArgs e)
        {
            if (_isDirty)
            {
                var result = MessageBox.Show("작성 중인 내용이 있습니다. 저장하지 않고 새 보고서를 만드시겠습니까?", "확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No) return;
            }

            DpMeetingDate.SelectedDate = DateTime.Today;
            CreateReportModal.Visibility = Visibility.Visible;
        }

        private void BtnConfirmCreate_Click(object sender, RoutedEventArgs e)
        {
            var selectedDate = DpMeetingDate.SelectedDate ?? DateTime.Today;
            string title = $"{selectedDate:yyyy년 M월 d일}";
            string dateRange = $"{selectedDate:yyyy.MM.dd}";

            var existing = GroupedHistory.SelectMany(g => g.Reports).FirstOrDefault(r => r.DateRange == dateRange);
            if (existing != null)
            {
                MessageBox.Show($"이미 '{selectedDate:yyyy.MM.dd}' 날짜의 미팅 보고서가 존재합니다.\n해당 보고서로 이동합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                SetCurrentReport(existing);
                CreateReportModal.Visibility = Visibility.Collapsed;
                return;
            }

            string monthGroupTitle = $"{selectedDate:yyyy년 M월}";
            var group = GroupedHistory.FirstOrDefault(g => g.MonthTitle == monthGroupTitle);
            if (group == null)
            {
                group = new ProductionMeetingGroupModel { MonthTitle = monthGroupTitle };
                GroupedHistory.Add(group);
                var sorted = GroupedHistory.OrderByDescending(g => g.MonthTitle).ToList();
                GroupedHistory.Clear();
                foreach (var s in sorted) GroupedHistory.Add(s);
            }

            var newReport = new ProductionMeetingReportModel
            {
                Title = title,
                ShortTitle = $"{selectedDate:MM.dd} ({selectedDate:ddd})",
                DateRange = dateRange
            };

            var lastReport = GroupedHistory
                .SelectMany(g => g.Reports)
                .OrderByDescending(r => ParseReportDate(r.DateRange))
                .FirstOrDefault();
            if (lastReport != null)
            {
                foreach (var b in lastReport.Blocks.Where(x => x.Status != "종결"))
                {
                    var copied = new ProductionMeetingBlockModel { Category = b.Category, Content = b.Content, FollowUp = b.FollowUp, Status = b.Status };
                    foreach (var att in b.FollowUpAttachments) copied.FollowUpAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
                    newReport.Blocks.Add(copied);
                }
            }

            group.Reports.Add(newReport);
            var sortedReports = group.Reports.OrderByDescending(r => ParseReportDate(r.DateRange)).ToList();
            group.Reports.Clear();
            foreach (var r in sortedReports) group.Reports.Add(r);

            RenumberBlocks(newReport);
            SetCurrentReport(newReport);
            SaveToStorage();
            CreateReportModal.Visibility = Visibility.Collapsed;
        }

        private static DateTime ParseReportDate(string dateText)
        {
            if (DateTime.TryParseExact(dateText, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            return DateTime.MinValue;
        }

        private void BtnCancelCreate_Click(object sender, RoutedEventArgs e) => CreateReportModal.Visibility = Visibility.Collapsed;

        // ==========================================
        // 5. 기타 블록 및 첨부파일 제어 (Draft 기반)
        // ==========================================
        private void BtnAddBlock_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            _draftReport.Blocks.Add(new ProductionMeetingBlockModel { Category = "신규 업무" });
            RenumberBlocks(_draftReport);
            UpdateOverviewStats();
        }

        private void BtnDeleteBlock_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            if (sender is Button { Tag: ProductionMeetingBlockModel block })
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
            if (sender is not Button { Tag: ProductionMeetingAttachmentModel attachment } || _draftReport == null) return;
            _draftReport.MemoAttachments.Remove(attachment);
            _isDirty = true;
            UpdateOverviewStats();
        }

        public void ShowReportTable() => BtnShowTable_Click(this, new RoutedEventArgs());
        private void BtnCloseTable_Click(object sender, RoutedEventArgs e) => TableModalOverlay.Visibility = Visibility.Collapsed;
        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null || _draftReport.Blocks.Count == 0)
            {
                MessageBox.Show("내보낼 데이터가 없습니다.", "엑셀 내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var templateDialog = new OpenFileDialog
            {
                Title = "엑셀 서식 파일 선택",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx"
            };
            if (templateDialog.ShowDialog() != true) return;

            var saveDialog = new SaveFileDialog
            {
                Title = "저장 위치 선택",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"{SanitizeFileName(_draftReport.Title)}_생산미팅.xlsx"
            };
            if (saveDialog.ShowDialog() != true) return;

            try
            {
                File.Copy(templateDialog.FileName, saveDialog.FileName, true);
                using var workbook = new XLWorkbook(saveDialog.FileName);
                var worksheet = workbook.Worksheets.FirstOrDefault() ?? workbook.AddWorksheet("생산미팅");

                WriteBlocksToWorksheet(worksheet, _draftReport.Title, _draftReport.Blocks.Cast<object>().ToList());

                workbook.Save();
                MessageBox.Show("엑셀 파일로 내보냈습니다.", "엑셀 내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 내보내기에 실패했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
            if (sender is not Button { Tag: ProductionMeetingBlockModel block }) return;
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "지원 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.pdf;*.xlsx;*.xls;*.doc;*.docx;*.ppt;*.pptx|모든 파일|*.*" };
            if (dialog.ShowDialog() != true) return;
            AddAttachments(block, dialog.FileNames);
        }

        private void BtnRemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: ProductionMeetingAttachmentModel attachment } || _draftReport == null) return;
            var owner = _draftReport.Blocks.FirstOrDefault(b => b.FollowUpAttachments.Contains(attachment));
            owner?.FollowUpAttachments.Remove(attachment);
        }

        private void AttachmentPreview_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ProductionMeetingAttachmentModel attachment }) return;
            string resolvedPath = ResolveAttachmentPath(attachment.FilePath);
            if (!File.Exists(resolvedPath))
            {
                MessageBox.Show("첨부 파일을 찾을 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (ProductionMeetingAttachmentModel.IsImagePath(resolvedPath)) ShowImagePopup(resolvedPath);
            else Process.Start(new ProcessStartInfo(resolvedPath) { UseShellExecute = true });
        }

        private void AttachmentPanel_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void AttachmentPanel_Drop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ProductionMeetingBlockModel block }) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            AddAttachments(block, files);
            e.Handled = true;
        }

        private void AttachmentPanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ProductionMeetingBlockModel block }) return;
            if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
            if (!Clipboard.ContainsImage()) return;
            string saved = SaveClipboardImage();
            if (!string.IsNullOrWhiteSpace(saved)) AddAttachments(block, new[] { saved });
            e.Handled = true;
        }

        private static void AddAttachments(ProductionMeetingBlockModel block, IEnumerable<string> files)
        {
            foreach (var file in files.Where(File.Exists))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (!AllowedExtensions.Contains(ext)) continue;
                string persistedPath = PersistAttachment(file);
                if (string.IsNullOrWhiteSpace(persistedPath)) continue;
                if (block.FollowUpAttachments.Any(a => string.Equals(a.FilePath, persistedPath, StringComparison.OrdinalIgnoreCase))) continue;
                block.FollowUpAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = persistedPath });
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
                string persistedPath = PersistAttachment(file);
                if (string.IsNullOrWhiteSpace(persistedPath)) continue;
                if (_draftReport.MemoAttachments.Any(a => string.Equals(a.FilePath, persistedPath, StringComparison.OrdinalIgnoreCase))) continue;
                _draftReport.MemoAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = persistedPath });
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
                if (fullSourcePath.StartsWith(AttachmentStorageRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return fullSourcePath;
                }

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

        private static void RenumberBlocks(ProductionMeetingReportModel report)
        {
            for (int i = 0; i < report.Blocks.Count; i++) report.Blocks[i].Number = i + 1;
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static string StoragePath => AppPaths.ProductionMeetingFilePath;

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
                    var mappedGroup = new ProductionMeetingGroupModel { MonthTitle = group.MonthTitle ?? "" };
                    foreach (var report in group.Reports ?? new())
                    {
                        var mappedReport = new ProductionMeetingReportModel
                        {
                            Id = string.IsNullOrWhiteSpace(report.Id) ? Guid.NewGuid().ToString() : report.Id,
                            Title = report.Title ?? "",
                            ShortTitle = report.ShortTitle ?? "",
                            DateRange = report.DateRange ?? "",
                            Memo = report.Memo ?? ""
                        };

                        foreach (var block in report.Blocks ?? new())
                        {
                            var mappedBlock = new ProductionMeetingBlockModel
                            {
                                Number = block.Number,
                                Category = block.Category ?? "",
                                Status = string.IsNullOrWhiteSpace(block.Status) ? "진행 중" : block.Status,
                                Content = block.Content ?? "",
                                FollowUp = block.FollowUp ?? ""
                            };
                            foreach (var att in block.FollowUpAttachments ?? new())
                            {
                                if (!string.IsNullOrWhiteSpace(att.FilePath))
                                    mappedBlock.FollowUpAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
                            }
                            mappedReport.Blocks.Add(mappedBlock);
                        }

                        foreach (var att in report.MemoAttachments ?? new())
                        {
                            if (!string.IsNullOrWhiteSpace(att.FilePath))
                                mappedReport.MemoAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
                        }

                        mappedGroup.Reports.Add(mappedReport);
                    }
                    GroupedHistory.Add(mappedGroup);
                }
            }
            catch { }
        }

        private void SaveToStorage()
        {
            try
            {
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
                        Blocks = r.Blocks.Select(b => new PersistedBlock
                        {
                            Number = b.Number,
                            Category = b.Category,
                            Status = b.Status,
                            Content = b.Content,
                            FollowUp = b.FollowUp,
                            FollowUpAttachments = b.FollowUpAttachments.Select(a => new PersistedAttachment { FilePath = a.FilePath }).ToList()
                        }).ToList(),
                        MemoAttachments = r.MemoAttachments.Select(a => new PersistedAttachment { FilePath = a.FilePath }).ToList()
                    }).ToList()
                }).ToList();

                File.WriteAllText(StoragePath, JsonSerializer.Serialize(data, JsonOptions));
            }
            catch { }
        }

        private class PersistedGroup
        {
            public string? MonthTitle { get; set; }
            public List<PersistedReport> Reports { get; set; } = new();
        }

        private class PersistedReport
        {
            public string? Id { get; set; }
            public string? Title { get; set; }
            public string? ShortTitle { get; set; }
            public string? DateRange { get; set; }
            public string? Memo { get; set; }
            public List<PersistedBlock> Blocks { get; set; } = new();
            public List<PersistedAttachment> MemoAttachments { get; set; } = new();
        }

        private class PersistedBlock
        {
            public int Number { get; set; }
            public string? Category { get; set; }
            public string? Status { get; set; }
            public string? Content { get; set; }
            public string? FollowUp { get; set; }
            public List<PersistedAttachment> FollowUpAttachments { get; set; } = new();
        }

        private class PersistedAttachment
        {
            public string? FilePath { get; set; }
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

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        private static void WriteBlocksToWorksheet(IXLWorksheet worksheet, string title, List<object> blocks)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                worksheet.Cell(1, 1).Value = title;
            }

            int headerRow = FindHeaderRow(worksheet);
            if (headerRow < 1)
            {
                headerRow = 3;
                worksheet.Cell(headerRow, 1).Value = "No.";
                worksheet.Cell(headerRow, 2).Value = "분류 및 항목";
                worksheet.Cell(headerRow, 3).Value = "세부 내용 및 팔로업";
                worksheet.Cell(headerRow, 4).Value = "상태";
            }

            int startRow = headerRow + 1;
            int lastUsedRow = Math.Max(startRow + blocks.Count - 1, worksheet.LastRowUsed()?.RowNumber() ?? startRow);
            for (int row = startRow; row <= lastUsedRow; row++)
            {
                worksheet.Cell(row, 1).Clear(XLClearOptions.Contents);
                worksheet.Cell(row, 2).Clear(XLClearOptions.Contents);
                worksheet.Cell(row, 3).Clear(XLClearOptions.Contents);
                worksheet.Cell(row, 4).Clear(XLClearOptions.Contents);
            }

            for (int i = 0; i < blocks.Count; i++)
            {
                int row = startRow + i;
                dynamic block = blocks[i];
                worksheet.Cell(row, 1).Value = block.Number;
                worksheet.Cell(row, 2).Value = block.Category ?? "";
                worksheet.Cell(row, 3).Value = block.FormattedContent ?? "";
                worksheet.Cell(row, 4).Value = block.Status ?? "";
                worksheet.Cell(row, 3).Style.Alignment.WrapText = true;
            }
        }

        private static int FindHeaderRow(IXLWorksheet worksheet)
        {
            int maxRow = Math.Min(20, worksheet.LastRowUsed()?.RowNumber() ?? 20);
            for (int row = 1; row <= maxRow; row++)
            {
                string c1 = worksheet.Cell(row, 1).GetString();
                string c2 = worksheet.Cell(row, 2).GetString();
                if (c1.Contains("No", StringComparison.OrdinalIgnoreCase) &&
                    (c2.Contains("분류") || c2.Contains("항목")))
                {
                    return row;
                }
            }
            return -1;
        }
    }
}