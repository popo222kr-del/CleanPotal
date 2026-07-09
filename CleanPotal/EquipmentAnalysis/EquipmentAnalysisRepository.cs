using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;

namespace CleanPotal.EquipmentAnalysis
{
    // ICP-MS 설비 분석 데이터 1행 (설비별 금속 오염도 ppb)
    public class EquipmentAnalysisRow
    {
        public long Id { get; set; }
        public string ProcessType { get; set; } = "";   // 시트 구분: DIP / US
        public string EqId { get; set; } = "";           // 설비 ID
        public string BathGb { get; set; } = "";         // 약액: S2/HF/HNO3/DIW 등
        public string Category { get; set; } = "";       // DIP=Use_CNT / US=공정 (C열 원본값)
        public string Unit { get; set; } = "ppb";
        public string AnalysisDate { get; set; } = "";   // yyyy-MM-dd
        // 원소별 값
        public Dictionary<string, double> Elements { get; set; } = new();
    }

    public static class EquipmentAnalysisRepository
    {
        // 원소 컬럼(엑셀 헤더와 동일한 순서). 컬럼 생성/입출력에 공통 사용.
        public static readonly string[] ElementCols =
            { "Li","Na","Mg","Al","K","Ca","Ti","Cr","Mn","Fe","Co","Ni","Cu","Zn","Ge","As","Cd","In","Ba","Ta","W","Pb" };

        public static void InitializeTables(IDbConnection? shared = null)
        {
            var db = shared ?? DatabaseHelper.GetConnection();
            try
            {
                string elemCols = string.Join(",\n", ElementCols.Select(e => $"        \"{e}\" REAL NOT NULL DEFAULT 0"));
                db.Execute($@"
                    CREATE TABLE IF NOT EXISTS EquipmentAnalysis (
                        Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                        ProcessType  TEXT NOT NULL DEFAULT '',
                        EqId         TEXT NOT NULL DEFAULT '',
                        BathGb       TEXT NOT NULL DEFAULT '',
                        Category     TEXT NOT NULL DEFAULT '',
                        Unit         TEXT NOT NULL DEFAULT 'ppb',
                        AnalysisDate TEXT NOT NULL DEFAULT '',
{elemCols}
                    );");
                // 재업로드 중복 방지(설비+약액+구분+분석일+공정타입 동일하면 무시)
                db.Execute(@"CREATE UNIQUE INDEX IF NOT EXISTS UX_EqAnalysis
                             ON EquipmentAnalysis(ProcessType, EqId, BathGb, Category, AnalysisDate);");

                // 측정 현황 특이사항(설비별·날짜별)
                db.Execute(@"
                    CREATE TABLE IF NOT EXISTS EquipmentCheckNote (
                        EqId      TEXT NOT NULL DEFAULT '',
                        CheckDate TEXT NOT NULL DEFAULT '',
                        Note      TEXT NOT NULL DEFAULT '',
                        UpdatedAt TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                        PRIMARY KEY (EqId, CheckDate)
                    );");
            }
            finally { if (shared == null) db.Dispose(); }
        }

        // 특정 날짜의 설비별 특이사항 (EqId → Note)
        public static Dictionary<string, string> GetCheckNotes(string checkDate)
        {
            using var db = DatabaseHelper.GetConnection();
            var rows = db.Query("SELECT EqId, Note FROM EquipmentCheckNote WHERE CheckDate = @d", new { d = checkDate ?? "" });
            var map = new Dictionary<string, string>();
            foreach (var r in rows)
            {
                var d = (IDictionary<string, object>)r;
                string eq = (d["EqId"] as string) ?? "";
                if (!string.IsNullOrEmpty(eq)) map[eq] = (d["Note"] as string) ?? "";
            }
            return map;
        }

        // 설비별·날짜별 특이사항 저장(빈 값이면 삭제)
        public static void UpsertCheckNote(string eqId, string checkDate, string note)
        {
            using var db = DatabaseHelper.GetConnection();
            if (string.IsNullOrWhiteSpace(note))
            {
                db.Execute("DELETE FROM EquipmentCheckNote WHERE EqId=@e AND CheckDate=@d",
                           new { e = eqId ?? "", d = checkDate ?? "" });
                return;
            }
            db.Execute(@"INSERT INTO EquipmentCheckNote (EqId, CheckDate, Note, UpdatedAt)
                         VALUES (@e, @d, @n, datetime('now','localtime'))
                         ON CONFLICT(EqId, CheckDate) DO UPDATE SET Note=@n, UpdatedAt=datetime('now','localtime');",
                       new { e = eqId ?? "", d = checkDate ?? "", n = note });
        }

        // 누적 삽입(중복은 무시). 삽입된 신규 행 수 반환.
        public static int InsertMany(IEnumerable<EquipmentAnalysisRow> rows)
        {
            using var db = DatabaseHelper.GetConnection();
            using var tx = db.BeginTransaction();
            int added = 0;
            string cols = "ProcessType,EqId,BathGb,Category,Unit,AnalysisDate," + string.Join(",", ElementCols.Select(e => $"\"{e}\""));
            string vals = "@ProcessType,@EqId,@BathGb,@Category,@Unit,@AnalysisDate," + string.Join(",", ElementCols.Select(e => "@" + e));
            string sql = $"INSERT OR IGNORE INTO EquipmentAnalysis ({cols}) VALUES ({vals});";
            foreach (var r in rows)
            {
                var p = new DynamicParameters();
                p.Add("ProcessType", r.ProcessType ?? "");
                p.Add("EqId", r.EqId ?? "");
                p.Add("BathGb", r.BathGb ?? "");
                p.Add("Category", r.Category ?? "");
                p.Add("Unit", string.IsNullOrWhiteSpace(r.Unit) ? "ppb" : r.Unit);
                p.Add("AnalysisDate", r.AnalysisDate ?? "");
                foreach (var e in ElementCols)
                    p.Add(e, r.Elements != null && r.Elements.TryGetValue(e, out var v) ? v : 0.0);
                added += db.Execute(sql, p, tx);
            }
            tx.Commit();
            return added;
        }

        public static List<EquipmentAnalysisRow> GetAll()
        {
            using var db = DatabaseHelper.GetConnection();
            var rows = db.Query("SELECT * FROM EquipmentAnalysis ORDER BY AnalysisDate, EqId");
            var list = new List<EquipmentAnalysisRow>();
            foreach (var r in rows)
            {
                var d = (IDictionary<string, object>)r;
                var row = new EquipmentAnalysisRow
                {
                    Id = Convert.ToInt64(d["Id"]),
                    ProcessType = (d["ProcessType"] as string) ?? "",
                    EqId = (d["EqId"] as string) ?? "",
                    BathGb = (d["BathGb"] as string) ?? "",
                    Category = (d["Category"] as string) ?? "",
                    Unit = (d["Unit"] as string) ?? "ppb",
                    AnalysisDate = (d["AnalysisDate"] as string) ?? "",
                };
                foreach (var e in ElementCols)
                    row.Elements[e] = d.TryGetValue(e, out var v) && v != null ? Convert.ToDouble(v) : 0.0;
                list.Add(row);
            }
            return list;
        }

        public static void DeleteAll()
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM EquipmentAnalysis;");
        }

        public static int Count()
        {
            using var db = DatabaseHelper.GetConnection();
            return db.ExecuteScalar<int>("SELECT COUNT(*) FROM EquipmentAnalysis;");
        }
    }
}
