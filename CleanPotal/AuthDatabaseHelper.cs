using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanPotal
{
    public class UserModel
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string RealName { get; set; } = "";
        public string TeamName { get; set; } = "";

        [JsonIgnore]
        public string InitialChar => string.IsNullOrEmpty(RealName) ? "?" : RealName.Substring(0, 1);

        public string JobTitle { get; set; } = "";
        public string Email { get; set; } = "";
        public string PhoneNumber { get; set; } = "";

        public bool CanManageFiles { get; set; } = false;
        public bool CanManageNotices { get; set; } = false;
        public bool CanManageVendors { get; set; } = false;
        // 🔥 신규: 교육/일정 관리 권한
        public bool CanManageSchedule { get; set; } = false;
        public string HireDate { get; set; } = "";
    }

    public static class AuthDatabaseHelper
    {
        private static string UsersFilePath => Path.Combine(AppPaths.DataRoot, "users.json");

        public static void InitializeDatabase()
        {
            if (!Directory.Exists(AppPaths.DataRoot))
                Directory.CreateDirectory(AppPaths.DataRoot);

            if (!File.Exists(UsersFilePath))
            {
                var defaultUsers = new List<UserModel>
                {
                    // 🔥 admin을 삭제하고 '1004' 박주언 님을 최고관리자로 지정 (초기 비번 1로 설정, 추후 변경 가능)
                    new UserModel {
                        Username = "1004", Password = "1", RealName = "박주언", TeamName = "관리자", JobTitle = "최고관리자",
                        CanManageFiles = true, CanManageNotices = true, CanManageVendors = true, CanManageSchedule = true
                    }
                };

                string json = JsonSerializer.Serialize(defaultUsers, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(UsersFilePath, json);
            }
        }

        public static UserModel? ValidateUserObject(string username, string password)
        {
            if (!File.Exists(UsersFilePath)) InitializeDatabase();
            try
            {
                string json = File.ReadAllText(UsersFilePath);
                var users = JsonSerializer.Deserialize<List<UserModel>>(json);
                return users?.FirstOrDefault(u => u.Username == username && u.Password == password);
            }
            catch { }
            return null;
        }

        public static List<UserModel> GetAllUsers()
        {
            if (!File.Exists(UsersFilePath)) InitializeDatabase();
            try
            {
                string json = File.ReadAllText(UsersFilePath);
                return JsonSerializer.Deserialize<List<UserModel>>(json) ?? new List<UserModel>();
            }
            catch { return new List<UserModel>(); }
        }

        public static void SaveAllUsers(List<UserModel> users)
        {
            string json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UsersFilePath, json);
        }
    }
}