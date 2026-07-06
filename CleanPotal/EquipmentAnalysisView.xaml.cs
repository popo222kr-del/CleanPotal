using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        public EquipmentAnalysisView()
        {
            InitializeComponent();
            Loaded += (_, _) => ReloadAll();
        }

        // 다른 페이지 갔다가 돌아오면 새로고침
        public void TryRefresh() { if (!_loading) ReloadAll(); }

        private void ReloadAll()
        {
            try { _all = EquipmentAnalysisRepository.GetAll(); }
            catch { _all = new(); }
            PopulateFilters();
            Render();
            TxtCount.Text = $"총 {_all.Count}행";
        }

        private void PopulateFilters()
        {
            _loading = true;
            try
            {
                string prevProc = CmbProcess.SelectedItem as string;
                string prevBath = CmbBath.SelectedItem as string;
                string prevElem = CmbElement.SelectedItem as string;
                string prevEq = CmbEquip.SelectedItem as string;

                var procs = new List<string> { "전체" };
                procs.AddRange(_all.Select(r => r.ProcessType).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s));
                CmbProcess.ItemsSource = procs;
                CmbProcess.SelectedItem = procs.Contains(prevProc) ? prevProc : "전체";

                var baths = new List<string> { "전체" };
                baths.AddRange(_all.Select(r => r.BathGb).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s));
                CmbBath.ItemsSource = baths;
                CmbBath.SelectedItem = baths.Contains(prevBath) ? prevBath : "전체";

                CmbElement.ItemsSource = Elements;
                CmbElement.SelectedItem = Elements.Contains(prevElem) ? prevElem : (Elements.Contains("Fe") ? "Fe" : Elements.FirstOrDefault());

                var eqs = _all.Select(r => r.EqId).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                CmbEquip.ItemsSource = eqs;
                CmbEquip.SelectedItem = eqs.Contains(prevEq) ? prevEq : eqs.FirstOrDefault();
            }
            finally { _loading = false; }
        }

        private bool IsTrendMode() => (CmbMode.SelectedItem as ComboBoxItem)?.Content?.ToString() == "시간 추이";

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            // ⚠️ XAML 파싱(InitializeComponent) 중 ComboBoxItem IsSelected 가 SelectionChanged 를
            //    먼저 발화시키는데, 그 시점엔 뒤에 선언된 컨트롤(LblEq 등)이 아직 null → NRE 방지
            if (_loading || LblEq == null || CmbEquip == null || Chart == null) return;
            Render();
        }

        private IEnumerable<EquipmentAnalysisRow> Filtered()
        {
            string proc = CmbProcess.SelectedItem as string ?? "전체";
            string bath = CmbBath.SelectedItem as string ?? "전체";
            IEnumerable<EquipmentAnalysisRow> q = _all;
            if (proc != "전체") q = q.Where(r => r.ProcessType == proc);
            if (bath != "전체") q = q.Where(r => r.BathGb == bath);
            return q;
        }

        private void Render()
        {
            // 시간 추이일 때만 설비 선택 노출 (초기 로드에도 반영되도록 Render 에서 처리)
            bool trend = IsTrendMode();
            LblEq.Visibility = trend ? Visibility.Visible : Visibility.Collapsed;
            CmbEquip.Visibility = trend ? Visibility.Visible : Visibility.Collapsed;

            BuildChart();
            BuildTable();
            TxtEmpty.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildChart()
        {
            string elem = CmbElement.SelectedItem as string ?? Elements.FirstOrDefault() ?? "Fe";
            var rows = Filtered().ToList();

            if (IsTrendMode())
            {
                string eq = CmbEquip.SelectedItem as string;
                var pts = rows.Where(r => r.EqId == eq).OrderBy(r => r.AnalysisDate).ToList();
                Chart.Series = new ISeries[]
                {
                    new LineSeries<double>
                    {
                        Name = $"{eq} · {elem}",
                        Values = pts.Select(r => r.Elements.TryGetValue(elem, out var v) ? v : 0.0).ToArray()
                    }
                };
                Chart.XAxes = new[] { new Axis { Labels = pts.Select(r => r.AnalysisDate).ToArray(), LabelsRotation = 30 } };
                Chart.YAxes = new[] { new Axis { Name = $"{elem} (ppb)" } };
            }
            else
            {
                // 설비별: 각 설비의 '최신 분석일' 값 비교
                var groups = rows.GroupBy(r => r.EqId)
                    .Select(g =>
                    {
                        var latest = g.OrderBy(x => x.AnalysisDate).Last();
                        return new { Eq = g.Key, Val = latest.Elements.TryGetValue(elem, out var v) ? v : 0.0 };
                    })
                    .OrderByDescending(x => x.Val)
                    .ToList();
                Chart.Series = new ISeries[]
                {
                    new ColumnSeries<double> { Name = elem, Values = groups.Select(x => x.Val).ToArray() }
                };
                Chart.XAxes = new[] { new Axis { Labels = groups.Select(x => x.Eq).ToArray(), LabelsRotation = 30 } };
                Chart.YAxes = new[] { new Axis { Name = $"{elem} (ppb, 최신값)" } };
            }
        }

        private void BuildTable()
        {
            var dt = new DataTable();
            dt.Columns.Add("공정");
            dt.Columns.Add("설비");
            dt.Columns.Add("약액");
            dt.Columns.Add("구분");
            dt.Columns.Add("분석일");
            dt.Columns.Add("단위");
            foreach (var el in Elements) dt.Columns.Add(el, typeof(double));

            foreach (var r in Filtered().OrderByDescending(r => r.AnalysisDate).ThenBy(r => r.EqId))
            {
                var vals = new List<object> { r.ProcessType, r.EqId, r.BathGb, r.Category, r.AnalysisDate, r.Unit };
                foreach (var el in Elements) vals.Add(r.Elements.TryGetValue(el, out var v) ? Math.Round(v, 4) : 0.0);
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
