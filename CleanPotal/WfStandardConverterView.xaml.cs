using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace CleanPotal
{
    public partial class WfStandardConverterView : UserControl
    {
        private string? _masterPath;
        private string? _sourceFolder;
        private string? _outputFolder;
        private string? _usedSheet;

        private List<WfGroup> _groups = new();
        private readonly List<(CheckBox Check, WfGroup Group)> _groupChecks = new();
        private readonly List<CheckBox> _deptChecks = new();
        private bool _busy;
        private bool _suppressFilterEvent;

        private bool _envChecked;

        public WfStandardConverterView()
        {
            InitializeComponent();
            Loaded += async (s, e) =>
            {
                if (!_envChecked)
                {
                    _envChecked = true;
                    await CheckEnvironmentAsync();
                    await LoadConfigAndGroupsAsync();
                }
            };
        }

        // 다른 페이지 갔다가 돌아오면 호출됨 → Python 환경 재점검 + 공유 설정 반영 (앱 재시작 불필요)
        public async void TryRefresh()
        {
            if (_busy) return;
            _envChecked = true; // Loaded 중복 실행 방지
            Log("── 환경 재확인 ──");
            await CheckEnvironmentAsync();
            if (_groups.Count == 0) await LoadConfigAndGroupsAsync();
        }

        // ───────────────────────── 공유 설정 (NAS) ─────────────────────────

        // 모든 사용자가 공유하도록 NAS(DataRoot)에 경로 설정을 저장한다.
        private static string ConfigPath => Path.Combine(AppPaths.DataRoot, "wf_converter_config.json");

        private static WfConfig? LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<WfConfig>(File.ReadAllText(ConfigPath));
            }
            catch { }
            return null;
        }

        private void SaveConfig()
        {
            try
            {
                if (!Directory.Exists(AppPaths.DataRoot)) Directory.CreateDirectory(AppPaths.DataRoot);
                var cfg = new WfConfig { MasterPath = _masterPath, SourceFolder = _sourceFolder, OutputFolder = _outputFolder };
                var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(ConfigPath, json, new UTF8Encoding(false));
            }
            catch (Exception ex) { Log("⚠ 공유 설정 저장 실패: " + ex.Message); }
        }

        private async Task LoadConfigAndGroupsAsync()
        {
            var cfg = LoadConfig();
            if (cfg == null) return;

            _masterPath = !string.IsNullOrEmpty(cfg.MasterPath) && File.Exists(cfg.MasterPath) ? cfg.MasterPath : null;
            _sourceFolder = !string.IsNullOrEmpty(cfg.SourceFolder) && Directory.Exists(cfg.SourceFolder) ? cfg.SourceFolder : null;
            _outputFolder = !string.IsNullOrEmpty(cfg.OutputFolder) && Directory.Exists(cfg.OutputFolder) ? cfg.OutputFolder : null;

            if (_masterPath != null) { Log($"[공유 설정] 마스터: {Path.GetFileName(_masterPath)}"); BtnSetSourceFolder.IsEnabled = true; }
            else if (!string.IsNullOrEmpty(cfg.MasterPath)) Log($"⚠ 공유 마스터 파일을 찾을 수 없습니다: {cfg.MasterPath}");

            if (_sourceFolder != null) { Log($"[공유 설정] 원본 폴더: {_sourceFolder}"); BtnSetOutputFolder.IsEnabled = true; }
            if (_outputFolder != null) Log($"[공유 설정] 변경 폴더: {_outputFolder}");

            if (_masterPath != null && _sourceFolder != null)
                await LoadGroupsAsync();

            UpdateConvertEnabled();
        }

        // ───────────────────────── Python 환경 점검 ─────────────────────────

        private async Task CheckEnvironmentAsync()
        {
            if (!File.Exists(RunnerScript))
            {
                Log($"⛔ 변환 스크립트를 찾을 수 없습니다.\n   ({RunnerScript})\n   빌드/배포 시 WfStandardConverter 폴더가 함께 복사되었는지 확인하세요.");
                return;
            }
            try
            {
                var (code, stdout, stderr) = await RunPythonCaptureAsync(new[] { RunnerScript, "check" });
                if (code != 0 && string.IsNullOrWhiteSpace(stdout))
                {
                    Log("⛔ Python 실행 실패. Python 설치 및 PATH 등록을 확인하세요.");
                    if (!string.IsNullOrWhiteSpace(stderr)) Log(stderr.Trim());
                    return;
                }

                string jsonText = stdout;
                int s0 = stdout.IndexOf('{'); int s1 = stdout.LastIndexOf('}');
                if (s0 >= 0 && s1 > s0) jsonText = stdout.Substring(s0, s1 - s0 + 1);

                var env = JsonSerializer.Deserialize<WfEnvResult>(jsonText);
                if (env == null) { Log("⛔ 환경 점검 결과를 해석하지 못했습니다."); return; }

                if (env.Missing != null && env.Missing.Count > 0)
                {
                    Log($"⚠ Python {env.Python} 확인됨. 그러나 필수 패키지 누락: {string.Join(", ", env.Missing)}");
                    Log($"   설치 명령:  python -m pip install {string.Join(" ", env.Missing)}");
                }
                else
                {
                    Log($"✓ Python {env.Python} · 필수 패키지(openpyxl, lxml) 정상. 준비 완료.");
                }
            }
            catch (Exception ex)
            {
                Log("⛔ Python 환경 점검 실패: " + ex.Message);
                Log("   Python 이 설치되어 있고 PATH 에 등록되어 있는지 확인하세요.");
            }
        }

        // ───────────────────────── 경로/실행 헬퍼 ─────────────────────────

        private static string ScriptsDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WfStandardConverter", "scripts");

        private static string RunnerScript => Path.Combine(ScriptsDir, "wf_run.py");

        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                if (LogText.Text == "대기 중...") LogText.Text = "";
                LogText.Text += (LogText.Text.Length > 0 ? "\n" : "") + msg;
                LogText.ScrollToEnd();
            });
        }

        private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(LogText.Text))
                {
                    Clipboard.SetText(LogText.Text);
                    BtnCopyLog.Content = "복사됨";
                    var timer = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(1.2) };
                    timer.Tick += (s, ev) => { BtnCopyLog.Content = "복사"; timer.Stop(); };
                    timer.Start();
                }
            }
            catch { }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            BtnLoadMaster.IsEnabled = !busy;
            BtnSetSourceFolder.IsEnabled = !busy && _masterPath != null;
            BtnSetOutputFolder.IsEnabled = !busy && _sourceFolder != null;
            UpdateConvertEnabled();
            ConvertProgress.IsIndeterminate = busy;
        }

        private void UpdateConvertEnabled()
        {
            bool anyChecked = _groupChecks.Any(g => g.Check.IsChecked == true);
            BtnConvert.IsEnabled = !_busy && _masterPath != null && _sourceFolder != null
                                   && _outputFolder != null && anyChecked;
        }

        // ───────────────────────── 1) 마스터 파일 ─────────────────────────

        private void BtnLoadMaster_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "변경안(마스터) 파일 선택",
                Filter = "Excel 파일 (*.xlsx;*.xls)|*.xlsx;*.xls|모든 파일 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            _masterPath = dlg.FileName;
            SaveConfig();
            Log($"[마스터] {Path.GetFileName(_masterPath)} 선택됨 (공유 설정 저장)");
            BtnSetSourceFolder.IsEnabled = true;
            DetailText.Text = "원본 파일 폴더를 지정하면 모표준 목록을 불러옵니다.";
        }

        // ───────────────────────── 2) 원본 파일 폴더 ─────────────────────────

        private async void BtnSetSourceFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "원본 파일이 있는 폴더 선택" };
            if (!string.IsNullOrEmpty(_sourceFolder) && Directory.Exists(_sourceFolder)) dlg.InitialDirectory = _sourceFolder;
            if (dlg.ShowDialog() != true) return;

            _sourceFolder = dlg.FolderName;
            SaveConfig();
            Log($"[원본 파일 폴더] {_sourceFolder}");
            BtnSetOutputFolder.IsEnabled = true;

            await LoadGroupsAsync();
        }

        // ───────────────────────── 3) 변경 파일 폴더 ─────────────────────────

        private void BtnSetOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "변경된 파일을 저장할 폴더 선택" };
            if (!string.IsNullOrEmpty(_outputFolder) && Directory.Exists(_outputFolder)) dlg.InitialDirectory = _outputFolder;
            if (dlg.ShowDialog() != true) return;

            _outputFolder = dlg.FolderName;
            SaveConfig();
            Log($"[변경 파일 폴더] {_outputFolder}");
            UpdateConvertEnabled();
        }

        // ───────────────────────── 모표준 목록 로드 ─────────────────────────

        private async Task LoadGroupsAsync()
        {
            if (_masterPath == null || _sourceFolder == null) return;
            if (!File.Exists(RunnerScript))
            {
                Log($"⛔ 실행 스크립트를 찾을 수 없습니다: {RunnerScript}");
                return;
            }

            SetBusy(true);
            Log("[목록] 마스터 분석 중...");
            try
            {
                var (code, stdout, stderr) = await RunPythonCaptureAsync(
                    new[] { RunnerScript, "list", _masterPath, _sourceFolder });

                if (code != 0 && string.IsNullOrWhiteSpace(stdout))
                {
                    Log("⛔ 목록 분석 실패");
                    if (!string.IsNullOrWhiteSpace(stderr)) Log(stderr.Trim());
                    return;
                }

                // stdout 에서 JSON 부분만 추출 (혹시 모를 잡음 대비)
                string jsonText = stdout;
                int s0 = stdout.IndexOf('{');
                int s1 = stdout.LastIndexOf('}');
                if (s0 >= 0 && s1 > s0) jsonText = stdout.Substring(s0, s1 - s0 + 1);

                WfListResult? result;
                try { result = JsonSerializer.Deserialize<WfListResult>(jsonText); }
                catch (Exception ex)
                {
                    Log("⛔ 결과 파싱 실패: " + ex.Message);
                    if (!string.IsNullOrWhiteSpace(stderr)) Log(stderr.Trim());
                    return;
                }

                if (result == null || result.Error != null)
                {
                    Log("⛔ " + (result?.Error ?? "알 수 없는 오류"));
                    return;
                }

                _groups = result.Groups ?? new List<WfGroup>();
                _usedSheet = result.Sheet;
                PopulateDeptFilter();
                BuildTree();

                int subCount = _groups.Sum(g => g.Subs?.Count ?? 0);
                Log($"[목록] 시트 '{_usedSheet}' → 모표준 {_groups.Count}개 · 부속서 {subCount}건");
            }
            catch (Exception ex)
            {
                Log("⛔ 오류: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        // 모표준명 앞부분에서 부서 추출 (예: "세정사업부 검사..." → "세정사업부")
        private static string DeptOf(WfGroup g)
        {
            var name = (g.Mname ?? "").Trim();
            if (name.Length == 0) return "(미분류)";
            int sp = name.IndexOf(' ');
            return sp > 0 ? name.Substring(0, sp) : name;
        }

        private void PopulateDeptFilter()
        {
            _suppressFilterEvent = true;
            DeptFilterPanel.Children.Clear();
            _deptChecks.Clear();

            foreach (var d in _groups.Select(DeptOf).Distinct().OrderBy(x => x))
            {
                int cnt = _groups.Count(g => DeptOf(g) == d);
                var chk = new CheckBox
                {
                    Content = $"{d} ({cnt})",
                    Tag = d,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 10, 4),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55))
                };
                chk.Checked += (s, e) => { if (!_suppressFilterEvent) BuildTree(); };
                chk.Unchecked += (s, e) => { if (!_suppressFilterEvent) BuildTree(); };
                DeptFilterPanel.Children.Add(chk);
                _deptChecks.Add(chk);
            }
            _suppressFilterEvent = false;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressFilterEvent) return;
            BuildTree();
        }

        private IEnumerable<WfGroup> FilteredGroups()
        {
            IEnumerable<WfGroup> q = _groups;

            // 체크된 부서가 하나도 없으면 전체, 있으면 해당 부서들만
            var checkedDepts = _deptChecks.Where(c => c.IsChecked == true)
                                          .Select(c => (string)c.Tag).ToHashSet();
            if (checkedDepts.Count > 0) q = q.Where(g => checkedDepts.Contains(DeptOf(g)));

            string kw = (SearchBox?.Text ?? "").Trim();
            if (kw.Length > 0)
                q = q.Where(g => (g.Mno ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase)
                              || (g.Mname ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase));
            return q;
        }

        private void BuildTree()
        {
            MasterTreeView.Items.Clear();
            _groupChecks.Clear();

            var shown = FilteredGroups().ToList();
            foreach (var g in shown)
            {
                var chk = new CheckBox
                {
                    Content = $"{g.Mno}  {g.Mname}",
                    IsChecked = true,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
                };
                chk.Checked += (s, e) => UpdateConvertEnabled();
                chk.Unchecked += (s, e) => UpdateConvertEnabled();

                var item = new TreeViewItem { Header = chk, IsExpanded = false };

                foreach (var sub in g.Subs ?? new List<WfSub>())
                {
                    bool missing = !string.IsNullOrEmpty(sub.Oldno) && string.IsNullOrEmpty(sub.Src);
                    var tb = new TextBlock
                    {
                        Text = $"{sub.Subno}  ·  {sub.Title}" + (missing ? "   ⛔ 소스 미발견" : ""),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(missing
                            ? Color.FromRgb(0xDC, 0x26, 0x26)
                            : Color.FromRgb(0x64, 0x74, 0x8B))
                    };
                    item.Items.Add(new TreeViewItem { Header = tb });
                }

                item.Selected += (s, e) => ShowDetail(g);
                MasterTreeView.Items.Add(item);
                _groupChecks.Add((chk, g));
            }

            CountText.Text = shown.Count == _groups.Count
                ? $"{_groups.Count}개"
                : $"{shown.Count} / {_groups.Count}개";
            UpdateConvertEnabled();
        }

        private void ShowDetail(WfGroup g)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"모표준 No : {g.Mno}");
            sb.AppendLine($"모표준명 : {g.Mname}");
            sb.AppendLine($"부속서 : {g.Subs?.Count ?? 0}건");
            sb.AppendLine();
            foreach (var sub in g.Subs ?? new List<WfSub>())
            {
                sb.AppendLine($"  • {sub.Subno}  ({sub.Oldno})");
                sb.AppendLine($"      {sub.Title}");
                sb.AppendLine($"      소스: {(string.IsNullOrEmpty(sub.Src) ? "⛔ 미발견" : sub.Src)}");
            }
            if (g.Issues != null && g.Issues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("이슈:");
                foreach (var i in g.Issues) sb.AppendLine("  - " + i);
            }
            DetailText.Text = sb.ToString();
            DetailText.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
        }

        // ───────────────────────── 전체 선택/해제 ─────────────────────────

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var (chk, _) in _groupChecks) chk.IsChecked = true;
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var (chk, _) in _groupChecks) chk.IsChecked = false;
        }

        // ───────────────────────── 4) 변환 실행 ─────────────────────────

        private async void BtnConvert_Click(object sender, RoutedEventArgs e)
        {
            if (_outputFolder == null) return;

            var selected = _groupChecks.Where(g => g.Check.IsChecked == true)
                                       .Select(g => g.Group).ToList();
            if (selected.Count == 0) { Log("선택된 모표준이 없습니다."); return; }

            // 선택된 그룹만 mapping.json 으로 작성 (convert.py 입력 형식)
            string mappingPath = Path.Combine(Path.GetTempPath(),
                $"wf_mapping_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            try
            {
                var json = JsonSerializer.Serialize(selected, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(mappingPath, json, new UTF8Encoding(false));
            }
            catch (Exception ex) { Log("⛔ mapping.json 작성 실패: " + ex.Message); return; }

            SetBusy(true);
            ConvertProgress.IsIndeterminate = false;
            ConvertProgress.Value = 0;
            int total = selected.Count;
            int done = 0;
            Log($"═══ 변환 시작 (모표준 {total}개) ═══");

            try
            {
                int code = await RunPythonStreamAsync(
                    new[] { RunnerScript, "convert", mappingPath, _outputFolder },
                    line =>
                    {
                        Log(line);
                        if (line.StartsWith("✓"))
                        {
                            done++;
                            Dispatcher.Invoke(() => ConvertProgress.Value = Math.Min(100, done * 100.0 / total));
                        }
                    });

                if (code == 0)
                {
                    Dispatcher.Invoke(() => ConvertProgress.Value = 100);
                    Log($"═══ 변환 완료 → {_outputFolder} ═══");
                    MessageBox.Show($"변환이 완료되었습니다.\n\n출력 폴더:\n{_outputFolder}",
                        "문서 개정작업", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Log($"⛔ 변환 종료 코드 {code} (오류 발생)");
                }
            }
            catch (Exception ex)
            {
                Log("⛔ 변환 오류: " + ex.Message);
            }
            finally
            {
                try { File.Delete(mappingPath); } catch { }
                SetBusy(false);
            }
        }

        // ───────────────────────── Python 실행 ─────────────────────────

        private static ProcessStartInfo BuildPsi(string exe, IEnumerable<string> args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = ScriptsDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUTF8"] = "1";
            return psi;
        }

        // python 실행기 후보를 순서대로 시도
        private static readonly string[] PythonCandidates = { "python", "py", "python3" };

        private static Process StartPython(IEnumerable<string> args)
        {
            var argList = args.ToList();
            Exception? last = null;
            foreach (var exe in PythonCandidates)
            {
                try { return Process.Start(BuildPsi(exe, argList))!; }
                catch (Exception ex) { last = ex; }
            }
            throw new InvalidOperationException(
                "Python 실행기를 찾을 수 없습니다 (python/py/python3). Python 설치 및 PATH 등록을 확인하세요.", last);
        }

        private static async Task<(int code, string stdout, string stderr)> RunPythonCaptureAsync(IEnumerable<string> args)
        {
            using var p = StartPython(args);
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            await Task.Run(() => p.WaitForExit());
            string so = await outTask;
            string se = await errTask;
            return (p.ExitCode, so, se);
        }

        private async Task<int> RunPythonStreamAsync(IEnumerable<string> args, Action<string> onLine)
        {
            using var p = StartPython(args);
            p.OutputDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLine(e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await Task.Run(() => p.WaitForExit());
            return p.ExitCode;
        }

        // ───────────────────────── JSON 모델 ─────────────────────────

        private class WfConfig
        {
            [JsonPropertyName("master")] public string? MasterPath { get; set; }
            [JsonPropertyName("source")] public string? SourceFolder { get; set; }
            [JsonPropertyName("output")] public string? OutputFolder { get; set; }
        }

        private class WfEnvResult
        {
            [JsonPropertyName("python")] public string? Python { get; set; }
            [JsonPropertyName("missing")] public List<string>? Missing { get; set; }
        }

        private class WfListResult
        {
            [JsonPropertyName("sheet")] public string? Sheet { get; set; }
            [JsonPropertyName("groups")] public List<WfGroup>? Groups { get; set; }
            [JsonPropertyName("error")] public string? Error { get; set; }
        }

        private class WfGroup
        {
            [JsonPropertyName("mno")] public string Mno { get; set; } = "";
            [JsonPropertyName("mname")] public string Mname { get; set; } = "";
            [JsonPropertyName("subs")] public List<WfSub>? Subs { get; set; }
            [JsonPropertyName("issues")] public List<string>? Issues { get; set; }
        }

        private class WfSub
        {
            [JsonPropertyName("subno")] public string Subno { get; set; } = "";
            [JsonPropertyName("oldno")] public string Oldno { get; set; } = "";
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("src")] public string? Src { get; set; }
        }
    }
}
