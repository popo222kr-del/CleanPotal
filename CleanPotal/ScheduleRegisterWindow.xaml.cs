using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CleanPotal
{
    public partial class ScheduleRegisterWindow : Window
    {
        private List<UserModel> _allUsers = new List<UserModel>();
        private List<string> _eduNamesList = new List<string>();
        private ICollectionView? _eduNamesView;

        // 공공데이터 API에서 가져온 공휴일 정보가 동적으로 담길 리스트
        private List<string> _dynamicHolidays = new List<string>();
        private bool _isHolidayLoaded = false;

        public ScheduleRegisterWindow(bool openEduTab = false, bool eduOnly = false)
        {
            InitializeComponent();

            if (eduOnly)
            {
                this.Loaded += (_, _) =>
                {
                    TabAttendance.Visibility = Visibility.Collapsed;
                    MainTab.SelectedItem = TabEdu;
                    TxtWindowTitle.Text = "교육 일정 등록";
                    TxtWindowSubtitle.Visibility = Visibility.Collapsed;
                };
            }
            else if (openEduTab)
            {
                this.Loaded += (_, _) => MainTab.SelectedItem = TabEdu;
            }
            else
            {
                // 세정팀 일정 달력: 교육 탭 항상 숨김
                this.Loaded += (_, _) => TabEdu.Visibility = Visibility.Collapsed;
            }

            DpShiftStart.SelectedDate = DateTime.Today;
            DpShiftEnd.SelectedDate = DateTime.Today;
            DpEduStart.SelectedDate = DateTime.Today;
            DpEduEnd.SelectedDate = DateTime.Today;

            // 권한 상관없이 전체 유저 정보는 미리 로드 (팀명 매핑 및 Upsert를 위해 필요)
            _allUsers = AuthDatabaseHelper.GetAllUsers();

            CmbShiftName.ItemsSource = new List<string> { SessionManager.CurrentRealName };
            CmbShiftName.SelectedIndex = 0;
            CmbShiftName.IsEnabled = false;

            bool isMaster = SessionManager.CurrentUsername == "1004";
            bool hasSchedulePermission = SessionManager.CanManageSchedule || eduOnly;
            bool canManageAllAttendance = isMaster || hasSchedulePermission;

            if (canManageAllAttendance)
            {
                var allNames = _allUsers.Where(u => !string.IsNullOrWhiteSpace(u.RealName))
                                        .Select(u => $"[{u.TeamName}] {u.RealName}").ToList();
                CmbShiftName.ItemsSource = allNames;

                var myItem = allNames.FirstOrDefault(n => n.Contains(SessionManager.CurrentRealName));
                CmbShiftName.SelectedItem = myItem ?? allNames.FirstOrDefault();
                CmbShiftName.IsEnabled = true;
            }

            // 교육 일정 등록은 "관리자" 권한이 체크된 사용자만 가능
            if (hasSchedulePermission)
            {
                TabEdu.Visibility = Visibility.Visible;

                // Office → 김팀 → 장팀 → 주간팀 → 나머지 순 정렬
                var teamOrder = new[] { "Office", "김팀", "장팀", "주간팀" };
                _eduNamesList = _allUsers
                    .Where(u => !string.IsNullOrWhiteSpace(u.RealName))
                    .OrderBy(u => { int i = Array.IndexOf(teamOrder, u.TeamName); return i < 0 ? 99 : i; })
                    .ThenBy(u => u.RealName)
                    .Select(u => $"[{u.TeamName}] {u.RealName}")
                    .ToList();

                _eduNamesView = CollectionViewSource.GetDefaultView(_eduNamesList);
                CmbEduName.ItemsSource = _eduNamesView;
                if (_eduNamesList.Count > 0) CmbEduName.SelectedIndex = 0;

                // 검색 필터링: 템플릿 로드 후 내부 TextBox에 이벤트 연결
                CmbEduName.Loaded += (_, _) =>
                {
                    if (CmbEduName.Template?.FindName("PART_EditableTextBox", CmbEduName) is TextBox tb)
                    {
                        tb.TextChanged += (__, ___) =>
                        {
                            string kw = tb.Text ?? "";
                            _eduNamesView!.Filter = o =>
                                string.IsNullOrEmpty(kw) ||
                                o.ToString()!.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0;
                            _eduNamesView.Refresh();
                            if (CmbEduName.Items.Count > 0) CmbEduName.IsDropDownOpen = true;
                        };
                    }
                };
            }
            else
            {
                TabEdu.Visibility = Visibility.Collapsed;
            }

            BtnSave.IsEnabled = false;
            MainTab.SelectionChanged += (_, __) => RefreshPreview();
            DpShiftEnd.SelectedDateChanged += (_, __) => RefreshPreview();
            DpEduEnd.SelectedDateChanged += (_, __) => RefreshPreview();
            CmbShiftName.SelectionChanged += (_, __) => RefreshPreview();
            CmbEduName.SelectionChanged += (_, __) => RefreshPreview();

            // 창이 열릴 때 백그라운드에서 공휴일 데이터를 로드
            this.Loaded += async (s, e) =>
            {
                int currentYear = DateTime.Today.Year;
                var thisYearHolidays = await HolidayManager.GetHolidaysAsync(currentYear);
                var nextYearHolidays = await HolidayManager.GetHolidaysAsync(currentYear + 1);

                _dynamicHolidays.AddRange(thisYearHolidays);
                _dynamicHolidays.AddRange(nextYearHolidays);
                _dynamicHolidays = _dynamicHolidays.Distinct().ToList();
                _isHolidayLoaded = true;
                BtnSave.IsEnabled = true;
                TxtHolidayLoadStatus.Text = "공휴일 데이터 로딩 완료";
                RefreshPreview();
            };

            RefreshPreview();
        }

        private void DpShiftStart_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DpShiftStart.SelectedDate.HasValue)
            {
                DpShiftEnd.SelectedDate = DpShiftStart.SelectedDate;
                RefreshPreview();
            }
        }

        private void DpEduStart_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DpEduStart.SelectedDate.HasValue)
            {
                DpEduEnd.SelectedDate = DpEduStart.SelectedDate;
                RefreshPreview();
            }
        }

        private void CmbShiftType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbHalfTime == null) return;
            string selected = (CmbShiftType.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
            CmbHalfTime.Visibility = selected == "반반차" ? Visibility.Visible : Visibility.Collapsed;
            RefreshPreview();
        }

        private void CmbEduTeam_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string? selectedTeam = CmbEduTeam.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedTeam)) return;

            var teamMembers = _allUsers.Where(u => u.TeamName == selectedTeam)
                                       .Select(u => u.RealName).ToList();

            CmbEduName.ItemsSource = teamMembers;
            if (teamMembers.Count > 0) CmbEduName.SelectedIndex = 0;
            RefreshPreview();
        }

        private int CountBusinessDays(DateTime startDate, DateTime endDate)
        {
            int count = 0;
            for (DateTime dt = startDate; dt <= endDate; dt = dt.AddDays(1))
            {
                bool isWeekend = dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday;
                bool isHoliday = _dynamicHolidays.Contains(dt.ToString("yyyy-MM-dd"));
                if (!isWeekend && !isHoliday) count++;
            }
            return count;
        }

        private void RefreshPreview()
        {
            if (TxtShiftPreview == null || TxtEduPreview == null) return;

            if (DpShiftStart.SelectedDate.HasValue && DpShiftEnd.SelectedDate.HasValue)
            {
                DateTime start = DpShiftStart.SelectedDate.Value.Date;
                DateTime end = DpShiftEnd.SelectedDate.Value.Date;
                if (start > end)
                {
                    TxtShiftPreview.Text = "시작일이 종료일보다 늦습니다.";
                }
                else
                {
                    int total = (end - start).Days + 1;
                    int business = _isHolidayLoaded ? CountBusinessDays(start, end) : 0;
                    string name = CmbShiftName.SelectedItem?.ToString() ?? SessionManager.CurrentRealName;
                    TxtShiftPreview.Text = _isHolidayLoaded
                        ? $"대상: {name} | 선택 {total}일 → 반영 {business}일 (주말/공휴일 제외)"
                        : $"대상: {name} | 선택 {total}일 (공휴일 계산 대기중)";
                }
            }

            if (DpEduStart.SelectedDate.HasValue && DpEduEnd.SelectedDate.HasValue)
            {
                DateTime start = DpEduStart.SelectedDate.Value.Date;
                DateTime end = DpEduEnd.SelectedDate.Value.Date;
                if (start > end)
                {
                    TxtEduPreview.Text = "시작일이 종료일보다 늦습니다.";
                }
                else
                {
                    int total = (end - start).Days + 1;
                    int business = _isHolidayLoaded ? CountBusinessDays(start, end) : 0;
                    string name = CmbEduName.SelectedItem?.ToString() ?? "-";
                    TxtEduPreview.Text = _isHolidayLoaded
                        ? $"대상: {name} | 선택 {total}일 → 스케줄 반영 {business}일"
                        : $"대상: {name} | 선택 {total}일 (공휴일 계산 대기중)";
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isHolidayLoaded)
                {
                    MessageBox.Show("공휴일 데이터를 불러오는 중입니다. 잠시 후 다시 저장해주세요.", "잠시만 기다려주세요", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (MainTab.SelectedIndex == 0) // 근태/휴가 다중 등록
                {
                    string selection = CmbShiftName.SelectedItem?.ToString() ?? "";
                    string name = selection.Contains("]") ? selection.Substring(selection.IndexOf(']') + 1).Trim() : selection;

                    if (string.IsNullOrEmpty(name) || !DpShiftStart.SelectedDate.HasValue || !DpShiftEnd.SelectedDate.HasValue) return;

                    DateTime startDate = DpShiftStart.SelectedDate.Value.Date;
                    DateTime endDate = DpShiftEnd.SelectedDate.Value.Date;

                    if (startDate > endDate)
                    {
                        MessageBox.Show("시작일이 종료일보다 늦을 수 없습니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string shiftType = (CmbShiftType.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "연차";
                    if (shiftType == "반반차")
                    {
                        string timeSlot = (CmbHalfTime.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
                        shiftType = $"반반차 ({timeSlot})";
                    }

                    int registeredCount = 0;
                    string teamGroup = _allUsers.FirstOrDefault(u => u.RealName == name)?.TeamName ?? "";

                    for (DateTime dt = startDate; dt <= endDate; dt = dt.AddDays(1))
                    {
                        bool isWeekend = dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday;
                        bool isHoliday = _dynamicHolidays.Contains(dt.ToString("yyyy-MM-dd"));

                        if (!isWeekend && !isHoliday)
                        {
                            var shiftModel = new ShiftScheduleModel
                            {
                                TargetDate = dt,
                                MemberName = name,
                                TeamGroup = teamGroup,
                                ShiftType = shiftType
                            };

                            DatabaseHelper.UpsertShiftSchedule(shiftModel);
                            registeredCount++;
                        }
                    }

                    if (registeredCount == 0)
                    {
                        MessageBox.Show("선택하신 기간 내에 등록 가능한 평일이 없습니다. (주말 및 법정 공휴일 자동 제외)", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    MessageBox.Show($"주말/공휴일을 제외하고 총 {registeredCount}일의 일정이 성공적으로 등록되었습니다.", "등록 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else // 교육 일정 등록
                {
                    string rawSel = CmbEduName.SelectedItem?.ToString() ?? CmbEduName.Text ?? "";
                    string name = rawSel.Contains("]") ? rawSel.Substring(rawSel.IndexOf(']') + 1).Trim() : rawSel;
                    string course = TxtEduCourse.Text.Trim();
                    string method = TxtEduMethod.Text.Trim();

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(course) || string.IsNullOrEmpty(method) ||
                        !DpEduStart.SelectedDate.HasValue || !DpEduEnd.SelectedDate.HasValue)
                    {
                        MessageBox.Show("대상자, 교육명, 기간, 교육 내용을 모두 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    DateTime startDate = DpEduStart.SelectedDate.Value.Date;
                    DateTime endDate = DpEduEnd.SelectedDate.Value.Date;

                    if (startDate > endDate)
                    {
                        MessageBox.Show("시작일이 종료일보다 늦을 수 없습니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 1. 교육 모델에 기본 저장
                    var eduModel = new EducationPlanModel
                    {
                        MemberName = name,
                        CourseName = course,
                        StartDate = startDate,
                        EndDate = endDate,
                        EduMethod = method,
                        Status = "대기",
                        Progress = 0
                    };
                    DatabaseHelper.InsertEducationPlan(eduModel);

                    // 🔥 2. 누락되었던 로직: 스케줄 보드(ShiftScheduleModel)에도 '교육'으로 자동 동시 등록
                    int shiftRegisteredCount = 0;
                    string teamGroup = _allUsers.FirstOrDefault(u => u.RealName == name)?.TeamName ?? "";

                    for (DateTime dt = startDate; dt <= endDate; dt = dt.AddDays(1))
                    {
                        bool isWeekend = dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday;
                        bool isHoliday = _dynamicHolidays.Contains(dt.ToString("yyyy-MM-dd"));

                        if (!isWeekend && !isHoliday)
                        {
                            var shiftModel = new ShiftScheduleModel
                            {
                                TargetDate = dt,
                                MemberName = name,
                                TeamGroup = teamGroup,
                                ShiftType = "교육" // 스케줄 보드에는 보라색 '교육'으로 렌더링 됨
                            };

                            // 스케줄 보드에 덮어쓰기 등록
                            DatabaseHelper.UpsertShiftSchedule(shiftModel);
                            shiftRegisteredCount++;
                        }
                    }

                    if (shiftRegisteredCount > 0)
                    {
                        MessageBox.Show($"교육 일정이 성공적으로 등록되었으며, 스케줄 보드에도 {shiftRegisteredCount}일간 연동 반영되었습니다.", "등록 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("교육 일정이 등록되었으나, 기간 내 평일이 없어 스케줄 보드에는 반영되지 않았습니다.", "등록 완료 (평일 없음)", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                this.DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"등록 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => this.DialogResult = false;
    }
}