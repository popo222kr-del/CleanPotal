using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
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
        public string ProductName { get; set; } = "";
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
                            // 3열 이상: LOT / 품명 / S/N — 2열(구버전 양식): LOT / S/N (품명 공백)
                            string lot = cols[0].Trim();
                            string product = cols.Length >= 3 ? cols[1].Trim() : "";
                            string sn = (cols.Length >= 3 ? cols[2] : cols[1]).Trim();

                            if (!string.IsNullOrEmpty(lot) && !string.IsNullOrEmpty(sn))
                            {
                                MesTaskList.Add(new ReportTaskModel { LotNumber = lot, ProductName = product, SerialNumber = sn, Status = "대기중", FileType = "MES" });
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

            // 출력 설정 다이얼로그를 먼저 열어 프린터 선택
            var printDialog = new System.Windows.Controls.PrintDialog();
            if (printDialog.ShowDialog() != true) return;

            string printerName = printDialog.PrintQueue.FullName;

            // 다이얼로그에서 고른 세부 설정(급지 트레이·용지 등)을 출력 동안 프린터 기본값에 임시 반영.
            // Excel/PPT/PDF 출력은 드라이버 '기본 설정'을 따르므로 반영하지 않으면 트레이 선택이 무시된다.
            System.Printing.PrintQueue? cfgQueue = null;
            System.Printing.PrintTicket? originalTicket = null;
            try
            {
                cfgQueue = printDialog.PrintQueue;
                originalTicket = cfgQueue.DefaultPrintTicket;
                cfgQueue.DefaultPrintTicket = printDialog.PrintTicket;
                cfgQueue.Commit();
            }
            catch { cfgQueue = null; originalTicket = null; }   // 권한 등으로 실패해도 출력은 계속(기존 동작)

            BtnRunPrint.IsEnabled = false;
            BtnRunPrint.Content   = "⏳ 출력 진행 중...";

            // Excel/PPT COM은 STA 스레드에서 실행해야 함 (Task.Run은 MTA → 0x800A03EC 오류)
            var tasks = PrintTaskList.ToList();
            var tcs   = new TaskCompletionSource<bool>();

            var staThread = new Thread(() =>
            {
                Excel.Application? excelApp = null;
                dynamic? pptApp = null;

                try
                {
                    foreach (var task in tasks)
                    {
                        if (task.Status == "출력 완료") continue;
                        task.Status = "인쇄중...";

                        string? tempFile = null;
                        try
                        {
                            switch (task.FileType)
                            {
                                case "Excel":
                                    excelApp ??= new Excel.Application { Visible = false, DisplayAlerts = false };
                                    // 한글/공백 경로는 ASCII 임시 경로로 복사 후 열기
                                    string excelPath = task.SourceFilePath;
                                    if (excelPath.Any(c => c > 127) || excelPath.Contains(' '))
                                    {
                                        tempFile = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid():N}.xlsx");
                                        File.Copy(excelPath, tempFile, true);
                                        excelPath = tempFile;
                                    }
                                    var wb = excelApp.Workbooks.Open(excelPath, ReadOnly: false);
                                    // ActivePrinter 형식 문제 회피: PrintOut에 프린터 직접 전달
                                    try   { wb.PrintOut(ActivePrinter: printerName); }
                                    catch { wb.PrintOut(); } // 프린터명 형식 불일치 시 기본 프린터로 출력
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
                                        try   { ppt.PrintOut(ActivePrinter: printerName); }
                                        catch { ppt.PrintOut(); }
                                        ppt.Close();
                                    }
                                    break;

                                case "PDF":
                                    PrintPdf(task.SourceFilePath, printerName);
                                    break;
                            }
                            task.Status = "출력 완료";
                        }
                        catch (Exception ex)
                        {
                            task.Status = $"오류: {ex.Message}";
                        }
                        finally
                        {
                            if (tempFile != null) try { File.Delete(tempFile); } catch { }
                        }
                    }
                    tcs.SetResult(true);
                }
                catch (Exception ex) { tcs.SetException(ex); }
                finally
                {
                    if (excelApp != null) { try { excelApp.Quit(); } catch { } Marshal.ReleaseComObject(excelApp); }
                    if (pptApp   != null) { try { pptApp.Quit();   } catch { } Marshal.ReleaseComObject(pptApp); }
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.IsBackground = true;
            staThread.Start();
            await tcs.Task;

            // 임시로 바꿔둔 프린터 기본 설정 복원
            try
            {
                if (cfgQueue != null && originalTicket != null)
                {
                    cfgQueue.DefaultPrintTicket = originalTicket;
                    cfgQueue.Commit();
                }
            }
            catch { }

            BtnRunPrint.IsEnabled = true;
            BtnRunPrint.Content   = "일괄출력 실행";
            MessageBox.Show("일괄출력 작업이 완료되었습니다.", "작업 완료",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // PDF 출력: 지정 프린터로 Adobe Reader → SumatraPDF → 레지스트리 명령 순 시도
        private static void PrintPdf(string pdfPath, string printerName)
        {
            // 1순위: Adobe Reader (/t file printer)
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
                    Arguments   = $"/t /h \"{pdfPath}\" \"{printerName}\"",
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                p?.WaitForExit(20000);
                try { p?.Kill(); } catch { }
                return;
            }

            // 2순위: SumatraPDF (-print-to 프린터명)
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
                    Arguments   = $"-print-to \"{printerName}\" \"{pdfPath}\"",
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                p?.WaitForExit(20000);
                return;
            }

            // 3순위: 레지스트리 shell\print\command (프린터 지정 불가, 기본 프린터로 출력)
            string? printCmd = GetRegistryPrintCommand();
            if (printCmd != null)
            {
                string args = printCmd.Replace("\"%1\"", $"\"{pdfPath}\"")
                                      .Replace("%1",     $"\"{pdfPath}\"");
                string exe, exeArgs;
                if (args.StartsWith("\""))
                {
                    int end = args.IndexOf('"', 1);
                    exe     = args[1..end];
                    exeArgs = args[(end + 1)..].Trim();
                }
                else
                {
                    int sp  = args.IndexOf(' ');
                    exe     = sp > 0 ? args[..sp] : args;
                    exeArgs = sp > 0 ? args[(sp + 1)..] : "";
                }
                if (File.Exists(exe))
                {
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName    = exe,
                        Arguments   = exeArgs,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    p?.WaitForExit(20000);
                    try { p?.Kill(); } catch { }
                    return;
                }
            }

            throw new InvalidOperationException(
                "PDF 자동 출력을 위해 Adobe Reader 또는 SumatraPDF가 필요합니다.\n" +
                "SumatraPDF는 무료로 설치할 수 있습니다.");
        }

        // 레지스트리에서 .pdf의 shell\print\command 조회 (print 동사 없으면 null)
        private static string? GetRegistryPrintCommand()
        {
            try
            {
                using var extKey = Registry.ClassesRoot.OpenSubKey(".pdf");
                string? progId = extKey?.GetValue(null) as string;
                if (string.IsNullOrEmpty(progId)) return null;

                using var printKey = Registry.ClassesRoot.OpenSubKey(
                    $@"{progId}\shell\print\command");
                string? cmd = printKey?.GetValue(null) as string;
                return string.IsNullOrWhiteSpace(cmd) ? null : cmd.Trim();
            }
            catch { return null; }
        }
    }
}