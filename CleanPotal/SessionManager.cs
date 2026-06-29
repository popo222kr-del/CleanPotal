using System;
using System.IO;
using System.Text;
using System.Windows;

namespace CleanPotal
{
    public enum PermissionType { Files, Notices, Vendors, Schedule, WeeklyReport, EtcMenu, BrokenMgmt, ShiftBoard, InventoryManage }

    public static class AuthManager
    {
        public static bool CheckAuth(PermissionType type)
        {
            if (!SessionManager.IsLoggedIn)
            {
                MessageBox.Show("로그인이 필요합니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // gest 계정은 기타 메뉴 외 모든 편집 권한 차단
            if (SessionManager.IsGuest && type != PermissionType.EtcMenu)
            {
                MessageBox.Show("열람 전용 계정입니다.\n수정/등록 작업은 '기타' 메뉴에서만 가능합니다.",
                    "열람 전용", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            bool hasPermission = type switch
            {
                PermissionType.Files => SessionManager.CanManageFiles,
                PermissionType.Notices => SessionManager.CanManageNotices,
                PermissionType.Vendors => SessionManager.CanManageVendors,
                PermissionType.Schedule => true,
                PermissionType.WeeklyReport => SessionManager.CurrentTeamName.ToUpper().Contains("OFFICE") || SessionManager.CurrentTeamName == "관리자",
                PermissionType.EtcMenu => SessionManager.CanAccessEtcMenu || SessionManager.CurrentUsername == "1004",
                PermissionType.BrokenMgmt => SessionManager.CanManageBroken || SessionManager.CurrentUsername == "1004",
                PermissionType.ShiftBoard => SessionManager.CanManageShiftBoard || SessionManager.CurrentUsername == "1004",
                PermissionType.InventoryManage => SessionManager.CanManageInventory || SessionManager.CurrentUsername == "1004",
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
                    PermissionType.WeeklyReport => "주간보고",
                    PermissionType.EtcMenu => "기타 메뉴",
                    PermissionType.BrokenMgmt => "BROKEN 관리",
                    PermissionType.ShiftBoard => "생산근무표",
                    PermissionType.InventoryManage => "재고 관리",
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
        public static string CurrentJobTitle  { get; set; } = "";
        public static string CurrentPhoneNumber { get; set; } = "";
        public static bool CanManageFiles { get; set; } = false;
        public static bool CanManageNotices { get; set; } = false;
        public static bool CanManageVendors { get; set; } = false;
        public static bool CanManageSchedule { get; set; } = false;
        public static bool CanManageBroken { get; set; } = false;
        public static bool CanAccessEtcMenu { get; set; } = false;
        public static bool CanManageShiftBoard { get; set; } = false;
        public static bool CanManageInventory { get; set; } = false;

        public static bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUsername);

        // gest 계정: 화면 열람만 가능, 수정/등록/삭제 불가 (단, 기타 메뉴는 권한 부여 시 사용 가능)
        public static bool IsGuest => string.Equals(CurrentUsername, "gest", StringComparison.OrdinalIgnoreCase);

        // 기타 메뉴 영역 안에 있을 때만 true (MainWindow가 네비게이션 시 설정). 기타 메뉴는 gest도 편집 허용.
        public static bool InEtcSection { get; set; } = false;

        // gest가 기타 메뉴 밖에서 데이터를 쓰려 할 때 true (데이터 계층 차단용, 메시지 없음)
        public static bool GuestWriteBlocked => IsGuest && !InEtcSection;

        // 편집/등록/삭제 시도 시 호출. gest가 기타 메뉴 밖에서 시도하면 막고 true 반환.
        public static bool BlockGuestEdit()
        {
            if (IsGuest && !InEtcSection)
            {
                MessageBox.Show("열람 전용 계정입니다.\n수정/등록 작업은 '기타' 메뉴에서만 가능합니다.",
                    "열람 전용", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            return false;
        }

        private static readonly string TokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CleanPotal", "auth_v2.dat");

        public static void Logout()
        {
            CurrentUsername = ""; CurrentRealName = ""; CurrentTeamName = "";
            CurrentJobTitle = ""; CurrentPhoneNumber = "";
            CanManageFiles = false; CanManageNotices = false; CanManageVendors = false; CanManageSchedule = false; CanManageBroken = false; CanAccessEtcMenu = false; CanManageShiftBoard = false; CanManageInventory = false;

            if (File.Exists(TokenPath)) File.Delete(TokenPath);
        }

        public static void ClearSavedLogin()
        {
            if (File.Exists(TokenPath)) File.Delete(TokenPath);
        }

        public static void SaveAutoLogin(string id, string pw)
        {
            try
            {
                string? dir = Path.GetDirectoryName(TokenPath);
                if (dir != null) Directory.CreateDirectory(dir);

                string data = $"{id}|{pw}";
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
                File.WriteAllText(TokenPath, encoded);
            }
            catch { }
        }

        public static (string? id, string? pw) LoadAutoLogin()
        {
            if (!File.Exists(TokenPath)) return (null, null);
            try
            {
                string encoded = File.ReadAllText(TokenPath);
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                string[] parts = decoded.Split('|');
                if (parts.Length == 2) return (parts[0], parts[1]);
            }
            catch { }
            return (null, null);
        }
    }
}