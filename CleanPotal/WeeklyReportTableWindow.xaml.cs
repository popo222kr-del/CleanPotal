using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace CleanPotal
{
    public partial class WeeklyReportTableWindow : Window
    {
        private WeeklyReportModel _report;
        private double _currentZoom = 1.0;
        private const double MinZoom = 0.7;
        private const double MaxZoom = 2.5;
        private const double ZoomStep = 0.1;
        private const double PresentationZoom = 1.5;

        public WeeklyReportTableWindow(WeeklyReportModel report)
        {
            InitializeComponent();
            _report = report;
            TxtTitle.Text = $"{report.Title} 주간보고 상세";
            ReportDataGrid.ItemsSource = report.Blocks;
            UpdateZoomStatus();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        // 🔥 폰트 확대/축소
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(_currentZoom + ZoomStep);
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(_currentZoom - ZoomStep);
        private void BtnZoomReset_Click(object sender, RoutedEventArgs e) => ApplyZoom(1.0);
        private void BtnPresentationZoom_Click(object sender, RoutedEventArgs e) => ApplyZoom(PresentationZoom);

        // 🔥 단축키: Ctrl+0 리셋, Ctrl+± 확대/축소, Ctrl+마우스휠은 PreviewMouseWheel로
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.D0 || e.Key == Key.NumPad0)
                {
                    ApplyZoom(1.0);
                    e.Handled = true;
                }
                else if (e.Key == Key.OemPlus || e.Key == Key.Add)
                {
                    ApplyZoom(_currentZoom + ZoomStep);
                    e.Handled = true;
                }
                else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                {
                    ApplyZoom(_currentZoom - ZoomStep);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                this.Close();
            }
            else if (e.Key == Key.F5)
            {
                // 상세 창에서 F5 = 발표용 확대 토글
                ApplyZoom(Math.Abs(_currentZoom - PresentationZoom) < 0.001 ? 1.0 : PresentationZoom);
                e.Handled = true;
            }
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                ApplyZoom(_currentZoom + (e.Delta > 0 ? ZoomStep : -ZoomStep));
                e.Handled = true;
                return;
            }
            base.OnPreviewMouseWheel(e);
        }

        private void ApplyZoom(double value)
        {
            _currentZoom = Math.Clamp(value, MinZoom, MaxZoom);
            if (ContentScaleTransform != null)
            {
                ContentScaleTransform.ScaleX = _currentZoom;
                ContentScaleTransform.ScaleY = _currentZoom;
            }
            UpdateZoomStatus();
        }

        private void UpdateZoomStatus()
        {
            int percent = (int)Math.Round(_currentZoom * 100);
            if (TxtZoomStatus != null) TxtZoomStatus.Text = $"확대: {percent}%  (Ctrl + 휠 / Ctrl + 0)";
            if (BtnZoomReset != null) BtnZoomReset.Content = $"{percent}%";
        }

        private void Attachment_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WeeklyAttachmentModel att)
            {
                if (File.Exists(att.AbsolutePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(att.AbsolutePath) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("파일을 열 수 없습니다: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("첨부 파일을 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "엑셀 저장",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"주간보고_{_report.Title.Replace(" ", "_")}.xlsx"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using var workbook = new XLWorkbook();
                    var ws = workbook.Worksheets.Add("주간보고");
                    ws.Cell(1, 1).Value = _report.Title + " 상세 내역";
                    ws.Cell(1, 1).Style.Font.Bold = true;
                    ws.Cell(1, 1).Style.Font.FontSize = 16;
                    ws.Range(1, 1, 1, 3).Merge();

                    int headerRow = 3;
                    ws.Cell(headerRow, 1).Value = "No.";
                    ws.Cell(headerRow, 2).Value = "분류(상태)";
                    ws.Cell(headerRow, 3).Value = "세부 내용 및 팔로업";

                    var header = ws.Range(headerRow, 1, headerRow, 3);
                    header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E6F0FF");
                    header.Style.Font.Bold = true;
                    header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    int row = 4;
                    foreach (var b in _report.Blocks)
                    {
                        ws.Cell(row, 1).Value = b.Number;
                        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        ws.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                        ws.Cell(row, 2).Value = $"[{b.Status}]\n{b.Category}";
                        ws.Cell(row, 2).Style.Alignment.WrapText = true;
                        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        ws.Cell(row, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                        ws.Cell(row, 3).Value = b.FormattedContent;
                        ws.Cell(row, 3).Style.Alignment.WrapText = true;
                        ws.Cell(row, 3).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

                        row++;
                    }

                    if (_report.Blocks.Count > 0)
                    {
                        var dataRange = ws.Range(4, 1, row - 1, 3);
                        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    }

                    // 🔥 열 너비 개선 - No.는 고정, 나머지는 내용에 맞춰 조정
                    ws.Column(1).Width = 6;
                    ws.Column(2).Width = 28;
                    ws.Column(3).Width = 100;

                    // 🔥 전체 행 자동 높이 조정 (긴 내용 잘림 방지)
                    ws.Rows().AdjustToContents();

                    workbook.SaveAs(saveDialog.FileName);
                    if (MessageBox.Show("엑셀이 저장되었습니다. 열어보시겠습니까?", "완료", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(saveDialog.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("엑셀 저장 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
