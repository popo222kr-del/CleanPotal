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
using System.Windows.Data;
using ClosedXML.Excel;

namespace CleanPotal
{
    public class AetsPreviewModel : INotifyPropertyChanged
    {
        private bool _isChecked = true;
        public bool IsChecked { get => _isChecked; set { _isChecked = value; OnPropertyChanged(); } }

        private string _productCode = "";
        public string ProductCode { get => _productCode; set { _productCode = value; OnPropertyChanged(); } }

        private DateTime _outDate = DateTime.Today;
        public DateTime OutDate { get => _outDate; set { _outDate = value; OnPropertyChanged(); } }

        private string _companyName = "";
        public string CompanyName { get => _companyName; set { _companyName = value; OnPropertyChanged(); } }

        private string _partName = "";
        public string PartName { get => _partName; set { _partName = value; OnPropertyChanged(); } }

        private string _itemCode = "";
        public string ItemCode { get => _itemCode; set { _itemCode = value; OnPropertyChanged(); } }

        private int _qty = 1;
        public int Qty { get => _qty; set { _qty = value; OnPropertyChanged(); } }

        private string _managerName = "";
        public string ManagerName { get => _managerName; set { _managerName = value; OnPropertyChanged(); } }

        private string _processName = "";
        public string ProcessName { get => _processName; set { _processName = value; OnPropertyChanged(); } }

        private string _productStatus = "";
        public string ProductStatus { get => _productStatus; set { _productStatus = value; OnPropertyChanged(); } }

        private string _result = "대기";
        public string Result { get => _result; set { _result = value; OnPropertyChanged(); } }

        private string _message = "-";
        public string Message { get => _message; set { _message = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class DispatchHistoryModel
    {
        public string CreateDate { get; set; } = "";
        public string RegNo { get; set; } = "";
        public string OutDate { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string ProductType { get; set; } = "";
        public string PartName { get; set; } = "";
        public string Qty { get; set; } = "";
        public string ManagerName { get; set; } = "";
        public string SavePath { get; set; } = "";
        public string Result { get; set; } = "";
    }

    public partial class DispatchCertificateBatchView : UserControl
    {
        public ObservableCollection<AetsPreviewModel> AetsPreviewList { get; } = new();
        public ObservableCollection<DispatchHistoryModel> HistoryList { get; } = new();
        private ICollectionView _historyView;

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
            AetsPreviewGrid.ItemsSource = AetsPreviewList;
            _historyView = CollectionViewSource.GetDefaultView(HistoryList);
            if (_historyView != null) _historyView.Filter = HistoryFilter;
            HistoryGrid.ItemsSource = _historyView;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) { }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            AetsPreviewList.Clear();
            TxtAetsStatus.Text = "파일을 놓으면 즉시 분석을 시작합니다.";
            TxtSummaryCompany.Text = "-";
            TxtSummaryManager.Text = "-";
            TxtSummaryPartName.Text = "-";
            TxtSummaryTotalQty.Text = "-"; // 총 수량 초기화
            TxtSummaryProcess.Text = "-";
            TxtSummaryStatus.Text = "-";
            TxtSummaryDate.Text = "-";
        }

        private void BtnShowHistory_Click(object sender, RoutedEventArgs e) { LoadHistoryData(); HistoryOverlay.Visibility = Visibility.Visible; }
        private void BtnCloseHistory_Click(object sender, RoutedEventArgs e) => HistoryOverlay.Visibility = Visibility.Collapsed;
        private void BtnRefreshHistory_Click(object sender, RoutedEventArgs e) => LoadHistoryData();
        private void TxtHistorySearch_TextChanged(object sender, TextChangedEventArgs e) => _historyView?.Refresh();

        private bool HistoryFilter(object obj)
        {
            if (obj is DispatchHistoryModel h)
            {
                string kw = TxtHistorySearch.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(kw)) return true;
                return (h.CompanyName?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true) ||
                       (h.PartName?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true);
            }
            return false;
        }

        private void LoadHistoryData()
        {
            HistoryList.Clear();
            if (string.IsNullOrEmpty(MasterDbPath) || !File.Exists(MasterDbPath)) return;
            try
            {
                using var wb = new XLWorkbook(MasterDbPath);
                var ws = wb.Worksheets.FirstOrDefault(w => w.Name == "생성이력");
                if (ws == null) return;
                int last = ws.LastRowUsed()?.RowNumber() ?? 1;
                for (int r = last; r >= 2; r--)
                {
                    HistoryList.Add(new DispatchHistoryModel
                    {
                        CreateDate = ws.Cell(r, "A").Value.ToString(),
                        RegNo = ws.Cell(r, "B").Value.ToString(),
                        OutDate = ws.Cell(r, "C").Value.ToString(),
                        CompanyName = ws.Cell(r, "D").Value.ToString(),
                        ProductType = ws.Cell(r, "E").Value.ToString(),
                        PartName = ws.Cell(r, "F").Value.ToString(),
                        Qty = ws.Cell(r, "G").Value.ToString(),
                        ManagerName = ws.Cell(r, "H").Value.ToString(),
                        SavePath = ws.Cell(r, "I").Value.ToString(),
                        Result = ws.Cell(r, "J").Value.ToString()
                    });
                }
            }
            catch { }
        }

        private void AetsPanel_PreviewDragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }

