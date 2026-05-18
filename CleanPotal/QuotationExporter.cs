using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ClosedXML.Excel;

namespace CleanPotal
{
    public static class QuotationExporter
    {
        private const int ItemStartRow = 22;
        private const int ItemEndRow   = 44;
        private const int MaxItems     = ItemEndRow - ItemStartRow + 1; // 23

        // ─── Excel 내보내기 ───────────────────────────────────────────────

        public static void ExportToExcel(QuotationModel q, string savePath)
        {
            using var templateStream = GetTemplateStream();
            using var wb = new XLWorkbook(templateStream);
            var ws = wb.Worksheets.First();

            FillWorksheet(ws, q);
            wb.SaveAs(savePath);
        }

        // ─── PDF 내보내기 (Excel Interop 이용, Office 설치 필요) ──────────

        public static void ExportToPdf(QuotationModel q, string pdfPath)
        {
            string tempXlsx = Path.Combine(
                Path.GetTempPath(),
                $"qtemp_{Guid.NewGuid():N}.xlsx");
            try
            {
                ExportToExcel(q, tempXlsx);
                SaveAsPdfViaInterop(tempXlsx, pdfPath);
            }
            finally
            {
                if (File.Exists(tempXlsx)) File.Delete(tempXlsx);
            }
        }

        // ─── 데이터 채우기 ────────────────────────────────────────────────

        private static void FillWorksheet(IXLWorksheet ws, QuotationModel q)
        {
            // 고객사 정보 (왼쪽)
            ws.Cell("E13").Value = q.Attention;
            ws.Cell("E14").Value = q.Company;
            ws.Cell("E16").Value = q.Email;
            ws.Cell("E17").Value = q.Phone;

            // 견적 정보 (오른쪽)
            // K14:M14 merged → master cell K14 에 직접 기록
            ws.Cell("K14").Value = string.IsNullOrWhiteSpace(q.Date) ? ":" : $": {q.Date}";
            // K16:M16 merged → master cell K16
            ws.Cell("K16").Value = string.IsNullOrWhiteSpace(q.Validity) ? ":" : $": {q.Validity}";
            // AETS 담당자 (이름 + 전화번호 조합)
            string managerCell = q.AetsManager;
            if (!string.IsNullOrWhiteSpace(q.AetsPhone))
                managerCell += $"  {q.AetsPhone}";
            ws.Cell("L17").Value = managerCell;
            ws.Cell("L18").Value = q.BusinessNo;

            // 품목 (행 22~44)
            var items = q.LineItems.Take(MaxItems).ToList();
            for (int row = ItemStartRow; row <= ItemEndRow; row++)
            {
                int idx = row - ItemStartRow;
                if (idx < items.Count)
                {
                    var item = items[idx];
                    ws.Cell(row, 1).Value  = item.No;
                    ws.Cell(row, 2).Value  = item.Description;   // B (B-H merged)
                    ws.Cell(row, 9).Value  = item.ListPrice;      // I
                    ws.Cell(row, 10).Value = item.StandardSpec;   // J
                    ws.Cell(row, 11).Value = item.Qty;            // K
                    ws.Cell(row, 12).Value = item.Amount;         // L
                }
                else
                {
                    ws.Cell(row, 1).Value  = "";
                    ws.Cell(row, 2).Value  = "";
                    ws.Cell(row, 9).Value  = "";
                    ws.Cell(row, 10).Value = "";
                    ws.Cell(row, 11).Value = "";
                    ws.Cell(row, 12).Value = "";
                }
            }

            // 합계 (수식을 값으로 교체)
            ws.Cell("K45").Value = items.Sum(x => x.Qty);
            ws.Cell("L45").Value = items.Sum(x => x.Amount);

            // 비고
            ws.Cell("C47").Value = q.Remarks;
        }

        // ─── 템플릿 스트림 ────────────────────────────────────────────────

        private static Stream GetTemplateStream()
        {
            var asm = Assembly.GetExecutingAssembly();
            const string name = "CleanPotal.Resources.quotation_template.xlsx";
            return asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException(
                    "견적서 템플릿 리소스를 찾을 수 없습니다.\n" +
                    "프로젝트 빌드 후 다시 시도하세요.");
        }

        // ─── Excel → PDF (Interop) ────────────────────────────────────────

        private static void SaveAsPdfViaInterop(string xlsxPath, string pdfPath)
        {
            Microsoft.Office.Interop.Excel.Application? app = null;
            Microsoft.Office.Interop.Excel.Workbook?    wb  = null;
            try
            {
                app = new Microsoft.Office.Interop.Excel.Application();
                app.Visible       = false;
                app.DisplayAlerts = false;

                wb = app.Workbooks.Open(
                    xlsxPath,
                    UpdateLinks: false,
                    ReadOnly:    true);

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
                if (wb  != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(wb);
                if (app != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(app);
            }
        }

        // ─── 파일명 안전화 ────────────────────────────────────────────────

        public static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
