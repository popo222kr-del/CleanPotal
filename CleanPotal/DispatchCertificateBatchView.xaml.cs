using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;

namespace CleanPotal
{
    // =========================================================
    // 1. 처리 결과 로그 모델
    // =========================================================
    public class DispatchCreationLog
    {
        public string RowNo { get; set; } = "";
        public string RegNo { get; set; } = "";
        public string Result { get; set; } = "대기";
        public string Message { get; set; } = "";
    }

    // =========================================================
    // 2. AETS 프리뷰 데이터 모델
    // =========================================================
    public class AetsPreviewModel : INotifyPropertyChanged
    {
        private bool _isChecked = true;
        public bool IsChecked { get => _isChecked; set { _isChecked = value; OnPropertyChanged(); } }

        private DateTime _outDate = DateTime.Today;
        public DateTime OutDate { get => _outDate; set { _outDate = value; OnPropertyChanged(); } }

        private string _companyName = "AETS";
        public string CompanyName { get => _companyName; set { _companyName = value; OnPropertyChanged(); } }

        private string _partName = "";
        public string PartName { get => _partName; set { _partName = value; OnPropertyChanged(); } }

        private string _itemCode = "";
        public string ItemCode { get => _itemCode; set { _itemCode = value; OnPropertyChanged(); } }

        private int _qty = 1;
        public int Qty { get => _qty; set { _qty = value; OnPropertyChanged(); } }

