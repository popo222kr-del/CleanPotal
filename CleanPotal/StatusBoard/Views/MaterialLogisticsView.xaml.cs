using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CleanPotal.StatusBoard.Models;
using CleanPotal.StatusBoard.Repositories;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace CleanPotal.StatusBoard.Views
{
    public partial class MaterialLogisticsView : UserControl
    {
        // ── Vehicle definitions: (display label, vehicle number key) ──
        private static readonly (string Label, string Key)[] Vehicles = new[]
        {
            ("5t\n(2255)", "2255"),
            ("5t\n(5907)", "5907"),
            ("3.5t\n(5335)", "5335"),
            ("1t\n(0765)", "0765"),
            ("1t\n(4795)", "4795"),
        };

        private readonly ObservableCollection<MaterialLogisticsRow> _rows = new();
        private DateTime _selectedDate;
        private bool _isLoading; // suppress events during programmatic changes

        // Column indices (set during BuildGrid)
        private const int ColPersonName = 0;
        private const int ColAmDest = 1;
        private const int ColAmVehicleStart = 2;  // 2..6  (5 vehicles)
        private const int ColPmDest = 7;
        private const int ColPmVehicleStart = 8;  // 8..12 (5 vehicles)
        private const int TotalColumns = 13;

        public MaterialLogisticsView()
        {
            InitializeComponent();

            _selectedDate = DateTime.Today;
            DpBoardDate.SelectedDate = _selectedDate;

            Loaded += (_, _) => LoadData();
        }

        // ══════════════════════════════════════════════════════════════
        //  Date Navigation
        // ══════════════════════════════════════════════════════════════

        private void DpBoardDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || DpBoardDate.SelectedDate == null) return;
            _selectedDate = DpBoardDate.SelectedDate.Value;
            LoadData();
        }

        private void BtnPrevDay_Click(object sender, RoutedEventArgs e)
        {
            _selectedDate = _selectedDate.AddDays(-1);
            SyncDatePicker();
            LoadData();
        }

        private void BtnNextDay_Click(object sender, RoutedEventArgs e)
        {
            _selectedDate = _selectedDate.AddDays(1);
            SyncDatePicker();
            LoadData();
        }

        private void BtnToday_Click(object sender, RoutedEventArgs e)
        {
            _selectedDate = DateTime.Today;
            SyncDatePicker();
            LoadData();
        }

        private void SyncDatePicker()
        {
            _isLoading = true;
            DpBoardDate.SelectedDate = _selectedDate;
            _isLoading = false;
        }

        // ══════════════════════════════════════════════════════════════
        //  Data Loading
        // ══════════════════════════════════════════════════════════════

        private void LoadData()
        {
            UpdateHeader();

            string dateKey = _selectedDate.ToString("yyyy-MM-dd");

            try
            {
                var entries = StatusBoardRepository.GetAllMaterialLogistics(dateKey);
                var memoRow = entries.FirstOrDefault(r => r.PersonName == "__MEMO__");
                var dataRows = entries.Where(r => r.PersonName != "__MEMO__").OrderBy(r => r.OrderNo).ToList();

                _rows.Clear();
                foreach (var entry in dataRows)
                    _rows.Add(entry);

                TxtSpecialNotes.Text = memoRow?.Memo ?? "";

                // If no data for this date, add a few blank rows
                if (_rows.Count == 0)
                {
                    for (int i = 0; i < 5; i++)
                        _rows.Add(new MaterialLogisticsRow
                        {
                            BoardDate = dateKey,
                            OrderNo = i + 1
                        });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"데이터 로딩 중 오류:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            BuildGrid();
        }

        private void UpdateHeader()
        {
            var culture = new CultureInfo("ko-KR");
            string[] dayNames = { "일요일", "월요일", "화요일", "수요일", "목요일", "금요일", "토요일" };
            string dayName = dayNames[(int)_selectedDate.DayOfWeek];
            TxtDate.Text = $"{_selectedDate:yyyy}년 {_selectedDate.Month}월 {_selectedDate.Day}일 {dayName}";
        }

        // ══════════════════════════════════════════════════════════════
        //  Grid Building
        // ══════════════════════════════════════════════════════════════

        private void BuildGrid()
        {
            var grid = ScheduleGrid;
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();

            // ── Define columns ──
            // Col 0: 담당자
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            // Col 1: 오전 목적지
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
            // Col 2-6: 오전 vehicles
            for (int v = 0; v < Vehicles.Length; v++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            // Col 7: 오후 목적지
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
            // Col 8-12: 오후 vehicles
            for (int v = 0; v < Vehicles.Length; v++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            // ── Define rows: 2 header rows + data rows ──
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: section headers
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: column headers
            for (int i = 0; i < _rows.Count; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

            // ── Row 0: Section Headers ──
            AddSectionHeader(grid, "담당자", 0, 0, 1, 2, "#0F172A", "#F1F5F9");

            // AM section
            AddSectionHeader(grid, "오전 (AM)", 0, ColAmDest, 1 + Vehicles.Length, 1, "#1E40AF", "#DBEAFE");
            // PM section
            AddSectionHeader(grid, "오후 (PM)", 0, ColPmDest, 1 + Vehicles.Length, 1, "#9A3412", "#FED7AA");

            // ── Row 1: Column Sub-headers ──
            AddColumnHeader(grid, "이름", 1, ColPersonName, "#64748B", "#F8FAFC");
            AddColumnHeader(grid, "목적지 & 근무", 1, ColAmDest, "#1E40AF", "#EFF6FF");
            for (int v = 0; v < Vehicles.Length; v++)
                AddColumnHeader(grid, Vehicles[v].Label, 1, ColAmVehicleStart + v, "#1E40AF", "#EFF6FF");
            AddColumnHeader(grid, "목적지 & 근무", 1, ColPmDest, "#9A3412", "#FFF7ED");
            for (int v = 0; v < Vehicles.Length; v++)
                AddColumnHeader(grid, Vehicles[v].Label, 1, ColPmVehicleStart + v, "#9A3412", "#FFF7ED");

            // ── Data Rows ──
            for (int i = 0; i < _rows.Count; i++)
            {
                int gridRow = i + 2;
                var row = _rows[i];
                string altBg = (i % 2 == 0) ? "#FFFFFF" : "#FAFAFA";

                // Person name (editable)
                AddEditableCell(grid, gridRow, ColPersonName, row.PersonName, altBg, "#334155", true,
                    (val) => row.PersonName = val);

                // AM destination (editable)
                AddEditableCell(grid, gridRow, ColAmDest, row.AmDestination, altBg, "#334155", false,
                    (val) => row.AmDestination = val);

                // AM vehicle toggles
                for (int v = 0; v < Vehicles.Length; v++)
                {
                    int vi = v; // capture
                    bool isAssigned = row.AmVehicle == Vehicles[v].Key;
                    AddVehicleToggle(grid, gridRow, ColAmVehicleStart + v, isAssigned, altBg,
                        () => ToggleVehicle(row, "AM", Vehicles[vi].Key));
                }

                // PM destination (editable)
                AddEditableCell(grid, gridRow, ColPmDest, row.PmDestination, altBg, "#334155", false,
                    (val) => row.PmDestination = val);

                // PM vehicle toggles
                for (int v = 0; v < Vehicles.Length; v++)
                {
                    int vi = v;
                    bool isAssigned = row.PmVehicle == Vehicles[v].Key;
                    AddVehicleToggle(grid, gridRow, ColPmVehicleStart + v, isAssigned, altBg,
                        () => ToggleVehicle(row, "PM", Vehicles[vi].Key));
                }

                // Bottom border for each row
                var borderLine = new Border
                {
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")!),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    IsHitTestVisible = false
                };
                Grid.SetRow(borderLine, gridRow);
                Grid.SetColumn(borderLine, 0);
                Grid.SetColumnSpan(borderLine, TotalColumns);
                grid.Children.Add(borderLine);
            }
        }

        private void AddSectionHeader(Grid grid, string text, int row, int col, int colSpan, int rowSpan,
            string fgHex, string bgHex)
        {
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)!),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")!),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(10, 8, 10, 8)
            };
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex)!),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            border.Child = tb;
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            Grid.SetColumnSpan(border, colSpan);
            if (rowSpan > 1) Grid.SetRowSpan(border, rowSpan);
            grid.Children.Add(border);
        }

        private void AddColumnHeader(Grid grid, string text, int row, int col, string fgHex, string bgHex)
        {
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)!),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")!),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(6, 6, 6, 6)
            };
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex)!),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            border.Child = tb;
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            grid.Children.Add(border);
        }

        private void AddEditableCell(Grid grid, int row, int col, string value, string bgHex,
            string fgHex, bool isBold, Action<string> onChanged)
        {
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)!),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")!),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(8, 4, 8, 4)
            };

            var tb = new TextBox
            {
                Text = value ?? "",
                FontSize = 13,
                FontWeight = isBold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex)!),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0)
            };
            tb.LostFocus += (_, _) => onChanged(tb.Text);
            border.Child = tb;

            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            grid.Children.Add(border);
        }

        private void AddVehicleToggle(Grid grid, int row, int col, bool isAssigned, string bgHex,
            Action onToggle)
        {
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)!),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")!),
                BorderThickness = new Thickness(0, 0, 1, 0)
            };

            var btn = new Button
            {
                Content = isAssigned ? "●" : "",   // filled circle or empty
                Style = (Style)FindResource("VehicleToggle"),
                Foreground = isAssigned
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")!)
                    : Brushes.Transparent,
                Tag = isAssigned
            };
            btn.Click += (_, _) =>
            {
                onToggle();
                BuildGrid(); // rebuild to reflect toggle state
            };
            border.Child = btn;

            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            grid.Children.Add(border);
        }

        private void ToggleVehicle(MaterialLogisticsRow row, string period, string vehicleKey)
        {
            if (period == "AM")
                row.AmVehicle = row.AmVehicle == vehicleKey ? "" : vehicleKey;
            else
                row.PmVehicle = row.PmVehicle == vehicleKey ? "" : vehicleKey;
        }

        // ══════════════════════════════════════════════════════════════
        //  Add / Remove Rows
        // ══════════════════════════════════════════════════════════════

        private void BtnAddRow_Click(object sender, RoutedEventArgs e)
        {
            string dateKey = _selectedDate.ToString("yyyy-MM-dd");
            int nextOrder = _rows.Count > 0 ? _rows.Max(r => r.OrderNo) + 1 : 1;
            _rows.Add(new MaterialLogisticsRow
            {
                BoardDate = dateKey,
                OrderNo = nextOrder
            });
            BuildGrid();
        }

        private void BtnRemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if (_rows.Count == 0) return;

            var result = MessageBox.Show(
                "마지막 행을 삭제하시겠습니까?",
                "인원 삭제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var lastRow = _rows.Last();
                if (lastRow.Id > 0) StatusBoardRepository.DeleteMaterialLogistics(lastRow.Id);
                _rows.Remove(lastRow);
                BuildGrid();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Save
        // ══════════════════════════════════════════════════════════════

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dateKey = _selectedDate.ToString("yyyy-MM-dd");

                // Sync all TextBox values from the grid into the model objects
                SyncGridToModel();

                // Read special notes memo (stored as a row with PersonName = "__MEMO__" or separate table)
                string memo = TxtSpecialNotes.Text?.Trim() ?? "";

                foreach (var row in _rows)
                {
                    row.BoardDate = dateKey;
                    row.UpdatedAt = DateTime.Now;
                }

                // 메모를 특수 행으로 저장
                var existing = StatusBoardRepository.GetAllMaterialLogistics(dateKey);
                foreach (var ex in existing)
                    StatusBoardRepository.DeleteMaterialLogistics(ex.Id);

                int order = 1;
                foreach (var row in _rows)
                {
                    row.OrderNo = order++;
                    row.Id = 0;
                    StatusBoardRepository.InsertMaterialLogistics(row);
                }

                if (!string.IsNullOrWhiteSpace(memo))
                {
                    StatusBoardRepository.InsertMaterialLogistics(new MaterialLogisticsRow
                    {
                        BoardDate = dateKey,
                        PersonName = "__MEMO__",
                        Memo = memo,
                        OrderNo = 9999,
                        UpdatedAt = DateTime.Now
                    });
                }

                MessageBox.Show("저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 중 오류:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Walk through TextBox children of the grid and sync their text values
        /// back into the corresponding model properties.
        /// </summary>
        private void SyncGridToModel()
        {
            // The grid is rebuilt every time, but TextBox LostFocus handlers
            // update the model. Force focus away to trigger any pending changes.
            var focused = Keyboard.FocusedElement as TextBox;
            if (focused != null)
            {
                // Move focus to the grid to trigger LostFocus on the active TextBox
                ScheduleGrid.Focus();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Print / Excel Export (A4 Landscape)
        // ══════════════════════════════════════════════════════════════

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            SyncGridToModel();

            var dlg = new SaveFileDialog
            {
                Title = "자재 & 물류 일정 현황 엑셀 내보내기 (A4 가로)",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"자재물류일정_{_selectedDate:yyyyMMdd}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("자재물류일정");

                // ── A4 landscape print setup ──
                ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
                ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                ws.PageSetup.Margins.Top = 0.2;
                ws.PageSetup.Margins.Bottom = 0.2;
                ws.PageSetup.Margins.Left = 0.2;
                ws.PageSetup.Margins.Right = 0.2;
                ws.PageSetup.Margins.Header = 0;
                ws.PageSetup.Margins.Footer = 0;
                ws.PageSetup.CenterHorizontally = true;
                ws.PageSetup.FitToPages(1, 1);

                int totalCols = TotalColumns;

                // ── Row 1: Title ──
                var culture = new CultureInfo("ko-KR");
                string[] dayNames = { "일요일", "월요일", "화요일", "수요일", "목요일", "금요일", "토요일" };
                string dayName = dayNames[(int)_selectedDate.DayOfWeek];
                string titleText = $"천안사업장 자재 & 물류 일정 현황  ({_selectedDate:yyyy}년 {_selectedDate.Month}월 {_selectedDate.Day}일 {dayName})";

                var titleCell = ws.Cell(1, 1);
                titleCell.Value = titleText;
                ws.Range(1, 1, 1, totalCols).Merge();
                titleCell.Style.Font.Bold = true;
                titleCell.Style.Font.FontSize = 16;
                titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Row(1).Height = 30;

                // ── Row 2: Section headers ──
                // Person column
                var personHeader = ws.Cell(2, 1);
                personHeader.Value = "담당자";
                personHeader.Style.Font.Bold = true;
                personHeader.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                personHeader.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Range(2, 1, 3, 1).Merge();

                // AM header
                ws.Cell(2, 2).Value = "오전 (AM)";
                ws.Range(2, 2, 2, 2 + Vehicles.Length).Merge();
                ws.Cell(2, 2).Style.Font.Bold = true;
                ws.Cell(2, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#DBEAFE");
                ws.Cell(2, 2).Style.Font.FontColor = XLColor.FromHtml("#1E40AF");
                ws.Cell(2, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // PM header
                int pmStart = 2 + Vehicles.Length + 1;
                ws.Cell(2, pmStart).Value = "오후 (PM)";
                ws.Range(2, pmStart, 2, pmStart + Vehicles.Length).Merge();
                ws.Cell(2, pmStart).Style.Font.Bold = true;
                ws.Cell(2, pmStart).Style.Fill.BackgroundColor = XLColor.FromHtml("#FED7AA");
                ws.Cell(2, pmStart).Style.Font.FontColor = XLColor.FromHtml("#9A3412");
                ws.Cell(2, pmStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // ── Row 3: Sub-headers ──
                // AM dest
                ws.Cell(3, 2).Value = "목적지 & 근무";
                ws.Cell(3, 2).Style.Font.Bold = true;
                ws.Cell(3, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF");
                ws.Cell(3, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                for (int v = 0; v < Vehicles.Length; v++)
                {
                    var c = ws.Cell(3, 3 + v);
                    c.Value = $"{Vehicles[v].Label.Replace("\n", " ")}";
                    c.Style.Font.Bold = true;
                    c.Style.Font.FontSize = 9;
                    c.Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF");
                    c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    c.Style.Alignment.WrapText = true;
                }

                // PM dest
                ws.Cell(3, pmStart).Value = "목적지 & 근무";
                ws.Cell(3, pmStart).Style.Font.Bold = true;
                ws.Cell(3, pmStart).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF7ED");
                ws.Cell(3, pmStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                for (int v = 0; v < Vehicles.Length; v++)
                {
                    var c = ws.Cell(3, pmStart + 1 + v);
                    c.Value = $"{Vehicles[v].Label.Replace("\n", " ")}";
                    c.Style.Font.Bold = true;
                    c.Style.Font.FontSize = 9;
                    c.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF7ED");
                    c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    c.Style.Alignment.WrapText = true;
                }

                ws.Row(2).Height = 22;
                ws.Row(3).Height = 30;

                // ── Data rows ──
                int exRow = 4;
                foreach (var row in _rows)
                {
                    ws.Cell(exRow, 1).Value = row.PersonName;
                    ws.Cell(exRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(exRow, 1).Style.Font.Bold = true;

                    ws.Cell(exRow, 2).Value = row.AmDestination;

                    for (int v = 0; v < Vehicles.Length; v++)
                    {
                        ws.Cell(exRow, 3 + v).Value = row.AmVehicle == Vehicles[v].Key ? "●" : "";
                        ws.Cell(exRow, 3 + v).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        ws.Cell(exRow, 3 + v).Style.Font.FontSize = 14;
                    }

                    ws.Cell(exRow, pmStart).Value = row.PmDestination;

                    for (int v = 0; v < Vehicles.Length; v++)
                    {
                        ws.Cell(exRow, pmStart + 1 + v).Value = row.PmVehicle == Vehicles[v].Key ? "●" : "";
                        ws.Cell(exRow, pmStart + 1 + v).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        ws.Cell(exRow, pmStart + 1 + v).Style.Font.FontSize = 14;
                    }

                    ws.Row(exRow).Height = 22;
                    exRow++;
                }

                // ── Special notes row ──
                string notes = TxtSpecialNotes.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(notes))
                {
                    exRow++;
                    ws.Cell(exRow, 1).Value = "특이사항";
                    ws.Cell(exRow, 1).Style.Font.Bold = true;
                    ws.Range(exRow, 2, exRow, totalCols).Merge();
                    ws.Cell(exRow, 2).Value = notes;
                    ws.Cell(exRow, 2).Style.Alignment.WrapText = true;
                    ws.Row(exRow).Height = 40;
                }

                int lastRow = exRow;

                // ── Borders ──
                if (lastRow >= 2)
                {
                    var table = ws.Range(2, 1, lastRow, totalCols);
                    table.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    table.Style.Border.OutsideBorderColor = XLColor.FromHtml("#475569");
                    table.Style.Border.InsideBorderColor = XLColor.FromHtml("#CBD5E1");
                }

                // ── Column widths ──
                ws.Column(1).Width = 16;  // 담당자
                ws.Column(2).Width = 24;  // AM 목적지
                for (int v = 0; v < Vehicles.Length; v++)
                    ws.Column(3 + v).Width = 10; // AM vehicles
                ws.Column(pmStart).Width = 24;    // PM 목적지
                for (int v = 0; v < Vehicles.Length; v++)
                    ws.Column(pmStart + 1 + v).Width = 10; // PM vehicles

                ws.Range(2, 1, lastRow, totalCols).Style.Font.FontSize = 11;

                wb.SaveAs(dlg.FileName);
                MessageBox.Show("엑셀 파일이 저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 내보내기 중 오류:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
