using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    public partial class PortalManagerWindow : Window
    {
        // 🔥 JSON 속성 매핑을 명시적으로 선언하여 오류 원천 차단
        public class ButtonItem : INotifyPropertyChanged
        {
            private string _title = "";
            private string _path = "";

            [JsonPropertyName("title")]
            public string title { get => _title; set { _title = value; OnPropertyChanged(nameof(title)); } }
            [JsonPropertyName("path")]
            public string path { get => _path; set { _path = value; OnPropertyChanged(nameof(path)); } }
            [JsonPropertyName("type")]
            public string type { get; set; } = "file";

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class ButtonGroup : INotifyPropertyChanged
        {
            private string _group = "";
            [JsonPropertyName("group")]
            public string group { get => _group; set { _group = value; OnPropertyChanged(nameof(group)); } }
            [JsonPropertyName("items")]
            public ObservableCollection<ButtonItem> items { get; set; } = new();

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private ObservableCollection<ButtonGroup> _editingGroups = new();

        public PortalManagerWindow()
        {
            InitializeComponent();
            LoadDataForEditing();
        }

        private void LoadDataForEditing()
        {
            try
            {
                string json = File.ReadAllText(AppPaths.ButtonsFilePath, Encoding.UTF8);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<List<ButtonGroup>>(json, options);

                if (data != null)
                {
                    _editingGroups = new ObservableCollection<ButtonGroup>(data);
                }
                GroupListBox.ItemsSource = _editingGroups;
            }
            catch { }
        }

        private void GroupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ClearItemEditFields();

            if (GroupListBox.SelectedItem is ButtonGroup selectedGroup)
            {
                SelectedGroupNameRun.Text = selectedGroup.group;
                GroupNameBox.Text = selectedGroup.group;
                ButtonDataGrid.ItemsSource = selectedGroup.items;
            }
            else
            {
                SelectedGroupNameRun.Text = "그룹 선택 안됨";
                GroupNameBox.Text = "";
                ButtonDataGrid.ItemsSource = null;
            }
        }

        private void ButtonDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ButtonDataGrid.SelectedItem is ButtonItem selectedItem)
            {
                ItemTitleBox.Text = selectedItem.title;
                ItemPathBox.Text = selectedItem.path;
            }
        }

        private void ClearItemEditFields()
        {
            ItemTitleBox.Text = "";
            ItemPathBox.Text = "";
        }

        private void AddOrUpdateGroup_Click(object sender, RoutedEventArgs e)
        {
            string newName = GroupNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;

            if (GroupListBox.SelectedItem is ButtonGroup selectedGroup)
            {
                selectedGroup.group = newName;
            }
            else
            {
                if (_editingGroups.Any(x => x.group == newName))
                {
                    MessageBox.Show("이미 존재하는 그룹 이름입니다.", "알림");
                    return;
                }
                _editingGroups.Add(new ButtonGroup { group = newName });
            }
        }

        private void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (GroupListBox.SelectedItem is ButtonGroup selectedGroup)
            {
                if (MessageBox.Show($"[{selectedGroup.group}] 그룹과 내부 버튼들을 모두 삭제할까요?", "확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _editingGroups.Remove(selectedGroup);
                }
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog { Filter = "모든 파일 (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
            {
                ItemPathBox.Text = dlg.FileName;
            }
        }

        private void AddNewItem_Click(object sender, RoutedEventArgs e)
        {
            if (GroupListBox.SelectedItem is not ButtonGroup selectedGroup)
            {
                MessageBox.Show("먼저 왼쪽에서 대분류 그룹을 선택하세요.", "알림");
                return;
            }
            selectedGroup.items.Add(new ButtonItem { title = "새 버튼", path = "" });
            ButtonDataGrid.SelectedIndex = selectedGroup.items.Count - 1;
        }

        private void ApplyItemEdit_Click(object sender, RoutedEventArgs e)
        {
            if (ButtonDataGrid.SelectedItem is not ButtonItem selectedItem) return;

            string title = ItemTitleBox.Text.Trim();
            string path = ItemPathBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("버튼 이름을 입력하세요.", "알림");
                return;
            }
            selectedItem.title = title;
            selectedItem.path = path;
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (GroupListBox.SelectedItem is ButtonGroup selectedGroup && ButtonDataGrid.SelectedItem is ButtonItem selectedItem)
            {
                selectedGroup.items.Remove(selectedItem);
                ClearItemEditFields();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("모든 변경 내용을 저장할까요?\n저장 후 메인 화면에 즉시 반영됩니다.", "확인", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    // 🔥 한글이 유니코드로 깨지지 않도록 UnsafeRelaxedJsonEscaping 설정 추가
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    string json = JsonSerializer.Serialize(_editingGroups.ToList(), options);
                    File.WriteAllText(AppPaths.ButtonsFilePath, json, Encoding.UTF8);

                    DialogResult = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"파일 저장 실패: {ex.Message}", "오류");
                }
            }
        }
    }
}