        private string _managerName = "";
        public string ManagerName { get => _managerName; set { _managerName = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // =========================================================
    // 3. 메인 컨트롤 로직
    // =========================================================
    public partial class DispatchCertificateBatchView : UserControl
    {
        public ObservableCollection<DispatchCreationLog> Logs { get; } = new();
        public ObservableCollection<AetsPreviewModel> AetsPreviewList { get; } = new();
        private bool _isAetsMode = false;

        private string MasterDbPath
        {
            get
            {
                string config = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "master_db_config.txt");
                return File.Exists(config) ? File.ReadAllText(config).Trim() : "";
            }
        }

        public DispatchCertificateBatchView()
        {
            InitializeComponent();
            LogGrid.ItemsSource = Logs;
            AetsPreviewGrid.ItemsSource = AetsPreviewList;
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            _isAetsMode = RbAets?.IsChecked == true;
            if (AetsFilePanel != null) AetsFilePanel.Visibility = _isAetsMode ? Visibility.Visible : Visibility.Collapsed;
            if (PreviewBorder != null) PreviewBorder.Visibility = _isAetsMode ? Visibility.Visible : Visibility.Collapsed;
            Logs.Clear();
        }

        private void AetsPanel_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void AetsPanel_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                // 🔥 수정 완료: 이제 .csv 파일과 .xlsx 파일을 모두 완벽하게 허용합니다!
                string path = files.FirstOrDefault(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) ?? "";

                if (!string.IsNullOrEmpty(path))
                {
                    TxtAetsStatus.Text = "데이터 분석 중...";
                    await ParseAetsFileAsync(path);
                    TxtAetsStatus.Text = $"[{Path.GetFileName(path)}] 분석 완료. 아래 표를 확인하세요.";
                }
                else
                {
                    MessageBox.Show("엑셀(.xlsx) 또는 CSV(.csv) 파일만 지원합니다.", "형식 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            e.Handled = true;
        }

        private async Task ParseAetsFileAsync(string aetsPath)
        {
            AetsPreviewList.Clear();
            try
            {
                await Task.Run(() =>
                {
                    // 확장자에 따라 파싱 방식(CSV vs Excel)을 스마트하게 분리합니다.
                    if (aetsPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseCsvData(aetsPath);
                    }
                    else
                    {
                        ParseExcelData(aetsPath);
                    }
                });
            }
            catch (Exception ex) { Dispatcher.Invoke(() => MessageBox.Show("파일 읽기 실패: " + ex.Message)); }
        }

        // 🔥 신규: CSV 전용 초고속 파싱 엔진 탑재
        private void ParseCsvData(string path)
        {
            DateTime exDate = DateTime.Today;
            string exMgr = "", exCom = "AETS";

            System.Text.Encoding encoding = System.Text.Encoding.Default;
            try { encoding = System.Text.Encoding.GetEncoding("euc-kr"); } catch { }

            string[] lines = File.ReadAllLines(path, encoding);
            bool isList = false, isInfo = false;

            foreach (var line in lines)
            {
                var cells = line.Split(',').Select(x => x.Trim('\"', ' ')).ToArray();
                if (cells.Length < 2) continue;

                string cellB = cells.Length > 1 ? cells[1] : "";
                string cellC = cells.Length > 2 ? cells[2] : "";
                string cellD = cells.Length > 3 ? cells[3] : "";
                string cellF = cells.Length > 5 ? cells[5] : "";

                if (cellB.Contains("반출 정보")) { isInfo = true; isList = false; continue; }
                if (cellB.Contains("반출 LIST")) { isList = true; isInfo = false; continue; }

                if (isInfo)
                {
                    if (cellC.Contains("업체") && !string.IsNullOrWhiteSpace(cellD)) exCom = cellD;
                    if (cellC.Contains("담당자") && !string.IsNullOrWhiteSpace(cellD)) exMgr = cellD;
                    if (cellC.Contains("반출 가능일") && !string.IsNullOrWhiteSpace(cellD))
                    {
                        var m = Regex.Match(cellD, @"([0-9]{2,4}[-/.][0-9]{1,2}[-/.][0-9]{1,2})");
                        if (m.Success) DateTime.TryParse(m.Groups[1].Value, out exDate);
                    }
                }

                if (isList)
                {
                    if (cellB.Contains("Total", StringComparison.OrdinalIgnoreCase) || cellC.Contains("Total", StringComparison.OrdinalIgnoreCase)) { isList = false; continue; }
                    if (string.IsNullOrEmpty(cellC) || cellC.Contains("Part's Name")) continue;

                    int qty = 1;
                    if (int.TryParse(cellF, out int parsedQty) && parsedQty > 0) qty = parsedQty;

                    for (int i = 0; i < qty; i++)
                    {
                        Dispatcher.Invoke(() => AetsPreviewList.Add(new AetsPreviewModel
                        {
                            PartName = cellC,
                            ItemCode = cellD,
                            CompanyName = exCom,
                            ManagerName = exMgr,
                            OutDate = exDate
                        }));
                    }
                }
            }
        }

        private void ParseExcelData(string path)
        {
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(1);
            DateTime exDate = DateTime.Today;
            string exMgr = "", exCom = "AETS";

            foreach (var row in ws.RowsUsed())
            {
                string c3 = row.Cell("C").GetString().Trim();
                string d3 = row.Cell("D").GetString().Trim();
                if (c3.Contains("업체")) exCom = d3;
                if (c3.Contains("담당자")) exMgr = d3;
                if (c3.Contains("반출 가능일"))
                {
                    var m = Regex.Match(d3, @"([0-9]{2,4}[-/.][0-9]{1,2}[-/.][0-9]{1,2})");
                    if (m.Success) DateTime.TryParse(m.Groups[1].Value, out exDate);
                }
            }

            bool isList = false;
            for (int r = 1; r <= ws.LastRowUsed().RowNumber(); r++)
            {
                string b = ws.Cell(r, "B").GetString();
                if (b.Contains("반출 LIST")) { isList = true; r++; continue; }
                if (isList)
                {
                    if (b.Contains("Total", StringComparison.OrdinalIgnoreCase) || ws.Cell(r, "C").GetString().Contains("Total", StringComparison.OrdinalIgnoreCase)) break;

                    string part = ws.Cell(r, "C").GetString().Trim();
                    string code = ws.Cell(r, "D").GetString().Trim();
                    string qtyStr = ws.Cell(r, "F").GetString().Trim();

                    if (string.IsNullOrEmpty(part) && string.IsNullOrEmpty(code)) continue;
                    if (part == "Part's Name") continue;

                    int qty = 1;
                    if (int.TryParse(qtyStr, out int parsedQty) && parsedQty > 0) qty = parsedQty;

                    for (int i = 0; i < qty; i++)
                    {
                        Dispatcher.Invoke(() => AetsPreviewList.Add(new AetsPreviewModel
                        {
                            PartName = part,
                            ItemCode = code,
                            CompanyName = exCom,
                            ManagerName = exMgr,
                            OutDate = exDate
                        }));
                    }
                }
            }
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(MasterDbPath) || !File.Exists(MasterDbPath))
            {
                MessageBox.Show("마스터 DB 경로가 설정되지 않았습니다. [업체 관리 -> 설정]에서 먼저 지정해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnRun.IsEnabled = false;
            BtnRun.Content = "처리 중...";
            Logs.Clear();

            try
            {
                DispatchBatchResult result;

                if (_isAetsMode)
                {
                    var targets = AetsPreviewList.Where(x => x.IsChecked).ToList();
                    if (targets.Count == 0) { MessageBox.Show("체크된 항목이 없습니다."); return; }
                    result = await Task.Run(() => ExecuteAetsBatch(MasterDbPath, targets));
                }
                else
                {
                    result = await Task.Run(() => ExecuteStandardBatch(MasterDbPath));
                }

                foreach (var log in result.Logs) Logs.Add(log);
                MessageBox.Show($"작업 완료! 새로 생성된 파일: {result.TotalCreated}개", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show($"처리 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally
            {
                BtnRun.IsEnabled = true;
                BtnRun.Content = "성적서 자동 생성 실행";
            }
        }

        private static DispatchBatchResult ExecuteAetsBatch(string masterPath, List<AetsPreviewModel> items)
        {
            var vendors = VendorStore.Load();
            using var workbook = new XLWorkbook(masterPath);
            var wsReg = workbook.Worksheets.FirstOrDefault(w => w.Name == "반출등록");
            var wsHist = workbook.Worksheets.FirstOrDefault(w => w.Name == "생성이력");
            var output = new DispatchBatchResult();

            if (wsReg == null || wsHist == null) throw new InvalidOperationException("마스터 DB 시트(반출등록/생성이력)를 찾을 수 없습니다.");

            int totalCreated = 0;
            var seqTracker = new Dictionary<string, int>();

            foreach (var item in items)
            {
                string drawingNo = item.ItemCode;
                string templatePath = "";
                string basePath = "";
                string fileNameRule = "";

                var vendor = vendors.FirstOrDefault(v => string.Equals(v.VendorName, item.CompanyName, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(v.VendorName) && item.CompanyName.Contains(v.VendorName)));

                if (vendor != null)
                {
                    var tpl = vendor.Templates.FirstOrDefault(t => t.IsUsed && string.Equals(t.ItemCode, drawingNo, StringComparison.OrdinalIgnoreCase));
                    if (tpl != null)
                    {
                        templatePath = tpl.TemplatePath;
                        basePath = tpl.BasePath;
                        fileNameRule = tpl.FileNameRule;
                    }
                }

                if (string.IsNullOrWhiteSpace(templatePath) || string.IsNullOrWhiteSpace(basePath))
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = "-", RegNo = "AETS-신규", Result = "실패", Message = $"[{item.PartName}] 업체관리(VendorStore) 템플릿 매칭 실패" });
                    continue;
                }

                if (!File.Exists(templatePath))
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = "-", RegNo = "AETS-신규", Result = "실패", Message = $"템플릿 파일 없음: {templatePath}" });
                    continue;
                }

                string yearFolder = Path.Combine(basePath, $"{item.OutDate:yy}년");
                string monthFolder = Path.Combine(yearFolder, $"{item.OutDate.Month}월");
                string dayFolder = Path.Combine(monthFolder, $"{item.OutDate.Day}일");
                Directory.CreateDirectory(dayFolder);

                if (!seqTracker.ContainsKey(drawingNo)) seqTracker[drawingNo] = 0;
                seqTracker[drawingNo]++;
                int currentSeq = seqTracker[drawingNo];

                string newFileName = BuildFileName(fileNameRule, item.OutDate, item.PartName, drawingNo, currentSeq) + ".xltx";
                string newFilePath = Path.Combine(dayFolder, newFileName);

                if (!File.Exists(newFilePath))
                {
                    File.Copy(templatePath, newFilePath);
                    totalCreated++;

                    int regRow = (wsReg.LastRowUsed()?.RowNumber() ?? 1) + 1;
                    string newRegNo = $"AETS-{DateTime.Now:MMddHHmm}-{totalCreated:D3}";

                    wsReg.Cell(regRow, "A").Value = newRegNo;
                    wsReg.Cell(regRow, "B").Value = item.OutDate;
                    wsReg.Cell(regRow, "C").Value = item.CompanyName;
                    wsReg.Cell(regRow, "D").Value = "기타세정";
                    wsReg.Cell(regRow, "E").Value = item.PartName;
                    wsReg.Cell(regRow, "F").Value = 1;
                    wsReg.Cell(regRow, "G").Value = item.ManagerName;
                    wsReg.Cell(regRow, "H").Value = drawingNo;
                    wsReg.Cell(regRow, "I").Value = "생성완료";
                    wsReg.Cell(regRow, "J").Value = dayFolder;
                    wsReg.Cell(regRow, "K").Value = 1;

                    int histRow = (wsHist.LastRowUsed()?.RowNumber() ?? 1) + 1;
                    wsHist.Cell(histRow, "A").Value = DateTime.Now;
                    wsHist.Cell(histRow, "B").Value = newRegNo;
                    wsHist.Cell(histRow, "C").Value = item.OutDate;
                    wsHist.Cell(histRow, "D").Value = item.CompanyName;
                    wsHist.Cell(histRow, "E").Value = "기타세정";
                    wsHist.Cell(histRow, "F").Value = item.PartName;
                    wsHist.Cell(histRow, "G").Value = 1;
                    wsHist.Cell(histRow, "H").Value = item.ManagerName;
                    wsHist.Cell(histRow, "I").Value = dayFolder;
                    wsHist.Cell(histRow, "J").Value = "성공";

                    output.Logs.Add(new DispatchCreationLog { RowNo = "-", RegNo = newRegNo, Result = "성공", Message = $"[{item.PartName}] 서식 생성 및 DB 등록 완료" });
                }
                else
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = "-", RegNo = "AETS-중복", Result = "건너뜀", Message = $"이미 파일 존재: {newFileName}" });
                }
            }

