using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace CleanPotal
{
    public class DispatchCreationLog
    {
        public int RowNo { get; set; }
        public string RegNo { get; set; } = "";
        public string Result { get; set; } = "대기";
        public string Message { get; set; } = "";
    }

    public partial class DispatchCertificateBatchView : UserControl
    {
        public ObservableCollection<DispatchCreationLog> Logs { get; } = new();

        public DispatchCertificateBatchView()
        {
            InitializeComponent();
            LogGrid.ItemsSource = Logs;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "반출등록 엑셀 선택",
                Filter = "Excel 파일 (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|모든 파일 (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtWorkbookPath.Text = dialog.FileName;
            }
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            string workbookPath = TxtWorkbookPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
            {
                MessageBox.Show("유효한 엑셀 파일 경로를 선택하세요.", "경로 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnRun.IsEnabled = false;
            BtnRun.Content = "처리 중...";
            Logs.Clear();

            try
            {
                var result = await Task.Run(() => ExecuteBatch(workbookPath));
                foreach (var log in result.Logs) Logs.Add(log);

                MessageBox.Show($"작업 완료! 새로 생성된 파일: {result.TotalCreated}개", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"처리 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnRun.IsEnabled = true;
                BtnRun.Content = "자동 생성 실행";
            }
        }

        private static DispatchBatchResult ExecuteBatch(string workbookPath)
        {
            using var workbook = new XLWorkbook(workbookPath);

            var wsReg = workbook.Worksheets.FirstOrDefault(w => w.Name == "반출등록");
            var wsBase = workbook.Worksheets.FirstOrDefault(w => w.Name == "기준정보");
            var wsHist = workbook.Worksheets.FirstOrDefault(w => w.Name == "생성이력");

            if (wsReg == null || wsBase == null || wsHist == null)
            {
                throw new InvalidOperationException("시트 이름을 찾을 수 없습니다. (반출등록/기준정보/생성이력 확인)");
            }

            int totalCreated = 0;
            int lastRow = wsReg.LastRowUsed()?.RowNumber() ?? 1;
            int lastBaseRow = wsBase.LastRowUsed()?.RowNumber() ?? 1;

            var output = new DispatchBatchResult();

            for (int targetRow = 2; targetRow <= lastRow; targetRow++)
            {
                string regNo = wsReg.Cell(targetRow, "A").GetString().Trim();
                string statusValue = wsReg.Cell(targetRow, "I").GetString().Trim();
                string companyName = wsReg.Cell(targetRow, "C").GetString().Trim();
                string itemName = wsReg.Cell(targetRow, "E").GetString().Trim();
                string drawingNo = wsReg.Cell(targetRow, "H").GetString().Trim();
                string productType = wsReg.Cell(targetRow, "D").GetString().Trim();
                string managerName = wsReg.Cell(targetRow, "G").GetString().Trim();

                long qty = ParseLong(wsReg.Cell(targetRow, "F").GetString());

                if (statusValue == "생성완료" || string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(itemName) || qty <= 0 || string.IsNullOrWhiteSpace(drawingNo))
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow, RegNo = regNo, Result = "건너뜀", Message = "필수값 없음 또는 이미 생성완료" });
                    continue;
                }

                if (!TryGetDate(wsReg.Cell(targetRow, "B").Value, out DateTime outDate))
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow, RegNo = regNo, Result = "건너뜀", Message = "반출일 형식 오류" });
                    continue;
                }

                string templatePath = "";
                string basePath = "";
                string fileNameRule = "";
                bool foundMatch = false;

                for (int baseRow = 2; baseRow <= lastBaseRow; baseRow++)
                {
                    string useYn = wsBase.Cell(baseRow, "A").GetString().Trim();
                    string baseDrawing = wsBase.Cell(baseRow, "E").GetString().Trim();

                    if (string.Equals(useYn, "Y", StringComparison.OrdinalIgnoreCase) && baseDrawing == drawingNo)
                    {
                        templatePath = wsBase.Cell(baseRow, "F").GetString().Trim();
                        basePath = wsBase.Cell(baseRow, "G").GetString().Trim();
                        fileNameRule = wsBase.Cell(baseRow, "I").GetString().Trim();
                        foundMatch = true;
                        break;
                    }
                }

                if (!foundMatch || string.IsNullOrWhiteSpace(templatePath) || string.IsNullOrWhiteSpace(basePath))
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow, RegNo = regNo, Result = "건너뜀", Message = "기준정보 매칭 실패" });
                    continue;
                }

                if (!File.Exists(templatePath))
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow, RegNo = regNo, Result = "실패", Message = $"템플릿 파일 없음: {templatePath}" });
                    continue;
                }

                string yearFolder = Path.Combine(basePath, $"{outDate:yy}년");
                string monthFolder = Path.Combine(yearFolder, $"{outDate.Month}월");
                string dayFolder = Path.Combine(monthFolder, $"{outDate.Day}일");
                Directory.CreateDirectory(dayFolder);

                // 요청사항: 생성 결과 파일은 항상 엑셀 서식파일(.xltx)로 저장
                string extName = ".xltx";
                int createdCount = 0;

                for (int i = 1; i <= qty; i++)
                {
                    string newFileName = BuildFileName(fileNameRule, outDate, itemName, drawingNo, i) + extName;
                    string newFilePath = Path.Combine(dayFolder, newFileName);

                    if (!File.Exists(newFilePath))
                    {
                        File.Copy(templatePath, newFilePath);
                        createdCount++;
                    }
                }

                if (createdCount > 0)
                {
                    wsReg.Cell(targetRow, "I").Value = "생성완료";
                    wsReg.Cell(targetRow, "J").Value = dayFolder;
                    wsReg.Cell(targetRow, "K").Value = createdCount;

                    int histRow = (wsHist.LastRowUsed()?.RowNumber() ?? 1) + 1;
                    wsHist.Cell(histRow, "A").Value = DateTime.Now;
                    wsHist.Cell(histRow, "B").Value = regNo;
                    wsHist.Cell(histRow, "C").Value = outDate;
                    wsHist.Cell(histRow, "D").Value = companyName;
                    wsHist.Cell(histRow, "E").Value = productType;
                    wsHist.Cell(histRow, "F").Value = itemName;
                    wsHist.Cell(histRow, "G").Value = qty;
                    wsHist.Cell(histRow, "H").Value = managerName;
                    wsHist.Cell(histRow, "I").Value = dayFolder;
                    wsHist.Cell(histRow, "J").Value = "성공";

                    totalCreated += createdCount;
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow, RegNo = regNo, Result = "성공", Message = $"{createdCount}개 생성" });
                }
                else
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow, RegNo = regNo, Result = "건너뜀", Message = "신규 생성 파일 없음" });
                }
            }

            workbook.Save();
            output.TotalCreated = totalCreated;
            return output;
        }

        private static bool TryGetDate(object raw, out DateTime result)
        {
            if (raw is DateTime dt)
            {
                result = dt;
                return true;
            }

            string text = raw?.ToString()?.Trim() ?? "";
            return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out result)
                || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        private static long ParseLong(string value)
        {
            if (long.TryParse(value, out long parsed)) return parsed;
            if (double.TryParse(value, out double parsedDouble)) return Convert.ToInt64(parsedDouble);
            return 0;
        }

        private static string BuildFileName(string ruleText, DateTime outDate, string itemName, string drawingNo, int seqNo)
        {
            string result = ruleText;
            result = result.Replace("반출일", outDate.ToString("yyyyMMdd"));
            result = result.Replace("품목명", itemName);
            result = result.Replace("도면명", drawingNo);
            result = result.Replace("순번", seqNo.ToString());
            return result;
        }
    }

    internal class DispatchBatchResult
    {
        public int TotalCreated { get; set; }
        public Collection<DispatchCreationLog> Logs { get; } = new();
    }
}