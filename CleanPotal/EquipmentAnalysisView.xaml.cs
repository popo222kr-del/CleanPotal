using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ClosedXML.Excel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Win32;
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

        public EquipmentAnalysisView()
        {
            InitializeComponent();
            BuildElementChips();
            FltProcess.SelectionChanged += Filter_MultiChanged;
            FltBath.SelectionChanged += Filter_MultiChanged;
            FltEquip.SelectionChanged += Filter_MultiChanged;
            FltDate.SelectionChanged += Filter_MultiChanged;
            Loaded += (_, _) => ReloadAll();
        }

        private void Filter_MultiChanged(object? sender, EventArgs e) { if (!_loading) Render(); }

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
            PopulateFilters();
            Render();
            int eqCnt = _all.Select(r => r.EqId).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Count();
            TxtCount.Text = $"총 {_all.Count}행 · 설비 {eqCnt}대";
        }

        private void PopulateFilters()
        {
            _loading = true;
            try
            {
                ApplyOptions(FltProcess, _all.Select(r => r.ProcessType).Distinct().OrderBy(s => s));
                ApplyOptions(FltBath, _all.Select(r => r.BathGb).Distinct().OrderBy(s => s));
                ApplyOptions(FltEquip, _all.Select(r => r.EqId).Distinct().OrderBy(s => s));
                // 날짜는 최근순
                ApplyOptions(FltDate, _all.Select(r => r.AnalysisDate).Distinct().OrderByDescending(s => s));
            }
            finally { _loading = false; }
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
            var date = FltDate.SelectedValues;
            IEnumerable<EquipmentAnalysisRow> q = _all.Where(r => !string.IsNullOrWhiteSpace(r.EqId));
            if (proc.Count > 0) q = q.Where(r => proc.Contains(r.ProcessType));
            if (bath.Count > 0) q = q.Where(r => bath.Contains(r.BathGb));
            if (eq.Count > 0) q = q.Where(r => eq.Contains(r.EqId));
            if (date.Count > 0) q = q.Where(r => date.Contains(r.AnalysisDate));
            return q;
        }

        private void Render()
        {
            // 단위(일/월/년)는 기간별 추이에서만 노출. 나머지 필터는 두 모드 공통.
            bool trend = IsTrendMode();
            LblPeriod.Visibility = trend ? Visibility.Visible : Visibility.Collapsed;
            CmbPeriod.Visibility = trend ? Visibility.Visible : Visibility.Collapsed;

            BuildChart();
            BuildTable();
            TxtEmpty.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
                        Name = eq,
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
                Chart.XAxes = new[] { new Axis { Labels = eqData.Select(x => x.Eq).ToArray(), LabelsRotation = 30 } };
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
            dt.Columns.Add("공정");
            dt.Columns.Add("설비");
            dt.Columns.Add("약액");
            dt.Columns.Add("구분");
            dt.Columns.Add("분석일");
            dt.Columns.Add("단위");
            foreach (var el in elems) dt.Columns.Add(el, typeof(double));

            foreach (var r in Filtered().OrderByDescending(r => r.AnalysisDate).ThenBy(r => r.EqId))
            {
                var vals = new List<object> { r.ProcessType, r.EqId, r.BathGb, r.Category, r.AnalysisDate, r.Unit };
                foreach (var el in elems) vals.Add(r.Elements.TryGetValue(el, out var v) ? Math.Round(v, 4) : 0.0);
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
            if (_all.Count == 0) { MessageBox.Show("내보낼 데이터가 없습니다.", "다운로드", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var dlg = new SaveFileDialog { Title = "설비 분석 엑셀 저장", Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"설비분석_ICPMS_{DateTime.Now:yyyyMMdd}.xlsx" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                using var wb = new XLWorkbook();
                foreach (var grp in _all.GroupBy(r => string.IsNullOrWhiteSpace(r.ProcessType) ? "DATA" : r.ProcessType))
                {
                    var ws = wb.Worksheets.Add(grp.Key.Length > 31 ? grp.Key.Substring(0, 31) : grp.Key);
                    string[] head = new[] { "EQ_ID", "Bath_GB", "구분", "Unit", "EQ_IN_DT" }.Concat(Elements).ToArray();
                    for (int c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
                    int r = 2;
                    foreach (var row in grp.OrderBy(x => x.AnalysisDate).ThenBy(x => x.EqId))
                    {
                        ws.Cell(r, 1).Value = row.EqId;
                        ws.Cell(r, 2).Value = row.BathGb;
                        ws.Cell(r, 3).Value = row.Category;
                        ws.Cell(r, 4).Value = row.Unit;
                        ws.Cell(r, 5).Value = row.AnalysisDate;
                        for (int i = 0; i < Elements.Length; i++)
                            ws.Cell(r, 6 + i).Value = row.Elements.TryGetValue(Elements[i], out var v) ? v : 0.0;
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
            if (_all.Count == 0) return;
            if (MessageBox.Show("설비 분석 데이터를 전체 삭제하시겠습니까?\n복구할 수 없습니다.", "전체 삭제",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            try { EquipmentAnalysisRepository.DeleteAll(); ReloadAll(); }
            catch (Exception ex) { MessageBox.Show($"삭제 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}
