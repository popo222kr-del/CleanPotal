using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    // ============================================================
    // 데이터 모델 (단일 파일 내부에 모두 정의)
    // ============================================================

    public class PersonalNote : INotifyPropertyChanged
    {
        public string NoteId { get; set; } = Guid.NewGuid().ToString("N");
        public string UserId { get; set; } = "";

        private string _title = "";
        public string Title
        {
            get => _title;
            set { if (_title == value) return; _title = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTitle)); }
        }

        private string _content = "";
        public string Content
        {
            get => _content;
            set { if (_content == value) return; _content = value; OnPropertyChanged(); OnPropertyChanged(nameof(Preview)); }
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        private DateTime _updatedAt = DateTime.Now;
        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set { if (_updatedAt == value) return; _updatedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShortDate)); }
        }

        public bool IsDeleted { get; set; } = false;
        public List<string> Tags { get; set; } = new();

        // ===== 표시용 파생 속성 (직렬화 제외) =====
        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "제목 없음" : Title;

        [System.Text.Json.Serialization.JsonIgnore]
        public string ShortDate => UpdatedAt.ToString("MM-dd");

        [System.Text.Json.Serialization.JsonIgnore]
        public string Preview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Content)) return "내용 없음";
                string firstLine = Content.Replace("\r", "").Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
                if (firstLine.Length > 60) firstLine = firstLine.Substring(0, 60) + "…";
                return firstLine;
            }
        }

        private bool _isSelectedInList;
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsSelectedInList
        {
            get => _isSelectedInList;
            set { if (_isSelectedInList == value) return; _isSelectedInList = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>왼쪽 목록의 월별 그룹</summary>
    public class PersonalNoteGroup
    {
        public string GroupHeader { get; set; } = "";
        public ObservableCollection<PersonalNote> Notes { get; set; } = new();
    }

    // ============================================================
    // PersonalMemoView - 개인 메모장
    // ============================================================
    public partial class PersonalMemoView : UserControl
    {
        // === 데이터 ===
        private readonly List<PersonalNote> _allNotes = new();              // 현재 사용자의 모든 메모(미삭제)
        private PersonalNote? _selectedNote;
        private readonly ObservableCollection<PersonalNoteGroup> _groupedNotes = new();

        // === 편집 상태 ===
        private bool _suppressEditorEvents = false;
        private bool _isDirty = false;

        // === 사용자 정보 ===
        private readonly string _userId;       // 파일 저장 키 (영문 ID)
        private readonly string _userName;     // 표시용 이름

        // === 저장 경로 ===
        private static string PersonalNotesRoot => Path.Combine(AppPaths.DataRoot, "personal_notes");

        public PersonalMemoView()
        {
            InitializeComponent();

            // 로그인 사용자 정보 가져오기 (기존 SessionManager 사용)
            _userId = SessionManager.IsLoggedIn ? (SessionManager.CurrentUsername ?? "").Trim() : "";
            _userName = SessionManager.IsLoggedIn ? (SessionManager.CurrentRealName ?? "") : "";

            GroupedNotesControl.ItemsSource = _groupedNotes;

            Loaded += PersonalMemoView_Loaded;
            Unloaded += PersonalMemoView_Unloaded;
        }

        private void PersonalMemoView_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_userId))
            {
                TxtUserHeader.Text = "(로그인 필요)";
                MessageBox.Show("로그인이 필요합니다. 로그인 후 다시 시도해주세요.", "개인 메모장", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            TxtUserHeader.Text = $"{_userName}({_userId})";
            LoadNotesFromDisk();
            RebuildGroupedList();

            // 가장 최근 메모 자동 선택
            if (_allNotes.Count > 0) SelectNote(_allNotes[0]);
        }

        private void PersonalMemoView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_isDirty && _selectedNote != null)
            {
                var result = MessageBox.Show(
                    $"\"{_selectedNote.DisplayTitle}\" 메모에 저장되지 않은 변경사항이 있습니다.\n저장하시겠습니까?",
                    "저장 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                    SaveSelectedNote(showStatus: false);
            }
        }

        // ============================================================
        // 파일 입출력 (Service 역할)
        // ============================================================

        private static string SanitizeUserId(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return "_unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var ch in userId.Trim())
            {
                if (invalid.Contains(ch) || ch == ' ') sb.Append('_');
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        private string GetUserFilePath()
        {
            string safeId = SanitizeUserId(_userId);
            return Path.Combine(PersonalNotesRoot, $"{safeId}.json");
        }

        public void TryRefresh()
        {
            if (_isDirty) return; // 미저장 변경사항 있으면 새로고침 건너뜀
            try { LoadNotesFromDisk(); RebuildGroupedList(); }
            catch { }
        }

        private void LoadNotesFromDisk()
        {
            _allNotes.Clear();
            try
            {
                string path = GetUserFilePath();
                if (!File.Exists(path)) return;

                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return;

                var loaded = JsonSerializer.Deserialize<List<PersonalNote>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (loaded == null) return;

                // IsDeleted 제외 + UpdatedAt 내림차순
                foreach (var n in loaded.Where(n => !n.IsDeleted).OrderByDescending(n => n.UpdatedAt))
                {
                    n.UserId = _userId; // 안전하게 강제 동기화
                    _allNotes.Add(n);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("개인 메모 불러오기 중 오류가 발생했습니다.\n" + ex.Message, "개인 메모장", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveAllToDisk()
        {
            if (string.IsNullOrWhiteSpace(_userId)) return;
            try
            {
                Directory.CreateDirectory(PersonalNotesRoot);
                string path = GetUserFilePath();

                // 삭제된 항목도 보존하기 위해 기존 파일 + 메모리 합본
                List<PersonalNote> merged = new();
                if (File.Exists(path))
                {
                    try
                    {
                        var existing = JsonSerializer.Deserialize<List<PersonalNote>>(File.ReadAllText(path, Encoding.UTF8), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (existing != null) merged.AddRange(existing.Where(e => e.IsDeleted)); // 삭제된 것만 보존
                    }
                    catch { /* 파일이 깨졌으면 무시 */ }
                }
                merged.AddRange(_allNotes);

                // 직렬화
                string json = JsonSerializer.Serialize(merged, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                // 안전 저장: 임시파일에 쓴 뒤 교체
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                TxtSaveStatus.Text = "저장 실패";
                MessageBox.Show("개인 메모 저장 중 오류가 발생했습니다.\n" + ex.Message, "개인 메모장", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ============================================================
        // 목록 / 그룹 / 검색
        // ============================================================

        private void RebuildGroupedList()
        {
            string keyword = (TxtSearch?.Text ?? "").Trim();
            IEnumerable<PersonalNote> filtered = _allNotes;
            if (!string.IsNullOrEmpty(keyword))
            {
                filtered = _allNotes.Where(n =>
                    (n.Title?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (n.Content?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            }

            var grouped = filtered
                .OrderByDescending(n => n.UpdatedAt)
                .GroupBy(n => n.UpdatedAt.ToString("yyyy년 MM월"))
                .Select(g => new PersonalNoteGroup
                {
                    GroupHeader = g.Key,
                    Notes = new ObservableCollection<PersonalNote>(g)
                })
                .ToList();

            _groupedNotes.Clear();
            foreach (var g in grouped) _groupedNotes.Add(g);
        }

        // ============================================================
        // UI 이벤트
        // ============================================================

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RebuildGroupedList();
        }

        private void NoteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PersonalNote note)
            {
                if (note == _selectedNote) return;
                if (_isDirty && _selectedNote != null)
                {
                    var result = MessageBox.Show(
                        $"\"{_selectedNote.DisplayTitle}\" 메모에 저장되지 않은 변경사항이 있습니다.\n저장하시겠습니까?",
                        "저장 확인", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Cancel) return;
                    if (result == MessageBoxResult.Yes) SaveSelectedNote(showStatus: false);
                }
                SelectNote(note);
            }
        }

        private void SelectNote(PersonalNote note)
        {
            // 이전 선택 해제
            if (_selectedNote != null) _selectedNote.IsSelectedInList = false;

            _selectedNote = note;
            _selectedNote.IsSelectedInList = true;
            _isDirty = false;

            // 편집 영역 채우기
            _suppressEditorEvents = true;
            try
            {
                TxtTitle.Text = note.Title;
                TxtContent.Text = note.Content;
                TxtMetaCreated.Text = $"생성일 {note.CreatedAt:yyyy-MM-dd HH:mm}";
                TxtMetaUpdated.Text = $"수정일 {note.UpdatedAt:yyyy-MM-dd HH:mm}";
                TxtMetaAuthor.Text = $"작성자 {_userName}({note.UserId})";
                TxtSaveStatus.Text = "";
            }
            finally
            {
                _suppressEditorEvents = false;
            }

            EmptyStatePanel.Visibility = Visibility.Collapsed;
            EditorPanel.Visibility = Visibility.Visible;
        }

        private void BtnNewNote_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_userId))
            {
                MessageBox.Show("로그인이 필요합니다.", "개인 메모장", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_isDirty && _selectedNote != null)
            {
                var result = MessageBox.Show(
                    $"\"{_selectedNote.DisplayTitle}\" 메모에 저장되지 않은 변경사항이 있습니다.\n저장하시겠습니까?",
                    "저장 확인", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes) SaveSelectedNote(showStatus: false);
            }

            // 중복 제목 회피
            string baseTitle = "새 메모";
            string title = baseTitle;
            int counter = 1;
            while (_allNotes.Any(n => string.Equals(n.Title?.Trim(), title, StringComparison.Ordinal)))
            {
                title = $"{baseTitle} {counter++}";
            }

            var newNote = new PersonalNote
            {
                NoteId = Guid.NewGuid().ToString("N"),
                UserId = _userId,
                Title = title,
                Content = "",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                IsDeleted = false
            };

            _allNotes.Insert(0, newNote);
            SaveAllToDisk();
            RebuildGroupedList();
            SelectNote(newNote);
            TxtTitle.Focus();
            TxtTitle.SelectAll();
        }

        private void EditorContent_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEditorEvents || _selectedNote == null) return;
            _selectedNote.Title = TxtTitle.Text;
            _selectedNote.Content = TxtContent.Text;

            if (!_isDirty)
            {
                _isDirty = true;
                TxtSaveStatus.Text = "저장되지 않은 변경사항";
            }
        }

        private void SaveSelectedNote(bool showStatus)
        {
            if (_selectedNote == null) return;
            if (string.IsNullOrWhiteSpace(_userId)) return;

            _selectedNote.UpdatedAt = DateTime.Now;
            SaveAllToDisk();

            _isDirty = false;

            // 메타 정보 갱신
            TxtMetaUpdated.Text = $"수정일 {_selectedNote.UpdatedAt:yyyy-MM-dd HH:mm}";

            // 정렬 변경되어 있을 수 있으므로 그룹 재구성
            _allNotes.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
            RebuildGroupedList();

            if (showStatus) TxtSaveStatus.Text = $"저장됨 · {DateTime.Now:HH:mm:ss}";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveSelectedNote(showStatus: true);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNote == null) return;
            var result = MessageBox.Show($"\"{_selectedNote.DisplayTitle}\" 메모를 삭제하시겠습니까?", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            _isDirty = false;

            // 휴지통 방식 - IsDeleted 플래그
            _selectedNote.IsDeleted = true;
            _selectedNote.UpdatedAt = DateTime.Now;

            // 메모리 목록에서는 제거
            _allNotes.Remove(_selectedNote);
            SaveAllToDisk();

            _selectedNote = null;
            RebuildGroupedList();

            // 다음 메모 자동 선택
            if (_allNotes.Count > 0)
            {
                SelectNote(_allNotes[0]);
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                EditorPanel.Visibility = Visibility.Collapsed;
                TxtTitle.Text = "";
                TxtContent.Text = "";
            }
        }
    }
}
