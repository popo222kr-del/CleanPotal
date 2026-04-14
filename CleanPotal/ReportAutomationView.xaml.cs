using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
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

        private readonly string SOURCE_DIR = @"\\10.10.40.98\nas\00.MESServer\Inspection\";
        private readonly string DEST_DIR = @"\\10.10.40.98\천안공장\25. 생산 Inform 자료\주언\1.성적서 복사 및 생성\";

        public ReportAutomationView()
        {
            InitializeComponent();
            MesDataGrid.ItemsSource = MesTaskList;
            DirectDataGrid.ItemsSource = DirectTaskList;
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

                        string sourceFile = Path.Combine(SOURCE_DIR, $"{task.LotNumber}.xlsx");
                        string destExcelFile = Path.Combine(DEST_DIR, $"{task.SerialNumber}.xlsx");
                        string destPdfFile = Path.Combine(DEST_DIR, $"{task.SerialNumber}.pdf");

                        if (!File.Exists(sourceFile)) { task.Status = "원본 없음"; continue; }

                        try
                        {
                            File.Copy(sourceFile, destExcelFile, true);

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

            BtnRunDirect.IsEnabled = true; BtnRunDirect.Content = "다이렉트 변환 실행";
            MessageBox.Show("다이렉트 파일 PDF 변환 작업이 완료되었습니다.", "작업 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}