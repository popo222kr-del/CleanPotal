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
        private static readonly string ConnectionString = $"Data Source={DbPath};Pooling=False";
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
                try { connection.Execute("PRAGMA journal_mode=DELETE;"); } catch { }
                connection.Execute("PRAGMA busy_timeout=5000;");
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
            InitializeWorkAssignmentTables();

            // 🔥 앱 실행 시 생산팀 요청사항 테이블 자동 생성 호출!
            CreateProdReqTable();

            // 🔥 현장 점검(NFC/QR 체크시트) 테이블 자동 생성
            FieldInspection.Repositories.FieldInspectionRepository.InitializeTables();

            // 🔥 현장 재고 관리 테이블 자동 생성 + 초기 데이터 주입
            FieldInventory.Repositories.FieldInventoryRepository.InitializeTables();
        }

        public static IDbConnection GetConnection()
        {
            var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=DELETE;"; cmd.ExecuteNonQuery(); }
            return conn;
        }

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
                try { connection.Execute("ALTER TABLE EducationPlan ADD COLUMN AttachmentPath TEXT;"); } catch { }

                string createLogTable = @"
                    CREATE TABLE IF NOT EXISTS ShiftScheduleLog (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TargetDate TEXT NOT NULL,
                        MemberName TEXT NOT NULL,
                        OldShiftType TEXT,
                        NewShiftType TEXT,
                        Action TEXT NOT NULL,
                        ModifiedBy TEXT NOT NULL,
                        ModifiedAt TEXT NOT NULL
                    )";
                connection.Execute(createLogTable);

                string createTeamEventsTable = @"
                    CREATE TABLE IF NOT EXISTS TeamEvents (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RegisteredBy TEXT NOT NULL,
                        StartDate TEXT NOT NULL,
                        EndDate TEXT NOT NULL,
                        Content TEXT NOT NULL
                    )";
                connection.Execute(createTeamEventsTable);
                try { connection.Execute("ALTER TABLE TeamEvents ADD COLUMN Detail TEXT;"); } catch { }
            }
        }

        public static void UpdateEducationPlanAttachment(int id, string? path)
        {
            using (var db = GetConnection())
                db.Execute("UPDATE EducationPlan SET AttachmentPath = @Path WHERE Id = @Id",
                    new { Path = path ?? "", Id = id });
        }

        private static void InsertShiftLog(IDbConnection db, string targetDate, string memberName, string? oldType, string? newType, string action)
        {
            string modifier = SessionManager.IsLoggedIn ? SessionManager.CurrentRealName : "알 수 없음";
            if (string.IsNullOrEmpty(modifier)) modifier = SessionManager.CurrentUsername;
            db.Execute(@"INSERT INTO ShiftScheduleLog (TargetDate, MemberName, OldShiftType, NewShiftType, Action, ModifiedBy, ModifiedAt)
                         VALUES (@TargetDate, @MemberName, @Old, @New, @Action, @By, @At)",
                new { TargetDate = targetDate, MemberName = memberName, Old = oldType ?? "", New = newType ?? "", Action = action, By = modifier, At = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        public static void UpsertShiftSchedule(ShiftScheduleModel item)
        {
            using (var db = GetConnection())
            {
                string dateStr = item.TargetDate.ToString("yyyy-MM-dd");
                var old = db.QueryFirstOrDefault<ShiftScheduleModel>("SELECT * FROM ShiftSchedule WHERE TargetDate = @Date AND MemberName = @Name",
                    new { Date = dateStr, Name = item.MemberName });

                string delSql = "DELETE FROM ShiftSchedule WHERE TargetDate = @Date AND MemberName = @Name";
                db.Execute(delSql, new { Date = dateStr, Name = item.MemberName });

                if (!string.IsNullOrWhiteSpace(item.ShiftType) && item.ShiftType != "비우기")
                {
                    string insertSql = @"INSERT INTO ShiftSchedule (TargetDate, TeamGroup, Role, MemberName, ShiftType)
                                         VALUES (@TargetDate, @TeamGroup, @Role, @MemberName, @ShiftType)";
                    db.Execute(insertSql, new
                    {
                        TargetDate = dateStr,
                        TeamGroup = item.TeamGroup ?? "세정",
                        Role = item.Role ?? "사원",
                        MemberName = item.MemberName,
                        ShiftType = item.ShiftType
                    });
                    string action = old != null ? "수정" : "등록";
                    InsertShiftLog(db, dateStr, item.MemberName, old?.ShiftType, item.ShiftType, action);
                }
                else if (old != null)
                {
                    InsertShiftLog(db, dateStr, item.MemberName, old.ShiftType, null, "삭제");
                }
            }
        }

        public static void InsertShiftSchedule(ShiftScheduleModel item) => UpsertShiftSchedule(item);

        public static void UpdateEducationPlan(EducationPlanModel item)
        {
            using var db = GetConnection();
            db.Execute(@"UPDATE EducationPlan SET MemberName=@MemberName, CourseName=@CourseName,
                         StartDate=@StartDate, EndDate=@EndDate, EduMethod=@EduMethod WHERE Id=@Id",
                new { item.MemberName, item.CourseName,
                      StartDate = item.StartDate.ToString("yyyy-MM-dd"),
                      EndDate   = item.EndDate.ToString("yyyy-MM-dd"),
                      item.EduMethod, item.Id });
        }

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
            using (var db = GetConnection())
            {
                var old = db.QueryFirstOrDefault<ShiftScheduleModel>("SELECT * FROM ShiftSchedule WHERE Id = @Id", new { Id = id });
                db.Execute("DELETE FROM ShiftSchedule WHERE Id = @Id", new { Id = id });
                if (old != null) InsertShiftLog(db, old.TargetDate.ToString("yyyy-MM-dd"), old.MemberName, old.ShiftType, null, "삭제");
            }
        }

        public static void UpdateShiftScheduleType(int id, string newShiftType)
        {
            using (var db = GetConnection())
            {
                var old = db.QueryFirstOrDefault<ShiftScheduleModel>("SELECT * FROM ShiftSchedule WHERE Id = @Id", new { Id = id });
                db.Execute("UPDATE ShiftSchedule SET ShiftType = @ShiftType WHERE Id = @Id",
                    new { ShiftType = newShiftType, Id = id });
                if (old != null) InsertShiftLog(db, old.TargetDate.ToString("yyyy-MM-dd"), old.MemberName, old.ShiftType, newShiftType, "수정");
            }
        }

        public static void DeleteEducationPlan(int id)
        {
            using (var db = GetConnection()) db.Execute("DELETE FROM EducationPlan WHERE Id = @Id", new { Id = id });
        }

        public static void InsertTeamEvent(TeamEvent item)
        {
            using (var db = GetConnection())
                db.Execute("INSERT INTO TeamEvents (RegisteredBy, StartDate, EndDate, Content, Detail) VALUES (@RegisteredBy, @StartDate, @EndDate, @Content, @Detail)",
                    new { item.RegisteredBy, item.StartDate, item.EndDate, item.Content, item.Detail });
        }

        public static List<TeamEvent> GetTeamEventsInRange(DateTime start, DateTime end)
        {
            using (var db = GetConnection())
            {
                string sql = "SELECT * FROM TeamEvents WHERE StartDate <= @End AND EndDate >= @Start ORDER BY StartDate ASC";
                return db.Query<TeamEvent>(sql, new { Start = start.ToString("yyyy-MM-dd"), End = end.ToString("yyyy-MM-dd") }).ToList();
            }
        }

        public static void DeleteTeamEvent(int id)
        {
            using (var db = GetConnection()) db.Execute("DELETE FROM TeamEvents WHERE Id = @Id", new { Id = id });
        }

        public static void UpdateTeamEvent(TeamEvent item)
        {
            using (var db = GetConnection())
                db.Execute("UPDATE TeamEvents SET StartDate=@StartDate, EndDate=@EndDate, Content=@Content, Detail=@Detail WHERE Id=@Id",
                    new { item.StartDate, item.EndDate, item.Content, item.Detail, item.Id });
        }

        public static List<ShiftScheduleLogModel> GetShiftScheduleLogs(DateTime? from = null, DateTime? to = null, string? memberName = null)
        {
            using (var db = GetConnection())
            {
                string sql = "SELECT * FROM ShiftScheduleLog WHERE 1=1";
                var p = new DynamicParameters();
                if (from.HasValue) { sql += " AND TargetDate >= @From"; p.Add("From", from.Value.ToString("yyyy-MM-dd")); }
                if (to.HasValue) { sql += " AND TargetDate <= @To"; p.Add("To", to.Value.ToString("yyyy-MM-dd")); }
                if (!string.IsNullOrEmpty(memberName)) { sql += " AND MemberName = @Name"; p.Add("Name", memberName); }
                sql += " ORDER BY ModifiedAt DESC";
                return db.Query<ShiftScheduleLogModel>(sql, p).ToList();
            }
        }

        public static void UpdateEducationPlanStatus(int id, string status, int? progress = null)
        {
            using (var db = GetConnection())
            {
                if (progress.HasValue)
                    db.Execute("UPDATE EducationPlan SET Status = @Status, Progress = @Progress WHERE Id = @Id",
                        new { Status = status, Progress = progress.Value, Id = id });
                else
                    db.Execute("UPDATE EducationPlan SET Status = @Status WHERE Id = @Id",
                        new { Status = status, Id = id });
            }
        }

        public static List<EducationPlanModel> GetEducationPlansByMember(string memberName)
        {
            using (var db = GetConnection())
                return db.Query<EducationPlanModel>("SELECT * FROM EducationPlan WHERE MemberName = @Name ORDER BY StartDate DESC",
                    new { Name = memberName }).ToList();
        }

        // ==========================================================
        // 개인별 업무 분장표 (WorkAssignment)
        // ==========================================================

        public static void InitializeWorkAssignmentTables()
        {
            using (var db = GetConnection())
            {
                db.Execute(@"CREATE TABLE IF NOT EXISTS WorkAssignmentMembers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE
                )");
                db.Execute(@"CREATE TABLE IF NOT EXISTS WorkAssignmentEduBasic (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL,
                    EduName TEXT,
                    StartDate TEXT,
                    EndDate TEXT
                )");
                try { db.Execute("ALTER TABLE WorkAssignmentEduBasic ADD COLUMN StartDate TEXT"); } catch { }
                try { db.Execute("ALTER TABLE WorkAssignmentEduBasic ADD COLUMN EndDate TEXT"); } catch { }
                try { db.Execute("ALTER TABLE WorkAssignmentMembers ADD COLUMN IsHidden INTEGER DEFAULT 0"); } catch { }
                try { db.Execute("ALTER TABLE WorkAssignmentMembers ADD COLUMN ResignDate TEXT"); } catch { }
                db.Execute(@"CREATE TABLE IF NOT EXISTS WorkAssignmentAccounts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL,
                    ServiceName TEXT,
                    AccountId TEXT,
                    AccountPassword TEXT,
                    Note TEXT
                )");
            }
        }

        public static List<string> GetWorkAssignmentUsernames()
        {
            using (var db = GetConnection())
                return db.Query<string>("SELECT Username FROM WorkAssignmentMembers ORDER BY rowid").ToList();
        }

        private class WaMemberRow { public string Username { get; set; } = ""; public int IsHidden { get; set; } public string? ResignDate { get; set; } }
        public static List<(string Username, bool IsHidden, string ResignDate)> GetWorkAssignmentMemberData()
        {
            using var db = GetConnection();
            return db.Query<WaMemberRow>("SELECT Username, COALESCE(IsHidden,0) AS IsHidden, COALESCE(ResignDate,'') AS ResignDate FROM WorkAssignmentMembers ORDER BY rowid")
                     .Select(r => (r.Username, r.IsHidden == 1, r.ResignDate ?? "")).ToList();
        }

        public static void SetWorkAssignmentMemberHidden(string username, bool hidden)
        {
            using var db = GetConnection();
            db.Execute("UPDATE WorkAssignmentMembers SET IsHidden=@H WHERE Username=@U",
                       new { H = hidden ? 1 : 0, U = username });
        }

        public static void SetWorkAssignmentResignInfo(string username, bool isHidden, string resignDate)
        {
            using var db = GetConnection();
            db.Execute("UPDATE WorkAssignmentMembers SET IsHidden=@H, ResignDate=@D WHERE Username=@U",
                       new { H = isHidden ? 1 : 0, D = resignDate, U = username });
        }

        public static void AddWorkAssignmentMember(string username)
        {
            using (var db = GetConnection())
                db.Execute("INSERT OR IGNORE INTO WorkAssignmentMembers (Username) VALUES (@Username)", new { Username = username });
        }

        public static void RemoveWorkAssignmentMember(string username)
        {
            using (var db = GetConnection())
            {
                db.Execute("DELETE FROM WorkAssignmentMembers WHERE Username = @Username", new { Username = username });
                db.Execute("DELETE FROM WorkAssignmentEduBasic WHERE Username = @Username", new { Username = username });
                db.Execute("DELETE FROM WorkAssignmentAccounts WHERE Username = @Username", new { Username = username });
            }
        }

        public static List<EduBasicItem> GetEduBasicItems(string username)
        {
            using (var db = GetConnection())
                return db.Query<EduBasicItem>("SELECT * FROM WorkAssignmentEduBasic WHERE Username = @Username ORDER BY Id", new { Username = username }).ToList();
        }

        public static void SaveEduBasicItems(string username, IEnumerable<EduBasicItem> items)
        {
            using (var db = GetConnection())
            {
                db.Execute("DELETE FROM WorkAssignmentEduBasic WHERE Username = @Username", new { Username = username });
                foreach (var item in items)
                    db.Execute("INSERT INTO WorkAssignmentEduBasic (Username, EduName, StartDate, EndDate) VALUES (@Username, @EduName, @StartDate, @EndDate)",
                        new { Username = username, item.EduName, item.StartDate, item.EndDate });
            }
        }

        public static List<AccountItem> GetAccountItems(string username)
        {
            using (var db = GetConnection())
                return db.Query<AccountItem>("SELECT * FROM WorkAssignmentAccounts WHERE Username = @Username ORDER BY Id", new { Username = username }).ToList();
        }

        public static void SaveAccountItems(string username, IEnumerable<AccountItem> items)
        {
            using (var db = GetConnection())
            {
                db.Execute("DELETE FROM WorkAssignmentAccounts WHERE Username = @Username", new { Username = username });
                foreach (var item in items)
                    db.Execute("INSERT INTO WorkAssignmentAccounts (Username, ServiceName, AccountId, AccountPassword, Note) VALUES (@Username, @ServiceName, @AccountId, @AccountPassword, @Note)",
                        new { Username = username, item.ServiceName, item.AccountId, item.AccountPassword, item.Note });
            }
        }

        // ==========================================================
        // 🔥 생산팀 요청사항 (ProdReq) 전용 DB 연동 메서드 (Dapper 최적화)
        // ==========================================================

        public static void CreateProdReqTable()
        {
            using (var db = GetConnection())
            {
                db.Execute(@"
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
                    )");
                try { db.Execute("ALTER TABLE ProdReqs ADD COLUMN CreatedAt TEXT"); } catch { }

                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS ProdReqReadState (
                        Username TEXT PRIMARY KEY,
                        LastReadTime TEXT NOT NULL
                    )");
            }
        }

        public static int GetUnreadProdReqCount(string username)
        {
            using var db = GetConnection();
            var lastRead = db.ExecuteScalar<string>(
                "SELECT LastReadTime FROM ProdReqReadState WHERE Username = @Username",
                new { Username = username });
            if (lastRead == null) return 0;
            return db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM ProdReqs WHERE CreatedAt > @LastRead",
                new { LastRead = lastRead });
        }

        public static void MarkProdReqAsRead(string username)
        {
            using var db = GetConnection();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            db.Execute(@"
                INSERT INTO ProdReqReadState (Username, LastReadTime) VALUES (@Username, @Now)
                ON CONFLICT(Username) DO UPDATE SET LastReadTime = @Now",
                new { Username = username, Now = now });
        }

        public static void InitProdReqReadStateIfNew(string username)
        {
            using var db = GetConnection();
            int exists = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM ProdReqReadState WHERE Username = @Username",
                new { Username = username });
            if (exists == 0)
            {
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                db.Execute("INSERT INTO ProdReqReadState (Username, LastReadTime) VALUES (@Username, @Now)",
                    new { Username = username, Now = now });
            }
        }

        public static List<ProdReqItem> GetAllProdReqs()
        {
            using var db = GetConnection();
            return db.Query<ProdReqItem>(
                "SELECT * FROM ProdReqs ORDER BY RequestDate DESC, DueDate ASC"
            ).AsList();
        }

        public static void InsertProdReq(ProdReqItem item)
        {
            using (var db = GetConnection())
            {
                string query = @"
                    INSERT INTO ProdReqs
                    (Id, RequestDate, DueDate, Status, Category, Location, RequestDetail, Requester, ActionDate, ActionDetail, Assignee, RequestMemo, ActionMemo, CreatedAt)
                    VALUES
                    (@Id, @RequestDate, @DueDate, @Status, @Category, @Location, @RequestDetail, @Requester, @ActionDate, @ActionDetail, @Assignee, @RequestMemo, @ActionMemo, @CreatedAt)";

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
                    ActionMemo = item.ActionMemo,
                    CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
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