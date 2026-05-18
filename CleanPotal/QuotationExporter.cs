using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using XL = Microsoft.Office.Interop.Excel;

namespace CleanPotal
{
    /// <summary>
    /// Office Interop으로 템플릿을 열어 값을 채운 뒤 xlsx / pdf로 저장.
    /// 이미지·스타일·수식이 템플릿 그대로 보존된다.
    /// </summary>
    public static class QuotationExporter
    {
        private const int ItemStartRow = 22;
        private const int ItemEndRow   = 44;
        private const int MaxItems     = ItemEndRow - ItemStartRow + 1; // 23

        // ─── 공개 API ─────────────────────────────────────────────────────

        public static void ExportToExcel(QuotationModel q, string savePath)
        {
            RunWithTemplate(q, xlsxPath: savePath, pdfPath: null);
        }

        public static void ExportToPdf(QuotationModel q, string pdfPath)
        {
            string tempXlsx = TempPath("qpdf", ".xlsx");
            try
            {
                RunWithTemplate(q, xlsxPath: tempXlsx, pdfPath: pdfPath);
            }
            finally
            {
                TryDelete(tempXlsx);
            }
        }

        // ─── 핵심 로직 ────────────────────────────────────────────────────

        private static void RunWithTemplate(QuotationModel q, string xlsxPath, string? pdfPath)
        {
            string tmpl = ExtractTemplate();
            XL.Application? app = null;
            XL.Workbook?    wb  = null;
            try
            {
                app = new XL.Application { Visible = false, DisplayAlerts = false };

                wb = app.Workbooks.Open(
                    Filename:    tmpl,
                    UpdateLinks: false,
                    ReadOnly:    false);

                var ws = (XL.Worksheet)wb.Worksheets[1];
                FillWorksheet(ws, q);
                Marshal.ReleaseComObject(ws);

                // Excel 파일 저장
                wb.SaveAs(
                    Filename:   xlsxPath,
                    FileFormat: XL.XlFileFormat.xlOpenXMLWorkbook);

                // PDF도 요청된 경우
                if (pdfPath != null)
                {
                    wb.ExportAsFixedFormat(
                        Type:             XL.XlFixedFormatType.xlTypePDF,
                        Filename:         pdfPath,
                        Quality:          XL.XlFixedFormatQuality.xlQualityStandard,
                        IncludeDocProperties: true,
                        IgnorePrintAreas: false,
                        OpenAfterPublish: false);
                }
            }
            finally
            {
                try { wb?.Close(false); } catch { }
                try { app?.Quit();      } catch { }
                if (wb  != null) Marshal.FinalReleaseComObject(wb);
                if (app != null) Marshal.FinalReleaseComObject(app);
                TryDelete(tmpl);
            }
        }

        // ─── 데이터 채우기 ────────────────────────────────────────────────

        private static void FillWorksheet(XL.Worksheet ws, QuotationModel q)
        {
            // ── 고객사 정보 (왼쪽) ──────────────────────────────────────────
            // Interop으로 열면 인접 셀이 진짜 빈 셀이므로 E열 텍스트가 overflow됨
            SetText(ws, "E13", q.Attention);
            SetText(ws, "E14", q.Company);
            SetText(ws, "E16", q.Email);
            SetText(ws, "E17", q.Phone);

            // ── 견적 정보 (오른쪽) ──────────────────────────────────────────
            // K14:M14, K16:M16 는 템플릿에서 이미 병합된 셀
            SetText(ws, "K14", string.IsNullOrWhiteSpace(q.Date)
                ? ":" : $": {q.Date}");
            SetText(ws, "K16", string.IsNullOrWhiteSpace(q.Validity)
                ? ":" : $": {q.Validity}");

            string manager = q.AetsManager;
            if (!string.IsNullOrWhiteSpace(q.AetsPhone))
                manager += $"  {q.AetsPhone}";
            SetText(ws, "L17", manager);

            // 사업자등록번호: 숫자로 해석되지 않도록 텍스트 서식 지정
            var bizCell = ws.Range["L18"];
            bizCell.NumberFormat = "@";
            bizCell.Value2 = q.BusinessNo;
            bizCell.Font.ColorIndex = 1;
            Marshal.ReleaseComObject(bizCell);

            // ── 품목 (행 22~44) ─────────────────────────────────────────────
            var items = q.LineItems.Take(MaxItems).ToList();
            for (int i = 0; i < MaxItems; i++)
            {
                int row = ItemStartRow + i;
                if (i < items.Count)
                {
                    var item = items[i];
                    SetNum(ws, $"A{row}", item.No);
                    SetText(ws, $"B{row}", item.Description);
                    SetNum(ws, $"I{row}", (double)item.ListPrice);
                    SetText(ws, $"J{row}", item.StandardSpec);
                    SetNum(ws, $"K{row}", item.Qty);
                    SetNum(ws, $"L{row}", (double)item.Amount);
                }
                else
                {
                    Clear(ws, $"A{row}");
                    Clear(ws, $"B{row}");
                    Clear(ws, $"I{row}");
                    Clear(ws, $"J{row}");
                    Clear(ws, $"K{row}");
                    Clear(ws, $"L{row}");
                }
            }

            // ── 합계 ────────────────────────────────────────────────────────
            SetNum(ws, "K45", items.Sum(x => x.Qty));
            SetNum(ws, "L45", (double)items.Sum(x => x.Amount));

            // ── 비고 ────────────────────────────────────────────────────────
            SetText(ws, "C47", q.Remarks);
        }

        // ─── 셀 쓰기 헬퍼 ────────────────────────────────────────────────

        private static void SetText(XL.Worksheet ws, string addr, string val)
        {
            var r = ws.Range[addr];
            r.Value2 = val ?? "";
            r.Font.ColorIndex = 1;
            Marshal.ReleaseComObject(r);
        }

        private static void SetNum(XL.Worksheet ws, string addr, object val)
        {
            var r = ws.Range[addr];
            r.Value2 = val;
            Marshal.ReleaseComObject(r);
        }

        private static void Clear(XL.Worksheet ws, string addr)
        {
            var r = ws.Range[addr];
            r.ClearContents();
            Marshal.ReleaseComObject(r);
        }

        // ─── 템플릿 추출 ─────────────────────────────────────────────────

        private static string ExtractTemplate()
        {
            string path = TempPath("qtmpl", ".xlsx");
            using var src = GetTemplateStream();
            using var dst = File.Create(path);
            src.CopyTo(dst);
            return path;
        }

        private static Stream GetTemplateStream()
        {
            var asm = Assembly.GetExecutingAssembly();
            const string name = "CleanPotal.Resources.quotation_template.xlsx";
            return asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException(
                    "견적서 템플릿 리소스를 찾을 수 없습니다.");
        }

        // ─── 유틸 ────────────────────────────────────────────────────────

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