        private async void AetsPanel_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string path = files.FirstOrDefault(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xltx", StringComparison.OrdinalIgnoreCase)) ?? "";
                if (!string.IsNullOrEmpty(path))
                {
                    TxtAetsStatus.Text = "데이터 분석 중...";
                    await ParseAetsFileAsync(path);
                    TxtAetsStatus.Text = $"[{Path.GetFileName(path)}] 분석 완료.";
                }
            }
        }

        private async Task ParseAetsFileAsync(string aetsPath)
        {
            AetsPreviewList.Clear();
            try
            {
                var data = await Task.Run(() => ParseExcelData(aetsPath));
                foreach (var item in data.Items) AetsPreviewList.Add(item);

                TxtSummaryCompany.Text = string.IsNullOrEmpty(data.Company) ? "-" : data.Company;
                TxtSummaryManager.Text = string.IsNullOrEmpty(data.Manager) ? "-" : data.Manager;
                TxtSummaryProcess.Text = string.IsNullOrEmpty(data.Process) ? "-" : data.Process;
                TxtSummaryStatus.Text = string.IsNullOrEmpty(data.Status) ? "-" : data.Status;
                TxtSummaryDate.Text = data.Date.ToString("yyyy-MM-dd");

                if (data.Items.Count > 0)
                {
                    var uniqueParts = data.Items.Select(x => x.PartName).Distinct().ToList();
                    if (uniqueParts.Count == 1)
                        TxtSummaryPartName.Text = uniqueParts[0];
                    else
                        TxtSummaryPartName.Text = $"{uniqueParts[0]} 외 {uniqueParts.Count - 1}건";

                    // 🔥 총 수량 합산 반영
                    int totalQty = data.Items.Sum(x => x.Qty);
                    TxtSummaryTotalQty.Text = $"{totalQty} EA";
                }
                else
                {
                    TxtSummaryPartName.Text = "-";
                    TxtSummaryTotalQty.Text = "-";
                }

            }
            catch (Exception ex) { MessageBox.Show("파일 읽기 실패: " + ex.Message); }
        }

        private static (List<AetsPreviewModel> Items, string Company, string Manager, DateTime Date, string Process, string Status) ParseExcelData(string path)
        {
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(1);
            DateTime exDate = DateTime.Today;
            string exMgr = "", exCom = "", exProc = "", exStat = "";
            var parsed = new List<AetsPreviewModel>();

            string GetMValue(IXLRow r, params string[] cs) { foreach (var c in cs) { string v = r.Cell(c).Value.ToString().Trim(); if (!string.IsNullOrEmpty(v)) return v; } return ""; }

            foreach (var row in ws.RowsUsed())
            {
                string cN = row.Cell("C").Value.ToString().Replace(" ", "");
                string dV = GetMValue(row, "D", "E", "F", "G", "H");
                if (cN.Contains("업체")) exCom = dV;
                if (cN.Contains("담당자")) exMgr = dV;
                if (cN.Contains("사용공정")) exProc = dV;
                if (cN.Contains("제품상태")) exStat = dV;
                if (cN.Contains("반출가능일") || cN.Contains("의뢰일"))
                {
                    var m = Regex.Match(dV, @"([0-9]{2,4}[-/.][0-9]{1,2}[-/.][0-9]{1,2})");
                    if (m.Success) DateTime.TryParse(m.Groups[1].Value, out exDate);
                }
            }

            bool isL = false;
            for (int r = 1; r <= (ws.LastRowUsed()?.RowNumber() ?? 1000); r++)
            {
                string bN = ws.Cell(r, "B").Value.ToString().Replace(" ", "").ToUpper();
                if (bN.Contains("반출LIST") || bN == "NO") { isL = true; continue; }
                if (isL)
                {
                    if (bN.Contains("TOTAL") || bN.Contains("합계") || bN.Contains("반출정보")) break;
                    if (!int.TryParse(ws.Cell(r, "B").Value.ToString(), out _)) continue;
                    string code = ws.Cell(r, "C").Value.ToString().Trim();
                    string part = ws.Cell(r, "D").Value.ToString().Trim();
                    string type = ws.Cell(r, "F").Value.ToString().Trim();
                    int qty = int.TryParse(ws.Cell(r, "G").Value.ToString(), out int q) ? q : 1;

                    if (!string.IsNullOrEmpty(part) || !string.IsNullOrEmpty(code))
                    {
                        parsed.Add(new AetsPreviewModel { ProductCode = type, PartName = part, ItemCode = code, Qty = qty, CompanyName = exCom, ManagerName = exMgr, ProcessName = exProc, ProductStatus = exStat, OutDate = exDate });
                    }
                }
            }
            return (parsed, exCom, exMgr, exDate, exProc, exStat);
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(MasterDbPath) || !File.Exists(MasterDbPath)) { MessageBox.Show("마스터 DB 설정이 필요합니다."); return; }
            var targets = AetsPreviewList.Where(x => x.IsChecked).ToList();
            if (targets.Count == 0) return;

