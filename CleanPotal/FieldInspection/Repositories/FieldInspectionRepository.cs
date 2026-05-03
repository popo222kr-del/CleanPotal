using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using CleanPotal.FieldInspection.Models;
using Dapper;

namespace CleanPotal.FieldInspection.Repositories
{
    /// <summary>
    /// 현장 점검(NFC/QR 체크시트) 전용 데이터 접근 계층.
    /// 추후 SQL Server 등으로 이식 가능하도록 DatabaseHelper.GetConnection()만 외부 의존.
    /// </summary>
    public static class FieldInspectionRepository
    {
        public static void InitializeTables()
        {
            using var db = DatabaseHelper.GetConnection();

            db.Execute(@"
                CREATE TABLE IF NOT EXISTS FieldLocations (
                    LocationId   INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code         TEXT NOT NULL UNIQUE,
                    Name         TEXT NOT NULL,
                    Zone         TEXT,
                    Equipment    TEXT,
                    IsActive     INTEGER NOT NULL DEFAULT 1,
                    Memo         TEXT,
                    CreatedAt    TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );");

            db.Execute(@"
                CREATE TABLE IF NOT EXISTS FieldTags (
                    TagId        TEXT PRIMARY KEY,
                    LocationId   INTEGER NOT NULL,
                    TagType      TEXT NOT NULL,
                    QrPayload    TEXT,
                    Token        TEXT NOT NULL,
                    IsActive     INTEGER NOT NULL DEFAULT 1,
                    Memo         TEXT,
                    CreatedAt    TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                    FOREIGN KEY (LocationId) REFERENCES FieldLocations(LocationId)
                );");

            db.Execute(@"
                CREATE TABLE IF NOT EXISTS FieldChecklists (
                    ChecklistId  INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name         TEXT NOT NULL,
                    LocationId   INTEGER,
                    Cycle        TEXT,
                    IsActive     INTEGER NOT NULL DEFAULT 1,
                    Memo         TEXT,
                    CreatedAt    TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                    FOREIGN KEY (LocationId) REFERENCES FieldLocations(LocationId)
                );");

            db.Execute(@"
                CREATE TABLE IF NOT EXISTS FieldChecklistItems (
                    ItemId       INTEGER PRIMARY KEY AUTOINCREMENT,
                    ChecklistId  INTEGER NOT NULL,
                    OrderNo      INTEGER NOT NULL,
                    Title        TEXT NOT NULL,
                    InputType    TEXT NOT NULL,
                    UnitOrHint   TEXT,
                    MinValue     REAL,
                    MaxValue     REAL,
                    IsRequired   INTEGER NOT NULL DEFAULT 1,
                    Memo         TEXT,
                    FOREIGN KEY (ChecklistId) REFERENCES FieldChecklists(ChecklistId)
                );");

            db.Execute(@"
                CREATE TABLE IF NOT EXISTS FieldInspectionRecords (
                    RecordId      TEXT PRIMARY KEY,
                    TagId         TEXT,
                    LocationId    INTEGER NOT NULL,
                    ChecklistId   INTEGER NOT NULL,
                    InspectorName TEXT NOT NULL,
                    InspectorId   TEXT,
                    StartedAt     TEXT NOT NULL,
                    CompletedAt   TEXT,
                    OverallStatus TEXT NOT NULL,
                    Note          TEXT,
                    ClientIp      TEXT,
                    CreatedAt     TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                    FOREIGN KEY (TagId) REFERENCES FieldTags(TagId),
                    FOREIGN KEY (LocationId) REFERENCES FieldLocations(LocationId),
                    FOREIGN KEY (ChecklistId) REFERENCES FieldChecklists(ChecklistId)
                );");

            db.Execute(@"
                CREATE TABLE IF NOT EXISTS FieldInspectionRecordItems (
                    RecordItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                    RecordId     TEXT NOT NULL,
                    ItemId       INTEGER NOT NULL,
                    ResultText   TEXT,
                    IsAbnormal   INTEGER NOT NULL DEFAULT 0,
                    Comment      TEXT,
                    FOREIGN KEY (RecordId) REFERENCES FieldInspectionRecords(RecordId),
                    FOREIGN KEY (ItemId) REFERENCES FieldChecklistItems(ItemId)
                );");

            db.Execute(@"
                CREATE TABLE IF NOT EXISTS FieldInspectionAttachments (
                    AttachmentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    RecordId     TEXT NOT NULL,
                    RecordItemId INTEGER,
                    FileName     TEXT NOT NULL,
                    RelativePath TEXT NOT NULL,
                    ContentType  TEXT,
                    ByteSize     INTEGER,
                    CreatedAt    TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                    FOREIGN KEY (RecordId) REFERENCES FieldInspectionRecords(RecordId)
                );");

            db.Execute("CREATE INDEX IF NOT EXISTS IX_FieldTags_Location ON FieldTags(LocationId);");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_FieldChecklistItems_Checklist ON FieldChecklistItems(ChecklistId);");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_FieldRecords_Location ON FieldInspectionRecords(LocationId);");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_FieldRecords_Started ON FieldInspectionRecords(StartedAt);");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_FieldRecordItems_Record ON FieldInspectionRecordItems(RecordId);");
        }

        // ---------------- FieldLocations ----------------

        public static List<FieldLocation> GetLocations(bool onlyActive = false)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = onlyActive
                ? "SELECT * FROM FieldLocations WHERE IsActive = 1 ORDER BY Code"
                : "SELECT * FROM FieldLocations ORDER BY Code";
            return db.Query<FieldLocation>(sql).ToList();
        }

        public static long InsertLocation(FieldLocation item)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"
                INSERT INTO FieldLocations (Code, Name, Zone, Equipment, IsActive, Memo)
                VALUES (@Code, @Name, @Zone, @Equipment, @IsActive, @Memo);
                SELECT last_insert_rowid();";
            return db.ExecuteScalar<long>(sql, new
            {
                item.Code,
                item.Name,
                item.Zone,
                item.Equipment,
                IsActive = item.IsActive ? 1 : 0,
                item.Memo
            });
        }

        public static void UpdateLocation(FieldLocation item)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"
                UPDATE FieldLocations
                SET Code = @Code, Name = @Name, Zone = @Zone, Equipment = @Equipment,
                    IsActive = @IsActive, Memo = @Memo
                WHERE LocationId = @LocationId";
            db.Execute(sql, new
            {
                item.Code,
                item.Name,
                item.Zone,
                item.Equipment,
                IsActive = item.IsActive ? 1 : 0,
                item.Memo,
                item.LocationId
            });
        }

        public static void DeleteLocation(long locationId)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM FieldLocations WHERE LocationId = @Id", new { Id = locationId });
        }

        // ---------------- FieldTags ----------------

        public static List<FieldTag> GetTags(long? locationId = null)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = locationId.HasValue
                ? "SELECT * FROM FieldTags WHERE LocationId = @Id ORDER BY CreatedAt DESC"
                : "SELECT * FROM FieldTags ORDER BY CreatedAt DESC";
            return db.Query<FieldTag>(sql, new { Id = locationId }).ToList();
        }

