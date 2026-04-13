using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
        public ObservableCollection<ReportTaskModel> TaskList { get; set; } = new ObservableCollection<ReportTaskModel>();

        private readonly string SOURCE_DIR = @"\\10.10.40.98\nas\00.MESServer\Inspection\";
        private readonly string DEST_DIR = @"\\10.10.40.98\천안공장\25. 생산 Inform 자료\주언\1.성적서 복사 및 생성\";

        public ReportAutomationView()
        {
            InitializeComponent();
            DataGridList.ItemsSource = TaskList;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus();
        }

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
                                TaskList.Add(new ReportTaskModel { LotNumber = lot, SerialNumber = sn, Status = "대기중" });
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

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TaskList.Clear();
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (TaskList.Count == 0)
            {
                MessageBox.Show("작업할 데이터가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool makePdf = ChkCreatePdf.IsChecked == true;

            BtnRun.IsEnabled = false;
            BtnRun.Content = "⏳ 작업 진행 중...";
            // 🔥 붙여넣기 버튼 관련 잠금(IsEnabled) 코드 모두 삭제 완료

            if (!Directory.Exists(DEST_DIR))
            {
                try { Directory.CreateDirectory(DEST_DIR); }
                catch { MessageBox.Show("목적지 네트워크 폴더에 접근할 수 없습니다.", "경로 오류", MessageBoxButton.OK, MessageBoxImage.Error); BtnRun.IsEnabled = true; BtnRun.Content = "자동 변환 실행"; return; }
            }

            await Task.Run(() =>
            {
                Excel.Application excelApp = null;

                try
                {
                    if (makePdf)
                    {
                        excelApp = new Excel.Application();
                        excelApp.Visible = false;
                        excelApp.DisplayAlerts = false;
                    }

                    foreach (var task in TaskList)
                    {
                        if (task.Status.Contains("성공")) continue;

                        task.Status = "진행중...";

                        string sourceFile = Path.Combine(SOURCE_DIR, $"{task.LotNumber}.xlsx");
                        string destExcelFile = Path.Combine(DEST_DIR, $"{task.SerialNumber}.xlsx");
                        string destPdfFile = Path.Combine(DEST_DIR, $"{task.SerialNumber}.pdf");

                        if (!File.Exists(sourceFile))
                        {
                            task.Status = "원본 없음";
                            continue;
                        }

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
                        catch (Exception)
                        {
                            task.Status = "오류 발생";
                        }
                    }
                }
                finally
                {
                    if (excelApp != null)
                    {
                        excelApp.Quit();
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp);
                    }
                }
            });

            BtnRun.IsEnabled = true;
            BtnRun.Content = "자동 변환 실행";
            MessageBox.Show("모든 파일 변환 작업이 완료되었습니다.", "작업 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}