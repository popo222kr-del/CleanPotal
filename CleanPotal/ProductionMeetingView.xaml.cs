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
    // 데이터 모델 정의
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

        private ProductionMeetingReportModel? _currentReport;
        private ProductionMeetingReportModel? _draftReport;
        private ObservableCollection<ProductionMeetingBlockModel>? _subscribedBlocks;
        private ObservableCollection<ProductionMeetingAttachmentModel>? _subscribedMemoAttachments;

        private bool _isDirty = false;
        private bool _isNavigating = false;

        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".pdf", ".xlsx", ".xls", ".doc", ".docx", ".ppt", ".pptx" };
        private static readonly string AttachmentStorageRoot = Path.Combine(AppPaths.DataRoot, "production_meeting_attachments");

        public ProductionMeetingView()
        {
            InitializeComponent();
            GroupedHistoryControl.ItemsSource = GroupedHistory;
            InitCreateModal();
            LoadFromStorage();
        }

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
                foreach (var att in block.FollowUpAttachments) clonedBlock.FollowUpAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
                clone.Blocks.Add(clonedBlock);
            }
            foreach (var attachment in original.MemoAttachments) clone.MemoAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = attachment.FilePath });
            return clone;
        }

        private void SetCurrentReport(ProductionMeetingReportModel report)
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

            TxtCurrentReportTitle.Text = _draftReport.Title;
            TxtCurrentReportDate.Text = _draftReport.DateRange;
            ReportBlocksControl.ItemsSource = _draftReport.Blocks;

            _isDirty = false;
            UpdateOverviewStats();
        }

        private void DraftReport_PropertyChanged(object? sender, PropertyChangedEventArgs e) => _isDirty = true;
        private void Block_PropertyChanged(object? sender, PropertyChangedEventArgs e) { _isDirty = true; UpdateOverviewStats(); }

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

        private void MemoAttachments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { _isDirty = true; UpdateOverviewStats(); }

        private void Blocks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _isDirty = true;
            if (e.NewItems != null) foreach (ProductionMeetingBlockModel item in e.NewItems) item.PropertyChanged += Block_PropertyChanged;
            if (e.OldItems != null) foreach (ProductionMeetingBlockModel item in e.OldItems) item.PropertyChanged -= Block_PropertyChanged;
            if (_draftReport != null) RenumberBlocks(_draftReport);
            UpdateOverviewStats();
        }

        public void SaveReportChanges() => BtnSaveContent_Click(this, new RoutedEventArgs());

        private void BtnSaveContent_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReport == null || _draftReport == null) return;
            CommitActiveEditorChanges();

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
                foreach (var att in block.FollowUpAttachments) clonedBlock.FollowUpAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
                _currentReport.Blocks.Add(clonedBlock);
            }
            _currentReport.MemoAttachments.Clear();
            foreach (var attachment in _draftReport.MemoAttachments) _currentReport.MemoAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = attachment.FilePath });

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

            if (sender is ListBox lb && lb.SelectedItem is ProductionMeetingReportModel selected)
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
                var result = MessageBox.Show("해당 업무 항목을 정말 삭제하시겠습니까?", "항목 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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

        // ==========================================
        // 🔥 생산미팅 엑셀 자동 생성 및 내보내기 (템플릿 불필요)
        // ==========================================
        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null || _draftReport.Blocks.Count == 0)
            {
                MessageBox.Show("내보낼 데이터가 없습니다.", "엑셀 내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 요청하신 파일명 형식: 생산미팅_26년 4월 16일 목요일.xlsx
            string defaultFileName = "생산미팅.xlsx";
            string reportTitle = _draftReport.Title + " 생산미팅";

            if (DateTime.TryParseExact(_draftReport.DateRange, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                string dateStr = dt.ToString("yy년 M월 d일 dddd", new CultureInfo("ko-KR")); // 예: 26년 4월 16일 목요일
                defaultFileName = $"생산미팅_{dateStr}.xlsx";
                reportTitle = $"{dateStr} 생산미팅";
            }
            else
            {
                defaultFileName = $"생산미팅_{SanitizeFileName(_draftReport.Title)}.xlsx";
            }

            // 바로 저장 위치 선택 창 띄우기
            var saveDialog = new SaveFileDialog
            {
                Title = "저장 위치 선택",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = defaultFileName
            };

            if (saveDialog.ShowDialog() != true) return;

            try
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("생산미팅");

                // 1. 타이틀 영역 생성 (A1)
                ws.Cell(1, 1).Value = reportTitle;
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 18;
                ws.Cell(1, 1).Style.Font.FontColor = XLColor.Black;
                ws.Range(1, 1, 1, 4).Merge(); // A1 ~ D1 병합

                // 2. 표 헤더 생성 (3행)
                int headerRow = 3;
                ws.Cell(headerRow, 1).Value = "No.";
                ws.Cell(headerRow, 2).Value = "분류 및 항목";
                ws.Cell(headerRow, 3).Value = "세부 내용 및 팔로업";
                ws.Cell(headerRow, 4).Value = "상태";

                // 헤더 스타일 적용 (이미지 디자인 반영: 옅은 파란색 배경, 파란색 글씨)
                var headerRange = ws.Range(headerRow, 1, headerRow, 4);
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E6F0FF");
                headerRange.Style.Font.FontColor = XLColor.FromHtml("#1E3A8A");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                headerRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#B4C6E7");
                headerRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#B4C6E7");

                // 3. 데이터 로우 채우기
                int currentRow = 4;
                foreach (var block in _draftReport.Blocks)
                {
                    ws.Cell(currentRow, 1).Value = block.Number;
                    ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(currentRow, 2).Value = block.Category;
                    ws.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(currentRow, 3).Value = block.FormattedContent;
                    ws.Cell(currentRow, 3).Style.Alignment.WrapText = true; // 내용 줄바꿈
                    ws.Cell(currentRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    ws.Cell(currentRow, 4).Value = block.Status;
                    ws.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    currentRow++;
                }

                // 데이터 영역 테두리 및 중앙 정렬 적용
                if (_draftReport.Blocks.Count > 0)
                {
                    var dataRange = ws.Range(4, 1, currentRow - 1, 4);
                    dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    dataRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#B4C6E7");
                    dataRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#B4C6E7");
                }

                // 4. 컬럼 너비 비율에 맞게 쾌적하게 조정
                ws.Column(1).Width = 6;
                ws.Column(2).Width = 25;
                ws.Column(3).Width = 80;
                ws.Column(4).Width = 12;

                // 엑셀 파일 저장
                workbook.SaveAs(saveDialog.FileName);

                // 생성 직후 바로 열기
                if (MessageBox.Show("엑셀 파일이 성공적으로 생성되었습니다.\n지금 바로 열어서 확인하시겠습니까?", "생성 완료", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(saveDialog.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 자동 생성에 실패했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnShowTable_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            TxtModalTitle.Text = $"{_draftReport.Title} 주간보고";
            ReportDataGrid.ItemsSource = null;
            RenumberBlocks(_draftReport);
            ReportDataGrid.ItemsSource = _draftReport.Blocks;
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
    }
}