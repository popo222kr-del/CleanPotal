using System;
using System.Windows;

namespace CleanPotal
{
    public partial class MainWindow : Window
    {
        private string _currentViewName = "Portal";
        private HandoverView? _handoverView;
        private ScheduleBoardView? _scheduleBoardView;

        public MainWindow()
        {
            InitializeComponent();
            WindowState = WindowState.Maximized;

            // 오늘 날짜 표시 (실제 현재 날짜 기준)
            TodayBadgeText.Text = DateTime.Now.ToString("yyyy-MM-dd dddd");

            // 🔥 프로그램 시작 시 DB 파일 및 테이블 자동 생성
            DatabaseHelper.InitializeDatabase();

            // 프로그램 시작 시 포털 화면 로드
            this.Loaded += (s, e) => ShowPortal();
        }

        // --- 사이드바 메뉴 클릭 이벤트 ---
        private void OpenPortal(object sender, RoutedEventArgs e) => ShowPortal();
        private void OpenHandover(object sender, RoutedEventArgs e) => ShowHandover();
        private void OpenSchedule(object sender, RoutedEventArgs e) => ShowSchedule();

        // 🔥 원본 권한 체크 (유지)
        private bool CheckAdminAuth()
        {
            var dialog = new PasswordDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                if (dialog.InputPassword == "admin1234") return true;
                MessageBox.Show("비밀번호가 일치하지 않습니다.", "권한 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        // 🔥 업체 관리 버튼 클릭 시 (유지)
        private void OpenVendorManager_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckAdminAuth()) return;

            UpdateNavSelection("Vendor");
            var vendorWin = new VendorManagerWindow { Owner = this };
            vendorWin.ShowDialog();
            UpdateNavSelection(_currentViewName); // 창 닫히면 이전 메뉴로 원복
        }

        private void ApplySectionMeta(string title, string description, string primaryButtonText)
        {
            CurrentSectionTitleText.Text = title;
            CurrentSectionDescriptionText.Text = description;
            CurrentSectionDescriptionText.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
            BtnCommandPrimary.Content = primaryButtonText;
        }

        // 🔥 기존 기능 초기화 (유지)
        private void ClearAllButtonHandlers()
        {
            BtnCommandPrimary.Click -= ManagePortalLinks_Click;
            BtnCommandPrimary.Click -= OpenRegisterModal_Click;
            BtnCommandSecondary.Click -= OpenDoneModal_Click;
            BtnCommandNotice.Click -= OpenNoticeModal_Click;
            BtnCommandProdReq.Click -= OpenProdReq_Click;
        }

        // --- 각 화면 로드 로직 ---
        private void ShowPortal()
        {
            _currentViewName = "Portal";
            _handoverView = null;
            _scheduleBoardView = null;
            MainContent.Content = new PortalView();

            ApplySectionMeta("업무 파일 통합 관리", "자주 사용하는 파일과 폴더를 빠르게 실행합니다.", "파일 관리자");

            ClearAllButtonHandlers();
            BtnCommandPrimary.Click += ManagePortalLinks_Click;

            BtnCommandPrimary.Visibility = Visibility.Visible;
            BtnCommandSecondary.Visibility = Visibility.Collapsed;
            BtnCommandNotice.Visibility = Visibility.Collapsed;
            BtnCommandProdReq.Visibility = Visibility.Collapsed;

            BtnCommandRecipeManage.Visibility = Visibility.Collapsed;
            BtnCommandCapture.Visibility = Visibility.Collapsed;
            BtnCommandUndo.Visibility = Visibility.Collapsed;
            BtnCommandPartialReset.Visibility = Visibility.Collapsed;
            BtnCommandReset.Visibility = Visibility.Collapsed;

            UpdateNavSelection("Portal");
        }

