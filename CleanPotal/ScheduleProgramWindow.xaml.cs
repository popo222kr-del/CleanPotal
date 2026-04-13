using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CleanPotal
{
    public class DayHeader
    {
        public string Text { get; set; } = "";
        public SolidColorBrush FgBrush { get; set; } = Brushes.Black;
        public SolidColorBrush BgBrush { get; set; } = Brushes.Transparent;
    }

    public class DailyCountModel { public int Count { get; set; } }

    public class ShiftBoardCell : INotifyPropertyChanged
    {
        public DateTime Date { get; set; }
        public string MemberName { get; set; } = "";
        public string TeamGroup { get; set; } = "";

        // 세로 기둥(Column) 배경색 지원 (주말/공휴일 하이라이트용)
        public SolidColorBrush ColumnBgBrush { get; set; } = Brushes.Transparent;

        private string _shiftType = "";
        public string ShiftType
        {
            get => _shiftType;
            set { _shiftType = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); OnPropertyChanged(nameof(BgBrush)); OnPropertyChanged(nameof(FgBrush)); }
        }

        // 텍스트 직관성: 주, 야, 휴를 제외한 나머지는 전체 글자 표시
        public string DisplayText
        {
            get
            {
                string val = ShiftType;
                if (val.Contains("주간")) return "주";
                if (val.Contains("야간")) return "야";
                if (val.Contains("반반차")) return "반반차";
                if (val.Contains("반차")) return "반차";
                if (val.Contains("휴무")) return "휴";
                if (val.Contains("연차")) return "연차";
                if (val.Contains("교육")) return "교육";
                if (val.Contains("특근")) return "특근";
                return val.Replace("예상:", "");
            }
        }

        // 🔥 휴무 배경색을 시인성이 좋은 노란색으로 변경
        public SolidColorBrush BgBrush
        {
            get
            {
                bool isPredict = ShiftType.StartsWith("예상:");
                string type = ShiftType.Replace("예상:", "");

                if (type.Contains("주간")) return new SolidColorBrush(Color.FromRgb(186, 230, 253));
                if (type.Contains("야간")) return new SolidColorBrush(Color.FromRgb(254, 205, 211));
                if (type.Contains("반차")) return new SolidColorBrush(Color.FromRgb(253, 186, 116));

                // 🔥 휴무: 밝은 노란색 적용
                if (type.Contains("휴무")) return new SolidColorBrush(Color.FromRgb(253, 224, 71));

                if (type.Contains("연차")) return new SolidColorBrush(Color.FromRgb(187, 247, 208));
                if (type.Contains("교육")) return new SolidColorBrush(Color.FromRgb(233, 213, 255));
                if (type.Contains("특근")) return new SolidColorBrush(Color.FromRgb(254, 202, 202));

                return Brushes.Transparent;
            }
        }

        public SolidColorBrush FgBrush
        {
            get
            {
                string type = ShiftType.Replace("예상:", "");

                if (type.Contains("주간")) return new SolidColorBrush(Color.FromRgb(3, 105, 161));
                if (type.Contains("야간")) return new SolidColorBrush(Color.FromRgb(190, 18, 60));
                if (type.Contains("반차")) return new SolidColorBrush(Color.FromRgb(154, 52, 18));

                // 🔥 노란색 배경에 잘 보이는 진한 황토색 계열로 가독성 확보
                if (type.Contains("휴무")) return new SolidColorBrush(Color.FromRgb(133, 77, 14));

                if (type.Contains("연차")) return new SolidColorBrush(Color.FromRgb(21, 128, 61));
                if (type.Contains("교육")) return new SolidColorBrush(Color.FromRgb(107, 33, 168));
                if (type.Contains("특근")) return new SolidColorBrush(Color.FromRgb(185, 28, 28));

                return Brushes.Transparent;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ShiftBoardRow : INotifyPropertyChanged
    {
        private bool _isChecked;
        public bool IsChecked { get { return _isChecked; } set { _isChecked = value; OnPropertyChanged(); } }
        private int _totalWorkDays;
        public int TotalWorkDays { get { return _totalWorkDays; } set { _totalWorkDays = value; OnPropertyChanged(); } }
        public string MemberName { get; set; } = "";
        public ObservableCollection<ShiftBoardCell> Cells { get; set; } = new();
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class JobTitleBoardGroup { public string JobTitle { get; set; } = ""; public ObservableCollection<ShiftBoardRow> Rows { get; set; } = new(); }

    public class TeamBoardGroup : INotifyPropertyChanged
    {
        public string TeamName { get; set; } = "";
        public ObservableCollection<JobTitleBoardGroup> JobTitles { get; set; } = new();
        public ObservableCollection<DailyCountModel> DailyCounts { get; set; } = new();
        private int _grandTotal;
        public int GrandTotal { get => _grandTotal; set { _grandTotal = value; OnPropertyChanged(); } }
        private bool _isTeamChecked;
        public bool IsTeamChecked { get { return _isTeamChecked; } set { _isTeamChecked = value; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class ScheduleProgramWindow : Window
    {
        private DateTime _currentMonth;
        private string _currentTeamFilter = "전체";
        public ObservableCollection<DayHeader> HeaderDays { get; set; } = new();
        public ObservableCollection<TeamBoardGroup> Teams { get; set; } = new();
        private List<string> _holidays = new();

        private bool _isWindowLoaded = false; // 🔥 추가: 윈도우 로딩 상태 체크용 플래그

        public ScheduleProgramWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            LeftTeamsList.ItemsSource = Teams;
            RightTeamsList.ItemsSource = Teams;

            this.Loaded += async (s, e) => {
                var fetchedHolidays = await HolidayManager.GetHolidaysAsync(_currentMonth.Year);
                if (fetchedHolidays != null) _holidays = fetchedHolidays;

                _isWindowLoaded = true; // 🔥 창이 완전히 준비됨을 알림
                LoadData();
            };
        }

        private void TeamFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Content != null)
            {
                _currentTeamFilter = rb.Content.ToString() ?? "전체";

                // 🔥 UI가 초기화되는 과정(InitializeComponent)에서
                // 아직 준비되지 않은 데이터를 성급하게 불러와 에러가 터지는 것을 막아줍니다.
                if (_isWindowLoaded)
                {
                    LoadData();
                }
            }
        }

        private int GetJobTitleOrder(string jobTitle)
        {
            if (string.IsNullOrWhiteSpace(jobTitle)) return 99;
            if (jobTitle.Contains("세정팀장")) return 1;
            if (jobTitle.Contains("세정")) return 2;
            if (jobTitle.Contains("QA팀장")) return 3;
            if (jobTitle.Contains("QA")) return 4;
            return 99;
        }

        private void LoadData()
        {
            DateTime startMonth = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
            DateTime endMonth = startMonth.AddMonths(1).AddDays(-1);
            int totalDays = (int)(endMonth - startMonth).TotalDays + 1;
            bool isPredictMode = TglPredictPattern.IsChecked == true;

            TxtMonthYear.Text = $"{_currentMonth.Year}년 {_currentMonth.Month}월";
            HeaderDays.Clear(); Teams.Clear();

            for (int i = 0; i < totalDays; i++)
            {
                DateTime dt = startMonth.AddDays(i);
                bool isHoliday = _holidays.Contains(dt.ToString("yyyy-MM-dd"));
                bool isWeekend = dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday;

                var fg = (dt.DayOfWeek == DayOfWeek.Sunday || isHoliday) ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : (dt.DayOfWeek == DayOfWeek.Saturday ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(71, 85, 105)));
                var bg = (isWeekend || isHoliday) ? new SolidColorBrush(Color.FromRgb(255, 241, 242)) : Brushes.Transparent;

                HeaderDays.Add(new DayHeader { Text = $"{dt.Day}\n{dt.ToString("ddd", new CultureInfo("ko-KR"))}", FgBrush = fg, BgBrush = bg });
            }

            string[] allTargetTeams = { "김팀", "장팀" };
            // 🔥 필터링 로직 적용
            var filterTeams = _currentTeamFilter == "전체" ? allTargetTeams : new[] { _currentTeamFilter };

            // 🔥 DB에서 가져온 유저 데이터(u)나 부서명(TeamName)이 비어있어 발생하는 Null 에러를 완벽 방어합니다.
            var allUsers = AuthDatabaseHelper.GetAllUsers()
                .Where(u => u != null && !string.IsNullOrWhiteSpace(u.TeamName) && filterTeams.Contains(u.TeamName))
                .OrderBy(u => u.TeamName)
                .ThenBy(u => GetJobTitleOrder(u.JobTitle))
                .ThenBy(u => u.RealName)
                .ToList();

            var allShifts = DatabaseHelper.GetShiftSchedulesInRange(startMonth, endMonth);

            foreach (var teamGrp in allUsers.GroupBy(u => u.TeamName))
            {
                var tGroup = new TeamBoardGroup { TeamName = teamGrp.Key, IsTeamChecked = false };
                foreach (var jobGrp in teamGrp.GroupBy(u => u.JobTitle ?? ""))
                {
                    var jGroup = new JobTitleBoardGroup { JobTitle = jobGrp.Key };
                    foreach (var user in jobGrp)
                    {
                        var row = new ShiftBoardRow { MemberName = user.RealName, IsChecked = false };
                        for (int i = 0; i < totalDays; i++)
                        {
                            DateTime cellDate = startMonth.AddDays(i);
                            var existing = allShifts.FirstOrDefault(s => s.MemberName == user.RealName && s.TargetDate.Date == cellDate.Date);
                            string shiftType = existing?.ShiftType ?? "";

                            if (isPredictMode && string.IsNullOrEmpty(shiftType))
                            {
                                int weekNum = CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(cellDate, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                                bool isDayShiftWeek = (weekNum / 2) % 2 == 0;
                                shiftType = isDayShiftWeek ? "예상:주간" : "예상:야간";
                            }

                            bool isWeekendHoliday = cellDate.DayOfWeek == DayOfWeek.Saturday || cellDate.DayOfWeek == DayOfWeek.Sunday || _holidays.Contains(cellDate.ToString("yyyy-MM-dd"));
                            var colBg = isWeekendHoliday ? new SolidColorBrush(Color.FromRgb(255, 251, 251)) : Brushes.Transparent;

                            row.Cells.Add(new ShiftBoardCell { Date = cellDate, MemberName = user.RealName, TeamGroup = user.TeamName, ShiftType = shiftType, ColumnBgBrush = colBg });
                        }
                        jGroup.Rows.Add(row);
                    }
                    tGroup.JobTitles.Add(jGroup);
                }
                Teams.Add(tGroup);
            }
            CalculateTotals();
        }

        private void CalculateTotals()
        {
            int totalDays = HeaderDays.Count;
            foreach (var tGrp in Teams)
            {
                int teamGrandTotal = 0; int[] teamDailyArray = new int[totalDays];
                foreach (var jGrp in tGrp.JobTitles)
                {
                    foreach (var row in jGrp.Rows)
                    {
                        int rowTotal = 0;
                        for (int i = 0; i < row.Cells.Count; i++)
                        {
                            var cell = row.Cells[i];
                            string type = cell.ShiftType.Replace("예상:", "");
                            if (!string.IsNullOrEmpty(type) && !type.Contains("휴무") && !type.Contains("연차") && !type.Contains("반차") && !type.Contains("교육") && type != "비우기")
                            {
                                rowTotal++; teamDailyArray[i]++;
                            }
                        }
                        row.TotalWorkDays = rowTotal; teamGrandTotal += rowTotal;
                    }
                }
                tGrp.DailyCounts.Clear();
                foreach (int count in teamDailyArray) tGrp.DailyCounts.Add(new DailyCountModel { Count = count });
                tGrp.GrandTotal = teamGrandTotal;
            }
        }

        private void RightScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) { if (e.VerticalChange != 0) LeftScroll.ScrollToVerticalOffset(e.VerticalOffset); }
        private void ChkTeam_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is TeamBoardGroup teamGrp) { bool isChecked = teamGrp.IsTeamChecked; foreach (var job in teamGrp.JobTitles) foreach (var row in job.Rows) row.IsChecked = isChecked; } }

        private void Cell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ShiftBoardCell clickedCell)
            {
                if (clickedCell.ShiftType.Contains("교육")) { MessageBox.Show("교육 일정은 직접 수정할 수 없습니다.", "알림"); return; }
                var targetRow = Teams.SelectMany(t => t.JobTitles).SelectMany(j => j.Rows).FirstOrDefault(r => r.MemberName == clickedCell.MemberName);
                if (targetRow == null || !targetRow.IsChecked) return;

                if (!int.TryParse(TxtPaintDays.Text, out int paintDays) || paintDays < 1) paintDays = 1;
                string paintType = (CmbPaintType.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
                var checkedRows = Teams.SelectMany(t => t.JobTitles).SelectMany(j => j.Rows).Where(r => r.IsChecked).ToList();

                foreach (var row in checkedRows)
                {
                    for (int i = 0; i < paintDays; i++)
                    {
                        DateTime targetDate = clickedCell.Date.AddDays(i);
                        var targetCell = row.Cells.FirstOrDefault(c => c.Date.Date == targetDate.Date);
                        if (targetCell != null && !targetCell.ShiftType.Contains("교육"))
                        {
                            targetCell.ShiftType = paintType;
                            DatabaseHelper.UpsertShiftSchedule(new ShiftScheduleModel { TargetDate = targetCell.Date, MemberName = row.MemberName, TeamGroup = targetCell.TeamGroup, ShiftType = paintType });
                        }
                    }
                }
                CalculateTotals();
            }
        }

        private void Cell_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ShiftBoardCell clickedCell)
            {
                if (clickedCell.ShiftType.Contains("교육")) { MessageBox.Show("교육 일정은 직접 삭제할 수 없습니다.", "알림"); return; }
                var checkedRows = Teams.SelectMany(t => t.JobTitles).SelectMany(j => j.Rows).Where(r => r.IsChecked).ToList();
                foreach (var row in checkedRows)
                {
                    var targetCell = row.Cells.FirstOrDefault(c => c.Date.Date == clickedCell.Date.Date);
                    if (targetCell != null && !targetCell.ShiftType.Contains("교육"))
                    {
                        targetCell.ShiftType = "";
                        DatabaseHelper.UpsertShiftSchedule(new ShiftScheduleModel { TargetDate = targetCell.Date, MemberName = row.MemberName, TeamGroup = targetCell.TeamGroup, ShiftType = "비우기" });
                    }
                }
                CalculateTotals();
            }
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e) { _currentMonth = _currentMonth.AddMonths(-1); LoadData(); }
        private void NextMonth_Click(object sender, RoutedEventArgs e) { _currentMonth = _currentMonth.AddMonths(1); LoadData(); }

        private void BtnOpenNewWindow_Click(object sender, RoutedEventArgs e)
        {
            var win = new ScheduleProgramWindow { Owner = this.Owner };
            win._currentMonth = this._currentMonth.AddMonths(-1);
            win.Show();
        }

        private void TglPredictPattern_Click(object sender, RoutedEventArgs e) => LoadData();
    }
}