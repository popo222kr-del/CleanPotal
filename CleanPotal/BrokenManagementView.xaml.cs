using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Win32;

namespace CleanPotal
{
    // ---------------------------------------------------------------------------
    // Data models
    // ---------------------------------------------------------------------------
    public class BrokenRecord
    {
        public int No { get; set; }
        public DateTime? OccurDate { get; set; }
        public string OccurDateStr => OccurDate.HasValue
            ? $"{OccurDate.Value.Year - 2000}년 {OccurDate.Value.Month}월 {OccurDate.Value.Day}일"
            : "-";
        public string Line { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string SN { get; set; } = "";
        public string Team { get; set; } = "";
        public string Causer { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string CareerAtOccur { get; set; } = "";
        public string ProductType { get; set; } = "";
        public double AccidentWeight =>
            ProductType.Equals("acc", StringComparison.OrdinalIgnoreCase) ? 0.5 : 1.0;
        public string WeightStr => AccidentWeight == 0.5 ? "0.5 (ACC)" : "1";
        public string OccurStage { get; set; } = "";
        public string Status { get; set; } = "";
        public string IsOfficial { get; set; } = "";
    }

    public class TeamSummary
    {
        public string Team { get; set; } = "";
        public int RawCount { get; set; }
        public double WeightedCount { get; set; }
        public bool IsAchieved { get; set; }
        public string AchievedStr => IsAchieved ? "O" : "X";
        public string PaymentMonth { get; set; } = "";
        public string RewardRate => IsAchieved ? "90%" : "30%";
    }

    // ---------------------------------------------------------------------------
    // View code-behind
    // ---------------------------------------------------------------------------
    public partial class BrokenManagementView : UserControl
    {
        private List<BrokenRecord> _allRecords = new();
        private ObservableCollection<BrokenRecord> _filteredRecords = new();
        private ObservableCollection<TeamSummary> _teamSummaries = new();

        private bool _suppressFilter = false;

        public BrokenManagementView()
        {
            InitializeComponent();
            DgBroken.ItemsSource = _filteredRecords;
            DgTeamSummary.ItemsSource = _teamSummaries;

            // Pre-populate filter ComboBoxes with placeholder
            ResetFilterComboBoxes();
        }

        public void TryRefresh() { /* no-op: file-based view */ }

        // -----------------------------------------------------------------------
        // UI events
        // -----------------------------------------------------------------------
        private void BtnLoadFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Broken 현황 Excel 파일 선택",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            string filePath = dlg.FileName;
            TxtFilePath.Text = filePath;
            TxtFilePath.Foreground = System.Windows.Media.Brushes.DimGray;

            try
            {
                LoadDataFromExcel(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일 읽기 오류:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtFilePath.Text = "파일을 선택하세요...";
                TxtFilePath.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilter) return;
            ApplyFilter();
        }

        private void BtnResetFilter_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilter = true;
            if (CmbYear.Items.Count > 0) CmbYear.SelectedIndex = 0;
            if (CmbLine.Items.Count > 0) CmbLine.SelectedIndex = 0;
            if (CmbTeam.Items.Count > 0) CmbTeam.SelectedIndex = 0;
            _suppressFilter = false;
            ApplyFilter();
        }

        // -----------------------------------------------------------------------
        // Excel loading
        // -----------------------------------------------------------------------
        private void LoadDataFromExcel(string filePath)
        {
            // Copy to temp to handle file-in-use (e.g. file open in Excel)
            string tempPath = Path.Combine(Path.GetTempPath(), $"broken_{Guid.NewGuid():N}.xlsx");
            try
            {
                File.Copy(filePath, tempPath, true);

                using var doc = SpreadsheetDocument.Open(tempPath, isEditable: false);
                var workbookPart = doc.WorkbookPart
                    ?? throw new InvalidOperationException("WorkbookPart를 찾을 수 없습니다.");

                var sheets = workbookPart.Workbook.Sheets?.Cast<Sheet>().ToList()
                    ?? new List<Sheet>();

                if (sheets.Count == 0)
                    throw new InvalidOperationException("시트가 없습니다.");

                // --- Sheet 2: 생산팀 경력 ---
                var careerDict = new Dictionary<string, (string career, string title)>(StringComparer.OrdinalIgnoreCase);
                if (sheets.Count >= 2)
                {
                    string sheetId2 = sheets[1].Id?.Value ?? "";
                    if (!string.IsNullOrEmpty(sheetId2))
                    {
                        var wsp2 = (WorksheetPart)workbookPart.GetPartById(sheetId2);
                        careerDict = ParseCareerSheet(wsp2, workbookPart);
                    }
                }

                // --- Sheet 1: Broken 현황 ---
                string sheetId1 = sheets[0].Id?.Value ?? "";
                if (string.IsNullOrEmpty(sheetId1))
                    throw new InvalidOperationException("첫 번째 시트를 열 수 없습니다.");

                var wsp1 = (WorksheetPart)workbookPart.GetPartById(sheetId1);
                _allRecords = ParseBrokenSheet(wsp1, workbookPart, careerDict);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }

            // Update UI
            TxtRecordCount.Text = $"총 {_allRecords.Count}건";
            PopulateFilterComboBoxes();
            ApplyFilter();
            RefreshTeamSummary();
        }

        // -----------------------------------------------------------------------
        // Sheet 1: Broken 현황 parser
        //   B=NO, C=발생일(serial), D=제품명, H=반출라인, I=S/N
        //   K=공식비공식, L=사고발생내용, V=유발자, W=발생기준경력, X=팀별
        //   Y=제품종류, Z=발생단계, AB=처리현황
        // -----------------------------------------------------------------------
        private List<BrokenRecord> ParseBrokenSheet(
            WorksheetPart wsp,
            WorkbookPart wbp,
            Dictionary<string, (string career, string title)> careerDict)
        {
            var sst = wbp.SharedStringTablePart?.SharedStringTable;
            var rows = wsp.Worksheet.Descendants<Row>().ToList();
            var result = new List<BrokenRecord>();

            foreach (var row in rows)
            {
                var cells = row.Elements<Cell>().ToList();

                string bVal = GetCellValue(cells, "B", sst);
                string cVal = GetCellValue(cells, "C", sst);

                // B must be numeric (the NO field)
                if (!double.TryParse(bVal, out double noVal)) continue;
                if (noVal <= 0) continue;

                // C must be an Excel date serial (> 40000)
                if (!double.TryParse(cVal, out double dateSerial) || dateSerial < 40000) continue;

                DateTime? occurDate = TryParseExcelDate(dateSerial);
                if (!occurDate.HasValue) continue;

                string dVal = GetCellValue(cells, "D", sst);   // 제품명
                string hVal = GetCellValue(cells, "H", sst);   // 반출라인
                string iVal = GetCellValue(cells, "I", sst);   // S/N
                string kVal = GetCellValue(cells, "K", sst);   // 공식비공식
                string lVal = GetCellValue(cells, "L", sst);   // 사고발생내용 (unused for now)
                string vVal = GetCellValue(cells, "V", sst);   // 유발자
                string wVal = GetCellValue(cells, "W", sst);   // 발생기준경력
                string xVal = GetCellValue(cells, "X", sst);   // 팀별
                string yVal = GetCellValue(cells, "Y", sst);   // 제품종류
                string zVal = GetCellValue(cells, "Z", sst);   // 발생단계
                string abVal = GetCellValue(cells, "AB", sst); // 처리현황

                // Join with career sheet
                string jobTitle = "";
                string careerStr = "";
                if (!string.IsNullOrEmpty(vVal) && careerDict.TryGetValue(vVal.Trim(), out var ci))
                {
                    careerStr = ci.career;
                    jobTitle = ci.title;
                }

                result.Add(new BrokenRecord
                {
                    No = (int)noVal,
                    OccurDate = occurDate,
                    Line = hVal,
                    ProductName = dVal,
                    SN = iVal,
                    Team = xVal,
                    Causer = vVal,
                    JobTitle = jobTitle,
                    CareerAtOccur = string.IsNullOrEmpty(careerStr) ? wVal : careerStr,
                    ProductType = yVal,
                    OccurStage = zVal,
                    Status = abVal,
                    IsOfficial = kVal
                });
            }

            return result.OrderBy(r => r.No).ToList();
        }

        // -----------------------------------------------------------------------
        // Sheet 2: 생산팀 경력 parser
        //   Rows 9+: C=성명, I=경력, J=부서/직위
        // -----------------------------------------------------------------------
        private Dictionary<string, (string career, string title)> ParseCareerSheet(
            WorksheetPart wsp,
            WorkbookPart wbp)
        {
            var sst = wbp.SharedStringTablePart?.SharedStringTable;
            var rows = wsp.Worksheet.Descendants<Row>()
                          .Where(r => r.RowIndex != null && r.RowIndex.Value >= 9)
                          .ToList();

            var dict = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var cells = row.Elements<Cell>().ToList();
                string name = GetCellValue(cells, "C", sst).Trim();
                if (string.IsNullOrEmpty(name)) continue;

                string career = GetCellValue(cells, "I", sst).Trim();
                string title = GetCellValue(cells, "J", sst).Trim();

                // Career might be stored as a number in I; fall back to building from F/G/H
                if (string.IsNullOrEmpty(career) || double.TryParse(career, out _))
                {
                    string fy = GetCellValue(cells, "F", sst);
                    string gm = GetCellValue(cells, "G", sst);
                    string hd = GetCellValue(cells, "H", sst);
                    if (!string.IsNullOrEmpty(fy) || !string.IsNullOrEmpty(gm))
                    {
                        career = $"{fy}년 {gm}월 {hd}일".Trim();
                    }
                }

                dict[name] = (career, title);
            }

            return dict;
        }

