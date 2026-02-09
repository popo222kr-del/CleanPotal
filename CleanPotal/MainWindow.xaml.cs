using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;          // ✅ 반드시 파일 맨 위 using 영역에
using System.Text.Json;
using System.Windows;

namespace CleanPortal
{
    public partial class MainWindow : Window
    {
        public List<GroupVm> Groups { get; set; } = new();
        public string TodayText { get; set; } = "";
        public string StatusText { get; set; } = "";

        private const string JsonFileName = "buttons.json";

        public MainWindow()
        {
            InitializeComponent();

            TodayText = DateTime.Now.ToString("yyyy-MM-dd dddd");
            LoadButtons();

            DataContext = this;
        }

        private void LoadButtons()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string jsonPath = Path.Combine(baseDir, JsonFileName);

                if (!File.Exists(jsonPath))
                {
                    StatusText = $"buttons.json 파일이 없습니다.\n경로: {jsonPath}";
                    return;
                }

                // ✅ UTF-8로 명시 읽기
                string json = File.ReadAllText(jsonPath, Encoding.UTF8);

                var groups = JsonSerializer.Deserialize<List<GroupVm>>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                Groups = groups ?? new List<GroupVm>();
                StatusText = $"로드 완료: 그룹 {Groups.Count}개";
            }
            catch (Exception ex)
            {
                StatusText = "버튼 목록 로드 실패: " + ex.Message;
            }
        }

        private void OpenItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.Tag is not ItemVm item) return;

            try
            {
                if (string.IsNullOrWhiteSpace(item.Path))
                {
                    MessageBox.Show("경로가 비어있습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool isFolder = item.Type?.Equals("folder", StringComparison.OrdinalIgnoreCase) == true;
                bool exists = isFolder ? Directory.Exists(item.Path) : File.Exists(item.Path);

                if (!exists)
                {
                    MessageBox.Show($"경로가 존재하지 않습니다.\n{item.Path}",
                        "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText = $"실패: {item.Title}";
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = item.Path,
                    UseShellExecute = true
                });

                StatusText = $"열기: {item.Title}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"열기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = $"오류: {item.Title}";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class GroupVm
    {
        public string Group { get; set; } = "";
        public List<ItemVm> Items { get; set; } = new();
    }

    public class ItemVm
    {
        public string Title { get; set; } = "";
        public string Path { get; set; } = "";
        public string Type { get; set; } = "file"; // file | folder
    }
}