            BtnRun.IsEnabled = false; BtnRun.Content = "처리 중...";
            foreach (var t in targets) { t.Result = "처리중..."; t.Message = "대기"; }

            try { int count = await Task.Run(() => ExecuteAetsBatch(MasterDbPath, targets)); MessageBox.Show($"작업 완료! 생성된 파일: {count}개"); }
            catch (Exception ex) { MessageBox.Show("오류: " + ex.Message); }
            finally { BtnRun.IsEnabled = true; BtnRun.Content = "성적서 자동 생성 실행"; }
        }

        private static string Clean(string s) { if (string.IsNullOrWhiteSpace(s)) return ""; string iv = new string(Path.GetInvalidFileNameChars()); foreach (char c in iv) s = s.Replace(c.ToString(), "_"); return s.Trim(); }

        private static int ExecuteAetsBatch(string masterPath, List<AetsPreviewModel> items)
        {
            var vendors = VendorStore.Load();
            var globalTpls = VendorStore.LoadGlobalTemplates();
            using var wbMaster = new XLWorkbook(masterPath);
            var wsReg = wbMaster.Worksheet("반출등록");
            var wsHist = wbMaster.Worksheet("생성이력");
            int total = 0;

            foreach (var item in items)
            {
                var vendor = vendors.FirstOrDefault(v => string.Equals(v.VendorName, item.CompanyName, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(v.VendorName) && item.CompanyName.Contains(v.VendorName)));
                var gTpl = globalTpls.FirstOrDefault(t => string.Equals(t.ProductCode?.Trim(), item.ProductCode?.Trim(), StringComparison.OrdinalIgnoreCase));

                string basePath = vendor?.BasePath ?? "";
                string tplPath = gTpl?.TemplatePath ?? "";
                string pType = gTpl?.ProductName ?? "기타세정";

                if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(tplPath) || !File.Exists(tplPath)) { item.Result = "실패"; item.Message = "경로 또는 템플릿 누락"; continue; }

                string safeMgr = Clean(item.ManagerName);
                string safeProc = Clean(item.ProcessName);
                string sub = safeMgr + (string.IsNullOrEmpty(safeProc) ? "" : " (" + safeProc + ")");

                string yearFolder = Path.Combine(basePath, $"{item.OutDate:yy}년");
                string monthFolder = Path.Combine(yearFolder, $"{item.OutDate.Month}월");
                string dayFolder = Path.Combine(monthFolder, $"{item.OutDate.Day}일");
                string finalPath = string.IsNullOrEmpty(sub) ? dayFolder : Path.Combine(dayFolder, sub);

                Directory.CreateDirectory(finalPath);

                if (item.ProductCode?.ToUpper() == "D")
                {
                    int totalQty = item.Qty;
                    int groupCount = (int)Math.Ceiling(totalQty / 10.0);

                    for (int g = 1; g <= groupCount; g++)
                    {
                        int currentGroupQty = (g == groupCount) ? (totalQty - (g - 1) * 10) : 10;
                        int startSN = (g - 1) * 10 + 1;
                        int endSN = startSN + currentGroupQty - 1;

                        string fileName = $"{item.PartName} ({item.ItemCode})_{currentGroupQty}EA_{g}.xlsx";
                        string fullFilePath = Path.Combine(finalPath, fileName);

                        if (!File.Exists(fullFilePath))
                        {
                            File.Copy(tplPath, fullFilePath);
                            FillPlateReportData(fullFilePath, item, currentGroupQty, startSN, endSN);

                            total++;
                            string regNo = $"AETS-{DateTime.Now:MMddHHmm}-{total:D3}";
                            RegisterToDB(wsReg, wsHist, item, regNo, currentGroupQty, finalPath, pType, total);
                        }
                    }
                }
                else
                {
                    for (int i = 1; i <= item.Qty; i++)
                    {
                        string suffix = item.Qty > 1 ? $"_{i}" : "";
                        string fileName = $"{item.PartName} ({item.ItemCode}){suffix}.xlsx";
                        string fullFilePath = Path.Combine(finalPath, fileName);

                        if (!File.Exists(fullFilePath))
                        {
                            File.Copy(tplPath, fullFilePath);

                            FillReportData(fullFilePath, item);

                            total++;
                            string regNo = $"AETS-{DateTime.Now:MMddHHmm}-{total:D3}";
                            RegisterToDB(wsReg, wsHist, item, regNo, 1, finalPath, pType, total);
                        }
                    }
                }
                item.Result = "성공";
                item.Message = $"{item.Qty}개 생성 및 데이터 매핑 완료";
            }
            wbMaster.Save();
            return total;
        }

        private static void RegisterToDB(IXLWorksheet wsReg, IXLWorksheet wsHist, AetsPreviewModel item, string regNo, int qty, string path, string pType, int total)
        {
            int rIdx = (wsReg.LastRowUsed()?.RowNumber() ?? 1) + 1;
            wsReg.Cell(rIdx, "A").Value = regNo; wsReg.Cell(rIdx, "B").Value = item.OutDate;
            wsReg.Cell(rIdx, "C").Value = item.CompanyName; wsReg.Cell(rIdx, "D").Value = pType;
            wsReg.Cell(rIdx, "E").Value = item.PartName; wsReg.Cell(rIdx, "F").Value = qty;
            wsReg.Cell(rIdx, "G").Value = item.ManagerName; wsReg.Cell(rIdx, "H").Value = item.ItemCode;
            wsReg.Cell(rIdx, "I").Value = "생성완료"; wsReg.Cell(rIdx, "J").Value = path;

            int hIdx = (wsHist.LastRowUsed()?.RowNumber() ?? 1) + 1;
            wsHist.Cell(hIdx, "A").Value = DateTime.Now; wsHist.Cell(hIdx, "B").Value = regNo;
            wsHist.Cell(hIdx, "C").Value = item.OutDate; wsHist.Cell(hIdx, "D").Value = item.CompanyName;
            wsHist.Cell(hIdx, "E").Value = pType; wsHist.Cell(hIdx, "F").Value = item.PartName;
            wsHist.Cell(hIdx, "G").Value = qty; wsHist.Cell(hIdx, "H").Value = item.ManagerName;
            wsHist.Cell(hIdx, "I").Value = path; wsHist.Cell(hIdx, "J").Value = "성공";
        }

        private static void FillPlateReportData(string filePath, AetsPreviewModel item, int groupQty, int startSN, int endSN)
        {
            try
            {
                using var wb = new XLWorkbook(filePath);
                var ws = wb.Worksheet(1);

                foreach (var row in ws.RowsUsed())
                {
                    for (int c = 1; c <= 20; c++)
                    {
                        string cellText = row.Cell(c).Value.ToString().Replace(" ", "");

                        if (cellText.Contains("Customer")) row.Cell("D").Value = item.CompanyName;
                        if (cellText.Contains("품목코드")) row.Cell("D").Value = item.ItemCode;
                        if (cellText.Contains("PartName")) row.Cell("D").Value = item.PartName;
                        if (cellText.Contains("제품상태")) row.Cell("L").Value = item.ProductStatus;
                        if (cellText.Contains("Process")) row.Cell("L").Value = item.ProcessName;
                        if (cellText.Contains("Remark")) row.Cell("D").Value = $"{groupQty} EA";

                        if (cellText.Contains("Weight(g)") || cellText.Contains("Particle"))
                        {
                            int startRow = row.RowNumber();
                            for (int i = 0; i < groupQty; i++)
                            {
                                ws.Cell(startRow + i, "C").Value = $"{item.ItemCode}-{startSN + i}";
                            }
                        }
                    }
                }
                wb.Save();
            }
            catch { }
        }

        private static void FillReportData(string filePath, AetsPreviewModel item)
        {
            try
            {
                using var wb = new XLWorkbook(filePath);
                var ws = wb.Worksheet(1);

                foreach (var row in ws.RowsUsed())
                {
                    for (int c = 1; c <= 20; c++)
                    {
                        string cellText = row.Cell(c).Value.ToString().Replace(" ", "");

                        if (cellText.Contains("Customer")) row.Cell("D").Value = item.CompanyName;
                        if (cellText.Contains("품목코드")) row.Cell("D").Value = item.ItemCode;
                        if (cellText.Contains("PartName")) row.Cell("D").Value = item.PartName;
                        if (cellText.Contains("제품상태")) row.Cell("L").Value = item.ProductStatus;
                        if (cellText.Contains("Process")) row.Cell("L").Value = item.ProcessName;
                    }
                }
                wb.Save();
            }
            catch { }
        }
    }
}