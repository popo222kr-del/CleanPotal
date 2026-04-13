using System.Windows;

namespace CleanPotal
{
    public enum PermissionType { Files, Notices, Vendors, Schedule }

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
                // 🔥 한시적 전면 개방: 일정 메뉴는 권한 여부와 상관없이 무조건 진입 허용
                PermissionType.Schedule => true, // 원래 코드: SessionManager.CanManageSchedule,
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
                    _ => "해당"
                };
                MessageBox.Show($"{menuName} 메뉴에 접근할 권한이 없습니다.", "접근 제한", MessageBoxButton.OK, MessageBoxImage.Stop);
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