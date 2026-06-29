using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CleanPotal.StatusBoard.Models;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Win32;

namespace CleanPotal.StatusBoard.Views
{
    public partial class ProductionBoardView : UserControl
    {
        // Fixed region keys for the packaging grid rows (index 0..4)
        private static readonly string[] Regions = { "기흥/화성", "평택", "A급/부적합", "WOOAM", "기타" };

        // Named TextBox arrays built in constructor — Day[row][col], Night[row][col]
        private TextBox[,] _dayBoxes = null!;
        private TextBox[,] _nightBoxes = null!;

        private readonly ObservableCollection<ProductionTeamMember> _teamA = new();
        private readonly ObservableCollection<ProductionTeamMember> _teamB = new();

        private bool _suppressTotalCalc;

        public ProductionBoardView()
        {
            InitializeComponent();

            InitializeTableTables();

            _dayBoxes = new TextBox[5, 4]
            {
                { DayOuter0, DayInner0, DayBoat0, DayAcc0 },
                { DayOuter1, DayInner1, DayBoat1, DayAcc1 },
                { DayOuter2, DayInner2, DayBoat2, DayAcc2 },
                { DayOuter3, DayInner3, DayBoat3, DayAcc3 },
                { DayOuter4, DayInner4, DayBoat4, DayAcc4 },
            };

            _nightBoxes = new TextBox[5, 4]
            {
                { NightOuter0, NightInner0, NightBoat0, NightAcc0 },
                { NightOuter1, NightInner1, NightBoat1, NightAcc1 },
                { NightOuter2, NightInner2, NightBoat2, NightAcc2 },
                { NightOuter3, NightInner3, NightBoat3, NightAcc3 },
                { NightOuter4, NightInner4, NightBoat4, NightAcc4 },
            };

            DgTeamA.ItemsSource = _teamA;
            DgTeamB.ItemsSource = _teamB;

            DpBoardDate.SelectedDate = DateTime.Today;

            Loaded += (_, _) => LoadData();
        }

