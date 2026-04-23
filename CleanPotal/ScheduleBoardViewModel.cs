using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CleanPotal
{
    public class ScheduleBoardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private double _zoom = 1.0;
        public double Zoom { get => _zoom; set { if (Math.Abs(_zoom - value) > 0.0001) { _zoom = value; OnPropertyChanged(nameof(Zoom)); } } }

        public double CellWidth { get; } = 20.0;
        public double RowHeight { get; } = 26.0;
        public double EquipmentColumnWidth { get; } = 220.0;

        public int StartHour { get; } = 7;
        public int EndHourExclusive { get; } = 31;

        public int TotalCells => ((EndHourExclusive - StartHour) * 60) / 10;
        public int TotalMinutes => (EndHourExclusive - StartHour) * 60;

        private const int MaxConcurrentDIBatches = 5;

        private static string DbPath => Path.Combine(AppPaths.DataRoot, "CleanPotal.db");
        private static string RecipeFile => Path.Combine(AppPaths.DataRoot, "recipes.json");

        private void LoadRecipes()
        {
            if (!File.Exists(RecipeFile)) return;
            var json = File.ReadAllText(RecipeFile);
            var list = JsonSerializer.Deserialize<List<RecipeDefinition>>(json);
            if (list == null) return;
            Recipes.Clear();
            foreach (var r in list) Recipes.Add(r);
        }

        public void SaveRecipes()
        {
            var dir = Path.GetDirectoryName(RecipeFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(Recipes.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RecipeFile, json);
        }

        private void InitializeDatabase()
        {
            var dir = Path.GetDirectoryName(DbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using var conn = new SqliteConnection($"Data Source={DbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS ScheduleBlocks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EquipmentIndex INTEGER NOT NULL,
    StartCellIndex INTEGER NOT NULL,
    TotalCells INTEGER NOT NULL,
    S2Cells INTEGER NOT NULL,
    HFCells INTEGER NOT NULL,
    DICCells INTEGER NOT NULL,
    S2Temperature INTEGER,
    RecipeText TEXT NOT NULL,
    CreatedTime TEXT NOT NULL
);";
            cmd.ExecuteNonQuery();
        }

        public string TodayText => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " (" + GetKoreanDayName(DateTime.Now.DayOfWeek) + ")";

        private string _statusText = "";
        public string StatusText { get => _statusText; set { if (_statusText != value) { _statusText = value; OnPropertyChanged(nameof(StatusText)); } } }

        private string _hoverText = "";
        public string HoverText { get => _hoverText; set { if (_hoverText != value) { _hoverText = value; OnPropertyChanged(nameof(HoverText)); } } }

        private string? _lastClickedEquipmentName;
        public string? LastClickedEquipmentName { get => _lastClickedEquipmentName; set { if (_lastClickedEquipmentName != value) { _lastClickedEquipmentName = value; OnPropertyChanged(nameof(LastClickedEquipmentName)); } } }

        public void ReloadFromDatabase() { LoadBlocksFromDb(); }

        private void LoadBlocksFromDb()
        {
            if (!File.Exists(DbPath)) return;
            using var conn = new SqliteConnection($"Data Source={DbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT EquipmentIndex, StartCellIndex, RecipeText, S2Cells, HFCells, DICells, S2Temperature FROM ScheduleBlocks;";
            using var reader = cmd.ExecuteReader();

            PlacedBlocks.Clear();
            while (reader.Read())
            {
                var block = new PlacedRecipeBlock
                {
                    EquipmentIndex = reader.GetInt32(0),
                    StartMinute = reader.GetInt32(1) % TotalMinutes,
                    RecipeText = reader.GetString(2),
                    S2Minutes = reader.GetInt32(3),
                    HFMinutes = reader.GetInt32(4),
                    DIMinutes = reader.GetInt32(5),
                    S2Temperature = reader.IsDBNull(6) ? null : reader.GetInt32(6)
                };
                PlacedBlocks.Add(block);
            }
        }

        private int _selectedEquipmentIndex = -1;
        private int _selectedMinuteIndex = -1;
        public bool HasSelectedCell => _selectedEquipmentIndex >= 0 && _selectedMinuteIndex >= 0;
        public void SetSelectedCell(int equipmentIndex, int minuteIndex) { _selectedEquipmentIndex = equipmentIndex; _selectedMinuteIndex = minuteIndex; }
        public (int EquipmentIndex, int CellIndex) GetSelectedCell() => (_selectedEquipmentIndex, _selectedMinuteIndex);

        public string GetCellTimeText(int minuteIndex)
        {
            if (minuteIndex < 0) return "-";
            int abs = StartHour * 60 + minuteIndex;
            return $"{(abs / 60) % 24:00}:{abs % 60:00}";
        }

        public ObservableCollection<EquipmentLine> Equipments { get; } = new();
        public ObservableCollection<RecipeDefinition> Recipes { get; } = new();
        public ObservableCollection<PlacedRecipeBlock> PlacedBlocks { get; } = new();
        private readonly Stack<BoardUndoSnapshot> _undoStack = new();
        public bool CanUndoLastBoardAction => _undoStack.Count > 0;

        private RecipeDefinition? _selectedRecipe;
        public RecipeDefinition? SelectedRecipe { get => _selectedRecipe; set { if (_selectedRecipe != value) { _selectedRecipe = value; OnPropertyChanged(nameof(SelectedRecipe)); } } }

        public ScheduleBoardViewModel()
        {
            SeedEquipments(); LoadRecipes();
            if (Recipes.Count == 0) SeedRecipes();
            StatusText = "선택 레시피: 없음";
            try { InitializeDatabase(); LoadBlocksFromDb(); } catch (Exception ex) { StatusText = $"DB 로드 실패: {ex.Message}"; }
        }

        private static PlacedRecipeBlock CloneBlock(PlacedRecipeBlock b) => new PlacedRecipeBlock { EquipmentIndex = b.EquipmentIndex, StartMinute = b.StartMinute, RecipeText = b.RecipeText, S2Minutes = b.S2Minutes, HFMinutes = b.HFMinutes, DIMinutes = b.DIMinutes, S2Temperature = b.S2Temperature };
        private List<PlacedRecipeBlock> SnapshotPlacedBlocks() => PlacedBlocks.Select(CloneBlock).ToList();
        private void RestorePlacedBlocks(IEnumerable<PlacedRecipeBlock> snapshot) { PlacedBlocks.Clear(); foreach (var block in snapshot) PlacedBlocks.Add(CloneBlock(block)); }
        private void PushUndoSnapshot(string actionDescription) { _undoStack.Push(new BoardUndoSnapshot { ActionDescription = actionDescription, Blocks = SnapshotPlacedBlocks() }); OnPropertyChanged(nameof(CanUndoLastBoardAction)); }

        public bool TryUndoLastBoardAction(out string message)
        {
            if (_undoStack.Count == 0) { message = "되돌릴 작업이 없습니다."; return false; }
            var snapshot = _undoStack.Pop(); RestorePlacedBlocks(snapshot.Blocks); SaveAllBlocksToDb(); OnPropertyChanged(nameof(CanUndoLastBoardAction));
            message = $"되돌리기 완료: {snapshot.ActionDescription}"; return true;
        }

        private void SeedEquipments()
        {
            string[] names = { "MDC01 (POLY)", "MDC02 (Hot Chemical)", "MDC03 (Hot Chemical)", "MDC04 (POLY)", "MDC05 (TEOS)", "MDC06 (ALO/HFO)", "MDC07 (POLY)", "MDC08 (N,G,D-POLY)", "MDC09 (SIGE)", "MDC10 (ALO/HFO)", "MSC01-1 (POLY/대대배치)", "MSC01-2 (Rinse 전용)", "NDC01 (WOOAM)", "NDC02 (OXIDE)", "NDC03 (A급)", "NDC04 (A급)", "NDC05 (N,G,D-POLY)", "NDC06 (Hot Chemical)", "NDC07 (SiN)" };
            for (int i = 0; i < names.Length; i++) Equipments.Add(new EquipmentLine { Index = i, DisplayName = names[i] });
        }

        private void SeedRecipes() { Recipes.Add(new RecipeDefinition("0-15-100") { IsFavorite = false }); Recipes.Add(new RecipeDefinition("120-30-100@60") { IsFavorite = false }); Recipes.Add(new RecipeDefinition("30-30-100") { IsFavorite = false }); ReorderRecipesByFavorite(); SaveRecipes(); }
        public IEnumerable<RecipeDefinition> GetRecipesOrdered() => Recipes.OrderByDescending(r => r.IsFavorite).ThenBy(r => r.OrderIndex).ThenBy(r => r.Text).ToList();
        public void ReorderRecipesByFavorite() { int idx = 0; foreach (var r in Recipes.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.Text)) r.OrderIndex = idx++; }

        public bool TryAddRecipe(string input, out string message)
        {
            input = (input ?? "").Trim();
            if (!RecipeDefinition.TryParse(input, out var parsed, out string parseMsg)) { message = parseMsg; SaveRecipes(); return false; }
            if (Recipes.Any(r => r.S2Minutes == parsed.S2Minutes && r.HFMinutes == parsed.HFMinutes && r.DIMinutes == parsed.DIMinutes && r.S2Temperature == parsed.S2Temperature)) { message = $"이미 존재하는 레시피입니다: {parsed.Text}"; return false; }
            parsed.OrderIndex = Recipes.Count; Recipes.Add(parsed); ReorderRecipesByFavorite(); SaveRecipes(); OnPropertyChanged(nameof(Recipes));
            message = $"레시피 추가 완료: {parsed.Text}"; return true;
        }

        public bool TryDeleteSelectedRecipe(out string message)
        {
            if (SelectedRecipe == null) { message = "삭제할 레시피를 선택하세요."; SaveRecipes(); return false; }
            string target = SelectedRecipe.Text;
            if (PlacedBlocks.Any(b => b.RecipeText == target)) { message = $"배치된 레시피가 있어 삭제할 수 없습니다: {target}"; return false; }
            Recipes.Remove(SelectedRecipe); SelectedRecipe = null; ReorderRecipesByFavorite(); SaveRecipes();
            message = $"레시피 삭제 완료: {target}"; return true;
        }

        private bool IntersectsCircularMin(int s1, int l1, int s2, int l2, int ringSize)
        {
            s1 %= ringSize; s2 %= ringSize;
            for (int i = 0; i < l1; i++)
            {
                int c1 = (s1 + i) % ringSize;
                for (int j = 0; j < l2; j++)
                {
                    int c2 = (s2 + j) % ringSize;
                    if (c1 == c2) return true;
                }
            }
            return false;
        }

        public bool TryPlaceRecipe(int equipmentIndex, int startMinute, out string message)
        {
            if (SelectedRecipe == null) { message = "레시피를 먼저 선택하세요."; return false; }
            if (equipmentIndex < 0 || equipmentIndex >= Equipments.Count) { message = "설비 인덱스 오류"; return false; }
            if (startMinute < 0 || startMinute >= TotalMinutes) { message = "셀 범위 오류"; return false; }

            if (SelectedRecipe.TotalMinutes > TotalMinutes) { message = "배치 불가: 레시피 길이가 24시간을 초과합니다."; return false; }

            bool overlap = PlacedBlocks.Any(b => b.EquipmentIndex == equipmentIndex && IntersectsCircularMin(startMinute, SelectedRecipe.TotalMinutes, b.StartMinute, b.TotalMinutes, TotalMinutes));
            if (overlap) { message = $"배치 불가: {Equipments[equipmentIndex].DisplayName} 행에 겹치는 레시피가 있습니다."; return false; }

            if (ExceedsConcurrentDILimit(startMinute, SelectedRecipe, out string diLimitMsg)) { message = $"배치 불가: {diLimitMsg}"; return false; }

            PushUndoSnapshot($"배치 취소 ({Equipments[equipmentIndex].DisplayName} / {SelectedRecipe.Text})");

            var block = new PlacedRecipeBlock
            {
                EquipmentIndex = equipmentIndex,
                StartMinute = startMinute,
                RecipeText = SelectedRecipe.Text,
                S2Minutes = SelectedRecipe.S2Minutes,
                HFMinutes = SelectedRecipe.HFMinutes,
                DIMinutes = SelectedRecipe.DIMinutes,
                S2Temperature = SelectedRecipe.S2Temperature
            };

            PlacedBlocks.Add(block);
            SaveAllBlocksToDb();
            message = $"적용 완료: {Equipments[equipmentIndex].DisplayName} / {SelectedRecipe.Text}";
            return true;
        }

        private bool ExceedsConcurrentDILimit(int newStartMinute, RecipeDefinition recipe, out string detail)
        {
            detail = string.Empty;
            if (recipe.DIMinutes <= 0) return false;

            int ringSize = TotalMinutes;
            for (int i = 0; i < recipe.DIMinutes; i++)
            {
                int minOffset = (newStartMinute + recipe.S2Minutes + recipe.HFMinutes + i) % ringSize;
                int concurrent = 1;

                foreach (var b in PlacedBlocks)
                {
                    if (b.DIMinutes <= 0) continue;
                    int bDiStart = b.StartMinute + b.S2Minutes + b.HFMinutes;
                    bool inside = false;
                    for (int j = 0; j < b.DIMinutes; j++)
                    {
                        if ((bDiStart + j) % ringSize == minOffset) { inside = true; break; }
                    }
                    if (inside) concurrent++;
                }

                if (concurrent > MaxConcurrentDIBatches)
                {
                    int absMinutes = StartHour * 60 + minOffset;
                    detail = $"DI 동시 배치 {MaxConcurrentDIBatches}개 제한 초과 ({(absMinutes / 60) % 24:00}:{absMinutes % 60:00} 구간)";
                    return true;
                }
            }
            return false;
        }

        public bool TryRemoveBlockAt(int equipmentIndex, int clickMinute, out string message)
        {
            int ringSize = TotalMinutes;
            var block = PlacedBlocks.FirstOrDefault(b =>
            {
                if (b.EquipmentIndex != equipmentIndex) return false;
                for (int i = 0; i < b.TotalMinutes; i++)
                {
                    if ((b.StartMinute + i) % ringSize == clickMinute) return true;
                }
                return false;
            });

            if (block == null) { message = "해당 시간에 삭제할 레시피가 없습니다."; return false; }
            PushUndoSnapshot($"삭제 취소 ({Equipments[equipmentIndex].DisplayName} / {block.RecipeText})");
            PlacedBlocks.Remove(block); SaveAllBlocksToDb();
            message = $"삭제 완료: {Equipments[equipmentIndex].DisplayName} / {block.RecipeText}"; return true;
        }

        public void ClearPlacedBlocks()
        {
            if (PlacedBlocks.Count == 0) return;
            PushUndoSnapshot("초기화 취소 (전체 배치)"); PlacedBlocks.Clear(); SaveAllBlocksToDb();
        }

        public bool TryPartialResetFromSelectedCell(out string message)
        {
            if (!HasSelectedCell) { message = "부분 초기화: 시간을 먼저 선택하세요."; return false; }
            if (PlacedBlocks.Count == 0) { message = "부분 초기화: 삭제할 배치가 없습니다."; return false; }

            var (_, minuteIdx) = GetSelectedCell();
            if (minuteIdx < 0) { message = "부분 초기화: 선택 셀 정보가 올바르지 않습니다."; return false; }

            string timeText = GetCellTimeText(minuteIdx);
            PushUndoSnapshot($"부분 초기화 취소 (시간 {timeText} 이후 트림)");

            int removedCount = 0; int trimmedCount = 0; int ringSize = TotalMinutes;
            var blocks = PlacedBlocks.ToList();

            foreach (var b in blocks)
            {
                bool covers = false;
                for (int i = 0; i < b.TotalMinutes; i++)
                {
                    if ((b.StartMinute + i) % ringSize == minuteIdx) { covers = true; break; }
                }

                if (covers)
                {
                    int keepMinutes = (minuteIdx - b.StartMinute + ringSize) % ringSize;
                    if (keepMinutes <= 0) { PlacedBlocks.Remove(b); removedCount++; }
                    else
                    {
                        int keepS2 = Math.Min(keepMinutes, b.S2Minutes);
                        int remain = keepMinutes - keepS2;
                        int keepHF = Math.Min(Math.Max(0, remain), b.HFMinutes);
                        remain -= keepHF;
                        int keepDI = Math.Min(Math.Max(0, remain), b.DIMinutes);

                        b.S2Minutes = keepS2; b.HFMinutes = keepHF; b.DIMinutes = keepDI;
                        trimmedCount++;
                    }
                }
                else
                {
                    if (b.StartMinute % ringSize >= minuteIdx) { PlacedBlocks.Remove(b); removedCount++; }
                }
            }

            message = $"부분 초기화 완료: {timeText} 이후 삭제 {removedCount}건, 잘라내기 {trimmedCount}건";
            SaveAllBlocksToDb(); return true;
        }

        private string GetKoreanDayName(DayOfWeek day) => day switch { DayOfWeek.Sunday => "일요일", DayOfWeek.Monday => "월요일", DayOfWeek.Tuesday => "화요일", DayOfWeek.Wednesday => "수요일", DayOfWeek.Thursday => "목요일", DayOfWeek.Friday => "금요일", DayOfWeek.Saturday => "토요일", _ => "" };

        private void ClearScheduleTable()
        {
            using var conn = new SqliteConnection($"Data Source={DbPath}"); conn.Open(); using var cmd = conn.CreateCommand(); cmd.CommandText = "DELETE FROM ScheduleBlocks;"; cmd.ExecuteNonQuery();
        }

        private void SaveAllBlocksToDb()
        {
            ClearScheduleTable();
            using var conn = new SqliteConnection($"Data Source={DbPath}"); conn.Open();

            foreach (var block in PlacedBlocks)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO ScheduleBlocks
(EquipmentIndex, StartCellIndex, TotalCells, S2Cells, HFCells, DICells, S2Temperature, RecipeText, CreatedTime)
VALUES (@eq, @start, @total, @s2, @hf, @di, @temp, @recipe, @time);";
                cmd.Parameters.AddWithValue("@eq", block.EquipmentIndex);
                cmd.Parameters.AddWithValue("@start", block.StartMinute);
                cmd.Parameters.AddWithValue("@total", block.TotalMinutes);
                cmd.Parameters.AddWithValue("@s2", block.S2Minutes);
                cmd.Parameters.AddWithValue("@hf", block.HFMinutes);
                cmd.Parameters.AddWithValue("@di", block.DIMinutes);
                cmd.Parameters.AddWithValue("@recipe", block.RecipeText);
                cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@temp", (object?)block.S2Temperature ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }
    }

    public class BoardUndoSnapshot { public string ActionDescription { get; set; } = ""; public List<PlacedRecipeBlock> Blocks { get; set; } = new(); }
    public class EquipmentLine { public int Index { get; set; } public string DisplayName { get; set; } = ""; }

    public class RecipeDefinition
    {
        public string Text { get; set; } = "";
        public int S2Minutes { get; set; }
        public int HFMinutes { get; set; }
        public int DIMinutes { get; set; }
        public bool IsFavorite { get; set; }
        public int OrderIndex { get; set; }
        public int? S2Temperature { get; set; }
        public string DisplayText { get { string baseText = Text.Split('@')[0]; return S2Temperature.HasValue ? $"{baseText} (Hot Chemical)" : baseText; } }

        public int TotalMinutes => S2Minutes + HFMinutes + DIMinutes;

        public RecipeDefinition() { }
        public RecipeDefinition(string text) { Text = text; if (TryParse(text, out var parsed, out _)) { S2Minutes = parsed.S2Minutes; HFMinutes = parsed.HFMinutes; DIMinutes = parsed.DIMinutes; } }

        public static bool TryParse(string input, out RecipeDefinition recipe, out string message)
        {
            recipe = new RecipeDefinition();
            if (string.IsNullOrWhiteSpace(input)) { message = "레시피 형식 오류"; return false; }
            string normalized = input.Trim().Replace(" ", ""); string[] tempSplit = normalized.Split('@');
            string recipePart = tempSplit[0]; int? temp = null;
            if (tempSplit.Length == 2 && int.TryParse(tempSplit[1], out int parsedTemp)) temp = parsedTemp;
            string[] parts = recipePart.Split('-');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int s2) || !int.TryParse(parts[1], out int hf) || !int.TryParse(parts[2], out int di)) { message = "레시피 형식 오류 (예: 30-30-100)"; return false; }
            if (s2 < 0 || hf < 0 || di < 0) { message = "음수는 입력할 수 없습니다."; return false; }

            recipe = new RecipeDefinition { Text = temp.HasValue ? $"{s2}-{hf}-{di}@{temp.Value}" : $"{s2}-{hf}-{di}", S2Minutes = s2, HFMinutes = hf, DIMinutes = di, S2Temperature = temp };
            message = "OK"; return true;
        }
    }

    public class PlacedRecipeBlock
    {
        public int EquipmentIndex { get; set; }
        public int StartMinute { get; set; }
        public int? S2Temperature { get; set; }
        public string RecipeText { get; set; } = "";
        public int S2Minutes { get; set; }
        public int HFMinutes { get; set; }
        public int DIMinutes { get; set; }
        public int TotalMinutes => S2Minutes + HFMinutes + DIMinutes;

        public string DisplayText { get { string baseText = RecipeText.Split('@')[0]; return S2Temperature.HasValue ? $"{baseText} (S2 60℃ {S2Temperature.Value}분)" : baseText; } }
    }
}