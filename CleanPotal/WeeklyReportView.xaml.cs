using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    public class WeeklyReportModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string DateRange { get; set; } = "";
        public ObservableCollection<WeeklyBlockModel> Blocks { get; set; } = new();
    }

    public class WeeklyBlockModel
    {
        public string Category { get; set; } = "";
        public string Content { get; set; } = "";
        public string FollowUp { get; set; } = "";
        public string Status { get; set; } = "진행 중";

        // 보고용 DataGrid 표를 위해 Content와 FollowUp을 묶어주는 읽기전용 속성
        public string FormattedContent
        {
            get
            {
                string result = "";
                if (!string.IsNullOrWhiteSpace(Content)) result += $"· {Content}\n";
                if (!string.IsNullOrWhiteSpace(FollowUp)) result += $"→ {FollowUp}";
                return result.TrimEnd();
            }
        }
    }

    public partial class WeeklyReportView : UserControl
    {
        public ObservableCollection<WeeklyReportModel> ReportsHistory { get; set; } = new();
        private WeeklyReportModel? _currentReport;

        public WeeklyReportView()
        {
            InitializeComponent();
            LoadDummyHistory();
            HistoryListBox.ItemsSource = ReportsHistory;
            if (ReportsHistory.Count > 0)
                HistoryListBox.SelectedIndex = 0;
        }

        private void LoadDummyHistory()
        {
            // 기본 더미 데이터 셋업
            var oldReport = new WeeklyReportModel
            {
                Title = "26년 4월 1주차 주간보고",
                DateRange = "2026.04.06 ~ 2026.04.10"
            };

            oldReport.Blocks.Add(new WeeklyBlockModel { Category = "1. 세정품 SPEC 관련", Content = "부적합 반입 리스트에 대한 사용 및 폐기 내역 회신 요청", FollowUp = "자체 이력 관리 체계를 통해 가이드라인 관리 가능 여부 확인 필요", Status = "보류" });
            oldReport.Blocks.Add(new WeeklyBlockModel { Category = "2. Valve 세정기 관련", Content = "4/9(목) 매매계약서 체결 완료", FollowUp = "차주 중 설비 개조를 위한 반출 예정", Status = "종결" });
            oldReport.Blocks.Add(new WeeklyBlockModel { Category = "3. PFA Tube 관련", Content = "3/4 규격 20M+20M TEST 완료", FollowUp = "1/2 규격 세정 완료되었으며 현재 분석 결과 대기 중", Status = "진행 중" });

            ReportsHistory.Add(oldReport);
        }

        private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryListBox.SelectedItem is WeeklyReportModel selectedReport)
            {
                _currentReport = selectedReport;
                TxtCurrentReportTitle.Text = _currentReport.Title;
                TxtCurrentReportDate.Text = _currentReport.DateRange;
                ReportBlocksControl.ItemsSource = _currentReport.Blocks;
            }
        }

        private void BtnCreateNewReport_Click(object sender, RoutedEventArgs e)
        {
            var newReport = new WeeklyReportModel
            {
                Title = $"26년 4월 {ReportsHistory.Count + 1}주차 주간보고",
                DateRange = DateTime.Now.ToString("yyyy.MM.dd") + " ~ " + DateTime.Now.AddDays(4).ToString("yyyy.MM.dd")
            };

            // 🔥 스마트 이월 시스템: 가장 최근 보고서에서 '종결'이 아닌 항목들만 자동으로 가져옴
            if (ReportsHistory.Count > 0)
            {
                var lastReport = ReportsHistory[0]; // 가장 최신 데이터가 인덱스 0이라 가정
                foreach (var block in lastReport.Blocks.Where(b => b.Status != "종결"))
                {
                    newReport.Blocks.Add(new WeeklyBlockModel
                    {
                        Category = block.Category,
                        Content = block.Content,
                        FollowUp = block.FollowUp, // 기존 팔로업 내역 유지
                        Status = block.Status
                    });
                }
            }

            // 최상단에 새 보고서 추가 후 선택
            ReportsHistory.Insert(0, newReport);
            HistoryListBox.SelectedIndex = 0;

            MessageBox.Show("지난 주간보고의 미종결 항목들이 자동으로 복사되어 이월되었습니다.", "새 주간보고 생성", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAddBlock_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReport != null)
            {
                _currentReport.Blocks.Add(new WeeklyBlockModel { Category = "신규 업무" });
            }
        }

        private void BtnSaveContent_Click(object sender, RoutedEventArgs e)
        {
            // 실제 환경에서는 DB Update 쿼리 실행
            MessageBox.Show("변경사항이 성공적으로 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnShowTable_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReport != null)
            {
                // 보고용 표에 데이터 바인딩
                ReportDataGrid.ItemsSource = null;
                ReportDataGrid.ItemsSource = _currentReport.Blocks;
                TableModalOverlay.Visibility = Visibility.Visible;
            }
        }

        private void BtnCloseTable_Click(object sender, RoutedEventArgs e)
        {
            TableModalOverlay.Visibility = Visibility.Collapsed;
        }
    }
}