        private void ManagePortalLinks_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckAdminAuth()) return;
            var managerWindow = new PortalManagerWindow { Owner = this };
            managerWindow.ShowDialog();
            if (_currentViewName == "Portal") MainContent.Content = new PortalView();
        }

        private void ShowHandover()
        {
            _currentViewName = "Handover";
            _scheduleBoardView = null;
            _handoverView = new HandoverView();
            MainContent.Content = _handoverView;

            ApplySectionMeta("현장 업무 인수인계", "업체별 진행 상황을 기록하고 배차를 관리합니다.", "새 항목 등록");

            ClearAllButtonHandlers();
            BtnCommandPrimary.Click += OpenRegisterModal_Click;
            BtnCommandPrimary.Visibility = Visibility.Visible;

            BtnCommandSecondary.Content = "완료 목록 조회";
            BtnCommandSecondary.Click += OpenDoneModal_Click;
            BtnCommandSecondary.Visibility = Visibility.Visible;

            BtnCommandNotice.Content = "공지 관리";
            BtnCommandNotice.Click += OpenNoticeModal_Click;
            BtnCommandNotice.Visibility = Visibility.Visible;

            BtnCommandProdReq.Visibility = Visibility.Visible;
            BtnCommandProdReq.Click += OpenProdReq_Click;

            BtnCommandRecipeManage.Visibility = Visibility.Collapsed;
            BtnCommandCapture.Visibility = Visibility.Collapsed;
            BtnCommandUndo.Visibility = Visibility.Collapsed;
            BtnCommandPartialReset.Visibility = Visibility.Collapsed;
            BtnCommandReset.Visibility = Visibility.Collapsed;

            UpdateNavSelection("Handover");
        }

        private void ShowSchedule()
        {
            _currentViewName = "Schedule";
            _handoverView = null;
            _scheduleBoardView = new ScheduleBoardView();
            MainContent.Content = _scheduleBoardView;

            ApplySectionMeta("스케줄보드", "생산 라인별 스케줄 및 레시피를 관리합니다.", "");

            ClearAllButtonHandlers();
            BtnCommandPrimary.Visibility = Visibility.Collapsed;
            BtnCommandSecondary.Visibility = Visibility.Collapsed;
            BtnCommandNotice.Visibility = Visibility.Collapsed;
            BtnCommandProdReq.Visibility = Visibility.Collapsed;

            BtnCommandRecipeManage.Visibility = Visibility.Visible;
            BtnCommandCapture.Visibility = Visibility.Visible;
            BtnCommandUndo.Visibility = Visibility.Visible;
            BtnCommandPartialReset.Visibility = Visibility.Visible;
            BtnCommandReset.Visibility = Visibility.Visible;

            UpdateNavSelection("Schedule");
        }

        private void OpenRegisterModal_Click(object sender, RoutedEventArgs e) => _handoverView?.OpenRegisterModal();
        private void OpenDoneModal_Click(object sender, RoutedEventArgs e) => _handoverView?.OpenDoneModal();
        private void OpenNoticeModal_Click(object sender, RoutedEventArgs e) => _handoverView?.OpenNoticeModal();
        private void OpenProdReq_Click(object sender, RoutedEventArgs e) { MessageBox.Show("생산팀 요청사항 기능은 추후 업데이트 예정입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information); }

        private void BtnCommandRecipeManage_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.OpenRecipeManager();
        private void BtnCommandCapture_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.CaptureBoard();
        private void BtnCommandUndo_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.UndoAction();
        private void BtnCommandPartialReset_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.PartialReset();
        private void BtnCommandReset_Click(object sender, RoutedEventArgs e) => _scheduleBoardView?.ResetAll();

        // 🔥 서브 메뉴 스타일 연동
        private void UpdateNavSelection(string viewName)
        {
            var normal = (Style)FindResource("SidebarButtonStyle");
            var selected = (Style)FindResource("SidebarButtonSelectedStyle");
            var subNormal = (Style)FindResource("SidebarSubButtonStyle");
            var subSelected = (Style)FindResource("SidebarSubButtonSelectedStyle");

            BtnNavPortal.Style = normal;
            BtnNavHandover.Style = normal;
            BtnNavSchedule.Style = normal;
            BtnNavVendor.Style = subNormal;

            switch (viewName)
            {
                case "Portal": BtnNavPortal.Style = selected; break;
                case "Handover": BtnNavHandover.Style = selected; break;
                case "Schedule": BtnNavSchedule.Style = selected; break;
                case "Vendor":
                    BtnNavHandover.Style = selected; // 부모 메뉴도 활성화 유지
                    BtnNavVendor.Style = subSelected; // 자식 메뉴 활성화
                    break;
            }
        }
    }
}