        // -----------------------------------------------------------------------
        // Filter helpers
        // -----------------------------------------------------------------------
        private void ResetFilterComboBoxes()
        {
            _suppressFilter = true;
            CmbYear.Items.Clear();
            CmbYear.Items.Add("전체");
            CmbYear.SelectedIndex = 0;

            CmbLine.Items.Clear();
            CmbLine.Items.Add("전체");
            CmbLine.SelectedIndex = 0;

            CmbTeam.Items.Clear();
            CmbTeam.Items.Add("전체");
            CmbTeam.SelectedIndex = 0;
            _suppressFilter = false;
        }

        private void PopulateFilterComboBoxes()
        {
            _suppressFilter = true;

            CmbYear.Items.Clear();
            CmbYear.Items.Add("전체");
            foreach (var y in _allRecords
                .Where(r => r.OccurDate.HasValue)
                .Select(r => r.OccurDate!.Value.Year)
                .Distinct()
                .OrderBy(y => y))
                CmbYear.Items.Add(y.ToString());
            CmbYear.SelectedIndex = 0;

            CmbLine.Items.Clear();
            CmbLine.Items.Add("전체");
            foreach (var l in _allRecords
                .Select(r => r.Line)
                .Where(l => !string.IsNullOrEmpty(l))
                .Distinct()
                .OrderBy(l => l))
                CmbLine.Items.Add(l);
            CmbLine.SelectedIndex = 0;

            CmbTeam.Items.Clear();
            CmbTeam.Items.Add("전체");
            foreach (var t in _allRecords
                .Select(r => r.Team)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .OrderBy(t => t))
                CmbTeam.Items.Add(t);
            CmbTeam.SelectedIndex = 0;

            _suppressFilter = false;
        }

