using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CleanPotal.StatusBoard.Models;
using CleanPotal.StatusBoard.Repositories;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace CleanPotal.StatusBoard.Views
{
    public partial class DongtanLogisticsView : UserControl
    {
        // ── Dispatch collections (split by group) ──
        private readonly ObservableCollection<DongtanDispatchVM> _gihwaRows = new();
        private readonly ObservableCollection<DongtanDispatchVM> _pyeongtaekRows = new();

        // ── Quantity grids: [direction][timeSlot] => region => entry ──
        // Regions are fixed: 화성, 기흥, 평택, 부적합, 기타
        private static readonly string[] Regions = { "화성", "기흥", "평택", "부적합", "기타" };
        private static readonly string[] QtyCols = { "OUTER", "INNER", "BOAT", "ACC", "ETC" };

        // TextBox references for quantity grids: key = "direction|timeSlot|region|col"
        private readonly Dictionary<string, TextBox> _qtyBoxes = new();
        // TextBlock references for sum row: key = "direction|timeSlot|col"
        private readonly Dictionary<string, TextBlock> _sumBlocks = new();
        // EtcMemo TextBox references: key = "direction|timeSlot|region"
        private readonly Dictionary<string, TextBox> _memoBoxes = new();

        private bool _loading;

        public DongtanLogisticsView()
        {
            InitializeComponent();
            IcGihwa.ItemsSource = _gihwaRows;
            IcPyeongtaek.ItemsSource = _pyeongtaekRows;

            BuildQuantityGrid(GridAmIn, "반입", "오전");
            BuildQuantityGrid(GridPmIn, "반입", "오후");
            BuildQuantityGrid(GridAmOut, "반출", "오전");
            BuildQuantityGrid(GridPmOut, "반출", "오후");

            DpBoardDate.SelectedDate = DateTime.Today;
            Loaded += (_, _) => LoadData();
        }

        // ═══════════════════════════════════════════════════════════
        // Date Navigation
        // ═══════════════════════════════════════════════════════════
        private string BoardDateStr => (DpBoardDate.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");

        private void DpBoardDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            LoadData();
        }

        private void BtnPrevDay_Click(object sender, RoutedEventArgs e)
        {
            DpBoardDate.SelectedDate = (DpBoardDate.SelectedDate ?? DateTime.Today).AddDays(-1);
        }

        private void BtnNextDay_Click(object sender, RoutedEventArgs e)
        {
            DpBoardDate.SelectedDate = (DpBoardDate.SelectedDate ?? DateTime.Today).AddDays(1);
        }

        private void BtnToday_Click(object sender, RoutedEventArgs e)
        {
            DpBoardDate.SelectedDate = DateTime.Today;
        }

        // ═══════════════════════════════════════════════════════════
        // Load / Save
        // ═══════════════════════════════════════════════════════════
        private void LoadData()
        {
            _loading = true;
            try
            {
                var date = BoardDateStr;
                TxtDate.Text = (DpBoardDate.SelectedDate ?? DateTime.Today).ToString("yyyy년 M월 d일 (ddd)");

                // ── Dispatch ──
                var dispatches = StatusBoardRepository.GetAllDongtanDispatch(date);
                _gihwaRows.Clear();
                _pyeongtaekRows.Clear();

                foreach (var d in dispatches)
                {
                    var vm = new DongtanDispatchVM(d);
                    if (d.RouteGroup == "평택")
                        _pyeongtaekRows.Add(vm);
                    else
                        _gihwaRows.Add(vm);
                }

                // If empty, seed default rows
                if (_gihwaRows.Count == 0)
                {
                    for (int i = 0; i < 4; i++)
                        _gihwaRows.Add(new DongtanDispatchVM(new DongtanDispatch
                        {
                            BoardDate = date, RouteGroup = "기흥/화성", OrderNo = i
                        }));
                }
                if (_pyeongtaekRows.Count == 0)
                {
                    for (int i = 0; i < 4; i++)
                        _pyeongtaekRows.Add(new DongtanDispatchVM(new DongtanDispatch
                        {
                            BoardDate = date, RouteGroup = "평택", OrderNo = i
                        }));
                }

                // ── Quantities ──
                var quantities = StatusBoardRepository.GetAllDongtanQuantity(date);
                var qtyLookup = quantities.ToDictionary(
                    q => $"{q.Direction}|{q.TimeSlot}|{q.Region}");

                foreach (var direction in new[] { "반입", "반출" })
                foreach (var slot in new[] { "오전", "오후" })
                foreach (var region in Regions)
                {
                    var key = $"{direction}|{slot}|{region}";
                    qtyLookup.TryGetValue(key, out var q);

                    SetQtyBox(direction, slot, region, "OUTER", q?.OuterQty ?? 0);
                    SetQtyBox(direction, slot, region, "INNER", q?.InnerQty ?? 0);
                    SetQtyBox(direction, slot, region, "BOAT", q?.BoatQty ?? 0);
                    SetQtyBox(direction, slot, region, "ACC", q?.AccQty ?? 0);
                    SetQtyBox(direction, slot, region, "ETC", q?.EtcQty ?? 0);

                    var memoKey = $"{direction}|{slot}|{region}";
                    if (_memoBoxes.TryGetValue(memoKey, out var memoBox))
                        memoBox.Text = q?.EtcMemo ?? "";
                }

                UpdateAllSums();
            }
            finally { _loading = false; }
        }

        private void SetQtyBox(string direction, string slot, string region, string col, int value)
        {
            var key = $"{direction}|{slot}|{region}|{col}";
            if (_qtyBoxes.TryGetValue(key, out var tb))
                tb.Text = value == 0 ? "" : value.ToString();
        }

        private int GetQtyBox(string direction, string slot, string region, string col)
        {
            var key = $"{direction}|{slot}|{region}|{col}";
            if (_qtyBoxes.TryGetValue(key, out var tb) && int.TryParse(tb.Text, out int v))
                return v;
            return 0;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var date = BoardDateStr;

                // ── Save Dispatch ──
                // Delete existing for this date, then re-insert
                var existing = StatusBoardRepository.GetAllDongtanDispatch(date);
                foreach (var ex in existing)
                    StatusBoardRepository.DeleteDongtanDispatch(ex.Id);

                int order = 0;
                foreach (var vm in _gihwaRows)
                {
                    var m = vm.Model;
                    m.BoardDate = date;
                    m.RouteGroup = "기흥/화성";
                    m.OrderNo = order++;
                    m.UpdatedAt = DateTime.Now;
                    m.Id = 0;
                    StatusBoardRepository.InsertDongtanDispatch(m);
                }
                foreach (var vm in _pyeongtaekRows)
                {
                    var m = vm.Model;
                    m.BoardDate = date;
                    m.RouteGroup = "평택";
                    m.OrderNo = order++;
                    m.UpdatedAt = DateTime.Now;
                    m.Id = 0;
                    StatusBoardRepository.InsertDongtanDispatch(m);
                }

                // ── Save Quantities ──
                var existingQty = StatusBoardRepository.GetAllDongtanQuantity(date);
                foreach (var ex in existingQty)
                    StatusBoardRepository.DeleteDongtanQuantity(ex.Id);

                foreach (var direction in new[] { "반입", "반출" })
                foreach (var slot in new[] { "오전", "오후" })
                foreach (var region in Regions)
                {
                    var memoKey = $"{direction}|{slot}|{region}";
                    _memoBoxes.TryGetValue(memoKey, out var memoBox);

                    var q = new DongtanQuantity
                    {
                        BoardDate = date,
                        TimeSlot = slot,
                        Direction = direction,
                        Region = region,
                        OuterQty = GetQtyBox(direction, slot, region, "OUTER"),
                        InnerQty = GetQtyBox(direction, slot, region, "INNER"),
                        BoatQty = GetQtyBox(direction, slot, region, "BOAT"),
                        AccQty = GetQtyBox(direction, slot, region, "ACC"),
                        EtcQty = GetQtyBox(direction, slot, region, "ETC"),
                        EtcMemo = memoBox?.Text ?? "",
                        UpdatedAt = DateTime.Now
                    };
                    StatusBoardRepository.InsertDongtanQuantity(q);
                }

                MessageBox.Show("저장되었습니다.", "저장", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 중 오류가 발생했습니다.\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Dispatch Row Add / Remove
        // ═══════════════════════════════════════════════════════════
        private void BtnAddGihwa_Click(object sender, RoutedEventArgs e)
        {
            _gihwaRows.Add(new DongtanDispatchVM(new DongtanDispatch
            {
                BoardDate = BoardDateStr, RouteGroup = "기흥/화성", OrderNo = _gihwaRows.Count
            }));
        }

        private void BtnAddPyeongtaek_Click(object sender, RoutedEventArgs e)
        {
            _pyeongtaekRows.Add(new DongtanDispatchVM(new DongtanDispatch
            {
                BoardDate = BoardDateStr, RouteGroup = "평택", OrderNo = _pyeongtaekRows.Count
            }));
        }

        private void BtnRemoveDriver_Click(object sender, RoutedEventArgs e)
        {
            // Remove last row from whichever group is larger, or prompt
            if (_gihwaRows.Count > 0 || _pyeongtaekRows.Count > 0)
            {
                var result = MessageBox.Show(
                    "기흥/화성 마지막 행을 삭제하려면 [예],\n평택 마지막 행을 삭제하려면 [아니오]를 누르세요.",
                    "행 삭제", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes && _gihwaRows.Count > 0)
                    _gihwaRows.RemoveAt(_gihwaRows.Count - 1);
                else if (result == MessageBoxResult.No && _pyeongtaekRows.Count > 0)
                    _pyeongtaekRows.RemoveAt(_pyeongtaekRows.Count - 1);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Build Quantity Grid (code-behind)
        // ═══════════════════════════════════════════════════════════
        private void BuildQuantityGrid(Grid grid, string direction, string timeSlot)
        {
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            grid.Children.Clear();

            // Columns: 구분 | OUTER | INNER | BOAT | ACC | ETC | 메모
            int colCount = 7;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // 구분
            for (int c = 0; c < 5; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // 메모

            // Rows: header + 5 regions + sum
            int rowCount = 1 + Regions.Length + 1;
            for (int r = 0; r < rowCount; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Header row ──
            string[] headers = { "구분", "OUTER", "INNER", "BOAT", "ACC", "ETC", "메모" };
            for (int c = 0; c < headers.Length; c++)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                    BorderThickness = new Thickness(0, 0, c < headers.Length - 1 ? 1 : 0, 1),
                    Padding = new Thickness(6)
                };
                var txt = new TextBlock
                {
                    Text = headers[c],
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                border.Child = txt;
                Grid.SetRow(border, 0);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }

            // ── Data rows ──
            for (int ri = 0; ri < Regions.Length; ri++)
            {
                int row = ri + 1;
                var region = Regions[ri];

                // 구분 label
                var labelBorder = new Border
                {
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(6, 4, 6, 4),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"))
                };
                var label = new TextBlock
                {
                    Text = region,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                labelBorder.Child = label;
                Grid.SetRow(labelBorder, row);
                Grid.SetColumn(labelBorder, 0);
                grid.Children.Add(labelBorder);

                // Quantity cells (OUTER..ETC)
                for (int ci = 0; ci < 5; ci++)
                {
                    var col = QtyCols[ci];
                    var cellBorder = new Border
                    {
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding = new Thickness(2)
                    };
                    var tb = new TextBox
                    {
                        BorderThickness = new Thickness(0),
                        Background = Brushes.Transparent,
                        FontSize = 13,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        Padding = new Thickness(4),
                        MinWidth = 40
                    };
                    // Focus highlight
                    tb.GotFocus += (s, _) => ((TextBox)s).Background =
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
                    tb.LostFocus += (s, _) => ((TextBox)s).Background = Brushes.Transparent;
                    tb.TextChanged += QtyTextChanged;
                    tb.Tag = $"{direction}|{timeSlot}|{col}";

                    var key = $"{direction}|{timeSlot}|{region}|{col}";
                    _qtyBoxes[key] = tb;

                    cellBorder.Child = tb;
                    Grid.SetRow(cellBorder, row);
                    Grid.SetColumn(cellBorder, ci + 1);
                    grid.Children.Add(cellBorder);
                }

                // Memo cell
                var memoBorder = new Border
                {
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(2)
                };
                var memoTb = new TextBox
                {
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(4),
                    TextWrapping = TextWrapping.Wrap
                };
                memoTb.GotFocus += (s, _) => ((TextBox)s).Background =
                    new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
                memoTb.LostFocus += (s, _) => ((TextBox)s).Background = Brushes.Transparent;

                _memoBoxes[$"{direction}|{timeSlot}|{region}"] = memoTb;
                memoBorder.Child = memoTb;
                Grid.SetRow(memoBorder, row);
                Grid.SetColumn(memoBorder, 6);
                grid.Children.Add(memoBorder);
            }

            // ── Sum row ──
            int sumRow = Regions.Length + 1;
            var sumLabelBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(6, 4, 6, 4)
            };
            var sumLabel = new TextBlock
            {
                Text = "합계",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            sumLabelBorder.Child = sumLabel;
            Grid.SetRow(sumLabelBorder, sumRow);
            Grid.SetColumn(sumLabelBorder, 0);
            grid.Children.Add(sumLabelBorder);

            for (int ci = 0; ci < 5; ci++)
            {
                var col = QtyCols[ci];
                var cellBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                    BorderThickness = new Thickness(0, 0, 1, 0),
                    Padding = new Thickness(6, 4, 6, 4)
                };
                var sumText = new TextBlock
                {
                    Text = "0",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                _sumBlocks[$"{direction}|{timeSlot}|{col}"] = sumText;
                cellBorder.Child = sumText;
                Grid.SetRow(cellBorder, sumRow);
                Grid.SetColumn(cellBorder, ci + 1);
                grid.Children.Add(cellBorder);
            }

            // Empty memo cell in sum row
            var emptyBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")),
            };
            Grid.SetRow(emptyBorder, sumRow);
            Grid.SetColumn(emptyBorder, 6);
            grid.Children.Add(emptyBorder);
        }

        // ═══════════════════════════════════════════════════════════
        // Auto-sum
        // ═══════════════════════════════════════════════════════════
        private void QtyTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            if (sender is TextBox tb && tb.Tag is string tag)
            {
                // tag = "direction|timeSlot|col"
                var parts = tag.Split('|');
                if (parts.Length == 3)
                    UpdateSum(parts[0], parts[1], parts[2]);
            }
        }

        private void UpdateSum(string direction, string timeSlot, string col)
        {
            int total = 0;
            foreach (var region in Regions)
                total += GetQtyBox(direction, timeSlot, region, col);

            var sumKey = $"{direction}|{timeSlot}|{col}";
            if (_sumBlocks.TryGetValue(sumKey, out var block))
                block.Text = total.ToString();
        }

        private void UpdateAllSums()
        {
            foreach (var direction in new[] { "반입", "반출" })
            foreach (var slot in new[] { "오전", "오후" })
            foreach (var col in QtyCols)
                UpdateSum(direction, slot, col);
        }

        // ═══════════════════════════════════════════════════════════
        // Print (ClosedXML Excel Export)
        // ═══════════════════════════════════════════════════════════
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "동탄 물류 현황판 내보내기",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"동탄물류현황판_{BoardDateStr}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("동탄 물류 현황판");
                ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
                ws.PageSetup.FitToPages(1, 1);

                int row = 1;

                // Title
                ws.Cell(row, 1).Value = $"동탄 물류 현황판 - {BoardDateStr}";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 16;
                ws.Range(row, 1, row, 10).Merge();
                row += 2;

                // ── 배차표: 기흥/화성 ──
                row = WriteDispatchToExcel(ws, row, "기흥 / 화성", _gihwaRows);
                row++;

                // ── 배차표: 평택 ──
                row = WriteDispatchToExcel(ws, row, "평택", _pyeongtaekRows);
                row += 2;

                // ── 반입 수량 ──
                row = WriteQuantitySection(ws, row, "반입");
                row++;

                // ── 반출 물량 ──
                row = WriteQuantitySection(ws, row, "반출");

                ws.Columns().AdjustToContents();
                wb.SaveAs(dlg.FileName);

                MessageBox.Show("엑셀 파일이 저장되었습니다.", "인쇄", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 저장 중 오류가 발생했습니다.\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private int WriteDispatchToExcel(IXLWorksheet ws, int row,
            string groupName, ObservableCollection<DongtanDispatchVM> rows)
        {
            ws.Cell(row, 1).Value = $"배차표 - {groupName}";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 12;
            ws.Range(row, 1, row, 4).Merge();
            row++;

            // Header
            string[] headers = { "성명", "차량", "오전", "오후" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            row++;

            foreach (var vm in rows)
            {
                ws.Cell(row, 1).Value = vm.DriverName;
                ws.Cell(row, 2).Value = vm.VehicleInfo;
                ws.Cell(row, 3).Value = vm.AmRoute;
                ws.Cell(row, 4).Value = vm.PmRoute;

                for (int c = 1; c <= 4; c++)
                {
                    ws.Cell(row, c).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
                    ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                row++;
            }
            return row;
        }

        private int WriteQuantitySection(IXLWorksheet ws, int row, string direction)
        {
            foreach (var slot in new[] { "오전", "오후" })
            {
                ws.Cell(row, 1).Value = $"{slot} {direction} 수량";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 11;
                ws.Range(row, 1, row, 7).Merge();
                row++;

                // Header
                string[] headers = { "구분", "OUTER", "INNER", "BOAT", "ACC", "ETC", "메모" };
                for (int c = 0; c < headers.Length; c++)
                {
                    var cell = ws.Cell(row, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                row++;

                foreach (var region in Regions)
                {
                    ws.Cell(row, 1).Value = region;
                    ws.Cell(row, 1).Style.Font.Bold = true;

                    for (int ci = 0; ci < QtyCols.Length; ci++)
                    {
                        int val = GetQtyBox(direction, slot, region, QtyCols[ci]);
                        if (val != 0)
                            ws.Cell(row, ci + 2).Value = val;
                        ws.Cell(row, ci + 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }

                    _memoBoxes.TryGetValue($"{direction}|{slot}|{region}", out var memoBox);
                    ws.Cell(row, 7).Value = memoBox?.Text ?? "";

                    for (int c = 1; c <= 7; c++)
                        ws.Cell(row, c).Style.Border.BottomBorder = XLBorderStyleValues.Hair;

                    row++;
                }

                // Sum row
                ws.Cell(row, 1).Value = "합계";
                ws.Cell(row, 1).Style.Font.Bold = true;
                for (int ci = 0; ci < QtyCols.Length; ci++)
                {
                    int total = 0;
                    foreach (var region in Regions)
                        total += GetQtyBox(direction, slot, region, QtyCols[ci]);
                    if (total != 0)
                        ws.Cell(row, ci + 2).Value = total;
                    ws.Cell(row, ci + 2).Style.Font.Bold = true;
                    ws.Cell(row, ci + 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                for (int c = 1; c <= 7; c++)
                {
                    ws.Cell(row, c).Style.Border.TopBorder = XLBorderStyleValues.Thin;
                    ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                }
                row += 2;
            }
            return row;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ViewModel wrapper for dispatch rows (adds VehicleBg)
    // ═══════════════════════════════════════════════════════════
    public class DongtanDispatchVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnProp(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public DongtanDispatch Model { get; }

        public DongtanDispatchVM(DongtanDispatch model)
        {
            Model = model;
            Model.PropertyChanged += (_, e) =>
            {
                OnProp(e.PropertyName!);
                if (e.PropertyName == nameof(VehicleInfo))
                    OnProp(nameof(VehicleBg));
            };
        }

        public string DriverName
        {
            get => Model.DriverName;
            set { Model.DriverName = value; OnProp(nameof(DriverName)); }
        }

        public string VehicleInfo
        {
            get => Model.VehicleInfo;
            set { Model.VehicleInfo = value; OnProp(nameof(VehicleInfo)); OnProp(nameof(VehicleBg)); }
        }

        public string AmRoute
        {
            get => Model.AmRoute;
            set { Model.AmRoute = value; OnProp(nameof(AmRoute)); }
        }

        public string PmRoute
        {
            get => Model.PmRoute;
            set { Model.PmRoute = value; OnProp(nameof(PmRoute)); }
        }

        /// <summary>
        /// Vehicle cell background based on tonnage keyword:
        /// 1T -> yellow (#FEF3C7), 2.5T -> green (#DCFCE7), 3.5T -> blue (#DBEAFE)
        /// </summary>
        public Brush VehicleBg
        {
            get
            {
                var v = VehicleInfo ?? "";
                if (v.Contains("3.5T"))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"));
                if (v.Contains("2.5T"))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7"));
                if (v.Contains("1T"))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
                return Brushes.Transparent;
            }
        }
    }
}
