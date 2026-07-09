using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Dapper;

namespace CleanPotal
{
    /// <summary>
    /// 범용 앱 데이터 저장소(Key-Value blob).
    /// 🚧 배포 스위치(AppPaths.DbMigrationEnabled)로 저장 위치를 라우팅:
    ///    - 플래그 OFF(갭) → 기존 JSON 파일을 직접 읽고/쓴다 = 구버전과 100% 동일 동작(공존 가능)
    ///    - 플래그 ON(배포) → SQLite(dispatch.db) AppData 한 행에 원자적 저장
    /// 연결은 DatabaseHelper.GetConnection()(journal_mode=DELETE)만 사용.
    /// </summary>
    public static class AppDataRepository
    {
        // key → 원본 JSON 파일 경로 (플래그 OFF일 때 이 파일을 직접 사용)
        private static Dictionary<string, string> FileMap() => new()
        {
            ["weekly_reports"]     = Path.Combine(AppPaths.DataRoot, "weekly_reports.json"),
            ["production_meetings"] = Path.Combine(AppPaths.DataRoot, "production_meetings.json"),
            ["vendors"]            = Path.Combine(AppPaths.DataRoot, "vendors.json"),
            ["quotations"]         = Path.Combine(AppPaths.DataRoot, "quotations.json"),
            ["product_master"]     = Path.Combine(AppPaths.DataRoot, "product_master.json"),
            ["quotation_config"]   = Path.Combine(AppPaths.DataRoot, "quotation_config.json"),
            ["recipes"]            = Path.Combine(AppPaths.DataRoot, "recipes.json"),
            ["global_templates"]   = Path.Combine(AppPaths.DataRoot, "global_templates.json"),
            ["buttons"]            = Path.Combine(AppPaths.DataRoot, "buttons.json"),
            ["broken_data"]        = Path.Combine(AppPaths.DataRoot, "broken_data.json"),
        };

        // 🚧 갭(플래그 OFF) 복구: 테스트/이관으로 .migrated 로 바뀐 원본 JSON을 .json 으로 되돌린다.
        //   (플래그 OFF에서 원본이 없어 seed/빈값으로 동작하던 문제 자가 치유 → 구버전과 공존)
        //   앱 시작 시(로그인 이전) 1회 호출. 복구되면 .migrated 는 사라지므로 이후엔 무동작(멱등).
        public static void RestoreMigratedJsonFilesForGap()
        {
            if (AppPaths.DbMigrationEnabled) return;   // 배포(플래그 ON)면 복구하지 않음

            var paths = new List<string>(FileMap().Values)
            {
                Path.Combine(AppPaths.DataRoot, "users.json"),
                Path.Combine(AppPaths.DataRoot, "office_notice.json"),
            };
            foreach (var p in paths)
            {
                try
                {
                    string bak = p + ".migrated";
                    if (!File.Exists(bak)) continue;      // 되돌릴 백업 없음
                    if (File.Exists(p)) File.Delete(p);   // 테스트로 생긴 seed/빈 파일 제거
                    File.Move(bak, p);                    // 원본 복구
                }
                catch { }
            }
        }

        public static void InitializeTables(IDbConnection? shared = null)
        {
            var db = shared ?? DatabaseHelper.GetConnection();
            try
            {
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS AppData (
                        DataKey   TEXT PRIMARY KEY,
                        Json      TEXT NOT NULL DEFAULT '',
                        UpdatedAt TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                    );");

                // 🚧 배포 스위치가 켜졌을 때만 JSON → DB 이관
                if (AppPaths.DbMigrationEnabled)
                    foreach (var kv in FileMap())
                    {
                        MigrateFile(db, kv.Key, kv.Value);
                        // 자가치유: DB에 데이터가 없는데 .migrated 백업이 있으면 그 내용으로 복구.
                        // (배포 전 테스트로 원본이 이미 .migrated 상태여서 배포날 이관이
                        //  건너뛰어진 키를 되살린다 — 예: quotations)
                        RestoreFromMigratedBackupIfEmpty(db, kv.Key, kv.Value);
                    }
            }
            finally
            {
                if (shared == null) db.Dispose();
            }
        }

        // DB 값이 없거나 비어 있을 때만 .migrated 백업에서 복구(멱등·기존 데이터는 절대 덮어쓰지 않음)
        private static void RestoreFromMigratedBackupIfEmpty(IDbConnection db, string key, string path)
        {
            try
            {
                string bak = path + ".migrated";
                if (!File.Exists(bak)) return;

                string? existing = db.ExecuteScalar<string?>(
                    "SELECT Json FROM AppData WHERE DataKey=@k", new { k = key });
                string trimmed = (existing ?? "").Trim();
                bool empty = trimmed.Length == 0 || trimmed == "[]" || trimmed == "{}" || trimmed == "null";
                if (!empty) return;   // 이미 데이터 있음 → 백업 무시

                string json = File.ReadAllText(bak);
                if (string.IsNullOrWhiteSpace(json)) return;
                db.Execute("INSERT OR REPLACE INTO AppData (DataKey, Json, UpdatedAt) VALUES (@k, @j, datetime('now','localtime'))",
                           new { k = key, j = json });
            }
            catch { }
        }

        // 원본 JSON 파일이 있으면 그 내용으로 DB를 '덮어쓰기' 이관 후 파일을 .migrated 로 보존(백업).
        private static void MigrateFile(IDbConnection db, string key, string path)
        {
            try
            {
                if (!File.Exists(path)) return;   // 이미 이관됨(또는 원본 없음)

                string json = File.ReadAllText(path);
                db.Execute("INSERT OR REPLACE INTO AppData (DataKey, Json, UpdatedAt) VALUES (@k, @j, datetime('now','localtime'))",
                           new { k = key, j = json });

                try
                {
                    string bak = path + ".migrated";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
                catch { }
            }
            catch { }
        }

        public static string? Get(string key)
        {
            // 플래그 OFF(갭) → 원본 JSON 파일 직접 읽기(구버전과 동일)
            if (!AppPaths.DbMigrationEnabled)
            {
                try
                {
                    if (FileMap().TryGetValue(key, out var p) && File.Exists(p))
                        return File.ReadAllText(p);
                }
                catch { }
                return null;
            }
            try
            {
                using var db = DatabaseHelper.GetConnection();
                return db.ExecuteScalar<string?>("SELECT Json FROM AppData WHERE DataKey=@k", new { k = key });
            }
            catch { return null; }
        }

        public static void Set(string key, string json)
        {
            // 플래그 OFF(갭) → 원본 JSON 파일에 저장(구버전과 동일)
            if (!AppPaths.DbMigrationEnabled)
            {
                try
                {
                    if (FileMap().TryGetValue(key, out var p))
                    {
                        try { Directory.CreateDirectory(AppPaths.DataRoot); } catch { }
                        File.WriteAllText(p, json ?? "");
                    }
                }
                catch { }
                return;
            }
            try
            {
                using var db = DatabaseHelper.GetConnection();
                db.Execute(@"INSERT INTO AppData (DataKey, Json, UpdatedAt) VALUES (@k, @j, datetime('now','localtime'))
                             ON CONFLICT(DataKey) DO UPDATE SET Json=@j, UpdatedAt=datetime('now','localtime')",
                           new { k = key, j = json ?? "" });
            }
            catch { }
        }
    }
}
