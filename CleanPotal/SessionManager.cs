using System;
using System.IO;
using System.Text;
using System.Windows;

namespace CleanPotal
{
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
                    PermissionType.WeeklyReport => "주간보고",
                    _ => "해당"
                };
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

        private static readonly string TokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CleanPotal", "auth_v2.dat");

        public static void Logout()
        {
            CurrentUsername = ""; CurrentRealName = ""; CurrentTeamName = "";
            CanManageFiles = false; CanManageNotices = false; CanManageVendors = false; CanManageSchedule = false;

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