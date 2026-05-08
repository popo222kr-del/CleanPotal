using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CleanPotal
{
    public partial class MainWindow : Window
    {
        private string _currentViewName = "Portal";
        private HandoverView? _handoverView;
        private ScheduleBoardView? _scheduleBoardView;
        private TeamScheduleView? _teamScheduleView;
        private WeeklyReportView? _weeklyReportView;
        private ProductionMeetingView? _productionMeetingView;
        private ReportAutomationView? _reportAutomationView;
        private DispatchCertificateBatchView? _dispatchCertificateBatchView;
        private ProdReqView? _prodReqView;
        private PersonalMemoView? _personalMemoView;
        private FieldChecklistView? _fieldChecklistView;

        private bool _isUpdatingNav = false;
        private bool _isSidebarOpen = true;

        private DispatcherTimer? _pollingTimer;
        private int _lastReqCount = 0;
        private int _unreadReqCount = 0;

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

            this.Loaded += (s, e) => {
                ShowPortal();
                InitializePollingTimer();
            };
        }

        private void InitializePollingTimer()
        {
            try { _lastReqCount = DatabaseHelper.GetAllProdReqs().Count; } catch { _lastReqCount = 0; }

            _pollingTimer = new DispatcherTimer();
            _pollingTimer.Interval = TimeSpan.FromSeconds(15);
            _pollingTimer.Tick += PollingTimer_Tick;
            _pollingTimer.Start();
        }

        private void PollingTimer_Tick(object? sender, EventArgs e)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var currentList = DatabaseHelper.GetAllProdReqs();
                    int currentCount = currentList.Count;

                    Dispatcher.Invoke(() =>
                    {
                        if (currentCount > _lastReqCount && _lastReqCount != 0)
                        {
                            int newItemsCount = currentCount - _lastReqCount;
                            _lastReqCount = currentCount;

                            if (_currentViewName != "ProdReq")
                            {
                                _unreadReqCount += newItemsCount;
                                UpdateBadge();
                                ShowToast($"방금 새로운 요청사항 {newItemsCount}건이 등록되었습니다.");
                            }
                        }
                        else if (currentCount != _lastReqCount)
                        {
                            _lastReqCount = currentCount;
                        }
                    });
                }
                catch { }
            });
        }

        private void UpdateBadge()
        {
            if (_unreadReqCount > 0)
            {
                BadgeProdReq.Visibility = Visibility.Visible;
                BadgeProdReqText.Text = _unreadReqCount > 99 ? "99+" : _unreadReqCount.ToString();
                BadgeExpanderProduction.Visibility = Visibility.Visible;
            }
            else
            {
                BadgeProdReq.Visibility = Visibility.Collapsed;
                BadgeExpanderProduction.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowToast(string message)
        {
            ToastMessage.Text = message;
            ToastNotification.Visibility = Visibility.Visible;

            var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            hideTimer.Tick += (s, e) => {
                ToastNotification.Visibility = Visibility.Collapsed;
                hideTimer.Stop();
            };
            hideTimer.Start();
        }

        private void CloseToast_Click(object sender, RoutedEventArgs e) => ToastNotification.Visibility = Visibility.Collapsed;

        // 🔥 내 계정 수정 모달 열기
        private void BtnOpenAccountEdit_Click(object sender, RoutedEventArgs e)
        {
            AccountEditWindow editWin = new AccountEditWindow { Owner = this };
            if (editWin.ShowDialog() == true)
            {
                LoginUserNameText.Text = SessionManager.CurrentRealName;
                AvatarText.Text = SessionManager.CurrentRealName.Substring(0, 1);
            }
        }

        // 🔥 로그아웃 기능 추가
        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("현재 계정에서 로그아웃 하시겠습니까?", "로그아웃 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                SessionManager.Logout(); // 정보 삭제

                LoginWindow loginWin = new LoginWindow();
                loginWin.Show();

                this.Close();
            }
        }

        private void HeaderSearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (MainContent.Content is PortalView pv) pv.FilterAndBind(HeaderSearchBox.Text); }
        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e) { if (_isSidebarOpen) CloseSidebar(); else OpenSidebar(); }

        private void OpenSidebar()
        {
            if (_isSidebarOpen) return;
            _isSidebarOpen = true;
            BtnToggleSidebar.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            AnimateSidebarWidth(240);
            RestoreActiveExpander();
            ToggleSectionHeaders(Visibility.Visible); // 🎨 섹션 헤더 표시
        }

        private void CloseSidebar()
        {
            if (!_isSidebarOpen) return;
            _isSidebarOpen = false;
            BtnToggleSidebar.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            AnimateSidebarWidth(64);

            _isUpdatingNav = true;
            ExpanderAttendance.IsExpanded = false; ExpanderProduction.IsExpanded = false;
            ExpanderFieldInspection.IsExpanded = false;
            ExpanderOffice.IsExpanded = false; ExpanderEtc.IsExpanded = false;
            _isUpdatingNav = false;
            ToggleSectionHeaders(Visibility.Collapsed); // 🎨 접을 땐 섹션 헤더 숨김
        }

        // 🎨 섹션 헤더(MAIN/WORKSPACE/TOOLS) Visibility 일괄 제어
        private void ToggleSectionHeaders(Visibility visibility)
        {
            if (SectionHeaderMain != null) SectionHeaderMain.Visibility = visibility;
            if (SectionHeaderWorkspace != null) SectionHeaderWorkspace.Visibility = visibility;
            if (SectionHeaderTools != null) SectionHeaderTools.Visibility = visibility;
        }

        private void AnimateSidebarWidth(double toWidth)
        {
            var anim = new DoubleAnimation { To = toWidth, Duration = TimeSpan.FromSeconds(0.15), DecelerationRatio = 0.9 };
            SidebarBorder.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }

        private void RestoreActiveExpander()
        {
            _isUpdatingNav = true;
            if (_currentViewName == "Report" || _currentViewName == "DispatchCert") ExpanderEtc.IsExpanded = true;
            else if (_currentViewName == "Handover" || _currentViewName == "ProdReq" || _currentViewName == "Schedule") ExpanderProduction.IsExpanded = true;
            else if (_currentViewName == "TeamSchedule") ExpanderAttendance.IsExpanded = true;
            else if (_currentViewName == "WeeklyReport") ExpanderOffice.IsExpanded = true;
            else if (_currentViewName == "PersonalTask") ExpanderProduction.IsExpanded = true;
            else if (_currentViewName == "FieldChecklist") ExpanderFieldInspection.IsExpanded = true;
            _isUpdatingNav = false;
        }

        private void ExpanderAttendance_Expanded(object sender, RoutedEventArgs e) { OpenSidebar(); if (!_isUpdatingNav && _currentViewName != "TeamSchedule") OpenTeamSchedule(sender, e); }
        private void ExpanderProduction_Expanded(object sender, RoutedEventArgs e) { OpenSidebar(); if (!_isUpdatingNav && _currentViewName != "Handover" && _currentViewName != "PersonalTask" && _currentViewName != "ProdReq" && _currentViewName != "Schedule") OpenHandover(sender, e); }
        private void ExpanderOffice_Expanded(object sender, RoutedEventArgs e) { OpenSidebar(); if (!_isUpdatingNav && _currentViewName != "WeeklyReport" && _currentViewName != "PersonalTask") OpenWeeklyReport_Click(sender, e); }
        private void ExpanderEtc_Expanded(object sender, RoutedEventArgs e) { OpenSidebar(); if (!_isUpdatingNav && _currentViewName != "Report" && _currentViewName != "DispatchCert") OpenReport_Click(sender, e); }
        private void ExpanderFieldInspection_Expanded(object sender, RoutedEventArgs e) { OpenSidebar(); if (!_isUpdatingNav && _currentViewName != "FieldChecklist") OpenFieldChecklist_Click(sender, e); }

        private void OpenPortal(object sender, RoutedEventArgs e) { OpenSidebar(); ShowPortal(); }
        private void OpenHandover(object sender, RoutedEventArgs e) { OpenSidebar(); ShowHandover(); }
        private void OpenSchedule(object sender, RoutedEventArgs e) { OpenSidebar(); ShowSchedule(); }
        private void OpenTeamSchedule(object sender, RoutedEventArgs e) { OpenSidebar(); ShowTeamSchedule(); }
        private void OpenProdReq_Click(object sender, RoutedEventArgs e) { OpenSidebar(); ShowProdReq(); }

        private void OpenWeeklyReport_Click(object sender, RoutedEventArgs e)
        {
            OpenSidebar();
            if (!AuthManager.CheckAuth(PermissionType.WeeklyReport)) return;
            ShowWeeklyReport();
        }

        private void OpenPersonalTask_Click(object sender, RoutedEventArgs e) { OpenSidebar(); ShowPersonalTask(); }

        private bool CanOpenEtcOfficeFeature()
        {
            string userTeam = SessionManager.CurrentTeamName;
            if (userTeam != "Office" && userTeam != "관리자")
            {
                MessageBox.Show("해당 기능은 Office 소속 인원만 사용할 수 있습니다.", "접근 권한 제한", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private void OpenDispatchCert_Click(object sender, RoutedEventArgs e) { OpenSidebar(); if (!CanOpenEtcOfficeFeature()) return; ShowDispatchCert(); }
        private void OpenReport_Click(object sender, RoutedEventArgs e) { OpenSidebar(); if (!CanOpenEtcOfficeFeature()) return; ShowReport(); }
        private void OpenPersonalMemo_Click(object sender, RoutedEventArgs e) { OpenSidebar(); ShowPersonalMemo(); }
        private void OpenFieldChecklist_Click(object sender, RoutedEventArgs e) { OpenSidebar(); ShowFieldChecklist(); }
        private void BtnCommandVendor_Click(object sender, RoutedEventArgs e) { if (!AuthManager.CheckAuth(PermissionType.Vendors)) return; new VendorManagerWindow { Owner = this }.ShowDialog(); }

        private void ManagePortalLinks_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthManager.CheckAuth(PermissionType.Files)) return;
            new PortalManagerWindow { Owner = this }.ShowDialog();
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
            if (HeaderCalendarControlArea != null) HeaderCalendarControlArea.Visibility = Visibility.Collapsed;
            HeaderSearchBox.Text = "";

            BtnCommandPrimary.Visibility = Visibility.Collapsed; BtnCommandNotice.Visibility = Visibility.Collapsed;
            BtnCommandSecondary.Visibility = Visibility.Collapsed; BtnCommandVendor.Visibility = Visibility.Collapsed;
            BtnCommandRecipeManage.Visibility = Visibility.Collapsed; BtnCommandCapture.Visibility = Visibility.Collapsed;
            BtnCommandUndo.Visibility = Visibility.Collapsed; BtnCommandPartialReset.Visibility = Visibility.Collapsed;
            BtnCommandReset.Visibility = Visibility.Collapsed; BtnCommandNewReq.Visibility = Visibility.Collapsed;
        }

        private void ShowPortal()
        {
            _currentViewName = "Portal";
            ApplySectionMeta("업무 파일 통합 관리", "자주 사용하는 파일과 폴더를 빠르게 실행합니다.");
            UpdateNavSelection("Portal");
            MainContent.Content = new PortalView();
            HideAllHeaderButtons(); HeaderSearchBoxArea.Visibility = Visibility.Visible;
            BtnCommandPrimary.Content = "파일 관리자"; BtnCommandPrimary.Visibility = Visibility.Visible;
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
            BtnCommandNotice.Content = "공지 관리"; BtnCommandNotice.Visibility = Visibility.Visible;
            BtnCommandSecondary.Content = "완료 목록 조회"; BtnCommandSecondary.Visibility = Visibility.Visible;
            BtnCommandVendor.Content = "업체 정보 관리"; BtnCommandVendor.Visibility = Visibility.Visible;
        }

        private void ShowProdReq()
        {
            _currentViewName = "ProdReq";
            ApplySectionMeta("생산팀 요청사항", "생산팀의 부자재/수리 요청을 기록하고 조치 결과를 관리합니다.");
            UpdateNavSelection("ProdReq");

            _unreadReqCount = 0; UpdateBadge(); ToastNotification.Visibility = Visibility.Collapsed;

            _prodReqView = new ProdReqView();
            MainContent.Content = _prodReqView;
            HideAllHeaderButtons();
            BtnCommandNewReq.Visibility = Visibility.Visible;
        }

        private void ShowSchedule()
        {
            _currentViewName = "Schedule";
            ApplySectionMeta("스케줄보드", "생산 라인별 스케줄 및 레시피를 관리합니다.");
            UpdateNavSelection("Schedule");
            if (_scheduleBoardView == null) _scheduleBoardView = new ScheduleBoardView();
            MainContent.Content = _scheduleBoardView;
            HideAllHeaderButtons();
            BtnCommandRecipeManage.Visibility = Visibility.Visible; BtnCommandCapture.Visibility = Visibility.Visible;
            BtnCommandUndo.Visibility = Visibility.Visible; BtnCommandPartialReset.Visibility = Visibility.Visible; BtnCommandReset.Visibility = Visibility.Visible;
        }

        private void ShowTeamSchedule()
        {
            _currentViewName = "TeamSchedule";
            if (_teamScheduleView == null)
            {
                _teamScheduleView = new TeamScheduleView();
                _teamScheduleView.MonthTextChanged += (monthText) => { if (HeaderMonthYearText != null) HeaderMonthYearText.Text = monthText; };
            }
            MainContent.Content = _teamScheduleView;
            ApplySectionMeta("세정팀 통합 일정 달력", "근무조 교대, 연차, 교육 등 부서 전체 일정을 한눈에 파악합니다.");
            UpdateNavSelection("TeamSchedule");
            HideAllHeaderButtons();
            if (HeaderMonthYearText != null) HeaderMonthYearText.Text = _teamScheduleView.CurrentMonthText;
            if (HeaderCalendarControlArea != null) HeaderCalendarControlArea.Visibility = Visibility.Visible;
        }

        private void ShowWeeklyReport()
        {
            _currentViewName = "WeeklyReport";
            if (_weeklyReportView == null) _weeklyReportView = new WeeklyReportView();
            MainContent.Content = _weeklyReportView;
            ApplySectionMeta("주간보고", "부서 주간보고 내역을 관리하고 지난 업무를 팔로업합니다.");
            UpdateNavSelection("WeeklyReport");
            HideAllHeaderButtons();
            BtnCommandNotice.Content = "보고용 표 보기"; BtnCommandNotice.Visibility = Visibility.Visible;
            BtnCommandSecondary.Content = "변경사항 저장"; BtnCommandSecondary.Visibility = Visibility.Visible;
        }

        private void ShowPersonalMemo()
        {
            _currentViewName = "PersonalMemo";
            if (_personalMemoView == null) _personalMemoView = new PersonalMemoView();
            MainContent.Content = _personalMemoView;
            ApplySectionMeta("개인 메모장", "개인 업무 메모를 작성하고 관리합니다.");
            UpdateNavSelection("PersonalMemo");
            HideAllHeaderButtons();
        }

        private void ShowPersonalTask()
        {
            _currentViewName = "PersonalTask";
            ApplySectionMeta("생산 미팅", "생산 관련 미팅 및 협의 내용을 관리합니다.");
            if (_productionMeetingView == null) _productionMeetingView = new ProductionMeetingView();
            MainContent.Content = _productionMeetingView;
            UpdateNavSelection("PersonalTask");
            HideAllHeaderButtons();
            BtnCommandSecondary.Content = "변경사항 저장"; BtnCommandSecondary.Visibility = Visibility.Visible;
        }

        private void ShowFieldChecklist()
        {
            _currentViewName = "FieldChecklist";
            ApplySectionMeta("현장 점검 - 체크시트", "NFC/QR 기반 현장 체크시트를 등록·조회·출력합니다.");
            UpdateNavSelection("FieldChecklist");
            if (_fieldChecklistView == null) _fieldChecklistView = new FieldChecklistView();
            else _fieldChecklistView.RefreshDashboardCounters();
            MainContent.Content = _fieldChecklistView;
            HideAllHeaderButtons();
        }

        private void ShowDispatchCert()
        {
            _currentViewName = "DispatchCert";
            ApplySectionMeta("반출등록 성적서 생성", "반출등록 시트 데이터를 기준으로 템플릿 성적서를 수량만큼 자동 생성하고 생성이력을 기록합니다.");
            UpdateNavSelection("DispatchCert");
            if (_dispatchCertificateBatchView == null) _dispatchCertificateBatchView = new DispatchCertificateBatchView();
            MainContent.Content = _dispatchCertificateBatchView;
            HideAllHeaderButtons();
        }

        private void BtnCommandNotice_Click(object sender, RoutedEventArgs e) { if (MainContent.Content is HandoverView hv) hv.OpenNoticeModal(); else if (MainContent.Content is WeeklyReportView wr) wr.ShowReportTable(); }
        private void BtnCommandSecondary_Click(object sender, RoutedEventArgs e) { if (MainContent.Content is HandoverView hv) hv.OpenDoneModal(); else if (MainContent.Content is WeeklyReportView wr) wr.SaveReportChanges(); else if (MainContent.Content is ProductionMeetingView pm) pm.SaveReportChanges(); }
        private void BtnCommandRecipeManage_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.OpenRecipeManager();
        private void BtnCommandCapture_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.CaptureBoard();
        private void BtnCommandUndo_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.UndoAction();
        private void BtnCommandPartialReset_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.PartialReset();
        private void BtnCommandReset_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.ResetAll();
        private void BtnCommandNewReq_Click(object sender, RoutedEventArgs e) => _prodReqView?.OpenRegisterModal();
        private void HeaderPrevMonth_Click(object sender, RoutedEventArgs e) => _teamScheduleView?.GoPrevMonth();
        private void HeaderNextMonth_Click(object sender, RoutedEventArgs e) => _teamScheduleView?.GoNextMonth();
        private void HeaderToday_Click(object sender, RoutedEventArgs e) => _teamScheduleView?.GoToday();
        private void HeaderCreatePattern_Click(object sender, RoutedEventArgs e) => _teamScheduleView?.CreatePattern();
        private void HeaderRegisterSchedule_Click(object sender, RoutedEventArgs e) => _teamScheduleView?.RegisterSchedule();

        private void UpdateNavSelection(string viewName)
        {
            _isUpdatingNav = true;

            var mainNormal = (Style)FindResource("ErpMainButtonStyle"); var mainSelected = (Style)FindResource("ErpMainButtonSelectedStyle");
            var subNormal = (Style)FindResource("ErpSubButtonStyle"); var subSelected = (Style)FindResource("ErpSubButtonSelectedStyle");
            var expNormal = (Style)FindResource("SidebarExpanderStyle"); var expActive = (Style)FindResource("SidebarExpanderActiveStyle");

            BtnNavPortal.Style = mainNormal; BtnNavReport.Style = subNormal; BtnNavHandover.Style = subNormal; BtnNavProdReq.Style = subNormal;
            BtnNavTeamSchedule.Style = subNormal; BtnNavSchedule.Style = subNormal; BtnNavWeeklyReport.Style = subNormal; BtnNavPersonalTask.Style = subNormal; BtnNavDispatchCert.Style = subNormal;
            BtnNavPersonalMemo.Style = subNormal; BtnNavFieldChecklist.Style = subNormal;

            ExpanderAttendance.Style = expNormal; ExpanderProduction.Style = expNormal; ExpanderOffice.Style = expNormal; ExpanderEtc.Style = expNormal;
            ExpanderFieldInspection.Style = expNormal;

            switch (viewName)
            {
                case "Portal": BtnNavPortal.Style = mainSelected; break;
                case "Report": BtnNavReport.Style = subSelected; ExpanderEtc.Style = expActive; if (_isSidebarOpen) ExpanderEtc.IsExpanded = true; break;
                case "Handover": BtnNavHandover.Style = subSelected; ExpanderProduction.Style = expActive; if (_isSidebarOpen) ExpanderProduction.IsExpanded = true; break;
                case "ProdReq": BtnNavProdReq.Style = subSelected; ExpanderProduction.Style = expActive; if (_isSidebarOpen) ExpanderProduction.IsExpanded = true; break;
                case "Schedule": BtnNavSchedule.Style = subSelected; ExpanderProduction.Style = expActive; if (_isSidebarOpen) ExpanderProduction.IsExpanded = true; break;
                case "TeamSchedule": BtnNavTeamSchedule.Style = subSelected; ExpanderAttendance.Style = expActive; if (_isSidebarOpen) ExpanderAttendance.IsExpanded = true; break;
                case "WeeklyReport": BtnNavWeeklyReport.Style = subSelected; ExpanderOffice.Style = expActive; if (_isSidebarOpen) ExpanderOffice.IsExpanded = true; break;
                case "PersonalTask": BtnNavPersonalTask.Style = subSelected; ExpanderProduction.Style = expActive; if (_isSidebarOpen) ExpanderProduction.IsExpanded = true; break;
                case "DispatchCert": BtnNavDispatchCert.Style = subSelected; ExpanderEtc.Style = expActive; if (_isSidebarOpen) ExpanderEtc.IsExpanded = true; break;
                case "PersonalMemo": BtnNavPersonalMemo.Style = subSelected; ExpanderAttendance.Style = expActive; if (_isSidebarOpen) ExpanderAttendance.IsExpanded = true; break;
                case "FieldChecklist": BtnNavFieldChecklist.Style = subSelected; ExpanderFieldInspection.Style = expActive; if (_isSidebarOpen) ExpanderFieldInspection.IsExpanded = true; break;
            }

            _isUpdatingNav = false;
        }
    }
}