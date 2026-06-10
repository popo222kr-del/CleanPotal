using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClosedXML.Excel;
using UglyToad.PdfPig;

namespace CleanPotal
{
    // ============================================================
    // 데이터 모델
    // ============================================================

    public class AiDocumentItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FileName { get; set; } = "";
        public string FileType { get; set; } = "";
        public string ExtractedText { get; set; } = "";
        public DateTime UploadedAt { get; set; } = DateTime.Now;

        [System.Text.Json.Serialization.JsonIgnore]
        public string SummaryInfo => $"{FileType.ToUpperInvariant()} · {ExtractedText.Length:N0}자 · {UploadedAt:yyyy-MM-dd}";
    }

    public class AiChatMessage : INotifyPropertyChanged
    {
        public string Role { get; set; } = "user"; // "user" | "assistant"

        private string _content = "";
        public string Content
        {
            get => _content;
            set { if (_content == value) return; _content = value; OnPropertyChanged(); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string RoleLabel => Role == "user" ? "나" : "Claude";

        [System.Text.Json.Serialization.JsonIgnore]
        public Brush BubbleBackground => Role == "user"
            ? new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE))
            : new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

        [System.Text.Json.Serialization.JsonIgnore]
        public HorizontalAlignment BubbleAlignment => Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ============================================================
    // API 키 / 모델 설정 로컬 저장 (SessionManager의 자동로그인 패턴과 동일)
    // ============================================================
    internal static class AiSettingsStore
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CleanPotal", "ai_settings.dat");

        public static void Save(string apiKey, string model)
        {
            try
            {
                string? dir = Path.GetDirectoryName(SettingsPath);
                if (dir != null) Directory.CreateDirectory(dir);
                string data = $"{apiKey}|{model}";
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
                File.WriteAllText(SettingsPath, encoded);
            }
            catch { }
        }

        public static (string apiKey, string model) Load()
        {
            if (!File.Exists(SettingsPath)) return ("", "claude-sonnet-4-6");
            try
            {
                string encoded = File.ReadAllText(SettingsPath);
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                string[] parts = decoded.Split('|', 2);
                if (parts.Length == 2) return (parts[0], parts[1]);
            }
            catch { }
            return ("", "claude-sonnet-4-6");
        }
    }

    // ============================================================
    // AiDocSearchView - 관리자 전용 AI 문서 검색
    // ============================================================
    public partial class AiDocSearchView : UserControl
    {
        private readonly ObservableCollection<AiDocumentItem> _documents = new();
        private readonly ObservableCollection<AiChatMessage> _chatMessages = new();

        private const int MaxCharsPerDocument = 200_000;

        private static string DocStoreRoot => Path.Combine(AppPaths.DataRoot, "ai_doc_search");
        private static string DocumentsFilePath => Path.Combine(DocStoreRoot, "documents.json");

        public AiDocSearchView()
        {
            InitializeComponent();

            DocListControl.ItemsSource = _documents;
            ChatListControl.ItemsSource = _chatMessages;

            Loaded += AiDocSearchView_Loaded;
        }

        private void AiDocSearchView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettingsIntoUi();
            LoadDocuments();
            UpdateDocCount();
        }

        public void TryRefresh()
        {
            try { LoadDocuments(); UpdateDocCount(); } catch { }
        }

        // ============================================================
        // 설정 (API 키 / 모델)
        // ============================================================
        private void LoadSettingsIntoUi()
        {
            var (apiKey, model) = AiSettingsStore.Load();
            PwdApiKey.Password = apiKey;
            foreach (var obj in CmbModel.Items)
            {
                if (obj is ComboBoxItem item && (string?)item.Tag == model)
                {
                    CmbModel.SelectedItem = item;
                    break;
                }
            }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = PwdApiKey.Password.Trim();
            string model = (CmbModel.SelectedItem as ComboBoxItem)?.Tag as string ?? "claude-sonnet-4-6";
            AiSettingsStore.Save(apiKey, model);
            TxtSettingsStatus.Text = "저장되었습니다.";
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
                var items = JsonSerializer.Deserialize<List<AiDocumentItem>>(json) ?? new List<AiDocumentItem>();
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
                MessageBox.Show($"문서 목록 저장 중 오류가 발생했습니다.\n{ex.Message}", "AI 문서 검색", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    _documents.Add(new AiDocumentItem
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
        }

        private void BtnDeleteDoc_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AiDocumentItem doc)
            {
                if (MessageBox.Show($"'{doc.FileName}' 문서를 삭제하시겠습니까?", "문서 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _documents.Remove(doc);
                SaveDocuments();
                UpdateDocCount();
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
        // 채팅
        // ============================================================
        private async void TxtQuestion_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private void BtnClearChat_Click(object sender, RoutedEventArgs e)
        {
            _chatMessages.Clear();
        }

        private async Task SendMessageAsync()
        {
            string question = TxtQuestion.Text.Trim();
            if (string.IsNullOrEmpty(question)) return;

            var (apiKey, model) = AiSettingsStore.Load();
            if (string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show("API 키를 입력하고 [설정 저장]을 눌러주세요.", "AI 문서 검색", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtQuestion.Clear();
            BtnSend.IsEnabled = false;

            _chatMessages.Add(new AiChatMessage { Role = "user", Content = question });
            var assistantMsg = new AiChatMessage { Role = "assistant", Content = "답변 작성 중..." };
            _chatMessages.Add(assistantMsg);
            ScrollChatToEnd();

            try
            {
                string systemPrompt = BuildSystemPrompt();
                var history = _chatMessages.Where(m => m != assistantMsg).ToList();
                string answer = await CallClaudeApiAsync(apiKey, model, systemPrompt, history);
                assistantMsg.Content = string.IsNullOrWhiteSpace(answer) ? "(빈 응답을 받았습니다.)" : answer;
            }
            catch (Exception ex)
            {
                assistantMsg.Content = $"오류가 발생했습니다.\n{ex.Message}";
            }
            finally
            {
                BtnSend.IsEnabled = true;
                ScrollChatToEnd();
            }
        }

        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("당신은 회사 내부 기준서와 문서를 기반으로 질문에 답변하는 AI 어시스턴트입니다.");
            sb.AppendLine("아래에 제공된 문서 내용을 참고하여 정확하게 답변하고, 문서에서 근거를 찾을 수 없는 내용은 추측하지 말고 모른다고 답하세요.");
            sb.AppendLine("가능하면 어떤 문서를 참고했는지 함께 알려주세요.");
            sb.AppendLine();

            if (_documents.Count == 0)
            {
                sb.AppendLine("(현재 업로드된 참고 문서가 없습니다.)");
                return sb.ToString();
            }

            foreach (var doc in _documents)
            {
                sb.AppendLine($"===== 문서: {doc.FileName} =====");
                string text = doc.ExtractedText;
                if (text.Length > MaxCharsPerDocument)
                    text = text.Substring(0, MaxCharsPerDocument) + "\n...(이하 생략)...";
                sb.AppendLine(text);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private void ScrollChatToEnd()
        {
            Dispatcher.InvokeAsync(() => ChatScrollViewer.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ============================================================
        // Claude API 호출 (HttpClient 직접 호출)
        // ============================================================
        private static async Task<string> CallClaudeApiAsync(string apiKey, string model, string systemPrompt, List<AiChatMessage> history)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var messages = history.Select(m => new { role = m.Role, content = m.Content }).ToList();
            var body = new
            {
                model,
                max_tokens = 4096,
                system = systemPrompt,
                messages
            };

            string requestJson = JsonSerializer.Serialize(body);
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var response = await client.PostAsync("https://api.anthropic.com/v1/messages", content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    using var errDoc = JsonDocument.Parse(responseBody);
                    if (errDoc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                        throw new Exception($"API 오류 ({(int)response.StatusCode}): {msg.GetString()}");
                }
                catch (JsonException) { }
                throw new Exception($"API 오류 ({(int)response.StatusCode}): {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var sb = new StringBuilder();
            if (doc.RootElement.TryGetProperty("content", out var contentArr))
            {
                foreach (var block in contentArr.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "text"
                        && block.TryGetProperty("text", out var textProp))
                    {
                        sb.Append(textProp.GetString());
                    }
                }
            }
            return sb.ToString();
        }
    }
}
