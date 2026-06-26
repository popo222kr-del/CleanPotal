using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CleanPotal
{
    public partial class ScheduleBoardView : UserControl
    {
        private ScheduleBoardViewModel _vm;

        private bool _isSyncingScroll;
        private bool _isInitializing;
        private bool _isDrawing;
        private Border? _hoverCellRect;
        private Rectangle? _hoverRowRect;
        private Rectangle? _hoverColumnRect;
        private Border? _hoverEquipmentRect;

        private const bool EnableHoverCellHighlight = true;
        private const int BoardStartHour = 7;
        private const int BoardEndHourExclusive = BoardStartHour + 24;
        private const int MinutesPerCell = 10;

        private AutoSyncManager _syncManager;

        private readonly SolidColorBrush _colorBgWhite = Brushes.White;
        private readonly SolidColorBrush _colorBgMDC = new SolidColorBrush(Color.FromRgb(240, 249, 255));
        private readonly SolidColorBrush _colorBgNDC = new SolidColorBrush(Color.FromRgb(255, 241, 242));

        private readonly SolidColorBrush _colorLineSoft = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        private readonly SolidColorBrush _colorLineDash = new SolidColorBrush(Color.FromRgb(226, 232, 240));
        private readonly SolidColorBrush _colorTextPrimary = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        private readonly SolidColorBrush _colorTextSecondary = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        private readonly SolidColorBrush _colorTextMuted = new SolidColorBrush(Color.FromRgb(148, 163, 184));

        private readonly SolidColorBrush _colorBlockS2 = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        private readonly SolidColorBrush _colorBlockHF = new SolidColorBrush(Color.FromRgb(250, 191, 36));
        private readonly SolidColorBrush _colorBlockDI = new SolidColorBrush(Color.FromRgb(56, 189, 248));

        public ScheduleBoardView()
        {
            InitializeComponent();

            _isInitializing = true;
            _vm = new ScheduleBoardViewModel();
            _vm.PropertyChanged += Vm_PropertyChanged;
            _vm.Recipes.CollectionChanged += Recipes_CollectionChanged;

            DataContext = _vm;
            SizeChanged += ScheduleBoardView_SizeChanged;

            _isInitializing = false;
            string dbPath = System.IO.Path.Combine(AppPaths.DataRoot, "CleanPotal.db");

            _syncManager = new AutoSyncManager(() =>
            {
                if (_isDrawing || _isInitializing) return;

                Dispatcher.Invoke(() =>
                {
                    _vm.ReloadFromDatabase();
                    RefreshRecipeList();
                    DrawBoard();
                });
            }, dbPath);

            this.Loaded += (s, e) => _syncManager.Start();
            this.Unloaded += (s, e) => _syncManager.Stop();
        }

        public void OpenRecipeManager()
        {
            var win = new RecipeManagerWindow(_vm) { Owner = Window.GetWindow(this) };
            win.ShowDialog();
            RefreshRecipeList();
            DrawBoard();
        }

        private void Recipes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.Invoke(() => { RefreshRecipeList(); });
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScheduleBoardViewModel.StatusText) || e.PropertyName == nameof(ScheduleBoardViewModel.HoverText))
                UpdateStatusText();
            else if (e.PropertyName == nameof(ScheduleBoardViewModel.Zoom))
                UpdateZoomText();
        }

        private void ScheduleBoardView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ScheduleBoardViewModel vm)
            {
                _vm = vm;
                _vm.PropertyChanged += Vm_PropertyChanged;
            }
            Focus();
            SafeInitCheckStates();
            UpdateZoomText();
            RefreshRecipeList();
            DrawBoard();

            // 초기 렌더링 이후에 확실히 스크롤 포지션을 잡아주기 위해 한번 더 호출
            ScrollToRangeStartByCheck();
            UpdateStatusText();

            _vm.PropertyChanged += (s, ev) =>
            {
                if (ev.PropertyName == nameof(ScheduleBoardViewModel.SelectedRecipe)) RefreshRecipeList();
            };
        }

        private void ScheduleBoardView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsLoaded || !IsVisible) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Focus();
                DrawBoard();
                ScrollToRangeStartByCheck();
            }), DispatcherPriority.Background);
        }

        private void ScheduleBoardView_SizeChanged(object sender, SizeChangedEventArgs e) { if (IsLoaded) DrawBoard(); }

        private void ScheduleBoardView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) HideHoverCell();
            if (e.Key == Key.F5)
            {
                _vm.ReloadFromDatabase();
                RefreshRecipeList();
                DrawBoard();
                _vm.StatusText = "새로고침 완료 (F5)";
                UpdateStatusText();
                e.Handled = true;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                if (e.SystemKey == Key.Left) { _vm.GoToPrevDay(); HideHoverCell(); DrawBoard(); e.Handled = true; }
                else if (e.SystemKey == Key.Right) { _vm.GoToNextDay(); HideHoverCell(); DrawBoard(); e.Handled = true; }
            }
        }

        private void ScheduleBoardView_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) HideHoverCell();
        }

        private void SafeInitCheckStates()
        {
            if (DayCheckBox == null || NightCheckBox == null) return;
            if (DayCheckBox.IsChecked != true && NightCheckBox.IsChecked != true) DayCheckBox.IsChecked = true;
        }

        private void UpdateZoomText()
        {
            if (ZoomPercentText != null) ZoomPercentText.Text = $"{Math.Round(_vm.Zoom * 100):0}%";
        }

        private void UpdateStatusText()
        {
            if (StatusTextBlock == null) return;
            string status = _vm.StatusText ?? string.Empty;
            string hover = _vm.HoverText ?? string.Empty;
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(hover) ? status : string.IsNullOrWhiteSpace(status) ? hover : $"{status} | {hover}";
        }

        private void HideHoverCell()
        {
            if (_hoverCellRect != null) _hoverCellRect.Visibility = Visibility.Collapsed;
            if (_hoverRowRect != null) _hoverRowRect.Visibility = Visibility.Collapsed;
            if (_hoverColumnRect != null) _hoverColumnRect.Visibility = Visibility.Collapsed;
            if (_hoverEquipmentRect != null) _hoverEquipmentRect.Visibility = Visibility.Collapsed;
        }

        private int HourToAbsMinutes(int hour24) { int h = hour24; if (h < BoardStartHour) h += 24; return h * 60; }
        private double GetCellWidth() => Math.Max(1, AlignPx(_vm.CellWidth * _vm.Zoom));
        private double GetRowHeight() => Math.Max(1, AlignPx(_vm.RowHeight * _vm.Zoom));
        private static double AlignPx(double value) => Math.Round(value);
        private static double RowTop(int rowIndex, double rowH) => AlignPx(rowIndex * rowH);
        private static double CellLeft(int cellIndex, double cellW) => AlignPx(cellIndex * cellW);

        private void ScrollToRangeStartByCheck()
        {
            bool useNight = NightCheckBox != null && NightCheckBox.IsChecked == true && (DayCheckBox == null || DayCheckBox.IsChecked != true);
            ScrollToRangeStart(useNight);
        }

        private void ScrollToRangeStart(bool night)
        {
            if (BoardScrollViewer == null || HeaderScrollViewer == null || !IsLoaded) return;

            // ★ 23:00 버그 픽스: Render 우선순위를 주어 WPF가 물리적 넓이 계산을 완료한 후 오프셋을 먹이도록 강제 동기화
            Dispatcher.BeginInvoke(new Action(() =>
            {
                int startHour = night ? 19 : 7;
                int startAbs = HourToAbsMinutes(startHour);
                int offsetMinutes = Math.Max(0, startAbs - (BoardStartHour * 60));
                double x = (offsetMinutes / (double)MinutesPerCell) * GetCellWidth();

                _isSyncingScroll = true;
                BoardScrollViewer.ScrollToHorizontalOffset(x);
                HeaderScrollViewer.ScrollToHorizontalOffset(x);
                _isSyncingScroll = false;
            }), DispatcherPriority.Render);
        }

        private void DrawBoard()
        {
            if (_isDrawing || !IsLoaded) return;
            if (BoardContentCanvas == null || BoardGridCanvas == null || BoardBlocksCanvas == null || BoardInputLayer == null || TimelineHeaderCanvas == null || EquipmentCanvas == null || EquipmentHeaderCanvas == null) return;

            _isDrawing = true;
            try
            {
                double prevH = BoardScrollViewer?.HorizontalOffset ?? 0;
                double prevV = BoardScrollViewer?.VerticalOffset ?? 0;

                BoardGridCanvas.Children.Clear(); BoardBlocksCanvas.Children.Clear(); BoardInputLayer.Children.Clear();
                TimelineHeaderCanvas.Children.Clear(); EquipmentHeaderCanvas.Children.Clear(); EquipmentCanvas.Children.Clear();

                double cellW = GetCellWidth(); double rowH = GetRowHeight(); int totalCells = _vm.TotalCells;

                double equipmentWidth = _vm.EquipmentColumnWidth; double headerHeight = 60.0;
                double bodyHeight = AlignPx(_vm.Equipments.Count * rowH); double boardWidth = AlignPx(totalCells * cellW);

                EquipmentHeaderCanvas.Width = equipmentWidth; EquipmentHeaderCanvas.Height = headerHeight;
                EquipmentCanvas.Width = equipmentWidth; EquipmentCanvas.Height = bodyHeight;
                TimelineHeaderCanvas.Width = boardWidth; TimelineHeaderCanvas.Height = headerHeight;
                BoardContentCanvas.Width = boardWidth; BoardContentCanvas.Height = bodyHeight;

                Canvas.SetLeft(BoardGridCanvas, 0); Canvas.SetTop(BoardGridCanvas, 0); BoardGridCanvas.Width = boardWidth; BoardGridCanvas.Height = bodyHeight;
                Canvas.SetLeft(BoardBlocksCanvas, 0); Canvas.SetTop(BoardBlocksCanvas, 0); BoardBlocksCanvas.Width = boardWidth; BoardBlocksCanvas.Height = bodyHeight;
                Canvas.SetLeft(BoardInputLayer, 0); Canvas.SetTop(BoardInputLayer, 0); BoardInputLayer.Width = boardWidth; BoardInputLayer.Height = bodyHeight;

                DrawEquipmentHeader(equipmentWidth, headerHeight);
                DrawEquipmentRows(equipmentWidth, rowH);
                DrawTimelineHeader(boardWidth, headerHeight, cellW, totalCells);
                DrawBoardGrid(boardWidth, bodyHeight, cellW, rowH, totalCells);
                DrawPlacedBlocks(cellW, rowH);
                CreateHoverIndicators(cellW, rowH, boardWidth, bodyHeight);

                UpdateZoomText(); UpdateStatusText();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (BoardScrollViewer == null || HeaderScrollViewer == null || EquipmentScrollViewer == null) return;
                    double maxH = Math.Max(0, BoardScrollViewer.ExtentWidth - BoardScrollViewer.ViewportWidth);
                    double maxV = Math.Max(0, BoardScrollViewer.ExtentHeight - BoardScrollViewer.ViewportHeight);
                    double h = Math.Min(prevH, maxH); double v = Math.Min(prevV, maxV);
                    _isSyncingScroll = true;
                    BoardScrollViewer.ScrollToHorizontalOffset(h); BoardScrollViewer.ScrollToVerticalOffset(v);
                    HeaderScrollViewer.ScrollToHorizontalOffset(h); EquipmentScrollViewer.ScrollToVerticalOffset(v);
                    _isSyncingScroll = false;
                }), DispatcherPriority.Loaded);
            }
            finally { _isDrawing = false; }
        }

        private void DrawEquipmentHeader(double equipmentWidth, double headerHeight)
        {
            var headerBg = new Rectangle { Width = equipmentWidth, Height = headerHeight, Fill = _colorBgWhite };
            TimelineHeaderCanvas.Children.Add(headerBg);
            var text = new TextBlock { Text = "설비 목록", FontWeight = FontWeights.Bold, FontSize = 14, Foreground = _colorTextSecondary };
            Canvas.SetLeft(text, 16); Canvas.SetTop(text, Math.Max(4, (headerHeight - 20) / 2));
            EquipmentHeaderCanvas.Children.Add(text);
        }

        private void DrawEquipmentRows(double equipmentWidth, double rowH)
        {
            for (int i = 0; i < _vm.Equipments.Count; i++)
            {
                double y = RowTop(i, rowH);
                string eqName = _vm.Equipments[i].DisplayName;

                Brush bg = _colorBgWhite;
                if (eqName.StartsWith("MDC")) bg = _colorBgMDC;
                else if (eqName.StartsWith("NDC")) bg = _colorBgNDC;

                var rowBg = new Rectangle { Width = equipmentWidth, Height = rowH, Fill = bg };
                Canvas.SetLeft(rowBg, 0); Canvas.SetTop(rowBg, y); EquipmentCanvas.Children.Add(rowBg);

                var txt = new TextBlock { Text = eqName, FontWeight = FontWeights.SemiBold, FontSize = Math.Max(11, 12 * _vm.Zoom), Foreground = _colorTextPrimary, TextTrimming = TextTrimming.CharacterEllipsis, Width = Math.Max(10, equipmentWidth - 20) };
                Canvas.SetLeft(txt, 16); Canvas.SetTop(txt, y + Math.Max(1, (rowH - 16) / 2)); EquipmentCanvas.Children.Add(txt);

                var line = new Line { X1 = 0, Y1 = y + rowH, X2 = equipmentWidth, Y2 = y + rowH, Stroke = _colorLineSoft, StrokeThickness = 1 };
                EquipmentCanvas.Children.Add(line);
            }
        }

        private void DrawTimelineHeader(double boardWidth, double headerHeight, double cellW, int totalCells)
        {
            var headerBg = new Rectangle { Width = boardWidth, Height = headerHeight, Fill = _colorBgWhite };
            TimelineHeaderCanvas.Children.Add(headerBg);

            for (int cell = 0; cell <= totalCells; cell++)
            {
                double x = CellLeft(cell, cellW);
                int absoluteMinutes = BoardStartHour * 60 + cell * MinutesPerCell;
                int hour = (absoluteMinutes / 60) % 24;
                int minute = absoluteMinutes % 60;

                if (minute == 0)
                {
                    var hourText = new TextBlock { Text = $"{hour:00}:00", FontSize = Math.Max(12, 14 * _vm.Zoom), FontWeight = FontWeights.Bold, Foreground = _colorTextPrimary };
                    Canvas.SetLeft(hourText, x + 4); Canvas.SetTop(hourText, 10);
                    TimelineHeaderCanvas.Children.Add(hourText);
                }
                else if (cell < totalCells)
                {
                    var minuteText = new TextBlock { Text = minute.ToString("00"), FontSize = Math.Max(9, 10 * _vm.Zoom), Foreground = _colorTextMuted, Width = cellW, TextAlignment = TextAlignment.Center };
                    Canvas.SetLeft(minuteText, x); Canvas.SetTop(minuteText, 40);
                    TimelineHeaderCanvas.Children.Add(minuteText);
                }
            }
        }

        private void DrawBoardGrid(double boardWidth, double bodyHeight, double cellW, double rowH, int totalCells)
        {
            var boardBg = new Rectangle { Width = boardWidth, Height = bodyHeight, Fill = _colorBgWhite };
            BoardGridCanvas.Children.Add(boardBg);

            for (int i = 0; i < _vm.Equipments.Count; i++)
            {
                string eqName = _vm.Equipments[i].DisplayName;
                Brush bg = _colorBgWhite;
                if (eqName.StartsWith("MDC")) bg = _colorBgMDC;
                else if (eqName.StartsWith("NDC")) bg = _colorBgNDC;

                if (bg != _colorBgWhite)
                {
                    double y = RowTop(i, rowH);
                    var rowBg = new Rectangle { Width = boardWidth, Height = rowH, Fill = bg, IsHitTestVisible = false };
                    Canvas.SetLeft(rowBg, 0); Canvas.SetTop(rowBg, y);
                    BoardGridCanvas.Children.Add(rowBg);
                }
            }

            for (int i = 0; i <= _vm.Equipments.Count; i++)
            {
                double y = RowTop(i, rowH);
                var hLine = new Line { X1 = 0, Y1 = y, X2 = boardWidth, Y2 = y, Stroke = _colorLineSoft, StrokeThickness = 1 };
                BoardGridCanvas.Children.Add(hLine);
            }

            int cellsPerHour = 60 / MinutesPerCell;
            for (int cell = 0; cell <= totalCells; cell += cellsPerHour)
            {
                double x = CellLeft(cell, cellW);
                var vLine = new Line { X1 = x, Y1 = 0, X2 = x, Y2 = bodyHeight, Stroke = _colorLineSoft, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } };
                BoardGridCanvas.Children.Add(vLine);
            }
        }

        private Border CreateBlockUI(PlacedRecipeBlock block, double cellW, double h)
        {
            double s2W = block.S2Minutes / 10.0 * cellW; double hfW = block.HFMinutes / 10.0 * cellW; double diW = block.DIMinutes / 10.0 * cellW;
            double totalW = Math.Round(s2W + hfW + diW); s2W = Math.Round(s2W); hfW = Math.Round(hfW); diW = Math.Round(diW);

            var container = new Border { Width = totalW, Height = h, CornerRadius = new CornerRadius(6), ClipToBounds = true, SnapsToDevicePixels = true, Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = 0.2, BlurRadius = 6, ShadowDepth = 2, Direction = 270 } };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(s2W) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(hfW) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(diW) });

            if (s2W > 0) { var r = new Rectangle { Fill = _colorBlockS2 }; Grid.SetColumn(r, 0); grid.Children.Add(r); }
            if (hfW > 0) { var r = new Rectangle { Fill = _colorBlockHF }; Grid.SetColumn(r, 1); grid.Children.Add(r); }
            if (diW > 0) { var r = new Rectangle { Fill = _colorBlockDI }; Grid.SetColumn(r, 2); grid.Children.Add(r); }

            var textBlock = new TextBlock { Text = block.DisplayText, FontSize = Math.Max(10, 11 * _vm.Zoom), FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumnSpan(textBlock, 3); grid.Children.Add(textBlock); container.Child = grid; return container;
        }

        private void DrawPlacedBlocks(double cellW, double rowH)
        {
            foreach (var block in _vm.PlacedBlocks)
            {
                if (block.EquipmentIndex < 0 || block.EquipmentIndex >= _vm.Equipments.Count) continue;

                double rowTop = RowTop(block.EquipmentIndex, rowH);
                double y = Math.Round(rowTop + 3);
                double h = Math.Round(Math.Max(2, rowH - 6));

                double x = Math.Round((block.StartMinute / 10.0) * cellW + 1);

                var ui1 = CreateBlockUI(block, cellW, h);
                Canvas.SetLeft(ui1, x); Canvas.SetTop(ui1, y); BoardBlocksCanvas.Children.Add(ui1);
            }
        }

        private void CreateHoverIndicators(double cellW, double rowH, double boardW, double bodyH)
        {
            if (!EnableHoverCellHighlight) return;
            _hoverRowRect = new Rectangle { Width = Math.Max(0, boardW), Height = rowH, Fill = new SolidColorBrush(Color.FromArgb(12, 0, 0, 0)), Visibility = Visibility.Collapsed, IsHitTestVisible = false }; BoardBlocksCanvas.Children.Add(_hoverRowRect);
            _hoverColumnRect = new Rectangle { Width = cellW / 10.0, Height = Math.Max(0, bodyH), Fill = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), Visibility = Visibility.Collapsed, IsHitTestVisible = false }; BoardBlocksCanvas.Children.Add(_hoverColumnRect);
            _hoverCellRect = new Border { Width = cellW, Height = rowH - 4, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(50, 56, 189, 248)), BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)), BorderThickness = new Thickness(1.5), Visibility = Visibility.Collapsed, IsHitTestVisible = false }; BoardBlocksCanvas.Children.Add(_hoverCellRect);
            if (EquipmentCanvas != null) { _hoverEquipmentRect = new Border { Width = _vm.EquipmentColumnWidth, Height = rowH, Background = new SolidColorBrush(Color.FromArgb(12, 0, 0, 0)), Visibility = Visibility.Collapsed, IsHitTestVisible = false }; EquipmentCanvas.Children.Add(_hoverEquipmentRect); }
        }

        private void RefreshRecipeList()
        {
            if (RecipeListPanel == null) return;
            RecipeListPanel.Children.Clear();
            var recipes = _vm.GetRecipesOrdered();

            var starStyle = TryFindResource("StarCheckBoxStyle") as Style;
            var btnStyle = TryFindResource("RecipeItemButtonStyle") as Style;

            foreach (var recipe in recipes)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());

                var fav = new CheckBox { IsChecked = recipe.IsFavorite, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0), ToolTip = "즐겨찾기", Tag = recipe };
                if (starStyle != null) fav.Style = starStyle;
                fav.Checked += RecipeFavoriteCheckBox_Checked;
                fav.Unchecked += RecipeFavoriteCheckBox_Unchecked;
                Grid.SetColumn(fav, 0);

                bool isSelected = ReferenceEquals(_vm.SelectedRecipe, recipe);
                var btn = new Button { Content = recipe.DisplayText, Tag = recipe };
                if (btnStyle != null) btn.Style = btnStyle;

                if (isSelected)
                {
                    btn.Background = new SolidColorBrush(Color.FromRgb(224, 242, 254));
                    btn.BorderBrush = new SolidColorBrush(Color.FromRgb(125, 211, 252));
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(3, 105, 161));
                    btn.FontWeight = FontWeights.Bold;
                }
                btn.Click += RecipeSelectButton_Click;
                Grid.SetColumn(btn, 1);

                row.Children.Add(fav); row.Children.Add(btn); RecipeListPanel.Children.Add(row);
            }
            UpdateStatusText();
        }

        private void RecipeSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not RecipeDefinition recipe) return;
            if (ReferenceEquals(_vm.SelectedRecipe, recipe)) { _vm.SelectedRecipe = null; _vm.StatusText = "레시피 선택 해제: 셀 선택 모드"; }
            else { _vm.SelectedRecipe = recipe; _vm.StatusText = $"적용 모드: {_vm.LastClickedEquipmentName ?? "-"} / {recipe.Text}"; }
            RefreshRecipeList();
        }

        private void RecipeFavoriteCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is RecipeDefinition recipe)
            {
                recipe.IsFavorite = true;
                _vm.ReorderRecipesByFavorite();
                _vm.SaveRecipes();
                RefreshRecipeList();
            }
        }

        private void RecipeFavoriteCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is RecipeDefinition recipe)
            {
                recipe.IsFavorite = false;
                _vm.ReorderRecipesByFavorite();
                _vm.SaveRecipes();
                RefreshRecipeList();
            }
        }

        public void UndoAction() { if (_vm.TryUndoLastBoardAction(out string msg)) { HideHoverCell(); _vm.StatusText = msg; DrawBoard(); return; } _vm.StatusText = msg; UpdateStatusText(); }
        public void ResetAll() { _vm.ClearPlacedBlocks(); HideHoverCell(); _vm.StatusText = "전체 초기화 완료 (배치된 레시피 전체 삭제)"; DrawBoard(); }
        public void CaptureBoard() { try { CaptureCurrentRangeToClipboard(); } catch (Exception ex) { _vm.StatusText = $"캡처 실패: {ex.Message}"; UpdateStatusText(); } }

        private void BtnPrevDay_Click(object sender, RoutedEventArgs e) { _vm.GoToPrevDay(); HideHoverCell(); DrawBoard(); }
        private void BtnNextDay_Click(object sender, RoutedEventArgs e) { _vm.GoToNextDay(); HideHoverCell(); DrawBoard(); }
        private void BtnToday_Click(object sender, RoutedEventArgs e) { _vm.GoToToday(); HideHoverCell(); DrawBoard(); }
        private void BtnCopyPrevDay_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.PlacedBlocks.Count > 0)
            {
                var result = MessageBox.Show($"현재 날짜({_vm.CurrentDate:yyyy-MM-dd})에 이미 배치된 데이터가 있습니다.\n전일 데이터를 추가로 복사하시겠습니까?", "전일 복사", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }
            if (_vm.CopyFromPrevDay(out string msg)) { HideHoverCell(); DrawBoard(); }
            _vm.StatusText = msg; UpdateStatusText();
        }

        private void DayCheckBox_Checked(object sender, RoutedEventArgs e) { if (_isInitializing) return; ScrollToRangeStart(night: false); }
        private void DayCheckBox_Unchecked(object sender, RoutedEventArgs e) { if (_isInitializing) return; if (DayCheckBox != null && NightCheckBox != null && DayCheckBox.IsChecked != true && NightCheckBox.IsChecked != true) { DayCheckBox.IsChecked = true; return; } if (NightCheckBox?.IsChecked == true) ScrollToRangeStart(night: true); }
        private void NightCheckBox_Checked(object sender, RoutedEventArgs e) { if (_isInitializing) return; ScrollToRangeStart(night: true); }
        private void NightCheckBox_Unchecked(object sender, RoutedEventArgs e) { if (_isInitializing) return; if (DayCheckBox != null && NightCheckBox != null && DayCheckBox.IsChecked != true && NightCheckBox.IsChecked != true) { NightCheckBox.IsChecked = true; return; } if (DayCheckBox?.IsChecked == true) ScrollToRangeStart(night: false); }

        private void CaptureCurrentRangeToClipboard()
        {
            if (_vm == null) return;
            bool day = DayCheckBox?.IsChecked == true; bool night = NightCheckBox?.IsChecked == true;
            int dayStart = 7 * 60; int dayEnd = 19 * 60; int nightStart = 19 * 60; int nightEndBoundary = BoardEndHourExclusive * 60;
            int startAbs; int endAbs; string label;

            if (day && night) { startAbs = dayStart; endAbs = nightEndBoundary; label = "전체(07:00~06:00)"; }
            else if (day) { startAbs = dayStart; endAbs = dayEnd; label = "주간(07:00~19:00)"; }
            else if (night) { startAbs = nightStart; endAbs = nightEndBoundary; label = "야간(19:00~06:00)"; }
            else { startAbs = dayStart; endAbs = dayEnd; label = "주간(07:00~19:00)"; }

            var blocks = _vm.LoadBlocksForDate(_vm.CurrentDate);
            var final = BuildCaptureBitmapFromModel(_vm.CurrentDate, blocks, startAbs, endAbs);
            Clipboard.SetImage(final);
            _vm.StatusText = $"캡처 완료: {label} / {_vm.CurrentDateText}"; UpdateStatusText();
        }

        public void MultiCaptureBoard()
        {
            try
            {
                var selectedDates = ShowMultiDatePickerDialog();
                if (selectedDates == null || selectedDates.Count == 0) return;

                bool day = DayCheckBox?.IsChecked == true; bool night = NightCheckBox?.IsChecked == true;
                int dayStart = 7 * 60; int dayEnd = 19 * 60; int nightStart = 19 * 60; int nightEndBoundary = BoardEndHourExclusive * 60;
                int startAbs; int endAbs; string label;

                if (day && night) { startAbs = dayStart; endAbs = nightEndBoundary; label = "전체(07:00~06:00)"; }
                else if (day) { startAbs = dayStart; endAbs = dayEnd; label = "주간(07:00~19:00)"; }
                else if (night) { startAbs = nightStart; endAbs = nightEndBoundary; label = "야간(19:00~06:00)"; }
                else { startAbs = dayStart; endAbs = dayEnd; label = "주간(07:00~19:00)"; }

                selectedDates.Sort();
                var final = BuildMultiDayCaptureBitmap(selectedDates, startAbs, endAbs);
                Clipboard.SetImage(final);

                string dateRange = selectedDates.Count == 1
                    ? $"{selectedDates[0]:yyyy-MM-dd}"
                    : $"{selectedDates[0]:yyyy-MM-dd} ~ {selectedDates[^1]:yyyy-MM-dd} ({selectedDates.Count}일)";
                _vm.StatusText = $"멀티 캡처 완료: {label} / {dateRange}"; UpdateStatusText();
            }
            catch (Exception ex) { _vm.StatusText = $"멀티 캡처 실패: {ex.Message}"; UpdateStatusText(); }
        }

        private List<DateTime>? ShowMultiDatePickerDialog()
        {
            var selectedDates = new HashSet<DateTime> { _vm.CurrentDate.Date };
            DateTime displayMonth = new DateTime(_vm.CurrentDate.Year, _vm.CurrentDate.Month, 1);

            var accentBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            var accentLightBrush = new SolidColorBrush(Color.FromRgb(219, 234, 254));
            var textDarkBrush = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            var textMutedBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            var bgBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            var hoverBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            var todayBorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            string[] dayHeaders = { "일", "월", "화", "수", "목", "금", "토" };
            string[] krDow = { "일요일", "월요일", "화요일", "수요일", "목요일", "금요일", "토요일" };

            var dlg = new Window
            {
                Title = "멀티 캡처",
                Width = 400, Height = 580,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent
            };

            var outerBorder = new Border
            {
                Background = bgBrush, CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)), BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = 0.15, BlurRadius = 24, ShadowDepth = 8 },
                Margin = new Thickness(12)
            };

            var rootPanel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            titleBar.ColumnDefinitions.Add(new ColumnDefinition());
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleText = new TextBlock { Text = "날짜 선택", FontSize = 18, FontWeight = FontWeights.ExtraBold, Foreground = textDarkBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(titleText, 0);
            titleBar.Children.Add(titleText);
            var closeBtn = new Button { Content = "✕", FontSize = 14, Width = 32, Height = 32, Cursor = Cursors.Hand, Background = Brushes.Transparent, Foreground = textMutedBrush, BorderThickness = new Thickness(0) };
            closeBtn.Click += (s, e) => { dlg.DialogResult = false; };
            Grid.SetColumn(closeBtn, 1);
            titleBar.Children.Add(closeBtn);
            rootPanel.Children.Add(titleBar);
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) dlg.DragMove(); };

            var monthNav = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            monthNav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            monthNav.ColumnDefinitions.Add(new ColumnDefinition());
            monthNav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var monthText = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = textDarkBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(monthText, 1);
            monthNav.Children.Add(monthText);

            var dayGrid = new UniformGrid { Rows = 1, Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
            foreach (string dh in dayHeaders)
            {
                var brush = dh == "일" ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : dh == "토" ? accentBrush : textMutedBrush;
                dayGrid.Children.Add(new TextBlock { Text = dh, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
            }
            rootPanel.Children.Add(monthNav);
            rootPanel.Children.Add(dayGrid);

            var calendarGrid = new UniformGrid { Rows = 6, Columns = 7 };
            rootPanel.Children.Add(calendarGrid);

            var selectedCountText = new TextBlock
            {
                Text = "1일 선택됨", FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = accentBrush, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 0)
            };
            rootPanel.Children.Add(selectedCountText);

            var selectedDatesText = new TextBlock
            {
                FontSize = 11, Foreground = textMutedBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 320, Margin = new Thickness(0, 4, 0, 0)
            };
            rootPanel.Children.Add(selectedDatesText);

            string[] krDowShort = { "일", "월", "화", "수", "목", "금", "토" };

            Action refreshCalendar = null!;
            Action updateSummary = () =>
            {
                int count = selectedDates.Count;
                selectedCountText.Text = count > 0 ? $"{count}일 선택됨" : "날짜를 선택하세요";
                if (count > 0)
                {
                    var sorted = selectedDates.OrderBy(d => d).ToList();
                    selectedDatesText.Text = FormatDateRanges(sorted, krDowShort);
                }
                else selectedDatesText.Text = "";
            };

            bool isDragging = false;
            bool dragAdding = true;
            DateTime? lastDragDate = null;

            DateTime? hitTestDate(Point pos)
            {
                int firstDow = (int)displayMonth.DayOfWeek;
                int daysInMonth = DateTime.DaysInMonth(displayMonth.Year, displayMonth.Month);
                double cellW = calendarGrid.ActualWidth / 7.0;
                double cellH = calendarGrid.ActualHeight / 6.0;
                int colIdx = (int)(pos.X / cellW);
                int rowIdx = (int)(pos.Y / cellH);
                if (colIdx < 0 || colIdx > 6 || rowIdx < 0 || rowIdx > 5) return null;
                int dayNum = rowIdx * 7 + colIdx - firstDow + 1;
                if (dayNum < 1 || dayNum > daysInMonth) return null;
                return new DateTime(displayMonth.Year, displayMonth.Month, dayNum);
            }

            refreshCalendar = () =>
            {
                calendarGrid.Children.Clear();
                monthText.Text = $"{displayMonth:yyyy년 M월}";
                int firstDow = (int)displayMonth.DayOfWeek;
                int daysInMonth = DateTime.DaysInMonth(displayMonth.Year, displayMonth.Month);
                DateTime today = DateTime.Today;

                for (int i = 0; i < 42; i++)
                {
                    int dayNum = i - firstDow + 1;
                    if (dayNum < 1 || dayNum > daysInMonth)
                    {
                        calendarGrid.Children.Add(new Border { Width = 42, Height = 42 });
                        continue;
                    }

                    DateTime cellDate = new DateTime(displayMonth.Year, displayMonth.Month, dayNum);
                    bool isSelected = selectedDates.Contains(cellDate);
                    bool isToday = cellDate == today;
                    int col = i % 7;

                    var cellBorder = new Border
                    {
                        Width = 42, Height = 42, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand,
                        Background = isSelected ? accentBrush : Brushes.Transparent,
                        BorderBrush = isToday && !isSelected ? todayBorderBrush : Brushes.Transparent,
                        BorderThickness = new Thickness(isToday && !isSelected ? 2 : 0),
                        Margin = new Thickness(1)
                    };

                    var dayText = new TextBlock
                    {
                        Text = dayNum.ToString(), FontSize = 14,
                        FontWeight = isSelected || isToday ? FontWeights.Bold : FontWeights.SemiBold,
                        Foreground = isSelected ? Brushes.White : col == 0 ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : col == 6 ? accentBrush : textDarkBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    cellBorder.Child = dayText;

                    cellBorder.MouseEnter += (s, e) => { if (!isDragging && !isSelected) ((Border)s!).Background = hoverBrush; };
                    cellBorder.MouseLeave += (s, e) => { if (!isDragging && !selectedDates.Contains(cellDate)) ((Border)s!).Background = Brushes.Transparent; };

                    calendarGrid.Children.Add(cellBorder);
                }
                updateSummary();
            };

            calendarGrid.MouseLeftButtonDown += (s, e) =>
            {
                var date = hitTestDate(e.GetPosition(calendarGrid));
                if (date == null) return;
                isDragging = true;
                dragAdding = !selectedDates.Contains(date.Value);
                if (dragAdding) selectedDates.Add(date.Value);
                else selectedDates.Remove(date.Value);
                lastDragDate = date.Value;
                calendarGrid.CaptureMouse();
                refreshCalendar();
                e.Handled = true;
            };
            calendarGrid.MouseMove += (s, e) =>
            {
                if (!isDragging) return;
                var date = hitTestDate(e.GetPosition(calendarGrid));
                if (date == null || date == lastDragDate) return;
                lastDragDate = date.Value;
                if (dragAdding) { if (!selectedDates.Contains(date.Value)) selectedDates.Add(date.Value); }
                else selectedDates.Remove(date.Value);
                refreshCalendar();
            };
            calendarGrid.MouseLeftButtonUp += (s, e) =>
            {
                if (isDragging) { isDragging = false; lastDragDate = null; calendarGrid.ReleaseMouseCapture(); }
            };

            var prevBtn = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            prevBtn.Child = new TextBlock { Text = "◀", FontSize = 12, Foreground = textDarkBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            prevBtn.MouseEnter += (s, e) => ((Border)s!).Background = hoverBrush;
            prevBtn.MouseLeave += (s, e) => ((Border)s!).Background = Brushes.Transparent;
            prevBtn.MouseLeftButtonDown += (s, e) => { displayMonth = displayMonth.AddMonths(-1); refreshCalendar(); };
            Grid.SetColumn(prevBtn, 0);
            monthNav.Children.Add(prevBtn);

            var nextBtn = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            nextBtn.Child = new TextBlock { Text = "▶", FontSize = 12, Foreground = textDarkBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            nextBtn.MouseEnter += (s, e) => ((Border)s!).Background = hoverBrush;
            nextBtn.MouseLeave += (s, e) => ((Border)s!).Background = Brushes.Transparent;
            nextBtn.MouseLeftButtonDown += (s, e) => { displayMonth = displayMonth.AddMonths(1); refreshCalendar(); };
            Grid.SetColumn(nextBtn, 2);
            monthNav.Children.Add(nextBtn);

            refreshCalendar();

            var btnPanel = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            btnPanel.ColumnDefinitions.Add(new ColumnDefinition());
            btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btnClear = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(12, 8, 12, 8) };
            btnClear.Child = new TextBlock { Text = "선택 초기화", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = textMutedBrush };
            btnClear.MouseEnter += (s, e) => ((Border)s!).Background = hoverBrush;
            btnClear.MouseLeave += (s, e) => ((Border)s!).Background = Brushes.Transparent;
            btnClear.MouseLeftButtonDown += (s, e) => { selectedDates.Clear(); updateSummary(); refreshCalendar(); };
            Grid.SetColumn(btnClear, 0);
            btnPanel.Children.Add(btnClear);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(btnRow, 2);

            var btnCancel = new Border { CornerRadius = new CornerRadius(8), Background = hoverBrush, Cursor = Cursors.Hand, Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0, 0, 8, 0) };
            btnCancel.Child = new TextBlock { Text = "취소", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = textDarkBrush, HorizontalAlignment = HorizontalAlignment.Center };
            btnCancel.MouseLeftButtonDown += (s, e) => { dlg.DialogResult = false; };

            var btnOk = new Border { CornerRadius = new CornerRadius(8), Background = accentBrush, Cursor = Cursors.Hand, Padding = new Thickness(20, 8, 20, 8) };
            btnOk.Child = new TextBlock { Text = "캡처", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            btnOk.MouseEnter += (s, e) => ((Border)s!).Background = new SolidColorBrush(Color.FromRgb(29, 78, 216));
            btnOk.MouseLeave += (s, e) => ((Border)s!).Background = accentBrush;

            List<DateTime>? result = null;
            btnOk.MouseLeftButtonDown += (s, e) =>
            {
                if (selectedDates.Count == 0) return;
                result = selectedDates.OrderBy(d => d).ToList();
                dlg.DialogResult = true;
            };

            btnRow.Children.Add(btnCancel);
            btnRow.Children.Add(btnOk);
            btnPanel.Children.Add(btnRow);
            rootPanel.Children.Add(btnPanel);

            outerBorder.Child = rootPanel;
            dlg.Content = outerBorder;

            return dlg.ShowDialog() == true ? result : null;
        }

        private RenderTargetBitmap BuildMultiDayCaptureBitmap(List<DateTime> dates, int startAbsMinutes, int endAbsMinutes)
        {
            double cellW = Math.Max(1, Math.Round(GetCellWidth())); double rowH = Math.Max(1, Math.Round(GetRowHeight()));
            double headerH = 60.0; double equipmentW = Math.Max(1, Math.Round(_vm.EquipmentColumnWidth));
            double bodyH = Math.Max(1, _vm.Equipments.Count) * rowH;
            double oneDayH = headerH + bodyH;
            double separatorH = 12.0;
            double totalH = dates.Count * oneDayH + (dates.Count - 1) * separatorH;

            int boardStartAbs = BoardStartHour * 60;
            int startCell = Math.Max(0, (int)Math.Floor((startAbsMinutes - boardStartAbs) / (double)MinutesPerCell));
            int endCell = Math.Min(_vm.TotalCells, (int)Math.Ceiling((endAbsMinutes - boardStartAbs) / (double)MinutesPerCell));
            double boardW = Math.Max(cellW, (endCell - startCell) * cellW);
            int outW = Math.Max(1, (int)Math.Ceiling(equipmentW + boardW));
            int outH = Math.Max(1, (int)Math.Ceiling(totalH));

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, outW, outH));

                for (int d = 0; d < dates.Count; d++)
                {
                    var blocks = _vm.LoadBlocksForDate(dates[d]);
                    double offsetY = d * (oneDayH + separatorH);

                    dc.PushTransform(new TranslateTransform(0, offsetY));
                    DrawCaptureGrid(dc, startCell, endCell, cellW, rowH, headerH, equipmentW);
                    DrawCaptureBlocksForList(dc, blocks, startCell, endCell, cellW, rowH, headerH, equipmentW);
                    DrawCaptureHeadersForDate(dc, dates[d], startCell, endCell, cellW, rowH, headerH, equipmentW);
                    var framePen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1);
                    dc.DrawRectangle(null, framePen, new Rect(0, 0, equipmentW + boardW, oneDayH));
                    dc.Pop();
                }
            }

            var final = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32); final.Render(dv); final.Freeze(); return final;
        }

        private RenderTargetBitmap BuildCaptureBitmapFromModel(DateTime date, List<PlacedRecipeBlock> blocks, int startAbsMinutes, int endAbsMinutes)
        {
            double cellW = Math.Max(1, Math.Round(GetCellWidth())); double rowH = Math.Max(1, Math.Round(GetRowHeight()));
            double headerH = 60.0; double equipmentW = Math.Max(1, Math.Round(_vm.EquipmentColumnWidth));
            double bodyH = Math.Max(1, _vm.Equipments.Count) * rowH;

            int boardStartAbs = BoardStartHour * 60;
            int startCell = (int)Math.Floor((startAbsMinutes - boardStartAbs) / (double)MinutesPerCell);
            int endCell = (int)Math.Ceiling((endAbsMinutes - boardStartAbs) / (double)MinutesPerCell);

            startCell = Math.Max(0, Math.Min(_vm.TotalCells, startCell)); endCell = Math.Max(startCell, Math.Min(_vm.TotalCells, endCell));
            double boardW = Math.Max(cellW, (endCell - startCell) * cellW);
            int outW = Math.Max(1, (int)Math.Ceiling(equipmentW + boardW)); int outH = Math.Max(1, (int)Math.Ceiling(headerH + bodyH));

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, outW, outH));
                DrawCaptureGrid(dc, startCell, endCell, cellW, rowH, headerH, equipmentW);
                DrawCaptureBlocksForList(dc, blocks, startCell, endCell, cellW, rowH, headerH, equipmentW);
                DrawCaptureHeadersForDate(dc, date, startCell, endCell, cellW, rowH, headerH, equipmentW);

                var framePen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1);
                dc.DrawRectangle(null, framePen, new Rect(0, 0, equipmentW + boardW, headerH + bodyH));
            }

            var final = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32); final.Render(dv); final.Freeze(); return final;
        }

        private void DrawCaptureHeadersForDate(DrawingContext dc, DateTime date, int startCell, int endCell, double cellW, double rowH, double headerH, double equipmentW)
        {
            string[] krDow = { "일요일", "월요일", "화요일", "수요일", "목요일", "금요일", "토요일" };
            var whiteBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255)); var textPrimary = new SolidColorBrush(Color.FromRgb(15, 23, 42)); var textMuted = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            dc.DrawRectangle(whiteBrush, null, new Rect(0, 0, equipmentW, headerH));
            string captureDateText = $"{date:yyyy-MM-dd} ({krDow[(int)date.DayOfWeek]})";

            double dateFont = Math.Max(16, 18 * _vm.Zoom); double dateY = Math.Max(2, (headerH - dateFont) / 2.0 - 2);
            DrawTextCentered(dc, captureDateText, 0, equipmentW, dateY, dateFont, FontWeights.Bold, textPrimary);

            for (int i = 0; i < _vm.Equipments.Count; i++)
            {
                double y = headerH + RowTop(i, rowH); string eqName = _vm.Equipments[i].DisplayName;
                Brush bg = _colorBgWhite; if (eqName.StartsWith("MDC")) bg = _colorBgMDC; else if (eqName.StartsWith("NDC")) bg = _colorBgNDC;
                dc.DrawRectangle(bg, null, new Rect(0, y, equipmentW, rowH));
                double ty = y + Math.Max(0, (rowH - Math.Max(10, 10 * _vm.Zoom)) / 2.0) - 2;
                DrawText(dc, _vm.Equipments[i].DisplayName, 16, ty, Math.Max(11, 12 * _vm.Zoom), FontWeights.SemiBold, textPrimary);
            }

            double boardW = (endCell - startCell) * cellW; dc.DrawRectangle(whiteBrush, null, new Rect(equipmentW, 0, boardW, headerH));
            for (int cell = startCell; cell < endCell; cell++)
            {
                int absMinutes = BoardStartHour * 60 + cell * MinutesPerCell; int hour = (absMinutes / 60) % 24; int minute = absMinutes % 60;
                double x = equipmentW + (cell - startCell) * cellW;
                if (x >= equipmentW + boardW) continue;
                if (minute == 0) DrawText(dc, $"{hour:00}:00", Math.Max(equipmentW, x) + 4, 10, Math.Max(12, 14 * _vm.Zoom), FontWeights.Bold, textPrimary);
                else DrawTextCentered(dc, $"{minute:00}", x, x + cellW, 40, Math.Max(9, 10 * _vm.Zoom), FontWeights.Normal, textMuted);
            }
        }

        private void DrawCaptureGrid(DrawingContext dc, int startCell, int endCell, double cellW, double rowH, double headerH, double equipmentW)
        {
            double bodyH = Math.Max(1, _vm.Equipments.Count) * rowH; double boardW = (endCell - startCell) * cellW;
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 255, 255)), null, new Rect(equipmentW, headerH, boardW, bodyH));

            for (int r = 0; r < _vm.Equipments.Count; r++)
            {
                string eqName = _vm.Equipments[r].DisplayName; Brush bg = _colorBgWhite; if (eqName.StartsWith("MDC")) bg = _colorBgMDC; else if (eqName.StartsWith("NDC")) bg = _colorBgNDC;
                if (bg != _colorBgWhite) { double y = headerH + RowTop(r, rowH); dc.DrawRectangle(bg, null, new Rect(equipmentW, y, boardW, rowH)); }
            }

            var linePen = new Pen(new SolidColorBrush(Color.FromRgb(241, 245, 249)), 1); var dashPen = new Pen(new SolidColorBrush(Color.FromRgb(241, 245, 249)), 1) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) };
            for (int r = 0; r <= _vm.Equipments.Count; r++) { double y = headerH + RowTop(r, rowH); dc.DrawLine(linePen, new Point(equipmentW, y), new Point(equipmentW + boardW, y)); }
            for (int rel = 0; rel <= endCell - startCell; rel++) { int cell = startCell + rel; double x = equipmentW + rel * cellW; if (((BoardStartHour * 60 + cell * MinutesPerCell) % 60) == 0) { dc.DrawLine(dashPen, new Point(x, headerH), new Point(x, headerH + bodyH)); } }
        }

        private void DrawCapturePhaseLinear(DrawingContext dc, int phaseStart, int phaseLen, Brush fill, int visibleStartMin, int visibleEndMin, int captureStartMin, double cellW, double equipmentW, double y, double h)
        {
            if (phaseLen <= 0 || visibleEndMin <= visibleStartMin) return;
            int phaseEnd = phaseStart + phaseLen;
            int s = Math.Max(phaseStart, visibleStartMin); int e = Math.Min(phaseEnd, visibleEndMin); if (e <= s) return;
            double leftBase = equipmentW + ((s - captureStartMin) / 10.0) * cellW; double rightBase = equipmentW + ((e - captureStartMin) / 10.0) * cellW;
            dc.DrawRectangle(fill, null, new Rect(leftBase, y, Math.Max(0, rightBase - leftBase), h));
        }

        private void DrawCaptureBlocksForList(DrawingContext dc, IEnumerable<PlacedRecipeBlock> blocks, int startCell, int endCell, double cellW, double rowH, double headerH, double equipmentW)
        {
            int startMin = startCell * 10; int endMin = endCell * 10;
            var s2Brush = new SolidColorBrush(Color.FromRgb(248, 113, 113)); var hfBrush = new SolidColorBrush(Color.FromRgb(250, 191, 36)); var diBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));

            foreach (var block in blocks)
            {
                if (block.EquipmentIndex < 0 || block.EquipmentIndex >= _vm.Equipments.Count) continue;
                double y = headerH + RowTop(block.EquipmentIndex, rowH) + 3; double h = Math.Max(2, rowH - 6);

                int blockEnd = block.StartMinute + block.TotalMinutes;
                int visibleStart = Math.Max(block.StartMinute, startMin); int visibleEnd = Math.Min(blockEnd, endMin); if (visibleEnd <= visibleStart) continue;
                double left = equipmentW + ((visibleStart - startMin) / 10.0) * cellW; double right = equipmentW + ((visibleEnd - startMin) / 10.0) * cellW;
                var rect = new Rect(left, y, Math.Max(2, right - left), h);

                dc.PushClip(new RectangleGeometry(rect, 6, 6));
                DrawCapturePhaseLinear(dc, block.StartMinute, block.S2Minutes, s2Brush, visibleStart, visibleEnd, startMin, cellW, equipmentW, y, h);
                DrawCapturePhaseLinear(dc, block.StartMinute + block.S2Minutes, block.HFMinutes, hfBrush, visibleStart, visibleEnd, startMin, cellW, equipmentW, y, h);
                DrawCapturePhaseLinear(dc, block.StartMinute + block.S2Minutes + block.HFMinutes, block.DIMinutes, diBrush, visibleStart, visibleEnd, startMin, cellW, equipmentW, y, h);
                dc.Pop();

                if (rect.Width > 14)
                {
                    double ty = rect.Top + (rect.Height - Math.Max(10, 11 * _vm.Zoom)) / 2.0 - 2;
                    DrawTextCentered(dc, block.DisplayText, rect.Left + 1, rect.Right + 1, ty + 1, Math.Max(10, 11 * _vm.Zoom), FontWeights.Bold, new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)));
                    DrawTextCentered(dc, block.DisplayText, rect.Left, rect.Right, ty, Math.Max(10, 11 * _vm.Zoom), FontWeights.Bold, Brushes.White);
                }
            }
        }

        private void DrawText(DrawingContext dc, string text, double x, double y, double fontSize, FontWeight weight, Brush brush)
        {
            if (string.IsNullOrEmpty(text)) return;
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(x, y));
        }

        private void DrawTextCentered(DrawingContext dc, string text, double left, double right, double y, double fontSize, FontWeight weight, Brush brush)
        {
            var ft = new FormattedText(text ?? string.Empty, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double x = left + Math.Max(0, ((right - left) - ft.Width) / 2.0); dc.DrawText(ft, new Point(x, y));
        }

        private void BoardScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) { if (_isSyncingScroll) return; if (HeaderScrollViewer == null || EquipmentScrollViewer == null) return; _isSyncingScroll = true; HeaderScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset); EquipmentScrollViewer.ScrollToVerticalOffset(e.VerticalOffset); _isSyncingScroll = false; }
        private void EquipmentScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) { if (_isSyncingScroll) return; if (BoardScrollViewer == null) return; _isSyncingScroll = true; BoardScrollViewer.ScrollToVerticalOffset(e.VerticalOffset); _isSyncingScroll = false; }
        private void HeaderScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) { if (_isSyncingScroll) return; if (BoardScrollViewer == null) return; _isSyncingScroll = true; BoardScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset); _isSyncingScroll = false; }
        private void AnyScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) { if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return; e.Handled = true; double step = e.Delta > 0 ? 0.1 : -0.1; double next = Math.Max(0.6, Math.Min(2.0, _vm.Zoom + step)); _vm.Zoom = next; if (ZoomSlider != null && Math.Abs(ZoomSlider.Value - next) > 0.0001) ZoomSlider.Value = next; HideHoverCell(); DrawBoard(); }
        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!IsLoaded) return; if (Math.Abs(_vm.Zoom - e.NewValue) > 0.0001) _vm.Zoom = e.NewValue; UpdateZoomText(); HideHoverCell(); DrawBoard(); }

        private void BoardInputLayer_MouseMove(object sender, MouseEventArgs e)
        {
            if (BoardGridCanvas == null) return;
            Point p = e.GetPosition(BoardGridCanvas);
            double cellW = GetCellWidth(); double rowH = GetRowHeight();
            if (cellW <= 0 || rowH <= 0) { HideHoverCell(); return; }

            int row = (int)(p.Y / rowH);
            int minuteOffset = (int)Math.Round((p.X / cellW) * 10.0);

            if (minuteOffset < 0 || minuteOffset >= _vm.TotalMinutes || row < 0 || row >= _vm.Equipments.Count) { HideHoverCell(); _vm.HoverText = string.Empty; return; }

            if (EnableHoverCellHighlight)
            {
                double boardW = BoardBlocksCanvas?.Width ?? 0; double boardH = BoardBlocksCanvas?.Height ?? 0;
                double y = RowTop(row, rowH);
                double xExact = (minuteOffset / 10.0) * cellW;

                if (_hoverColumnRect != null) { _hoverColumnRect.Width = cellW / 10.0; _hoverColumnRect.Height = boardH; Canvas.SetLeft(_hoverColumnRect, xExact); Canvas.SetTop(_hoverColumnRect, 0); _hoverColumnRect.Visibility = Visibility.Visible; }
                if (_hoverRowRect != null) { _hoverRowRect.Width = boardW; _hoverRowRect.Height = rowH; Canvas.SetLeft(_hoverRowRect, 0); Canvas.SetTop(_hoverRowRect, y); _hoverRowRect.Visibility = Visibility.Visible; }
                if (_hoverCellRect != null) { _hoverCellRect.Width = cellW; _hoverCellRect.Height = rowH - 4; Canvas.SetLeft(_hoverCellRect, (minuteOffset / 10) * cellW); Canvas.SetTop(_hoverCellRect, y + 2); _hoverCellRect.Visibility = Visibility.Visible; }
                if (_hoverEquipmentRect != null) { _hoverEquipmentRect.Width = _vm.EquipmentColumnWidth; _hoverEquipmentRect.Height = rowH; Canvas.SetLeft(_hoverEquipmentRect, 0); Canvas.SetTop(_hoverEquipmentRect, y); _hoverEquipmentRect.Visibility = Visibility.Visible; }
            }

            int absoluteMinutes = BoardStartHour * 60 + minuteOffset;
            int hour = (absoluteMinutes / 60) % 24;
            int minute = absoluteMinutes % 60;
            _vm.HoverText = $"{_vm.Equipments[row].DisplayName} | {hour:00}:{minute:00}";
        }

        private void BoardInputLayer_MouseLeave(object sender, MouseEventArgs e) { HideHoverCell(); _vm.HoverText = string.Empty; }

        private void BoardInputLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (BoardGridCanvas == null) return;
            Point p = e.GetPosition(BoardGridCanvas);
            double cellW = GetCellWidth(); double rowH = GetRowHeight();
            if (cellW <= 0 || rowH <= 0) return;

            int row = (int)(p.Y / rowH);
            int startMinute = (int)Math.Round((p.X / cellW) * 10.0);

            if (startMinute < 0 || startMinute >= _vm.TotalMinutes || row < 0 || row >= _vm.Equipments.Count) return;

            _vm.LastClickedEquipmentName = _vm.Equipments[row].DisplayName;
            _vm.SetSelectedCell(row, startMinute);

            if (_vm.SelectedRecipe == null)
            {
                _vm.StatusText = $"시간 선택: {_vm.LastClickedEquipmentName} / {_vm.GetCellTimeText(startMinute)}";
                UpdateStatusText(); e.Handled = true; return;
            }

            bool placed = _vm.TryPlaceRecipe(row, startMinute, out string msg);
            if (placed) { HideHoverCell(); DrawBoard(); }
            else if (msg.Contains("DI 동시 배치", StringComparison.Ordinal)) MessageBox.Show(msg, "DI 배치 제한", MessageBoxButton.OK, MessageBoxImage.Warning);

            _vm.StatusText = msg; UpdateStatusText(); e.Handled = true;
        }

        private void BoardInputLayer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (BoardGridCanvas == null) return;
            Point p = e.GetPosition(BoardGridCanvas);
            double cellW = GetCellWidth(); double rowH = GetRowHeight();
            if (cellW <= 0 || rowH <= 0) return;

            int row = (int)(p.Y / rowH);
            int clickMinute = (int)Math.Round((p.X / cellW) * 10.0);

            if (clickMinute < 0 || clickMinute >= _vm.TotalMinutes || row < 0 || row >= _vm.Equipments.Count) return;

            _vm.LastClickedEquipmentName = _vm.Equipments[row].DisplayName;
            _vm.SetSelectedCell(row, clickMinute);

            if (_vm.TryRemoveBlockAt(row, clickMinute, out string msg)) { HideHoverCell(); DrawBoard(); }
            _vm.StatusText = msg; UpdateStatusText(); e.Handled = true;
        }

        private string FormatDateRanges(List<DateTime> sorted, string[] krDowShort)
        {
            if (sorted.Count == 0) return "";

            var parts = new List<string>();
            int i = 0;
            while (i < sorted.Count)
            {
                int start = i;
                while (i + 1 < sorted.Count && (sorted[i + 1] - sorted[i]).Days == 1)
                    i++;

                var first = sorted[start];
                var last = sorted[i];

                if (start == i)
                    parts.Add($"{first.Month}-{first.Day} ({krDowShort[(int)first.DayOfWeek]})");
                else
                    parts.Add($"{first.Month}-{first.Day}~{last.Day} ({krDowShort[(int)first.DayOfWeek]}~{krDowShort[(int)last.DayOfWeek]})");

                i++;
            }
            return string.Join(", ", parts);
        }
    }
}