        public static FieldTag? GetTag(string tagId)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.QueryFirstOrDefault<FieldTag>(
                "SELECT * FROM FieldTags WHERE TagId = @Id",
                new { Id = tagId });
        }

        public static void InsertTag(FieldTag tag)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"
                INSERT INTO FieldTags (TagId, LocationId, TagType, QrPayload, Token, IsActive, Memo)
                VALUES (@TagId, @LocationId, @TagType, @QrPayload, @Token, @IsActive, @Memo)";
            db.Execute(sql, new
            {
                tag.TagId,
                tag.LocationId,
                tag.TagType,
                tag.QrPayload,
                tag.Token,
                IsActive = tag.IsActive ? 1 : 0,
                tag.Memo
            });
        }

        public static void UpdateTag(FieldTag tag)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"
                UPDATE FieldTags
                SET LocationId = @LocationId, TagType = @TagType, QrPayload = @QrPayload,
                    Token = @Token, IsActive = @IsActive, Memo = @Memo
                WHERE TagId = @TagId";
            db.Execute(sql, new
            {
                tag.TagId,
                tag.LocationId,
                tag.TagType,
                tag.QrPayload,
                tag.Token,
                IsActive = tag.IsActive ? 1 : 0,
                tag.Memo
            });
        }

        public static void DeleteTag(string tagId)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM FieldTags WHERE TagId = @Id", new { Id = tagId });
        }

        // ---------------- FieldChecklists ----------------

        public static List<FieldChecklist> GetChecklists(bool onlyActive = false)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = onlyActive
                ? "SELECT * FROM FieldChecklists WHERE IsActive = 1 ORDER BY Name"
                : "SELECT * FROM FieldChecklists ORDER BY Name";
            return db.Query<FieldChecklist>(sql).ToList();
        }

        public static FieldChecklist? GetChecklistByLocation(long locationId)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"SELECT * FROM FieldChecklists
                           WHERE IsActive = 1 AND (LocationId = @Id OR LocationId IS NULL)
                           ORDER BY (CASE WHEN LocationId = @Id THEN 0 ELSE 1 END)
                           LIMIT 1";
            return db.QueryFirstOrDefault<FieldChecklist>(sql, new { Id = locationId });
        }

        public static long InsertChecklist(FieldChecklist item)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"
                INSERT INTO FieldChecklists (Name, LocationId, Cycle, IsActive, Memo)
                VALUES (@Name, @LocationId, @Cycle, @IsActive, @Memo);
                SELECT last_insert_rowid();";
            return db.ExecuteScalar<long>(sql, new
            {
                item.Name,
                item.LocationId,
                item.Cycle,
                IsActive = item.IsActive ? 1 : 0,
                item.Memo
            });
        }

        public static void UpdateChecklist(FieldChecklist item)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"
                UPDATE FieldChecklists
                SET Name = @Name, LocationId = @LocationId, Cycle = @Cycle,
                    IsActive = @IsActive, Memo = @Memo
                WHERE ChecklistId = @ChecklistId";
            db.Execute(sql, new
            {
                item.Name,
                item.LocationId,
                item.Cycle,
                IsActive = item.IsActive ? 1 : 0,
                item.Memo,
                item.ChecklistId
            });
        }

        public static void DeleteChecklist(long checklistId)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM FieldChecklistItems WHERE ChecklistId = @Id", new { Id = checklistId });
            db.Execute("DELETE FROM FieldChecklists WHERE ChecklistId = @Id", new { Id = checklistId });
        }

        // ---------------- FieldChecklistItems ----------------

        public static List<FieldChecklistItem> GetChecklistItems(long checklistId)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<FieldChecklistItem>(
                "SELECT * FROM FieldChecklistItems WHERE ChecklistId = @Id ORDER BY OrderNo",
                new { Id = checklistId }).ToList();
        }

        public static long InsertChecklistItem(FieldChecklistItem item)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"
                INSERT INTO FieldChecklistItems
                    (ChecklistId, OrderNo, Title, InputType, UnitOrHint, MinValue, MaxValue, IsRequired, Memo)
                VALUES
                    (@ChecklistId, @OrderNo, @Title, @InputType, @UnitOrHint, @MinValue, @MaxValue, @IsRequired, @Memo);
                SELECT last_insert_rowid();";
            return db.ExecuteScalar<long>(sql, new
            {
                item.ChecklistId,
                item.OrderNo,
                item.Title,
                item.InputType,
                item.UnitOrHint,
                item.MinValue,
                item.MaxValue,
                IsRequired = item.IsRequired ? 1 : 0,
                item.Memo
            });
        }

        public static void UpdateChecklistItem(FieldChecklistItem item)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"
                UPDATE FieldChecklistItems
                SET OrderNo = @OrderNo, Title = @Title, InputType = @InputType,
                    UnitOrHint = @UnitOrHint, MinValue = @MinValue, MaxValue = @MaxValue,
                    IsRequired = @IsRequired, Memo = @Memo
                WHERE ItemId = @ItemId";
            db.Execute(sql, new
            {
                item.OrderNo,
                item.Title,
                item.InputType,
                item.UnitOrHint,
                item.MinValue,
                item.MaxValue,
                IsRequired = item.IsRequired ? 1 : 0,
                item.Memo,
                item.ItemId
            });
        }

        public static void DeleteChecklistItem(long itemId)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM FieldChecklistItems WHERE ItemId = @Id", new { Id = itemId });
        }

        // ---------------- FieldInspectionRecords ----------------

        public static void InsertRecord(FieldInspectionRecord record)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Open();
            using var tx = db.BeginTransaction();

            string sql = @"
                INSERT INTO FieldInspectionRecords
                    (RecordId, TagId, LocationId, ChecklistId, InspectorName, InspectorId,
                     StartedAt, CompletedAt, OverallStatus, Note, ClientIp)
                VALUES
                    (@RecordId, @TagId, @LocationId, @ChecklistId, @InspectorName, @InspectorId,
                     @StartedAt, @CompletedAt, @OverallStatus, @Note, @ClientIp)";

            db.Execute(sql, new
            {
                record.RecordId,
                record.TagId,
                record.LocationId,
                record.ChecklistId,
                record.InspectorName,
                record.InspectorId,
                StartedAt = record.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                CompletedAt = record.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                record.OverallStatus,
                record.Note,
                record.ClientIp
            }, tx);

            string itemSql = @"
                INSERT INTO FieldInspectionRecordItems
                    (RecordId, ItemId, ResultText, IsAbnormal, Comment)
                VALUES
                    (@RecordId, @ItemId, @ResultText, @IsAbnormal, @Comment)";

            foreach (var i in record.Items)
            {
                db.Execute(itemSql, new
                {
                    RecordId = record.RecordId,
                    i.ItemId,
                    i.ResultText,
                    IsAbnormal = i.IsAbnormal ? 1 : 0,
                    i.Comment
                }, tx);
            }

            tx.Commit();
        }

        public static List<FieldInspectionRecord> SearchRecords(
            DateTime? from,
            DateTime? to,
            long? locationId,
            string? inspectorName,
            string? overallStatus)
        {
            using var db = DatabaseHelper.GetConnection();

            var where = new List<string>();
            var p = new DynamicParameters();
            if (from.HasValue) { where.Add("StartedAt >= @From"); p.Add("From", from.Value.ToString("yyyy-MM-dd 00:00:00")); }
            if (to.HasValue)   { where.Add("StartedAt <= @To");   p.Add("To",   to.Value.ToString("yyyy-MM-dd 23:59:59")); }
            if (locationId.HasValue) { where.Add("LocationId = @LocationId"); p.Add("LocationId", locationId.Value); }
            if (!string.IsNullOrWhiteSpace(inspectorName)) { where.Add("InspectorName LIKE @Insp"); p.Add("Insp", "%" + inspectorName + "%"); }
            if (!string.IsNullOrWhiteSpace(overallStatus)) { where.Add("OverallStatus = @Status"); p.Add("Status", overallStatus); }

            string sql = "SELECT * FROM FieldInspectionRecords"
                + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                + " ORDER BY StartedAt DESC";

            return db.Query<FieldInspectionRecord>(sql, p).ToList();
        }

        public static List<FieldInspectionRecordItem> GetRecordItems(string recordId)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<FieldInspectionRecordItem>(
                "SELECT * FROM FieldInspectionRecordItems WHERE RecordId = @Id",
                new { Id = recordId }).ToList();
        }

        // ---------------- FieldInspectionAttachments ----------------

        public static long InsertAttachment(FieldInspectionAttachment att)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"
                INSERT INTO FieldInspectionAttachments
                    (RecordId, RecordItemId, FileName, RelativePath, ContentType, ByteSize)
                VALUES
                    (@RecordId, @RecordItemId, @FileName, @RelativePath, @ContentType, @ByteSize);
                SELECT last_insert_rowid();";
            return db.ExecuteScalar<long>(sql, att);
        }

        public static List<FieldInspectionAttachment> GetAttachments(string recordId)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<FieldInspectionAttachment>(
                "SELECT * FROM FieldInspectionAttachments WHERE RecordId = @Id ORDER BY AttachmentId",
                new { Id = recordId }).ToList();
        }
    }
}
