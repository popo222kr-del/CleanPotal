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

        private string _elapsedLabel = "";
        public string ElapsedLabel
        {
            get => _elapsedLabel;
            set { if (_elapsedLabel == value) return; _elapsedLabel = value; OnPropertyChanged(); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string RoleLabel => Role == "user" ? "나" : "주언비서";

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
    // Ollama 서버 주소 / 모델 설정 로컬 저장
    // ============================================================
    internal static class OllamaSettingsStore
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CleanPotal", "ai_settings.dat");

        private const string DefaultEndpoint = "http://localhost:11434";
        private const string DefaultModel = "llama3.1:8b";

        public static void Save(string endpoint, string model)
        {
            try
            {
                string? dir = Path.GetDirectoryName(SettingsPath);
                if (dir != null) Directory.CreateDirectory(dir);
                string data = $"{endpoint}|{model}";
                File.WriteAllText(SettingsPath, data);
            }
            catch { }
        }

        public static (string endpoint, string model) Load()
        {
            if (!File.Exists(SettingsPath)) return (DefaultEndpoint, DefaultModel);
            try
            {
                string data = File.ReadAllText(SettingsPath);
                string[] parts = data.Split('|', 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                    return (parts[0], parts[1]);
            }
            catch { }
            return (DefaultEndpoint, DefaultModel);
        }
    }

    // ============================================================
    // AiDocSearchView - 관리자 전용 AI 문서 검색 (로컬 Ollama 기반)
    // ============================================================
    public partial class AiDocSearchView : UserControl
    {
        private readonly ObservableCollection<AiDocumentItem> _documents = new();
        private readonly ObservableCollection<AiChatMessage> _chatMessages = new();

        private const int MaxTotalContextChars = 8_000;
        private const int MaxHistoryMessages = 6;

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
            var (endpoint, model) = OllamaSettingsStore.Load();
            TxtOllamaEndpoint.Text = endpoint;
            CmbModel.Text = model;
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string endpoint = TxtOllamaEndpoint.Text.Trim().TrimEnd('/');
            string model = CmbModel.Text.Trim();
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(model))
            {
                MessageBox.Show("Ollama 서버 주소와 모델 이름을 모두 입력해주세요.", "AI 문서 검색", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            OllamaSettingsStore.Save(endpoint, model);
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

            var (endpoint, model) = OllamaSettingsStore.Load();

            TxtQuestion.Clear();
            BtnSend.IsEnabled = false;

            _chatMessages.Add(new AiChatMessage { Role = "user", Content = question });
            var assistantMsg = new AiChatMessage { Role = "assistant", Content = "답변 작성 중... (로컬 모델은 응답이 느릴 수 있습니다)" };
            _chatMessages.Add(assistantMsg);
            ScrollChatToEnd();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string systemPrompt = BuildSystemPrompt();
                var history = _chatMessages.Where(m => m != assistantMsg).ToList();
                if (history.Count > MaxHistoryMessages)
                    history = history.Skip(history.Count - MaxHistoryMessages).ToList();

                bool firstChunk = true;
                string answer = await CallOllamaApiAsync(endpoint, model, systemPrompt, history, partial =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (firstChunk) { assistantMsg.Content = ""; firstChunk = false; }
                        assistantMsg.Content += partial;
                        ScrollChatToEnd();
                    });
                });

                if (firstChunk)
                    assistantMsg.Content = string.IsNullOrWhiteSpace(answer) ? "(빈 응답을 받았습니다.)" : answer;
            }
            catch (Exception ex)
            {
                assistantMsg.Content = $"오류가 발생했습니다.\n{ex.Message}";
            }
            finally
            {
                stopwatch.Stop();
                var ts = stopwatch.Elapsed;
                assistantMsg.ElapsedLabel = ts.TotalMinutes >= 1
                    ? $"응답 시간: {(int)ts.TotalMinutes}분 {ts.Seconds}초"
                    : $"응답 시간: {ts.Seconds}초";
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
            sb.AppendLine("기본적으로 한국어로 답변하되, 전문 용어나 고유 명사는 영어를 함께 사용해도 됩니다. 단, 일본어나 한자는 절대 섞지 마세요.");
            sb.AppendLine();

            if (_documents.Count == 0)
            {
                sb.AppendLine("(현재 업로드된 참고 문서가 없습니다.)");
                return sb.ToString();
            }

            int remaining = MaxTotalContextChars;
            foreach (var doc in _documents)
            {
                if (remaining <= 0)
                {
                    sb.AppendLine($"===== 문서: {doc.FileName} (컨텍스트 한도 초과로 생략됨) =====");
                    continue;
                }

                sb.AppendLine($"===== 문서: {doc.FileName} =====");
                string text = doc.ExtractedText;
                if (text.Length > remaining)
                    text = text.Substring(0, remaining) + "\n...(이하 생략)...";
                sb.AppendLine(text);
                sb.AppendLine();
                remaining -= text.Length;
            }

            return sb.ToString();
        }

        private void ScrollChatToEnd()
        {
            Dispatcher.InvokeAsync(() => ChatScrollViewer.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ============================================================
        // Ollama API 호출 (로컬 서버, /api/chat)
        // ============================================================
        private static async Task<string> CallOllamaApiAsync(string endpoint, string model, string systemPrompt, List<AiChatMessage> history, Action<string>? onPartial = null)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            var messages = new List<object> { new { role = "system", content = systemPrompt } };
            messages.AddRange(history.Select(m => (object)new { role = m.Role, content = m.Content }));

            var body = new
            {
                model,
                messages,
                stream = true,
                keep_alive = "30m",
                options = new { num_ctx = 4096, num_predict = 1024, temperature = 0.3, num_thread = 12 }
            };

            string requestJson = JsonSerializer.Serialize(body);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/chat")
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ollama 서버({endpoint})에 연결할 수 없습니다. Ollama가 실행 중인지 확인해주세요.\n{ex.Message}");
            }

            if (!response.IsSuccessStatusCode)
            {
                string errBody = await response.Content.ReadAsStringAsync();
                try
                {
                    using var errDoc = JsonDocument.Parse(errBody);
                    if (errDoc.RootElement.TryGetProperty("error", out var err))
                        throw new Exception($"Ollama 오류: {err.GetString()}");
                }
                catch (JsonException) { }
                throw new Exception($"Ollama 오류 ({(int)response.StatusCode}): {errBody}");
            }

            var fullAnswer = new StringBuilder();
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("error", out var streamErr))
                        throw new Exception($"Ollama 오류: {streamErr.GetString()}");

                    if (root.TryGetProperty("message", out var messageEl) &&
                        messageEl.TryGetProperty("content", out var contentEl))
                    {
                        string piece = contentEl.GetString() ?? "";
                        if (piece.Length > 0)
                        {
                            fullAnswer.Append(piece);
                            onPartial?.Invoke(piece);
                        }
                    }

                    if (root.TryGetProperty("done", out var doneEl) && doneEl.GetBoolean())
                        break;
                }
                catch (JsonException) { }
            }

            return fullAnswer.ToString();
        }
    }
}
