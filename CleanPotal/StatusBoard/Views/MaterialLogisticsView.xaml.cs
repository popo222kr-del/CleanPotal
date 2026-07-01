using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CleanPotal;
using CleanPotal.StatusBoard.Models;
using CleanPotal.StatusBoard.Repositories;
using CleanPotal.StatusBoard.Views;

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

        // 목적지&근무 고정 빠른선택(배차 유무와 무관하게 항상 표시)
        private static readonly string[] QuickOptions = { "내근", "차량검사소", "천안 ↔ 동탄" };

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

            Loaded += (_, _) => LoadData();
        }

        // ══════════════════════════════════════════════════════════════
        //  Date Navigation
        // ══════════════════════════════════════════════════════════════

        // 표 카드의 둥근 모서리: WPF Border는 자식을 라운드로 클립하지 않으므로
        // ScrollViewer에 크기에 맞춘 둥근 사각형 Clip을 씌워 헤더/셀 모서리를 둥글게 처리.
        private void TableScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                fe.Clip = new RectangleGeometry(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight), 11, 11);
        }

        private void BtnPrevDay_Click(object sender, RoutedEventArgs e)
        {
            _selectedDate = _selectedDate.AddDays(-1);
            LoadData();
        }

        private void BtnNextDay_Click(object sender, RoutedEventArgs e)
        {
            _selectedDate = _selectedDate.AddDays(1);
            LoadData();
        }

        private void BtnToday_Click(object sender, RoutedEventArgs e)
        {
            _selectedDate = DateTime.Today;
            LoadData();
        }

        // 날짜 알약 클릭 → 달력 팝업 열기
        private void BtnDatePill_Click(object sender, RoutedEventArgs e)
        {
            _isLoading = true;
            CalPicker.DisplayDate = _selectedDate;
            CalPicker.SelectedDate = _selectedDate;
            _isLoading = false;
            CalPopup.IsOpen = true;
        }

        // 달력에서 날짜 선택 → 반영 후 닫기
        private void CalPicker_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || CalPicker.SelectedDate == null) return;
            _selectedDate = CalPicker.SelectedDate.Value;
            CalPopup.IsOpen = false;
            LoadData();
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
                // 고정 인원 로스터 기준으로 행 구성 (담당자 이름 고정)
                var members = StatusBoardRepository.GetMaterialLogisticsMembers();
                var entries = StatusBoardRepository.GetAllMaterialLogistics(dateKey);
                var memoRow = entries.FirstOrDefault(r => r.PersonName == "__MEMO__");

                // 해당 날짜의 인원별 데이터(목적지·차량)를 이름으로 매칭
                var byName = entries.Where(r => r.PersonName != "__MEMO__")
                                    .GroupBy(r => r.PersonName)
                                    .ToDictionary(g => g.Key, g => g.First());

                _rows.Clear();
                int order = 1;
                foreach (var m in members)
                {
                    byName.TryGetValue(m.Name, out var d);
                    _rows.Add(new MaterialLogisticsRow
                    {
                        BoardDate = dateKey,
                        PersonName = m.Name,
                        AmDestination = d?.AmDestination ?? "",
                        AmVehicle = d?.AmVehicle ?? "",
                        PmDestination = d?.PmDestination ?? "",
                        PmVehicle = d?.PmVehicle ?? "",
                        OrderNo = order++
                    });
                }

                // 특이사항: 오전 = AmDestination, 오후 = PmDestination (구버전 호환: Memo는 오전으로)
                TxtAmNotes.Text = memoRow?.AmDestination ?? memoRow?.Memo ?? "";
                TxtPmNotes.Text = memoRow?.PmDestination ?? "";
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
            string[] dayNames = { "일", "월", "화", "수", "목", "금", "토" };
            string dayName = dayNames[(int)_selectedDate.DayOfWeek];
            TxtDatePill.Text = $"{_selectedDate:yyyy}년 {_selectedDate.Month}월 {_selectedDate.Day}일 ({dayName})";
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
            AddSectionHeader(grid, "자재/물류", 0, 0, 1, 1, "#0F172A", "#F1F5F9");

            // AM section
            AddSectionHeader(grid, "오전 (AM)", 0, ColAmDest, 1 + Vehicles.Length, 1, "#1E40AF", "#DBEAFE");
            // PM section
            AddSectionHeader(grid, "오후 (PM)", 0, ColPmDest, 1 + Vehicles.Length, 1, "#9A3412", "#FED7AA");

            // ── Row 1: Column Sub-headers ──
            AddColumnHeader(grid, "담당자", 1, ColPersonName, "#64748B", "#F8FAFC");
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

                // Person name (고정 표시 — 인원 관리에서만 변경)
                AddNameCell(grid, gridRow, ColPersonName, row.PersonName, altBg);

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

            var popup = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };

            SolidColorBrush Br(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);

            TextBlock Section(string t) => new TextBlock
            {
                Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = Br("#64748B"), Margin = new Thickness(10, 8, 10, 6)
            };

            Button ItemBtn(object content, string fill)
            {
                var b = new Button
                {
                    Content = content,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    BorderBrush = Br("#F1F5F9"),
                    Padding = new Thickness(10, 7, 10, 7),
                    Cursor = Cursors.Hand
                };
                b.Click += (_, _) => { target.Text = fill; onChanged(fill); popup.IsOpen = false; };
                return b;
            }

            // 왼쪽: 배차 목록 (없으면 안내)
            var leftSp = new StackPanel();
            leftSp.Children.Add(Section($"{_selectedDate:M월 d일} 배차 ({records.Count}건)"));
            if (records.Count == 0)
                leftSp.Children.Add(new TextBlock
                {
                    Text = "배차 목록 없음", FontSize = 13, Foreground = Br("#94A3B8"),
                    Margin = new Thickness(12, 4, 12, 14)
                });
            else
                foreach (var r in records)
                    leftSp.Children.Add(ItemBtn(BuildDispatchItemContent(r), BuildDestinationText(r)));

            // 오른쪽: 고정 빠른선택 (항상 표시)
            var rightSp = new StackPanel();
            rightSp.Children.Add(Section("빠른 선택"));
            foreach (var opt in QuickOptions)
                rightSp.Children.Add(ItemBtn(
                    new TextBlock { Text = opt, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Br("#0F172A") },
                    opt));

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            var leftScroll = new ScrollViewer { Content = leftSp, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(leftScroll, 0); layout.Children.Add(leftScroll);
            var divider = new Border { Background = Br("#E2E8F0") };
            Grid.SetColumn(divider, 1); layout.Children.Add(divider);
            Grid.SetColumn(rightSp, 2); layout.Children.Add(rightSp);

            var listBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = Br("#CBD5E1"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                MinWidth = 440,
                MaxWidth = 640,
                Child = layout,
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

        // 목적지&근무 칸에 채울 텍스트: 업체명만
        private static string BuildDestinationText(DispatchItemModel r)
        {
            return (r.VendorName ?? "").Trim();
        }

        // 담당자 이름: 고정 표시(읽기 전용)
        private void AddNameCell(Grid grid, int row, int col, string value, string bgHex)
        {
            var border = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)!),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")!),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(8, 4, 8, 4)
            };
            border.Child = new TextBlock
            {
                Text = value ?? "",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")!),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
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

        // 인원 관리(추가/삭제/순서) 다이얼로그 → 저장 시 로스터 반영 후 새로고침
        private void BtnManageMembers_Click(object sender, RoutedEventArgs e)
        {
            // 현재 편집 중인 목적지/차량 값을 잃지 않도록 먼저 저장 안내는 생략(로스터만 변경)
            var current = StatusBoardRepository.GetMaterialLogisticsMembers()
                                               .Select(m => m.Name).ToList();
            var win = new MemberManagerWindow(current) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                StatusBoardRepository.ReplaceMaterialLogisticsMembers(win.ResultMembers);
                LoadData();
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

    }
}
