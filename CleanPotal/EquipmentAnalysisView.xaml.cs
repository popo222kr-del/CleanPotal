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
            Loaded += (_, _) => ReloadAll();
        }

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
                string prevProc = CmbProcess.SelectedItem as string;
                string prevBath = CmbBath.SelectedItem as string;
                string prevEq = CmbEquip.SelectedItem as string;

                var procs = new List<string> { "전체" };
                procs.AddRange(_all.Select(r => r.ProcessType).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s));
                CmbProcess.ItemsSource = procs;
                CmbProcess.SelectedItem = procs.Contains(prevProc) ? prevProc : "전체";

                var baths = new List<string> { "전체" };
                baths.AddRange(_all.Select(r => r.BathGb).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s));
                CmbBath.ItemsSource = baths;
                CmbBath.SelectedItem = baths.Contains(prevBath) ? prevBath : "전체";

                var eqs = new List<string> { "전체" };
                eqs.AddRange(_all.Select(r => r.EqId).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s));
                CmbEquip.ItemsSource = eqs;
                CmbEquip.SelectedItem = eqs.Contains(prevEq) ? prevEq : "전체";

                // 설비별 비교용 날짜 선택: 최신 + 분석일 목록(최근순)
                string prevDate = CmbDate.SelectedItem as string;
                var dates = new List<string> { "최신" };
                dates.AddRange(_all.Select(r => r.AnalysisDate).Where(s => !string.IsNullOrWhiteSpace(s))
                                   .Distinct().OrderByDescending(s => s));
                CmbDate.ItemsSource = dates;
                CmbDate.SelectedItem = dates.Contains(prevDate) ? prevDate : "최신";
            }
            finally { _loading = false; }
        }

        private bool IsTrendMode() => RbTrend?.IsChecked == true;

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            // XAML 파싱 중(InitializeComponent) RbBar IsChecked=True 가 먼저 발화 → 뒤 컨트롤 null 방지
            if (_loading || Chart == null || LblEq == null || CmbEquip == null || CmbDate == null || CmbPeriod == null) return;
            Render();
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || Chart == null || LblEq == null || CmbEquip == null || CmbDate == null || CmbPeriod == null) return;
            Render();
        }

        // 공정·약액·설비(전체/특정) 공통 필터
        private IEnumerable<EquipmentAnalysisRow> Filtered()
        {
            string proc = CmbProcess.SelectedItem as string ?? "전체";
            string bath = CmbBath.SelectedItem as string ?? "전체";
            string eq = CmbEquip.SelectedItem as string ?? "전체";
            IEnumerable<EquipmentAnalysisRow> q = _all;
            if (proc != "전체") q = q.Where(r => r.ProcessType == proc);
            if (bath != "전체") q = q.Where(r => r.BathGb == bath);
            if (eq != "전체" && !string.IsNullOrEmpty(eq)) q = q.Where(r => r.EqId == eq);
            return q;
        }

        private void Render()
        {
            // 설비는 두 모드 모두 표시. 날짜=설비별 비교 전용, 단위(일/월/년)=시간 추이 전용
            bool trend = IsTrendMode();
            LblDate.Visibility = trend ? Visibility.Collapsed : Visibility.Visible;
            CmbDate.Visibility = trend ? Visibility.Collapsed : Visibility.Visible;
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
            var rows = Filtered().Where(r => !string.IsNullOrWhiteSpace(r.EqId)).ToList();

            if (IsTrendMode())
            {
                string unit = (CmbPeriod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "월별";
                string eqSel = CmbEquip.SelectedItem as string ?? "전체";
                var dated = rows.Where(r => !string.IsNullOrWhiteSpace(r.AnalysisDate)).ToList();
                var periods = dated.Select(r => PeriodKey(r.AnalysisDate, unit)).Distinct().OrderBy(p => p).ToList();

                if (eqSel == "전체")
                {
                    // 설비 전체: 첫 번째 선택 원소 기준, 설비별 라인 비교 (기간 평균, 데이터 없는 구간은 공백)
                    string el = elems.FirstOrDefault() ?? "Fe";
                    var eqIds = dated.Select(r => r.EqId).Distinct().OrderBy(s => s).ToList();
                    Chart.Series = eqIds.Select(eq => (ISeries)new LineSeries<double?>
                    {
                        Name = eq,
                        Values = periods.Select(p =>
                        {
                            var pr = dated.Where(r => r.EqId == eq && PeriodKey(r.AnalysisDate, unit) == p)
                                          .Select(r => r.Elements.TryGetValue(el, out var v) ? v : 0.0).ToList();
                            return pr.Count == 0 ? (double?)null : pr.Average();
                        }).ToArray()
                    }).ToArray();
                    Chart.YAxes = new[] { new Axis { Name = $"{el} (ppb · {unit} 평균)" } };
                }
                else
                {
                    // 특정 설비: 선택 원소별 라인 (기간 평균)
                    Chart.Series = elems.Select(el => (ISeries)new LineSeries<double?>
                    {
                        Name = el,
                        Values = periods.Select(p =>
                        {
                            var pr = dated.Where(r => PeriodKey(r.AnalysisDate, unit) == p)
                                          .Select(r => r.Elements.TryGetValue(el, out var v) ? v : 0.0).ToList();
                            return pr.Count == 0 ? (double?)null : pr.Average();
                        }).ToArray()
                    }).ToArray();
                    Chart.YAxes = new[] { new Axis { Name = $"ppb · {unit} 평균" } };
                }
                Chart.XAxes = new[] { new Axis { Labels = periods.ToArray(), LabelsRotation = 30 } };
            }
            else
            {
                // 설비별 비교: 날짜 선택(최신/특정 분석일). 같은 날짜에 여러 행이면 평균.
                string dateSel = CmbDate.SelectedItem as string ?? "최신";
                var eqData = rows.GroupBy(r => r.EqId).OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        string d = dateSel == "최신" ? g.Max(x => x.AnalysisDate) : dateSel;
                        var sub = g.Where(x => x.AnalysisDate == d).ToList();
                        return new { Eq = g.Key, Sub = sub };
                    })
                    .Where(x => x.Sub.Count > 0)
                    .ToList();

                Chart.Series = elems.Select(el => (ISeries)new ColumnSeries<double>
                {
                    Name = el,
                    Values = eqData.Select(x => x.Sub.Average(r => r.Elements.TryGetValue(el, out var v) ? v : 0.0)).ToArray()
                }).ToArray();
                Chart.XAxes = new[] { new Axis { Labels = eqData.Select(x => x.Eq).ToArray(), LabelsRotation = 30 } };
                Chart.YAxes = new[] { new Axis { Name = dateSel == "최신" ? "ppb (설비별 최신값)" : $"ppb ({dateSel})" } };
            }
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
