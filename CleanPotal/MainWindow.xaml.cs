using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;

namespace CleanPotal
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // --------------------------
        // 파일명들
        // --------------------------
        private const string ButtonsFileName = "buttons.json";

        private const string LegacyHandoverFileName = "handover.json";
        private const string LegacyMigratedBakName = "handover_legacy_migrated.bak";

        private const string ActiveFileName = "handover_active.json";
        private const string DoneFileName = "handover_done.json";

        private const string DoneDeletedExcel = "인수인계 이력.xltx";
        private const string DoneDeletedSheet = "삭제기록";

        private const string ScheduleBoardXlsm = "세정스케쥴보드_rev1.10_220526.xlsm";
        private const string ScheduleBoardSheetName = "세정 스케쥴 보드";

        private string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // 날짜 표시(탭1/탭2 공통)
            TodayText = DateTime.Now.ToString("yyyy-MM-dd dddd");

            // 공지사항(탭2)
            NoticeText =
                "1) 공지 1.\n" +
                "2) 공지 2.\n" +
                "3) 공지 3.";

            StatusOptions = new ObservableCollection<string> { "진행", "보류", "완료" };
            EditStatus = "진행";
            EditInDate = DateTime.Today;
            EditOutDate = DateTime.Today;

            LoadPortalButtons();
            LoadHandoverAll();

            // 관리컬럼 숨김/표시 로직(체크박스 기반)
            HookManageCheckedEvents();
            UpdateManageColumnVisibility();

            RefreshProgressPreview();
        }

        // --------------------------
        // INotifyPropertyChanged
        // --------------------------
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _todayText = "";
        public string TodayText
        {
            get => _todayText;
            set { _todayText = value; OnPropertyChanged(); }
        }

        private string _noticeText = "";
        public string NoticeText
        {
            get => _noticeText;
            set { _noticeText = value; OnPropertyChanged(); }
        }

        private string _statusText = "로드 준비";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // --------------------------
        // 탭1: 포털(buttons.json)
        // --------------------------
        private class ButtonItem
        {
            public string title { get; set; } = "";
            public string path { get; set; } = "";
            public string type { get; set; } = "file"; // file/folder
        }
        private class ButtonGroup
        {
            public string group { get; set; } = "";
            public List<ButtonItem> items { get; set; } = new();
        }

        private void LoadPortalButtons()
        {
            try
            {
                string jsonPath = Path.Combine(BaseDir, ButtonsFileName);

                if (!File.Exists(jsonPath))
                {
                    StatusText = $"buttons.json 없음: {jsonPath}";
                    return;
                }

                string json = File.ReadAllText(jsonPath, Encoding.UTF8);
                var groups = JsonSerializer.Deserialize<List<ButtonGroup>>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? new List<ButtonGroup>();

                var map = new Dictionary<string, Button[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["A급 QTZ"] = new[] { Btn_A1, Btn_A2, Btn_A3, Btn_A4, Btn_A5 },
                    ["세메스"] = new[] { Btn_S1, Btn_S2, Btn_S3 },
                    ["기타"] = new[] { Btn_E1, Btn_E2, Btn_E3 },
                    ["폴더"] = new[] { Btn_F1, Btn_F2, Btn_F3, Btn_F4 },
                    ["DOME"] = new[] { Btn_D1, Btn_D2, Btn_D3 },
                    ["Daily"] = new[] { Btn_Daily1, Btn_Daily2 },
                };

                // 초기화
                foreach (var arr in map.Values)
                {
                    foreach (var b in arr)
                    {
                        b.Content = "";
                        b.Tag = null;
                        b.Click -= PortalButton_Click;
                        b.IsEnabled = false;
                    }
                }

                int totalButtons = 0;

                foreach (var g in groups)
                {
                    if (!map.TryGetValue(g.group, out var btns))
                        continue;

                    for (int i = 0; i < btns.Length && i < g.items.Count; i++)
                    {
                        var item = g.items[i];
                        var btn = btns[i];

                        btn.Content = item.title;
                        btn.Tag = item;
                        btn.IsEnabled = true;
                        btn.Click += PortalButton_Click;
                        totalButtons++;
                    }
                }

                StatusText = $"로드 완료. 그룹 {groups.Count}개 / 버튼 {totalButtons}개";
            }
            catch (Exception ex)
            {
                StatusText = $"buttons.json 로드 실패: {ex.Message}";
            }
        }

        private void PortalButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not ButtonItem item) return;

            try
            {
                if (string.IsNullOrWhiteSpace(item.path))
                    return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{item.path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"열기 실패: {ex.Message}", "오류");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // --------------------------
        // 탭2: 인수인계
        // --------------------------
        public ObservableCollection<string> StatusOptions { get; set; } = new();

        public class HandoverItem : INotifyPropertyChanged
        {
            public Guid Id { get; set; } = Guid.NewGuid();

            private string _vendor = "";
            public string Vendor
            {
                get => _vendor;
                set { _vendor = value; OnPropertyChanged(); }
            }

            private string _content = "";
            public string Content
            {
                get => _content;
                set { _content = value; OnPropertyChanged(); }
            }

            private DateTime? _inDate;
            public DateTime? InDate
            {
                get => _inDate;
                set { _inDate = value; OnPropertyChanged(); NotifyProgress(); }
            }

            private DateTime? _outDate;
            public DateTime? OutDate
            {
                get => _outDate;
                set { _outDate = value; OnPropertyChanged(); NotifyProgress(); }
            }

            private string _status = "진행";
            public string Status
            {
                get => _status;
                set { _status = value; OnPropertyChanged(); NotifyProgress(); }
            }

            private string _memo = "";
            public string Memo
            {
                get => _memo;
                set { _memo = value; OnPropertyChanged(); }
            }

            private bool _manageChecked;
            public bool ManageChecked
            {
                get => _manageChecked;
                set { _manageChecked = value; OnPropertyChanged(); }
            }

            public bool IsDone => string.Equals(Status, "완료", StringComparison.OrdinalIgnoreCase);

            public int ProgressPercent
            {
                get
                {
                    if (IsDone) return 100;
                    return CalcProgressPercent(DateTime.Today, InDate, OutDate);
                }
            }

            public string ProgressText => $"{ProgressPercent}%";

            public void NotifyProgress()
            {
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(ProgressPercent));
                OnPropertyChanged(nameof(ProgressText));
            }

            private static int CalcProgressPercent(DateTime today, DateTime? inDate, DateTime? outDate)
            {
                if (inDate == null || outDate == null) return 0;

                var start = inDate.Value.Date;
                var end = outDate.Value.Date;

                if (end <= start)
                    return today >= end ? 100 : 0;

                var totalDays = (end - start).TotalDays;
                var elapsed = (today - start).TotalDays;

                var ratio = elapsed / totalDays * 100.0;
                var p = (int)Math.Round(ratio, MidpointRounding.AwayFromZero);

                if (p < 0) p = 0;
                if (p > 100) p = 100;
                return p;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public ObservableCollection<HandoverItem> ActiveItems { get; set; } = new();
        public ObservableCollection<HandoverItem> DoneItems { get; set; } = new();

        // 관리 컬럼 표시/숨김 (체크박스 기반)
        private void HookManageCheckedEvents()
        {
            foreach (var it in ActiveItems)
                it.PropertyChanged += ActiveItem_PropertyChanged;

            ActiveItems.CollectionChanged += ActiveItems_CollectionChanged;
        }

        private void ActiveItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (HandoverItem it in e.OldItems)
                    it.PropertyChanged -= ActiveItem_PropertyChanged;

            if (e.NewItems != null)
                foreach (HandoverItem it in e.NewItems)
                    it.PropertyChanged += ActiveItem_PropertyChanged;

            UpdateManageColumnVisibility();
        }

        private void ActiveItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HandoverItem.ManageChecked))
                UpdateManageColumnVisibility();
        }

        private void UpdateManageColumnVisibility()
        {
            bool show = ActiveItems.Any(x => x.ManageChecked);

            // XAML에서 x:Name="ManageColumn"을 줬기 때문에 바로 접근 가능
            if (ManageColumn != null)
                ManageColumn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        // --------------------------
        // 등록/수정 바인딩 필드
        // --------------------------
        private Guid? _editId = null;

        private string _editVendor = "";
        public string EditVendor { get => _editVendor; set { _editVendor = value; OnPropertyChanged(); } }

        private string _editContent = "";
        public string EditContent { get => _editContent; set { _editContent = value; OnPropertyChanged(); } }

        private DateTime? _editInDate;
        public DateTime? EditInDate { get => _editInDate; set { _editInDate = value; OnPropertyChanged(); RefreshProgressPreview(); } }

        private DateTime? _editOutDate;
        public DateTime? EditOutDate { get => _editOutDate; set { _editOutDate = value; OnPropertyChanged(); RefreshProgressPreview(); } }

        private string _editStatus = "진행";
        public string EditStatus
        {
            get => _editStatus;
            set { _editStatus = value; OnPropertyChanged(); RefreshProgressPreview(); }
        }

        private string _editMemo = "";
        public string EditMemo { get => _editMemo; set { _editMemo = value; OnPropertyChanged(); } }

        private int _editProgressPercent = 0;
        public int EditProgressPercent { get => _editProgressPercent; set { _editProgressPercent = value; OnPropertyChanged(); } }

        private string _editProgressText = "0%";
        public string EditProgressText { get => _editProgressText; set { _editProgressText = value; OnPropertyChanged(); } }

        private void RefreshProgressPreview()
        {
            if (string.Equals(EditStatus, "완료", StringComparison.OrdinalIgnoreCase))
            {
                EditProgressPercent = 100;
                EditProgressText = "100%";
                return;
            }

            EditProgressPercent = CalcProgressPercent(DateTime.Today, EditInDate, EditOutDate);
            EditProgressText = $"{EditProgressPercent}%";
        }

        private static int CalcProgressPercent(DateTime today, DateTime? inDate, DateTime? outDate)
        {
            if (inDate == null || outDate == null) return 0;

            var start = inDate.Value.Date;
            var end = outDate.Value.Date;

            if (end <= start)
                return today >= end ? 100 : 0;

            var totalDays = (end - start).TotalDays;
            var elapsed = (today - start).TotalDays;

            var ratio = elapsed / totalDays * 100.0;
            var p = (int)Math.Round(ratio, MidpointRounding.AwayFromZero);

            if (p < 0) p = 0;
            if (p > 100) p = 100;
            return p;
        }

        // --------------------------
        // 로드/세이브
        // --------------------------
        private void LoadHandoverAll()
        {
            ActiveItems.Clear();
            DoneItems.Clear();

            string legacy = Path.Combine(BaseDir, LegacyHandoverFileName);
            string legacyBak = Path.Combine(BaseDir, LegacyMigratedBakName);

            // legacy 1회만 마이그레이션
            if (File.Exists(legacy) && !File.Exists(legacyBak))
            {
                var list = ReadJsonList(legacy);

                foreach (var it in list)
                {
                    it.ManageChecked = false;
                    it.NotifyProgress();
                    if (it.IsDone) DoneItems.Add(it);
                    else ActiveItems.Add(it);
                }

                ReorderDone();
                SaveAll();

                try { File.Move(legacy, legacyBak); } catch { }
                return;
            }

            // active/done 로드
            string activePath = Path.Combine(BaseDir, ActiveFileName);
            string donePath = Path.Combine(BaseDir, DoneFileName);

            foreach (var it in ReadJsonList(activePath))
            {
                it.ManageChecked = false;
                it.NotifyProgress();
                ActiveItems.Add(it);
            }

            foreach (var it in ReadJsonList(donePath))
            {
                it.ManageChecked = false;
                it.NotifyProgress();
                DoneItems.Add(it);
            }

            ReorderDone();
        }

        private List<HandoverItem> ReadJsonList(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<HandoverItem>();

                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<List<HandoverItem>>(json,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new List<HandoverItem>();
            }
            catch
            {
                return new List<HandoverItem>();
            }
        }

        private void SaveAll()
        {
            WriteJson(Path.Combine(BaseDir, ActiveFileName), ActiveItems.ToList());
            WriteJson(Path.Combine(BaseDir, DoneFileName), DoneItems.ToList());
        }

        private void WriteJson(string path, List<HandoverItem> list)
        {
            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        // --------------------------
        // 버튼 이벤트
        // --------------------------
        private void HandoverReset_Click(object sender, RoutedEventArgs e)
        {
            _editId = null;
            EditVendor = "";
            EditContent = "";
            EditInDate = DateTime.Today;
            EditOutDate = DateTime.Today;
            EditStatus = "진행";
            EditMemo = "";
            RefreshProgressPreview();
        }

        private void HandoverSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(EditVendor) && string.IsNullOrWhiteSpace(EditContent))
                {
                    MessageBox.Show("업체 또는 내용을 입력하세요.", "알림");
                    return;
                }

                var vendor = EditVendor?.Trim() ?? "";
                var content = EditContent ?? "";
                var memo = EditMemo ?? "";
                var status = EditStatus ?? "진행";

                if (_editId == null)
                {
                    var item = new HandoverItem
                    {
                        Id = Guid.NewGuid(),
                        Vendor = vendor,
                        Content = content,
                        InDate = EditInDate,
                        OutDate = EditOutDate,
                        Status = status,
                        Memo = memo,
                        ManageChecked = false
                    };

                    item.NotifyProgress();

                    if (item.IsDone) DoneItems.Add(item);
                    else ActiveItems.Add(item);
                }
                else
                {
                    var target = FindById(_editId.Value);
                    if (target == null) return;

                    // 완료 목록은 수정 금지
                    if (DoneItems.Any(x => x.Id == target.Id))
                    {
                        MessageBox.Show("완료 목록 항목은 수정할 수 없습니다.", "알림");
                        return;
                    }

                    // 여기서 property setter가 OnPropertyChanged를 올리므로 "저장 즉시" 그리드 반영됨
                    target.Vendor = vendor;
                    target.Content = content;
                    target.InDate = EditInDate;
                    target.OutDate = EditOutDate;
                    target.Status = status;
                    target.Memo = memo;

                    target.ManageChecked = false;
                    target.NotifyProgress();

                    // 완료로 바뀌면 이동
                    if (target.IsDone)
                    {
                        ActiveItems.Remove(target);
                        DoneItems.Add(target);
                    }
                }

                ReorderDone();
                SaveAll();

                UpdateManageColumnVisibility();
                HandoverReset_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}", "오류");
            }
        }

        private HandoverItem? FindById(Guid id)
        {
            var a = ActiveItems.FirstOrDefault(x => x.Id == id);
            if (a != null) return a;
            return DoneItems.FirstOrDefault(x => x.Id == id);
        }

        private void HandoverEdit_Click(object sender, RoutedEventArgs e)
        {
            var rowItem = (sender as FrameworkElement)?.DataContext as HandoverItem;
            if (rowItem == null) return;

            if (!rowItem.ManageChecked)
            {
                MessageBox.Show("체크한 항목만 수정할 수 있습니다.", "알림");
                return;
            }

            if (rowItem.IsDone)
            {
                MessageBox.Show("완료 항목은 수정할 수 없습니다.", "알림");
                return;
            }

            _editId = rowItem.Id;

            EditVendor = rowItem.Vendor;
            EditContent = rowItem.Content;
            EditInDate = rowItem.InDate ?? DateTime.Today;
            EditOutDate = rowItem.OutDate ?? DateTime.Today;
            EditStatus = rowItem.Status;
            EditMemo = rowItem.Memo;

            RefreshProgressPreview();
        }

        private void HandoverDelete_Click(object sender, RoutedEventArgs e)
        {
            var rowItem = (sender as FrameworkElement)?.DataContext as HandoverItem;
            if (rowItem == null) return;

            if (!rowItem.ManageChecked)
            {
                MessageBox.Show("체크한 항목만 삭제할 수 있습니다.", "알림");
                return;
            }

            if (rowItem.IsDone)
            {
                MessageBox.Show("완료 항목은 완료 목록에서 삭제하세요.", "알림");
                return;
            }

            var ok = MessageBox.Show("선택한 항목을 삭제할까요?", "확인", MessageBoxButton.YesNo);
            if (ok != MessageBoxResult.Yes) return;

            ActiveItems.Remove(rowItem);
            SaveAll();
            UpdateManageColumnVisibility();
            HandoverReset_Click(sender, e);
        }

        private void DoneDelete_Click(object sender, RoutedEventArgs e)
        {
            var rowItem = (sender as FrameworkElement)?.DataContext as HandoverItem;
            if (rowItem == null) return;

            var ok = MessageBox.Show(
                "완료 항목을 삭제하면 기록 후 목록에서 제거됩니다.\n삭제할까요?",
                "확인",
                MessageBoxButton.YesNo);

            if (ok != MessageBoxResult.Yes) return;

            try
            {
                AppendDoneDeletedToExcel(rowItem);
                DoneItems.Remove(rowItem);
                SaveAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 저장 실패: {ex.Message}", "오류");
            }
        }

        private void AppendDoneDeletedToExcel(HandoverItem item)
        {
            // ✅ 생성 위치: 실행파일 폴더(BaseDir)
            // 예) ...\bin\Debug\net10.0-windows\handover_deleted_done.xlsx
            string path = Path.Combine(BaseDir, DoneDeletedExcel);

            XLWorkbook wb;
            IXLWorksheet ws;

            if (File.Exists(path))
            {
                wb = new XLWorkbook(path);
                ws = wb.Worksheets.FirstOrDefault(x => x.Name == DoneDeletedSheet) ?? wb.AddWorksheet(DoneDeletedSheet);
            }
            else
            {
                wb = new XLWorkbook();
                ws = wb.AddWorksheet(DoneDeletedSheet);
            }

            if (ws.LastRowUsed() == null)
            {
                ws.Cell(1, 1).Value = "삭제일시";
                ws.Cell(1, 2).Value = "업체";
                ws.Cell(1, 3).Value = "내용";
                ws.Cell(1, 4).Value = "입고일";
                ws.Cell(1, 5).Value = "출고일";
                ws.Cell(1, 6).Value = "상태";
                ws.Cell(1, 7).Value = "메모";
                ws.Row(1).Style.Font.Bold = true;
            }

            int row = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;

            ws.Cell(row, 1).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cell(row, 2).Value = item.Vendor;
            ws.Cell(row, 3).Value = item.Content;
            ws.Cell(row, 4).Value = item.InDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 5).Value = item.OutDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 6).Value = item.Status;
            ws.Cell(row, 7).Value = item.Memo;

            ws.Columns().AdjustToContents();
            wb.SaveAs(path);
            wb.Dispose();

            // 필요하면 경로 안내(원치 않으면 삭제)
            // MessageBox.Show($"삭제기록 저장 완료\n{path}", "완료");
        }

        private void ReorderDone()
        {
            var ordered = DoneItems
                .OrderBy(x => x.OutDate ?? DateTime.MaxValue)
                .ToList();

            DoneItems.Clear();
            foreach (var it in ordered)
            {
                it.ManageChecked = false;
                it.NotifyProgress();
                DoneItems.Add(it);
            }
        }
    }
}