using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace CleanPotal
{
    /// <summary>
    /// Open XML SDK 로 템플릿 XML 을 직접 수정 → xlsx 저장.
    /// COM/Interop 레이어를 거치지 않으므로 병합 셀 오류가 없고
    /// 이미지·스타일·수식 구조가 원본 그대로 보존된다.
    /// PDF 변환은 Excel Interop 으로 별도 처리 (Excel 설치 필요).
    /// </summary>
    public static class QuotationExporter
    {
        private const int ItemStartRow = 20;
        private const int ItemEndRow   = 42;
        private const int MaxItems     = ItemEndRow - ItemStartRow + 1; // 23

        // ─── 공개 API ─────────────────────────────────────────────────────

        public static void ExportToExcel(QuotationModel q, string savePath)
        {
            // 템플릿을 그대로 savePath 에 복사 후 XML 만 수정
            byte[] templateBytes = ReadTemplateBytes();
            File.WriteAllBytes(savePath, templateBytes);

            using var doc = SpreadsheetDocument.Open(savePath, isEditable: true);
            var wbPart = doc.WorkbookPart!;
            var sheet  = wbPart.Workbook.Sheets!.Elements<Sheet>().First();
            var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);
            var sd     = wsPart.Worksheet.GetFirstChild<SheetData>()!;

            FillSheetData(sd, q);

            wsPart.Worksheet.Save();

            // 수식 셀을 값으로 교체하면 calcChain이 불일치 → Excel 복구 경고 발생
            // calcChain 파트를 삭제하면 Excel이 열 때 자동 재계산하므로 경고 없음
            if (wbPart.CalculationChainPart != null)
                wbPart.DeletePart(wbPart.CalculationChainPart);
        }

        public static void ExportToPdf(QuotationModel q, string pdfPath)
        {
            string tempXlsx = TempPath("qpdf", ".xlsx");
            try
            {
                ExportToExcel(q, tempXlsx);
                ConvertToPdf(tempXlsx, pdfPath);
            }
            finally { TryDelete(tempXlsx); }
        }

        // ─── 데이터 채우기 ────────────────────────────────────────────────

        private static void FillSheetData(SheetData sd, QuotationModel q)
        {
            static string WithColon(string? v) =>
                string.IsNullOrWhiteSpace(v) ? ":" : $": {v}";

            // 왼쪽
            SetStr(sd, "D12", WithColon(q.QuoteNo));     // D12:G12 병합 마스터
            SetStr(sd, "D13", WithColon(q.RfqNo));       // D13:G13 병합 마스터
            SetStr(sd, "D14", WithColon(q.Company));
            SetStr(sd, "D15", WithColon(q.Attention));
            SetStr(sd, "D16", WithColon(q.Phone));

            // 오른쪽
            SetStr(sd, "K12", WithColon(q.Date));
            SetStr(sd, "K13", WithColon(q.Validity));
            SetStr(sd, "K14", WithColon(q.AetsManager)); // K14:M14 병합 마스터
            SetStr(sd, "K15", WithColon(q.AetsPhone));   // K15:M15 병합 마스터
            SetStr(sd, "K16", WithColon(q.BusinessNo));

            // 품목 행 20~42
            var items = q.LineItems.Take(MaxItems).ToList();
            for (int i = 0; i < MaxItems; i++)
            {
                int row = ItemStartRow + i;
                if (i < items.Count)
                {
                    var item = items[i];
                    SetNum(sd, $"A{row}", item.No);
                    SetStr(sd, $"B{row}", item.Description);   // B~H 병합 마스터
                    SetStr(sd, $"I{row}", item.PartCode);      // Standard Spec 열 = 품목코드
                    SetNum(sd, $"J{row}", (double)item.ListPrice);
                    SetNum(sd, $"K{row}", item.Qty);
                    SetNum(sd, $"L{row}", (double)item.Amount);
                }
                else
                {
                    ClearCell(sd, $"A{row}");
                    ClearCell(sd, $"B{row}");
                    ClearCell(sd, $"I{row}");  // PartCode
                    ClearCell(sd, $"J{row}");  // ListPrice
                    ClearCell(sd, $"K{row}");
                    ClearCell(sd, $"L{row}");
                }
            }

            // 합계 (SUM 수식을 값으로 교체)
            SetNum(sd, "K43", items.Sum(x => x.Qty));
            SetNum(sd, "L43", (double)items.Sum(x => x.Amount));

            // 비고 — 줄바꿈(\n)을 C45, C46, C47... 로 분산 기록 (최대 7줄)
            var remarkLines = (q.Remarks ?? "")
                .Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .ToList();
            for (int i = 0; i < 7; i++)
            {
                string line = i < remarkLines.Count ? remarkLines[i] : "";
                SetStr(sd, $"C{45 + i}", line);
            }
        }

        // ─── 셀 쓰기 헬퍼 ────────────────────────────────────────────────

        private static void SetStr(SheetData sd, string cellRef, string? value)
        {
            var cell = GetOrCreateCell(sd, cellRef);
            cell.RemoveAllChildren();
            cell.DataType  = CellValues.InlineString;
            cell.CellValue = null;
            // 가운데/오른쪽 정렬 스타일이 남아있으면 왼쪽 정렬로 강제 지정
            // (center-aligned 셀이 텍스트가 왼쪽으로 넘쳐 레이블과 겹치는 문제 방지)
            ForceLeftAlign(cell);
            cell.Append(new InlineString(new Text(value ?? "")
                { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
        }

        private static void SetNum(SheetData sd, string cellRef, double value)
        {
            var cell = GetOrCreateCell(sd, cellRef);
            cell.RemoveAllChildren();
            cell.DataType = null; // numeric
            cell.Append(new CellValue(
                value.ToString("G", CultureInfo.InvariantCulture)));
        }

        private static void SetNum(SheetData sd, string cellRef, int value) =>
            SetNum(sd, cellRef, (double)value);

        private static void ForceLeftAlign(Cell cell)
        {
            // StyleIndex를 제거하면 Excel이 기본 서식(왼쪽 정렬)을 적용.
            // 가운데 정렬 셀에 긴 텍스트를 쓸 때 왼쪽으로 넘쳐 레이블과 겹치는 현상 방지.
            cell.StyleIndex = null;
        }

        private static void ClearCell(SheetData sd, string cellRef)
        {
            uint rowIdx = ParseRow(cellRef);
            var row  = sd.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == rowIdx);
            var cell = row?.Elements<Cell>().FirstOrDefault(
                c => c.CellReference?.Value == cellRef);
            if (cell == null) return;
            cell.RemoveAllChildren();
            cell.DataType = null;
        }

        // ─── 셀 조회/생성 ────────────────────────────────────────────────

        private static Cell GetOrCreateCell(SheetData sd, string cellRef)
        {
            uint rowIdx = ParseRow(cellRef);

            // 행 조회/생성 (행 번호 순서 유지)
            Row? row = sd.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == rowIdx);
            if (row == null)
            {
                row = new Row { RowIndex = rowIdx };
                var nextRow = sd.Elements<Row>()
                    .FirstOrDefault(r => r.RowIndex?.Value > rowIdx);
                if (nextRow != null) sd.InsertBefore(row, nextRow);
                else                 sd.Append(row);
            }

            // 셀 조회/생성 (열 순서 유지)
            var cell = row.Elements<Cell>()
                .FirstOrDefault(c => c.CellReference?.Value == cellRef);
            if (cell == null)
            {
                cell = new Cell { CellReference = cellRef };
                var nextCell = row.Elements<Cell>()
                    .FirstOrDefault(c => ColOrder(c.CellReference?.Value) > ColOrder(cellRef));
                if (nextCell != null) row.InsertBefore(cell, nextCell);
                else                  row.Append(cell);
            }
            return cell;
        }

        private static uint ParseRow(string cellRef) =>
            uint.Parse(new string(cellRef.SkipWhile(char.IsLetter).ToArray()));

        // 열 문자(A-Z, AA-AZ ...) → 정수 순서
        private static int ColOrder(string? cellRef)
        {
            if (cellRef == null) return int.MaxValue;
            string col = new string(cellRef.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
            int n = 0;
            foreach (char c in col) n = n * 26 + (c - 'A' + 1);
            return n;
        }

        // ─── PDF 변환 (Excel Interop) ─────────────────────────────────────

        private static void ConvertToPdf(string xlsxPath, string pdfPath)
        {
            Microsoft.Office.Interop.Excel.Application? app = null;
            Microsoft.Office.Interop.Excel.Workbook?    wb  = null;
            try
            {
                app = new Microsoft.Office.Interop.Excel.Application
                {
                    Visible = false, DisplayAlerts = false
                };
                wb = app.Workbooks.Open(xlsxPath, ReadOnly: true);
                wb.ExportAsFixedFormat(
                    Type:             Microsoft.Office.Interop.Excel.XlFixedFormatType.xlTypePDF,
                    Filename:         pdfPath,
                    Quality:          Microsoft.Office.Interop.Excel.XlFixedFormatQuality.xlQualityStandard,
                    IncludeDocProperties: true,
                    IgnorePrintAreas: false,
                    OpenAfterPublish: false);
            }
            finally
            {
                try { wb?.Close(false); } catch { }
                try { app?.Quit();      } catch { }
                if (wb  != null) Marshal.FinalReleaseComObject(wb);
                if (app != null) Marshal.FinalReleaseComObject(app);
            }
        }

        // ─── 유틸 ────────────────────────────────────────────────────────

        private static byte[] ReadTemplateBytes()
        {
            var asm = Assembly.GetExecutingAssembly();
            const string name = "CleanPotal.Resources.quotation_template.xlsx";
            using var stream = asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("견적서 템플릿 리소스를 찾을 수 없습니다.");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static string TempPath(string prefix, string ext) =>
            Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}{ext}");

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
