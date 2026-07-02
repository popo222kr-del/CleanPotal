using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;

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
        public bool CanManageSchedule { get; set; } = false;
        public bool CanManageBroken { get; set; } = false;
        public bool CanAccessEtcMenu { get; set; } = false;
        public bool CanManageShiftBoard { get; set; } = false;
        public bool CanManageInventory { get; set; } = false;
        public bool IsResigned { get; set; } = false;
        public string ResignDate { get; set; } = "";
        public string HireDate { get; set; } = "";
        // 아이디(Username)와 분리된 사번 — 기존 사용자는 Username과 동일하게 자동 초기화됨
        public string EmployeeNumber { get; set; } = "";
    }

    /// <summary>
    /// 사용자 계정(로그인) 저장소. users.json → SQLite(dispatch.db) Users 테이블로 정규화 이관.
    /// ⚠️ 로그인 핵심 경로 — 이관 실패/빈 테이블이어도 반드시 기본 관리자(1004)를 시드해 잠기지 않게 한다.
    /// 연결은 DatabaseHelper.GetConnection()(journal_mode=DELETE)만 사용.
    /// </summary>
    public static class AuthDatabaseHelper
    {
        private static string UsersFilePath => Path.Combine(AppPaths.DataRoot, "users.json");

        // 앱 시작 시 1회: 테이블 생성 + users.json 이관 + (비어있으면) 기본 관리자 시드.
        public static void InitializeDatabase(IDbConnection? shared = null)
        {
            if (!Directory.Exists(AppPaths.DataRoot))
                Directory.CreateDirectory(AppPaths.DataRoot);

            var db = shared ?? DatabaseHelper.GetConnection();
            try
            {
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS Users (
                        Username            TEXT PRIMARY KEY,
                        Password            TEXT NOT NULL DEFAULT '',
                        RealName            TEXT NOT NULL DEFAULT '',
                        TeamName            TEXT NOT NULL DEFAULT '',
                        JobTitle            TEXT NOT NULL DEFAULT '',
                        Email               TEXT NOT NULL DEFAULT '',
                        PhoneNumber         TEXT NOT NULL DEFAULT '',
                        CanManageFiles      INTEGER NOT NULL DEFAULT 0,
                        CanManageNotices    INTEGER NOT NULL DEFAULT 0,
                        CanManageVendors    INTEGER NOT NULL DEFAULT 0,
                        CanManageSchedule   INTEGER NOT NULL DEFAULT 0,
                        CanManageBroken     INTEGER NOT NULL DEFAULT 0,
                        CanAccessEtcMenu    INTEGER NOT NULL DEFAULT 0,
                        CanManageShiftBoard INTEGER NOT NULL DEFAULT 0,
                        CanManageInventory  INTEGER NOT NULL DEFAULT 0,
                        IsResigned          INTEGER NOT NULL DEFAULT 0,
                        ResignDate          TEXT NOT NULL DEFAULT '',
                        HireDate            TEXT NOT NULL DEFAULT '',
                        EmployeeNumber      TEXT NOT NULL DEFAULT '',
                        OrderNo             INTEGER NOT NULL DEFAULT 0
                    );");

                // 🚧 배포 스위치(플래그)가 켜졌고 users.json 이 있으면 그 내용으로 '덮어쓰기' 이관.
                //    (JSON 존재 여부로 판단 → 배포 시점 최신 계정이 반영됨. 이관 후 .migrated 로 보존)
                if (AppPaths.DbMigrationEnabled && File.Exists(UsersFilePath))
                {
                    try
                    {
                        var users = JsonSerializer.Deserialize<List<UserModel>>(File.ReadAllText(UsersFilePath))
                                    ?? new List<UserModel>();
                        // ⚠️ 유효 계정이 하나라도 있을 때만 덮어쓰기 — 빈/깨진 JSON 으로 전체 계정이 날아가는 사고 방지
                        var valid = users.Where(u => u != null && !string.IsNullOrWhiteSpace(u.Username)).ToList();
                        if (valid.Count > 0)
                        {
                            db.Execute("DELETE FROM Users");
                            int order = 0;
                            foreach (var u in valid) Upsert(db, u, order++, null);
                            try
                            {
                                string bak = UsersFilePath + ".migrated";
                                if (File.Exists(bak)) File.Delete(bak);
                                File.Move(UsersFilePath, bak);
                            }
                            catch { }
                        }
                    }
                    catch { /* 이관 실패해도 아래 시드로 로그인은 보장 */ }
                }

                // 비어 있으면 기본 관리자(1004) 시드 — 게이트와 무관하게 로그인 잠김 방지
                if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM Users") == 0)
                {
                    Upsert(db, new UserModel
                    {
                        Username = "1004", Password = "1", RealName = "박주언", TeamName = "관리자", JobTitle = "최고관리자",
                        CanManageFiles = true, CanManageNotices = true, CanManageVendors = true,
                        CanManageSchedule = true, CanManageShiftBoard = true, CanManageInventory = true
                    }, 0, null);
                }

                // 사번이 비어 있는 사용자는 아이디와 동일하게 보정(기존 로직 계승)
                db.Execute("UPDATE Users SET EmployeeNumber = Username WHERE EmployeeNumber IS NULL OR EmployeeNumber = ''");
            }
            finally
            {
                if (shared == null) db.Dispose();
            }
        }

        private static void Upsert(IDbConnection db, UserModel u, int order, IDbTransaction? tx)
        {
            db.Execute(@"
                INSERT OR REPLACE INTO Users
                    (Username, Password, RealName, TeamName, JobTitle, Email, PhoneNumber,
                     CanManageFiles, CanManageNotices, CanManageVendors, CanManageSchedule, CanManageBroken,
                     CanAccessEtcMenu, CanManageShiftBoard, CanManageInventory,
                     IsResigned, ResignDate, HireDate, EmployeeNumber, OrderNo)
                VALUES
                    (@Username, @Password, @RealName, @TeamName, @JobTitle, @Email, @PhoneNumber,
                     @CanManageFiles, @CanManageNotices, @CanManageVendors, @CanManageSchedule, @CanManageBroken,
                     @CanAccessEtcMenu, @CanManageShiftBoard, @CanManageInventory,
                     @IsResigned, @ResignDate, @HireDate, @EmployeeNumber, @OrderNo)",
                new
                {
                    Username = u.Username ?? "",
                    Password = u.Password ?? "",
                    RealName = u.RealName ?? "",
                    TeamName = u.TeamName ?? "",
                    JobTitle = u.JobTitle ?? "",
                    Email = u.Email ?? "",
                    PhoneNumber = u.PhoneNumber ?? "",
                    CanManageFiles = u.CanManageFiles ? 1 : 0,
                    CanManageNotices = u.CanManageNotices ? 1 : 0,
                    CanManageVendors = u.CanManageVendors ? 1 : 0,
                    CanManageSchedule = u.CanManageSchedule ? 1 : 0,
                    CanManageBroken = u.CanManageBroken ? 1 : 0,
                    CanAccessEtcMenu = u.CanAccessEtcMenu ? 1 : 0,
                    CanManageShiftBoard = u.CanManageShiftBoard ? 1 : 0,
                    CanManageInventory = u.CanManageInventory ? 1 : 0,
                    IsResigned = u.IsResigned ? 1 : 0,
                    ResignDate = u.ResignDate ?? "",
                    HireDate = u.HireDate ?? "",
                    EmployeeNumber = u.EmployeeNumber ?? "",
                    OrderNo = order
                }, tx);
        }

        // ⚠️ Dapper의 INTEGER→bool 자동 변환은 드라이버/버전에 따라 캐스트 예외가 날 수 있어
        //    (로그인 경로) 명시적 매핑으로 안전하게 읽는다.
        private static UserModel MapUser(IDictionary<string, object> d)
        {
            string S(string k) => d.TryGetValue(k, out var v) && v != null ? v.ToString() ?? "" : "";
            bool B(string k) => d.TryGetValue(k, out var v) && v != null && Convert.ToInt64(v) != 0;
            return new UserModel
            {
                Username = S("Username"), Password = S("Password"), RealName = S("RealName"),
                TeamName = S("TeamName"), JobTitle = S("JobTitle"), Email = S("Email"), PhoneNumber = S("PhoneNumber"),
                CanManageFiles = B("CanManageFiles"), CanManageNotices = B("CanManageNotices"),
                CanManageVendors = B("CanManageVendors"), CanManageSchedule = B("CanManageSchedule"),
                CanManageBroken = B("CanManageBroken"), CanAccessEtcMenu = B("CanAccessEtcMenu"),
                CanManageShiftBoard = B("CanManageShiftBoard"), CanManageInventory = B("CanManageInventory"),
                IsResigned = B("IsResigned"), ResignDate = S("ResignDate"), HireDate = S("HireDate"),
                EmployeeNumber = S("EmployeeNumber")
            };
        }

        public static UserModel? ValidateUserObject(string username, string password)
        {
            try
            {
                using var db = DatabaseHelper.GetConnection();
                var row = db.Query("SELECT * FROM Users WHERE Username = @u AND Password = @p",
                    new { u = username, p = password }).FirstOrDefault();
                return row == null ? null : MapUser((IDictionary<string, object>)row);
            }
            catch { return null; }
        }

        public static List<UserModel> GetAllUsers()
        {
            try
            {
                using var db = DatabaseHelper.GetConnection();
                return db.Query("SELECT * FROM Users ORDER BY OrderNo, rowid")
                         .Select(r => MapUser((IDictionary<string, object>)r))
                         .ToList();
            }
            catch { return new List<UserModel>(); }
        }

        public static void SaveAllUsers(List<UserModel> users)
        {
            using var db = DatabaseHelper.GetConnection();
            using var tx = db.BeginTransaction();
            db.Execute("DELETE FROM Users", transaction: tx);
            int order = 0;
            foreach (var u in users)
            {
                if (u == null || string.IsNullOrWhiteSpace(u.Username)) continue;
                Upsert(db, u, order++, tx);
            }
            tx.Commit();
        }
    }
}
