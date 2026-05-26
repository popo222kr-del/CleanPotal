using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;
using Excel = Microsoft.Office.Interop.Excel;

namespace CleanPotal
{
    public class ReportTaskModel : INotifyPropertyChanged
    {
        public string LotNumber { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string SourceFilePath { get; set; } = "";
        public string FileType { get; set; } = "MES";

        private string _status = "대기중";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class ReportAutomationView : UserControl
    {
        // 양쪽 카드의 리스트 모델을 완벽하게 분리
        public ObservableCollection<ReportTaskModel> MesTaskList { get; set; } = new ObservableCollection<ReportTaskModel>();
        public ObservableCollection<ReportTaskModel> DirectTaskList { get; set; } = new ObservableCollection<ReportTaskModel>();
        public ObservableCollection<ReportTaskModel> PrintTaskList { get; set; } = new ObservableCollection<ReportTaskModel>();

        // 🔥 요청: 신규/기존 경로 분리 적용
        private readonly string NEW_SOURCE_DIR = @"\\10.10.40.98\nas\00.MESServer\Inspection_cov\Ori\";
        private readonly string OLD_SOURCE_DIR = @"\\10.10.40.98\nas\00.MESServer\Inspection\";

        private readonly string DEST_DIR = @"\\10.10.40.98\천안공장\25. 생산 Inform 자료\주언\1.성적서 복사 및 생성\";

        public ReportAutomationView()
        {
            InitializeComponent();
            MesDataGrid.ItemsSource   = MesTaskList;
            DirectDataGrid.ItemsSource = DirectTaskList;
            PrintDataGrid.ItemsSource  = PrintTaskList;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus();
        }

        // ==========================================
        // 1. [좌측] NAS 성적서 자동 변환 (MES) 로직
        // ==========================================
        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteDataFromClipboard();
                e.Handled = true;
            }
        }

