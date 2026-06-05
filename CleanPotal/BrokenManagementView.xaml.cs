using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Win32;
using WpfBorder = System.Windows.Controls.Border;
using WpfColor = System.Windows.Media.Color;

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

    // ─── Thumbnail converters ────────────────────────────────────────────────────

    public class FileThumbnailConverter : IValueConverter
    {
        private static readonly HashSet<string> _img = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".ico" };

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || !File.Exists(path)) return null;
            if (!_img.Contains(Path.GetExtension(path))) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path);
                bi.DecodePixelWidth = 80;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    public class IsImageFileConverter : IValueConverter
    {
        private static readonly HashSet<string> _img = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".ico" };
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string path && _img.Contains(Path.GetExtension(path))
                ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    public class IsNotImageFileConverter : IValueConverter
    {
        private static readonly HashSet<string> _img = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".ico" };
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string path && _img.Contains(Path.GetExtension(path))
                ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    public class FileTypeTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path) return "FILE";
            return Path.GetExtension(path).ToLower() switch
            {
                ".xlsx" or ".xls"  => "XLS",
                ".ppt"  or ".pptx" => "PPT",
                ".pdf"             => "PDF",
                var ext            => ext.TrimStart('.').ToUpper()
            };
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    public class FileTypeBgConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path) return new SolidColorBrush(WpfColor.FromRgb(0x94, 0xA3, 0xB8));
            return Path.GetExtension(path).ToLower() switch
            {
                ".xlsx" or ".xls"  => new SolidColorBrush(WpfColor.FromRgb(0x16, 0xA3, 0x4A)),
                ".ppt"  or ".pptx" => new SolidColorBrush(WpfColor.FromRgb(0xEA, 0x58, 0x0C)),
                ".pdf"             => new SolidColorBrush(WpfColor.FromRgb(0xDC, 0x26, 0x26)),
                _                  => new SolidColorBrush(WpfColor.FromRgb(0x63, 0x66, 0xF1))
            };
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    // ---------------------------------------------------------------------------
    // Data models
    // ---------------------------------------------------------------------------
    public class BrokenRecord : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string n) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));

        public BrokenRecord()
        {
            IncidentReports.CollectionChanged       += (_, _) => { Notify(nameof(HasIncident));       Notify(nameof(IncidentLabel));       };
            CountermeasureReports.CollectionChanged  += (_, _) => { Notify(nameof(HasCountermeasure)); Notify(nameof(CountermeasureLabel)); };
            TrainingDocs.CollectionChanged           += (_, _) => { Notify(nameof(HasTraining));       Notify(nameof(TrainingLabel));       };
            TrainingImages.CollectionChanged         += (_, _) => { Notify(nameof(HasTrainingImage));  Notify(nameof(TrainingImageLabel));  };
        }

        public int No { get; set; }

        private int _displayNo;
        public int DisplayNo { get => _displayNo; set { if (_displayNo == value) return; _displayNo = value; Notify(nameof(DisplayNo)); } }

        private DateTime? _occurDate;
        public DateTime? OccurDate
        {
            get => _occurDate;
            set { _occurDate = value; Notify(nameof(OccurDate)); Notify(nameof(OccurYear)); Notify(nameof(OccurDateShort)); }
        }

        public string OccurYear => OccurDate.HasValue ? $"{OccurDate.Value.Year - 2000}년" : "-";
        public string OccurDateShort => OccurDate.HasValue
            ? $"{OccurDate.Value.Month:D2}월 {OccurDate.Value.Day:D2}일 ({DayOfWeekKorean(OccurDate.Value)})"
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

        public ObservableCollection<string> IncidentReports      { get; } = new();
        public ObservableCollection<string> CountermeasureReports { get; } = new();
        public ObservableCollection<string> TrainingDocs          { get; } = new();
        public ObservableCollection<string> TrainingImages        { get; } = new();

        public bool   HasIncident        => IncidentReports.Count > 0;
        public string IncidentLabel      => IncidentReports.Count switch {
            0 => "첨부", 1 => $"경위서{Path.GetExtension(IncidentReports[0])}", _ => $"경위서 {IncidentReports.Count}건" };
        public bool   HasCountermeasure  => CountermeasureReports.Count > 0;
        public string CountermeasureLabel => CountermeasureReports.Count switch {
            0 => "첨부", 1 => $"대책서{Path.GetExtension(CountermeasureReports[0])}", _ => $"대책서 {CountermeasureReports.Count}건" };
        public bool   HasTraining        => TrainingDocs.Count > 0;
        public string TrainingLabel      => TrainingDocs.Count switch {
            0 => "첨부", 1 => $"교육서{Path.GetExtension(TrainingDocs[0])}", _ => $"교육서 {TrainingDocs.Count}건" };
        public bool   HasTrainingImage   => TrainingImages.Count > 0;
        public string TrainingImageLabel => TrainingImages.Count switch {
            0 => "첨부", 1 => $"교육이미지{Path.GetExtension(TrainingImages[0])}", _ => $"교육이미지 {TrainingImages.Count}건" };

        private static string DayOfWeekKorean(DateTime d) => d.DayOfWeek switch
        {
            DayOfWeek.Monday    => "월", DayOfWeek.Tuesday  => "화",
            DayOfWeek.Wednesday => "수", DayOfWeek.Thursday => "목",
            DayOfWeek.Friday    => "금", DayOfWeek.Saturday => "토",
            DayOfWeek.Sunday    => "일", _ => ""
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
    // ---------------------------------------------------------------------------
    // Save DTOs
    // ---------------------------------------------------------------------------
    public class AppSaveData
    {
        public List<BrokenRecordDto> Records { get; set; } = new();
        public string Memo { get; set; } = "";
    }

    public class BrokenRecordDto
    {
        public int No { get; set; }
        public DateTime? OccurDate { get; set; }
        public string Line { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string SN { get; set; } = "";
        public string Team { get; set; } = "";
        public string Causer { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string ProductType { get; set; } = "";
        public string OccurStage { get; set; } = "";
        public string Status { get; set; } = "";
        public string IsOfficial { get; set; } = "";
        public List<string> IncidentReports { get; set; } = new();
        public List<string> CountermeasureReports { get; set; } = new();
        public List<string> TrainingDocs { get; set; } = new();
        public List<string> TrainingImages { get; set; } = new();
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
            LoadAppData();
        }

        public void TryRefresh() { }

        // -----------------------------------------------------------------------
        // File load — button / click
        // -----------------------------------------------------------------------
        private void BtnLoadFile_Click(object sender, RoutedEventArgs e) => OpenFilePicker();
        private void DropZone_Click(object sender, MouseButtonEventArgs e) => OpenFilePicker();

        private void OpenFilePicker()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Broken 현황 Excel 파일 선택",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;
            LoadFile(dlg.FileName);
        }

        private void LoadFile(string path)
        {
            try
            {
                LoadDataFromExcel(path);
                TxtFilePath.Text = Path.GetFileName(path);
                TxtFilePath.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일 읽기 오류:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtFilePath.Text = "";
                TxtRecordCount.Text = "";
            }
        }

        // -----------------------------------------------------------------------
        // Drag and drop
        // -----------------------------------------------------------------------
        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files?.Any(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) == true)
                {
                    e.Effects = DragDropEffects.Copy;
                    SetDropZoneActive(true);
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            var pos = e.GetPosition(DropZoneBg);
            if (pos.X < 0 || pos.Y < 0 ||
                pos.X > DropZoneBg.ActualWidth ||
                pos.Y > DropZoneBg.ActualHeight)
                SetDropZoneActive(false);
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            SetDropZoneActive(false);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var xlsx = files?.FirstOrDefault(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
            if (xlsx == null)
            {
                MessageBox.Show("xlsx 파일만 지원합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            LoadFile(xlsx);
        }

        private void SetDropZoneActive(bool active)
        {
            if (active)
            {
                RectDash.Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB));
                DropZoneBg.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0xF6, 0xFF));
                TxtDropIcon.Text = "📥";
                TxtDropHint.Text = "여기에 놓으세요!";
                TxtDropHint.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB));
            }
            else
            {
                RectDash.Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCB, 0xD5, 0xE1));
                DropZoneBg.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0xFA, 0xFC));
                TxtDropIcon.Text = "📂";
                TxtDropHint.Text = "xlsx 파일을 여기에 드래그하세요";
                TxtDropHint.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B));
            }
        }

        // -----------------------------------------------------------------------
        // Filter events
        // -----------------------------------------------------------------------
        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilter) return;
            if (ReferenceEquals(sender, CmbYear))
                RepopulateSubFilters();
            ApplyFilter();
        }

        private void FilterCombo_DropDownOpened(object sender, EventArgs e)
        {
            if (sender is not ComboBox cmb) return;
            cmb.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                if (cmb.Template.FindName("PART_Popup", cmb) is System.Windows.Controls.Primitives.Popup popup
                    && popup.Child != null)
                {
                    FindVisualChild<ScrollViewer>(popup.Child)?.ScrollToTop();
                }
            });
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void BtnResetFilter_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilter = true;
            if (CmbYear.Items.Count > 0) CmbYear.SelectedIndex = 0;
            _suppressFilter = false;
            RepopulateSubFilters();
            ApplyFilter();
        }

        // -----------------------------------------------------------------------
        // Row add / delete
        // -----------------------------------------------------------------------
        private void BtnAddRow_Click(object sender, RoutedEventArgs e)
        {
            var newRecord = new BrokenRecord { OccurDate = DateTime.Today };
            _allRecords.Add(newRecord);
            newRecord.DisplayNo = _filteredRecords.Count + 1;
            _filteredRecords.Add(newRecord);
            TxtRecordCount.Text = _allRecords.Count == _filteredRecords.Count
                ? $"총 {_allRecords.Count}건"
                : $"총 {_allRecords.Count}건 (표시: {_filteredRecords.Count}건)";

            DgBroken.SelectedItem = newRecord;
            DgBroken.ScrollIntoView(newRecord);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                DgBroken.UpdateLayout();
                DgBroken.CurrentCell = new DataGridCellInfo(newRecord, DgBroken.Columns[1]);
                DgBroken.BeginEdit();
            });
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (DgBroken.SelectedItem is not BrokenRecord selected) return;
            if (MessageBox.Show("선택한 행을 삭제하시겠습니까?", "확인",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _allRecords.Remove(selected);
            ApplyFilter();
        }

        // -----------------------------------------------------------------------
        // Save (JSON) / Load
        // -----------------------------------------------------------------------
        private static readonly string SaveFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanPotal", "broken_data.json");

        private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveAppData();
                MessageBox.Show("저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveAppData()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SaveFilePath)!);
            var dto = new AppSaveData
            {
                Memo = TxtMemo.Text,
                Records = _allRecords.Select(r => new BrokenRecordDto
                {
                    No = r.No, OccurDate = r.OccurDate,
                    Line = r.Line, ProductName = r.ProductName, SN = r.SN,
                    Team = r.Team, Causer = r.Causer, JobTitle = r.JobTitle,
                    ProductType = r.ProductType, OccurStage = r.OccurStage,
                    Status = r.Status, IsOfficial = r.IsOfficial,
                    IncidentReports = r.IncidentReports.ToList(),
                    CountermeasureReports = r.CountermeasureReports.ToList(),
                    TrainingDocs = r.TrainingDocs.ToList(),
                    TrainingImages = r.TrainingImages.ToList()
                }).ToList()
            };
            File.WriteAllText(SaveFilePath, JsonSerializer.Serialize(dto, _jsonOpts), Encoding.UTF8);
        }

        private void LoadAppData()
        {
            if (!File.Exists(SaveFilePath)) return;
            try
            {
                var dto = JsonSerializer.Deserialize<AppSaveData>(
                    File.ReadAllText(SaveFilePath, Encoding.UTF8), _jsonOpts);
                if (dto == null) return;

                _allRecords = dto.Records.Select(d =>
                {
                    var r = new BrokenRecord
                    {
                        No = d.No, OccurDate = d.OccurDate,
                        Line = d.Line, ProductName = d.ProductName, SN = d.SN,
                        Team = d.Team, Causer = d.Causer, JobTitle = d.JobTitle,
                        ProductType = d.ProductType, OccurStage = d.OccurStage,
                        Status = d.Status, IsOfficial = d.IsOfficial
                    };
                    foreach (var p in d.IncidentReports)      r.IncidentReports.Add(p);
                    foreach (var p in d.CountermeasureReports) r.CountermeasureReports.Add(p);
                    foreach (var p in d.TrainingDocs)          r.TrainingDocs.Add(p);
                    foreach (var p in d.TrainingImages)        r.TrainingImages.Add(p);
                    return r;
                }).ToList();

                TxtMemo.Text = dto.Memo ?? "";
                PopulateFilterComboBoxes();
                ApplyFilter();
            }
            catch { /* 손상된 저장파일 무시 */ }
        }

        // -----------------------------------------------------------------------
        // Excel 내보내기
        // -----------------------------------------------------------------------
        private string? _exportFilePath;

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "엑셀 내보내기",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = _exportFilePath != null ? Path.GetFileName(_exportFilePath) : "broken_export.xlsx"
            };
            if (_exportFilePath != null)
                dlg.InitialDirectory = Path.GetDirectoryName(_exportFilePath);
            if (dlg.ShowDialog() != true) return;
            _exportFilePath = dlg.FileName;
            try
            {
                SaveToExcel(_exportFilePath);
                MessageBox.Show("내보내기 완료", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"내보내기 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveToExcel(string filePath)
        {
            using var doc = SpreadsheetDocument.Create(filePath, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
            var wbp = doc.AddWorkbookPart();
            wbp.Workbook = new Workbook();
            var wsp = wbp.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            wsp.Worksheet = new Worksheet(sheetData);
            wbp.Workbook.AppendChild(new Sheets()).Append(new Sheet
            {
                Id = wbp.GetIdOfPart(wsp), SheetId = 1, Name = "Broken현황"
            });

            string[] headers = { "NO", "발생일", "라인", "제품명", "S/N", "팀", "유발자", "직위", "제품종류", "발생단계", "경위서", "대책서", "교육서", "교육이미지" };
            sheetData.AppendChild(MakeSaveRow(1u, headers));

            uint ri = 2; int no = 1;
            foreach (var r in _allRecords)
            {
                sheetData.AppendChild(MakeSaveRow(ri++, new[]
                {
                    (no++).ToString(),
                    r.OccurDate?.ToString("yyyy-MM-dd") ?? "",
                    r.Line, r.ProductName, r.SN, r.Team, r.Causer, r.JobTitle,
                    r.ProductType, r.OccurStage,
                    string.Join(";", r.IncidentReports),
                    string.Join(";", r.CountermeasureReports),
                    string.Join(";", r.TrainingDocs),
                    string.Join(";", r.TrainingImages)
                }));
            }
            wbp.Workbook.Save();
        }

        private static Row MakeSaveRow(uint rowIdx, string[] values)
        {
            var row = new Row { RowIndex = rowIdx };
            for (int c = 0; c < values.Length; c++)
                row.AppendChild(new Cell
                {
                    CellReference = SaveColRef(c) + rowIdx,
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString { Text = new DocumentFormat.OpenXml.Spreadsheet.Text(values[c] ?? "") }
                });
            return row;
        }

        private static string SaveColRef(int c)
        {
            string r = ""; c++;
            while (c > 0) { r = (char)('A' + (c - 1) % 26) + r; c = (c - 1) / 26; }
            return r;
        }

        // -----------------------------------------------------------------------
        // File attach — chip clicks (compact badge)
        // -----------------------------------------------------------------------
        private static readonly string AttachFilter =
            "지원 파일|*.xlsx;*.xls;*.ppt;*.pptx;*.pdf;" +
                       "*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tiff;*.tif;*.webp;*.ico|" +
            "이미지|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tiff;*.tif;*.webp;*.ico|" +
            "Excel|*.xlsx;*.xls|PowerPoint|*.ppt;*.pptx|PDF|*.pdf";

        private void AttachChipIncident_Click(object sender, MouseButtonEventArgs e)
            => HandleChipClick(sender, r => r.IncidentReports);

        private void AttachChipCountermeasure_Click(object sender, MouseButtonEventArgs e)
            => HandleChipClick(sender, r => r.CountermeasureReports);

        private void AttachChipTraining_Click(object sender, MouseButtonEventArgs e)
            => HandleChipClick(sender, r => r.TrainingDocs);

        private void AttachChipTrainingImages_Click(object sender, MouseButtonEventArgs e)
            => HandleChipClick(sender, r => r.TrainingImages);

        private void HandleChipClick(object sender, Func<BrokenRecord, ObservableCollection<string>> getCol)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not BrokenRecord record) return;
            var col = getCol(record);

            if (col.Count == 0)
            {
                var dlg = new OpenFileDialog { Title = "파일 첨부", Filter = AttachFilter, Multiselect = true };
                if (dlg.ShowDialog() != true) return;
                foreach (var path in dlg.FileNames)
                    if (!col.Contains(path)) col.Add(path);
                return;
            }

            if (col.Count == 1)
            {
                OpenFile(col[0]);
                return;
            }

            // 2건 이상: 어떤 파일을 열지 선택
            var menu = new ContextMenu();
            foreach (var path in col.ToList())
            {
                var captured = path;
                var mi = new MenuItem { Header = Path.GetFileName(captured) };
                mi.Click += (_, _) => OpenFile(captured);
                menu.Items.Add(mi);
            }
            menu.Items.Add(new Separator());
            var addItem = new MenuItem { Header = "파일 추가" };
            addItem.Click += (_, _) =>
            {
                var dlg = new OpenFileDialog { Title = "파일 첨부", Filter = AttachFilter, Multiselect = true };
                if (dlg.ShowDialog() != true) return;
                foreach (var path in dlg.FileNames)
                    if (!col.Contains(path)) col.Add(path);
            };
            menu.Items.Add(addItem);
            menu.Items.Add(new Separator());
            var delItem = new MenuItem { Header = "모두 삭제" };
            delItem.Click += (_, _) => col.Clear();
            menu.Items.Add(delItem);

            menu.PlacementTarget = fe;
            menu.IsOpen = true;
        }

        private static void OpenFile(string path)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"파일 열기 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        // -----------------------------------------------------------------------
        // File attach — drag and drop onto cells
        // -----------------------------------------------------------------------
        private static readonly HashSet<string> _allowedExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".ico",
            ".xlsx", ".xls", ".ppt", ".pptx", ".pdf"
        };

        private void CellAttachment_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (sender is WpfBorder bd)
                    bd.Background = new SolidColorBrush(WpfColor.FromArgb(0x25, 0x25, 0x63, 0xEB));
                e.Effects = DragDropEffects.Copy;
            }
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void CellAttachment_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is not WpfBorder bd) return;
            var pos = e.GetPosition(bd);
            if (pos.X < 0 || pos.Y < 0 || pos.X > bd.ActualWidth || pos.Y > bd.ActualHeight)
                bd.ClearValue(WpfBorder.BackgroundProperty);
        }

        private void CellDropIncident_Drop(object sender, DragEventArgs e)
            => HandleCellDrop(sender, e, r => r.IncidentReports);

        private void CellDropCountermeasure_Drop(object sender, DragEventArgs e)
            => HandleCellDrop(sender, e, r => r.CountermeasureReports);

        private void CellDropTraining_Drop(object sender, DragEventArgs e)
            => HandleCellDrop(sender, e, r => r.TrainingDocs);

        private void CellDropTrainingImages_Drop(object sender, DragEventArgs e)
            => HandleCellDrop(sender, e, r => r.TrainingImages);

        private void HandleCellDrop(object sender, DragEventArgs e,
            Func<BrokenRecord, ObservableCollection<string>> getCol)
        {
            if (sender is WpfBorder bd) bd.ClearValue(WpfBorder.BackgroundProperty);
            if (sender is not FrameworkElement fe || fe.Tag is not BrokenRecord record) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            var col = getCol(record);
            foreach (var path in files)
                if (_allowedExts.Contains(Path.GetExtension(path)) && !col.Contains(path))
                    col.Add(path);
            e.Handled = true;
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
                string id1 = sheets[0].Id?.Value ?? "";
                if (string.IsNullOrEmpty(id1)) throw new InvalidOperationException("첫 번째 시트를 열 수 없습니다.");
                records = ParseBrokenSheet((WorksheetPart)wbp.GetPartById(id1), wbp);
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
        // Sheet parser
        // -----------------------------------------------------------------------
        // Column layout: A(빈칸) B(NO) C(발생일) D(제품명) E(반출라인) F(S/N)
        //                G(공식여부) H(유발자) I(팀별) J(제품종류) K(발생단계)
        private List<BrokenRecord> ParseBrokenSheet(WorksheetPart wsp, WorkbookPart wbp)
        {
            var sst = wbp.SharedStringTablePart?.SharedStringTable;
            var result = new List<BrokenRecord>();
            int currentYear = DateTime.Now.Year;

            foreach (var row in wsp.Worksheet.Descendants<Row>())
            {
                var cells = row.Elements<Cell>().ToList();
                string bVal = GetCellValue(cells, "B", sst);
                string cVal = GetCellValue(cells, "C", sst);

                if (!double.TryParse(bVal, out double noVal) || noVal <= 0) continue;

                var occurDate = TryParseDate(cVal, ref currentYear);
                if (!occurDate.HasValue) continue;

                result.Add(new BrokenRecord
                {
                    No          = (int)noVal,
                    OccurDate   = occurDate,
                    ProductName = GetCellValue(cells, "D", sst),
                    Line        = GetCellValue(cells, "E", sst),
                    SN          = GetCellValue(cells, "F", sst),
                    IsOfficial  = GetCellValue(cells, "G", sst),
                    Causer      = GetCellValue(cells, "H", sst),
                    Team        = GetCellValue(cells, "I", sst),
                    ProductType = GetCellValue(cells, "J", sst),
                    OccurStage  = GetCellValue(cells, "K", sst),
                });
            }

            return result;
        }

        // 날짜 파싱: OADate숫자 / "21.02.10" / "08월 14일"
        private static DateTime? TryParseDate(string val, ref int currentYear)
        {
            if (string.IsNullOrWhiteSpace(val)) return null;

            // OADate serial (Excel 숫자 날짜)
            if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double serial) && serial > 40000)
            {
                try { var dt = DateTime.FromOADate(serial); currentYear = dt.Year; return dt; }
                catch { }
            }

            // "YY.MM.DD" 형식 (예: "21.02.10")
            var parts = val.Trim().Split('.');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out int yy) &&
                int.TryParse(parts[1], out int mm) &&
                int.TryParse(parts[2], out int dd))
            {
                int year = yy < 100 ? 2000 + yy : yy;
                try { var dt = new DateTime(year, mm, dd); currentYear = year; return dt; }
                catch { }
            }

            // "MM월 DD일" 형식 (예: "08월 14일") — 인접 행의 년도를 사용
            var match = Regex.Match(val.Trim(), @"(\d{1,2})월\s*(\d{1,2})일");
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out int month) &&
                int.TryParse(match.Groups[2].Value, out int day))
            {
                try { return new DateTime(currentYear, month, day); }
                catch { }
            }

            return null;
        }

        // -----------------------------------------------------------------------
        // Filter helpers
        // -----------------------------------------------------------------------
        private void ResetFilterComboBoxes()
        {
            _suppressFilter = true;
            CmbYear.Items.Clear();        CmbYear.Items.Add("전체");        CmbYear.SelectedIndex = 0;
            CmbLine.Items.Clear();        CmbLine.Items.Add("전체");        CmbLine.SelectedIndex = 0;
            CmbTeam.Items.Clear();        CmbTeam.Items.Add("전체");        CmbTeam.SelectedIndex = 0;
            CmbProductType.Items.Clear(); CmbProductType.Items.Add("전체"); CmbProductType.SelectedIndex = 0;
            CmbOccurStage.Items.Clear();  CmbOccurStage.Items.Add("전체");  CmbOccurStage.SelectedIndex = 0;
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

            // 현재 년도 자동 선택
            string currentYear = DateTime.Now.Year.ToString();
            CmbYear.SelectedIndex = 0;
            for (int i = 0; i < CmbYear.Items.Count; i++)
                if (CmbYear.Items[i].ToString() == currentYear) { CmbYear.SelectedIndex = i; break; }

            _suppressFilter = false;
            RepopulateSubFilters();
        }

        private void RepopulateSubFilters()
        {
            _suppressFilter = true;

            string year = CmbYear.SelectedItem?.ToString() ?? "전체";
            var base_ = (year != "전체" && int.TryParse(year, out int yr))
                ? _allRecords.Where(r => r.OccurDate.HasValue && r.OccurDate.Value.Year == yr).ToList()
                : _allRecords;

            CmbLine.Items.Clear(); CmbLine.Items.Add("전체");
            foreach (var l in base_.Select(r => r.Line).Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l))
                CmbLine.Items.Add(l);
            CmbLine.SelectedIndex = 0;

            CmbTeam.Items.Clear(); CmbTeam.Items.Add("전체");
            foreach (var t in base_.Select(r => r.Team).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t))
                CmbTeam.Items.Add(t);
            CmbTeam.SelectedIndex = 0;

            CmbProductType.Items.Clear(); CmbProductType.Items.Add("전체");
            foreach (var p in base_.Select(r => r.ProductType).Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderBy(p => p))
                CmbProductType.Items.Add(p);
            CmbProductType.SelectedIndex = 0;

            CmbOccurStage.Items.Clear(); CmbOccurStage.Items.Add("전체");
            foreach (var s in base_.Select(r => r.OccurStage).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s))
                CmbOccurStage.Items.Add(s);
            CmbOccurStage.SelectedIndex = 0;

            _suppressFilter = false;
        }

        private void ApplyFilter()
        {
            string year        = CmbYear.SelectedItem?.ToString()        ?? "전체";
            string line        = CmbLine.SelectedItem?.ToString()        ?? "전체";
            string team        = CmbTeam.SelectedItem?.ToString()        ?? "전체";
            string productType = CmbProductType.SelectedItem?.ToString() ?? "전체";
            string occurStage  = CmbOccurStage.SelectedItem?.ToString()  ?? "전체";

            var filtered = _allRecords.AsEnumerable();
            if (year != "전체" && int.TryParse(year, out int yr))
                filtered = filtered.Where(r => r.OccurDate.HasValue && r.OccurDate.Value.Year == yr);
            if (line != "전체")
                filtered = filtered.Where(r => r.Line == line);
            if (team != "전체")
                filtered = filtered.Where(r => r.Team == team);
            if (productType != "전체")
                filtered = filtered.Where(r => r.ProductType == productType);
            if (occurStage != "전체")
                filtered = filtered.Where(r => r.OccurStage == occurStage);

            var list = filtered.ToList();

            _filteredRecords.Clear();
            int displayNo = 1;
            foreach (var rec in list)
            {
                rec.DisplayNo = displayNo++;
                _filteredRecords.Add(rec);
            }

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

    }
}
