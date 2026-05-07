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
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

namespace CleanPotal
{
    // 🔥 블록 종류 (생산 미팅 본문 블록 타입)
    public enum BlockKind
    {
        Task,        // 업무 항목 (기존 동작 - 기본값)
        Memo,        // 일반 메모
        Checklist,   // 체크리스트
        Issue,       // 이슈/리스크
        Decision,    // 결정사항
        FollowUp,    // 후속조치
        Heading,     // 섹션 제목
        Divider      // 구분선
    }

    // 🔥 체크리스트 항목
    public class ChecklistItem : INotifyPropertyChanged
    {
        private bool _isDone;
        public bool IsDone
        {
            get => _isDone;
            set { if (_isDone == value) return; _isDone = value; OnPropertyChanged(); }
        }

        private string _text = "";
        public string Text
        {
            get => _text;
            set { if (_text == value) return; _text = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ==========================================
    // 데이터 모델 정의
    // ==========================================
    public class ProductionMeetingGroupModel
    {
        public string MonthTitle { get; set; } = "";
        public ObservableCollection<ProductionMeetingReportModel> Reports { get; set; } = new();
        public bool IsCurrentMonth { get; set; } = false;
    }

    public class ProductionMeetingReportModel : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string ShortTitle { get; set; } = "";
        public string DateRange { get; set; } = "";

        // 🔥 회의 메타 정보 (Phase 3)
        private string _attendees = "";
        public string Attendees
        {
            get => _attendees;
            set { if (_attendees == value) return; _attendees = value; OnPropertyChanged(); }
        }

        private string _summary = "";
        public string Summary
        {
            get => _summary;
            set { if (_summary == value) return; _summary = value; OnPropertyChanged(); }
        }

        private string _memo = "";
        public string Memo
        {
            get => _memo;
            set { if (_memo == value) return; _memo = value; OnPropertyChanged(); }
        }

        // 🔥 중앙 본문(평문) - 큰 메모장 형태의 자유로운 작성 공간
        private string _mainContent = "";
        public string MainContent
        {
            get => _mainContent;
            set { if (_mainContent == value) return; _mainContent = value; OnPropertyChanged(); }
        }

        // 🔥 중앙 본문(서식+이미지+체크박스 포함된 FlowDocument XAML)
        private string _mainContentRich = "";
        public string MainContentRich
        {
            get => _mainContentRich;
            set { if (_mainContentRich == value) return; _mainContentRich = value; OnPropertyChanged(); }
        }

        // 🔥 RichTextBox용 FlowDocument XAML (서식+이미지+체크박스 포함)
        // 비어있으면 RichTextBox는 평문 Memo를 fallback으로 표시
        private string _memoRich = "";
        public string MemoRich
        {
            get => _memoRich;
            set { if (_memoRich == value) return; _memoRich = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ProductionMeetingBlockModel> Blocks { get; set; } = new();
        public ObservableCollection<ProductionMeetingAttachmentModel> MemoAttachments { get; set; } = new();

        // 🔥 중앙 본문 별도 첨부 (본문 하단 영역)
        public ObservableCollection<ProductionMeetingAttachmentModel> MainAttachments { get; set; } = new();

        // 🎨 통계: Task 블록 기준 진행/완료/보류 카운트 (왼쪽 페이지 목록 칩에 사용)
        [System.Text.Json.Serialization.JsonIgnore]
        public string StatusSummary
        {
            get
            {
                var taskBlocks = Blocks.Where(b => b.Kind == BlockKind.Task || b.Kind == BlockKind.Issue || b.Kind == BlockKind.FollowUp).ToList();
                int inProg = taskBlocks.Count(b => b.Status == "진행 중" || b.Status == "검토 필요");
                int done = taskBlocks.Count(b => b.Status == "완료");
                int hold = taskBlocks.Count(b => b.Status == "보류" || b.Status == "취소");
                return $"진행 {inProg} · 완료 {done} · 보류 {hold}";
            }
        }

        // 🎨 통계 갱신용 (Block 변경/추가/삭제 시 호출)
        public void NotifyStatusSummaryChanged() => OnPropertyChanged(nameof(StatusSummary));

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

        // 🔥 RichTextBox용 FlowDocument XAML (각 카드의 내용/팔로업 서식+이미지 보존)
        private string _contentRich = "";
        public string ContentRich
        {
            get => _contentRich;
            set { if (_contentRich == value) return; _contentRich = value; OnPropertyChanged(); }
        }

        private string _followUpRich = "";
        public string FollowUpRich
        {
            get => _followUpRich;
            set { if (_followUpRich == value) return; _followUpRich = value; OnPropertyChanged(); }
        }

        private string _status = "진행 중";
        public string Status
        {
            get => _status;
            set { if (_status == value) return; _status = value; OnPropertyChanged(); }
        }

        // 🔥 블록 종류 (기본값 Task → 기존 데이터 호환)
        private BlockKind _kind = BlockKind.Task;
        public BlockKind Kind
        {
            get => _kind;
            set { if (_kind == value) return; _kind = value; OnPropertyChanged(); OnPropertyChanged(nameof(KindDisplay)); OnPropertyChanged(nameof(KindColor)); }
        }

        // 🔥 제목 블록용 (Heading), 섹션 제목 텍스트
        private string _heading = "";
        public string Heading
        {
            get => _heading;
            set { if (_heading == value) return; _heading = value; OnPropertyChanged(); }
        }

        // 🔥 블록 접기 상태
        private bool _isCollapsed = false;
        public bool IsCollapsed
        {
            get => _isCollapsed;
            set { if (_isCollapsed == value) return; _isCollapsed = value; OnPropertyChanged(); }
        }

        // 🔥 진행률 (0~100)
        private int? _progressPercent;
        public int? ProgressPercent
        {
            get => _progressPercent;
            set { if (_progressPercent == value) return; _progressPercent = value; OnPropertyChanged(); }
        }

        // 🔥 중요도 (낮음/보통/높음/긴급)
        private string _importance = "보통";
        public string Importance
        {
            get => _importance;
            set { if (_importance == value) return; _importance = value; OnPropertyChanged(); }
        }

        // 🔥 체크리스트 항목들 (Checklist 블록 전용)
        public ObservableCollection<ChecklistItem> ChecklistItems { get; set; } = new();

        // 🎨 카드 선택 상태 (좌측 컬러바 강조용)
        private bool _isSelectedCard;
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsSelectedCard
        {
            get => _isSelectedCard;
            set { if (_isSelectedCard == value) return; _isSelectedCard = value; OnPropertyChanged(); }
        }

        // 🎨 블록 종류별 컬러바 색상 (좌측 라벨)
        [System.Text.Json.Serialization.JsonIgnore]
        public string KindColor
        {
            get
            {
                return Kind switch
                {
                    BlockKind.Task => "#3B82F6",       // 파랑
                    BlockKind.Memo => "#94A3B8",       // 회색
                    BlockKind.Checklist => "#A855F7",  // 보라
                    BlockKind.Issue => "#EF4444",      // 빨강
                    BlockKind.Decision => "#10B981",   // 초록
                    BlockKind.FollowUp => "#F59E0B",   // 주황
                    BlockKind.Heading => "#0F172A",    // 검정
                    BlockKind.Divider => "#E5E7EB",    // 연회색
                    _ => "#3B82F6"
                };
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string KindDisplay
        {
            get
            {
                return Kind switch
                {
                    BlockKind.Task => "업무",
                    BlockKind.Memo => "메모",
                    BlockKind.Checklist => "체크",
                    BlockKind.Issue => "이슈",
                    BlockKind.Decision => "결정",
                    BlockKind.FollowUp => "후속",
                    BlockKind.Heading => "제목",
                    BlockKind.Divider => "구분",
                    _ => ""
                };
            }
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

        // 인라인 이미지 드래그-이동 상태
        private InlineUIContainer? _movingImageContainer;
        private string _movingImagePath = "";
        private Point _imageDragStartPoint;
        private bool _imageDragStarted = false;

        // 메모 첨부 드래그-정렬 상태
        private ProductionMeetingAttachmentModel? _memoDragSource;
        private Point _memoDragStartPoint;

        // 메인 첨부 드래그-정렬 상태
        private ProductionMeetingAttachmentModel? _mainDragSource;
        private Point _mainDragStartPoint;

        private bool _isDirty = false;
        private bool _isNavigating = false;
        private ListBox? _activeHistoryListBox;
        private double _zoomLevel = 1.0;

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
                Memo = original.Memo,
                MemoRich = original.MemoRich,
                Attendees = original.Attendees,
                Summary = original.Summary
            };
            foreach (var block in original.Blocks)
            {
                var clonedBlock = new ProductionMeetingBlockModel
                {
                    Number = block.Number,
                    Category = block.Category,
                    Status = block.Status,
                    Content = block.Content,
                    FollowUp = block.FollowUp,
                    ContentRich = block.ContentRich,
                    FollowUpRich = block.FollowUpRich,
                    Kind = block.Kind,
                    Heading = block.Heading,
                    IsCollapsed = block.IsCollapsed,
                    ProgressPercent = block.ProgressPercent,
                    Importance = block.Importance
                };
                foreach (var ci in block.ChecklistItems) clonedBlock.ChecklistItems.Add(new ChecklistItem { IsDone = ci.IsDone, Text = ci.Text });
                foreach (var att in block.FollowUpAttachments) clonedBlock.FollowUpAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
                clone.Blocks.Add(clonedBlock);
            }
            foreach (var attachment in original.MemoAttachments) clone.MemoAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = attachment.FilePath });
            foreach (var attachment in original.MainAttachments) clone.MainAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = attachment.FilePath });
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

            // 🔥 RichTextBox에 메모 로드 (MemoRich 우선, 없으면 평문 Memo)
            LoadMemoIntoRichEditor(_draftReport);

            // 🔥 중앙 본문 RichTextBox에 MainContent 로드 (MainContentRich 우선, 없으면 평문)
            LoadMainContentIntoRichEditor(_draftReport);

            _isDirty = false;
            UpdateOverviewStats();
        }

        // 🔥 메모 로드: MemoRich(FlowDocument XAML)가 있으면 그걸로, 없으면 평문 Memo로 fallback
        private bool _suppressMemoTextChanged = false;
        private void LoadMemoIntoRichEditor(ProductionMeetingReportModel report)
        {
            if (MemoRichEditor == null) return;
            _suppressMemoTextChanged = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(report.MemoRich))
                {
                    try
                    {
                        using var sr = new StringReader(report.MemoRich);
                        using var xr = XmlReader.Create(sr);
                        if (XamlReader.Load(xr) is FlowDocument doc)
                        {
                            doc.PageWidth = 99999;
                            MemoRichEditor.Document = doc;
                        }
                        else
                        {
                            var fallback = new FlowDocument(new Paragraph(new Run(report.Memo ?? ""))) { PageWidth = 99999 };
                            MemoRichEditor.Document = fallback;
                        }
                    }
                    catch
                    {
                        var fallback = new FlowDocument(new Paragraph(new Run(report.Memo ?? ""))) { PageWidth = 99999 };
                        MemoRichEditor.Document = fallback;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(report.Memo))
                {
                    var doc = new FlowDocument { PageWidth = 99999 };
                    foreach (var line in report.Memo.Replace("\r\n", "\n").Split('\n'))
                        doc.Blocks.Add(new Paragraph(new Run(line)));
                    MemoRichEditor.Document = doc;
                }
                else
                {
                    MemoRichEditor.Document = new FlowDocument { PageWidth = 99999 };
                }
            }
            finally
            {
                _suppressMemoTextChanged = false;
            }
            ReattachInteractiveElements(MemoRichEditor);
        }

