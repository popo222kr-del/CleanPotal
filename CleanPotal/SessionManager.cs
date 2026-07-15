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

            bool hasPermission = type switch
            {
                PermissionType.Files => SessionManager.CanManageFiles,
                PermissionType.Notices => SessionManager.CanManageNotices,
                PermissionType.Vendors => SessionManager.CanManageVendors,
                PermissionType.Schedule => true,
                PermissionType.WeeklyReport => SessionManager.CurrentTeamName.ToUpper().Contains("OFFICE") || SessionManager.CurrentTeamName == "관리자" || SessionManager.IsExecutive,
                PermissionType.EtcMenu => SessionManager.CanAccessEtcMenu || SessionManager.IsMasterAdmin,
                PermissionType.BrokenMgmt => SessionManager.CanManageBroken || SessionManager.IsMasterAdmin || SessionManager.IsExecutive,
                PermissionType.ShiftBoard => SessionManager.CanManageShiftBoard || SessionManager.IsMasterAdmin,
                PermissionType.InventoryManage => SessionManager.CanManageInventory || SessionManager.IsMasterAdmin,
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

        // 최고 관리자 판별(단일 소스). 추가 관리자 계정은 여기서 관리.
        private static readonly string[] _masterAdminIds = { "AETS" };
        public static bool IsMasterAdmin
            => Array.Exists(_masterAdminIds, id => string.Equals(id, CurrentUsername, StringComparison.OrdinalIgnoreCase));

        // 임원 직위 판별(직위명에 아래 키워드 포함 시 임원). OFFICE 업무 열람 허용 기준(단일 소스).
        private static readonly string[] _execTitles =
            { "임원", "회장", "부회장", "대표", "사장", "부사장", "전무", "상무", "이사" };
        public static bool IsExecutive
        {
            get
            {
                string t = (CurrentJobTitle ?? "").Replace(" ", "");
                return t.Length > 0 && System.Array.Exists(_execTitles, k => t.Contains(k));
            }
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