using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ClosedXML.Excel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Win32;
using SkiaSharp;
using CleanPotal.EquipmentAnalysis;

namespace CleanPotal
{
    public partial class EquipmentAnalysisView : UserControl
    {
        private static readonly string[] Elements = EquipmentAnalysisRepository.ElementCols;
        private List<EquipmentAnalysisRow> _all = new();
        private bool _loading;

        // 원소 선택 칩 ('전체' + 원소별). 기본은 전체.
        private ToggleButton? _chipAll;
        private readonly List<ToggleButton> _elemChips = new();

        // 날짜(달력) 필터
        private DateTime _calMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private readonly HashSet<DateTime> _selDates = new();   // 선택 날짜들(빈 집합=전체)
        private readonly HashSet<DateTime> _dataDates = new();  // 측정 기록 있는 날짜

        // 전체 삭제는 최고 관리자(1004)만 허용
        private static bool IsAdmin => SessionManager.CurrentUsername == "1004";

        private static bool _lvcConfigured;

        public EquipmentAnalysisView()
        {
            // 차트에 한글 폰트 적용(원소 라벨/설비 공정명 한글 □·짤림 방지). 앱 1회만.
            if (!_lvcConfigured)
            {
                _lvcConfigured = true;
                try { LiveCharts.Configure(c => c.HasGlobalSKTypeface(SKTypeface.FromFamilyName("Malgun Gothic"))); }
                catch { }
            }

            InitializeComponent();
            BuildElementChips();
            // 툴팁을 더 촘촘하게(원소 다수 선택 시 세로로 길어져 잘리는 것 완화)
            Chart.TooltipTextSize = 11;
            Chart.TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Top;
            FltProcess.SelectionChanged += Process_Changed;   // 설비 유형 → 약액·설비·날짜 옵션 갱신
            FltBath.SelectionChanged += Filter_MultiChanged;
            FltEquip.SelectionChanged += Filter_MultiChanged;
            // 관리자 아니면 '전체 삭제' 숨김
            BtnClear.Visibility = IsAdmin ? Visibility.Visible : Visibility.Collapsed;
            Loaded += (_, _) => ReloadAll();
        }

        private void Filter_MultiChanged(object? sender, EventArgs e) { if (_loading) return; RefreshDataDates(); Render(); }

        // 다른 페이지 갔다가 돌아오면 새로고침
        public void TryRefresh() { if (!_loading) ReloadAll(); }

        // ── 원소 칩 구성 ──
        private void BuildElementChips()
        {
            _loading = true;
            try
            {
                ChipPanel.Children.Clear();
                _elemChips.Clear();

                _chipAll = MakeChip("전체", true);
                ChipPanel.Children.Add(_chipAll);
                foreach (var el in Elements)
                {
                    var c = MakeChip(el, false);
                    _elemChips.Add(c);
                    ChipPanel.Children.Add(c);
                }
            }
            finally { _loading = false; }
        }

        private ToggleButton MakeChip(string label, bool check)
        {
            var t = new ToggleButton { Content = label, IsChecked = check, Style = (Style)FindResource("Chip") };
            t.Checked += Chip_Toggled;
            t.Unchecked += Chip_Toggled;
            return t;
        }