            workbook.Save();
            output.TotalCreated = totalCreated;
            return output;
        }

        private static DispatchBatchResult ExecuteStandardBatch(string workbookPath)
        {
            var vendors = VendorStore.Load();
            using var workbook = new XLWorkbook(workbookPath);
            var wsReg = workbook.Worksheets.FirstOrDefault(w => w.Name == "반출등록");
            var wsHist = workbook.Worksheets.FirstOrDefault(w => w.Name == "생성이력");

            if (wsReg == null || wsHist == null) throw new InvalidOperationException("시트 이름을 찾을 수 없습니다. (반출등록/생성이력 확인)");

            int totalCreated = 0;
            int lastRow = wsReg.LastRowUsed()?.RowNumber() ?? 1;
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
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow.ToString(), RegNo = regNo, Result = "건너뜀", Message = "필수값 없음 또는 생성완료" });
                    continue;
                }

                if (!TryGetDate(wsReg.Cell(targetRow, "B").Value, out DateTime outDate))
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow.ToString(), RegNo = regNo, Result = "건너뜀", Message = "반출일 형식 오류" });
                    continue;
                }

                string templatePath = "";
                string basePath = "";
                string fileNameRule = "";

                var vendor = vendors.FirstOrDefault(v => string.Equals(v.VendorName, companyName, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(v.VendorName) && companyName.Contains(v.VendorName)));
                if (vendor != null)
                {
                    var tpl = vendor.Templates.FirstOrDefault(t => t.IsUsed && string.Equals(t.ItemCode, drawingNo, StringComparison.OrdinalIgnoreCase));
                    if (tpl != null)
                    {
                        templatePath = tpl.TemplatePath;
                        basePath = tpl.BasePath;
                        fileNameRule = tpl.FileNameRule;
                    }
                }

                if (string.IsNullOrWhiteSpace(templatePath) || string.IsNullOrWhiteSpace(basePath))
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow.ToString(), RegNo = regNo, Result = "건너뜀", Message = "업체관리(VendorStore) 템플릿 매칭 실패" });
                    continue;
                }

                if (!File.Exists(templatePath))
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow.ToString(), RegNo = regNo, Result = "실패", Message = $"템플릿 파일 없음: {templatePath}" });
                    continue;
                }

                string yearFolder = Path.Combine(basePath, $"{outDate:yy}년");
                string monthFolder = Path.Combine(yearFolder, $"{outDate.Month}월");
                string dayFolder = Path.Combine(monthFolder, $"{outDate.Day}일");
                Directory.CreateDirectory(dayFolder);

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
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow.ToString(), RegNo = regNo, Result = "성공", Message = $"{createdCount}개 생성" });
                }
                else
                {
                    output.Logs.Add(new DispatchCreationLog { RowNo = targetRow.ToString(), RegNo = regNo, Result = "건너뜀", Message = "신규 생성 파일 없음" });
                }
            }

            workbook.Save();
            output.TotalCreated = totalCreated;
            return output;
        }

        private static bool TryGetDate(object raw, out DateTime result)
        {
            if (raw is DateTime dt) { result = dt; return true; }
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