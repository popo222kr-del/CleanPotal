using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Win32;

namespace CleanPotal
{
    // ---------------------------------------------------------------------------
    // Converters
    // ---------------------------------------------------------------------------
    public class FileNameConverter : IValueConverter
    {
        public static readonly FileNameConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string path ? Path.GetFileName(path) : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public static readonly CountToVisibilityConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int c && c > 0 ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // ---------------------------------------------------------------------------
    // Data models
    // ---------------------------------------------------------------------------
    public class BrokenRecord
    {
        public int No { get; set; }
        public DateTime? OccurDate { get; set; }

        public string OccurYear => OccurDate.HasValue
            ? $"{OccurDate.Value.Year - 2000}년"
            : "-";

        public string OccurDateShort => OccurDate.HasValue
            ? $"{OccurDate.Value.Month}월 {OccurDate.Value.Day}일 ({DayOfWeekKorean(OccurDate.Value)})"
            : "-";

        public string Line { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string SN { get; set; } = "";
        public string Team { get; set; } = "";
        public string Causer { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string ProductType { get; set; } = "";
        public double AccidentWeight =>
            ProductType.Equals("acc", StringComparison.OrdinalIgnoreCase) ? 0.5 : 1.0;
        public string OccurStage { get; set; } = "";
        public string Status { get; set; } = "";
        public string IsOfficial { get; set; } = "";

        public ObservableCollection<string> AttachedFiles { get; } = new();

        private static string DayOfWeekKorean(DateTime d) => d.DayOfWeek switch
        {
            DayOfWeek.Monday => "월",
            DayOfWeek.Tuesday => "화",
            DayOfWeek.Wednesday => "수",
            DayOfWeek.Thursday => "목",
            DayOfWeek.Friday => "금",
            DayOfWeek.Saturday => "토",
            DayOfWeek.Sunday => "일",
            _ => ""
        };
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
        private readonly ObservableCollection<BrokenRecord> _filteredRecords = new();
        private readonly ObservableCollection<TeamSummary> _teamSummaries = new();
        private bool _suppressFilter = false;

        public BrokenManagementView()
        {
            InitializeComponent();
            DgBroken.ItemsSource = _filteredRecords;
            DgTeamSummary.ItemsSource = _teamSummaries;
            ResetFilterComboBoxes();
        }

        public void TryRefresh() { }

        // -----------------------------------------------------------------------
        // File load
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

            TxtFilePath.Text = dlg.FileName;
            TxtFilePath.Foreground = System.Windows.Media.Brushes.DimGray;

            try
            {
                LoadDataFromExcel(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일 읽기 오류:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtFilePath.Text = "파일을 선택하세요...";
                TxtFilePath.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        // -----------------------------------------------------------------------
        // Filter events
        // -----------------------------------------------------------------------
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
        // File attach
        // -----------------------------------------------------------------------
        private void BtnAttachFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BrokenRecord record) return;

            var dlg = new OpenFileDialog
            {
                Title = "자료 첨부",
                Filter = "지원 파일|*.xlsx;*.xls;*.ppt;*.pptx;*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.gif|" +
                         "Excel|*.xlsx;*.xls|PowerPoint|*.ppt;*.pptx|PDF|*.pdf|이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            foreach (var path in dlg.FileNames)
                if (!record.AttachedFiles.Contains(path))
                    record.AttachedFiles.Add(path);
        }

        private void FileChip_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string path)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"파일 열기 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // -----------------------------------------------------------------------
        // Excel loading
        // -----------------------------------------------------------------------
        private void LoadDataFromExcel(string filePath)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"broken_{Guid.NewGuid():N}.xlsx");
            List<BrokenRecord> records;
            try
            {
                File.Copy(filePath, tempPath, true);
                using var doc = SpreadsheetDocument.Open(tempPath, isEditable: false);
                var wbp = doc.WorkbookPart ?? throw new InvalidOperationException("WorkbookPart 없음");
                var sheets = wbp.Workbook.Sheets?.Cast<Sheet>().ToList() ?? new List<Sheet>();
                if (sheets.Count == 0) throw new InvalidOperationException("시트가 없습니다.");

                var careerDict = new Dictionary<string, (string career, string title)>(StringComparer.OrdinalIgnoreCase);
                if (sheets.Count >= 2)
                {
                    string id2 = sheets[1].Id?.Value ?? "";
                    if (!string.IsNullOrEmpty(id2))
                        careerDict = ParseCareerSheet((WorksheetPart)wbp.GetPartById(id2), wbp);
                }

                string id1 = sheets[0].Id?.Value ?? "";
                if (string.IsNullOrEmpty(id1)) throw new InvalidOperationException("첫 번째 시트를 열 수 없습니다.");
                records = ParseBrokenSheet((WorksheetPart)wbp.GetPartById(id1), wbp, careerDict);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }

            _allRecords = records;
            PopulateFilterComboBoxes();
            ApplyFilter();
        }

