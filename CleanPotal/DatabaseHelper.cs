using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;

namespace CleanPotal
{
    public class SqliteGuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        public override void SetValue(IDbDataParameter parameter, Guid value) => parameter.Value = value.ToString();
        public override Guid Parse(object value) => Guid.Parse((string)value);
    }

    public static class DatabaseHelper
    {
        private static readonly string DbPath = Path.Combine(AppPaths.DataRoot, "dispatch.db");
        private static readonly string ConnectionString = $"Data Source={DbPath}";
        private static bool _isMapperInitialized = false;

        public static void InitializeDatabase()
        {
            if (!Directory.Exists(AppPaths.DataRoot)) Directory.CreateDirectory(AppPaths.DataRoot);

            if (!_isMapperInitialized)
            {
                SqlMapper.AddTypeHandler(new SqliteGuidTypeHandler());
                _isMapperInitialized = true;
            }

            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                string createDispatchTableSql = @"
                    CREATE TABLE IF NOT EXISTS DispatchList (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        VendorName TEXT NOT NULL,
                        OutgoingDetails TEXT,
                        IncomingDetails TEXT,
                        ManagerName TEXT,
                        ContactNumber TEXT,
                        FullAddress TEXT,
                        Note TEXT,
                        CreateDate DATETIME DEFAULT CURRENT_TIMESTAMP
                    );";
                connection.Execute(createDispatchTableSql);

                string createHandoverTableSql = @"
                    CREATE TABLE IF NOT EXISTS HandoverList (
                        Id TEXT PRIMARY KEY, Vendor TEXT, Owner TEXT, Content TEXT,
                        InDate DATETIME, OutDate DATETIME, Status TEXT, Memo TEXT, CreateDate DATETIME DEFAULT CURRENT_TIMESTAMP
                    );";
                connection.Execute(createHandoverTableSql);

                try { connection.Execute("ALTER TABLE HandoverList ADD COLUMN CreatorName TEXT;"); } catch { }
                try { connection.Execute("ALTER TABLE HandoverList ADD COLUMN ModifierName TEXT;"); } catch { }
                try { connection.Execute("ALTER TABLE HandoverList ADD COLUMN ModifyDate DATETIME;"); } catch { }
                try { connection.Execute("ALTER TABLE HandoverList ADD COLUMN ReadBy TEXT;"); } catch { }
            }

            InitializeScheduleTables();

            // 🔥 앱 실행 시 생산팀 요청사항 테이블 자동 생성 호출!
            CreateProdReqTable();

            // 🔥 현장 점검(NFC/QR 체크시트) 테이블 자동 생성
            FieldInspection.Repositories.FieldInspectionRepository.InitializeTables();
        }

        public static IDbConnection GetConnection() => new SqliteConnection(ConnectionString);

        public static int InsertDispatch(DispatchItemModel item, DateTime targetDate)
        {
            using (var db = GetConnection())
            {
                string sql = @"INSERT INTO DispatchList (VendorName, OutgoingDetails, IncomingDetails, ManagerName, ContactNumber, FullAddress, Note, CreateDate) 
                               VALUES (@VendorName, @OutgoingDetails, @IncomingDetails, @ManagerName, @ContactNumber, @FullAddress, @Note, @CreateDate);
                               SELECT last_insert_rowid();";
                long id = db.ExecuteScalar<long>(sql, new
                {
                    item.VendorName,
                    item.OutgoingDetails,
                    item.IncomingDetails,
                    ManagerName = item.ManagerName,
                    item.ContactNumber,
                    FullAddress = item.FullAddress,
                    item.Note,
                    CreateDate = targetDate.ToString("yyyy-MM-dd") + " 00:00:00"
                });
                return (int)id;
            }
        }

        public static void UpdateDispatch(DispatchItemModel item, DateTime targetDate)
        {
            using (var db = GetConnection())
            {
                string sql = @"UPDATE DispatchList SET 
                               VendorName = @VendorName, OutgoingDetails = @OutgoingDetails, IncomingDetails = @IncomingDetails, 
                               ManagerName = @ManagerName, ContactNumber = @ContactNumber, FullAddress = @FullAddress, Note = @Note, 
                               CreateDate = @CreateDate WHERE Id = @Id";
                db.Execute(sql, new
                {
                    item.VendorName,
                    item.OutgoingDetails,
                    item.IncomingDetails,
                    ManagerName = item.ManagerName,
                    item.ContactNumber,
                    FullAddress = item.FullAddress,
                    item.Note,
                    CreateDate = targetDate.ToString("yyyy-MM-dd") + " 00:00:00",
                    item.Id
                });
            }
        }

        public static void DeleteDispatch(int id)
        {
            using (var db = GetConnection())
            {
                db.Execute("DELETE FROM DispatchList WHERE Id = @Id", new { Id = id });
            }
        }

        public static List<DispatchItemModel> GetDispatchModelsByDate(DateTime targetDate)
        {
            using (var db = GetConnection())
            {
                string sql = "SELECT * FROM DispatchList WHERE date(CreateDate) = date(@TargetDate) ORDER BY Id ASC";
                var rawList = db.Query(sql, new { TargetDate = targetDate.ToString("yyyy-MM-dd") }).ToList();
                var result = new List<DispatchItemModel>();

                foreach (var row in rawList)
                {
                    if (row.Id == null) continue;
                    if (!int.TryParse(row.Id.ToString(), out int safeId)) continue;

                    var model = new DispatchItemModel
                    {
                        Id = safeId,
                        VendorName = (string?)row.VendorName ?? string.Empty,
                        OutgoingDetails = (string?)row.OutgoingDetails ?? "-",
                        IncomingDetails = (string?)row.IncomingDetails ?? string.Empty,
                        Note = (string?)row.Note ?? string.Empty,
                        ManagerName = (string?)row.ManagerName ?? string.Empty,
                        FullAddress = (string?)row.FullAddress ?? string.Empty,
                        ContactNumber = (string?)row.ContactNumber ?? string.Empty
                    };
                    model.LoadComboboxData(model.ContactNumber, preserveTypedValues: true);
                    result.Add(model);
                }
                return result;
            }
        }

        public static List<HandoverItem> GetAllHandovers()
        {
            using (var db = GetConnection())
                return db.Query<HandoverItem>("SELECT * FROM HandoverList ORDER BY CreateDate DESC").ToList();
        }

        public static void InsertHandover(HandoverItem item)
        {
            using (var db = GetConnection())
            {
                string sql = @"INSERT INTO HandoverList (Id, Vendor, Owner, Content, InDate, OutDate, Status, Memo, CreatorName, CreateDate, ModifierName, ModifyDate, ReadBy) 
                               VALUES (@Id, @Vendor, @Owner, @Content, @InDate, @OutDate, @Status, @Memo, @CreatorName, @CreateDate, @ModifierName, @ModifyDate, @ReadBy)";
                db.Execute(sql, new { Id = item.Id.ToString(), item.Vendor, item.Owner, item.Content, item.InDate, item.OutDate, item.Status, item.Memo, item.CreatorName, item.CreateDate, item.ModifierName, item.ModifyDate, item.ReadBy });
            }
        }

        public static void UpdateHandover(HandoverItem item)
        {
            using (var db = GetConnection())
            {
                string sql = @"UPDATE HandoverList 
                               SET Vendor = @Vendor, Owner = @Owner, Content = @Content, InDate = @InDate, OutDate = @OutDate, Status = @Status, Memo = @Memo, ModifierName = @ModifierName, ModifyDate = @ModifyDate, ReadBy = @ReadBy 
                               WHERE Id = @Id";
                db.Execute(sql, new { item.Vendor, item.Owner, item.Content, item.InDate, item.OutDate, item.Status, item.Memo, item.ModifierName, item.ModifyDate, item.ReadBy, Id = item.Id.ToString() });
            }
        }

        public static void UpdateHandoverReadBy(Guid id, string readBy)
        {
            using (var db = GetConnection())
            {
                string sql = "UPDATE HandoverList SET ReadBy = @ReadBy WHERE Id = @Id";
                db.Execute(sql, new { ReadBy = readBy, Id = id.ToString() });
            }
        }

        public static void DeleteHandover(Guid id)
        {
            using (var db = GetConnection())
            {
                string sql = "DELETE FROM HandoverList WHERE Id = @Id";
                db.Execute(sql, new { Id = id.ToString() });
            }
        }

        public static void InitializeScheduleTables()
        {
            using (var connection = GetConnection())
            {
                string createShiftTable = @"
                    CREATE TABLE IF NOT EXISTS ShiftSchedule (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TargetDate TEXT NOT NULL,
                        TeamGroup TEXT,
                        Role TEXT,
                        MemberName TEXT NOT NULL,
                        ShiftType TEXT NOT NULL
                    )";
                connection.Execute(createShiftTable);

                string createEduTable = @"
                    CREATE TABLE IF NOT EXISTS EducationPlan (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        MemberName TEXT NOT NULL,
                        CourseName TEXT NOT NULL,
                        StartDate TEXT NOT NULL,
                        EndDate TEXT NOT NULL,
                        Status TEXT,
                        Progress INTEGER,
                        EduMethod TEXT
                    )";
                connection.Execute(createEduTable);
            }
        }

        public static void UpsertShiftSchedule(ShiftScheduleModel item)
        {
            using (var db = GetConnection())
            {
                string delSql = "DELETE FROM ShiftSchedule WHERE TargetDate = @Date AND MemberName = @Name";
                db.Execute(delSql, new { Date = item.TargetDate.ToString("yyyy-MM-dd"), Name = item.MemberName });

                if (!string.IsNullOrWhiteSpace(item.ShiftType) && item.ShiftType != "비우기")
                {
                    string insertSql = @"INSERT INTO ShiftSchedule (TargetDate, TeamGroup, Role, MemberName, ShiftType) 
                                         VALUES (@TargetDate, @TeamGroup, @Role, @MemberName, @ShiftType)";
                    db.Execute(insertSql, new
                    {
                        TargetDate = item.TargetDate.ToString("yyyy-MM-dd"),
                        TeamGroup = item.TeamGroup ?? "세정",
                        Role = item.Role ?? "사원",
                        MemberName = item.MemberName,
                        ShiftType = item.ShiftType
                    });
                }
            }
        }

        public static void InsertShiftSchedule(ShiftScheduleModel item) => UpsertShiftSchedule(item);

        public static void InsertEducationPlan(EducationPlanModel item)
        {
            using (var db = GetConnection())
            {
                string sql = @"INSERT INTO EducationPlan (MemberName, CourseName, StartDate, EndDate, Status, Progress, EduMethod) 
                                   VALUES (@MemberName, @CourseName, @StartDate, @EndDate, @Status, @Progress, @EduMethod)";
                db.Execute(sql, new
                {
                    item.MemberName,
                    item.CourseName,
                    StartDate = item.StartDate.ToString("yyyy-MM-dd"),
                    EndDate = item.EndDate.ToString("yyyy-MM-dd"),
                    Status = item.Status ?? "대기",
                    Progress = item.Progress,
                    EduMethod = item.EduMethod ?? "이러닝"
                });
            }
        }

        public static List<ShiftScheduleModel> GetShiftSchedulesByDate(DateTime targetDate)
        {
            using (var db = GetConnection())
            {
                string sql = "SELECT * FROM ShiftSchedule WHERE TargetDate = @Date";
                return db.Query<ShiftScheduleModel>(sql, new { Date = targetDate.ToString("yyyy-MM-dd") }).ToList();
            }
        }

        public static List<EducationPlanModel> GetEducationPlansByDate(DateTime targetDate)
        {
            using (var db = GetConnection())
            {
                string sql = "SELECT * FROM EducationPlan WHERE StartDate <= @Date AND EndDate >= @Date";
                return db.Query<EducationPlanModel>(sql, new { Date = targetDate.ToString("yyyy-MM-dd") }).ToList();
            }
        }

        public static List<ShiftScheduleModel> GetShiftSchedulesInRange(DateTime start, DateTime end)
        {
            using (var db = GetConnection())
            {
                string sql = "SELECT * FROM ShiftSchedule WHERE TargetDate >= @Start AND TargetDate <= @End";
                return db.Query<ShiftScheduleModel>(sql, new { Start = start.ToString("yyyy-MM-dd"), End = end.ToString("yyyy-MM-dd") }).ToList();
            }
        }

        public static List<EducationPlanModel> GetEducationPlansInRange(DateTime start, DateTime end)
        {
            using (var db = GetConnection())
            {
                string sql = "SELECT * FROM EducationPlan WHERE StartDate <= @End AND EndDate >= @Start";
                return db.Query<EducationPlanModel>(sql, new { Start = start.ToString("yyyy-MM-dd"), End = end.ToString("yyyy-MM-dd") }).ToList();
            }
        }

        public static void DeleteShiftSchedule(int id)
        {
            using (var db = GetConnection()) db.Execute("DELETE FROM ShiftSchedule WHERE Id = @Id", new { Id = id });
        }

        public static void DeleteEducationPlan(int id)
        {
            using (var db = GetConnection()) db.Execute("DELETE FROM EducationPlan WHERE Id = @Id", new { Id = id });
        }

        public static void UpdateEducationPlanStatus(int id, string status)
        {
            using (var db = GetConnection())
                db.Execute("UPDATE EducationPlan SET Status = @Status WHERE Id = @Id", new { Status = status, Id = id });
        }

        // ==========================================================
        // 🔥 생산팀 요청사항 (ProdReq) 전용 DB 연동 메서드 (Dapper 최적화)
        // ==========================================================

        public static void CreateProdReqTable()
        {
            using (var db = GetConnection())
            {
                string query = @"
                    CREATE TABLE IF NOT EXISTS ProdReqs (
                        Id TEXT PRIMARY KEY,
                        RequestDate TEXT,
                        DueDate TEXT,
                        Status TEXT,
                        Category TEXT,
                        Location TEXT,
                        RequestDetail TEXT,
                        Requester TEXT,
                        ActionDate TEXT,
                        ActionDetail TEXT,
                        Assignee TEXT,
                        RequestMemo TEXT,
                        ActionMemo TEXT
                    )";
                db.Execute(query);
            }
        }

        public static List<ProdReqItem> GetAllProdReqs()
        {
            var list = new List<ProdReqItem>();
            using (var db = GetConnection())
            {
                string query = "SELECT * FROM ProdReqs ORDER BY RequestDate DESC, DueDate ASC";
                var rawData = db.Query(query);

                foreach (var row in rawData)
                {
                    list.Add(new ProdReqItem
                    {
                        Id = Guid.Parse(Convert.ToString(row.Id)),
                        RequestDate = string.IsNullOrEmpty(Convert.ToString(row.RequestDate)) ? null : DateTime.Parse(Convert.ToString(row.RequestDate)),
                        DueDate = string.IsNullOrEmpty(Convert.ToString(row.DueDate)) ? null : DateTime.Parse(Convert.ToString(row.DueDate)),
                        Status = Convert.ToString(row.Status) ?? "진행",
                        Category = Convert.ToString(row.Category) ?? "",
                        Location = Convert.ToString(row.Location) ?? "",
                        RequestDetail = Convert.ToString(row.RequestDetail) ?? "",
                        Requester = Convert.ToString(row.Requester) ?? "",
                        ActionDate = string.IsNullOrEmpty(Convert.ToString(row.ActionDate)) ? null : DateTime.Parse(Convert.ToString(row.ActionDate)),
                        ActionDetail = Convert.ToString(row.ActionDetail) ?? "",
                        Assignee = Convert.ToString(row.Assignee) ?? "",
                        RequestMemo = Convert.ToString(row.RequestMemo) ?? "",
                        ActionMemo = Convert.ToString(row.ActionMemo) ?? "",
                        ManageChecked = false
                    });
                }
            }
            return list;
        }

        public static void InsertProdReq(ProdReqItem item)
        {
            using (var db = GetConnection())
            {
                string query = @"
                    INSERT INTO ProdReqs 
                    (Id, RequestDate, DueDate, Status, Category, Location, RequestDetail, Requester, ActionDate, ActionDetail, Assignee, RequestMemo, ActionMemo) 
                    VALUES 
                    (@Id, @RequestDate, @DueDate, @Status, @Category, @Location, @RequestDetail, @Requester, @ActionDate, @ActionDetail, @Assignee, @RequestMemo, @ActionMemo)";

                db.Execute(query, new
                {
                    Id = item.Id.ToString(),
                    RequestDate = item.RequestDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                    DueDate = item.DueDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                    Status = item.Status,
                    Category = item.Category,
                    Location = item.Location,
                    RequestDetail = item.RequestDetail,
                    Requester = item.Requester,
                    ActionDate = item.ActionDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                    ActionDetail = item.ActionDetail,
                    Assignee = item.Assignee,
                    RequestMemo = item.RequestMemo,
                    ActionMemo = item.ActionMemo
                });
            }
        }

        public static void UpdateProdReq(ProdReqItem item)
        {
            using (var db = GetConnection())
            {
                string query = @"
                    UPDATE ProdReqs 
                    SET RequestDate = @RequestDate, DueDate = @DueDate, Status = @Status, Category = @Category, 
                        Location = @Location, RequestDetail = @RequestDetail, Requester = @Requester, 
                        ActionDate = @ActionDate, ActionDetail = @ActionDetail, Assignee = @Assignee, 
                        RequestMemo = @RequestMemo, ActionMemo = @ActionMemo
                    WHERE Id = @Id";

                db.Execute(query, new
                {
                    Id = item.Id.ToString(),
                    RequestDate = item.RequestDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                    DueDate = item.DueDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                    Status = item.Status,
                    Category = item.Category,
                    Location = item.Location,
                    RequestDetail = item.RequestDetail,
                    Requester = item.Requester,
                    ActionDate = item.ActionDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                    ActionDetail = item.ActionDetail,
                    Assignee = item.Assignee,
                    RequestMemo = item.RequestMemo,
                    ActionMemo = item.ActionMemo
                });
            }
        }

        public static void DeleteProdReq(Guid id)
        {
            using (var db = GetConnection())
            {
                string query = "DELETE FROM ProdReqs WHERE Id = @Id";
                db.Execute(query, new { Id = id.ToString() });
            }
        }
    }
}