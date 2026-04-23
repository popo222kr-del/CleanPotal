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

        public WeeklyReportTableWindow(WeeklyReportModel report)
        {
            InitializeComponent();
            _report = report;
            TxtTitle.Text = $"{report.Title} 주간보고 상세";
            ReportDataGrid.ItemsSource = report.Blocks;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        private void Attachment_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WeeklyAttachmentModel att)
            {
                if (File.Exists(att.AbsolutePath))
                {
                    Process.Start(new ProcessStartInfo(att.AbsolutePath) { UseShellExecute = true });
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

                        ws.Cell(row, 2).Value = $"[{b.Status}]\n{b.Category}";
                        ws.Cell(row, 2).Style.Alignment.WrapText = true;
                        ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        ws.Cell(row, 3).Value = b.FormattedContent;
                        ws.Cell(row, 3).Style.Alignment.WrapText = true;

                        row++;
                    }

                    if (_report.Blocks.Count > 0)
                    {
                        var dataRange = ws.Range(4, 1, row - 1, 3);
                        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    }

                    ws.Column(1).Width = 6;
                    ws.Column(2).Width = 25;
                    ws.Column(3).Width = 90;

                    workbook.SaveAs(saveDialog.FileName);
                    if (MessageBox.Show("엑셀이 저장되었습니다. 열어보시겠습니까?", "완료", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(saveDialog.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex) { MessageBox.Show("엑셀 저장 실패: " + ex.Message); }
            }
        }
    }
}