using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        public string Category { get; set; } = "";
        public string StoredPath { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public DateTime UploadedAt { get; set; } = DateTime.Now;

        [System.Text.Json.Serialization.JsonIgnore]
        public string SummaryInfo => $"{FileType.ToUpperInvariant()} · {ExtractedText.Length:N0}자 · {UploadedAt:yyyy-MM-dd}";
    }

    public class DocCategoryGroup
    {
        public string Name { get; set; } = "";
        public ObservableCollection<DocSearchItem> Items { get; } = new();

        public string CountLabel => $"({Items.Count})";
    }

    public class SearchResultItem
    {
        public string FileName { get; set; } = "";
        public string Snippet { get; set; } = "";
        public int MatchCount { get; set; }
        public DocSearchItem? Document { get; set; }

        public string MatchCountLabel => $"{MatchCount}건 일치";
    }

    public partial class DocSearchView : UserControl
    {
        private readonly ObservableCollection<DocSearchItem> _documents = new();

        private const int SnippetContext = 30;
        private const int MaxSnippetsPerDoc = 3;

        // 문서 번호 기준 분류 순서
        private static readonly string[] CategoryOrder = { "검사 기준서", "관리 기준서", "작업 표준서", "기타 문서" };

        private static string DocStoreRoot => Path.Combine(AppPaths.DataRoot, "doc_search");
        private static string FilesRoot => Path.Combine(DocStoreRoot, "files");
        private static string DocumentsFilePath => Path.Combine(DocStoreRoot, "documents.json");

        public DocSearchView()
        {
            InitializeComponent();

            Loaded += DocSearchView_Loaded;
        }

        private void DocSearchView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDocuments();
            UpdateDocCount();
            RebuildGroups();
        }

        public void TryRefresh()
        {
            try { LoadDocuments(); UpdateDocCount(); RebuildGroups(); } catch { }
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
                foreach (var item in items)
                {
                    if (string.IsNullOrEmpty(item.Category)) item.Category = DetermineCategory(item.FileName);
                    _documents.Add(item);
                }
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
        // 문서 번호(AQI-XXX) 기준 분류 / 그룹화
        // ============================================================
        private static string DetermineCategory(string fileName)
        {
            var match = Regex.Match(fileName, "AQI[-_]?(\\d{3})", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                switch (match.Groups[1].Value)
                {
                    case "803": return "검사 기준서";
                    case "804": return "관리 기준서";
                    case "802": return "작업 표준서";
                }
            }
            return "기타 문서";
        }

        private void RebuildGroups()
        {
            var groups = new List<DocCategoryGroup>();
            foreach (var categoryName in CategoryOrder)
            {
                var items = _documents.Where(d => d.Category == categoryName).ToList();
                // 기준서 3종은 비어 있어도 상위 메뉴로 항상 표시, 기타 문서는 있을 때만 표시
                if (items.Count == 0 && categoryName == "기타 문서") continue;

                var group = new DocCategoryGroup { Name = categoryName };
                foreach (var item in items) group.Items.Add(item);
                groups.Add(group);
            }

            DocListControl.ItemsSource = groups;
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
                    string fileName = Path.GetFileName(path);

                    var item = new DocSearchItem
                    {
                        FileName = fileName,
                        FileType = ext,
                        ExtractedText = text,
                        Category = DetermineCategory(fileName),
                        SourcePath = path,
                        UploadedAt = DateTime.Now
                    };

                    Directory.CreateDirectory(FilesRoot);
                    string storedPath = Path.Combine(FilesRoot, $"{item.Id}{Path.GetExtension(path)}");
                    File.Copy(path, storedPath, overwrite: true);
                    item.StoredPath = storedPath;

                    _documents.Add(item);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"'{Path.GetFileName(path)}' 파일을 처리하는 중 오류가 발생했습니다.\n{ex.Message}", "문서 업로드", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            SaveDocuments();
            UpdateDocCount();
            RebuildGroups();
            RunSearch();
        }

        private void BtnDeleteDoc_Click(object sender, RoutedEventArgs e)
        {
            if (SessionManager.CurrentUsername != "1004")
            {
                MessageBox.Show("문서 삭제는 시스템 관리자(마스터)만 가능합니다.", "문서 삭제", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (sender is Button btn && btn.Tag is DocSearchItem doc)
            {
                if (MessageBox.Show($"'{doc.FileName}' 문서를 삭제하시겠습니까?", "문서 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _documents.Remove(doc);

                if (!string.IsNullOrEmpty(doc.StoredPath) && File.Exists(doc.StoredPath))
                {
                    try { File.Delete(doc.StoredPath); } catch { }
                }

                SaveDocuments();
                UpdateDocCount();
                RebuildGroups();
                RunSearch();
            }
        }

        // ============================================================
        // 문서 열기
        // ============================================================
        private void DocItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (IsDescendantOfButton(e.OriginalSource as DependencyObject)) return;
            if (sender is FrameworkElement fe && fe.Tag is DocSearchItem doc) OpenDocument(doc);
        }

        private void ResultItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is SearchResultItem result && result.Document != null)
                OpenDocument(result.Document);
        }

        private static bool IsDescendantOfButton(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is Button) return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private static void OpenDocument(DocSearchItem doc)
        {
            // 보관본 우선, 없으면 업로드 당시 원본 경로로 폴백
            string? openPath = null;
            if (!string.IsNullOrEmpty(doc.StoredPath) && File.Exists(doc.StoredPath)) openPath = doc.StoredPath;
            else if (!string.IsNullOrEmpty(doc.SourcePath) && File.Exists(doc.SourcePath)) openPath = doc.SourcePath;

            if (openPath == null)
            {
                MessageBox.Show(
                    $"'{doc.FileName}' 문서의 원본 파일이 보관되어 있지 않습니다.\n\n" +
                    "문서 열기 기능이 추가되기 전에 등록된 문서는 파일이 보관되지 않았습니다.\n" +
                    "해당 문서를 삭제 후 다시 업로드하면 클릭으로 열 수 있습니다.",
                    "문서 열기", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(openPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"문서를 여는 중 오류가 발생했습니다.\n{ex.Message}", "문서 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private void CmbCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            RunSearch();
        }

        private string SelectedCategoryFilter =>
            (CmbCategoryFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "전체 문서";

        private void RunSearch()
        {
            string input = TxtSearch.Text.Trim();

            if (string.IsNullOrEmpty(input))
            {
                ResultListControl.ItemsSource = null;
                TxtResultSummary.Text = "검색어를 입력해주세요.";
                return;
            }

            // 공백으로 구분된 여러 단어를 모두 포함(AND 조건)하는 문서만 검색
            string[] keywords = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string categoryFilter = SelectedCategoryFilter;

            var results = new List<SearchResultItem>();

            foreach (var doc in _documents)
            {
                if (categoryFilter != "전체 문서" && doc.Category != categoryFilter) continue;

                string text = doc.ExtractedText;
                if (string.IsNullOrEmpty(text)) continue;

                if (keywords.Any(k => text.IndexOf(k, StringComparison.OrdinalIgnoreCase) < 0)) continue;

                var snippets = new List<string>();
                int matchCount = 0;

                foreach (var keyword in keywords)
                {
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
                }

                results.Add(new SearchResultItem
                {
                    FileName = doc.FileName,
                    Snippet = string.Join("\n", snippets),
                    MatchCount = matchCount,
                    Document = doc
                });
            }

            results = results.OrderByDescending(r => r.MatchCount).ToList();

            string scopeLabel = categoryFilter == "전체 문서" ? "" : $" ({categoryFilter})";
            ResultListControl.ItemsSource = results;
            TxtResultSummary.Text = results.Count > 0
                ? $"'{input}' 검색 결과{scopeLabel}: 문서 {results.Count}건에서 발견됨"
                : $"'{input}'에 대한 검색 결과가 없습니다.{scopeLabel}";
        }
    }
}