        // 🔥 RichTextBox → 모델: FlowDocument를 XAML 문자열로 직렬화 + 평문도 함께 보관
        private void SyncMemoFromRichEditor()
        {
            if (MemoRichEditor == null || _draftReport == null) return;

            // FlowDocument를 XAML 문자열로 저장
            try
            {
                var doc = MemoRichEditor.Document;
                var sb = new System.Text.StringBuilder();
                using var xw = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true });
                XamlWriter.Save(doc, xw);
                _draftReport.MemoRich = sb.ToString();
            }
            catch { _draftReport.MemoRich = ""; }

            // 평문 추출 (검색/엑셀에서 활용되도록 호환성 유지)
            try
            {
                var range = new TextRange(MemoRichEditor.Document.ContentStart, MemoRichEditor.Document.ContentEnd);
                _draftReport.Memo = range.Text?.Trim() ?? "";
            }
            catch { _draftReport.Memo = ""; }
        }

        private void MemoRichEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressMemoTextChanged) return;
            _isDirty = true;
        }

        // 🔥 중앙 본문(MainContent) 로드 - 메모 영역과 동일한 방식
        private bool _suppressMainContentTextChanged = false;
        private void LoadMainContentIntoRichEditor(ProductionMeetingReportModel report)
        {
            if (MainContentRichEditor == null) return;
            _suppressMainContentTextChanged = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(report.MainContentRich))
                {
                    try
                    {
                        using var sr = new StringReader(report.MainContentRich);
                        using var xr = XmlReader.Create(sr);
                        if (XamlReader.Load(xr) is FlowDocument doc)
                        {
                            doc.PageWidth = 99999;
                            MainContentRichEditor.Document = doc;
                        }
                        else
                        {
                            var fallback = new FlowDocument(new Paragraph(new Run(report.MainContent ?? ""))) { PageWidth = 99999 };
                            MainContentRichEditor.Document = fallback;
                        }
                    }
                    catch
                    {
                        var fallback = new FlowDocument(new Paragraph(new Run(report.MainContent ?? ""))) { PageWidth = 99999 };
                        MainContentRichEditor.Document = fallback;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(report.MainContent))
                {
                    var doc = new FlowDocument { PageWidth = 99999 };
                    foreach (var line in report.MainContent.Replace("\r\n", "\n").Split('\n'))
                        doc.Blocks.Add(new Paragraph(new Run(line)));
                    MainContentRichEditor.Document = doc;
                }
                else
                {
                    MainContentRichEditor.Document = new FlowDocument { PageWidth = 99999 };
                }
            }
            finally
            {
                _suppressMainContentTextChanged = false;
            }
            ReattachInteractiveElements(MainContentRichEditor);
        }

        // 🔥 RichTextBox 안 모든 Image와 파일 박스에 다시 ContextMenu 등 부착
        // (FlowDocument XAML로 저장/복원 시 이벤트와 ContextMenu가 손실되므로 재바인딩)
        private void ReattachInteractiveElements(RichTextBox rtb)
        {
            if (rtb == null) return;
            try
            {
                foreach (Block block in rtb.Document.Blocks.ToList())
                {
                    ReattachInBlock(block);
                }
            }
            catch { }
        }

        private void ReattachInBlock(Block block)
        {
            if (block is Paragraph p)
            {
                foreach (Inline inline in p.Inlines.ToList())
                {
                    if (inline is InlineUIContainer iuc)
                    {
                        if (iuc.Child is Image img)
                        {
                            img.Cursor = Cursors.Hand;
                            img.MouseLeftButtonDown -= ImageInline_MouseLeftButtonDown;
                            img.MouseMove -= ImageInline_MouseMove;
                            img.MouseLeftButtonUp -= ImageInline_MouseLeftButtonUp;
                            AttachImageDragAndClick(img);
                            if (img.ContextMenu == null) AttachImageContextMenu(img);
                        }
                        else if (iuc.Child is Border fileBorder && fileBorder.Tag is string filePath)
                        {
                            // 파일 박스 - 더블클릭 열기 다시 부착
                            fileBorder.Cursor = Cursors.Hand;
                            fileBorder.MouseLeftButtonDown -= FileInline_MouseLeftButtonDown;
                            fileBorder.MouseLeftButtonDown += FileInline_MouseLeftButtonDown;
                            // 우클릭 메뉴 다시 부착
                            if (fileBorder.ContextMenu == null) AttachFileContextMenu(fileBorder, filePath);
                        }
                    }
                }
            }
        }

        // ── 인라인 이미지 드래그-이동 + 클릭 열기 ──────────────────────────────

        private void AttachImageDragAndClick(Image img)
        {
            img.MouseLeftButtonDown += ImageInline_MouseLeftButtonDown;
            img.MouseMove += ImageInline_MouseMove;
            img.MouseLeftButtonUp += ImageInline_MouseLeftButtonUp;
        }

        private void ImageInline_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Image img) return;
            _imageDragStartPoint = e.GetPosition(img);
            _imageDragStarted = false;

            if (img.Parent is InlineUIContainer iuc)
            {
                _movingImageContainer = iuc;
                _movingImagePath = img.Tag as string ?? "";
            }
            img.CaptureMouse();
            e.Handled = true;
        }

        private void ImageInline_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not Image img) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (_movingImageContainer == null) return;

            var pos = e.GetPosition(img);
            if (!_imageDragStarted &&
                (Math.Abs(pos.X - _imageDragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(pos.Y - _imageDragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                _imageDragStarted = true;
                img.ReleaseMouseCapture();
                var data = new DataObject("MoveInlineImage", _movingImagePath);
                DragDrop.DoDragDrop(img, data, DragDropEffects.Move);
            }
        }

        private void ImageInline_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Image img) return;
            img.ReleaseMouseCapture();
            if (!_imageDragStarted && _movingImageContainer != null)
            {
                // 드래그 없이 떼었으면 → 원본 보기
                if (img.Tag is string p) ShowImagePopup(p);
            }
            _movingImageContainer = null;
            _imageDragStarted = false;
        }

        private void FileInline_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (sender is Border bd && bd.Tag is string p && File.Exists(p))
            {
                try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
                catch { }
            }
        }

        private void AttachFileContextMenu(Border border, string filePath)
        {
            var menu = new ContextMenu();
            var miOpen = new MenuItem { Header = "열기" };
            miOpen.Click += (_, __) =>
            {
                if (File.Exists(filePath))
                {
                    try { Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true }); }
                    catch { }
                }
            };
            menu.Items.Add(miOpen);
            var miFolder = new MenuItem { Header = "파일 위치 열기" };
            miFolder.Click += (_, __) =>
            {
                if (File.Exists(filePath))
                {
                    try { Process.Start("explorer.exe", $"/select,\"{filePath}\""); }
                    catch { }
                }
            };
            menu.Items.Add(miFolder);
            border.ContextMenu = menu;
        }

        // 🔥 중앙 본문(MainContent) 모델 동기화
        private void SyncMainContentFromRichEditor()
        {
            if (MainContentRichEditor == null || _draftReport == null) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                using var xw = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true });
                XamlWriter.Save(MainContentRichEditor.Document, xw);
                _draftReport.MainContentRich = sb.ToString();
            }
            catch { _draftReport.MainContentRich = ""; }

            try
            {
                var range = new TextRange(MainContentRichEditor.Document.ContentStart, MainContentRichEditor.Document.ContentEnd);
                _draftReport.MainContent = range.Text?.Trim() ?? "";
            }
            catch { _draftReport.MainContent = ""; }
        }

        private void MainContentRichEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressMainContentTextChanged) return;
            _isDirty = true;
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

        private void AutoSaveCurrentReport()
        {
            if (_currentReport == null || _draftReport == null) return;
            CommitActiveEditorChanges();
            SyncMemoFromRichEditor();
            SyncMainContentFromRichEditor();

            _currentReport.Memo = _draftReport.Memo;
            _currentReport.MemoRich = _draftReport.MemoRich;
            _currentReport.MainContent = _draftReport.MainContent;
            _currentReport.MainContentRich = _draftReport.MainContentRich;
            _currentReport.Attendees = _draftReport.Attendees;
            _currentReport.Summary = _draftReport.Summary;
            _currentReport.Blocks.Clear();
            foreach (var block in _draftReport.Blocks)
            {
                var clonedBlock = new ProductionMeetingBlockModel
                {
                    Number = block.Number,
                    Category = block.Category,
                    Status = block.Status,
                    Content = block.Content,
                    FollowUp = block.FollowUp,
                    ContentRich = block.ContentRich,
                    FollowUpRich = block.FollowUpRich,
                    Kind = block.Kind,
                    Heading = block.Heading,
                    IsCollapsed = block.IsCollapsed,
                    ProgressPercent = block.ProgressPercent,
                    Importance = block.Importance
                };
                foreach (var ci in block.ChecklistItems) clonedBlock.ChecklistItems.Add(new ChecklistItem { IsDone = ci.IsDone, Text = ci.Text });
                foreach (var att in block.FollowUpAttachments) clonedBlock.FollowUpAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
                _currentReport.Blocks.Add(clonedBlock);
            }
            _currentReport.MemoAttachments.Clear();
            foreach (var att in _draftReport.MemoAttachments) _currentReport.MemoAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
            _currentReport.MainAttachments.Clear();
            foreach (var att in _draftReport.MainAttachments) _currentReport.MainAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });

            _isDirty = false;
            SaveToStorage();
        }

        private void BtnSaveContent_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReport == null || _draftReport == null) return;
            CommitActiveEditorChanges();
            SyncMemoFromRichEditor(); // 🔥 RichTextBox 내용을 _draftReport.MemoRich/Memo에 반영
            SyncMainContentFromRichEditor(); // 🔥 중앙 본문 동기화

            _currentReport.Memo = _draftReport.Memo;
            _currentReport.MemoRich = _draftReport.MemoRich;
            _currentReport.MainContent = _draftReport.MainContent;
            _currentReport.MainContentRich = _draftReport.MainContentRich;
            _currentReport.Attendees = _draftReport.Attendees;
            _currentReport.Summary = _draftReport.Summary;
            _currentReport.Blocks.Clear();

            foreach (var block in _draftReport.Blocks)
            {
                var clonedBlock = new ProductionMeetingBlockModel
                {
                    Number = block.Number,
                    Category = block.Category,
                    Status = block.Status,
                    Content = block.Content,
                    FollowUp = block.FollowUp,
                    ContentRich = block.ContentRich,
                    FollowUpRich = block.FollowUpRich,
                    Kind = block.Kind,
                    Heading = block.Heading,
                    IsCollapsed = block.IsCollapsed,
                    ProgressPercent = block.ProgressPercent,
                    Importance = block.Importance
                };
                foreach (var ci in block.ChecklistItems) clonedBlock.ChecklistItems.Add(new ChecklistItem { IsDone = ci.IsDone, Text = ci.Text });
                foreach (var att in block.FollowUpAttachments) clonedBlock.FollowUpAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
                _currentReport.Blocks.Add(clonedBlock);
            }
            _currentReport.MemoAttachments.Clear();
            foreach (var attachment in _draftReport.MemoAttachments) _currentReport.MemoAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = attachment.FilePath });
            _currentReport.MainAttachments.Clear();
            foreach (var attachment in _draftReport.MainAttachments) _currentReport.MainAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = attachment.FilePath });

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
                    // 다른 보고서로 이동 시 자동 저장
                    AutoSaveCurrentReport();
                }

                // 다른 달 ListBox의 선택 해제 (단일 선택 보장)
                if (_activeHistoryListBox != null && _activeHistoryListBox != lb)
                {
                    _isNavigating = true;
                    _activeHistoryListBox.SelectedItem = null;
                    _isNavigating = false;
                }
                _activeHistoryListBox = lb;

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
            if (_isDirty) AutoSaveCurrentReport();

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
            string currentMonthTitle = DateTime.Now.ToString("yyyy년 M월");
            var group = GroupedHistory.FirstOrDefault(g => g.MonthTitle == monthGroupTitle);
            if (group == null)
            {
                group = new ProductionMeetingGroupModel
                {
                    MonthTitle = monthGroupTitle,
                    IsCurrentMonth = (monthGroupTitle == currentMonthTitle)
                };
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
                    var copied = new ProductionMeetingBlockModel { Category = b.Category, Content = b.Content, FollowUp = b.FollowUp, ContentRich = b.ContentRich, FollowUpRich = b.FollowUpRich, Status = b.Status, Kind = b.Kind, Heading = b.Heading, Importance = b.Importance };
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

        // 🔥 기본 + 항목 추가 = Task 블록 (기존 동작 유지)
        private void BtnAddBlock_Click(object sender, RoutedEventArgs e)
        {
            AddBlockOfKind(BlockKind.Task);
        }

        // 🔥 블록 종류별 추가 (팝업 메뉴에서 호출)
        private void BtnAddBlockOfKind_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string kindName && Enum.TryParse<BlockKind>(kindName, out var kind))
            {
                AddBlockOfKind(kind);
            }
            // 팝업 닫기
            // AddBlockPopup 제거됨
        }

        private void AddBlockOfKind(BlockKind kind)
        {
            if (_draftReport == null) return;

            var newBlock = new ProductionMeetingBlockModel { Kind = kind };

            switch (kind)
            {
                case BlockKind.Task:
                    newBlock.Category = "신규 업무";
                    break;
                case BlockKind.Memo:
                    newBlock.Category = "메모";
                    break;
                case BlockKind.Checklist:
                    newBlock.Category = "체크리스트";
                    newBlock.ChecklistItems.Add(new ChecklistItem { Text = "" });
                    break;
                case BlockKind.Issue:
                    newBlock.Category = "이슈/리스크";
                    newBlock.Status = "검토 필요";
                    break;
                case BlockKind.Decision:
                    newBlock.Category = "결정사항";
                    newBlock.Status = "완료";
                    break;
                case BlockKind.FollowUp:
                    newBlock.Category = "후속조치";
                    break;
                case BlockKind.Heading:
                    newBlock.Heading = "섹션 제목";
                    break;
                case BlockKind.Divider:
                    // 구분선은 별도 정보 없음
                    break;
            }

            _draftReport.Blocks.Add(newBlock);
            RenumberBlocks(_draftReport);
            UpdateOverviewStats();
            _draftReport.NotifyStatusSummaryChanged();
            _isDirty = true;
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
                    _draftReport.NotifyStatusSummaryChanged();
                }
            }
        }

        // 🔥 블록 접기/펼치기
        private void BtnToggleCollapseBlock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ProductionMeetingBlockModel block)
            {
                block.IsCollapsed = !block.IsCollapsed;
                _isDirty = true;
            }
        }

        // 🔥 블록 위로 이동
        private void BtnMoveBlockUp_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            if (sender is FrameworkElement fe && fe.Tag is ProductionMeetingBlockModel block)
            {
                int idx = _draftReport.Blocks.IndexOf(block);
                if (idx > 0)
                {
                    _draftReport.Blocks.Move(idx, idx - 1);
                    RenumberBlocks(_draftReport);
                    _isDirty = true;
                }
            }
        }

        // 🔥 블록 아래로 이동
        private void BtnMoveBlockDown_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            if (sender is FrameworkElement fe && fe.Tag is ProductionMeetingBlockModel block)
            {
                int idx = _draftReport.Blocks.IndexOf(block);
                if (idx >= 0 && idx < _draftReport.Blocks.Count - 1)
                {
                    _draftReport.Blocks.Move(idx, idx + 1);
                    RenumberBlocks(_draftReport);
                    _isDirty = true;
                }
            }
        }

        // 🔥 체크리스트 항목 추가
        private void BtnAddChecklistItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ProductionMeetingBlockModel block && block.Kind == BlockKind.Checklist)
            {
                block.ChecklistItems.Add(new ChecklistItem { Text = "" });
                _isDirty = true;
            }
        }

        // 🔥 체크리스트 항목 삭제
        private void BtnRemoveChecklistItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ChecklistItem ci)
            {
                // 어느 블록에 속해있는지 찾기
                if (_draftReport != null)
                {
                    foreach (var block in _draftReport.Blocks)
                    {
                        if (block.ChecklistItems.Remove(ci))
                        {
                            _isDirty = true;
                            break;
                        }
                    }
                }
            }
        }

        // 🔥 + 항목 추가 버튼 클릭 → 팝업 열기
        private void BtnOpenAddBlockMenu_Click(object sender, RoutedEventArgs e)
        {
            // AddBlockPopup 제거됨
        }

        // 🔥 카드 클릭 시 우측 패널이 그 블록의 상세 정보 표시
        private ProductionMeetingBlockModel? _selectedBlock;
        private void BlockCard_Click(object sender, RoutedEventArgs e)
        {
            // 큰 메모장 모드로 변경되면서 블록 상세 패널은 사용하지 않음
        }

        // 🔥 우측 패널: 메모 모드로 복귀 (블록 상세 닫기)
        private void BtnCloseBlockDetail_Click(object sender, RoutedEventArgs e)
        {
            // 큰 메모장 모드로 변경되면서 블록 상세 패널은 사용하지 않음
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


        // ==========================================
        // 🔥 통합 리치 에디터 - 공유 플로팅 툴바, 통합 핸들러
        // ==========================================

        // 현재 포커스를 가진 RichTextBox (Memo / 카드 Content / 카드 FollowUp 중 하나)
        private RichTextBox? _activeRichEditor;

        // 통합 포커스 핸들러 - 어떤 RichTextBox든 포커스 받으면 활성화
        private void AnyRichTextBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is RichTextBox rtb)
            {
                _activeRichEditor = rtb;
                if (SharedToolbar != null) SharedToolbar.Visibility = Visibility.Visible;
            }
        }

        private void AnyRichTextBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // 다른 RichTextBox 또는 툴바로 이동하는 경우는 유지
            if (e.NewFocus is RichTextBox) return;
            if (e.NewFocus is DependencyObject d && IsDescendantOfToolbar(d)) return;
            if (SharedToolbar != null) SharedToolbar.Visibility = Visibility.Collapsed;
            _activeRichEditor = null;
        }

        private bool IsDescendantOfToolbar(DependencyObject d)
        {
            if (SharedToolbar == null) return false;
            DependencyObject? cur = d;
            while (cur != null)
            {
                if (cur == SharedToolbar) return true;
                cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
            }
            return false;
        }

        // 카드 RichTextBox - 데이터 로드 (FlowDocument XAML 또는 평문 fallback)
        private bool _suppressCardTextChanged = false;
        private void CardRichTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not RichTextBox rtb || rtb.DataContext is not ProductionMeetingBlockModel block) return;
            string targetField = (rtb.Tag as string) ?? "";
            string rich = targetField == "Content" ? block.ContentRich : block.FollowUpRich;
            string plain = targetField == "Content" ? block.Content : block.FollowUp;

            _suppressCardTextChanged = true;
            try
            {
                rtb.Document = new FlowDocument();
                if (!string.IsNullOrWhiteSpace(rich))
                {
                    try
                    {
                        using var sr = new StringReader(rich);
                        using var xr = XmlReader.Create(sr);
                        if (XamlReader.Load(xr) is FlowDocument doc) rtb.Document = doc;
                    }
                    catch
                    {
                        rtb.Document = new FlowDocument(new Paragraph(new Run(plain ?? "")));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(plain))
                {
                    var doc = new FlowDocument();
                    foreach (var line in plain.Replace("\r\n", "\n").Split('\n'))
                        doc.Blocks.Add(new Paragraph(new Run(line)));
                    rtb.Document = doc;
                }
            }
            finally { _suppressCardTextChanged = false; }
        }

        // 카드 RichTextBox - 텍스트 변경 시 모델로 동기화
        private void CardRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressCardTextChanged) return;
            if (sender is not RichTextBox rtb || rtb.DataContext is not ProductionMeetingBlockModel block) return;
            string targetField = (rtb.Tag as string) ?? "";

            // 평문 추출
            string plain = "";
            try
            {
                var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
                plain = range.Text?.Trim() ?? "";
            }
            catch { }

            // FlowDocument XAML로 직렬화
            string rich = "";
            try
            {
                var sb = new System.Text.StringBuilder();
                using var xw = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true });
                XamlWriter.Save(rtb.Document, xw);
                rich = sb.ToString();
            }
            catch { }

            if (targetField == "Content")
            {
                block.Content = plain;
                block.ContentRich = rich;
            }
            else if (targetField == "FollowUp")
            {
                block.FollowUp = plain;
                block.FollowUpRich = rich;
            }
            _isDirty = true;
        }

        private void CardRichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 단축키 통합 처리
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.B) { ApplyToggleProperty(TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal); e.Handled = true; }
                else if (e.Key == Key.I) { ApplyToggleProperty(TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal); e.Handled = true; }
                else if (e.Key == Key.U) { ApplyUnderlineToggle(); e.Handled = true; }
                else if (e.Key == Key.D1) { ApplyFontSize(18); e.Handled = true; }
                else if (e.Key == Key.D2) { ApplyFontSize(16); e.Handled = true; }
                else if (e.Key == Key.D3) { ApplyFontSize(14); e.Handled = true; }
                else if (e.Key == Key.V && Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null)
                    {
                        string saved = SaveClipboardImageInline(img);
                        if (!string.IsNullOrWhiteSpace(saved)) InsertImageIntoActiveEditor(saved);
                        e.Handled = true;
                    }
                }
            }
            else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (e.Key == Key.L) { InsertCheckboxIntoActive(); e.Handled = true; }
            }
        }

        // 드래그앤드롭 통합 처리 (모든 RichTextBox 공통)
        private void AnyRichTextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("MoveInlineImage"))
            {
                e.Effects = e.Data.GetDataPresent("MoveInlineImage") ? DragDropEffects.Move : DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void AnyRichTextBox_Drop(object sender, DragEventArgs e)
        {
            if (sender is not RichTextBox rtb) return;

            // 인라인 이미지 이동
            if (e.Data.GetDataPresent("MoveInlineImage") && _movingImageContainer != null)
            {
                try
                {
                    var dropPos = rtb.GetPositionFromPoint(e.GetPosition(rtb), true);
                    if (dropPos != null)
                    {
                        // 원래 위치에서 제거
                        if (_movingImageContainer.Parent is Paragraph oldPara)
                            oldPara.Inlines.Remove(_movingImageContainer);

                        // 새 위치에 삽입
                        rtb.CaretPosition = dropPos;
                        _activeRichEditor = rtb;
                        InsertImageIntoActiveEditor(_movingImagePath);
                    }
                }
                catch { }
                _movingImageContainer = null;
                _movingImagePath = "";
                _imageDragStarted = false;
                e.Handled = true;
                return;
            }

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

            try
            {
                var pos = rtb.GetPositionFromPoint(e.GetPosition(rtb), true);
                if (pos != null) rtb.CaretPosition = pos;
            }
            catch { }
            _activeRichEditor = rtb;

            foreach (var file in files.Where(File.Exists))
            {
                if (ProductionMeetingAttachmentModel.IsImagePath(file))
                    InsertImageIntoActiveEditor(file);
                else
                    InsertFileIntoActiveEditor(file);
            }
            e.Handled = true;
        }

        // ===== 공유 툴바 핸들러 =====

        private void ToolbarBold_Click(object sender, RoutedEventArgs e) => ApplyToggleProperty(TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal);
        private void ToolbarItalic_Click(object sender, RoutedEventArgs e) => ApplyToggleProperty(TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal);
        private void ToolbarUnderline_Click(object sender, RoutedEventArgs e) => ApplyUnderlineToggle();

        private void ToolbarFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
            if (!double.TryParse(item.Content?.ToString(), out double size)) return;
            ApplyFontSize(size);
        }

        private void ToolbarColor_Click(object sender, RoutedEventArgs e)
        {
            if (_activeRichEditor == null || sender is not Button btn || btn.Tag is not string colorHex) return;
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                _activeRichEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                _isDirty = true;
                _activeRichEditor.Focus();
            }
            catch { }
        }

        private void ToolbarHighlight_Click(object sender, RoutedEventArgs e)
        {
            if (_activeRichEditor == null || sender is not Button btn || btn.Tag is not string colorHex) return;
            try
            {
                var current = _activeRichEditor.Selection.GetPropertyValue(TextElement.BackgroundProperty) as SolidColorBrush;
                var target = (Color)ColorConverter.ConvertFromString(colorHex);
                if (current != null && current.Color == target)
                    _activeRichEditor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
                else
                    _activeRichEditor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(target));
                _isDirty = true;
                _activeRichEditor.Focus();
            }
            catch { }
        }

        private void ToolbarBullet_Click(object sender, RoutedEventArgs e)
        {
            if (_activeRichEditor == null) return;
            EditingCommands.ToggleBullets.Execute(null, _activeRichEditor);
            _isDirty = true;
            _activeRichEditor.Focus();
        }

        private void ToolbarNumbering_Click(object sender, RoutedEventArgs e)
        {
            if (_activeRichEditor == null) return;
            EditingCommands.ToggleNumbering.Execute(null, _activeRichEditor);
            _isDirty = true;
            _activeRichEditor.Focus();
        }

        private void ToolbarCheckbox_Click(object sender, RoutedEventArgs e) => InsertCheckboxIntoActive();

        private void ToolbarInsertImage_Click(object sender, RoutedEventArgs e)
        {
            if (_activeRichEditor == null) return;
            var dialog = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp" };
            if (dialog.ShowDialog() != true) return;
            InsertImageIntoActiveEditor(dialog.FileName);
        }

        // ===== 헬퍼 메서드 =====

        private void ApplyToggleProperty(DependencyProperty prop, object onValue, object offValue)
        {
            if (_activeRichEditor == null) return;
            if (_activeRichEditor.Selection.IsEmpty) { _activeRichEditor.Focus(); return; }
            var current = _activeRichEditor.Selection.GetPropertyValue(prop);
            bool isOn = current != null && current.Equals(onValue);
            _activeRichEditor.Selection.ApplyPropertyValue(prop, isOn ? offValue : onValue);
            _isDirty = true;
        }

        private void ApplyUnderlineToggle()
        {
            if (_activeRichEditor == null || _activeRichEditor.Selection.IsEmpty) { _activeRichEditor?.Focus(); return; }
            var current = _activeRichEditor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            bool hasUnderline = current?.Any(d => d.Location == TextDecorationLocation.Underline) == true;
            _activeRichEditor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, hasUnderline ? null : TextDecorations.Underline);
            _isDirty = true;
        }

        private void ApplyFontSize(double size)
        {
            if (_activeRichEditor == null) return;
            if (_activeRichEditor.Selection.IsEmpty) { _activeRichEditor.Focus(); return; }
            _activeRichEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            _isDirty = true;
        }

        private void InsertCheckboxIntoActive()
        {
            if (_activeRichEditor == null) return;
            var caret = _activeRichEditor.CaretPosition;
            var checkbox = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Arrow
            };
            checkbox.Checked += (_, __) => UpdateCheckboxLineThrough(checkbox, true);
            checkbox.Unchecked += (_, __) => UpdateCheckboxLineThrough(checkbox, false);
            var container = new InlineUIContainer(checkbox, caret);
            _activeRichEditor.CaretPosition = container.ElementEnd;
            _activeRichEditor.Focus();
            _isDirty = true;
        }

        private void UpdateCheckboxLineThrough(CheckBox cb, bool strike)
        {
            try
            {
                var container = FindAncestorContainer(cb);
                if (container == null) return;
                var paragraph = container.Parent as Paragraph;
                if (paragraph == null) return;
                foreach (var inline in paragraph.Inlines.ToList())
                {
                    if (inline == container) continue;
                    if (inline is Run run)
                    {
                        run.TextDecorations = strike ? TextDecorations.Strikethrough : null;
                        run.Foreground = strike ? new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)) : new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
                    }
                }
            }
            catch { }
        }

        private static InlineUIContainer? FindAncestorContainer(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is InlineUIContainer c) return c;
                d = LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        private void InsertImageIntoActiveEditor(string sourcePath)
        {
            if (_activeRichEditor == null) return;
            try
            {
                string persistedPath = PersistAttachment(sourcePath);
                if (string.IsNullOrWhiteSpace(persistedPath)) return;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(persistedPath);
                bmp.EndInit();
                bmp.Freeze();

                double maxWidth = 320;
                double width = bmp.PixelWidth > maxWidth ? maxWidth : bmp.PixelWidth;
                double ratio = bmp.PixelHeight / (double)bmp.PixelWidth;
                double height = width * ratio;

                var img = new Image
                {
                    Source = bmp,
                    Width = width,
                    Height = height,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 4, 0, 4),
                    Cursor = Cursors.Hand,
                    Tag = persistedPath
                };
                AttachImageDragAndClick(img);

                // 🔥 이미지 우클릭 메뉴 - 크기 조절
                AttachImageContextMenu(img);

                var container = new InlineUIContainer(img, _activeRichEditor.CaretPosition);
                _activeRichEditor.CaretPosition = container.ElementEnd;
                _activeRichEditor.Focus();
                _isDirty = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("이미지 삽입 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // 🔥 간단한 인라인 입력 다이얼로그 (Microsoft.VisualBasic 의존 없음)
        private static string? ShowSimpleInputDialog(string title, string prompt, string initialValue = "")
        {
            var dlg = new Window
            {
                Title = title,
                Width = 360,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = prompt, FontSize = 13, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")), Margin = new Thickness(0, 0, 0, 8) });
            var tb = new TextBox { Text = initialValue, FontSize = 14, Padding = new Thickness(8, 6, 8, 6) };
            sp.Children.Add(tb);
            var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            string? result = null;
            var btnOk = new Button { Content = "확인", Width = 70, Height = 28, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            btnOk.Click += (_, __) => { result = tb.Text; dlg.Close(); };
            var btnCancel = new Button { Content = "취소", Width = 70, Height = 28, IsCancel = true };
            btnCancel.Click += (_, __) => { dlg.Close(); };
            bp.Children.Add(btnOk);
            bp.Children.Add(btnCancel);
            sp.Children.Add(bp);
            dlg.Content = sp;
            tb.Focus();
            tb.SelectAll();
            dlg.ShowDialog();
            return result;
        }

        // 🔥 이미지에 ContextMenu(우클릭) 부착 - 크기 조절 메뉴
        private void AttachImageContextMenu(Image img)
        {
            var menu = new ContextMenu();

            void ResizeImage(double newWidth)
            {
                if (img.Source is BitmapSource src && src.PixelWidth > 0)
                {
                    double ratio = src.PixelHeight / (double)src.PixelWidth;
                    img.Width = newWidth;
                    img.Height = newWidth * ratio;
                    _isDirty = true;
                }
            }

            var miSmall = new MenuItem { Header = "작게 (200px)" };
            miSmall.Click += (_, __) => ResizeImage(200);
            menu.Items.Add(miSmall);

            var miMedium = new MenuItem { Header = "중간 (400px)" };
            miMedium.Click += (_, __) => ResizeImage(400);
            menu.Items.Add(miMedium);

            var miLarge = new MenuItem { Header = "크게 (640px)" };
            miLarge.Click += (_, __) => ResizeImage(640);
            menu.Items.Add(miLarge);

            menu.Items.Add(new Separator());

            var miCustom = new MenuItem { Header = "직접 입력 (px)…" };
            miCustom.Click += (_, __) =>
            {
                string? input = ShowSimpleInputDialog("이미지 크기 조절", "가로 크기를 px 단위로 입력하세요 (예: 280)", img.Width.ToString("0"));
                if (!string.IsNullOrWhiteSpace(input) && double.TryParse(input, out double w) && w >= 50 && w <= 1600)
                {
                    ResizeImage(w);
                }
            };
            menu.Items.Add(miCustom);

            menu.Items.Add(new Separator());

            var miOpen = new MenuItem { Header = "원본 보기" };
            miOpen.Click += (_, __) =>
            {
                if (img.Tag is string p) ShowImagePopup(p);
            };
            menu.Items.Add(miOpen);

            img.ContextMenu = menu;
        }

        // 🔥 파일 인라인 삽입 (본문에 파일 아이콘 박스로 삽입)
        private void InsertFileIntoActiveEditor(string sourcePath)
        {
            if (_activeRichEditor == null) return;
            try
            {
                string persistedPath = PersistAttachment(sourcePath);
                if (string.IsNullOrWhiteSpace(persistedPath)) return;

                string ext = Path.GetExtension(persistedPath).ToLowerInvariant();
                string fileName = Path.GetFileName(persistedPath);

                // 확장자별 아이콘과 색상
                (string icon, string color) = ext switch
                {
                    ".docx" or ".doc" => ("📄", "#2563EB"),
                    ".xlsx" or ".xls" or ".csv" => ("📊", "#16A34A"),
                    ".pptx" or ".ppt" => ("📑", "#EA580C"),
                    ".pdf" => ("📕", "#DC2626"),
                    ".zip" or ".rar" or ".7z" => ("🗜️", "#7C3AED"),
                    ".txt" or ".md" => ("📝", "#475569"),
                    _ => ("📎", "#64748B")
                };

                // 파일 박스 UI
                var border = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    BorderThickness = new Thickness(1, 1, 1, 1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 2, 0, 2),
                    Cursor = Cursors.Hand,
                    Tag = persistedPath,
                    ToolTip = $"{fileName}\n더블클릭하면 열립니다"
                };

                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock
                {
                    Text = icon,
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                sp.Children.Add(new TextBlock
                {
                    Text = fileName,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                border.Child = sp;

                // 더블클릭 시 시스템 기본 프로그램으로 열기
                border.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount == 2 && border.Tag is string p && File.Exists(p))
                    {
                        try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
                        catch { }
                    }
                };

                // 우클릭 메뉴
                var menu = new ContextMenu();
                var miOpen = new MenuItem { Header = "열기" };
                miOpen.Click += (_, __) =>
                {
                    if (border.Tag is string p && File.Exists(p))
                    {
                        try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
                        catch { }
                    }
                };
                menu.Items.Add(miOpen);
                var miFolder = new MenuItem { Header = "파일 위치 열기" };
                miFolder.Click += (_, __) =>
                {
                    if (border.Tag is string p && File.Exists(p))
                    {
                        try { Process.Start("explorer.exe", $"/select,\"{p}\""); }
                        catch { }
                    }
                };
                menu.Items.Add(miFolder);
                border.ContextMenu = menu;

                var container = new InlineUIContainer(border, _activeRichEditor.CaretPosition);
                _activeRichEditor.CaretPosition = container.ElementEnd;
                _activeRichEditor.Focus();
                _isDirty = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("파일 삽입 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // 🔥 툴바 파일 첨부 버튼
        private void ToolbarInsertFile_Click(object sender, RoutedEventArgs e)
        {
            if (_activeRichEditor == null) return;
            var dialog = new OpenFileDialog { Filter = "모든 파일|*.*", Multiselect = true };
            if (dialog.ShowDialog() != true) return;
            foreach (var f in dialog.FileNames)
            {
                InsertFileIntoActiveEditor(f);
            }
        }

        private string SaveClipboardImageInline(System.Windows.Media.Imaging.BitmapSource image)
        {
            try
            {
                EnsureAttachmentStorageDirectory();
                string path = Path.Combine(AttachmentStorageRoot, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png");
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(fs);
                return path;
            }
            catch { return ""; }
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

        // 🔥 본문 별도 첨부 - + 버튼
        private void BtnAddMainAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (_draftReport == null) return;
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "지원 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.pdf;*.xlsx;*.xls;*.doc;*.docx;*.ppt;*.pptx|모든 파일|*.*" };
            if (dialog.ShowDialog() != true) return;
            AddMainAttachments(dialog.FileNames);
        }

        // 🔥 본문 별도 첨부 - 삭제
        private void BtnRemoveMainAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: ProductionMeetingAttachmentModel attachment } || _draftReport == null) return;
            _draftReport.MainAttachments.Remove(attachment);
            _isDirty = true;
        }

        // 🔥 본문 별도 첨부 - 드래그 오버
        private void MainAttachmentPanel_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        // 🔥 본문 별도 첨부 - 드롭
        private void MainAttachmentPanel_Drop(object sender, DragEventArgs e)
        {
            if (_draftReport == null) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            AddMainAttachments(files);
            e.Handled = true;
        }

        // 🔥 본문 별도 첨부 - 키 (Delete 등 처리 필요시 확장)
        private void MainAttachmentPanel_PreviewKeyDown(object sender, KeyEventArgs e) { }

        // 🔥 본문 별도 첨부 - 추가 (저장 + 모델 반영)
        private void AddMainAttachments(IEnumerable<string> files)
        {
            if (_draftReport == null) return;
            foreach (var f in files)
            {
                if (string.IsNullOrWhiteSpace(f) || !File.Exists(f)) continue;
                string persistedPath = PersistAttachment(f);
                if (string.IsNullOrWhiteSpace(persistedPath)) continue;
                if (_draftReport.MainAttachments.Any(a => string.Equals(a.FilePath, persistedPath, StringComparison.OrdinalIgnoreCase))) continue;
                _draftReport.MainAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = persistedPath });
            }
            _isDirty = true;
        }

        // ==========================================
        // 검색 기능
        // ==========================================
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
                ApplySearchFilter(tb.Text);
        }

        private void ApplySearchFilter(string query)
        {
            var q = query.Trim().ToLower();
            if (string.IsNullOrEmpty(q))
            {
                GroupedHistoryControl.ItemsSource = GroupedHistory;
                return;
            }

            var filtered = GroupedHistory
                .Select(g => new ProductionMeetingGroupModel
                {
                    MonthTitle = g.MonthTitle,
                    Reports = new ObservableCollection<ProductionMeetingReportModel>(
                        g.Reports.Where(r =>
                            r.Title.ToLower().Contains(q) ||
                            r.ShortTitle.ToLower().Contains(q) ||
                            r.MainContent.ToLower().Contains(q) ||
                            r.Memo.ToLower().Contains(q) ||
                            r.Blocks.Any(b =>
                                b.Category.ToLower().Contains(q) ||
                                b.Content.ToLower().Contains(q)
                            )
                        )
                    )
                })
                .Where(g => g.Reports.Count > 0)
                .ToList();

            GroupedHistoryControl.ItemsSource = filtered;
        }

        // ==========================================
        // 화면 확대/축소
        // ==========================================
        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (_zoomLevel >= 2.0) return;
            _zoomLevel = Math.Round(_zoomLevel + 0.1, 1);
            ApplyZoom();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (_zoomLevel <= 0.5) return;
            _zoomLevel = Math.Round(_zoomLevel - 0.1, 1);
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            MainScaleTransform.ScaleX = _zoomLevel;
            MainScaleTransform.ScaleY = _zoomLevel;
            TxtZoomLevel.Text = $"{(int)(_zoomLevel * 100)}%";
        }

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

        // ── 메인 첨부 드래그-정렬 ──────────────────────────────────────────────

        private void MainAttachmentItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProductionMeetingAttachmentModel att)
            {
                _mainDragSource = att;
                _mainDragStartPoint = e.GetPosition(fe);
            }
        }

        private void MainAttachmentItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _mainDragSource == null) return;
            if (sender is not FrameworkElement fe) return;
            var pos = e.GetPosition(fe);
            if (Math.Abs(pos.X - _mainDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _mainDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var data = new DataObject("AttachmentReorder", _mainDragSource);
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
        }

        private void MainAttachmentItem_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("AttachmentReorder") ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void MainAttachmentItem_Drop(object sender, DragEventArgs e)
        {
            if (_draftReport == null || _mainDragSource == null) return;
            if (!e.Data.GetDataPresent("AttachmentReorder")) return;
            if (sender is not FrameworkElement fe || fe.DataContext is not ProductionMeetingAttachmentModel target) return;
            if (ReferenceEquals(_mainDragSource, target)) return;

            var list = _draftReport.MainAttachments;
            int fromIdx = list.IndexOf(_mainDragSource);
            int toIdx = list.IndexOf(target);
            if (fromIdx < 0 || toIdx < 0) return;
            list.Move(fromIdx, toIdx);
            _isDirty = true;
            _mainDragSource = null;
            e.Handled = true;
        }

        // ── 메모 첨부 드래그-정렬 ──────────────────────────────────────────────

        private void MemoAttachmentItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProductionMeetingAttachmentModel att)
            {
                _memoDragSource = att;
                _memoDragStartPoint = e.GetPosition(fe);
            }
        }

        private void MemoAttachmentItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _memoDragSource == null) return;
            if (sender is not FrameworkElement fe) return;
            var pos = e.GetPosition(fe);
            if (Math.Abs(pos.X - _memoDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _memoDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var data = new DataObject("AttachmentReorder", _memoDragSource);
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
        }

        private void MemoAttachmentItem_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("AttachmentReorder") ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void MemoAttachmentItem_Drop(object sender, DragEventArgs e)
        {
            if (_draftReport == null || _memoDragSource == null) return;
            if (!e.Data.GetDataPresent("AttachmentReorder")) return;
            if (sender is not FrameworkElement fe || fe.DataContext is not ProductionMeetingAttachmentModel target) return;
            if (ReferenceEquals(_memoDragSource, target)) return;

            var list = _draftReport.MemoAttachments;
            int fromIdx = list.IndexOf(_memoDragSource);
            int toIdx = list.IndexOf(target);
            if (fromIdx < 0 || toIdx < 0) return;
            list.Move(fromIdx, toIdx);
            _isDirty = true;
            _memoDragSource = null;
            e.Handled = true;
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
            // ReportBlocksControl 제거됨
            MemoArea.DataContext = null;
            // 🔥 RichEditor도 비우기
            if (MemoRichEditor != null)
            {
                _suppressMemoTextChanged = true;
                MemoRichEditor.Document = new FlowDocument();
                _suppressMemoTextChanged = false;
            }
            if (MainContentRichEditor != null)
            {
                _suppressMainContentTextChanged = true;
                MainContentRichEditor.Document = new FlowDocument();
                _suppressMainContentTextChanged = false;
            }
            _isDirty = false;
            UpdateOverviewStats();
        }

        private void UpdateOverviewStats()
        {
            // 큰 메모장 모드로 변경되면서 상단 통계 칩은 사용하지 않음
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

                string currentMonthTitle = DateTime.Now.ToString("yyyy년 M월");
                GroupedHistory.Clear();
                foreach (var group in data)
                {
                    var mappedGroup = new ProductionMeetingGroupModel
                    {
                        MonthTitle = group.MonthTitle ?? "",
                        IsCurrentMonth = (group.MonthTitle == currentMonthTitle)
                    };
                    foreach (var report in group.Reports ?? new())
                    {
                        var mappedReport = new ProductionMeetingReportModel
                        {
                            Id = string.IsNullOrWhiteSpace(report.Id) ? Guid.NewGuid().ToString() : report.Id,
                            Title = report.Title ?? "",
                            ShortTitle = report.ShortTitle ?? "",
                            DateRange = report.DateRange ?? "",
                            Memo = report.Memo ?? "",
                            MemoRich = report.MemoRich ?? "",
                            MainContent = report.MainContent ?? "",
                            MainContentRich = report.MainContentRich ?? "",
                            Attendees = report.Attendees ?? "",
                            Summary = report.Summary ?? ""
                        };

                        foreach (var block in report.Blocks ?? new())
                        {
                            var mappedBlock = new ProductionMeetingBlockModel
                            {
                                Number = block.Number,
                                Category = block.Category ?? "",
                                Status = string.IsNullOrWhiteSpace(block.Status) ? "진행 중" : block.Status,
                                Content = block.Content ?? "",
                                FollowUp = block.FollowUp ?? "",
                                ContentRich = block.ContentRich ?? "",
                                FollowUpRich = block.FollowUpRich ?? "",
                                Kind = block.Kind ?? BlockKind.Task,
                                Heading = block.Heading ?? "",
                                IsCollapsed = block.IsCollapsed ?? false,
                                ProgressPercent = block.ProgressPercent,
                                Importance = block.Importance ?? "보통"
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

                        foreach (var att in report.MainAttachments ?? new())
                        {
                            if (!string.IsNullOrWhiteSpace(att.FilePath))
                                mappedReport.MainAttachments.Add(new ProductionMeetingAttachmentModel { FilePath = att.FilePath });
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
                        MemoRich = r.MemoRich,
                        MainContent = r.MainContent,
                        MainContentRich = r.MainContentRich,
                        Attendees = r.Attendees,
                        Summary = r.Summary,
                        Blocks = r.Blocks.Select(b => new PersistedBlock
                        {
                            Number = b.Number,
                            Category = b.Category,
                            Status = b.Status,
                            Content = b.Content,
                            FollowUp = b.FollowUp,
                            ContentRich = b.ContentRich,
                            FollowUpRich = b.FollowUpRich,
                            Kind = b.Kind,
                            Heading = b.Heading,
                            IsCollapsed = b.IsCollapsed,
                            ProgressPercent = b.ProgressPercent,
                            Importance = b.Importance,
                            FollowUpAttachments = b.FollowUpAttachments.Select(a => new PersistedAttachment { FilePath = a.FilePath }).ToList()
                        }).ToList(),
                        MemoAttachments = r.MemoAttachments.Select(a => new PersistedAttachment { FilePath = a.FilePath }).ToList(),
                        MainAttachments = r.MainAttachments.Select(a => new PersistedAttachment { FilePath = a.FilePath }).ToList()
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
            public string? MemoRich { get; set; }
            public string? MainContent { get; set; }
            public string? MainContentRich { get; set; }
            public string? Attendees { get; set; }
            public string? Summary { get; set; }
            public List<PersistedBlock> Blocks { get; set; } = new();
            public List<PersistedAttachment> MemoAttachments { get; set; } = new();
            public List<PersistedAttachment> MainAttachments { get; set; } = new();
        }

        private class PersistedBlock
        {
            public int Number { get; set; }
            public string? Category { get; set; }
            public string? Status { get; set; }
            public string? Content { get; set; }
            public string? FollowUp { get; set; }
            public string? ContentRich { get; set; }
            public string? FollowUpRich { get; set; }
            public BlockKind? Kind { get; set; }
            public string? Heading { get; set; }
            public bool? IsCollapsed { get; set; }
            public int? ProgressPercent { get; set; }
            public string? Importance { get; set; }
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