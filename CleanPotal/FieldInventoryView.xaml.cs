using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CleanPotal.FieldInventory.Models;
using CleanPotal.FieldInventory.Repositories;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace CleanPotal
{
    public partial class FieldInventoryView : UserControl
    {
        private readonly ObservableCollection<FieldInventoryItem> _items = new();
        private bool _suppressEdit;

        public FieldInventoryView()
        {
            InitializeComponent();
            Loaded += (s, e) => Load();
        }

        private void Load()
        {
            try
            {
                var all = FieldInventoryRepository.GetAll();
                _items.Clear();
                foreach (var i in all) _items.Add(i);

                // 창고 필터 콤보 구성
                var locations = new[] { "전체" }.Concat(_items.Select(i => i.StorageLocation).Distinct()).ToArray();
                CmbLocationFilter.ItemsSource = locations;
                CmbLocationFilter.SelectedIndex = 0;

                ApplyFilter("전체");
                RefreshStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"재고 목록을 불러오지 못했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter(string location)
        {
            var source = string.IsNullOrEmpty(location) || location == "전체"
                ? _items
                : (IEnumerable<FieldInventoryItem>)_items.Where(i => i.StorageLocation == location);

            var view = new CollectionViewSource { Source = source };
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FieldInventoryItem.StorageLocation)));
            DgInventory.ItemsSource = view.View;
        }

        private void RefreshStats()
        {
            int total = _items.Count;
            int low = _items.Count(i => i.IsLow);
            StatTotalText.Text = total.ToString();
            StatLowText.Text = low.ToString();

            var latest = _items.Where(i => i.UpdatedAt != default).OrderByDescending(i => i.UpdatedAt).FirstOrDefault();
            StatUpdatedText.Text = latest != null ? latest.UpdatedAt.ToString("yyyy-MM-dd HH:mm") : "-";
        }

        private void CmbLocationFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLocationFilter.SelectedItem is string loc)
                ApplyFilter(loc);
        }

        // -----------------------------------------------------------------------
        // 항목 추가
        // -----------------------------------------------------------------------
        private void BtnAddRow_Click(object sender, RoutedEventArgs e)
        {
            int nextNo = _items.Count == 0 ? 1 : _items.Max(i => i.OrderNo) + 1;
            var newItem = new FieldInventoryItem { OrderNo = nextNo, StorageLocation = "메탈 반입구", ItemName = "새 품목" };
            try
            {
                newItem.ItemId = FieldInventoryRepository.Insert(newItem);
                _items.Add(newItem);
                RefreshStats();
                DgInventory.SelectedItem = newItem;
                DgInventory.ScrollIntoView(newItem);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"항목 추가 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -----------------------------------------------------------------------
        // 선택 항목 삭제
        // -----------------------------------------------------------------------
        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            var selected = DgInventory.SelectedItems.Cast<FieldInventoryItem>().ToList();
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
                RefreshStats();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"삭제 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -----------------------------------------------------------------------
        // 셀 편집 완료 → 즉시 저장
        // -----------------------------------------------------------------------
        private void DgInventory_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (_suppressEdit) return;
            if (e.Row?.Item is not FieldInventoryItem item) return;

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
        // 새로고침
        // -----------------------------------------------------------------------
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Load();

        // -----------------------------------------------------------------------
        // 엑셀 내보내기
        // -----------------------------------------------------------------------
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

                // 헤더
                var headers = new[] { "NO", "창고", "품목", "현재 재고", "적정 재고", "최소 발주", "발주 날짜", "발주 수량", "입고 예정", "발주 회사", "비고" };
                for (int c = 0; c < headers.Length; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                // 데이터
                var data = _items.OrderBy(i => i.OrderNo).ToList();
                for (int r = 0; r < data.Count; r++)
                {
                    var item = data[r];
                    int row = r + 2;
                    ws.Cell(row, 1).Value = item.OrderNo;
                    ws.Cell(row, 2).Value = item.StorageLocation;
                    ws.Cell(row, 3).Value = item.ItemName;
                    ws.Cell(row, 4).Value = item.CurrentStock;
                    ws.Cell(row, 5).Value = item.AppropriateStock;
                    ws.Cell(row, 6).Value = item.MinOrderQty;
                    ws.Cell(row, 7).Value = item.OrderDate;
                    ws.Cell(row, 8).Value = item.OrderQty;
                    ws.Cell(row, 9).Value = item.ExpectedReceipt;
                    ws.Cell(row, 10).Value = item.Supplier;
                    ws.Cell(row, 11).Value = item.Memo;

                    // 재고 부족 행 빨간색
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

    /// <summary>IsLow 값에 따라 행 스타일을 동적으로 선택.</summary>
    public class InventoryRowStyleSelector : StyleSelector
    {
        public Style? NormalStyle { get; set; }
        public Style? LowStockStyle { get; set; }

        public override Style? SelectStyle(object item, DependencyObject container)
            => item is FieldInventoryItem inv && inv.IsLow ? LowStockStyle : NormalStyle;
    }
}
