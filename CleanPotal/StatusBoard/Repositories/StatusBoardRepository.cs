using System;
using System.Collections.Generic;
using System.Linq;
using CleanPotal.StatusBoard.Models;
using Dapper;

namespace CleanPotal.StatusBoard.Repositories
{
    public static class StatusBoardRepository
    {
        // ===================================================================
        // 테이블 초기화
        // ===================================================================
        public static void InitializeTables()
        {
            using var db = DatabaseHelper.GetConnection();

            // 1. MaterialLogisticsBoard (자재물류 일정 현황 - 천안사업장)
            db.Execute(@"
                CREATE TABLE IF NOT EXISTS MaterialLogisticsBoard (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    BoardDate       TEXT NOT NULL,
                    PersonName      TEXT NOT NULL DEFAULT '',
                    AmDestination   TEXT NOT NULL DEFAULT '',
                    AmVehicle       TEXT NOT NULL DEFAULT '',
                    PmDestination   TEXT NOT NULL DEFAULT '',
                    PmVehicle       TEXT NOT NULL DEFAULT '',
                    Memo            TEXT NOT NULL DEFAULT '',
                    OrderNo         INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt       TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_MatLog_Date ON MaterialLogisticsBoard(BoardDate);");

            // 2-a. ProductionPackaging (포장 수량)
            db.Execute(@"
                CREATE TABLE IF NOT EXISTS ProductionPackaging (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    BoardDate       TEXT NOT NULL,
                    Shift           TEXT NOT NULL DEFAULT '',
                    ShiftRound      TEXT NOT NULL DEFAULT '',
                    Region          TEXT NOT NULL DEFAULT '',
                    OuterQty        INTEGER NOT NULL DEFAULT 0,
                    InnerQty        INTEGER NOT NULL DEFAULT 0,
                    BoatQty         INTEGER NOT NULL DEFAULT 0,
                    AccQty          INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt       TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_ProdPkg_Date ON ProductionPackaging(BoardDate);");

            // 2-b. ProductionTeamMember (팀 인원)
            db.Execute(@"
                CREATE TABLE IF NOT EXISTS ProductionTeamMember (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    TeamType        TEXT NOT NULL DEFAULT '',
                    MemberName      TEXT NOT NULL DEFAULT '',
                    Experience      TEXT NOT NULL DEFAULT '',
                    PhoneNumber     TEXT NOT NULL DEFAULT '',
                    OrderNo         INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt       TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );");

            // 3-a. DongtanDispatch (배차표)
            db.Execute(@"
                CREATE TABLE IF NOT EXISTS DongtanDispatch (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    BoardDate       TEXT NOT NULL,
                    DriverName      TEXT NOT NULL DEFAULT '',
                    VehicleInfo     TEXT NOT NULL DEFAULT '',
                    AmRoute         TEXT NOT NULL DEFAULT '',
                    PmRoute         TEXT NOT NULL DEFAULT '',
                    RouteGroup      TEXT NOT NULL DEFAULT '',
                    OrderNo         INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt       TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_DtDisp_Date ON DongtanDispatch(BoardDate);");

            // 3-b. DongtanQuantity (반입/반출 물량)
            db.Execute(@"
                CREATE TABLE IF NOT EXISTS DongtanQuantity (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    BoardDate       TEXT NOT NULL,
                    TimeSlot        TEXT NOT NULL DEFAULT '',
                    Direction       TEXT NOT NULL DEFAULT '',
                    Region          TEXT NOT NULL DEFAULT '',
                    OuterQty        INTEGER NOT NULL DEFAULT 0,
                    InnerQty        INTEGER NOT NULL DEFAULT 0,
                    BoatQty         INTEGER NOT NULL DEFAULT 0,
                    AccQty          INTEGER NOT NULL DEFAULT 0,
                    EtcQty          INTEGER NOT NULL DEFAULT 0,
                    EtcMemo         TEXT NOT NULL DEFAULT '',
                    UpdatedAt       TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );");
            db.Execute("CREATE INDEX IF NOT EXISTS IX_DtQty_Date ON DongtanQuantity(BoardDate);");
        }

        // ===================================================================
        // 1. MaterialLogisticsBoard CRUD
        // ===================================================================

        public static List<MaterialLogisticsRow> GetAllMaterialLogistics(string boardDate)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<MaterialLogisticsRow>(
                "SELECT * FROM MaterialLogisticsBoard WHERE BoardDate = @BoardDate ORDER BY OrderNo",
                new { BoardDate = boardDate }).ToList();
        }

        public static List<string> GetMaterialLogisticsDates()
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<string>(
                "SELECT DISTINCT BoardDate FROM MaterialLogisticsBoard ORDER BY BoardDate DESC").ToList();
        }

        public static long InsertMaterialLogistics(MaterialLogisticsRow row)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.ExecuteScalar<long>(@"
                INSERT INTO MaterialLogisticsBoard
                    (BoardDate, PersonName, AmDestination, AmVehicle, PmDestination, PmVehicle, Memo, OrderNo, UpdatedAt)
                VALUES
                    (@BoardDate, @PersonName, @AmDestination, @AmVehicle, @PmDestination, @PmVehicle, @Memo, @OrderNo, @UpdatedAt);
                SELECT last_insert_rowid();", ToParam(row));
        }

        public static void UpdateMaterialLogistics(MaterialLogisticsRow row)
        {
            row.UpdatedAt = DateTime.Now;
            using var db = DatabaseHelper.GetConnection();
            db.Execute(@"
                UPDATE MaterialLogisticsBoard
                SET BoardDate=@BoardDate, PersonName=@PersonName, AmDestination=@AmDestination, AmVehicle=@AmVehicle,
                    PmDestination=@PmDestination, PmVehicle=@PmVehicle, Memo=@Memo, OrderNo=@OrderNo, UpdatedAt=@UpdatedAt
                WHERE Id=@Id", ToParam(row));
        }

        public static void DeleteMaterialLogistics(long id)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM MaterialLogisticsBoard WHERE Id = @Id", new { Id = id });
        }

        private static object ToParam(MaterialLogisticsRow r) => new
        {
            r.Id, r.BoardDate, r.PersonName, r.AmDestination, r.AmVehicle,
            r.PmDestination, r.PmVehicle, r.Memo, r.OrderNo,
            UpdatedAt = r.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        };

        // ===================================================================
        // 2-a. ProductionPackaging CRUD
        // ===================================================================

        public static List<ProductionPackaging> GetAllProductionPackaging(string boardDate)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<ProductionPackaging>(
                "SELECT * FROM ProductionPackaging WHERE BoardDate = @BoardDate ORDER BY Shift, ShiftRound, Region",
                new { BoardDate = boardDate }).ToList();
        }

        public static List<string> GetProductionPackagingDates()
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<string>(
                "SELECT DISTINCT BoardDate FROM ProductionPackaging ORDER BY BoardDate DESC").ToList();
        }

        public static long InsertProductionPackaging(ProductionPackaging row)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.ExecuteScalar<long>(@"
                INSERT INTO ProductionPackaging
                    (BoardDate, Shift, ShiftRound, Region, OuterQty, InnerQty, BoatQty, AccQty, UpdatedAt)
                VALUES
                    (@BoardDate, @Shift, @ShiftRound, @Region, @OuterQty, @InnerQty, @BoatQty, @AccQty, @UpdatedAt);
                SELECT last_insert_rowid();", ToParam(row));
        }

        public static void UpdateProductionPackaging(ProductionPackaging row)
        {
            row.UpdatedAt = DateTime.Now;
            using var db = DatabaseHelper.GetConnection();
            db.Execute(@"
                UPDATE ProductionPackaging
                SET BoardDate=@BoardDate, Shift=@Shift, ShiftRound=@ShiftRound, Region=@Region,
                    OuterQty=@OuterQty, InnerQty=@InnerQty, BoatQty=@BoatQty, AccQty=@AccQty, UpdatedAt=@UpdatedAt
                WHERE Id=@Id", ToParam(row));
        }

        public static void DeleteProductionPackaging(long id)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM ProductionPackaging WHERE Id = @Id", new { Id = id });
        }

        private static object ToParam(ProductionPackaging r) => new
        {
            r.Id, r.BoardDate, r.Shift, r.ShiftRound, r.Region,
            r.OuterQty, r.InnerQty, r.BoatQty, r.AccQty,
            UpdatedAt = r.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        };

        // ===================================================================
        // 2-b. ProductionTeamMember CRUD
        // ===================================================================

        public static List<ProductionTeamMember> GetAllProductionTeamMembers()
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<ProductionTeamMember>(
                "SELECT * FROM ProductionTeamMember ORDER BY TeamType, OrderNo").ToList();
        }

        public static long InsertProductionTeamMember(ProductionTeamMember row)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.ExecuteScalar<long>(@"
                INSERT INTO ProductionTeamMember
                    (TeamType, MemberName, Experience, PhoneNumber, OrderNo, UpdatedAt)
                VALUES
                    (@TeamType, @MemberName, @Experience, @PhoneNumber, @OrderNo, @UpdatedAt);
                SELECT last_insert_rowid();", ToParam(row));
        }

        public static void UpdateProductionTeamMember(ProductionTeamMember row)
        {
            row.UpdatedAt = DateTime.Now;
            using var db = DatabaseHelper.GetConnection();
            db.Execute(@"
                UPDATE ProductionTeamMember
                SET TeamType=@TeamType, MemberName=@MemberName, Experience=@Experience,
                    PhoneNumber=@PhoneNumber, OrderNo=@OrderNo, UpdatedAt=@UpdatedAt
                WHERE Id=@Id", ToParam(row));
        }

        public static void DeleteProductionTeamMember(long id)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM ProductionTeamMember WHERE Id = @Id", new { Id = id });
        }

        private static object ToParam(ProductionTeamMember r) => new
        {
            r.Id, r.TeamType, r.MemberName, r.Experience, r.PhoneNumber, r.OrderNo,
            UpdatedAt = r.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        };

        // ===================================================================
        // 3-a. DongtanDispatch CRUD
        // ===================================================================

        public static List<DongtanDispatch> GetAllDongtanDispatch(string boardDate)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<DongtanDispatch>(
                "SELECT * FROM DongtanDispatch WHERE BoardDate = @BoardDate ORDER BY OrderNo",
                new { BoardDate = boardDate }).ToList();
        }

        public static List<string> GetDongtanDispatchDates()
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<string>(
                "SELECT DISTINCT BoardDate FROM DongtanDispatch ORDER BY BoardDate DESC").ToList();
        }

        public static long InsertDongtanDispatch(DongtanDispatch row)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.ExecuteScalar<long>(@"
                INSERT INTO DongtanDispatch
                    (BoardDate, DriverName, VehicleInfo, AmRoute, PmRoute, RouteGroup, OrderNo, UpdatedAt)
                VALUES
                    (@BoardDate, @DriverName, @VehicleInfo, @AmRoute, @PmRoute, @RouteGroup, @OrderNo, @UpdatedAt);
                SELECT last_insert_rowid();", ToParam(row));
        }

        public static void UpdateDongtanDispatch(DongtanDispatch row)
        {
            row.UpdatedAt = DateTime.Now;
            using var db = DatabaseHelper.GetConnection();
            db.Execute(@"
                UPDATE DongtanDispatch
                SET BoardDate=@BoardDate, DriverName=@DriverName, VehicleInfo=@VehicleInfo,
                    AmRoute=@AmRoute, PmRoute=@PmRoute, RouteGroup=@RouteGroup, OrderNo=@OrderNo, UpdatedAt=@UpdatedAt
                WHERE Id=@Id", ToParam(row));
        }

        public static void DeleteDongtanDispatch(long id)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM DongtanDispatch WHERE Id = @Id", new { Id = id });
        }

        private static object ToParam(DongtanDispatch r) => new
        {
            r.Id, r.BoardDate, r.DriverName, r.VehicleInfo,
            r.AmRoute, r.PmRoute, r.RouteGroup, r.OrderNo,
            UpdatedAt = r.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        };

        // ===================================================================
        // 3-b. DongtanQuantity CRUD
        // ===================================================================

        public static List<DongtanQuantity> GetAllDongtanQuantity(string boardDate)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<DongtanQuantity>(
                "SELECT * FROM DongtanQuantity WHERE BoardDate = @BoardDate ORDER BY TimeSlot, Direction, Region",
                new { BoardDate = boardDate }).ToList();
        }

