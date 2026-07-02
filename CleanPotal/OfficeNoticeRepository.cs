using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dapper;

namespace CleanPotal
{
    /// <summary>
    /// 사무실 공지(office_notice) 저장소. 기존 office_notice.json → SQLite(dispatch.db) 이관 파일럿.
    /// NAS 공유 DB이므로 연결은 DatabaseHelper.GetConnection()(journal_mode=DELETE)만 사용.
    /// </summary>
    public static class OfficeNoticeRepository
    {
        public record NoticeRow(Guid Id, string Text);

        // 시작 시 1회 호출: 테이블 생성 + 기존 JSON 자동 이관.
        // shared 연결이 오면 재사용(닫지 않음) — 시작 성능 최적화.
        public static void InitializeTables(IDbConnection? shared = null)
        {
            var db = shared ?? DatabaseHelper.GetConnection();
            try
            {
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS OfficeNotices (
                        Id        TEXT PRIMARY KEY,
                        Text      TEXT NOT NULL DEFAULT '',
                        OrderNo   INTEGER NOT NULL DEFAULT 0,
                        CreatedAt TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                    );");

                MigrateFromJsonIfNeeded(db);
            }
            finally
            {
                if (shared == null) db.Dispose();
            }
        }

        private static string JsonPath => Path.Combine(AppPaths.DataRoot, "office_notice.json");

        // 테이블이 비어 있고 기존 JSON이 있으면 1회 이관. 이관 후 JSON은 .migrated 로 보존(백업).
        private static void MigrateFromJsonIfNeeded(IDbConnection db)
        {
            try
            {
                long count = db.ExecuteScalar<long>("SELECT COUNT(*) FROM OfficeNotices");
                if (count > 0) return;               // 이미 데이터 있음 → 이관 불필요
                string path = JsonPath;
                if (!File.Exists(path)) return;      // 옮길 JSON 없음

                var list = JsonSerializer.Deserialize<List<JsonNotice>>(File.ReadAllText(path))
                           ?? new List<JsonNotice>();

                int order = 0;
                foreach (var n in list)
                {
                    if (n == null) continue;
                    string text = (n.Text ?? "").Trim();
                    if (text.Length == 0) continue;
                    Guid id = n.Id != Guid.Empty ? n.Id : Guid.NewGuid();
                    db.Execute(
                        "INSERT OR IGNORE INTO OfficeNotices (Id, Text, OrderNo) VALUES (@Id, @Text, @Order)",
                        new { Id = id.ToString(), Text = text, Order = order++ });
                }

                // 원본 JSON 보존용 이름 변경(재이관 방지 + 백업). 실패해도 무시(테이블에 이미 들어감).
                try
                {
                    string bak = path + ".migrated";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
                catch { }
            }
            catch { /* 이관 실패해도 앱 시작은 계속 (빈 목록으로 동작) */ }
        }

        public static List<NoticeRow> GetAll()
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query("SELECT Id, Text FROM OfficeNotices ORDER BY OrderNo, rowid")
                     .Select(r => new NoticeRow(
                         Guid.TryParse((string)r.Id, out var g) ? g : Guid.NewGuid(),
                         (string?)r.Text ?? ""))
                     .ToList();
        }

        // UI가 전체 목록을 저장하는 방식(기존 SaveNotices와 동일 의미) → 전체 교체.
        public static void ReplaceAll(IEnumerable<NoticeRow> items)
        {
            using var db = DatabaseHelper.GetConnection();   // 이미 Open 상태
            using var tx = db.BeginTransaction();
            db.Execute("DELETE FROM OfficeNotices", transaction: tx);
            int order = 0;
            foreach (var it in items)
            {
                string text = (it.Text ?? "").Trim();
                if (text.Length == 0) continue;
                Guid id = it.Id != Guid.Empty ? it.Id : Guid.NewGuid();
                db.Execute(
                    "INSERT INTO OfficeNotices (Id, Text, OrderNo) VALUES (@Id, @Text, @Order)",
                    new { Id = id.ToString(), Text = text, Order = order++ }, transaction: tx);
            }
            tx.Commit();
        }

        private class JsonNotice
        {
            public Guid Id { get; set; }
            public string? Text { get; set; }
        }
    }
}