        private void Chip_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading || _chipAll == null) return;
            _loading = true;
            try
            {
                if (ReferenceEquals(sender, _chipAll))
                {
                    if (_chipAll.IsChecked == true)
                        foreach (var c in _elemChips) c.IsChecked = false;   // 전체 선택 → 개별 해제
                    else if (!_elemChips.Any(c => c.IsChecked == true))
                        _chipAll.IsChecked = true;                            // 아무것도 없으면 전체 유지
                }
                else
                {
                    _chipAll.IsChecked = !_elemChips.Any(c => c.IsChecked == true);   // 개별 선택 시 전체 해제
                }
            }
            finally { _loading = false; }
            Render();
        }

        // 선택된 원소(전체 or 없음 → 전 원소)
        private string[] SelectedElements()
        {
            if (_chipAll?.IsChecked == true) return Elements;
            var sel = _elemChips.Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToArray();
            return sel.Length == 0 ? Elements : sel;
        }

        // ── 데이터 로드/필터 ──
        private void ReloadAll()
        {
            try { _all = EquipmentAnalysisRepository.GetAll(); }
            catch { _all = new(); }
            try { _processMap = EquipmentAnalysisRepository.GetEquipmentMaster(); }
            catch { _processMap = new(); }

            // 설비 유형 옵션(전체 기준), 이후 유형에 맞춰 약액·설비·날짜 재구성
            _loading = true;
            try { ApplyOptions(FltProcess, _all.Select(r => r.ProcessType).Distinct().OrderBy(s => s)); }
            finally { _loading = false; }
            RefreshDependentFilters();

            // 최초 진입(또는 선택 없을 때): 날짜 필터 기본값을 '최신 측정일'로 지정
            // → 차트·카드가 최신일 데이터만 사용(해당일 분석 없는 설비는 차트에서 제외)
            if (_selDates.Count == 0 && _dataDates.Count > 0)
            {
                _selDates.Add(_dataDates.Max());
                UpdateDateButton();
            }

            Render();
            int eqCnt = _all.Select(r => r.EqId).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Count();
            TxtCount.Text = $"총 {_all.Count}행 · 설비 {eqCnt}대";
        }

        private void Process_Changed(object? sender, EventArgs e)
        {
            if (_loading) return;
            RefreshDependentFilters();
            Render();
        }

        // 모든 필터 초기화(설비유형·약액·설비·날짜·원소 전부 전체로)
        private void ResetFilters_Click(object sender, RoutedEventArgs e)
        {
            _loading = true;
            try
            {
                FltProcess.Clear();
                FltBath.Clear();
                FltEquip.Clear();
                _selDates.Clear();
                if (_chipAll != null) _chipAll.IsChecked = true;
                foreach (var c in _elemChips) c.IsChecked = false;
            }
            finally { _loading = false; }
            RefreshDependentFilters();   // 약액·설비 옵션/달력 전체 기준으로 재구성
            Render();
        }

        // 설비 유형(FltProcess) 선택에 맞춰 약액·설비·날짜(달력) 옵션을 그 유형 데이터로만 좁힌다.
        private void RefreshDependentFilters()
        {
            var proc = FltProcess.SelectedValues;
            var rows = _all.Where(r => !string.IsNullOrWhiteSpace(r.EqId));
            if (proc.Count > 0) rows = rows.Where(r => proc.Contains(r.ProcessType));
            var list = rows.ToList();

            _loading = true;
            try
            {
                ApplyOptions(FltBath, list.Select(r => r.BathGb).Distinct().OrderBy(s => s));
                ApplyOptions(FltEquip, list.Select(r => r.EqId).Distinct().OrderBy(s => s));
            }
            finally { _loading = false; }

            RefreshDataDates();
        }

        // 달력: 현재 선택된 설비유형·약액·설비 조합에 실제 분석 기록이 있는 날짜만 표시/선택 가능
        private void RefreshDataDates()
        {
            var proc = FltProcess.SelectedValues;
            var bath = FltBath.SelectedValues;
            var eq = FltEquip.SelectedValues;

            var rows = _all.Where(r => !string.IsNullOrWhiteSpace(r.EqId));
            if (proc.Count > 0) rows = rows.Where(r => proc.Contains(r.ProcessType));
            if (bath.Count > 0) rows = rows.Where(r => bath.Contains(r.BathGb));
            if (eq.Count > 0)   rows = rows.Where(r => eq.Contains(r.EqId));

            _dataDates.Clear();
            foreach (var d in rows.Select(r => r.AnalysisDate))
                if (DateTime.TryParse(d, out var dt)) _dataDates.Add(dt.Date);
            _selDates.RemoveWhere(d => !_dataDates.Contains(d));   // 데이터 없는 날짜 선택 해제
            if (_dataDates.Count > 0) _calMonth = new DateTime(_dataDates.Max().Year, _dataDates.Max().Month, 1);
            UpdateDateButton();
        }

        // ── 날짜 달력 ──
        private void UpdateDateButton()
        {
            DateText.Text = _selDates.Count == 0 ? "전체"
                : _selDates.Count == 1 ? _selDates.First().ToString("yyyy-MM-dd")
                : $"{_selDates.Min():yyyy-MM-dd} 외 {_selDates.Count - 1}";
        }

        private void DateButton_Click(object sender, RoutedEventArgs e)
        {
            BuildCalendar();
            DatePopup.IsOpen = true;
        }

        private void CalPrev_Click(object sender, RoutedEventArgs e) { _calMonth = _calMonth.AddMonths(-1); BuildCalendar(); }
        private void CalNext_Click(object sender, RoutedEventArgs e) { _calMonth = _calMonth.AddMonths(1); BuildCalendar(); }
        // 달력 내 초기화: 선택 날짜 전부 해제(팝업은 유지 → 바로 다시 선택 가능)
        private void CalReset_Click(object sender, RoutedEventArgs e)
        {
            _selDates.Clear();
            UpdateDateButton();
            BuildCalendar();
            Render();
        }

        private static SolidColorBrush Br(string hex) => new((Color)ColorConverter.ConvertFromString(hex)!);

        private void BuildCalendar()
        {
            CalTitle.Text = _calMonth.ToString("yyyy년 M월", CultureInfo.InvariantCulture);
            CalDays.Children.Clear();

            int lead = (int)new DateTime(_calMonth.Year, _calMonth.Month, 1).DayOfWeek; // 일=0
            for (int i = 0; i < lead; i++) CalDays.Children.Add(new Border());

            int days = DateTime.DaysInMonth(_calMonth.Year, _calMonth.Month);
            for (int d = 1; d <= days; d++)
            {
                var date = new DateTime(_calMonth.Year, _calMonth.Month, d);
                bool hasData = _dataDates.Contains(date);
                bool selected = _selDates.Contains(date);
                bool today = date == DateTime.Today;

                var ell = new Ellipse { Width = 30, Height = 30 };
                var tb = new TextBlock { Text = d.ToString(), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (selected) { ell.Fill = Br("#2563EB"); tb.Foreground = Brushes.White; tb.FontWeight = FontWeights.Bold; }
                else if (hasData) { ell.Fill = Br("#DBEAFE"); tb.Foreground = Br("#1D4ED8"); tb.FontWeight = FontWeights.SemiBold; }
                else { ell.Fill = Brushes.Transparent; tb.Foreground = Br("#374151"); }
                if (today && !selected) { ell.Stroke = Br("#93C5FD"); ell.StrokeThickness = 1.5; }

                var g = new Grid { Width = 34, Height = 34 };
                g.Children.Add(ell);
                g.Children.Add(tb);
                // 데이터 있는 날짜만 선택 가능(없는 날은 비활성)
                var btn = new Button { Content = g, Tag = date, Style = (Style)FindResource("CalDay"), IsEnabled = hasData };
                if (!hasData) { tb.Foreground = Br("#CBD5E1"); btn.Opacity = 0.55; }
                btn.Click += Day_Click;
                CalDays.Children.Add(btn);
            }
            // 6주 채우기(레이아웃 안정)
            int total = lead + days;
            for (int i = total; i < 42; i++) CalDays.Children.Add(new Border());
        }

        private void Day_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is DateTime date)
            {
                if (!_dataDates.Contains(date)) return;   // 데이터 없는 날짜는 선택 불가
                if (!_selDates.Remove(date)) _selDates.Add(date);   // 토글(복수 선택). 팝업은 열어둠
                UpdateDateButton();
                BuildCalendar();   // 선택 표시 갱신
                Render();
            }
        }

        // 옵션을 채우되 기존 선택은 유지(여전히 존재하는 값만)
        private static void ApplyOptions(MultiSelectFilter f, IEnumerable<string> src)
        {
            var prev = f.SelectedValues;
            var opts = src.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            f.SetOptions(opts);
            var keep = prev.Where(p => opts.Contains(p)).ToArray();
            if (keep.Length > 0) f.SetChecked(keep);
        }

        private bool IsTrendMode() => RbTrend?.IsChecked == true;

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            // XAML 파싱 중(InitializeComponent) RbBar IsChecked=True 가 먼저 발화 → 뒤 컨트롤 null 방지
            if (_loading || Chart == null || LblPeriod == null || CmbPeriod == null || FltEquip == null) return;
            Render();
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)   // CmbPeriod(단위)
        {
            if (_loading || Chart == null || LblPeriod == null || CmbPeriod == null) return;
            Render();
        }

        // 공정·약액·설비·날짜 복수선택 필터 (빈 선택 = 전체)
        private IEnumerable<EquipmentAnalysisRow> Filtered()
        {
            var proc = FltProcess.SelectedValues;
            var bath = FltBath.SelectedValues;
            var eq = FltEquip.SelectedValues;
            IEnumerable<EquipmentAnalysisRow> q = _all.Where(r => !string.IsNullOrWhiteSpace(r.EqId));
            if (proc.Count > 0) q = q.Where(r => proc.Contains(r.ProcessType));
            if (bath.Count > 0) q = q.Where(r => bath.Contains(r.BathGb));
            if (eq.Count > 0) q = q.Where(r => eq.Contains(r.EqId));
            if (_selDates.Count > 0)
            {
                var ds = _selDates.Select(d => d.ToString("yyyy-MM-dd")).ToHashSet();
                q = q.Where(r => ds.Contains(r.AnalysisDate));
            }
            return q;
        }

        private void Render()
        {
            // 단위(일/월/년)는 기간별 추이에서만 노출. 나머지 필터는 두 모드 공통.
            bool trend = IsTrendMode();
            bool log = IsLogMode();
            LblPeriod.Visibility = trend ? Visibility.Visible : Visibility.Collapsed;
            CmbPeriod.Visibility = trend ? Visibility.Visible : Visibility.Collapsed;

            // 점검 일지 모드: 차트/표 숨기고 로그 패널 표시
            if (LogPanel != null) LogPanel.Visibility = log ? Visibility.Visible : Visibility.Collapsed;
            if (ChartCard != null) ChartCard.Visibility = log ? Visibility.Collapsed : Visibility.Visible;
            if (TableCard != null) TableCard.Visibility = log ? Visibility.Collapsed : Visibility.Visible;

            UpdateStats();
            if (log) { BuildLogDates(); return; }

            BuildChart();
            BuildTable();
            TxtEmpty.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 요약 카드: 현재 필터+선택 원소 기준
        private void UpdateStats()
        {
            var rows = Filtered().ToList();
            var elems = SelectedElements();

            TxtStatCount.Text = rows.Count.ToString("#,0");
            // 부제목: 전체 설비(측정 이력 ∪ 마스터) 대비 이 필터에서 측정된 설비 / 미측정
            int measuredEq = rows.Select(r => r.EqId).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Count();
            int totalEq = _all.Where(r => !string.IsNullOrWhiteSpace(r.EqId)).Select(r => r.EqId)
                              .Concat(_processMap.Keys).Distinct().Count();
            TxtStatCountSub.Text = $"전체 {totalEq}대 · 측정 완료 {measuredEq}대 · 미측정 {Math.Max(0, totalEq - measuredEq)}대";

            var dates = rows.Select(r => r.AnalysisDate).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            string latest = dates.Count > 0 ? dates.Max() : "";
            TxtStatLatest.Text = dates.Count > 0 ? latest : "-";
            TxtStatLatestSub.Text = dates.Count > 0 ? $"측정일 {dates.Distinct().Count()}일" : "";

            // 최고 오염/평균은 현재 필터(기본값=최신 측정일) 기준
            double maxV = double.MinValue; string maxEq = "", maxEl = "", maxDate = "";
            foreach (var r in rows)
                foreach (var el in elems)
                {
                    double v = r.Elements.TryGetValue(el, out var x) ? x : 0.0;
                    if (v > maxV) { maxV = v; maxEq = r.EqId; maxEl = el; maxDate = r.AnalysisDate; }
                }
            if (maxV <= double.MinValue) { TxtStatMax.Text = "-"; TxtStatMaxWho.Text = ""; }
            else { TxtStatMax.Text = maxV.ToString("#,0.##"); TxtStatMaxWho.Text = $"{maxEq} · {maxEl} · {maxDate}"; }

            var vals = rows.SelectMany(r => elems.Select(el => r.Elements.TryGetValue(el, out var v) ? v : 0.0)).ToList();
            TxtStatAvg.Text = vals.Count > 0 ? vals.Average().ToString("#,0.##") : "-";
        }

        // ===== 점검 일지 (날짜별 측정 현황 + 특이사항) =====
        public class CheckStatusItem : INotifyPropertyChanged
        {
            public string OrigEqId { get; set; } = "";   // 변경 감지용 원본 이름
            public string EqId { get; set; } = "";        // 편집 가능(설비명)
            public string Process { get; set; } = "";     // 편집 가능(공정/급)
            public bool IsMeasured { get; set; }
            public string StatusText { get; set; } = "";
            public string Summary { get; set; } = "";     // 그날 주요값(최고 원소)
            public Brush StatusBg { get; set; } = Brushes.Transparent;
            public Brush StatusFg { get; set; } = Brushes.Black;
            private string _note = "";
            public string Note { get => _note; set { _note = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Note))); } }
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly ObservableCollection<CheckStatusItem> _checkItems = new();
        private string _checkDate = "";
        private bool _logLoading;
        private Dictionary<string, string> _processMap = new();   // EqId → 공정(급)

        private static SolidColorBrush CB(string hex)
            => new((Color)ColorConverter.ConvertFromString(hex));

        private bool IsLogMode() => RbLog?.IsChecked == true;

        // 점검 일지 진입: 날짜 목록 채우고(최신순) 최신일 선택
        private void BuildLogDates()
        {
            _logLoading = true;
            var dates = _all.Where(r => !string.IsNullOrWhiteSpace(r.AnalysisDate))
                            .Select(r => r.AnalysisDate).Distinct()
                            .OrderByDescending(s => s, StringComparer.Ordinal).ToList();
            LogDateList.ItemsSource = dates;
            _logLoading = false;

            if (dates.Count > 0)
                LogDateList.SelectedItem = (!string.IsNullOrEmpty(_checkDate) && dates.Contains(_checkDate)) ? _checkDate : dates[0];
            else
            {
                _checkItems.Clear(); LogEqList.ItemsSource = _checkItems;
                LogSub.Text = "측정 데이터가 없습니다"; LogSummary.Text = "";
            }
        }

        private void LogDate_Selected(object sender, SelectionChangedEventArgs e)
        {
            if (_logLoading) return;
            if (LogDateList.SelectedItem is string d) { _checkDate = d; BuildLogItems(); }
        }

        private void BuildLogItems()
        {
            _checkItems.Clear();
            // 설비 목록 = 측정 이력 있는 설비 ∪ 마스터 등록 설비(측정 없어도 표시)
            var eqIds = _all.Where(r => !string.IsNullOrWhiteSpace(r.EqId)).Select(r => r.EqId)
                            .Concat(_processMap.Keys)
                            .Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
            var dayRows = _all.Where(r => r.AnalysisDate == _checkDate).ToList();
            var measured = dayRows.Select(r => r.EqId).Distinct().ToHashSet();
            var notes = EquipmentAnalysisRepository.GetCheckNotes(_checkDate);

            int done = 0;
            foreach (var eq in eqIds)
            {
                bool m = measured.Contains(eq);
                if (m) done++;
                // 주요값: 그날 그 설비의 최고 원소·값
                string summary = "-";
                if (m)
                {
                    double mv = double.MinValue; string mel = "";
                    foreach (var r in dayRows.Where(r => r.EqId == eq))
                        foreach (var el in Elements)
                            if (r.Elements.TryGetValue(el, out var v) && v > mv) { mv = v; mel = el; }
                    if (mv > double.MinValue) summary = $"최고 {mel} {mv:#,0.##}";
                }
                _checkItems.Add(new CheckStatusItem
                {
                    OrigEqId = eq,
                    EqId = eq,
                    Process = _processMap.TryGetValue(eq, out var pr) ? pr : "",
                    IsMeasured = m,
                    StatusText = m ? "측정 완료" : "미측정",
                    Summary = summary,
                    StatusBg = m ? CB("#DCFCE7") : CB("#FEE2E2"),
                    StatusFg = m ? CB("#16A34A") : CB("#DC2626"),
                    Note = notes.TryGetValue(eq, out var n) ? n : ""
                });
            }
            LogEqList.ItemsSource = _checkItems;
            LogSub.Text = _checkDate;
            LogSummary.Text = $"전체 {eqIds.Count}대 · 측정 완료 {done}대 · 미측정 {eqIds.Count - done}대";
        }

        private void LogSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_checkDate)) return;
            try
            {
                // 이름 변경분 확인
                var renames = _checkItems
                    .Where(it => !string.IsNullOrWhiteSpace(it.EqId) && it.EqId.Trim() != it.OrigEqId)
                    .ToList();
                if (renames.Count > 0)
                {
                    string list = string.Join("\n", renames.Select(r => $"{r.OrigEqId} → {r.EqId.Trim()}"));
                    if (MessageBox.Show($"설비명을 변경하면 해당 설비의 측정 데이터·특이사항이 모두 새 이름으로 매칭됩니다.\n\n{list}\n\n변경할까요?",
                        "설비명 변경", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return;
                    foreach (var r in renames)
                        EquipmentAnalysisRepository.RenameEquipment(r.OrigEqId, r.EqId.Trim());
                }

                foreach (var it in _checkItems)
                {
                    string eq = it.EqId?.Trim() ?? "";
                    if (string.IsNullOrEmpty(eq)) continue;
                    EquipmentAnalysisRepository.UpsertEquipmentProcess(eq, it.Process?.Trim() ?? "");
                    EquipmentAnalysisRepository.UpsertCheckNote(eq, _checkDate, it.Note?.Trim() ?? "");
                }

                // 데이터/마스터 다시 로드 후 갱신(이름 변경·공정 반영)
                _all = EquipmentAnalysisRepository.GetAll();
                _processMap = EquipmentAnalysisRepository.GetEquipmentMaster();
                BuildLogItems();
                MessageBox.Show("저장되었습니다.", "점검 일지");
            }
            catch (Exception ex) { MessageBox.Show("저장 실패: " + ex.Message, "오류"); }
        }

        private void BtnLogAddEq_Click(object sender, RoutedEventArgs e)
        {
            string name = LogNewEqBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { MessageBox.Show("추가할 설비명을 입력하세요.", "설비 추가"); return; }
            EquipmentAnalysisRepository.AddEquipment(name);
            _processMap = EquipmentAnalysisRepository.GetEquipmentMaster();
            LogNewEqBox.Text = "";
            BuildLogItems();
        }

        // 설비별 특이사항 날짜 이력(누적) 보기
        private void LogHistory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CheckStatusItem it) return;
            var hist = EquipmentAnalysisRepository.GetCheckNoteHistory(it.EqId);
            string body = hist.Count == 0
                ? "기록된 특이사항이 없습니다."
                : string.Join("\n", hist.Select(h => $"[{h.Date}] {h.Note}"));
            MessageBox.Show(body, $"{it.EqId} 특이사항 이력");
        }

        // 분석일 → 기간 키 (일별=yyyy-MM-dd / 월별=yyyy-MM / 년별=yyyy)
        private static string PeriodKey(string date, string unit)
        {
            if (string.IsNullOrWhiteSpace(date)) return "";
            return unit switch
            {
                "년별" => date.Length >= 4 ? date.Substring(0, 4) : date,
                "월별" => date.Length >= 7 ? date.Substring(0, 7) : date,
                _ => date
            };
        }

        // 차트 라벨: 한 줄, 설비명 / 공정 (SkiaSharp는 줄바꿈 미지원 → 구분자 사용)
        private string EqLabel(string eq)
            => _processMap.TryGetValue(eq, out var p) && !string.IsNullOrWhiteSpace(p) ? $"{eq}  /  {p}" : eq;

        private string EqName(string eq) => EqLabel(eq);

        private void BuildChart()
        {
            var elems = SelectedElements();
            var rows = Filtered().ToList();

            if (IsTrendMode())
            {
                string unit = (CmbPeriod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "월별";
                var dated = rows.Where(r => !string.IsNullOrWhiteSpace(r.AnalysisDate)).ToList();
                var periods = dated.Select(r => PeriodKey(r.AnalysisDate, unit)).Distinct().OrderBy(p => p).ToList();
                var eqIds = dated.Select(r => r.EqId).Distinct().OrderBy(s => s).ToList();

                if (eqIds.Count == 1)
                {
                    // 설비 1대: 선택 원소별 라인 (기간 평균)
                    Chart.Series = elems.Select(el => (ISeries)new LineSeries<double?>
                    {
                        Name = el,
                        Values = periods.Select(p => PeriodAvg(dated, null, el, unit, p)).ToArray()
                    }).ToArray();
                    Chart.YAxes = new[] { new Axis { Name = "ppb" } };
                }
                else
                {
                    // 설비 여러 대: 첫 번째 선택 원소 기준, 설비별 라인 비교 (기간 평균)
                    string el = elems.FirstOrDefault() ?? "Fe";
                    Chart.Series = eqIds.Select(eq => (ISeries)new LineSeries<double?>
                    {
                        Name = EqName(eq),
                        Values = periods.Select(p => PeriodAvg(dated, eq, el, unit, p)).ToArray()
                    }).ToArray();
                    Chart.YAxes = new[] { new Axis { Name = "ppb" } };
                }
                Chart.XAxes = new[] { new Axis { Labels = periods.ToArray(), LabelsRotation = 30 } };
            }
            else
            {
                // 설비별 비교: 각 설비의 '최신(필터 내)' 분석일 값. 같은 날짜 다중 행이면 평균.
                var eqData = rows.GroupBy(r => r.EqId).OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        string d = g.Max(x => x.AnalysisDate);
                        return new { Eq = g.Key, Sub = g.Where(x => x.AnalysisDate == d).ToList() };
                    })
                    .Where(x => x.Sub.Count > 0)
                    .ToList();

                Chart.Series = elems.Select(el => (ISeries)new ColumnSeries<double>
                {
                    Name = el,
                    Values = eqData.Select(x => x.Sub.Average(r => r.Elements.TryGetValue(el, out var v) ? v : 0.0)).ToArray()
                }).ToArray();
                Chart.XAxes = new[] { new Axis { Labels = eqData.Select(x => EqLabel(x.Eq)).ToArray(), LabelsRotation = 30 } };
                Chart.YAxes = new[] { new Axis { Name = "ppb" } };
            }
        }

        // 기간(period) 내 특정 설비(eq==null이면 전체) 원소 평균. 데이터 없으면 null(공백).
        private static double? PeriodAvg(List<EquipmentAnalysisRow> src, string eq, string el, string unit, string period)
        {
            var pr = src.Where(r => (eq == null || r.EqId == eq) && PeriodKey(r.AnalysisDate, unit) == period)
                        .Select(r => r.Elements.TryGetValue(el, out var v) ? v : 0.0).ToList();
            return pr.Count == 0 ? (double?)null : pr.Average();
        }

        private void BuildTable()
        {
            var elems = SelectedElements();   // 표 컬럼도 선택 원소만 표시
            var dt = new DataTable();
            // '공정(설비유형)'은 표에서 숨김 — 필터로만 사용, 다운로드엔 출력됨
            dt.Columns.Add("설비");
            dt.Columns.Add("약액");
            dt.Columns.Add("구분");
            dt.Columns.Add("분석일");
            dt.Columns.Add("단위");
            foreach (var el in elems) dt.Columns.Add(el, typeof(double));

            foreach (var r in Filtered().OrderByDescending(r => r.AnalysisDate).ThenBy(r => r.EqId))
            {
                var vals = new List<object> { r.EqId, r.BathGb, r.Category, r.AnalysisDate, r.Unit };
                foreach (var el in elems) vals.Add(r.Elements.TryGetValue(el, out var v) ? Math.Round(v, 3) : 0.0);
                dt.Rows.Add(vals.ToArray());
            }
            Grid.ItemsSource = dt.DefaultView;
            if (TxtTableCount != null) TxtTableCount.Text = $"{dt.Rows.Count}행";
        }

        // 차트 접기 → 접으면 데이터 표가 그만큼 넓어진다
        private void ToggleChart_Click(object sender, RoutedEventArgs e)
        {
            bool show = ChartBody.Visibility != Visibility.Visible;
            ChartBody.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ChartArrow.Text = show ? "▾" : "▸";
            RowChart.Height = show ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        }

        // 데이터 표 접기 → 접으면 차트가 그만큼 커진다
        private void ToggleTable_Click(object sender, RoutedEventArgs e)
        {
            bool show = TableBody.Visibility != Visibility.Visible;
            TableBody.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            TableArrow.Text = show ? "▾" : "▸";
            RowTable.Height = show ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        }

        // 표 카드 하단 모서리 라운드(그리드가 사각으로 덮어 각져 보이던 것 보정)
        private void TableInner_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                fe.Clip = new RectangleGeometry(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight), 12, 12);
        }

        // 자동 생성 컬럼: 셀 텍스트 상하·좌우 중앙 정렬
        private void Grid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            // 메타 컬럼은 고정폭, 원소 컬럼은 남는 폭을 균등 분배(*) → 가로 스크롤 없이 화면에 꽉 채움
            string h = e.Column.Header?.ToString() ?? "";
            switch (h)
            {
                case "설비": e.Column.Width = new DataGridLength(56); break;
                case "약액": e.Column.Width = new DataGridLength(42); break;
                case "구분": e.Column.Width = new DataGridLength(42); break;
                case "분석일": e.Column.Width = new DataGridLength(78); break;
                case "단위": e.Column.Width = new DataGridLength(38); break;
                default: e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star); e.Column.MinWidth = 40; break;
            }
            if (e.Column is DataGridTextColumn tc)
            {
                var st = new Style(typeof(TextBlock));
                st.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
                st.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
                st.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
                st.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(1, 0, 1, 0)));
                tc.ElementStyle = st;
            }
        }

        // ── 엑셀 업로드 ──
        private void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "설비 분석 엑셀 선택", Filter = "Excel (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var rows = ParseExcel(dlg.FileName);
                if (rows.Count == 0) { MessageBox.Show("읽어들일 데이터가 없습니다.", "업로드", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                int added = EquipmentAnalysisRepository.InsertMany(rows);
                ReloadAll();
                MessageBox.Show($"{rows.Count}행 중 {added}행이 추가되었습니다.\n(중복 {rows.Count - added}행 제외)", "업로드 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"업로드 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<EquipmentAnalysisRow> ParseExcel(string path)
        {
            var result = new List<EquipmentAnalysisRow>();
            using var wb = new XLWorkbook(path);
            var elemSet = Elements.ToDictionary(x => x.ToLower(), x => x);

            foreach (var ws in wb.Worksheets)
            {
                int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                if (lastCol == 0 || lastRow < 2) continue;

                // 헤더 매핑
                int cEq = 0, cBath = 0, cCat = 0, cUnit = 0, cDate = 0;
                var cElem = new Dictionary<int, string>();
                for (int c = 1; c <= lastCol; c++)
                {
                    string h = (ws.Cell(1, c).GetString() ?? "").Trim();
                    string hl = h.ToLower();
                    if (hl.Contains("eq_id")) cEq = c;
                    else if (hl.Contains("bath")) cBath = c;
                    else if (hl == "unit") cUnit = c;
                    else if (hl.Contains("dt") || hl.Contains("date")) cDate = c;
                    else if (elemSet.TryGetValue(hl, out var el)) cElem[c] = el;
                    else if (cCat == 0) cCat = c;   // 나머지(Use_CNT/공정) 첫 컬럼을 '구분'으로
                }
                if (cEq == 0) continue;   // 설비 컬럼 없으면 이 시트는 스킵

                for (int r = 2; r <= lastRow; r++)
                {
                    string eq = (ws.Cell(r, cEq).GetString() ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(eq)) continue;

                    string date = "";
                    if (cDate > 0)
                    {
                        var dc = ws.Cell(r, cDate);
                        if (dc.TryGetValue<DateTime>(out var dtv)) date = dtv.ToString("yyyy-MM-dd");
                        else date = (dc.GetString() ?? "").Trim();
                    }

                    var row = new EquipmentAnalysisRow
                    {
                        ProcessType = ws.Name,
                        EqId = eq,
                        BathGb = cBath > 0 ? (ws.Cell(r, cBath).GetString() ?? "").Trim() : "",
                        Category = cCat > 0 ? (ws.Cell(r, cCat).GetString() ?? "").Trim() : "",
                        Unit = cUnit > 0 ? ((ws.Cell(r, cUnit).GetString() ?? "").Trim() is { Length: > 0 } u ? u : "ppb") : "ppb",
                        AnalysisDate = date,
                    };
                    foreach (var kv in cElem)
                    {
                        double v = 0;
                        var cell = ws.Cell(r, kv.Key);
                        if (cell.TryGetValue<double>(out var dv)) v = dv;
                        row.Elements[kv.Value] = v;
                    }
                    result.Add(row);
                }
            }
            return result;
        }

        // ── 엑셀 다운로드 ──
        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            // 현재 필터가 적용된 결과만 내보낸다(필터 없으면 전체)
            var rows = Filtered().ToList();
            if (rows.Count == 0) { MessageBox.Show("내보낼 데이터가 없습니다. (필터 결과 0건)", "다운로드", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            bool filtered = FltProcess.SelectedValues.Count > 0 || FltBath.SelectedValues.Count > 0
                            || FltEquip.SelectedValues.Count > 0 || _selDates.Count > 0;
            string suffix = filtered ? "_필터" : "";
            var dlg = new SaveFileDialog { Title = "설비 분석 엑셀 저장", Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"설비분석_ICPMS{suffix}_{DateTime.Now:yyyyMMdd}.xlsx" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                using var wb = new XLWorkbook();
                foreach (var grp in rows.GroupBy(r => string.IsNullOrWhiteSpace(r.ProcessType) ? "DATA" : r.ProcessType))
                {
                    var ws = wb.Worksheets.Add(grp.Key.Length > 31 ? grp.Key.Substring(0, 31) : grp.Key);
                    string[] head = new[] { "설비 유형", "EQ_ID", "Bath_GB", "구분", "Unit", "EQ_IN_DT" }.Concat(Elements).ToArray();
                    for (int c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
                    int r = 2;
                    foreach (var row in grp.OrderBy(x => x.AnalysisDate).ThenBy(x => x.EqId))
                    {
                        ws.Cell(r, 1).Value = row.ProcessType;
                        ws.Cell(r, 2).Value = row.EqId;
                        ws.Cell(r, 3).Value = row.BathGb;
                        ws.Cell(r, 4).Value = row.Category;
                        ws.Cell(r, 5).Value = row.Unit;
                        ws.Cell(r, 6).Value = row.AnalysisDate;
                        for (int i = 0; i < Elements.Length; i++)
                            ws.Cell(r, 7 + i).Value = row.Elements.TryGetValue(Elements[i], out var v) ? v : 0.0;
                        r++;
                    }
                    ws.Row(1).Style.Font.Bold = true;
                    ws.SheetView.FreezeRows(1);
                }
                wb.SaveAs(dlg.FileName);
                MessageBox.Show("저장되었습니다.", "다운로드 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"다운로드 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdmin) { MessageBox.Show("전체 삭제는 관리자만 가능합니다.", "권한 제한", MessageBoxButton.OK, MessageBoxImage.Stop); return; }
            if (_all.Count == 0) return;
            if (MessageBox.Show("설비 분석 데이터를 전체 삭제하시겠습니까?\n복구할 수 없습니다.", "전체 삭제",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            try { EquipmentAnalysisRepository.DeleteAll(); ReloadAll(); }
            catch (Exception ex) { MessageBox.Show($"삭제 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}