        private void PasteDataFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string clipboardText = Clipboard.GetText();
                    string[] rows = clipboardText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string row in rows)
                    {
                        string[] cols = row.Split('\t');
                        if (cols.Length >= 2)
                        {
                            string lot = cols[0].Trim();
                            string sn = cols[1].Trim();

                            if (!string.IsNullOrEmpty(lot) && !string.IsNullOrEmpty(sn))
                            {
                                MesTaskList.Add(new ReportTaskModel { LotNumber = lot, SerialNumber = sn, Status = "대기중", FileType = "MES" });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"붙여넣기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearMes_Click(object sender, RoutedEventArgs e) => MesTaskList.Clear();

        private async void BtnRunMes_Click(object sender, RoutedEventArgs e)
        {
            if (MesTaskList.Count == 0)
            {
                MessageBox.Show("작업할 MES 데이터가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool makePdf = ChkCreatePdfMes.IsChecked == true;
            BtnRunMes.IsEnabled = false; BtnRunMes.Content = "⏳ 작업 진행 중...";

            if (!Directory.Exists(DEST_DIR))
            {
                try { Directory.CreateDirectory(DEST_DIR); }
                catch { MessageBox.Show("목적지 폴더에 접근할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error); BtnRunMes.IsEnabled = true; BtnRunMes.Content = "성적서 변환 실행"; return; }
            }

            await Task.Run(() =>
            {
                Excel.Application excelApp = null;
                try
                {
                    if (makePdf) excelApp = new Excel.Application { Visible = false, DisplayAlerts = false };

                    foreach (var task in MesTaskList)
                    {
                        if (task.Status.Contains("성공")) continue;
                        task.Status = "진행중...";

                        // 🔥 요청: 1순위(신규경로), 2순위(기존경로) 순차 확인 로직 적용
                        string sourceFileNew = Path.Combine(NEW_SOURCE_DIR, $"{task.LotNumber}.xlsx");
                        string sourceFileOld = Path.Combine(OLD_SOURCE_DIR, $"{task.LotNumber}.xlsx");
                        string targetSourceFile = "";

                        if (File.Exists(sourceFileNew))
                        {
                            targetSourceFile = sourceFileNew;
                        }
                        else if (File.Exists(sourceFileOld))
                        {
                            targetSourceFile = sourceFileOld;
                        }
                        else
                        {
                            task.Status = "원본 없음";
                            continue;
                        }

                        string destExcelFile = Path.Combine(DEST_DIR, $"{task.SerialNumber}.xlsx");
                        string destPdfFile = Path.Combine(DEST_DIR, $"{task.SerialNumber}.pdf");

                        try
                        {
                            File.Copy(targetSourceFile, destExcelFile, true);

                            if (makePdf && excelApp != null)
                            {
                                Excel.Workbook wb = excelApp.Workbooks.Open(destExcelFile);
                                wb.ExportAsFixedFormat(Excel.XlFixedFormatType.xlTypePDF, destPdfFile);
                                wb.Close(false);
                                task.Status = "성공 (PDF 완료)";
                            }
                            else
                            {
                                task.Status = "성공 (엑셀만)";
                            }
                        }
                        catch { task.Status = "오류 발생"; }
                    }
                }
                finally
                {
                    if (excelApp != null) { excelApp.Quit(); System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp); }
                }
            });

            BtnRunMes.IsEnabled = true; BtnRunMes.Content = "성적서 변환 실행";
            MessageBox.Show("MES 성적서 변환 작업이 완료되었습니다.", "작업 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==========================================
        // 2. [우측] 다이렉트 파일 PDF 변환 로직
        // ==========================================
        private void FileDropZone_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void FileDropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var file in files)
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".xls" || ext == ".xlsx" || ext == ".ppt" || ext == ".pptx")
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        DirectTaskList.Add(new ReportTaskModel
                        {
                            LotNumber = fileName,
                            SerialNumber = "-", // 다이렉트 변환은 파일 이름 변경 없이 원본 유지
                            Status = "대기중",
                            SourceFilePath = file,
                            FileType = ext.Contains("ppt") ? "PPT" : "Excel"
                        });
                    }
                }
            }
            e.Handled = true;
        }

        private void BtnClearDirect_Click(object sender, RoutedEventArgs e) => DirectTaskList.Clear();

        private async void BtnRunDirect_Click(object sender, RoutedEventArgs e)
        {
            if (DirectTaskList.Count == 0)
            {
                MessageBox.Show("변환할 파일이 없습니다. 파일을 우측 카드에 드래그하여 추가해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnRunDirect.IsEnabled = false; BtnRunDirect.Content = "⏳ 작업 진행 중...";

            if (!Directory.Exists(DEST_DIR))
            {
                try { Directory.CreateDirectory(DEST_DIR); }
                catch { MessageBox.Show("목적지 폴더에 접근할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error); BtnRunDirect.IsEnabled = true; BtnRunDirect.Content = "다이렉트 변환 실행"; return; }
            }

            await Task.Run(() =>
            {
                Excel.Application excelApp = null;
                dynamic pptApp = null; // 참조 없이 PPT 제어용 dynamic 바인딩

                try
                {
                    foreach (var task in DirectTaskList)
                    {
                        if (task.Status.Contains("성공")) continue;
                        task.Status = "진행중...";

                        string sourceFile = task.SourceFilePath;
                        string destPdfFile = Path.Combine(DEST_DIR, $"{task.LotNumber}.pdf");

                        try
                        {
                            if (task.FileType == "PPT")
                            {
                                if (pptApp == null)
                                {
                                    Type pptType = Type.GetTypeFromProgID("PowerPoint.Application");
                                    if (pptType != null) pptApp = Activator.CreateInstance(pptType);
                                }

                                if (pptApp != null)
                                {
                                    dynamic ppt = pptApp.Presentations.Open(sourceFile, -1, 0, 0);
                                    ppt.SaveAs(destPdfFile, 32); // 32 = PDF
                                    ppt.Close();
                                    task.Status = "성공 (PDF 완료)";
                                }
                                else { task.Status = "오류 발생"; }
                            }
                            else
                            {
                                if (excelApp == null) excelApp = new Excel.Application { Visible = false, DisplayAlerts = false };

                                Excel.Workbook wb = excelApp.Workbooks.Open(sourceFile);
                                wb.ExportAsFixedFormat(Excel.XlFixedFormatType.xlTypePDF, destPdfFile);
                                wb.Close(false);
                                task.Status = "성공 (PDF 완료)";
                            }
                        }
                        catch { task.Status = "오류 발생"; }
                    }
                }
                finally
                {
                    if (excelApp != null) { excelApp.Quit(); System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp); }
                    if (pptApp != null) { pptApp.Quit(); System.Runtime.InteropServices.Marshal.ReleaseComObject(pptApp); }
                }
            });

            BtnRunDirect.IsEnabled = true; BtnRunDirect.Content = "변환 실행";
            MessageBox.Show("다이렉트 파일 PDF 변환 작업이 완료되었습니다.", "작업 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==========================================
        // 3. 일괄출력 로직
        // ==========================================
        private void PrintDropZone_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void PrintDropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            foreach (var file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                string fileType = ext switch
                {
                    ".xls" or ".xlsx" => "Excel",
                    ".ppt" or ".pptx" => "PPT",
                    ".pdf"            => "PDF",
                    _                 => ""
                };
                if (string.IsNullOrEmpty(fileType)) continue;

                PrintTaskList.Add(new ReportTaskModel
                {
                    LotNumber      = Path.GetFileName(file),
                    SourceFilePath = file,
                    FileType       = fileType,
                    Status         = "대기중"
                });
            }
            e.Handled = true;
        }

        private void BtnClearPrint_Click(object sender, RoutedEventArgs e) => PrintTaskList.Clear();

        private async void BtnRunPrint_Click(object sender, RoutedEventArgs e)
        {
            if (PrintTaskList.Count == 0)
            {
                MessageBox.Show("출력할 파일이 없습니다. 파일을 드래그하여 추가해주세요.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnRunPrint.IsEnabled = false;
            BtnRunPrint.Content   = "⏳ 출력 진행 중...";

            await Task.Run(() =>
            {
                Excel.Application? excelApp = null;
                dynamic? pptApp = null;

                try
                {
                    foreach (var task in PrintTaskList)
                    {
                        if (task.Status == "출력 완료") continue;
                        task.Status = "인쇄중...";

                        try
                        {
                            switch (task.FileType)
                            {
                                case "Excel":
                                    excelApp ??= new Excel.Application { Visible = false, DisplayAlerts = false };
                                    var wb = excelApp.Workbooks.Open(task.SourceFilePath, ReadOnly: true);
                                    wb.PrintOut();
                                    wb.Close(false);
                                    Marshal.ReleaseComObject(wb);
                                    break;

                                case "PPT":
                                    if (pptApp == null)
                                    {
                                        var pptType = Type.GetTypeFromProgID("PowerPoint.Application");
                                        if (pptType != null) pptApp = Activator.CreateInstance(pptType);
                                    }
                                    if (pptApp != null)
                                    {
                                        dynamic ppt = pptApp.Presentations.Open(task.SourceFilePath, -1, 0, 0);
                                        ppt.PrintOut();
                                        ppt.Close();
                                    }
                                    break;

                                case "PDF":
                                    PrintPdf(task.SourceFilePath);
                                    break;
                            }
                            task.Status = "출력 완료";
                        }
                        catch { task.Status = "오류 발생"; }
                    }
                }
                finally
                {
                    if (excelApp != null) { excelApp.Quit(); Marshal.ReleaseComObject(excelApp); }
                    if (pptApp   != null) { pptApp.Quit();   Marshal.ReleaseComObject(pptApp); }
                }
            });

            BtnRunPrint.IsEnabled = true;
            BtnRunPrint.Content   = "일괄출력 실행";
            MessageBox.Show("일괄출력 작업이 완료되었습니다.", "작업 완료",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // PDF 출력: 레지스트리 기본 핸들러 → Adobe Reader → SumatraPDF → 셸 print → 파일 열기
        private static void PrintPdf(string pdfPath)
        {
            // 1순위: 레지스트리에서 .pdf 기본 핸들러 실행 경로 직접 조회
            // 소프트캠프·기타 뷰어도 여기에 등록되어 있으면 자동 감지됨
            string? registryExe = GetPdfHandlerExe();
            if (registryExe != null && File.Exists(registryExe))
            {
                string lower = registryExe.ToLowerInvariant();

                if (lower.Contains("acrord32") || lower.Contains("acrobat"))
                {
                    // Adobe Reader: /t = 인쇄, /h = 숨김
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName    = registryExe,
                        Arguments   = $"/t /h \"{pdfPath}\"",
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    p?.WaitForExit(20000);
                    try { p?.Kill(); } catch { }
                    return;
                }

                if (lower.Contains("sumatra"))
                {
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName    = registryExe,
                        Arguments   = $"-print-to-default \"{pdfPath}\"",
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    p?.WaitForExit(20000);
                    return;
                }

                // 소프트캠프 또는 기타 뷰어: 셸 print 동사로 시도
                try
                {
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName        = pdfPath,
                        Verb            = "print",
                        UseShellExecute = true,
                        WindowStyle     = ProcessWindowStyle.Hidden
                    });
                    p?.WaitForExit(20000);
                    return;
                }
                catch { /* 아래 폴백으로 이어짐 */ }
            }

            // 2순위: Adobe Reader 고정 경로
            string[] acroPaths =
            {
                @"C:\Program Files (x86)\Adobe\Acrobat Reader DC\Reader\AcroRd32.exe",
                @"C:\Program Files\Adobe\Acrobat Reader DC\Reader\AcroRd32.exe",
                @"C:\Program Files (x86)\Adobe\Acrobat DC\Acrobat\Acrobat.exe",
                @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe",
            };
            foreach (var acro in acroPaths)
            {
                if (!File.Exists(acro)) continue;
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName    = acro,
                    Arguments   = $"/t /h \"{pdfPath}\"",
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                p?.WaitForExit(20000);
                try { p?.Kill(); } catch { }
                return;
            }

            // 3순위: SumatraPDF 고정 경로
            string[] sumatraPaths =
            {
                @"C:\Program Files\SumatraPDF\SumatraPDF.exe",
                @"C:\Program Files (x86)\SumatraPDF\SumatraPDF.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             @"SumatraPDF\SumatraPDF.exe"),
            };
            foreach (var sumatra in sumatraPaths)
            {
                if (!File.Exists(sumatra)) continue;
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName    = sumatra,
                    Arguments   = $"-print-to-default \"{pdfPath}\"",
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                p?.WaitForExit(20000);
                return;
            }

            // 4순위: 셸 print 동사
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName        = pdfPath,
                    Verb            = "print",
                    UseShellExecute = true,
                    WindowStyle     = ProcessWindowStyle.Hidden
                });
                p?.WaitForExit(15000);
            }
            catch
            {
                // 최후 폴백: 파일 열기
                Process.Start(new ProcessStartInfo { FileName = pdfPath, UseShellExecute = true });
                throw new InvalidOperationException("자동 출력 불가 — 파일을 열었습니다. 직접 출력해주세요.");
            }
        }

        // 레지스트리에서 .pdf 파일의 기본 핸들러 실행 파일 경로를 조회
        private static string? GetPdfHandlerExe()
        {
            try
            {
                // HKCR\.pdf → ProgID → shell\open\command
                using var extKey = Registry.ClassesRoot.OpenSubKey(".pdf");
                string? progId = extKey?.GetValue(null) as string;
                if (string.IsNullOrEmpty(progId)) return null;

                using var cmdKey = Registry.ClassesRoot.OpenSubKey(
                    $@"{progId}\shell\open\command");
                string? cmd = cmdKey?.GetValue(null) as string;
                if (string.IsNullOrEmpty(cmd)) return null;

                // 경로 파싱: "C:\path\to\app.exe" "%1" 형태
                cmd = cmd.Trim();
                if (cmd.StartsWith("\""))
                {
                    int end = cmd.IndexOf('"', 1);
                    return end > 1 ? cmd[1..end] : null;
                }
                int space = cmd.IndexOf(' ');
                return space > 0 ? cmd[..space] : cmd;
            }
            catch { return null; }
        }
    }
}