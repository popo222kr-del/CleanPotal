using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;

namespace CleanPotal
{
    public static class DatabaseHelper
    {
        private static readonly string DbPath = Path.Combine(AppPaths.DataRoot, "dispatch.db");
        private static readonly string ConnectionString = $"Data Source={DbPath}";

        public static void InitializeDatabase()
        {
            if (!Directory.Exists(AppPaths.DataRoot)) Directory.CreateDirectory(AppPaths.DataRoot);

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
            }
        }

        public static IDbConnection GetConnection() => new SqliteConnection(ConnectionString);

        // 🔥 배차 데이터 저장 (원하는 적용 날짜 지정)
        public static void InsertDispatch(DispatchItemModel item, DateTime targetDate)
        {
            using (var db = GetConnection())
            {
                string sql = @"INSERT INTO DispatchList (VendorName, OutgoingDetails, IncomingDetails, ManagerName, ContactNumber, FullAddress, Note, CreateDate) 
                               VALUES (@VendorName, @OutgoingDetails, @IncomingDetails, @ManagerName, @ContactNumber, @FullAddress, @Note, @CreateDate)";
                db.Execute(sql, new
                {
                    item.VendorName,
                    item.OutgoingDetails,
                    item.IncomingDetails,
                    ManagerName = item.SelectedManager?.ManagerName,
                    item.ContactNumber,
                    FullAddress = item.SelectedAddress?.FullAddress,
                    item.Note,
                    CreateDate = targetDate.ToString("yyyy-MM-dd") + " 00:00:00"
                });
            }
        }

        // 🔥 배차 데이터 덮어쓰기(수정)
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
                    ManagerName = item.SelectedManager?.ManagerName,
                    item.ContactNumber,
                    FullAddress = item.SelectedAddress?.FullAddress,
                    item.Note,
                    CreateDate = targetDate.ToString("yyyy-MM-dd") + " 00:00:00",
                    item.Id
                });
            }
        }

        // 🔥 배차 개별 항목 삭제
        public static void DeleteDispatch(int id)
        {
            using (var db = GetConnection())
            {
                db.Execute("DELETE FROM DispatchList WHERE Id = @Id", new { Id = id });
            }
        }

        // 🔥 특정 날짜의 배차 이력을 조회하여 모델 리스트로 반환
        public static List<DispatchItemModel> GetDispatchModelsByDate(DateTime targetDate)
        {
            using (var db = GetConnection())
            {
                string sql = "SELECT * FROM DispatchList WHERE date(CreateDate) = date(@TargetDate) ORDER BY Id ASC";
                var rawList = db.Query(sql, new { TargetDate = targetDate.ToString("yyyy-MM-dd") }).ToList();
                var result = new List<DispatchItemModel>();

                foreach (var row in rawList)
                {
                    var model = new DispatchItemModel
                    {
                        Id = (int)row.Id,
                        VendorName = (string)row.VendorName,
                        OutgoingDetails = (string)row.OutgoingDetails,
                        IncomingDetails = (string)row.IncomingDetails,
                        Note = (string)row.Note,
                        ManagerName = (string)row.ManagerName,
                        FullAddress = (string)row.FullAddress
                    };
                    model.LoadComboboxData((string)row.ContactNumber); // DB 값을 콤보박스에 매칭
                    result.Add(model);
                }
                return result;
            }
        }

        // --- 인수인계 로직 (유지) ---
        public static List<HandoverItem> GetAllHandovers() { using (var db = GetConnection()) return db.Query<HandoverItem>("SELECT * FROM HandoverList ORDER BY CreateDate DESC").ToList(); }
        public static void InsertHandover(HandoverItem item) { using (var db = GetConnection()) { string sql = @"INSERT INTO HandoverList (Id, Vendor, Owner, Content, InDate, OutDate, Status, Memo) VALUES (@Id, @Vendor, @Owner, @Content, @InDate, @OutDate, @Status, @Memo)"; db.Execute(sql, new { Id = item.Id.ToString(), item.Vendor, item.Owner, item.Content, item.InDate, item.OutDate, item.Status, item.Memo }); } }
        public static void UpdateHandover(HandoverItem item) { using (var db = GetConnection()) { string sql = @"UPDATE HandoverList SET Vendor = @Vendor, Owner = @Owner, Content = @Content, InDate = @InDate, OutDate = @OutDate, Status = @Status, Memo = @Memo WHERE Id = @Id"; db.Execute(sql, new { item.Vendor, item.Owner, item.Content, item.InDate, item.OutDate, item.Status, item.Memo, Id = item.Id.ToString() }); } }
        public static void DeleteHandover(Guid id) { using (var db = GetConnection()) { string sql = "DELETE FROM HandoverList WHERE Id = @Id"; db.Execute(sql, new { Id = id.ToString() }); } }
    }
}