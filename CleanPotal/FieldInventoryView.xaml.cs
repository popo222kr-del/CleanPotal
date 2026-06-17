using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CleanPotal.FieldInventory.Models;
using CleanPotal.FieldInventory.Repositories;
using ClosedXML.Excel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Win32;
using SkiaSharp;

namespace CleanPotal
{
    public partial class FieldInventoryView : UserControl
    {
        private readonly ObservableCollection<FieldInventoryItem> _items = new();
        private List<FieldInventoryItem> _filtered = new();
        private bool _sortDescending = true;
        private FieldInventoryItem? _editingItem;
        private string _mode = "view";   // "view"=재고 현황, "analysis"=재고 분석, "manage"=재고 리스트 관리
        private bool _editMode => _mode == "manage";

        // 점검일자 필터는 '조회' 버튼을 눌렀을 때만 확정 적용 (선택 즉시 반영 안 함)
        private DateTime? _filterFrom;
        private DateTime? _filterTo;

        // 엑셀 업로드 임시 반영(스테이징) 상태 — '업로드 확정' 전까지 DB 미저장
        private bool _hasStagedUpload = false;
        private readonly HashSet<long> _stagedItems = new();

        private DataGrid[] AllGrids => new[] { DgMetal, DgNonmetal, DgOffice, DgCleaning };

        public FieldInventoryView()
        {
            InitializeComponent();
            foreach (var g in AllGrids)
                BuildColumns(g);
            ApplyEditMode();
            Loaded += (s, e) => Load();
        }

        private void BuildColumns(DataGrid g)
        {
            g.Columns.Clear();

            g.Columns.Add(new DataGridCheckBoxColumn
            {
                Binding = new System.Windows.Data.Binding("IsSelected") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(36)
            });

            g.Columns.Add(CreateDatePickerColumn("발주일", "OrderDate", 110));
            g.Columns.Add(CreateDatePickerColumn("입고 예정일", "ExpectedReceipt", 120));

            var catTemplate = new DataGridTemplateColumn
            {
                Header = "카테고리",
                Width = new DataGridLength(90),
                SortMemberPath = "Category"
            };
            catTemplate.CellTemplate = CreateCategoryDisplayTemplate();
            catTemplate.CellEditingTemplate = CreateCategoryEditTemplate();
            g.Columns.Add(catTemplate);

            AddTextColumn(g, "품목명", "ItemName", 0, "LeftCell", minWidth: 120, star: true);
            AddTextColumn(g, "현재 재고", "CurrentStock", 80, "CurrentStockCell", updateSourceTrigger: true);
            AddTextColumn(g, "이전 재고", "PreviousStock", 70, "CenterCell", readOnly: true);
            AddTextColumn(g, "이전 대비", "WeeklyDeltaText", 70, "WeeklyDeltaCell", readOnly: true);
            AddTextColumn(g, "안전재고", "AppropriateStock", 75, "CenterCell");
            AddTextColumn(g, "단위", "Unit", 50, "CenterCell");
            AddTextColumn(g, "품목코드", "ItemCode", 90, "ItemCodeCell");

            var regCol = new DataGridTextColumn
            {
                Header = "등록일자",
                Binding = new System.Windows.Data.Binding("RegisteredDate") { StringFormat = "yyyy-MM-dd" },
                Width = new DataGridLength(95),
                IsReadOnly = true,
                CanUserSort = true,
                SortMemberPath = "RegisteredDate"
            };
            regCol.ElementStyle = (Style)FindResource("CenterCell");
            g.Columns.Add(regCol);

            var actionCol = new DataGridTemplateColumn
            {
                Header = "작업",
                Width = new DataGridLength(80),
                IsReadOnly = true,
                CellTemplate = CreateActionTemplate()
            };
            g.Columns.Add(actionCol);
        }

        private void AddTextColumn(DataGrid g, string header, string bindingPath, double width, string styleKey,
            bool readOnly = false, double minWidth = 0, bool star = false, bool updateSourceTrigger = false)
        {
            var binding = new System.Windows.Data.Binding(bindingPath);
            if (updateSourceTrigger)
                binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;

            var col = new DataGridTextColumn
            {
                Header = header,
                Binding = binding,
                Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
                IsReadOnly = readOnly
            };
            if (minWidth > 0) col.MinWidth = minWidth;
            col.ElementStyle = (Style)FindResource(styleKey);
            g.Columns.Add(col);
        }

        private DataTemplate CreateCategoryDisplayTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(FrameworkElement.StyleProperty, FindResource("CategoryBadge"));
            factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var txt = new FrameworkElementFactory(typeof(TextBlock));
            txt.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Category"));
            txt.SetValue(TextBlock.FontSizeProperty, 12.0);
            txt.SetValue(TextBlock.ForegroundProperty, (Brush)new BrushConverter().ConvertFromString("#475569")!);
            factory.AppendChild(txt);
            return new DataTemplate { VisualTree = factory };
        }

