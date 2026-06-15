using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WpfBorder = System.Windows.Controls.Border;

namespace CleanPotal
{
    // ---------------------------------------------------------------------------
    // 교육 현황 - 데이터 모델
    // ---------------------------------------------------------------------------
    public class TrainingRecord : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string n) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));

        public TrainingRecord()
        {
            Documents.CollectionChanged += (_, _) => { Notify(nameof(HasDocument)); Notify(nameof(DocumentLabel)); };
        }

        private int _displayNo;
        public int DisplayNo { get => _displayNo; set { if (_displayNo == value) return; _displayNo = value; Notify(nameof(DisplayNo)); } }

        private DateTime? _trainingDate;
        public DateTime? TrainingDate
        {
            get => _trainingDate;
            set { _trainingDate = value; Notify(nameof(TrainingDate)); Notify(nameof(TrainingDateShort)); }
        }
        public string TrainingDateShort => TrainingDate.HasValue
            ? $"{TrainingDate.Value.Year}년 {TrainingDate.Value.Month:D2}월 {TrainingDate.Value.Day:D2}일"
            : "-";

        public ObservableCollection<string> Documents { get; } = new();

        public bool   HasDocument   => Documents.Count > 0;
        public string DocumentLabel => Documents.Count switch {
            0 => "첨부", 1 => "교육기록서", _ => $"교육기록서 {Documents.Count}건" };
    }

    // 교육 활동 실행률 표의 한 행 (생산 / 물류 / 합계)
    public class TrainingSummaryRow : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string n) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));

        public string CategoryLabel { get; set; } = "";
        public string CategoryKey { get; set; } = "";
        public bool IsTotal { get; set; }

        private int _target2025;
        public int Target2025 { get => _target2025; set { if (_target2025 == value) return; _target2025 = value; Notify(nameof(Target2025)); } }

        private int _target2026;
        public int Target2026 { get => _target2026; set { if (_target2026 == value) return; _target2026 = value; Notify(nameof(Target2026)); } }

        public int Actual2026 { get; set; }
        public List<int> Monthly { get; set; } = new(new int[12]);
        public int FirstHalf { get; set; }
        public int SecondHalf { get; set; }

        public void NotifyComputed()
        {
            Notify(nameof(Actual2026));
            Notify(nameof(Monthly));
            Notify(nameof(FirstHalf));
            Notify(nameof(SecondHalf));
        }
    }

    // ---------------------------------------------------------------------------
    // 교육 현황 탭 - 코드비하인드
    // ---------------------------------------------------------------------------
    public partial class BrokenManagementView
    {
        // 교육 활동 실행률 표 컬럼 헤더를 현재 연도 기준으로 설정 ("25년 실적", "26년 목표", "26년 실적" 등)
        private void SetupTrainingSummaryHeaders()
        {
            int curYear = DateTime.Now.Year % 100;
            int prevYear = curYear - 1;
            ColPrevYearActual.Header = $"{prevYear}년 실적";
            ColCurYearTarget.Header = $"{curYear}년 목표";
            ColCurYearActual.Header = $"{curYear}년 실적";
        }

        // 교육 기록 리스트를 바탕으로 실행률 표(생산/물류/합계)를 다시 계산
        private void RebuildTrainingSummary()
        {
            int curYear = DateTime.Now.Year;

            var prod = new TrainingSummaryRow
            {
                CategoryLabel = "생산(월 1회)", CategoryKey = "생산",
                Target2025 = _trainingGoals.ProductionTarget2025, Target2026 = _trainingGoals.ProductionTarget2026
            };
            var logi = new TrainingSummaryRow
            {
                CategoryLabel = "물류(월 1회)", CategoryKey = "물류",
                Target2025 = _trainingGoals.LogisticsTarget2025, Target2026 = _trainingGoals.LogisticsTarget2026
            };
            var total = new TrainingSummaryRow { CategoryLabel = "합계", CategoryKey = "합계", IsTotal = true };

            foreach (var (row, records) in new[] { (prod, _trainingRecordsProd), (logi, _trainingRecordsLogi) })
            {
                var monthly = new List<int>(new int[12]);
                foreach (var rec in records)
                {
                    if (!rec.TrainingDate.HasValue || rec.TrainingDate.Value.Year != curYear) continue;
                    monthly[rec.TrainingDate.Value.Month - 1]++;
                }
                row.Monthly = monthly;
                row.Actual2026 = monthly.Sum();
                row.FirstHalf = monthly.Take(6).Sum();
                row.SecondHalf = monthly.Skip(6).Sum();
                row.NotifyComputed();
            }

            total.Target2025 = prod.Target2025 + logi.Target2025;
            total.Target2026 = prod.Target2026 + logi.Target2026;
            total.Monthly = Enumerable.Range(0, 12).Select(i => prod.Monthly[i] + logi.Monthly[i]).ToList();
            total.Actual2026 = total.Monthly.Sum();
            total.FirstHalf = total.Monthly.Take(6).Sum();
            total.SecondHalf = total.Monthly.Skip(6).Sum();
            total.NotifyComputed();

            _trainingSummary.Clear();
            _trainingSummary.Add(prod);
            _trainingSummary.Add(logi);
            _trainingSummary.Add(total);
        }

        // 합계 행은 편집 불가, 25년 실적/26년 목표만 직접 입력 가능
        private void DgTrainingSummary_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is not TrainingSummaryRow row) return;
            if (row.IsTotal) { e.Cancel = true; return; }
            if (e.Column != ColPrevYearActual && e.Column != ColCurYearTarget) e.Cancel = true;
        }

        // 25년 실적/26년 목표 입력값을 저장하고 합계 행을 다시 계산
        private void DgTrainingSummary_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not TrainingSummaryRow row) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (row.CategoryKey == "생산")
                {
                    _trainingGoals.ProductionTarget2025 = row.Target2025;
                    _trainingGoals.ProductionTarget2026 = row.Target2026;
                }
                else if (row.CategoryKey == "물류")
                {
                    _trainingGoals.LogisticsTarget2025 = row.Target2025;
                    _trainingGoals.LogisticsTarget2026 = row.Target2026;
                }
                RebuildTrainingSummary();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private static void RenumberTrainingRecords(ObservableCollection<TrainingRecord> records)
        {
            for (int i = 0; i < records.Count; i++) records[i].DisplayNo = i + 1;
        }

        private void RenumberTrainingRecords()
        {
            RenumberTrainingRecords(_trainingRecordsProd);
            RenumberTrainingRecords(_trainingRecordsLogi);
        }

        // -----------------------------------------------------------------------
        // 교육 기록 - 행 추가 / 삭제 / 저장
        // -----------------------------------------------------------------------
        private void AddTrainingRow(ObservableCollection<TrainingRecord> records, DataGrid grid)
        {
            var rec = new TrainingRecord { TrainingDate = DateTime.Today };
            records.Add(rec);
            RenumberTrainingRecords();
            RebuildTrainingSummary();

            grid.SelectedItem = rec;
            grid.ScrollIntoView(rec);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                grid.UpdateLayout();
                grid.CurrentCell = new DataGridCellInfo(rec, grid.Columns[1]);
                grid.BeginEdit();
            });
        }

        private void DeleteTrainingRow(ObservableCollection<TrainingRecord> records, DataGrid grid)
        {
            if (grid.SelectedItem is not TrainingRecord selected) return;
            if (MessageBox.Show("선택한 행을 삭제하시겠습니까?", "확인",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            records.Remove(selected);
            RenumberTrainingRecords();
            RebuildTrainingSummary();
        }

        private void BtnAddTrainingRowProd_Click(object sender, RoutedEventArgs e)
            => AddTrainingRow(_trainingRecordsProd, DgTrainingProd);

        private void BtnAddTrainingRowLogi_Click(object sender, RoutedEventArgs e)
            => AddTrainingRow(_trainingRecordsLogi, DgTrainingLogi);

        private void BtnDeleteTrainingRowProd_Click(object sender, RoutedEventArgs e)
            => DeleteTrainingRow(_trainingRecordsProd, DgTrainingProd);

        private void BtnDeleteTrainingRowLogi_Click(object sender, RoutedEventArgs e)
            => DeleteTrainingRow(_trainingRecordsLogi, DgTrainingLogi);

        private void BtnSaveTraining_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveAppData();
                MessageBox.Show("저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 교육일자가 바뀌면 실행률 표를 다시 계산
        private void DgTrainingRecord_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            Dispatcher.BeginInvoke(new Action(RebuildTrainingSummary),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // -----------------------------------------------------------------------
        // 교육기록서 첨부 - 클릭 / 우클릭 / 드래그앤드롭 (Broken 리스트와 동일한 방식으로 공유 저장소에 저장)
        // -----------------------------------------------------------------------
        private void AttachChipTrainingRecord_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not TrainingRecord record) return;
            var col = record.Documents;

            if (col.Count == 0)
            {
                var dlg = new OpenFileDialog { Title = "파일 첨부", Filter = AttachFilter, Multiselect = true };
                if (dlg.ShowDialog() != true) return;
                AddAttachments(col, dlg.FileNames);
                return;
            }

            if (col.Count == 1)
            {
                OpenFile(col[0]);
                return;
            }

            ShowAttachMenu(fe, col);
        }

        private void AttachChipTrainingRecord_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not TrainingRecord record) return;
            ShowAttachMenu(fe, record.Documents);
        }

        private void CellDropTrainingRecord_Drop(object sender, DragEventArgs e)
        {
            if (sender is WpfBorder bd) bd.ClearValue(WpfBorder.BackgroundProperty);
            if (sender is not FrameworkElement fe || fe.Tag is not TrainingRecord record) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            AddAttachments(record.Documents, files.Where(path => _allowedExts.Contains(System.IO.Path.GetExtension(path))));
            e.Handled = true;
        }
    }
}
