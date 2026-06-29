using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    public partial class PortalView : UserControl
    {
        public class ButtonItem
        {
            public string title { get; set; } = "";
            public string path { get; set; } = "";
            public string type { get; set; } = "file";
        }

        public class ButtonGroup
        {
            public string group { get; set; } = "";
            public List<ButtonItem> items { get; set; } = new();
        }

        private List<ButtonGroup> _allGroups = new();

        public PortalView()
        {
            InitializeComponent();
            LoadButtons();
        }

        // 네트워크 접근 없이 확장자 '모양'으로만 파일 여부 판단.
        // 예: ".xlsm"→파일, "03. 기타세정"의 끝부분(" 기타세정")→폴더 (공백/한글 포함 → 확장자 아님)
        private static bool IsLikelyFileExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext) || ext.Length < 2 || ext.Length > 6) return false;
            for (int i = 1; i < ext.Length; i++) // [0]은 '.'
            {
                char c = ext[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return false;
            }
            return true;
        }

        private void LoadButtons()
        {
            try
            {
                string path = AppPaths.ButtonsFilePath;
                if (!File.Exists(path)) path = AppPaths.GetFallbackButtonsPath();

                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _allGroups = JsonSerializer.Deserialize<List<ButtonGroup>>(json, options) ?? new();

                    foreach (var g in _allGroups)
                    {
                        foreach (var item in g.items)
                        {
                            if (string.IsNullOrWhiteSpace(item.type) || item.type == "file")
                            {
                                try
                                {
                                    if (item.path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                    {
                                        item.type = "web";
                                    }
                                    else
                                    {
                                        string ext = Path.GetExtension(item.path).ToLower();
                                        if (ext == ".xls" || ext == ".xlsx" || ext == ".xlsm" || ext == ".csv") item.type = "excel";
                                        else if (ext == ".pdf") item.type = "pdf";
                                        else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif") item.type = "image";
                                        // ⚠️ 네트워크(Directory.Exists) 탐색 금지: NAS가 느리거나 죽으면 포털 로드가 멈춤.
                                        //    확장자 '모양'만으로 폴더/파일 판별 (실제 확장자처럼 안 생기면 폴더로 간주).
                                        else if (!IsLikelyFileExtension(ext)) item.type = "folder";
                                        else item.type = "file";
                                    }
                                }
                                catch
                                {
                                    item.type = "file";
                                }
                            }
                        }
                    }
                    FilterAndBind("");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"버튼 데이터를 불러오는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void FilterAndBind(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                GroupsItemsControl.ItemsSource = _allGroups;
                return;
            }

            var filteredGroups = new List<ButtonGroup>();
            keyword = keyword.ToLower();

            foreach (var g in _allGroups)
            {
                var matchingItems = g.items.Where(i => i.title.ToLower().Contains(keyword) || i.path.ToLower().Contains(keyword)).ToList();

                if (matchingItems.Count > 0)
                {
                    filteredGroups.Add(new ButtonGroup { group = g.group, items = matchingItems });
                }
                else if (g.group.ToLower().Contains(keyword))
                {
                    filteredGroups.Add(g);
                }
            }

            GroupsItemsControl.ItemsSource = filteredGroups;
        }

        private void PortalButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && !string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"파일/폴더를 열 수 없습니다.\n경로: {path}\n\n상세 오류: {ex.Message}", "실행 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void OpenVendorManager_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthManager.CheckAuth(PermissionType.Vendors)) return;

            VendorManagerWindow win = new VendorManagerWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
        }

        public void OpenPortalManager_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthManager.CheckAuth(PermissionType.Files)) return;

            PortalManagerWindow win = new PortalManagerWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
            LoadButtons();
        }
    }
}