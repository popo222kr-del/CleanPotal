using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CleanPotal
{
    public partial class MainWindow : Window
    {
        private string _currentViewName = "Portal";
        private HandoverView? _handoverView;
        private ScheduleBoardView? _scheduleBoardView;
        private TeamScheduleView? _teamScheduleView; // 달력 뷰 인스턴스
        private ReportAutomationView? _reportAutomationView;
        private ProdReqView? _prodReqView;

        private bool _isUpdatingNav = false;
        private bool _isSidebarOpen = true;

        public MainWindow()
        {
            InitializeComponent();
            WindowState = WindowState.Maximized;

            if (SessionManager.IsLoggedIn)
            {
                string realName = SessionManager.CurrentRealName ?? "";

                LoginUserNameText.Text = string.IsNullOrEmpty(realName) ? "사용자" : realName;
                AvatarText.Text = string.IsNullOrEmpty(realName) ? "?" : realName.Substring(0, 1);

                string team = SessionManager.CurrentTeamName ?? "소속없음";
                string id = SessionManager.CurrentUsername ?? "";
                LoginUserRoleText.Text = string.IsNullOrEmpty(id) ? team : $"{id} · {team}";

                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else
            {
                LoginUserNameText.Text = "로그인 정보 없음";
                AvatarText.Text = "?";
                LoginUserRoleText.Text = "오프라인";
                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            }

            BtnCommandNotice.Click += BtnCommandNotice_Click;
            BtnCommandSecondary.Click += BtnCommandSecondary_Click;
            BtnCommandVendor.Click += BtnCommandVendor_Click;

            DatabaseHelper.InitializeDatabase();
            this.Loaded += (s, e) => ShowPortal();
        }

        private void HeaderSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (MainContent.Content is PortalView pv)
            {
                pv.FilterAndBind(HeaderSearchBox.Text);
            }
        }

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (_isSidebarOpen) CloseSidebar();
            else OpenSidebar();
        }

        private void OpenSidebar()
        {
            if (_isSidebarOpen) return;
            _isSidebarOpen = true;
            BtnToggleSidebar.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            AnimateSidebarWidth(260);
            RestoreActiveExpander();
        }

        private void CloseSidebar()
        {
            if (!_isSidebarOpen) return;
            _isSidebarOpen = false;
            BtnToggleSidebar.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            AnimateSidebarWidth(64);

            _isUpdatingNav = true;
            ExpanderAttendance.IsExpanded = false;
            ExpanderProduction.IsExpanded = false;
            ExpanderOffice.IsExpanded = false;
            ExpanderEtc.IsExpanded = false;
            _isUpdatingNav = false;
        }

        private void AnimateSidebarWidth(double toWidth)
        {
            var anim = new DoubleAnimation
            {
                To = toWidth,
                Duration = TimeSpan.FromSeconds(0.15),
                DecelerationRatio = 0.9
            };
            SidebarBorder.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }

        private void RestoreActiveExpander()
        {
            _isUpdatingNav = true;
            if (_currentViewName == "Report") ExpanderEtc.IsExpanded = true;
            else if (_currentViewName == "Handover" || _currentViewName == "ProdReq" || _currentViewName == "Schedule") ExpanderProduction.IsExpanded = true;
            else if (_currentViewName == "TeamSchedule") ExpanderAttendance.IsExpanded = true;
            else if (_currentViewName == "WeeklyReport" || _currentViewName == "PersonalTask") ExpanderOffice.IsExpanded = true;
            _isUpdatingNav = false;
        }

        private void ExpanderAttendance_Expanded(object sender, RoutedEventArgs e)
        {
            OpenSidebar();
            if (!_isUpdatingNav && _currentViewName != "TeamSchedule") OpenTeamSchedule(sender, e);
        }

        private void ExpanderProduction_Expanded(object sender, RoutedEventArgs e)
        {
            OpenSidebar();
            if (!_isUpdatingNav && _currentViewName != "Handover") OpenHandover(sender, e);
        }

        private void ExpanderOffice_Expanded(object sender, RoutedEventArgs e)
        {
            OpenSidebar();
            if (!_isUpdatingNav && _currentViewName != "WeeklyReport" && _currentViewName != "PersonalTask")
                OpenWeeklyReport_Click(sender, e);
        }

        private void ExpanderEtc_Expanded(object sender, RoutedEventArgs e)
        {
            OpenSidebar();
            if (!_isUpdatingNav && _currentViewName != "Report") OpenReport_Click(sender, e);
        }

        private void OpenPortal(object sender, RoutedEventArgs e) { OpenSidebar(); ShowPortal(); }
        private void OpenHandover(object sender, RoutedEventArgs e) { OpenSidebar(); ShowHandover(); }
        private void OpenSchedule(object sender, RoutedEventArgs e) { OpenSidebar(); ShowSchedule(); }
        private void OpenTeamSchedule(object sender, RoutedEventArgs e) { OpenSidebar(); ShowTeamSchedule(); }
        private void OpenProdReq_Click(object sender, RoutedEventArgs e) { OpenSidebar(); ShowProdReq(); }
        private void OpenWeeklyReport_Click(object sender, RoutedEventArgs e) { OpenSidebar(); ShowWeeklyReport(); }
        private void OpenPersonalTask_Click(object sender, RoutedEventArgs e) { OpenSidebar(); ShowPersonalTask(); }

        private void OpenReport_Click(object sender, RoutedEventArgs e)
        {
            OpenSidebar();
            string userTeam = SessionManager.CurrentTeamName;
            if (userTeam != "Office" && userTeam != "관리자")
            {
                MessageBox.Show("해당 기능은 Office 소속 인원만 사용할 수 있습니다.", "접근 권한 제한", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ShowReport();
        }

        private void BtnCommandVendor_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthManager.CheckAuth(PermissionType.Vendors)) return;
            var vendorWin = new VendorManagerWindow { Owner = this };
            vendorWin.ShowDialog();
        }

        private void ManagePortalLinks_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthManager.CheckAuth(PermissionType.Files)) return;
            PortalManagerWindow win = new PortalManagerWindow { Owner = this };
            win.ShowDialog();
            if (_currentViewName == "Portal") ShowPortal();
        }

        private void ApplySectionMeta(string title, string description)
        {
            CurrentSectionTitleText.Text = title;
            CurrentSectionDescriptionText.Text = description;
            CurrentSectionDescriptionText.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void HideAllHeaderButtons()
        {
            HeaderSearchBoxArea.Visibility = Visibility.Collapsed;
            HeaderCalendarControlArea.Visibility = Visibility.Collapsed; // 캘린더 컨트롤 숨김
            HeaderSearchBox.Text = "";

            BtnCommandPrimary.Visibility = Visibility.Collapsed;
            BtnCommandNotice.Visibility = Visibility.Collapsed;
            BtnCommandSecondary.Visibility = Visibility.Collapsed;
            BtnCommandVendor.Visibility = Visibility.Collapsed;
            BtnCommandRecipeManage.Visibility = Visibility.Collapsed;
            BtnCommandCapture.Visibility = Visibility.Collapsed;
            BtnCommandUndo.Visibility = Visibility.Collapsed;
            BtnCommandPartialReset.Visibility = Visibility.Collapsed;
            BtnCommandReset.Visibility = Visibility.Collapsed;
        }

        private void ShowPortal()
        {
            _currentViewName = "Portal";
            ApplySectionMeta("업무 파일 통합 관리", "자주 사용하는 파일과 폴더를 빠르게 실행합니다.");
            UpdateNavSelection("Portal");

            MainContent.Content = new PortalView();

            HideAllHeaderButtons();
            HeaderSearchBoxArea.Visibility = Visibility.Visible;
            BtnCommandPrimary.Content = "파일 관리자";
            BtnCommandPrimary.Visibility = Visibility.Visible;
        }

        private void ShowReport()
        {
            _currentViewName = "Report";
            ApplySectionMeta("성적서 자동 변환", "NAS 서버의 성적서 엑셀 파일을 복사하고 S/N 이름으로 PDF를 일괄 변환합니다.");
            UpdateNavSelection("Report");

            if (_reportAutomationView == null) _reportAutomationView = new ReportAutomationView();
            MainContent.Content = _reportAutomationView;

            HideAllHeaderButtons();
        }

        private void ShowHandover()
        {
            _currentViewName = "Handover";
            ApplySectionMeta("현장 업무 인수인계", "업체별 진행 상황을 기록하고 배차를 관리합니다.");
            UpdateNavSelection("Handover");

            if (_handoverView == null) _handoverView = new HandoverView();
            MainContent.Content = _handoverView;

            HideAllHeaderButtons();
            BtnCommandNotice.Content = "공지 관리";
            BtnCommandNotice.Visibility = Visibility.Visible;
            BtnCommandSecondary.Content = "완료 목록 조회";
            BtnCommandSecondary.Visibility = Visibility.Visible;
            BtnCommandVendor.Content = "업체 정보 관리";
            BtnCommandVendor.Visibility = Visibility.Visible;
        }

        private void ShowProdReq()
        {
            _currentViewName = "ProdReq";
            ApplySectionMeta("생산팀 요청사항", "생산팀의 부자재/수리 요청을 기록하고 조치 결과를 관리합니다.");
            UpdateNavSelection("ProdReq");

            if (_prodReqView == null) _prodReqView = new ProdReqView();
            MainContent.Content = _prodReqView;

            HideAllHeaderButtons();
        }

        private void ShowSchedule()
        {
            _currentViewName = "Schedule";
            ApplySectionMeta("스케줄보드", "생산 라인별 스케줄 및 레시피를 관리합니다.");
            UpdateNavSelection("Schedule");

            if (_scheduleBoardView == null) _scheduleBoardView = new ScheduleBoardView();
            MainContent.Content = _scheduleBoardView;

            HideAllHeaderButtons();
            BtnCommandRecipeManage.Visibility = Visibility.Visible;
            BtnCommandCapture.Visibility = Visibility.Visible;
            BtnCommandUndo.Visibility = Visibility.Visible;
            BtnCommandPartialReset.Visibility = Visibility.Visible;
            BtnCommandReset.Visibility = Visibility.Visible;
        }

        // 🔥 달력 화면 진입 시 헤더 컨트롤을 켜고 연결하는 로직
        private void ShowTeamSchedule()
        {
            _currentViewName = "TeamSchedule";

            if (_teamScheduleView == null)
            {
                _teamScheduleView = new TeamScheduleView();
                // 캘린더에서 달이 바뀔 때 메인 헤더의 텍스트(예: 2026년 4월)를 실시간으로 바꿔줍니다.
                _teamScheduleView.MonthTextChanged += (monthText) => { HeaderMonthYearText.Text = monthText; };
            }

            MainContent.Content = _teamScheduleView;

            ApplySectionMeta("세정팀 통합 일정 달력", "근무조 교대, 연차, 교육 등 부서 전체 일정을 한눈에 파악합니다.");
            UpdateNavSelection("TeamSchedule");

            HideAllHeaderButtons();

            // 진입 시 현재 달력의 년/월을 강제로 한 번 렌더링
            HeaderMonthYearText.Text = _teamScheduleView.CurrentMonthText;
            HeaderCalendarControlArea.Visibility = Visibility.Visible;
        }

        private void ShowWeeklyReport()
        {
            _currentViewName = "WeeklyReport";
            ApplySectionMeta("주간보고", "부서 주간보고 내역을 관리합니다.");
            UpdateNavSelection("WeeklyReport");
            MainContent.Content = null;
            HideAllHeaderButtons();
        }

        private void ShowPersonalTask()
        {
            _currentViewName = "PersonalTask";
            ApplySectionMeta("개인업무", "개인별 업무 진행 상태와 일정을 관리합니다.");
            UpdateNavSelection("PersonalTask");
            MainContent.Content = null;
            HideAllHeaderButtons();
        }

        private void BtnCommandNotice_Click(object sender, RoutedEventArgs e)
        {
            if (MainContent.Content is HandoverView hv) hv.OpenNoticeModal();
        }

        private void BtnCommandSecondary_Click(object sender, RoutedEventArgs e)
        {
            if (MainContent.Content is HandoverView hv) hv.OpenDoneModal();
        }

        private void BtnCommandRecipeManage_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.OpenRecipeManager();
        private void BtnCommandCapture_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.CaptureBoard();
        private void BtnCommandUndo_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.UndoAction();
        private void BtnCommandPartialReset_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.PartialReset();
        private void BtnCommandReset_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.ResetAll();

        // 🔥 메인 헤더에 생성된 달력 조작 버튼 클릭 시 -> 캘린더 뷰(_teamScheduleView)의 명령을 실행
        private void HeaderPrevMonth_Click(object sender, RoutedEventArgs e) => _teamScheduleView?.GoPrevMonth();
        private void HeaderNextMonth_Click(object sender, RoutedEventArgs e) => _teamScheduleView?.GoNextMonth();
        private void HeaderToday_Click(object sender, RoutedEventArgs e) => _teamScheduleView?.GoToday();
        private void HeaderCreatePattern_Click(object sender, RoutedEventArgs e) => _teamScheduleView?.CreatePattern();
        private void HeaderRegisterSchedule_Click(object sender, RoutedEventArgs e) => _teamScheduleView?.RegisterSchedule();

        private void UpdateNavSelection(string viewName)
        {
            _isUpdatingNav = true;

            var mainNormal = (Style)FindResource("ErpMainButtonStyle");
            var mainSelected = (Style)FindResource("ErpMainButtonSelectedStyle");
            var subNormal = (Style)FindResource("ErpSubButtonStyle");
            var subSelected = (Style)FindResource("ErpSubButtonSelectedStyle");

            var expNormal = (Style)FindResource("SidebarExpanderStyle");
            var expActive = (Style)FindResource("SidebarExpanderActiveStyle");

            BtnNavPortal.Style = mainNormal;
            BtnNavReport.Style = subNormal;
            BtnNavHandover.Style = subNormal;
            BtnNavProdReq.Style = subNormal;
            BtnNavTeamSchedule.Style = subNormal;
            BtnNavSchedule.Style = subNormal;
            BtnNavWeeklyReport.Style = subNormal;
            BtnNavPersonalTask.Style = subNormal;

            ExpanderAttendance.Style = expNormal;
            ExpanderProduction.Style = expNormal;
            ExpanderOffice.Style = expNormal;
            ExpanderEtc.Style = expNormal;

            switch (viewName)
            {
                case "Portal":
                    BtnNavPortal.Style = mainSelected;
                    break;
                case "Report":
                    BtnNavReport.Style = subSelected;
                    ExpanderEtc.Style = expActive;
                    if (_isSidebarOpen) ExpanderEtc.IsExpanded = true;
                    break;
                case "Handover":
                    BtnNavHandover.Style = subSelected;
                    ExpanderProduction.Style = expActive;
                    if (_isSidebarOpen) ExpanderProduction.IsExpanded = true;
                    break;
                case "ProdReq":
                    BtnNavProdReq.Style = subSelected;
                    ExpanderProduction.Style = expActive;
                    if (_isSidebarOpen) ExpanderProduction.IsExpanded = true;
                    break;
                case "Schedule":
                    BtnNavSchedule.Style = subSelected;
                    ExpanderProduction.Style = expActive;
                    if (_isSidebarOpen) ExpanderProduction.IsExpanded = true;
                    break;
                case "TeamSchedule":
                    BtnNavTeamSchedule.Style = subSelected;
                    ExpanderAttendance.Style = expActive;
                    if (_isSidebarOpen) ExpanderAttendance.IsExpanded = true;
                    break;
                case "WeeklyReport":
                    BtnNavWeeklyReport.Style = subSelected;
                    ExpanderOffice.Style = expActive;
                    if (_isSidebarOpen) ExpanderOffice.IsExpanded = true;
                    break;
                case "PersonalTask":
                    BtnNavPersonalTask.Style = subSelected;
                    ExpanderOffice.Style = expActive;
                    if (_isSidebarOpen) ExpanderOffice.IsExpanded = true;
                    break;
            }

            _isUpdatingNav = false;
        }
    }
}