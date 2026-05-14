using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CleanPotal
{
    public class ScheduleDetailItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string SourceType { get; set; } = "";
    }

    public partial class TeamScheduleView : UserControl
    {
        private DateTime _currentDate;
        private Dictionary<string, List<ScheduleDetailItem>> _badgeDetails = new();
        private int _calendarBuildVersion = 0;

        // 🔥 메인 창(MainWindow)의 헤더 텍스트를 실시간 변경하기 위한 이벤트
        public event Action<string>? MonthTextChanged;
        public string CurrentMonthText => $"{_currentDate.Year}년 {_currentDate.Month}월";

        public TeamScheduleView()
        {
            InitializeComponent();
            _currentDate = DateTime.Today;
            _ = BuildCalendarAsync(_currentDate);
        }

        public void TryRefresh() => _ = BuildCalendarAsync(_currentDate);

        private async System.Threading.Tasks.Task BuildCalendarAsync(DateTime targetDate)
        {
            // 달력이 그려질 때마다 메인 윈도우의 텍스트(예: 2026년 4월)를 바꿔라! 라고 신호를 쏩니다.
            MonthTextChanged?.Invoke(CurrentMonthText);
            int buildVersion = ++_calendarBuildVersion;

            DateTime firstDayOfMonth = new DateTime(targetDate.Year, targetDate.Month, 1);
            int startDayOfWeek = (int)firstDayOfMonth.DayOfWeek;

            DateTime startDate = firstDayOfMonth.AddDays(-startDayOfWeek);
            DateTime endDate = startDate.AddDays(41);

            var shifts = DatabaseHelper.GetShiftSchedulesInRange(startDate, endDate);
            var edus = DatabaseHelper.GetEducationPlansInRange(startDate, endDate);
            var teamEvents = DatabaseHelper.GetTeamEventsInRange(startDate, endDate);
            var allUsers = AuthDatabaseHelper.GetAllUsers();

            // 1) 먼저 휴일 정보 없이 즉시 렌더링(화면 멈춤 방지)
            var initialDays = BuildDayModels(targetDate, startDate, shifts, edus, teamEvents, allUsers, new Dictionary<string, string>());
            if (buildVersion != _calendarBuildVersion) return;
            CalendarItemsControl.ItemsSource = initialDays;

            // 2) 휴일명 비동기 로드 후 갱신
            Dictionary<string, string> holidayMap;
            try
            {
                holidayMap = await HolidayManager.GetHolidayNameMapAsync(startDate.Year, endDate.Year);
            }
            catch
            {
                holidayMap = new Dictionary<string, string>();
            }

            if (buildVersion != _calendarBuildVersion) return;
            var finalDays = BuildDayModels(targetDate, startDate, shifts, edus, teamEvents, allUsers, holidayMap);
            CalendarItemsControl.ItemsSource = finalDays;
        }

        private ObservableCollection<CalendarDayModel> BuildDayModels(
            DateTime targetDate,
            DateTime startDate,
            List<ShiftScheduleModel> shifts,
            List<EducationPlanModel> edus,
            List<TeamEvent> teamEvents,
            List<UserModel> allUsers,
            Dictionary<string, string> holidayMap)
        {
            _badgeDetails.Clear();
            var days = new ObservableCollection<CalendarDayModel>();

            for (int i = 0; i < 42; i++)
            {
                DateTime cellDate = startDate.AddDays(i);
                var dayModel = new CalendarDayModel
                {
                    Date = cellDate,
                    IsCurrentMonth = cellDate.Month == targetDate.Month
                };

                string cellDateStr = cellDate.ToString("yyyy-MM-dd");
                if (holidayMap.TryGetValue(cellDateStr, out var holidayName))
                {
                    dayModel.IsHoliday = true;
                    dayModel.HolidayName = holidayName;
                }

                var dayShifts = shifts.Where(s => s.TargetDate.ToString("yyyy-MM-dd") == cellDateStr && s.ShiftType == "주간").ToList();
                var nightShifts = shifts.Where(s => s.TargetDate.ToString("yyyy-MM-dd") == cellDateStr && s.ShiftType == "야간").ToList();
                var rawOffShifts = shifts.Where(s => s.TargetDate.ToString("yyyy-MM-dd") == cellDateStr && (s.ShiftType.Contains("휴무") || s.ShiftType.Contains("연차") || s.ShiftType.Contains("반차"))).ToList();
                var dayEdus = edus.Where(e => e.StartDate.Date <= cellDate.Date && e.EndDate.Date >= cellDate.Date).ToList();

                // 1. 주간 근무 뱃지
                if (dayShifts.Count > 0)
                {
                    string key = Guid.NewGuid().ToString();
                    _badgeDetails[key] = dayShifts.Select(s => new ScheduleDetailItem { Id = s.Id, Name = s.MemberName, Type = s.ShiftType, SourceType = "Shift" }).ToList();

                    dayModel.Badges.Add(new ScheduleBadge
                    {
                        Text = $"주간: {dayShifts.Count}",
                        TooltipText = string.Join("\n", dayShifts.Select(s => s.MemberName)),
                        BackgroundBrush = new SolidColorBrush(Color.FromRgb(254, 243, 199)),
                        TextBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                        FontWeight = FontWeights.Bold,
                        RecordType = "Group",
                        GroupMembers = key
                    });
                }

                // 2. 야간 근무 뱃지
                if (nightShifts.Count > 0)
                {
                    string key = Guid.NewGuid().ToString();
                    _badgeDetails[key] = nightShifts.Select(s => new ScheduleDetailItem { Id = s.Id, Name = s.MemberName, Type = s.ShiftType, SourceType = "Shift" }).ToList();

                    dayModel.Badges.Add(new ScheduleBadge
                    {
                        Text = $"야간: {nightShifts.Count}",
                        TooltipText = string.Join("\n", nightShifts.Select(s => s.MemberName)),
                        BackgroundBrush = new SolidColorBrush(Color.FromRgb(224, 231, 255)),
                        TextBrush = new SolidColorBrush(Color.FromRgb(67, 56, 202)),
                        FontWeight = FontWeights.Bold,
                        RecordType = "Group",
                        GroupMembers = key
                    });
                }

                // 3. 분할 휴무 뱃지
                if (rawOffShifts.Count > 0)
                {
                    var dayOffShifts = new List<ShiftScheduleModel>();
                    var nightOffShifts = new List<ShiftScheduleModel>();
                    var generalOffShifts = new List<ShiftScheduleModel>();

                    foreach (var off in rawOffShifts)
                    {
                        string tg = off.TeamGroup;
                        if (string.IsNullOrEmpty(tg))
                        {
                            tg = allUsers.FirstOrDefault(u => u.RealName == off.MemberName)?.TeamName ?? "";
                        }

                        if (tg.Contains("주간") || tg.Contains("Office") || tg.Contains("오피스") || string.IsNullOrEmpty(tg))
                        {
                            generalOffShifts.Add(off);
                        }
                        else
                        {
                            bool teamHasDay = dayShifts.Any(s => (s.TeamGroup ?? allUsers.FirstOrDefault(u => u.RealName == s.MemberName)?.TeamName) == tg);
                            bool teamHasNight = nightShifts.Any(s => (s.TeamGroup ?? allUsers.FirstOrDefault(u => u.RealName == s.MemberName)?.TeamName) == tg);

                            if (teamHasNight && !teamHasDay)
                                nightOffShifts.Add(off);
                            else if (teamHasDay && !teamHasNight)
                                dayOffShifts.Add(off);
                            else
                            {
                                var adjShift = shifts.FirstOrDefault(s => (s.TeamGroup ?? allUsers.FirstOrDefault(u => u.RealName == s.MemberName)?.TeamName) == tg && (s.ShiftType == "주간" || s.ShiftType == "야간") && Math.Abs((s.TargetDate - cellDate).TotalDays) <= 3);

                                if (adjShift != null && adjShift.ShiftType == "야간")
                                    nightOffShifts.Add(off);
                                else
                                    dayOffShifts.Add(off);
                            }
                        }
                    }

                    AddOffBadge(dayOffShifts, "주간", dayModel);
                    AddOffBadge(nightOffShifts, "야간", dayModel);
                    AddOffBadge(generalOffShifts, "", dayModel);
                }

                // 4. 교육 뱃지
                if (dayEdus.Count > 0)
                {
                    string key = Guid.NewGuid().ToString();
                    _badgeDetails[key] = dayEdus.Select(e => new ScheduleDetailItem { Id = e.Id, Name = e.MemberName, Type = e.CourseName, SourceType = "Edu" }).ToList();

                    var eduDetails = dayEdus.Select(e => $"[{e.CourseName}] {e.MemberName}");
                    dayModel.Badges.Add(new ScheduleBadge
                    {
                        Text = $"교육: {dayEdus.Count}",
                        TooltipText = string.Join("\n", eduDetails),
                        BackgroundBrush = new SolidColorBrush(Color.FromRgb(236, 252, 203)),
                        TextBrush = new SolidColorBrush(Color.FromRgb(101, 163, 13)),
                        FontWeight = FontWeights.Bold,
                        RecordType = "Group",
                        GroupMembers = key
                    });
                }

                // 5. 팀 일정 — 날짜 헤더 영역에 표시 (공휴일 위치)
                var dayTeamEvents = teamEvents
                    .Where(t => DateTime.Parse(t.StartDate).Date <= cellDate.Date && DateTime.Parse(t.EndDate).Date >= cellDate.Date)
                    .ToList();
                foreach (var te in dayTeamEvents)
                {
                    string key = Guid.NewGuid().ToString();
                    _badgeDetails[key] = new List<ScheduleDetailItem>
                    {
                        new ScheduleDetailItem { Id = te.Id, Name = te.RegisteredBy, Type = te.Content, SourceType = "TeamEvent" }
                    };
                    string displayText = te.Content.Length > 16 ? te.Content.Substring(0, 15) + "…" : te.Content;
                    dayModel.HeaderTeamEventBadges.Add(new ScheduleBadge
                    {
                        Text = displayText,
                        TooltipText = $"[팀 일정] {te.Content}\n등록자: {te.RegisteredBy}",
                        BackgroundBrush = new SolidColorBrush(Color.FromRgb(219, 234, 254)),
                        TextBrush = new SolidColorBrush(Color.FromRgb(29, 78, 216)),
                        FontWeight = FontWeights.Bold,
                        RecordType = "Group",
                        GroupMembers = key
                    });
                }

                days.Add(dayModel);
            }

            return days;
        }

        private void AddOffBadge(List<ShiftScheduleModel> offList, string prefix, CalendarDayModel dayModel)
        {
            if (offList.Count == 0) return;

            string key = Guid.NewGuid().ToString();
            _badgeDetails[key] = offList.Select(s => new ScheduleDetailItem { Id = s.Id, Name = s.MemberName, Type = s.ShiftType.StartsWith("반반차") ? $"{s.ShiftType} (2시간)" : s.ShiftType, SourceType = "Shift" }).ToList();

            var offDetails = offList.Select(s => s.ShiftType.StartsWith("반반차") ? $"[{s.ShiftType}] {s.MemberName} (2시간)" : $"[{s.ShiftType}] {s.MemberName}");

            string badgeTitle = "휴무/연차";
            bool hasLeave = offList.Any(s => s.ShiftType == "연차");
            bool hasHalf = offList.Any(s => s.ShiftType.Contains("반차"));
            bool hasOff = offList.Any(s => s.ShiftType == "휴무");

            if (hasLeave && !hasHalf && !hasOff) badgeTitle = "연차";
            else if (!hasLeave && hasHalf && !hasOff) badgeTitle = "반차";
            else if (!hasLeave && !hasHalf && hasOff) badgeTitle = "휴무";
            else badgeTitle = "휴무/연차";

            SolidColorBrush bgBrush;

            if (prefix == "주간")
            {
                badgeTitle = $"주간 {badgeTitle}";
                bgBrush = new SolidColorBrush(Color.FromRgb(254, 243, 199));
            }
            else if (prefix == "야간")
            {
                badgeTitle = $"야간 {badgeTitle}";
                bgBrush = new SolidColorBrush(Color.FromRgb(224, 231, 255));
            }
            else
            {
                bgBrush = new SolidColorBrush(Color.FromRgb(243, 232, 255));
            }

            dayModel.Badges.Add(new ScheduleBadge
            {
                Text = $"{badgeTitle}: {offList.Count}",
                TooltipText = string.Join("\n", offDetails),
                BackgroundBrush = bgBrush,
                TextBrush = new SolidColorBrush(Color.FromRgb(126, 34, 206)),
                FontWeight = FontWeights.Bold,
                RecordType = "Group",
                GroupMembers = key
            });
        }

        private void Badge_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is ScheduleBadge badge)
            {
                if (badge.RecordType == "Group")
                {
                    string title = badge.Text.Split(':')[0].Trim();
                    TxtModalTitle.Text = $"[{title}] 일정 관리";

                    if (_badgeDetails.TryGetValue(badge.GroupMembers, out var items))
                    {
                        DetailDataGrid.ItemsSource = items;
                        ModalOverlay.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnDeleteDetail_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ScheduleDetailItem item)
            {
                bool isMaster = SessionManager.CurrentUsername == "1004" || SessionManager.CanManageSchedule;
                bool isOffice = SessionManager.CurrentTeamName?.ToUpper().Contains("OFFICE") == true;
                bool isMine = item.Name == SessionManager.CurrentRealName;
                bool canDeleteTeamEvent = item.SourceType == "TeamEvent" && (isOffice || isMaster);

                if (!isMaster && !isMine && !canDeleteTeamEvent)
                {
                    MessageBox.Show("본인이 등록한 일정만 삭제하거나 수정할 수 있습니다.", "권한 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show($"'{item.Name}' 님의 일정을 정말 삭제하시겠습니까?", "일정 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    if (item.SourceType == "Shift") DatabaseHelper.DeleteShiftSchedule(item.Id);
                    else if (item.SourceType == "Edu") DatabaseHelper.DeleteEducationPlan(item.Id);
                    else if (item.SourceType == "TeamEvent") DatabaseHelper.DeleteTeamEvent(item.Id);

                    ModalOverlay.Visibility = Visibility.Collapsed;
                    _ = BuildCalendarAsync(_currentDate);
                }
            }
        }

        // ===============================================
        // 🔥 MainWindow에서 호출 가능하도록 만든 public 외부 함수들
        // ===============================================

        public void GoPrevMonth()
        {
            _currentDate = _currentDate.AddMonths(-1);
            _ = BuildCalendarAsync(_currentDate);
        }

        public void GoNextMonth()
        {
            _currentDate = _currentDate.AddMonths(1);
            _ = BuildCalendarAsync(_currentDate);
        }

        public void GoToday()
        {
            _currentDate = DateTime.Today;
            _ = BuildCalendarAsync(_currentDate);
        }

        public void CreatePattern()
        {
            // 실제 배포 시에는 마스터 권한 로직 활성화
            bool isMaster = true;
            if (!isMaster)
            {
                MessageBox.Show("부서 전체의 근무표를 생성/관리할 수 있는 마스터 권한이 없습니다.", "접근 제한", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            var boardWin = new ScheduleProgramWindow { Owner = Window.GetWindow(this) };
            boardWin.ShowDialog();

            _ = BuildCalendarAsync(_currentDate);
        }

        public void RegisterSchedule()
        {
            var win = new ScheduleRegisterWindow();
            win.Owner = Window.GetWindow(this);

            if (win.ShowDialog() == true)
            {
                _ = BuildCalendarAsync(_currentDate);
            }
        }
    }
}