        public static List<string> GetDongtanQuantityDates()
        {
            using var db = DatabaseHelper.GetConnection();
            return db.Query<string>(
                "SELECT DISTINCT BoardDate FROM DongtanQuantity ORDER BY BoardDate DESC").ToList();
        }

        public static long InsertDongtanQuantity(DongtanQuantity row)
        {
            using var db = DatabaseHelper.GetConnection();
            return db.ExecuteScalar<long>(@"
                INSERT INTO DongtanQuantity
                    (BoardDate, TimeSlot, Direction, Region, OuterQty, InnerQty, BoatQty, AccQty, EtcQty, EtcMemo, UpdatedAt)
                VALUES
                    (@BoardDate, @TimeSlot, @Direction, @Region, @OuterQty, @InnerQty, @BoatQty, @AccQty, @EtcQty, @EtcMemo, @UpdatedAt);
                SELECT last_insert_rowid();", ToParam(row));
        }

        public static void UpdateDongtanQuantity(DongtanQuantity row)
        {
            row.UpdatedAt = DateTime.Now;
            using var db = DatabaseHelper.GetConnection();
            db.Execute(@"
                UPDATE DongtanQuantity
                SET BoardDate=@BoardDate, TimeSlot=@TimeSlot, Direction=@Direction, Region=@Region,
                    OuterQty=@OuterQty, InnerQty=@InnerQty, BoatQty=@BoatQty, AccQty=@AccQty,
                    EtcQty=@EtcQty, EtcMemo=@EtcMemo, UpdatedAt=@UpdatedAt
                WHERE Id=@Id", ToParam(row));
        }

        public static void DeleteDongtanQuantity(long id)
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM DongtanQuantity WHERE Id = @Id", new { Id = id });
        }

        private static object ToParam(DongtanQuantity r) => new
        {
            r.Id, r.BoardDate, r.TimeSlot, r.Direction, r.Region,
            r.OuterQty, r.InnerQty, r.BoatQty, r.AccQty, r.EtcQty, r.EtcMemo,
            UpdatedAt = r.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }
}
