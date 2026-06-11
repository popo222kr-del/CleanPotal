using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using UglyToad.PdfPig;

namespace CleanPotal
{
    public class DocSearchItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FileName { get; set; } = "";
        public string FileType { get; set; } = "";
        public string ExtractedText { get; set; } = "";
        public DateTime UploadedAt { get; set; } = DateTime.Now;

        [System.Text.Json.Serialization.JsonIgnore]
        public string SummaryInfo => $"{FileType.ToUpperInvariant()} · {ExtractedText.Length:N0}자 · {UploadedAt:yyyy-MM-dd}";
    }

    public class SearchResultItem
    {
        public string FileName { get; set; } = "";
        public string Snippet { get; set; } = "";
        public int MatchCount { get; set; }

        public string MatchCountLabel => $"{MatchCount}건 일치";
    }

    public partial class DocSearchView : UserControl
    {
        private readonly ObservableCollection<DocSearchItem> _documents = new();

        private const int SnippetContext = 30;
        private const int MaxSnippetsPerDoc = 3;

        private static string DocStoreRoot => Path.Combine(AppPaths.DataRoot, "doc_search");
        private static string DocumentsFilePath => Path.Combine(DocStoreRoot, "documents.json");

        public DocSearchView()
        {
            InitializeComponent();

            DocListControl.ItemsSource = _documents;

            Loaded += DocSearchView_Loaded;
        }

        private void DocSearchView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDocuments();
            UpdateDocCount();
        }

        public void TryRefresh()
        {
            try { LoadDocuments(); UpdateDocCount(); } catch { }
        }

        // ============================================================
        // 문서 목록 저장/불러오기
        // ============================================================
        private void LoadDocuments()
        {
            _documents.Clear();
            if (!File.Exists(DocumentsFilePath)) return;
            try
            {
                string json = File.ReadAllText(DocumentsFilePath);
                var items = JsonSerializer.Deserialize<List<DocSearchItem>>(json) ?? new List<DocSearchItem>();
                foreach (var item in items) _documents.Add(item);
            }
            catch { }
        }

        private void SaveDocuments()
        {
            try
            {
                Directory.CreateDirectory(DocStoreRoot);
                string json = JsonSerializer.Serialize(_documents.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(DocumentsFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"문서 목록 저장 중 오류가 발생했습니다.\n{ex.Message}", "문서 검색", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateDocCount()
        {
            TxtDocCount.Text = $"{_documents.Count}개";
        }

        // ============================================================
        // 문서 업로드 / 삭제 / 텍스트 추출
        // ============================================================
        private void BtnUploadDoc_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "지원 문서 (*.xlsx;*.pdf;*.txt)|*.xlsx;*.pdf;*.txt",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            foreach (var path in dlg.FileNames)
            {
                try
                {
                    string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                    string text = ExtractText(path, ext);
                    _documents.Add(new DocSearchItem
                    {
                        FileName = Path.GetFileName(path),
                        FileType = ext,
                        ExtractedText = text,
                        UploadedAt = DateTime.Now
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"'{Path.GetFileName(path)}' 파일을 처리하는 중 오류가 발생했습니다.\n{ex.Message}", "문서 업로드", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            SaveDocuments();
            UpdateDocCount();
            RunSearch();
        }

        private void BtnDeleteDoc_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DocSearchItem doc)
            {
                if (MessageBox.Show($"'{doc.FileName}' 문서를 삭제하시겠습니까?", "문서 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _documents.Remove(doc);
                SaveDocuments();
                UpdateDocCount();
                RunSearch();
            }
        }

        private static string ExtractText(string path, string ext)
        {
            switch (ext)
            {
                case "txt":
                    return File.ReadAllText(path);
                case "xlsx":
                    return ExtractTextFromXlsx(path);
                case "pdf":
                    return ExtractTextFromPdf(path);
                default:
                    throw new NotSupportedException($"지원하지 않는 파일 형식입니다: {ext}");
            }
        }

        private static string ExtractTextFromXlsx(string path)
        {
            var sb = new StringBuilder();
            using var wb = new XLWorkbook(path);
            foreach (var ws in wb.Worksheets)
            {
                sb.AppendLine($"[시트: {ws.Name}]");
                var range = ws.RangeUsed();
                if (range == null) continue;
                foreach (var row in range.RowsUsed())
                {
                    var cells = row.CellsUsed()
                        .Select(c => c.GetFormattedString())
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    string line = string.Join(" | ", cells);
                    if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine(line);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string ExtractTextFromPdf(string path)
        {
            var sb = new StringBuilder();
            using var pdf = PdfDocument.Open(path);
            foreach (var page in pdf.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            return sb.ToString();
        }

        // ============================================================
        // 키워드 검색
        // ============================================================
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RunSearch();
        }

        private void RunSearch()
        {
            string keyword = TxtSearch.Text.Trim();

            if (string.IsNullOrEmpty(keyword))
            {
                ResultListControl.ItemsSource = null;
                TxtResultSummary.Text = "검색어를 입력해주세요.";
                return;
            }

            var results = new List<SearchResultItem>();

            foreach (var doc in _documents)
            {
                string text = doc.ExtractedText;
                if (string.IsNullOrEmpty(text)) continue;

                var snippets = new List<string>();
                int matchCount = 0;
                int searchFrom = 0;

                while (true)
                {
                    int idx = text.IndexOf(keyword, searchFrom, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) break;

                    matchCount++;

                    if (snippets.Count < MaxSnippetsPerDoc)
                    {
                        int start = Math.Max(0, idx - SnippetContext);
                        int end = Math.Min(text.Length, idx + keyword.Length + SnippetContext);

                        string before = text.Substring(start, idx - start).Replace("\r", " ").Replace("\n", " ");
                        string match = text.Substring(idx, keyword.Length);
                        string after = text.Substring(idx + keyword.Length, end - (idx + keyword.Length)).Replace("\r", " ").Replace("\n", " ");

                        string prefix = start > 0 ? "…" : "";
                        string suffix = end < text.Length ? "…" : "";

                        snippets.Add($"{prefix}{before}【{match}】{after}{suffix}");
                    }

                    searchFrom = idx + keyword.Length;
                }

                if (matchCount > 0)
                {
                    results.Add(new SearchResultItem
                    {
                        FileName = doc.FileName,
                        Snippet = string.Join("\n", snippets),
                        MatchCount = matchCount
                    });
                }
            }

            results = results.OrderByDescending(r => r.MatchCount).ToList();

            ResultListControl.ItemsSource = results;
            TxtResultSummary.Text = results.Count > 0
                ? $"'{keyword}' 검색 결과: 문서 {results.Count}건에서 발견됨"
                : $"'{keyword}'에 대한 검색 결과가 없습니다.";
        }
    }
}