        // ─────────────────────────────────────────────
        //  DB Init
        // ─────────────────────────────────────────────
        private void InitializeTableTables()
        {
            using var db = DatabaseHelper.GetConnection();

            db.Execute(@"
                CREATE TABLE IF NOT EXISTS ProductionPackaging (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    BoardDate   TEXT NOT NULL DEFAULT '',
                    Shift       TEXT NOT NULL DEFAULT '',
                    ShiftRound  TEXT NOT NULL DEFAULT '',
                    Region      TEXT NOT NULL DEFAULT '',
                    OuterQty    INTEGER NOT NULL DEFAULT 0,
                    InnerQty    INTEGER NOT NULL DEFAULT 0,
                    BoatQty     INTEGER NOT NULL DEFAULT 0,
                    AccQty      INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt   TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );");

            db.Execute("CREATE INDEX IF NOT EXISTS IX_ProdPkg_Date ON ProductionPackaging(BoardDate);");

            db.Execute(@"
                CREATE TABLE IF NOT EXISTS ProductionTeamMember (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    TeamType    TEXT NOT NULL DEFAULT '',
                    MemberName  TEXT NOT NULL DEFAULT '',
                    Experience  TEXT NOT NULL DEFAULT '',
                    PhoneNumber TEXT NOT NULL DEFAULT '',
                    OrderNo     INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt   TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );");
        }

        // ─────────────────────────────────────────────
        //  Load
        // ─────────────────────────────────────────────
        private void LoadData()
        {
            var dateStr = (DpBoardDate.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");

            _suppressTotalCalc = true;

            // ── Packaging ──
            using (var db = DatabaseHelper.GetConnection())
            {
                var rows = db.Query<ProductionPackaging>(
                    "SELECT * FROM ProductionPackaging WHERE BoardDate = @d", new { d = dateStr }).ToList();

                LoadShift(rows, "주간", _dayBoxes, TbDayRound);
                LoadShift(rows, "야간", _nightBoxes, TbNightRound);
            }

            _suppressTotalCalc = false;
            RecalcTotals(_dayBoxes, DayTotalOuter, DayTotalInner, DayTotalBoat, DayTotalAcc);
            RecalcTotals(_nightBoxes, NightTotalOuter, NightTotalInner, NightTotalBoat, NightTotalAcc);

            // ── Team Members ──
            LoadTeamMembers();
        }

        private void LoadShift(List<ProductionPackaging> allRows, string shift, TextBox[,] boxes, TextBox roundBox)
        {
            var rows = allRows.Where(r => r.Shift == shift).ToList();
            var roundVal = rows.FirstOrDefault()?.ShiftRound ?? "1차";
            roundBox.Text = roundVal;

            for (int r = 0; r < 5; r++)
            {
                var match = rows.FirstOrDefault(x => x.Region == Regions[r]);
                boxes[r, 0].Text = match != null && match.OuterQty != 0 ? match.OuterQty.ToString() : "";
                boxes[r, 1].Text = match != null && match.InnerQty != 0 ? match.InnerQty.ToString() : "";
                boxes[r, 2].Text = match != null && match.BoatQty != 0 ? match.BoatQty.ToString() : "";
                boxes[r, 3].Text = match != null && match.AccQty != 0 ? match.AccQty.ToString() : "";
            }
        }

        private void LoadTeamMembers()
        {
            using var db = DatabaseHelper.GetConnection();
            var all = db.Query<ProductionTeamMember>(
                "SELECT * FROM ProductionTeamMember ORDER BY OrderNo").ToList();

            _teamA.Clear();
            foreach (var m in all.Where(x => x.TeamType == "장팀"))
                _teamA.Add(m);

            _teamB.Clear();
            foreach (var m in all.Where(x => x.TeamType == "팀"))
                _teamB.Add(m);
        }

        // ─────────────────────────────────────────────
        //  Save
        // ─────────────────────────────────────────────
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (SessionManager.BlockGuestEdit()) return;
            try
            {
                SavePackaging();
                SaveTeamMembers();
                MessageBox.Show("저장 완료", "생산 현황판", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SavePackaging()
        {
            var dateStr = (DpBoardDate.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");

            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM ProductionPackaging WHERE BoardDate = @d", new { d = dateStr });

            SaveShift(db, dateStr, "주간", _dayBoxes, TbDayRound.Text.Trim());
            SaveShift(db, dateStr, "야간", _nightBoxes, TbNightRound.Text.Trim());
        }

        private void SaveShift(System.Data.IDbConnection db, string dateStr, string shift, TextBox[,] boxes, string round)
        {
            for (int r = 0; r < 5; r++)
            {
                int outer = ParseInt(boxes[r, 0].Text);
                int inner = ParseInt(boxes[r, 1].Text);
                int boat = ParseInt(boxes[r, 2].Text);
                int acc = ParseInt(boxes[r, 3].Text);

                // Skip completely empty rows
                if (outer == 0 && inner == 0 && boat == 0 && acc == 0)
                    continue;

                db.Execute(@"INSERT INTO ProductionPackaging (BoardDate, Shift, ShiftRound, Region, OuterQty, InnerQty, BoatQty, AccQty)
                             VALUES (@BoardDate, @Shift, @ShiftRound, @Region, @OuterQty, @InnerQty, @BoatQty, @AccQty)",
                    new
                    {
                        BoardDate = dateStr,
                        Shift = shift,
                        ShiftRound = round,
                        Region = Regions[r],
                        OuterQty = outer,
                        InnerQty = inner,
                        BoatQty = boat,
                        AccQty = acc
                    });
            }
        }

        private void SaveTeamMembers()
        {
            using var db = DatabaseHelper.GetConnection();
            db.Execute("DELETE FROM ProductionTeamMember");

            int order = 0;
            foreach (var m in _teamA)
            {
                if (string.IsNullOrWhiteSpace(m.MemberName)) continue;
                m.TeamType = "장팀";
                m.OrderNo = order++;
                m.UpdatedAt = DateTime.Now;
                db.Execute(@"INSERT INTO ProductionTeamMember (TeamType, MemberName, Experience, PhoneNumber, OrderNo, UpdatedAt)
                             VALUES (@TeamType, @MemberName, @Experience, @PhoneNumber, @OrderNo, @UpdatedAt)", m);
            }

            order = 0;
            foreach (var m in _teamB)
            {
                if (string.IsNullOrWhiteSpace(m.MemberName)) continue;
                m.TeamType = "팀";
                m.OrderNo = order++;
                m.UpdatedAt = DateTime.Now;
                db.Execute(@"INSERT INTO ProductionTeamMember (TeamType, MemberName, Experience, PhoneNumber, OrderNo, UpdatedAt)
                             VALUES (@TeamType, @MemberName, @Experience, @PhoneNumber, @OrderNo, @UpdatedAt)", m);
            }
        }

        // ─────────────────────────────────────────────
        //  Auto-calc totals
        // ─────────────────────────────────────────────
        private void PkgCell_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTotalCalc) return;
            RecalcTotals(_dayBoxes, DayTotalOuter, DayTotalInner, DayTotalBoat, DayTotalAcc);
            RecalcTotals(_nightBoxes, NightTotalOuter, NightTotalInner, NightTotalBoat, NightTotalAcc);
        }

        private void RecalcTotals(TextBox[,] boxes, TextBlock tOuter, TextBlock tInner, TextBlock tBoat, TextBlock tAcc)
        {
            int[] sums = new int[4];
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 4; c++)
                    sums[c] += ParseInt(boxes[r, c].Text);

            tOuter.Text = sums[0] > 0 ? sums[0].ToString() : "";
            tInner.Text = sums[1] > 0 ? sums[1].ToString() : "";
            tBoat.Text  = sums[2] > 0 ? sums[2].ToString() : "";
            tAcc.Text   = sums[3] > 0 ? sums[3].ToString() : "";
        }

        // ─────────────────────────────────────────────
        //  Team add/remove
        // ─────────────────────────────────────────────
        private void BtnAddTeamA_Click(object sender, RoutedEventArgs e) =>
            _teamA.Add(new ProductionTeamMember { TeamType = "장팀" });

        private void BtnRemoveTeamA_Click(object sender, RoutedEventArgs e)
        {
            if (DgTeamA.SelectedItem is ProductionTeamMember m)
                _teamA.Remove(m);
            else if (_teamA.Count > 0)
                _teamA.RemoveAt(_teamA.Count - 1);
        }

        private void BtnAddTeamB_Click(object sender, RoutedEventArgs e) =>
            _teamB.Add(new ProductionTeamMember { TeamType = "팀" });

        private void BtnRemoveTeamB_Click(object sender, RoutedEventArgs e)
        {
            if (DgTeamB.SelectedItem is ProductionTeamMember m)
                _teamB.Remove(m);
            else if (_teamB.Count > 0)
                _teamB.RemoveAt(_teamB.Count - 1);
        }

        // ─────────────────────────────────────────────
        //  Date changed
        // ─────────────────────────────────────────────
        private void DpBoardDate_SelectedDateChanged(object? sender, SelectionChangedEventArgs e) => LoadData();

        // ─────────────────────────────────────────────
        //  Print (Excel export, A4 portrait)
        // ─────────────────────────────────────────────
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            var dateStr = (DpBoardDate.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            var dlg = new SaveFileDialog
            {
                Title = "생산 현황판 엑셀 내보내기 (A4 세로)",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"생산현황판_{dateStr.Replace("-", "")}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("생산 현황판");

                // ── A4 portrait page setup ──
                ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
                ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
                ws.PageSetup.Margins.Top = 0.3;
                ws.PageSetup.Margins.Bottom = 0.3;
                ws.PageSetup.Margins.Left = 0.3;
                ws.PageSetup.Margins.Right = 0.3;
                ws.PageSetup.Margins.Header = 0;
                ws.PageSetup.Margins.Footer = 0;
                ws.PageSetup.CenterHorizontally = true;
                ws.PageSetup.FitToPages(1, 1);

                int row = 1;

                // ── Title ──
                ws.Cell(row, 1).Value = $"생산 현황판  ({dateStr})";
                ws.Range(row, 1, row, 10).Merge();
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 16;
                ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Row(row).Height = 30;
                row += 2;

                // ── 주간 포장 수량 ──
                row = WritePackagingTable(ws, row, "주간 포장 수량", TbDayRound.Text, _dayBoxes,
                    DayTotalOuter, DayTotalInner, DayTotalBoat, DayTotalAcc);
                row++;

                // ── 야간 포장 수량 ──
                row = WritePackagingTable(ws, row, "야간 포장 수량", TbNightRound.Text, _nightBoxes,
                    NightTotalOuter, NightTotalInner, NightTotalBoat, NightTotalAcc);
                row++;

                // ── 장 팀 ──
                row = WriteTeamTable(ws, row, "장 팀", _teamA);
                row++;

                // ── 팀 ──
                row = WriteTeamTable(ws, row, "팀", _teamB);

                // Auto column widths
                ws.Columns().AdjustToContents();

                wb.SaveAs(dlg.FileName);
                MessageBox.Show("엑셀 파일이 저장되었습니다.", "인쇄", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 내보내기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private int WritePackagingTable(IXLWorksheet ws, int startRow, string title, string round,
            TextBox[,] boxes, TextBlock tOuter, TextBlock tInner, TextBlock tBoat, TextBlock tAcc)
        {
            int row = startRow;
            string[] cols = { "구분", "OUTER", "INNER", "BOAT", "ACC" };

            // Section header
            ws.Cell(row, 1).Value = $"{title} ({round})";
            ws.Range(row, 1, row, 5).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 12;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#DBEAFE");
            ws.Row(row).Height = 22;
            row++;

            // Column headers
            for (int c = 0; c < cols.Length; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = cols[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#334155");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            row++;

            // Data rows
            for (int r = 0; r < 5; r++)
            {
                ws.Cell(row, 1).Value = Regions[r];
                ws.Cell(row, 1).Style.Font.Bold = true;
                for (int c = 0; c < 4; c++)
                {
                    var val = ParseInt(boxes[r, c].Text);
                    if (val != 0) ws.Cell(row, c + 2).Value = val;
                    ws.Cell(row, c + 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                row++;
            }

            // Total row
            ws.Cell(row, 1).Value = "전산:";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#2563EB");
            TextBlock[] totals = { tOuter, tInner, tBoat, tAcc };
            for (int c = 0; c < 4; c++)
            {
                var v = ParseInt(totals[c].Text);
                if (v != 0) ws.Cell(row, c + 2).Value = v;
                ws.Cell(row, c + 2).Style.Font.Bold = true;
                ws.Cell(row, c + 2).Style.Font.FontColor = XLColor.FromHtml("#2563EB");
                ws.Cell(row, c + 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // Border around the table
            var tableRange = ws.Range(startRow, 1, row, 5);
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            return row + 1;
        }

        private int WriteTeamTable(IXLWorksheet ws, int startRow, string title,
            ObservableCollection<ProductionTeamMember> members)
        {
            int row = startRow;
            string[] cols = { "이름", "경력", "연락처" };

            // Section header
            ws.Cell(row, 1).Value = title;
            ws.Range(row, 1, row, 3).Merge();
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 12;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0F172A");
            ws.Row(row).Height = 22;
            row++;

            // Column headers
            for (int c = 0; c < cols.Length; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = cols[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            row++;

            // Data rows
            foreach (var m in members)
            {
                if (string.IsNullOrWhiteSpace(m.MemberName)) continue;
                ws.Cell(row, 1).Value = m.MemberName;
                ws.Cell(row, 2).Value = m.Experience;
                ws.Cell(row, 3).Value = m.PhoneNumber;
                row++;
            }

            if (members.Count == 0) row++; // at least one blank row

            // Border
            var tableRange = ws.Range(startRow, 1, row - 1, 3);
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            return row;
        }

        // ─────────────────────────────────────────────
        //  Utility
        // ─────────────────────────────────────────────
        private static int ParseInt(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return int.TryParse(text.Trim(), out int v) ? v : 0;
        }
    }
}
