using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CleanPotal;
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

                // 특이사항: 오전 = AmDestination, 오후 = PmDestination (구버전 호환: Memo는 오전으로)
                TxtAmNotes.Text = memoRow?.AmDestination ?? memoRow?.Memo ?? "";
                TxtPmNotes.Text = memoRow?.PmDestination ?? "";

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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
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
            // 담당자는 row 0만 차지 (이름 헤더와 겹쳐 잘리던 문제 수정 — 기존 rowSpan=2 제거)
            AddSectionHeader(grid, "담당자", 0, 0, 1, 1, "#0F172A", "#F1F5F9");

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

                // AM destination (editable + 배차 불러오기)
                AddDestinationCell(grid, gridRow, ColAmDest, row.AmDestination, altBg, "#334155",
                    (val) => row.AmDestination = val);

                // AM vehicle toggles
                for (int v = 0; v < Vehicles.Length; v++)
                {
                    int vi = v; // capture
                    bool isAssigned = row.AmVehicle == Vehicles[v].Key;
                    AddVehicleToggle(grid, gridRow, ColAmVehicleStart + v, isAssigned, altBg,
                        () => ToggleVehicle(row, "AM", Vehicles[vi].Key));
                }

                // PM destination (editable + 배차 불러오기)
                AddDestinationCell(grid, gridRow, ColPmDest, row.PmDestination, altBg, "#334155",
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
                FontSize = 16,
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
                FontSize = 13,
                FontWeight = FontWeights.Bold,
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
                FontSize = 15,
                FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
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

        // 목적지 & 근무 셀: 직접 입력 TextBox + '배차 불러오기' 드롭다운(▾)
        private void AddDestinationCell(Grid grid, int row, int col, string value, string bgHex,
            string fgHex, Action<string> onChanged)
        {
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)!),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")!),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(8, 4, 4, 4)
            };

            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tb = new TextBox
            {
                Text = value ?? "",
                FontSize = 15,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex)!),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                Padding = new Thickness(0)
            };
            tb.LostFocus += (_, _) => onChanged(tb.Text);
            Grid.SetColumn(tb, 0);
            inner.Children.Add(tb);

            var pickBtn = new Button
            {
                Content = "▾",
                FontSize = 12,
                Width = 22,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")!),
                Cursor = Cursors.Hand,
                ToolTip = "배차 이력에서 불러오기",
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0)
            };
            pickBtn.Click += (_, _) => ShowDispatchPicker(pickBtn, tb, onChanged);
            Grid.SetColumn(pickBtn, 1);
            inner.Children.Add(pickBtn);

            border.Child = inner;
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            grid.Children.Add(border);
        }

        // 선택한 날짜의 배차 이력을 팝업으로 띄우고, 선택 시 목적지&근무 칸에 채운다.
        private void ShowDispatchPicker(UIElement anchor, TextBox target, Action<string> onChanged)
        {
            List<DispatchItemModel> records;
            try
            {
                records = DatabaseHelper.GetDispatchModelsByDate(_selectedDate)
                                        .Where(r => !string.IsNullOrWhiteSpace(r.VendorName))
                                        .ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"배차 이력 조회 중 오류:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (records.Count == 0)
            {
                MessageBox.Show(
                    $"{_selectedDate:yyyy-MM-dd} 날짜의 배차 이력이 없습니다.\n\n현장 인수인계 > 배차 이력에서 먼저 배차를 작성해 주세요.",
                    "배차 불러오기", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sp = new StackPanel { Margin = new Thickness(0) };
            sp.Children.Add(new TextBlock
            {
                Text = $"{_selectedDate:M월 d일} 배차 ({records.Count}건) — 선택하면 입력됩니다",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")!),
                Margin = new Thickness(10, 8, 10, 6)
            });

            var popup = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };

            foreach (var r in records)
            {
                string fill = BuildDestinationText(r);
                var itemBtn = new Button
                {
                    Content = BuildDispatchItemContent(r),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9")!),
                    Padding = new Thickness(10, 7, 10, 7),
                    Cursor = Cursors.Hand
                };
                itemBtn.Click += (_, _) =>
                {
                    target.Text = fill;
                    onChanged(fill);
                    popup.IsOpen = false;
                };
                sp.Children.Add(itemBtn);
            }

            var scroll = new ScrollViewer
            {
                Content = sp,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var listBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1")!),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                MinWidth = 360,
                MaxWidth = 560,
                Child = scroll,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString("#94A3B8")!,
                    BlurRadius = 12, ShadowDepth = 2, Opacity = 0.4
                }
            };
            popup.Child = listBorder;
            popup.IsOpen = true;
        }

        // 팝업 항목 표시: 업체명(굵게) + 주소 + 납품 미리보기
        private static UIElement BuildDispatchItemContent(DispatchItemModel r)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = (r.VendorName ?? "").Trim(),
                FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")!)
            });
            string addr = (r.FullAddress ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(addr))
                panel.Children.Add(new TextBlock
                {
                    Text = "📍 " + addr,
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569")!),
                    TextWrapping = TextWrapping.Wrap
                });
            string deliv = (r.IncomingDetails ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (!string.IsNullOrWhiteSpace(deliv) && deliv != "-")
                panel.Children.Add(new TextBlock
                {
                    Text = "📦 " + (deliv.Length > 60 ? deliv.Substring(0, 60) + "…" : deliv),
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")!),
                    TextWrapping = TextWrapping.Wrap
                });
            return panel;
        }

        // 목적지&근무 칸에 채울 텍스트: 업체명 / 주소
        private static string BuildDestinationText(DispatchItemModel r)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.VendorName)) parts.Add(r.VendorName.Trim());
            if (!string.IsNullOrWhiteSpace(r.FullAddress)) parts.Add(r.FullAddress.Trim());
            return string.Join(" / ", parts);
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

                // 특이사항 오전/오후
                string amMemo = TxtAmNotes.Text?.Trim() ?? "";
                string pmMemo = TxtPmNotes.Text?.Trim() ?? "";

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

                if (!string.IsNullOrWhiteSpace(amMemo) || !string.IsNullOrWhiteSpace(pmMemo))
                {
                    StatusBoardRepository.InsertMaterialLogistics(new MaterialLogisticsRow
                    {
                        BoardDate = dateKey,
                        PersonName = "__MEMO__",
                        AmDestination = amMemo,   // 오전 특이사항
                        PmDestination = pmMemo,   // 오후 특이사항
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

                // ── Special notes rows (오전/오후 분리) ──
                string amNotes = TxtAmNotes.Text?.Trim() ?? "";
                string pmNotes = TxtPmNotes.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(amNotes) || !string.IsNullOrEmpty(pmNotes))
                {
                    exRow++;
                    // 오전 특이사항
                    ws.Cell(exRow, 1).Value = "특이사항(오전)";
                    ws.Cell(exRow, 1).Style.Font.Bold = true;
                    ws.Cell(exRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#DBEAFE");
                    ws.Range(exRow, 2, exRow, totalCols).Merge();
                    ws.Cell(exRow, 2).Value = amNotes;
                    ws.Cell(exRow, 2).Style.Alignment.WrapText = true;
                    ws.Row(exRow).Height = 40;

                    exRow++;
                    // 오후 특이사항
                    ws.Cell(exRow, 1).Value = "특이사항(오후)";
                    ws.Cell(exRow, 1).Style.Font.Bold = true;
                    ws.Cell(exRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FED7AA");
                    ws.Range(exRow, 2, exRow, totalCols).Merge();
                    ws.Cell(exRow, 2).Value = pmNotes;
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
