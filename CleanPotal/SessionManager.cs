using System.Windows;

namespace CleanPotal
{
    // 🔥 WeeklyReport 권한 타입 추가
    public enum PermissionType { Files, Notices, Vendors, Schedule, WeeklyReport }

    public static class AuthManager
    {
        public static bool CheckAuth(PermissionType type)
        {
            if (!SessionManager.IsLoggedIn)
            {
                MessageBox.Show("로그인이 필요합니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            bool hasPermission = type switch
            {
                PermissionType.Files => SessionManager.CanManageFiles,
                PermissionType.Notices => SessionManager.CanManageNotices,
                PermissionType.Vendors => SessionManager.CanManageVendors,
                PermissionType.Schedule => true,
                // 🔥 주간보고는 부서명에 "Office" 또는 "OFFICE"가 포함되거나 "관리자"인 경우만 허용
                PermissionType.WeeklyReport => SessionManager.CurrentTeamName.ToUpper().Contains("OFFICE") || SessionManager.CurrentTeamName == "관리자",
                _ => false
            };

            if (!hasPermission)
            {
                string menuName = type switch
                {
                    PermissionType.Files => "파일 관리자",
                    PermissionType.Notices => "공지사항 관리",
                    PermissionType.Vendors => "업체 관리",
                    PermissionType.Schedule => "일정/교육 관리",
                    PermissionType.WeeklyReport => "주간보고", // 🔥 권한 실패 시 팝업에 표시될 이름
                    _ => "해당"
                };

                // 🔥 권한이 없을 때 경고창 표시
                MessageBox.Show($"{menuName} 메뉴에 접근할 권한이 없습니다.\n(OFFICE 소속 인원만 사용 가능합니다.)", "접근 제한", MessageBoxButton.OK, MessageBoxImage.Stop);
                return false;
            }
            return true;
        }
    }

    public static class SessionManager
    {
        public static string CurrentUsername { get; set; } = "";
        public static string CurrentRealName { get; set; } = "";
        public static string CurrentTeamName { get; set; } = "";

        public static bool CanManageFiles { get; set; } = false;
        public static bool CanManageNotices { get; set; } = false;
        public static bool CanManageVendors { get; set; } = false;
        public static bool CanManageSchedule { get; set; } = false;

        public static bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUsername);

        public static void Logout()
        {
            CurrentUsername = ""; CurrentRealName = ""; CurrentTeamName = "";
            CanManageFiles = false; CanManageNotices = false; CanManageVendors = false; CanManageSchedule = false;
        }
    }
}