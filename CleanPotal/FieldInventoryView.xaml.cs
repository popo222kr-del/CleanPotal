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
using Microsoft.Win32;

namespace CleanPotal
{
    public partial class FieldInventoryView : UserControl
    {
        private readonly ObservableCollection<FieldInventoryItem> _items = new();
        private List<FieldInventoryItem> _filtered = new();
        private bool _sortDescending = true;
        private string _locationFilter = "전체";
        private FieldInventoryItem? _editingItem;
        private bool _editMode = false;   // false=재고 현황(조회), true=재고 리스트 관리(편집)

        public FieldInventoryView()
        {
            InitializeComponent();
            ApplyEditMode();
            Loaded += (s, e) => Load();
        }

        // -----------------------------------------------------------------------
        // 화면 모드 전환 — '재고 현황'(조회 전용) / '재고 리스트 관리'(편집)
        // -----------------------------------------------------------------------
        private void ModeTab_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;
            bool manage = tag == "manage";
            if (manage == _editMode) return;
            _editMode = manage;
            ApplyEditMode();
        }

        private void ApplyEditMode()
        {
            var blue = (Brush)new BrushConverter().ConvertFromString("#2563EB")!;
            var gray = (Brush)new BrushConverter().ConvertFromString("#64748B")!;

            ModeTabView.Background = _editMode ? Brushes.Transparent : blue;
            TxtModeView.Foreground = _editMode ? gray : Brushes.White;
            TxtModeView.FontWeight = _editMode ? FontWeights.SemiBold : FontWeights.Bold;

            ModeTabManage.Background = _editMode ? blue : Brushes.Transparent;
            TxtModeManage.Foreground = _editMode ? Brushes.White : gray;
            TxtModeManage.FontWeight = _editMode ? FontWeights.Bold : FontWeights.SemiBold;

            // 편집 기능: 관리 모드에서만 노출
            DgInventory.IsReadOnly = !_editMode;
            ColSelect.Visibility = _editMode ? Visibility.Visible : Visibility.Collapsed;
            ColActions.Visibility = _editMode ? Visibility.Visible : Visibility.Collapsed;

            var editVis = _editMode ? Visibility.Visible : Visibility.Collapsed;
            BtnWeeklyClose.Visibility = editVis;
            BtnDeleteRow.Visibility = editVis;
            BtnAddLocation.Visibility = editVis;
            BtnAddRow.Visibility = editVis;
            BtnBatchEdit.Visibility = editVis;
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

                RenderLocationTabs();
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
        // 필터 / 정렬 / 위치 탭
        // -----------------------------------------------------------------------
        private void ApplyFilters()
        {
            if (DgInventory == null || LocationTabPanel == null) return;

            IEnumerable<FieldInventoryItem> source = _items;

            if (_locationFilter != "전체")
                source = source.Where(i => i.StorageLocation == _locationFilter);

            if (DpFrom.SelectedDate.HasValue)
                source = source.Where(i => i.RegisteredDate.Date >= DpFrom.SelectedDate.Value.Date);
            if (DpTo.SelectedDate.HasValue)
                source = source.Where(i => i.RegisteredDate.Date <= DpTo.SelectedDate.Value.Date);

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
            var view = new CollectionViewSource { Source = _filtered };
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FieldInventoryItem.StorageLocation)));
            DgInventory.ItemsSource = view.View;
            TxtTotalCount.Text = $"총 {_filtered.Count}개";
        }

        // 보관위치 탭 구성 ("전체" + 등장한 모든 위치, 탭 스타일로 렌더링)
        private void RenderLocationTabs()
        {
            LocationTabPanel.Children.Clear();

            var locations = new[] { "전체" }
                .Concat(_items.Select(i => i.StorageLocation).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s))
                .ToList();
            if (!locations.Contains(_locationFilter)) _locationFilter = "전체";

            foreach (var loc in locations)
            {
                bool active = loc == _locationFilter;
                var (bg, fg) = GetLocationColors(loc);
                int count = loc == "전체" ? _items.Count : _items.Count(i => i.StorageLocation == loc);

                var border = new System.Windows.Controls.Border
                {
                    CornerRadius = new CornerRadius(8, 8, 0, 0),
                    Padding = new Thickness(14, 8, 14, 8),
                    Margin = new Thickness(0, 0, 2, 0),
                    Cursor = Cursors.Hand,
                    Background = active ? Brushes.White : (Brush)new BrushConverter().ConvertFromString(bg)!,
                    BorderThickness = active ? new Thickness(1, 1, 1, 0) : new Thickness(0),
                    BorderBrush = active ? (Brush)new BrushConverter().ConvertFromString("#E2E8F0")! : Brushes.Transparent,
                };

                var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                label.Inlines.Add(new Run(loc)
                {
                    FontSize = 13,
                    FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold,
                    Foreground = active ? (Brush)new BrushConverter().ConvertFromString("#0F172A")! : (Brush)new BrushConverter().ConvertFromString(fg)!
                });
                label.Inlines.Add(new Run($" {count}")
                {
                    FontSize = 11,
                    Foreground = (Brush)new BrushConverter().ConvertFromString(active ? "#64748B" : "#94A3B8")!
                });

                border.Child = label;
                border.MouseLeftButtonDown += (_, __) => { _locationFilter = loc; ApplyFilters(); RenderLocationTabs(); };
                LocationTabPanel.Children.Add(border);
            }
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

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void DateFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();

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
                RenderLocationTabs();
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
            if (e.Column == ColSelect) return;                 // 선택용 체크박스는 저장 대상 아님
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

        private void BtnAddLocation_Click(object sender, RoutedEventArgs e)
        {
            string? name = ShowSimpleInputDialog("보관위치 추가", "새 보관위치 이름을 입력하세요.");
            if (string.IsNullOrWhiteSpace(name)) return;
            OpenModal(null, presetLocation: name.Trim());
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
                RenderLocationTabs();
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
                RenderLocationTabs();
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
                RenderLocationTabs();
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
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Load();

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "재고 현황 엑셀 내보내기",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"재고현황_{DateTime.Now:yyyyMMdd}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("재고 현황");

                var headers = new[] { "NO", "등록일자", "품목코드", "카테고리", "품목명", "위치", "현재고", "이전재고", "이전대비", "안전재고", "단위", "발주여부", "최소발주", "발주날짜", "발주수량", "입고예정", "발주회사", "비고" };
                for (int c = 0; c < headers.Length; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                var data = _items.OrderBy(i => i.OrderNo).ToList();
                for (int r = 0; r < data.Count; r++)
                {
                    var item = data[r];
                    int row = r + 2;
                    ws.Cell(row, 1).Value = item.OrderNo;
                    ws.Cell(row, 2).Value = item.RegisteredDate.ToString("yyyy-MM-dd");
                    ws.Cell(row, 3).Value = item.ItemCode;
                    ws.Cell(row, 4).Value = item.Category;
                    ws.Cell(row, 5).Value = item.ItemName;
                    ws.Cell(row, 6).Value = item.StorageLocation;
                    ws.Cell(row, 7).Value = item.CurrentStock;
                    ws.Cell(row, 8).Value = item.PreviousStock;
                    ws.Cell(row, 9).Value = item.WeeklyDeltaText;
                    ws.Cell(row, 10).Value = item.AppropriateStock;
                    ws.Cell(row, 11).Value = item.Unit;
                    ws.Cell(row, 12).Value = item.IsOrdered ? "발주완료" : "미발주";
                    ws.Cell(row, 13).Value = item.MinOrderQty;
                    ws.Cell(row, 14).Value = item.OrderDate;
                    ws.Cell(row, 15).Value = item.OrderQty;
                    ws.Cell(row, 16).Value = item.ExpectedReceipt;
                    ws.Cell(row, 17).Value = item.Supplier;
                    ws.Cell(row, 18).Value = item.Memo;

                    if (item.IsLow)
                    {
                        var rowRange = ws.Range(row, 1, row, headers.Length);
                        rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF2F2");
                        rowRange.Style.Font.FontColor = XLColor.FromHtml("#B91C1C");
                    }
                }

                ws.Columns().AdjustToContents();
                ws.Cell(data.Count + 3, 1).Value = "※ 현재재고 ≤ 적정재고 항목은 즉시 발주 필요 (빨간색 행)";

                wb.SaveAs(dlg.FileName);
                MessageBox.Show("엑셀 파일이 저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 내보내기 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
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
}
