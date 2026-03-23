using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        public PortalView()
        {
            InitializeComponent();
            LoadButtons();
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
                    var groups = JsonSerializer.Deserialize<List<ButtonGroup>>(json, options);
                    GroupsItemsControl.ItemsSource = groups;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"버튼 데이터를 불러오는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PortalButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && !string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                        // 🔥 [해결 3] 파일/폴더를 열 때 묻지도 따지지도 않고 무조건 관리자 권한으로 실행
                        Verb = "runas"
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"파일/폴더를 관리자 권한으로 열 수 없습니다.\n경로: {path}\n\n상세 오류: {ex.Message}", "실행 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void OpenVendorManager_Click(object sender, RoutedEventArgs e)
        {
            VendorManagerWindow win = new VendorManagerWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
        }

        public void OpenPortalManager_Click(object sender, RoutedEventArgs e)
        {
            PortalManagerWindow win = new PortalManagerWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
        }
    }
}