        private DataTemplate CreateCategoryEditTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(TextBox));
            factory.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Category") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            factory.SetValue(TextBox.FontSizeProperty, 12.0);
            factory.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            factory.SetValue(Control.BorderThicknessProperty, new Thickness(1));
            factory.SetValue(Control.BorderBrushProperty, (Brush)new BrushConverter().ConvertFromString("#2563EB")!);
            return new DataTemplate { VisualTree = factory };
        }

        private DataTemplate CreateActionTemplate()
        {
            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            sp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);

            var btnEdit = new FrameworkElementFactory(typeof(Button));
            btnEdit.SetValue(ContentControl.ContentProperty, "수정");
            btnEdit.SetValue(FrameworkElement.StyleProperty, FindResource("LinkBtn"));
            btnEdit.SetValue(Control.ForegroundProperty, (Brush)new BrushConverter().ConvertFromString("#2563EB")!);
            btnEdit.AddHandler(Button.ClickEvent, new RoutedEventHandler(BtnEditItem_Click));
            sp.AppendChild(btnEdit);

            var btnDel = new FrameworkElementFactory(typeof(Button));
            btnDel.SetValue(ContentControl.ContentProperty, "삭제");
            btnDel.SetValue(FrameworkElement.StyleProperty, FindResource("LinkBtn"));
            btnDel.SetValue(Control.ForegroundProperty, (Brush)new BrushConverter().ConvertFromString("#DC2626")!);
            btnDel.AddHandler(Button.ClickEvent, new RoutedEventHandler(BtnDeleteItem_Click));
            sp.AppendChild(btnDel);

            return new DataTemplate { VisualTree = sp };
        }

        private readonly StringDateConverter _dateConv = new();

        // 평소에는 날짜 텍스트만 표시(깔끔), 셀을 클릭해 편집할 때만 DatePicker 노출.
        // 편집용 DatePicker의 SelectedDate를 양방향+PropertyChanged 컨버터로 바인딩해
        // 달력 팝업에서 고르는 즉시 모델에 반영 → 편집이 취소돼도 값 누락 없음.
        private DataGridTemplateColumn CreateDatePickerColumn(string header, string bindingPath, double width)
        {
            // 표시 템플릿: 텍스트
            var displayFactory = new FrameworkElementFactory(typeof(TextBlock));
            displayFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(bindingPath));
            displayFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            displayFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            displayFactory.SetValue(TextBlock.ForegroundProperty, (Brush)new BrushConverter().ConvertFromString("#334155")!);

            // 편집 템플릿: DatePicker
            var editFactory = new FrameworkElementFactory(typeof(DatePicker));
            editFactory.SetBinding(DatePicker.SelectedDateProperty, new System.Windows.Data.Binding(bindingPath)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Converter = _dateConv
            });
            editFactory.SetValue(DatePicker.FontSizeProperty, 12.0);
            editFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            editFactory.AddHandler(DatePicker.SelectedDateChangedEvent,
                new EventHandler<SelectionChangedEventArgs>(DateCell_SelectedDateChanged));

            return new DataGridTemplateColumn
            {
                Header = header,
                Width = new DataGridLength(width),
                CellTemplate = new DataTemplate { VisualTree = displayFactory },
                CellEditingTemplate = new DataTemplate { VisualTree = editFactory }
            };
        }

        // 셀 내 DatePicker에서 날짜를 선택하면 모델에 반영된 뒤 즉시 DB 저장
        private void DateCell_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not DatePicker dp || dp.DataContext is not FieldInventoryItem item) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    FieldInventoryRepository.Update(item);
                    TxtLastSaved.Text = $"저장됨 {DateTime.Now:HH:mm:ss}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"저장 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // -----------------------------------------------------------------------
        // 화면 모드 전환 — '재고 현황'(조회 전용) / '재고 리스트 관리'(편집)
        // -----------------------------------------------------------------------
        private void ModeTab_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;
            if (tag == _mode) return;
            if (tag is "analysis" or "manage" && !AuthManager.CheckAuth(PermissionType.InventoryManage)) return;
            _mode = tag;
            ApplyEditMode();
            if (_mode == "analysis") LoadAnalysisDashboard();
        }

        private void ApplyEditMode()
        {
            var blue = (Brush)new BrushConverter().ConvertFromString("#2563EB")!;
            var gray = (Brush)new BrushConverter().ConvertFromString("#64748B")!;

            // 3탭 스타일 적용
            foreach (var (border, text, tag) in new[]
            {
                (ModeTabView, TxtModeView, "view"),
                (ModeTabAnalysis, TxtModeAnalysis, "analysis"),
                (ModeTabManage, TxtModeManage, "manage")
            })
            {
                bool active = _mode == tag;
                border.Background = active ? blue : Brushes.Transparent;
                text.Foreground = active ? Brushes.White : gray;
                text.FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
            }

            // 재고 분석 패널 표시/숨김
            PanelAnalysis.Visibility = _mode == "analysis" ? Visibility.Visible : Visibility.Collapsed;

            // 기존 패널(통계/툴바/그리드) — 분석 모드에서는 숨김
            var gridVis = _mode != "analysis" ? Visibility.Visible : Visibility.Collapsed;
            PanelStats.Visibility = gridVis;
            PanelToolbar.Visibility = gridVis;
            PanelZoneGrid.Visibility = gridVis;

            var editVis = _editMode ? Visibility.Visible : Visibility.Collapsed;

            foreach (var g in AllGrids)
                ConfigureColumns(g);

            // 주간 마감 / 엑셀 업로드: 재고 현황(조회) 모드에서만 노출
            BtnWeeklyClose.Visibility = _editMode ? Visibility.Collapsed : Visibility.Visible;
            BtnImportExcel.Visibility = _editMode ? Visibility.Collapsed : Visibility.Visible;

            // 편집 기능 버튼: 관리 모드에서만 노출
            BtnDeleteRow.Visibility = editVis;
            BtnManageLocation.Visibility = editVis;
            BtnAddRow.Visibility = editVis;
            BtnBatchEdit.Visibility = editVis;

            RefreshUploadButtons();
        }

        // 업로드 임시 반영 상태에서만 '업로드 확정' 버튼 노출
        private void RefreshUploadButtons()
        {
            BtnConfirmUpload.Visibility = (!_editMode && _hasStagedUpload)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // 컬럼 순서: 0 선택, 1 발주일, 2 입고예정일, 3 카테고리, 4 품목명, 5 현재 재고,
        //            6 이전 재고, 7 이전 대비, 8 안전재고, 9 단위, 10 품목코드, 11 등록일자, 12 작업
        private void ConfigureColumns(DataGrid g)
        {
            if (g == null || g.Columns.Count < 13) return;
            g.IsReadOnly = false;

            var editVis = _editMode ? Visibility.Visible : Visibility.Collapsed;
            var viewVis = _editMode ? Visibility.Collapsed : Visibility.Visible;  // 조회 모드 전용
            bool ro = !_editMode;
            var c = g.Columns;

            c[0].Visibility = editVis;   // 선택 (관리 모드 전용)
            c[1].Visibility = viewVis;   // 발주일      — 조회 모드 전용 (클릭 시 DatePicker 편집)
            c[1].IsReadOnly = false;
            c[2].Visibility = viewVis;   // 입고 예정일 — 조회 모드 전용 (클릭 시 DatePicker 편집)
            c[2].IsReadOnly = false;
            c[3].Visibility = editVis;   // 카테고리    — 관리 모드 전용
            c[3].IsReadOnly = ro;
            c[4].IsReadOnly = ro;        // 품목명
            c[4].Width = _editMode ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(1, DataGridLengthUnitType.Star);
            c[4].MinWidth = _editMode ? 120 : 180;
            c[5].IsReadOnly = false;     // 현재 재고   — 항상 수정 가능
            c[6].Visibility = viewVis;   // 이전 재고   — 조회 모드 전용
            c[6].Width = new DataGridLength(80);
            c[7].Visibility = viewVis;   // 이전 대비   — 조회 모드 전용
            c[7].Width = new DataGridLength(80);
            c[8].IsReadOnly = ro;        // 안전재고
            c[9].Visibility = editVis;   // 단위        — 관리 모드 전용
            c[9].IsReadOnly = ro;
            c[10].Visibility = editVis;  // 품목코드    — 관리 모드 전용
            c[10].IsReadOnly = ro;
            c[11].Visibility = editVis;  // 등록일자 (관리 모드 전용)
            c[12].Visibility = editVis;  // 작업     (관리 모드 전용)
        }

        private enum Zone { Metal, Nonmetal, Office, Cleaning }

        private static Zone ClassifyZone(string location)
        {
            string s = location ?? "";
            if (s.Contains("논메탈")) return Zone.Nonmetal;
            if (s.Contains("메탈") || s.Contains("반입구")) return Zone.Metal;
            if (s.Contains("OFFICE", StringComparison.OrdinalIgnoreCase)) return Zone.Office;
            return Zone.Cleaning;
        }

        private void Load()
        {
            try
            {
                var all = FieldInventoryRepository.GetAll();
                var snapshot = FieldInventoryRepository.GetLatestSnapshotStocks();
                _items.Clear();
                foreach (var i in all)
                {
                    i.PreviousStock = snapshot.TryGetValue(i.ItemId, out var s) ? s : "";
                    _items.Add(i);
                }

                ApplyFilters();
                RefreshStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"재고 목록을 불러오지 못했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshStats()
        {
            int total = _items.Count;
            int low = _items.Count(i => i.IsLow);
            StatTotalText.Text = total.ToString();
            StatLowText.Text = low.ToString();

            var latest = _items.Where(i => i.UpdatedAt != default).OrderByDescending(i => i.UpdatedAt).FirstOrDefault();
            StatUpdatedText.Text = latest != null ? latest.UpdatedAt.ToString("yyyy-MM-dd HH:mm") : "-";

            var snapDate = FieldInventoryRepository.GetLatestSnapshotDate();
            TxtLastSnapshot.Text = snapDate.HasValue ? $"이전 마감: {snapDate.Value:yyyy-MM-dd}" : "이전 마감 기록 없음";
        }

        // -----------------------------------------------------------------------
        // 재고 분석 대시보드
        // -----------------------------------------------------------------------
        private static double ParseStockNumber(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            string cleaned = s.Trim().Replace(",", "");
            var m = System.Text.RegularExpressions.Regex.Match(cleaned, @"^(\d+(?:\.\d+)?)");
            return m.Success && double.TryParse(m.Groups[1].Value, out double v) ? v : 0;
        }

        // 점검일자(마감) 기간 필터 — DateSearch에서 설정한 범위 적용 (없으면 전체)
        private bool SnapshotInRange(string dateStr)
        {
            if (!DateTime.TryParse(dateStr, out var d)) return false;
            if (_filterFrom.HasValue && d.Date < _filterFrom.Value.Date) return false;
            if (_filterTo.HasValue && d.Date > _filterTo.Value.Date) return false;
            return true;
        }

        // 추이 차트 렌더링 — item이 null이면 구역별 합산, 아니면 선택 품목의 재고 추이
        private void RenderStockTrend(FieldInventoryItem? item)
        {
            var snapDates = FieldInventoryRepository.GetSnapshotDates().Where(SnapshotInRange).ToList();
            var allSnapshots = FieldInventoryRepository.GetAllSnapshots().Where(s => SnapshotInRange(s.Date)).ToList();
            var snapshotByDate = allSnapshots.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.ToList());

            var xLabels = snapDates.Select(d => DateTime.TryParse(d, out var dt) ? dt.ToString("MM/dd") : d).ToArray();

            if (item != null)
            {
                // ---- 선택 품목의 재고 추이 ----
                TxtTrendTitle.Text = $"📈 {item.ItemName} — 재고 추이";

                if (snapDates.Count >= 1)
                {
                    TxtNoTrendData.Visibility = Visibility.Collapsed;
                    var values = snapDates.Select(date =>
                    {
                        var snaps = snapshotByDate.GetValueOrDefault(date) ?? new();
                        var hit = snaps.FirstOrDefault(s => s.ItemId == item.ItemId);
                        return ParseStockNumber(hit.Stock);
                    }).ToList();

                    var color = new SKColor(37, 99, 235);
                    ChartStockTrend.Series = new ISeries[]
                    {
                        new LineSeries<double>
                        {
                            Name = item.ItemName,
                            Values = values,
                            Stroke = new SolidColorPaint(color, 3),
                            GeometryStroke = new SolidColorPaint(color, 3),
                            GeometrySize = 8,
                            Fill = new SolidColorPaint(new SKColor(37, 99, 235, 30))
                        }
                    };
                    ChartStockTrend.XAxes = new[] { new Axis { Labels = xLabels, TextSize = 11 } };
                    ChartStockTrend.YAxes = new[] { new Axis { TextSize = 11, MinLimit = 0 } };
                }
                else
                {
                    TxtNoTrendData.Visibility = Visibility.Visible;
                    ChartStockTrend.Series = Array.Empty<ISeries>();
                }
                return;
            }

            // ---- 구역별 합산 추이 (기본) ----
            TxtTrendTitle.Text = "주간 재고 추이 (구역별 · 행 클릭 시 품목별)";

            if (snapDates.Count >= 2)
            {
                TxtNoTrendData.Visibility = Visibility.Collapsed;
                var itemLocMap = _items.ToDictionary(i => i.ItemId, i => ClassifyZone(i.StorageLocation));

                var zoneSeries = new[] {
                    (Zone.Metal, "METAL 반입구", new SKColor(29, 78, 216)),
                    (Zone.Nonmetal, "N-METAL 출고실", new SKColor(190, 24, 93)),
                    (Zone.Office, "Office 보관", new SKColor(21, 128, 61)),
                    (Zone.Cleaning, "세정랩", new SKColor(109, 40, 217))
                };

                var series = new List<ISeries>();
                foreach (var (zone, label, color) in zoneSeries)
                {
                    var values = snapDates.Select(date =>
                    {
                        var snaps = snapshotByDate.GetValueOrDefault(date) ?? new();
                        return snaps.Where(s => itemLocMap.TryGetValue(s.ItemId, out var z) && z == zone)
                            .Sum(s => ParseStockNumber(s.Stock));
                    }).ToList();
                    series.Add(new LineSeries<double>
                    {
                        Name = label,
                        Values = values,
                        Stroke = new SolidColorPaint(color, 2),
                        GeometryStroke = new SolidColorPaint(color, 2),
                        GeometrySize = 6,
                        Fill = null
                    });
                }

                ChartStockTrend.Series = series;
                ChartStockTrend.XAxes = new[] { new Axis { Labels = xLabels, TextSize = 11 } };
                ChartStockTrend.YAxes = new[] { new Axis { TextSize = 11, MinLimit = 0 } };
            }
            else
            {
                TxtNoTrendData.Visibility = Visibility.Visible;
                ChartStockTrend.Series = Array.Empty<ISeries>();
            }
        }

        // 품목 상세 그리드에서 행을 선택하면 추이 차트를 해당 품목으로 전환
        private void DgAnalysisDetail_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RenderStockTrend(DgAnalysisDetail.SelectedItem as FieldInventoryItem);
        }

        // 품목 상세 조회 초기화 — 검색어·선택 해제 후 추이 차트를 구역별 기본 보기로 복귀
        private void BtnAnalysisDetailReset_Click(object sender, RoutedEventArgs e)
        {
            TxtAnalysisSearch.Text = "";
            DgAnalysisDetail.UnselectAll();
            DgAnalysisDetail.SelectedItem = null;
            RefreshAnalysisDetail();
            RenderStockTrend(null);
        }

        private void LoadAnalysisDashboard()
        {
            try
            {
                var snapDates = FieldInventoryRepository.GetSnapshotDates().Where(SnapshotInRange).ToList();
                int lowCount = _items.Count(i => i.IsLow);
                int totalItems = _items.Count;
                var zones = _items.Select(i => ClassifyZone(i.StorageLocation)).Distinct().Count();

                // 기간 표시
                string rangeLabel = (_filterFrom.HasValue || _filterTo.HasValue)
                    ? $"{(_filterFrom?.ToString("yyyy-MM-dd") ?? "처음")} ~ {(_filterTo?.ToString("yyyy-MM-dd") ?? "최근")}"
                    : "전체 기간";
                TxtAnalysisRange.Text = rangeLabel;

                // 요약 카드
                AnalSnapCount.Text = $"{snapDates.Count}회";
                AnalLowCount.Text = $"{lowCount}개";
                AnalTotalItems.Text = $"{totalItems}개";
                AnalZoneCount.Text = $"{zones}개";

                // ----- 1. 주간 재고 추이 (구역별 총 재고 합산, 또는 선택 품목) -----
                RenderStockTrend(DgAnalysisDetail?.SelectedItem as FieldInventoryItem);

                // ----- 2. 구역별 품목 분포 (파이 차트) -----
                var zoneGroups = _items.GroupBy(i => ClassifyZone(i.StorageLocation));
                var pieColors = new Dictionary<Zone, SKColor> {
                    [Zone.Metal] = new(59, 130, 246),
                    [Zone.Nonmetal] = new(236, 72, 153),
                    [Zone.Office] = new(34, 197, 94),
                    [Zone.Cleaning] = new(139, 92, 246)
                };
                var zoneNames = new Dictionary<Zone, string> {
                    [Zone.Metal] = "METAL 반입구",
                    [Zone.Nonmetal] = "N-METAL 출고실",
                    [Zone.Office] = "Office 보관",
                    [Zone.Cleaning] = "세정랩"
                };

                ChartZoneDist.Series = zoneGroups.Select(g => new PieSeries<double>
                {
                    Name = zoneNames.GetValueOrDefault(g.Key, "기타"),
                    Values = new[] { (double)g.Count() },
                    Fill = new SolidColorPaint(pieColors.GetValueOrDefault(g.Key, new SKColor(148, 163, 184))),
                    DataLabelsSize = 12,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                    DataLabelsFormatter = p => $"{p.Coordinate.PrimaryValue:0}"
                }).ToArray();

                // ----- 3. 구역별 부족 vs 정상 (스택 바 차트) -----
                var zoneLabels = new[] { "METAL", "N-METAL", "Office", "세정랩" };
                var zoneEnums = new[] { Zone.Metal, Zone.Nonmetal, Zone.Office, Zone.Cleaning };
                var lowValues = new List<double>();
                var okValues = new List<double>();
                foreach (var z in zoneEnums)
                {
                    var zoneItems = _items.Where(i => ClassifyZone(i.StorageLocation) == z).ToList();
                    int lo = zoneItems.Count(i => i.IsLow);
                    lowValues.Add(lo);
                    okValues.Add(zoneItems.Count - lo);
                }

                ChartZoneLow.Series = new ISeries[]
                {
                    new StackedColumnSeries<double>
                    {
                        Name = "부족",
                        Values = lowValues,
                        Fill = new SolidColorPaint(new SKColor(239, 68, 68)),
                        MaxBarWidth = 40
                    },
                    new StackedColumnSeries<double>
                    {
                        Name = "정상",
                        Values = okValues,
                        Fill = new SolidColorPaint(new SKColor(34, 197, 94)),
                        MaxBarWidth = 40
                    }
                };
                ChartZoneLow.XAxes = new[] { new Axis { Labels = zoneLabels, TextSize = 11 } };
                ChartZoneLow.YAxes = new[] { new Axis { TextSize = 11, MinLimit = 0 } };

                // ----- 4. 소비량 Top 10 (이전 대비 감소) -----
                var consumItems = _items.Where(i => i.WeeklyDelta is double d && d < 0)
                    .OrderBy(i => i.WeeklyDelta)
                    .Take(10)
                    .ToList();

                if (consumItems.Any())
                {
                    TxtNoConsumData.Visibility = Visibility.Collapsed;
                    ChartTopConsumption.Series = new ISeries[]
                    {
                        new RowSeries<double>
                        {
                            Name = "감소량",
                            Values = consumItems.Select(i => Math.Abs(i.WeeklyDelta!.Value)).ToArray(),
                            Fill = new SolidColorPaint(new SKColor(239, 68, 68)),
                            MaxBarWidth = 24
                        }
                    };
                    ChartTopConsumption.YAxes = new[] { new Axis {
                        Labels = consumItems.Select(i => i.ItemName.Length > 12 ? i.ItemName[..12] + "…" : i.ItemName).ToArray(),
                        TextSize = 11
                    }};
                    ChartTopConsumption.XAxes = new[] { new Axis { TextSize = 11, MinLimit = 0 } };
                }
                else
                {
                    TxtNoConsumData.Visibility = Visibility.Visible;
                    ChartTopConsumption.Series = Array.Empty<ISeries>();
                }

                // ----- 5. 품목 상세 조회 목록 -----
                RefreshAnalysisDetail();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"분석 데이터 로드 중 오류:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -----------------------------------------------------------------------
        // 필터 / 정렬
        // -----------------------------------------------------------------------
        private void ApplyFilters()
        {
            if (DgMetal == null) return;

            IEnumerable<FieldInventoryItem> source = _items;

            string keyword = TxtSearch.Text.Trim();
            if (!string.IsNullOrEmpty(keyword))
            {
                source = source.Where(i =>
                    i.ItemName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    i.ItemCode.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    i.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = source.OrderBy(i => i.StorageLocation);
            source = _sortDescending
                ? ordered.ThenByDescending(i => i.RegisteredDate).ThenByDescending(i => i.OrderNo)
                : ordered.ThenBy(i => i.RegisteredDate).ThenBy(i => i.OrderNo);

            _filtered = source.ToList();
            RenderList();
        }

        private void RenderList()
        {
            var grouped = _filtered.GroupBy(i => ClassifyZone(i.StorageLocation))
                .ToDictionary(g => g.Key, g => g.ToList());

            var metal = grouped.GetValueOrDefault(Zone.Metal) ?? new List<FieldInventoryItem>();
            var nonmetal = grouped.GetValueOrDefault(Zone.Nonmetal) ?? new List<FieldInventoryItem>();
            var office = grouped.GetValueOrDefault(Zone.Office) ?? new List<FieldInventoryItem>();
            var cleaning = grouped.GetValueOrDefault(Zone.Cleaning) ?? new List<FieldInventoryItem>();

            DgMetal.ItemsSource = metal;
            DgNonmetal.ItemsSource = nonmetal;
            DgOffice.ItemsSource = office;
            DgCleaning.ItemsSource = cleaning;

            TxtMetalCount.Text = $"{metal.Count}개";
            TxtNonmetalCount.Text = $"{nonmetal.Count}개";
            TxtOfficeCount.Text = $"{office.Count}개";
            TxtCleaningCount.Text = $"{cleaning.Count}개";
            TxtTotalCount.Text = $"총 {_filtered.Count}개";

            // 구역 카드 제목을 실제 위치 이름으로 동적 표시 (이름 변경 즉시 반영)
            TxtMetalTitle.Text = ZoneTitle(metal, "METAL 반입구");
            TxtNonmetalTitle.Text = ZoneTitle(nonmetal, "N-METAL 출고실");
            TxtOfficeTitle.Text = ZoneTitle(office, "Office 보관");
            TxtCleaningTitle.Text = ZoneTitle(cleaning, "세정랩");
        }

        // 구역 내 실제 위치명을 제목으로 사용. 비어 있으면 기본 라벨 유지,
        // 여러 위치가 섞여 있으면 " / "로 연결해 표시.
        private static string ZoneTitle(List<FieldInventoryItem> zoneItems, string fallback)
        {
            var names = zoneItems.Select(i => i.StorageLocation)
                                 .Where(s => !string.IsNullOrWhiteSpace(s))
                                 .Distinct().ToList();
            return names.Count == 0 ? fallback : string.Join(" / ", names);
        }

        // 위치명에 따른 (배경색, 글자색) 매핑
        public static (string Bg, string Fg) GetLocationColors(string location)
        {
            if (location.Contains("논메탈")) return ("#FCE7F3", "#BE185D");
            if (location.Contains("메탈")) return ("#DBEAFE", "#1D4ED8");
            if (location.Contains("세정")) return ("#EDE9FE", "#6D28D9");
            if (location.Contains("OFFICE", StringComparison.OrdinalIgnoreCase)) return ("#DCFCE7", "#15803D");
            return ("#F1F5F9", "#475569");
        }

        // 구역 그리드 내부 스크롤이 끝(또는 스크롤 불필요)이면 휠 입력을 바깥 ZoneScroll로 전달
        // → 그리드 위에서 휠을 굴려도 오피스·세정랩 구역으로 스크롤 가능
        private void DgZone_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DependencyObject dep) return;
            var inner = FindDescendantScrollViewer(dep);

            bool forward = inner == null || inner.ScrollableHeight == 0
                || (e.Delta > 0 && inner.VerticalOffset <= 0)
                || (e.Delta < 0 && inner.VerticalOffset >= inner.ScrollableHeight);

            if (forward && ZoneScroll != null)
            {
                ZoneScroll.ScrollToVerticalOffset(ZoneScroll.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv) return sv;
                var found = FindDescendantScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtSearchPlaceholder != null)
                TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilters();
        }

        // 재고 분석: 선택한 점검일자(마감) 범위로 대시보드를 다시 집계
        private void BtnDateSearch_Click(object sender, RoutedEventArgs e)
        {
            _filterFrom = DpFrom.SelectedDate;
            _filterTo = DpTo.SelectedDate;
            LoadAnalysisDashboard();
        }

        // 재고 분석: 기간 필터 해제 → 전체 기간 집계
        private void BtnAnalysisReset_Click(object sender, RoutedEventArgs e)
        {
            DpFrom.SelectedDate = null;
            DpTo.SelectedDate = null;
            _filterFrom = null;
            _filterTo = null;
            LoadAnalysisDashboard();
        }

        // 재고 분석: 품목 상세 조회 검색 (품목명·코드)
        private void TxtAnalysisSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtAnalysisSearchPlaceholder != null)
                TxtAnalysisSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtAnalysisSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
            RefreshAnalysisDetail();
        }

        private void RefreshAnalysisDetail()
        {
            if (DgAnalysisDetail == null) return;
            string keyword = TxtAnalysisSearch.Text.Trim();
            IEnumerable<FieldInventoryItem> source = _items;
            if (!string.IsNullOrEmpty(keyword))
            {
                source = source.Where(i =>
                    i.ItemName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    i.ItemCode.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }
            DgAnalysisDetail.ItemsSource = source.OrderBy(i => i.StorageLocation).ThenBy(i => i.OrderNo).ToList();
        }

        // 점검일자 앞(시작) 날짜를 고르면 뒤(종료) 날짜를 우선 동일하게 채움
        // (비어 있거나 시작보다 이전일 때만 — 사용자가 범위를 넓히는 건 그대로 허용)
        private void DpFrom_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DpFrom.SelectedDate is DateTime from
                && (DpTo.SelectedDate == null || DpTo.SelectedDate < from))
            {
                DpTo.SelectedDate = from;
            }
        }

        private void DgInventory_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (e.Column.SortMemberPath != nameof(FieldInventoryItem.RegisteredDate))
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
            _sortDescending = e.Column.SortDirection != ListSortDirection.Descending;
            e.Column.SortDirection = _sortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            ApplyFilters();
        }

        // -----------------------------------------------------------------------
        // 일괄 수정
        // -----------------------------------------------------------------------
        private List<FieldInventoryItem> _batchTargets = new();

        private void BtnBatchEdit_Click(object sender, RoutedEventArgs e)
        {
            _batchTargets = _items.Where(i => i.IsSelected).ToList();
            if (!_batchTargets.Any())
            {
                MessageBox.Show("일괄 수정할 항목을 체크박스로 선택해주세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            TxtBatchCount.Text = $"({_batchTargets.Count}개 선택)";
            DgBatchPreview.ItemsSource = _batchTargets;
            CmbBatchField.SelectedIndex = 0;
            TxtBatchValue.Text = "";
            TxtBatchValue.Visibility = Visibility.Visible;
            LblBatchValue.Visibility = Visibility.Visible;
            BatchOverlay.Visibility = Visibility.Visible;
            TxtBatchValue.Focus();
        }

        private void BtnCloseBatch_Click(object sender, RoutedEventArgs e)
        {
            BatchOverlay.Visibility = Visibility.Collapsed;
            _batchTargets.Clear();
        }

        private void BtnApplyBatch_Click(object sender, RoutedEventArgs e)
        {
            if (!_batchTargets.Any()) return;

            int fieldIdx = CmbBatchField.SelectedIndex;
            string val = TxtBatchValue.Text.Trim();

            // 발주 완료/해제는 값 입력 불필요
            if (fieldIdx < 4 && string.IsNullOrEmpty(val))
            {
                MessageBox.Show("변경할 값을 입력해주세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                foreach (var item in _batchTargets)
                {
                    switch (fieldIdx)
                    {
                        case 0: item.CurrentStock = val; break;
                        case 1: item.AppropriateStock = val; break;
                        case 2: item.Unit = val; break;
                        case 3: item.Category = val; break;
                        case 4: item.IsOrdered = true; break;
                        case 5: item.IsOrdered = false; break;
                    }
                    FieldInventoryRepository.Update(item);
                    item.IsSelected = false;
                }

                BatchOverlay.Visibility = Visibility.Collapsed;
                _batchTargets.Clear();
                TxtLastSaved.Text = $"일괄 저장됨 {DateTime.Now:HH:mm:ss}";
                ApplyFilters();
                RefreshStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"일괄 수정 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -----------------------------------------------------------------------
        // 인라인 편집 — 셀 편집이 끝나면 즉시 저장
        // -----------------------------------------------------------------------
        private void DgInventory_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            // 선택용 체크박스(IsSelected)는 저장 대상 아님
            if ((e.Column as DataGridCheckBoxColumn)?.Binding is System.Windows.Data.Binding b
                && b.Path?.Path == nameof(FieldInventoryItem.IsSelected)) return;
            if (e.Row.Item is not FieldInventoryItem item) return;

            // 바인딩이 모델에 반영된 뒤 저장 (편집 커밋 직후 디스패치)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    FieldInventoryRepository.Update(item);
                    TxtLastSaved.Text = $"저장됨 {DateTime.Now:HH:mm:ss}";
                    RefreshStats();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"저장 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // -----------------------------------------------------------------------
        // 주간 마감 — 현재고를 오늘 날짜 스냅샷으로 저장 → 다음 주 '이전 대비' 기준이 됨
        // -----------------------------------------------------------------------
        private void BtnWeeklyClose_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                    "현재 재고 현황을 이번 주 마감으로 저장합니다.\n다음 주부터 '이전 대비 증감'의 비교 기준이 됩니다.\n\n진행하시겠습니까?",
                    "주간 마감", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            try
            {
                FieldInventoryRepository.CreateSnapshot(DateTime.Today);
                Load();
                MessageBox.Show("이번 주 재고가 마감 저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"주간 마감 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -----------------------------------------------------------------------
        // 항목 추가 / 수정 / 삭제
        // -----------------------------------------------------------------------
        private void BtnAddRow_Click(object sender, RoutedEventArgs e) => OpenModal(null);

        private void BtnManageLocation_Click(object sender, RoutedEventArgs e)
        {
            ShowLocationManagerDialog();
        }

        // 현재 위치 목록 + 이름 변경 + 새 위치 추가를 한 곳에서 관리
        private void ShowLocationManagerDialog()
        {
            var brush = new BrushConverter();
            var dlg = new Window
            {
                Title = "위치 관리",
                Width = 420,
                Height = 460,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Window.GetWindow(this),
                Background = (Brush)brush.ConvertFromString("#F8FAFC")!
            };

            var root = new DockPanel { Margin = new Thickness(16) };

            // 상단 안내
            var header = new TextBlock
            {
                Text = "현재 등록된 위치",
                FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = (Brush)brush.ConvertFromString("#0F172A")!,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // 하단 액션 영역
            var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(bottom, Dock.Bottom);

            var renameLabel = new TextBlock { Text = "선택한 위치의 새 이름", FontSize = 12, Foreground = (Brush)brush.ConvertFromString("#64748B")!, Margin = new Thickness(0, 0, 0, 4) };
            var renameRow = new StackPanel { Orientation = Orientation.Horizontal };
            var tbRename = new TextBox { Width = 250, FontSize = 14, Padding = new Thickness(8, 6, 8, 6), VerticalContentAlignment = VerticalAlignment.Center };
            var btnRename = new Button { Content = "이름 변경", Width = 90, Height = 32, Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand };
            renameRow.Children.Add(tbRename);
            renameRow.Children.Add(btnRename);

            var divider = new Border { Height = 1, Background = (Brush)brush.ConvertFromString("#E2E8F0")!, Margin = new Thickness(0, 14, 0, 14) };

            var btnAddNew = new Button { Content = "+ 새 위치 추가 (품목 등록)", Height = 34, Cursor = Cursors.Hand, FontWeight = FontWeights.SemiBold };
            var btnClose = new Button { Content = "닫기", Height = 30, Margin = new Thickness(0, 8, 0, 0), Cursor = Cursors.Hand };

            bottom.Children.Add(renameLabel);
            bottom.Children.Add(renameRow);
            bottom.Children.Add(divider);
            bottom.Children.Add(btnAddNew);
            bottom.Children.Add(btnClose);
            root.Children.Add(bottom);

            // 위치 목록 (가운데)
            var listCard = new Border
            {
                Background = Brushes.White,
                BorderBrush = (Brush)brush.ConvertFromString("#E2E8F0")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4)
            };
            var list = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 13 };
            listCard.Child = list;
            root.Children.Add(listCard);

            void RefreshList()
            {
                var locs = _items.GroupBy(i => i.StorageLocation)
                                 .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                                 .OrderBy(g => g.Key)
                                 .Select(g => new { Name = g.Key, Count = g.Count() })
                                 .ToList();
                list.Items.Clear();
                foreach (var l in locs)
                    list.Items.Add($"{l.Name}   ({l.Count}개)");
            }
            RefreshList();

            string? SelectedLocationName()
            {
                if (list.SelectedItem is not string s) return null;
                int idx = s.LastIndexOf("   (");
                return idx > 0 ? s.Substring(0, idx) : s;
            }

            list.SelectionChanged += (_, __) =>
            {
                var name = SelectedLocationName();
                if (name != null) tbRename.Text = name;
            };

            btnRename.Click += (_, __) =>
            {
                var oldName = SelectedLocationName();
                if (string.IsNullOrWhiteSpace(oldName))
                {
                    MessageBox.Show("이름을 변경할 위치를 선택하세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                string newName = tbRename.Text.Trim();
                if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

                var targets = _items.Where(i => i.StorageLocation == oldName).ToList();
                if (MessageBox.Show($"'{oldName}' 위치의 {targets.Count}개 품목을 '{newName}'(으)로 변경하시겠습니까?",
                        "위치명 수정", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

                try
                {
                    foreach (var item in targets)
                    {
                        item.StorageLocation = newName;
                        FieldInventoryRepository.Update(item);
                    }
                    ApplyFilters();
                    RefreshStats();
                    RefreshList();
                    MessageBox.Show($"위치명이 '{newName}'(으)로 변경되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"위치명 수정 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            btnAddNew.Click += (_, __) =>
            {
                string? name = ShowSimpleInputDialog("새 위치 추가", "새 보관위치 이름을 입력하세요.");
                if (string.IsNullOrWhiteSpace(name)) return;
                dlg.Close();
                OpenModal(null, presetLocation: name.Trim());
            };

            btnClose.Click += (_, __) => dlg.Close();

            dlg.Content = root;
            dlg.ShowDialog();
        }

        private void BtnEditItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FieldInventoryItem item)
                OpenModal(item);
        }

        private void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not FieldInventoryItem item) return;
            if (MessageBox.Show($"'{item.ItemName}' 항목을 삭제하시겠습니까?",
                    "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                FieldInventoryRepository.Delete(item.ItemId);
                _items.Remove(item);
                ApplyFilters();
                RefreshStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"삭제 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            var selected = _items.Where(i => i.IsSelected).ToList();
            if (!selected.Any())
            {
                MessageBox.Show("삭제할 항목을 선택해주세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"선택한 {selected.Count}개 항목을 삭제하시겠습니까?",
                    "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                foreach (var item in selected)
                {
                    FieldInventoryRepository.Delete(item.ItemId);
                    _items.Remove(item);
                }
                ApplyFilters();
                RefreshStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"삭제 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -----------------------------------------------------------------------
        // 등록/수정 모달
        // -----------------------------------------------------------------------
        private void OpenModal(FieldInventoryItem? item, string? presetLocation = null)
        {
            _editingItem = item;
            TxtModalTitle.Text = item == null ? "품목 등록" : "품목 정보 수정";
            BtnSaveModal.Content = item == null ? "등록 완료" : "수정 완료";

            CmbEditCategory.ItemsSource = _items.Select(i => i.Category).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
            CmbEditLocation.ItemsSource = _items.Select(i => i.StorageLocation).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
            CmbEditUnit.ItemsSource = _items.Select(i => i.Unit).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();

            TxtEditCode.Text = item?.ItemCode ?? "";
            CmbEditCategory.Text = item?.Category ?? "";
            TxtEditName.Text = item?.ItemName ?? "";
            DpEditDate.SelectedDate = item?.RegisteredDate ?? DateTime.Now.Date;
            CmbEditLocation.Text = item?.StorageLocation ?? presetLocation ?? "";
            TxtEditCurrent.Text = item?.CurrentStock ?? "";
            TxtEditSafe.Text = item?.AppropriateStock ?? "";
            CmbEditUnit.Text = item?.Unit ?? "";
            TxtEditMemo.Text = item?.Memo ?? "";
            TxtEditMinOrder.Text = item?.MinOrderQty ?? "";
            TxtEditOrderQty.Text = item?.OrderQty ?? "";
            TxtEditOrderDate.Text = item?.OrderDate ?? "";
            TxtEditExpected.Text = item?.ExpectedReceipt ?? "";
            TxtEditSupplier.Text = item?.Supplier ?? "";
            ChkEditOrdered.IsChecked = item?.IsOrdered ?? false;

            EditOverlay.Visibility = Visibility.Visible;
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            EditOverlay.Visibility = Visibility.Collapsed;
            _editingItem = null;
        }

        private void BtnSaveModal_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtEditName.Text))
            {
                MessageBox.Show("품목명을 입력해주세요.", "확인", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (_editingItem == null)
                {
                    int nextNo = _items.Count == 0 ? 1 : _items.Max(i => i.OrderNo) + 1;
                    var newItem = new FieldInventoryItem
                    {
                        OrderNo = nextNo,
                        ItemCode = TxtEditCode.Text.Trim(),
                        Category = CmbEditCategory.Text.Trim(),
                        ItemName = TxtEditName.Text.Trim(),
                        RegisteredDate = DpEditDate.SelectedDate ?? DateTime.Now.Date,
                        StorageLocation = CmbEditLocation.Text.Trim(),
                        CurrentStock = TxtEditCurrent.Text.Trim(),
                        AppropriateStock = TxtEditSafe.Text.Trim(),
                        Unit = CmbEditUnit.Text.Trim(),
                        Memo = TxtEditMemo.Text.Trim(),
                        MinOrderQty = TxtEditMinOrder.Text.Trim(),
                        OrderQty = TxtEditOrderQty.Text.Trim(),
                        OrderDate = TxtEditOrderDate.Text.Trim(),
                        ExpectedReceipt = TxtEditExpected.Text.Trim(),
                        Supplier = TxtEditSupplier.Text.Trim(),
                        IsOrdered = ChkEditOrdered.IsChecked == true
                    };
                    newItem.ItemId = FieldInventoryRepository.Insert(newItem);
                    _items.Add(newItem);
                }
                else
                {
                    var item = _editingItem;
                    item.ItemCode = TxtEditCode.Text.Trim();
                    item.Category = CmbEditCategory.Text.Trim();
                    item.ItemName = TxtEditName.Text.Trim();
                    item.RegisteredDate = DpEditDate.SelectedDate ?? item.RegisteredDate;
                    item.StorageLocation = CmbEditLocation.Text.Trim();
                    item.CurrentStock = TxtEditCurrent.Text.Trim();
                    item.AppropriateStock = TxtEditSafe.Text.Trim();
                    item.Unit = CmbEditUnit.Text.Trim();
                    item.Memo = TxtEditMemo.Text.Trim();
                    item.MinOrderQty = TxtEditMinOrder.Text.Trim();
                    item.OrderQty = TxtEditOrderQty.Text.Trim();
                    item.OrderDate = TxtEditOrderDate.Text.Trim();
                    item.ExpectedReceipt = TxtEditExpected.Text.Trim();
                    item.Supplier = TxtEditSupplier.Text.Trim();
                    item.IsOrdered = ChkEditOrdered.IsChecked == true;
                    FieldInventoryRepository.Update(item);
                }

                EditOverlay.Visibility = Visibility.Collapsed;
                _editingItem = null;
                TxtLastSaved.Text = $"저장됨 {DateTime.Now:HH:mm:ss}";
                ApplyFilters();
                RefreshStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 간단한 인라인 입력 다이얼로그 (Microsoft.VisualBasic 의존 없음)
        private string? ShowSimpleInputDialog(string title, string prompt, string initialValue = "")
        {
            var dlg = new Window
            {
                Title = title,
                Width = 360,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Window.GetWindow(this)
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = prompt, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
            var tb = new TextBox { Text = initialValue, FontSize = 14, Padding = new Thickness(8, 6, 8, 6) };
            sp.Children.Add(tb);
            var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            string? result = null;
            var btnOk = new Button { Content = "확인", Width = 70, Height = 28, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            btnOk.Click += (_, __) => { result = tb.Text; dlg.Close(); };
            var btnCancel = new Button { Content = "취소", Width = 70, Height = 28, IsCancel = true };
            btnCancel.Click += (_, __) => dlg.Close();
            bp.Children.Add(btnOk);
            bp.Children.Add(btnCancel);
            sp.Children.Add(bp);
            dlg.Content = sp;
            tb.Focus();
            tb.SelectAll();
            dlg.ShowDialog();
            return result;
        }

        // -----------------------------------------------------------------------
        // 새로고침 / 엑셀 내보내기
        // -----------------------------------------------------------------------
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            // 새로고침: 업로드 임시 반영 내용을 모두 취소(무시)하고 DB 기준으로 재로드
            _stagedItems.Clear();
            _hasStagedUpload = false;
            Load();
            RefreshUploadButtons();
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "재고 리스트 엑셀 내보내기 (A4 가로)",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"재고리스트_{DateTime.Now:yyyyMMdd}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("재고 리스트");

                // ── A4 가로 출력 설정 ──
                ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
                ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                ws.PageSetup.Margins.Top = 0.1;
                ws.PageSetup.Margins.Bottom = 0.1;
                ws.PageSetup.Margins.Left = 0.1;
                ws.PageSetup.Margins.Right = 0.1;
                ws.PageSetup.Margins.Header = 0;
                ws.PageSetup.Margins.Footer = 0;
                ws.PageSetup.CenterHorizontally = true;
                ws.PageSetup.CenterVertically = true;
                ws.PageSetup.FitToPages(1, 1);              // 한 장(가로 1 × 세로 1)에 맞춤
                ws.PageSetup.SetRowsToRepeatAtTop(1, 2);    // 제목/머리글 행 반복 인쇄

                // 인쇄에 꼭 필요한 핵심 컬럼만 (A4 가로 기준)
                var headers = new[] { "NO", "품목명", "위치", "현재재고", "적정재고", "체크", "발주여부", "발주날짜", "입고예정", "발주회사", "비고" };
                int cols = headers.Length;

                // ── 제목 행 ──
                var titleCell = ws.Cell(1, 1);
                titleCell.Value = $"현장 재고 리스트  ({DateTime.Now:yyyy-MM-dd} 기준)";
                ws.Range(1, 1, 1, cols).Merge();
                titleCell.Style.Font.Bold = true;
                titleCell.Style.Font.FontSize = 14;
                titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Row(1).Height = 26;

                // ── 머리글 행 ──
                for (int c = 0; c < cols; c++)
                {
                    var cell = ws.Cell(2, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#334155");
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }
                ws.Row(2).Height = 22;

                // ── 구역별 그룹핑 ──
                var zoneInfo = new (Zone z, string title, string color)[]
                {
                    (Zone.Metal,    "METAL 반입구",   "#1D4ED8"),
                    (Zone.Nonmetal, "N-METAL 출고실", "#BE185D"),
                    (Zone.Office,   "Office 보관",    "#15803D"),
                    (Zone.Cleaning, "세정랩",         "#6D28D9")
                };

                int row = 3;
                foreach (var (zone, zoneTitle, zoneColor) in zoneInfo)
                {
                    var zoneItems = _items.Where(i => ClassifyZone(i.StorageLocation) == zone)
                                          .OrderBy(i => i.OrderNo).ToList();
                    if (zoneItems.Count == 0) continue;

                    // 구역 구분 헤더 (전체 열 병합) — 실제 위치명 반영
                    string actualTitle = ZoneTitle(zoneItems, zoneTitle);
                    var zoneCell = ws.Cell(row, 1);
                    zoneCell.Value = $"◤ {actualTitle}  ({zoneItems.Count}개)";
                    ws.Range(row, 1, row, cols).Merge();
                    zoneCell.Style.Font.Bold = true;
                    zoneCell.Style.Font.FontColor = XLColor.White;
                    zoneCell.Style.Fill.BackgroundColor = XLColor.FromHtml(zoneColor);
                    zoneCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    ws.Row(row).Height = 20;
                    row++;

                    foreach (var item in zoneItems)
                    {
                        ws.Cell(row, 1).Value = item.OrderNo;
                        ws.Cell(row, 2).Value = item.ItemName;
                        ws.Cell(row, 3).Value = item.StorageLocation;
                        ws.Cell(row, 4).Value = item.CurrentStock;
                        ws.Cell(row, 5).Value = item.AppropriateStock;
                        // 체크(6) 칸은 실사 수량을 직접 적도록 빈칸으로 둠
                        ws.Cell(row, 7).Value = item.IsOrdered ? "발주완료" : "미발주";
                        ws.Cell(row, 8).Value = item.OrderDate;
                        ws.Cell(row, 9).Value = item.ExpectedReceipt;
                        ws.Cell(row, 10).Value = item.Supplier;
                        ws.Cell(row, 11).Value = item.Memo;

                        // 숫자/구분 컬럼 가운데 정렬
                        foreach (int c in new[] { 1, 4, 5, 6, 7 })
                            ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        if (item.IsLow)
                        {
                            var rng = ws.Range(row, 1, row, cols);
                            rng.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF2F2");
                            rng.Style.Font.FontColor = XLColor.FromHtml("#B91C1C");
                            rng.Style.Font.Bold = true;
                        }

                        // 체크 컬럼(6)은 노란색으로 강조
                        ws.Cell(row, 6).Style.Fill.BackgroundColor = XLColor.FromHtml("#FDE047");
                        row++;
                    }
                }

                int lastDataRow = row - 1;

                // ── 테두리 (머리글 ~ 마지막 데이터) ──
                if (lastDataRow >= 2)
                {
                    var table = ws.Range(2, 1, lastDataRow, cols);
                    table.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    table.Style.Border.OutsideBorderColor = XLColor.FromHtml("#475569");
                    table.Style.Border.InsideBorderColor = XLColor.FromHtml("#CBD5E1");
                }

                // ── 컬럼 너비 (A4 가로 비율 1.41:1에 맞춰 페이지를 꽉 채움) ──
                ws.Column(1).Width = 7;    // NO
                ws.Column(2).Width = 38;   // 품목명
                ws.Column(3).Width = 18;   // 위치
                ws.Column(4).Width = 13;   // 현재재고
                ws.Column(5).Width = 13;   // 적정재고
                ws.Column(6).Width = 10;   // 체크
                ws.Column(7).Width = 13;   // 발주여부
                ws.Column(8).Width = 16;   // 발주날짜
                ws.Column(9).Width = 16;   // 입고예정
                ws.Column(10).Width = 24;  // 발주회사
                ws.Column(11).Width = 36;  // 비고

                // 데이터 행 높이를 키워 세로 공간도 채움
                for (int rr = 3; rr <= lastDataRow; rr++)
                    if (ws.Row(rr).Height < 19) ws.Row(rr).Height = 19;

                ws.Range(2, 1, lastDataRow, cols).Style.Font.FontSize = 11;

                wb.SaveAs(dlg.FileName);
                MessageBox.Show("엑셀 파일이 저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 내보내기 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 엑셀 업로드: 내보낸 양식의 '체크'(6열)에 적은 실사 수량을 현재 재고로 반영.
        // 반영 전 현재 재고를 스냅샷으로 저장해 '이전 재고'·'증감'이 자동 계산됨.
        private void BtnImportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "재고 리스트 엑셀 업로드",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                // NO(1열) → 체크(6열) 수량 매핑
                var updates = new Dictionary<int, string>();
                using (var wb = new XLWorkbook(dlg.FileName))
                {
                    var ws = wb.Worksheets.First();
                    var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                    for (int r = 1; r <= lastRow; r++)
                    {
                        string noText = ws.Cell(r, 1).GetString().Trim();
                        if (!int.TryParse(noText, out int orderNo)) continue;   // 제목/머리글/구역행 건너뜀

                        string check = ws.Cell(r, 6).GetString().Trim();
                        if (string.IsNullOrWhiteSpace(check)) continue;          // 빈칸은 변경 안 함
                        updates[orderNo] = check;
                    }
                }

                if (updates.Count == 0)
                {
                    MessageBox.Show("체크 칸에 입력된 수량이 없습니다.\n실사 수량을 체크 칸에 적은 후 업로드하세요.",
                        "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 실제 변경 대상 집계 (값이 달라지는 항목만)
                var targets = _items.Where(i => updates.ContainsKey(i.OrderNo)
                                                && i.CurrentStock.Trim() != updates[i.OrderNo]).ToList();
                if (targets.Count == 0)
                {
                    MessageBox.Show($"체크 칸 {updates.Count}건을 읽었지만 기존 재고와 동일하여 변경된 항목이 없습니다.",
                        "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (MessageBox.Show(
                        $"체크 칸 수량을 현재 재고에 '임시 반영'합니다.\n변경 대상: {targets.Count}개 품목\n\n" +
                        "※ 아직 저장되지 않습니다. 화면에서 증감을 확인한 뒤\n" +
                        "   '업로드 확정'을 눌러야 저장됩니다.\n" +
                        "   '새로고침'을 누르면 업로드 내용이 모두 취소됩니다.\n\n진행하시겠습니까?",
                        "엑셀 업로드 (임시 반영)", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

                // 메모리에만 임시 반영 — DB 미저장. 업로드 직전 값을 '이전 재고'로 미리보기.
                foreach (var item in targets)
                {
                    item.PreviousStock = item.CurrentStock;       // 업로드 직전 값 = 이전 재고(미리보기)
                    item.CurrentStock = updates[item.OrderNo];    // 체크 수량 = 새 현재고
                    _stagedItems.Add(item.ItemId);
                }
                _hasStagedUpload = true;

                ApplyFilters();
                RefreshStats();
                RefreshUploadButtons();
                MessageBox.Show(
                    $"{targets.Count}개 품목이 임시 반영되었습니다.\n증감을 확인한 뒤 '업로드 확정'으로 저장하세요.",
                    "임시 반영 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 업로드 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 업로드 확정: 임시 반영된 현재고를 DB에 저장. 직전 값은 스냅샷('이전 재고')으로 보존.
        private void BtnConfirmUpload_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasStagedUpload || _stagedItems.Count == 0)
            {
                MessageBox.Show("확정할 업로드 내용이 없습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                    $"임시 반영된 {_stagedItems.Count}개 품목을 저장합니다.\n" +
                    "저장 후에는 새로고침해도 유지되며, 증감 비교 기준이 갱신됩니다.\n\n저장하시겠습니까?",
                    "업로드 확정", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            try
            {
                // 1) DB는 아직 업로드 직전(옛) 현재고 상태 → 스냅샷이 '이전 재고'가 됨
                FieldInventoryRepository.CreateSnapshot(DateTime.Today);

                // 2) 임시 반영된 새 현재고를 DB에 저장
                foreach (var item in _items.Where(i => _stagedItems.Contains(i.ItemId)))
                    FieldInventoryRepository.Update(item);

                _stagedItems.Clear();
                _hasStagedUpload = false;

                // 3) 재로드 → 스냅샷(옛 값)이 '이전 재고'로 주입되어 증감 확정
                Load();
                RefreshUploadButtons();
                MessageBox.Show("업로드 내용이 저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"업로드 확정 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>보관위치 문자열을 (배경색, 글자색)으로 변환. ConverterParameter="Fg"이면 글자색 반환.</summary>
    public class LocationColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string location = value as string ?? "";
            var (bg, fg) = FieldInventoryView.GetLocationColors(location);
            string hex = (parameter as string) == "Fg" ? fg : bg;
            return new BrushConverter().ConvertFromString(hex)!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>날짜 문자열 ↔ DateTime? 변환 (셀 내 DatePicker.SelectedDate 바인딩용).</summary>
    public class StringDateConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string ?? "";
            if (string.IsNullOrWhiteSpace(s)) return null;
            return DateTime.TryParse(s, out var d) ? d : (DateTime?)null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is DateTime d ? d.ToString("yyyy-MM-dd") : "";
    }
}