        private void ApplyFilter()
        {
            if (_allRecords == null) return;

            string selectedYear = CmbYear.SelectedItem?.ToString() ?? "전체";
            string selectedLine = CmbLine.SelectedItem?.ToString() ?? "전체";
            string selectedTeam = CmbTeam.SelectedItem?.ToString() ?? "전체";

            var filtered = _allRecords.AsEnumerable();

            if (selectedYear != "전체")
            {
                if (int.TryParse(selectedYear, out int yr))
                    filtered = filtered.Where(r => r.OccurDate.HasValue && r.OccurDate.Value.Year == yr);
            }

            if (selectedLine != "전체")
                filtered = filtered.Where(r => r.Line == selectedLine);

            if (selectedTeam != "전체")
                filtered = filtered.Where(r => r.Team == selectedTeam);

            _filteredRecords.Clear();
            foreach (var rec in filtered)
                _filteredRecords.Add(rec);

            TxtRecordCount.Text = $"총 {_allRecords.Count}건 (표시: {_filteredRecords.Count}건)";

            RefreshTeamSummary(filtered.ToList());
        }

        // -----------------------------------------------------------------------
        // Team summary
        // -----------------------------------------------------------------------
        private void RefreshTeamSummary(List<BrokenRecord>? source = null)
        {
            source ??= _allRecords;

            _teamSummaries.Clear();

            // Determine payment month based on current half-year
            int month = DateTime.Now.Month;
            string payMonth = month >= 1 && month <= 6 ? "7월 지급예정" : "익년 1월 지급예정";

            var groups = source
                .Where(r => !string.IsNullOrEmpty(r.Team))
                .GroupBy(r => r.Team)
                .OrderBy(g => g.Key);

            foreach (var g in groups)
            {
                double weighted = g.Sum(r => r.AccidentWeight);
                _teamSummaries.Add(new TeamSummary
                {
                    Team = g.Key,
                    RawCount = g.Count(),
                    WeightedCount = weighted,
                    IsAchieved = weighted == 0,
                    PaymentMonth = payMonth
                });
            }
        }

        // -----------------------------------------------------------------------
        // OpenXml helpers
        // -----------------------------------------------------------------------
        private static string GetCellValue(List<Cell> cells, string colRef, SharedStringTable? sst)
        {
            var cell = cells.FirstOrDefault(c =>
            {
                if (c.CellReference?.Value == null) return false;
                string addr = c.CellReference.Value;
                // Extract column letters from address (e.g. "AB12" -> "AB")
                string col = new string(addr.TakeWhile(ch => !char.IsDigit(ch)).ToArray());
                return string.Equals(col, colRef, StringComparison.OrdinalIgnoreCase);
            });

            if (cell == null) return "";

            string raw = cell.InnerText ?? "";

            // Shared string
            if (cell.DataType?.Value == CellValues.SharedString && sst != null)
            {
                if (int.TryParse(raw, out int idx))
                {
                    var ssItem = sst.ElementAt(idx);
                    return ssItem?.InnerText ?? "";
                }
            }

            return raw;
        }

        private static DateTime? TryParseExcelDate(double serial)
        {
            try
            {
                // Excel date serial: 1 = Jan 1, 1900 (with the 1900 leap year bug)
                // OADate handles this correctly
                return DateTime.FromOADate(serial);
            }
            catch
            {
                return null;
            }
        }
    }
}