        // -----------------------------------------------------------------------
        // Sheet parsers
        // -----------------------------------------------------------------------
        private List<BrokenRecord> ParseBrokenSheet(
            WorksheetPart wsp, WorkbookPart wbp,
            Dictionary<string, (string career, string title)> careerDict)
        {
            var sst = wbp.SharedStringTablePart?.SharedStringTable;
            var result = new List<BrokenRecord>();

            foreach (var row in wsp.Worksheet.Descendants<Row>())
            {
                var cells = row.Elements<Cell>().ToList();
                string bVal = GetCellValue(cells, "B", sst);
                string cVal = GetCellValue(cells, "C", sst);

                if (!double.TryParse(bVal, out double noVal) || noVal <= 0) continue;
                if (!double.TryParse(cVal, out double dateSerial) || dateSerial < 40000) continue;

                var occurDate = TryParseExcelDate(dateSerial);
                if (!occurDate.HasValue) continue;

                string vVal = GetCellValue(cells, "V", sst);
                string jobTitle = "";
                if (!string.IsNullOrEmpty(vVal) && careerDict.TryGetValue(vVal.Trim(), out var ci))
                    jobTitle = ci.title;

                result.Add(new BrokenRecord
                {
                    No = (int)noVal,
                    OccurDate = occurDate,
                    Line = GetCellValue(cells, "H", sst),
                    ProductName = GetCellValue(cells, "D", sst),
                    SN = GetCellValue(cells, "I", sst),
                    Team = GetCellValue(cells, "X", sst),
                    Causer = vVal,
                    JobTitle = jobTitle,
                    ProductType = GetCellValue(cells, "Y", sst),
                    OccurStage = GetCellValue(cells, "Z", sst),
                    Status = GetCellValue(cells, "AB", sst),
                    IsOfficial = GetCellValue(cells, "K", sst)
                });
            }

            return result.OrderBy(r => r.No).ToList();
        }

        private Dictionary<string, (string, string)> ParseCareerSheet(WorksheetPart wsp, WorkbookPart wbp)
        {
            var sst = wbp.SharedStringTablePart?.SharedStringTable;
            var dict = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in wsp.Worksheet.Descendants<Row>()
                         .Where(r => r.RowIndex?.Value >= 9))
            {
                var cells = row.Elements<Cell>().ToList();
                string name = GetCellValue(cells, "C", sst).Trim();
                if (string.IsNullOrEmpty(name)) continue;

                string career = GetCellValue(cells, "I", sst).Trim();
                string title = GetCellValue(cells, "J", sst).Trim();

                if (string.IsNullOrEmpty(career) || double.TryParse(career, out _))
                {
                    string fy = GetCellValue(cells, "F", sst);
                    string gm = GetCellValue(cells, "G", sst);
                    string hd = GetCellValue(cells, "H", sst);
                    if (!string.IsNullOrEmpty(fy) || !string.IsNullOrEmpty(gm))
                        career = $"{fy}년 {gm}월 {hd}일".Trim();
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
            CmbYear.Items.Clear(); CmbYear.Items.Add("전체"); CmbYear.SelectedIndex = 0;
            CmbLine.Items.Clear(); CmbLine.Items.Add("전체"); CmbLine.SelectedIndex = 0;
            CmbTeam.Items.Clear(); CmbTeam.Items.Add("전체"); CmbTeam.SelectedIndex = 0;
            _suppressFilter = false;
        }

        private void PopulateFilterComboBoxes()
        {
            _suppressFilter = true;

            CmbYear.Items.Clear();
            CmbYear.Items.Add("전체");
            foreach (var y in _allRecords.Where(r => r.OccurDate.HasValue)
                         .Select(r => r.OccurDate!.Value.Year).Distinct().OrderBy(y => y))
                CmbYear.Items.Add(y.ToString());
            CmbYear.SelectedIndex = 0;

            CmbLine.Items.Clear();
            CmbLine.Items.Add("전체");
            foreach (var l in _allRecords.Select(r => r.Line)
                         .Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l))
                CmbLine.Items.Add(l);
            CmbLine.SelectedIndex = 0;

            CmbTeam.Items.Clear();
            CmbTeam.Items.Add("전체");
            foreach (var t in _allRecords.Select(r => r.Team)
                         .Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t))
                CmbTeam.Items.Add(t);
            CmbTeam.SelectedIndex = 0;

            _suppressFilter = false;
        }

        private void ApplyFilter()
        {
            string year = CmbYear.SelectedItem?.ToString() ?? "전체";
            string line = CmbLine.SelectedItem?.ToString() ?? "전체";
            string team = CmbTeam.SelectedItem?.ToString() ?? "전체";

            var filtered = _allRecords.AsEnumerable();
            if (year != "전체" && int.TryParse(year, out int yr))
                filtered = filtered.Where(r => r.OccurDate.HasValue && r.OccurDate.Value.Year == yr);
            if (line != "전체")
                filtered = filtered.Where(r => r.Line == line);
            if (team != "전체")
                filtered = filtered.Where(r => r.Team == team);

            var list = filtered.ToList();

            _filteredRecords.Clear();
            foreach (var rec in list)
                _filteredRecords.Add(rec);

            TxtRecordCount.Text = _allRecords.Count == _filteredRecords.Count
                ? $"총 {_allRecords.Count}건"
                : $"총 {_allRecords.Count}건 (표시: {_filteredRecords.Count}건)";

            RefreshTeamSummary(list);
        }

        private void RefreshTeamSummary(List<BrokenRecord>? source = null)
        {
            source ??= _allRecords;
            _teamSummaries.Clear();

            string payMonth = DateTime.Now.Month <= 6 ? "7월 지급예정" : "익년 1월 지급예정";

            foreach (var g in source.Where(r => !string.IsNullOrEmpty(r.Team))
                         .GroupBy(r => r.Team).OrderBy(g => g.Key))
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
                string col = new string(c.CellReference.Value.TakeWhile(ch => !char.IsDigit(ch)).ToArray());
                return string.Equals(col, colRef, StringComparison.OrdinalIgnoreCase);
            });
            if (cell == null) return "";
            string raw = cell.InnerText ?? "";
            if (cell.DataType?.Value == CellValues.SharedString && sst != null)
                if (int.TryParse(raw, out int idx))
                    return sst.ElementAt(idx)?.InnerText ?? "";
            return raw;
        }

        private static DateTime? TryParseExcelDate(double serial)
        {
            try { return DateTime.FromOADate(serial); }
            catch { return null; }
        }
    }
}
