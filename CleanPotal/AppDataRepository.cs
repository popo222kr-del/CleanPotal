using System;
using System.Data;
using System.IO;
using Dapper;

namespace CleanPotal
{
    /// <summary>
    /// 범용 앱 데이터 저장소(Key-Value blob). 중첩 구조라 정규화가 과한 JSON 문서를
    /// 파일 대신 SQLite(dispatch.db) 한 행에 원자적으로 저장한다.
    /// (NAS 공유 파일의 부분쓰기 손상·경쟁을 피하고, busy_timeout 으로 쓰기 직렬화)
    /// 연결은 DatabaseHelper.GetConnection()(journal_mode=DELETE)만 사용.
    /// </summary>
    public static class AppDataRepository
    {
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

                // 파일 기반 JSON → AppData 이관. 🚧 배포 스위치(플래그 파일)가 있을 때만 실행.
                if (AppPaths.DbMigrationEnabled)
                {
                    MigrateFile(db, "weekly_reports", Path.Combine(AppPaths.DataRoot, "weekly_reports.json"));
                    MigrateFile(db, "production_meetings", Path.Combine(AppPaths.DataRoot, "production_meetings.json"));
                    MigrateFile(db, "vendors", Path.Combine(AppPaths.DataRoot, "vendors.json"));
                    MigrateFile(db, "quotations", Path.Combine(AppPaths.DataRoot, "quotations.json"));
                    MigrateFile(db, "product_master", Path.Combine(AppPaths.DataRoot, "product_master.json"));
                    MigrateFile(db, "quotation_config", Path.Combine(AppPaths.DataRoot, "quotation_config.json"));
                    MigrateFile(db, "recipes", Path.Combine(AppPaths.DataRoot, "recipes.json"));
                    MigrateFile(db, "global_templates", Path.Combine(AppPaths.DataRoot, "global_templates.json"));
                    MigrateFile(db, "buttons", Path.Combine(AppPaths.DataRoot, "buttons.json"));
                    MigrateFile(db, "broken_data", Path.Combine(AppPaths.DataRoot, "broken_data.json"));
                }
            }
            finally
            {
                if (shared == null) db.Dispose();
            }
        }

        // 원본 JSON 파일이 있으면 그 내용으로 DB를 '덮어쓰기' 이관 후 파일을 .migrated 로 보존(백업).
        //   · JSON 존재 여부로 판단 → 배포 시점의 최신 JSON(갭 중 수정분 포함)이 DB에 반영됨
        //   · 이관 완료 후 JSON은 .migrated 로 바뀌므로 다음 실행부터는 건너뜀(멱등)
        private static void MigrateFile(IDbConnection db, string key, string path)
        {
            try
            {
                if (!File.Exists(path)) return;   // 이미 이관됨(또는 원본 없음) → 건너뜀

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
            using var db = DatabaseHelper.GetConnection();
            return db.ExecuteScalar<string?>("SELECT Json FROM AppData WHERE DataKey=@k", new { k = key });
        }

        public static void Set(string key, string json)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute(@"INSERT INTO AppData (DataKey, Json, UpdatedAt) VALUES (@k, @j, datetime('now','localtime'))
                         ON CONFLICT(DataKey) DO UPDATE SET Json=@j, UpdatedAt=datetime('now','localtime')",
                       new { k = key, j = json ?? "" });
        }
    }
}
