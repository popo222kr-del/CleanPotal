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

                if (AppPaths.DbMigrationEnabled) MigrateFromJsonIfNeeded(db);
            }
            finally
            {
                if (shared == null) db.Dispose();
            }
        }

        private static string JsonPath => Path.Combine(AppPaths.DataRoot, "office_notice.json");

        // 원본 JSON이 있으면 그 내용으로 덮어쓰기 이관 후 .migrated 로 보존(백업). (배포 스위치가 켜졌을 때만 호출됨)
        private static void MigrateFromJsonIfNeeded(IDbConnection db)
        {
            try
            {
                string path = JsonPath;
                if (!File.Exists(path)) return;      // 이미 이관됨(또는 원본 없음)

                db.Execute("DELETE FROM OfficeNotices");   // 배포 시점 JSON을 원본으로 삼아 덮어쓰기

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
            // 🚧 플래그 OFF(갭) → 기존 office_notice.json 직접 읽기(구버전과 동일)
            if (!AppPaths.DbMigrationEnabled)
            {
                try
                {
                    if (File.Exists(JsonPath))
                    {
                        var list = JsonSerializer.Deserialize<List<JsonNotice>>(File.ReadAllText(JsonPath))
                                   ?? new List<JsonNotice>();
                        return list.Where(n => n != null && !string.IsNullOrWhiteSpace(n.Text))
                                   .Select(n => new NoticeRow(n.Id != Guid.Empty ? n.Id : Guid.NewGuid(), (n.Text ?? "").Trim()))
                                   .ToList();
                    }
                }
                catch { }
                return new List<NoticeRow>();
            }

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
            // 🚧 플래그 OFF(갭) → 기존 office_notice.json 에 저장(구버전과 동일 포맷)
            if (!AppPaths.DbMigrationEnabled)
            {
                try
                {
                    var arr = items.Where(i => !string.IsNullOrWhiteSpace(i.Text))
                                   .Select(i => new JsonNotice { Id = i.Id != Guid.Empty ? i.Id : Guid.NewGuid(), Text = (i.Text ?? "").Trim() })
                                   .ToList();
                    try { Directory.CreateDirectory(AppPaths.DataRoot); } catch { }
                    File.WriteAllText(JsonPath, JsonSerializer.Serialize(arr, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }
                return;
            }

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
