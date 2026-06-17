using System;
using System.Collections.Generic;
using System.Linq;
using CleanPotal.FieldInventory.Models;
using Dapper;

namespace CleanPotal.FieldInventory.Repositories
{
    public static class FieldInventoryRepository
    {
        public static void InitializeTables()
        {
            using var db = DatabaseHelper.GetConnection();

            db.Execute(@"
                CREATE TABLE IF NOT EXISTS FieldInventoryItems (
                    ItemId           INTEGER PRIMARY KEY AUTOINCREMENT,
                    OrderNo          INTEGER NOT NULL DEFAULT 0,
                    ItemCode         TEXT NOT NULL DEFAULT '',
                    Category         TEXT NOT NULL DEFAULT '',
                    Unit             TEXT NOT NULL DEFAULT '',
                    RegisteredDate   TEXT NOT NULL DEFAULT '',
                    StorageLocation  TEXT NOT NULL DEFAULT '',
                    ItemName         TEXT NOT NULL DEFAULT '',
                    CurrentStock     TEXT NOT NULL DEFAULT '',
                    AppropriateStock TEXT NOT NULL DEFAULT '',
                    MinOrderQty      TEXT NOT NULL DEFAULT '',
                    Supplier         TEXT NOT NULL DEFAULT '',
                    OrderDate        TEXT NOT NULL DEFAULT '',
                    OrderQty         TEXT NOT NULL DEFAULT '',
                    ExpectedReceipt  TEXT NOT NULL DEFAULT '',
                    Memo             TEXT NOT NULL DEFAULT '',
                    IsOrdered        INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt        TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );");

            db.Execute("CREATE INDEX IF NOT EXISTS IX_FieldInventory_Location ON FieldInventoryItems(StorageLocation);");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_FieldInventory_Order ON FieldInventoryItems(OrderNo);");

            // 주간 재고 스냅샷 (주간 마감 시점의 현재고를 누적 저장 → 전주 대비 증감 / 추후 월별 집계)
            db.Execute(@"
                CREATE TABLE IF NOT EXISTS FieldInventorySnapshots (
                    SnapshotId   INTEGER PRIMARY KEY AUTOINCREMENT,
                    ItemId       INTEGER NOT NULL,
                    SnapshotDate TEXT NOT NULL,
                    Stock        TEXT NOT NULL DEFAULT '',
                    CreatedAt    TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_FieldInvSnap_Item ON FieldInventorySnapshots(ItemId);");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_FieldInvSnap_Date ON FieldInventorySnapshots(SnapshotDate);");

            // 마이그레이션: 기존 DB에 신규 컬럼 추가
            try { db.Execute("ALTER TABLE FieldInventoryItems ADD COLUMN ItemCode TEXT NOT NULL DEFAULT '';"); } catch { }
            try { db.Execute("ALTER TABLE FieldInventoryItems ADD COLUMN Category TEXT NOT NULL DEFAULT '';"); } catch { }
            try { db.Execute("ALTER TABLE FieldInventoryItems ADD COLUMN Unit TEXT NOT NULL DEFAULT '';"); } catch { }
            try { db.Execute("ALTER TABLE FieldInventoryItems ADD COLUMN RegisteredDate TEXT NOT NULL DEFAULT '';"); } catch { }
            try { db.Execute("ALTER TABLE FieldInventoryItems ADD COLUMN IsOrdered INTEGER NOT NULL DEFAULT 0;"); } catch { }

            SeedIfEmpty(db);

            // 등록일자가 비어있는 행은 오늘 날짜로 채움 (신규 컬럼 마이그레이션 + 시드 데이터 공통)
            db.Execute("UPDATE FieldInventoryItems SET RegisteredDate = date('now','localtime') WHERE RegisteredDate = '';");
        }

        public static List<FieldInventoryItem> GetAll()
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<FieldInventoryItem>(
                "SELECT * FROM FieldInventoryItems ORDER BY OrderNo").ToList();
        }

        public static long Insert(FieldInventoryItem item)
        {
            using var db = DatabaseHelper.GetConnection();
            string sql = @"
                INSERT INTO FieldInventoryItems
                    (OrderNo, ItemCode, Category, Unit, RegisteredDate, StorageLocation, ItemName, CurrentStock, AppropriateStock,
                     MinOrderQty, Supplier, OrderDate, OrderQty, ExpectedReceipt, Memo, IsOrdered, UpdatedAt)
                VALUES
                    (@OrderNo, @ItemCode, @Category, @Unit, @RegisteredDate, @StorageLocation, @ItemName, @CurrentStock, @AppropriateStock,
                     @MinOrderQty, @Supplier, @OrderDate, @OrderQty, @ExpectedReceipt, @Memo, @IsOrdered, @UpdatedAt);
                SELECT last_insert_rowid();";
            return db.ExecuteScalar<long>(sql, ToParam(item));
        }

        public static void Update(FieldInventoryItem item)
        {
            item.UpdatedAt = DateTime.Now;
            using var db = DatabaseHelper.GetConnection();
            db.Execute(@"
                UPDATE FieldInventoryItems
                SET OrderNo=@OrderNo, ItemCode=@ItemCode, Category=@Category, Unit=@Unit, RegisteredDate=@RegisteredDate,
                    StorageLocation=@StorageLocation, ItemName=@ItemName,
                    CurrentStock=@CurrentStock, AppropriateStock=@AppropriateStock,
                    MinOrderQty=@MinOrderQty, Supplier=@Supplier,
                    OrderDate=@OrderDate, OrderQty=@OrderQty, ExpectedReceipt=@ExpectedReceipt,
                    Memo=@Memo, IsOrdered=@IsOrdered, UpdatedAt=@UpdatedAt
                WHERE ItemId=@ItemId", ToParam(item));
        }

        public static void Delete(long itemId)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM FieldInventoryItems WHERE ItemId = @Id", new { Id = itemId });
        }

        private static object ToParam(FieldInventoryItem i) => new
        {
            i.ItemId, i.OrderNo, i.ItemCode, i.Category, i.Unit, i.StorageLocation, i.ItemName,
            i.CurrentStock, i.AppropriateStock, i.MinOrderQty, i.Supplier,
            i.OrderDate, i.OrderQty, i.ExpectedReceipt, i.Memo,
            IsOrdered = i.IsOrdered ? 1 : 0,
            RegisteredDate = i.RegisteredDate.ToString("yyyy-MM-dd"),
            UpdatedAt = i.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        };

        // -----------------------------------------------------------------------
        // 주간 스냅샷
        // -----------------------------------------------------------------------
        /// <summary>현재 모든 항목의 현재고를 지정한 날짜의 스냅샷으로 저장 (같은 날짜는 덮어쓰기).</summary>
        public static void CreateSnapshot(DateTime date)
        {
            using var db = DatabaseHelper.GetConnection();
            string d = date.ToString("yyyy-MM-dd");
            db.Execute("DELETE FROM FieldInventorySnapshots WHERE SnapshotDate = @D", new { D = d });
            db.Execute(@"
                INSERT INTO FieldInventorySnapshots (ItemId, SnapshotDate, Stock, CreatedAt)
                SELECT ItemId, @D, CurrentStock, @At FROM FieldInventoryItems",
                new { D = d, At = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        /// <summary>가장 최근 스냅샷 날짜의 (ItemId → 재고값) 맵. 스냅샷이 없으면 빈 맵.</summary>
        public static Dictionary<long, string> GetLatestSnapshotStocks()
        {
            using var db = DatabaseHelper.GetConnection();
            string? latest = db.ExecuteScalar<string?>("SELECT MAX(SnapshotDate) FROM FieldInventorySnapshots");
            var map = new Dictionary<long, string>();
            if (string.IsNullOrEmpty(latest)) return map;

            foreach (var row in db.Query("SELECT ItemId, Stock FROM FieldInventorySnapshots WHERE SnapshotDate = @D", new { D = latest }))
                map[(long)row.ItemId] = (string)(row.Stock ?? "");
            return map;
        }

        /// <summary>가장 최근 스냅샷 날짜 (없으면 null).</summary>
        public static DateTime? GetLatestSnapshotDate()
        {
            using var db = DatabaseHelper.GetConnection();
            string? latest = db.ExecuteScalar<string?>("SELECT MAX(SnapshotDate) FROM FieldInventorySnapshots");
            return DateTime.TryParse(latest, out var dt) ? dt : (DateTime?)null;
        }

        /// <summary>모든 스냅샷 데이터를 날짜순으로 반환 (분석 대시보드용).</summary>
        public static List<(string Date, long ItemId, string Stock)> GetAllSnapshots()
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query("SELECT SnapshotDate, ItemId, Stock FROM FieldInventorySnapshots ORDER BY SnapshotDate")
                .Select(r => ((string)r.SnapshotDate, (long)r.ItemId, (string)(r.Stock ?? "")))
                .ToList();
        }

        /// <summary>스냅샷 날짜 목록 (오름차순).</summary>
        public static List<string> GetSnapshotDates()
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<string>("SELECT DISTINCT SnapshotDate FROM FieldInventorySnapshots ORDER BY SnapshotDate").ToList();
        }

        // 초기 데이터 — 엑셀의 "26년 6월 1주" 시트 내용을 그대로 주입
        private static void SeedIfEmpty(System.Data.IDbConnection db)
        {
            int count = db.ExecuteScalar<int>("SELECT COUNT(*) FROM FieldInventoryItems");
            if (count > 0) return;

            var seeds = new[]
            {
                (1,  "메탈 반입구",   "검정색 토너",              "",         "3EA",      "4EA",    "신도비엠"),
                (2,  "메탈 반입구",   "빨간색 토너",              "",         "3EA",      "4EA",    "신도비엠"),
                (3,  "메탈 반입구",   "노란색 토너",              "",         "3EA",      "4EA",    "신도비엠"),
                (4,  "메탈 반입구",   "파란색 토너",              "",         "3EA",      "4EA",    "신도비엠"),
                (5,  "메탈 반입구",   "에어캡",                   "6",        "8EA",      "10EA",   "주식회사 우암"),
                (6,  "메탈 반입구",   "투명테이프",               "50EA 이상","50EA",     "100EA",  "인터넷"),
                (7,  "메탈 반입구",   "비닐장갑",                 "3",        "2팩",      "1팩",    "인터넷"),
                (8,  "메탈 반입구",   "방진용 덧신",              "50팩 이상","50팩",     "200팩",  "세이프티존"),
                (9,  "메탈 반입구",   "멤브레인용 보안 라벨",     "50매 이상","50매",     "200매",  "다인정보기술"),
                (10, "논메탈 반입구", "크린페이퍼",               "28",       "10EA",     "40EA",   "KM"),
                (11, "논메탈 반입구", "손타라(롤)와이퍼",         "5",        "6EA",      "8EA",    "KM"),
                (12, "논메탈 반입구", "부직포 와이퍼",            "19",       "2EA",      "10EA",   "KM"),
                (13, "논메탈 반입구", "극세사 와이퍼",            "20",       "10팩",     "10팩",   "KM"),
                (14, "논메탈 반입구", "에탄올 와이퍼",            "16",       "5팩",      "10팩",   "KM"),
                (15, "논메탈 반입구", "스티키매트",               "5",        "5EA",      "10EA",   "KM"),
                (16, "논메탈 반입구", "무접지 테이프(TTS용)",     "",         "2EA",      "2EA",    "인터넷"),
                (17, "논메탈 반입구", "방진테이프(제품포장용)",   "30EA 이상","30EA",     "300EA",  "코어텍"),
                (18, "논메탈 반입구", "흰라벨(10*9)",             "48",       "6롤",      "20롤",   "이엔시스"),
                (19, "논메탈 반입구", "빨간라벨(10*9)",           "13",       "4롤",      "20롤",   "이엔시스"),
                (20, "논메탈 반입구", "노란라벨(10*9)",           "9",        "4롤",      "20롤",   "이엔시스"),
                (21, "논메탈 반입구", "초록라벨(10*9)",           "20",       "4롤",      "20롤",   "이엔시스"),
                (22, "논메탈 반입구", "영신 A급 라벨 小 중국",    "600매 이상","600매",   "1,200매","다인정보기술"),
                (23, "논메탈 반입구", "영신 A급 라벨 小 한국",    "600매 이상","600매",   "1,200매","다인정보기술"),
                (24, "논메탈 반입구", "영신 A급 라벨 大 중국",    "150매 이상","150매",   "300매",  "다인정보기술"),
                (25, "논메탈 반입구", "영신 A급 라벨 大 한국",    "150매 이상","150매",   "300매",  "다인정보기술"),
                (26, "논메탈 반입구", "GP 스티커",                "30매 이상","30매",     "20매",   "영신쿼츠, 금강쿼츠"),
                (27, "논메탈 반입구", "손바닥 스티커",            "2",        "1롤",      "2롤",    "영신쿼츠, 금강쿼츠"),
                (28, "논메탈 반입구", "리테이너링 포장 박스",     "400",      "40SET",    "500SET", "주안포장"),
                (29, "OFFICE 보관",  "내산 방진복",              "6",        "10EA",     "5EA",    "수성안전"),
                (30, "OFFICE 보관",  "내산 앞치마",              "23",       "5EA",      "5EA",    "수성안전"),
                (31, "OFFICE 보관",  "심리스 글러브",            "10",       "5팩",      "10팩",   "KM"),
                (32, "세정랩",       "MSDS 안전 스티커(SD-1)",   "",         "50매",     "100매",  "디자인톡"),
                (33, "세정랩",       "SD-1 말통",                "",         "50EA",     "100EA",  "화진"),
                (34, "세정랩",       "SD-1 속마개",              "",         "50EA",     "100EA",  "화진"),
            };

            string sql = @"
                INSERT INTO FieldInventoryItems
                    (OrderNo, StorageLocation, ItemName, CurrentStock, AppropriateStock, MinOrderQty, Supplier, UpdatedAt)
                VALUES
                    (@No, @Loc, @Name, @Cur, @Apt, @Min, @Sup, @At)";

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            foreach (var (no, loc, name, cur, apt, min, sup) in seeds)
            {
                db.Execute(sql, new { No = no, Loc = loc, Name = name, Cur = cur, Apt = apt, Min = min, Sup = sup, At = now });
            }
        }
    }
}
