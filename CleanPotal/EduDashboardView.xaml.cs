using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace CleanPotal
{
    public partial class EduDashboardView : UserControl
    {
        private int _selectedYear = DateTime.Today.Year;
        private List<EduDashboardRow> _allRows = new();
        private ICollectionView? _view;

        private static readonly string[] StatusOrder = { "전체", "대기", "신청완료", "진행", "완료", "취소" };

        public EduDashboardView()
        {
            InitializeComponent();

            StatusFilter.ItemsSource = StatusOrder;
            StatusFilter.SelectedIndex = 0;

            YearText.Text = _selectedYear.ToString();

            if (SessionManager.CanManageSchedule || SessionManager.CurrentUsername == "1004")
                BtnAddEdu.Visibility = System.Windows.Visibility.Visible;

            LoadData();
        }

        public void TryRefresh() => LoadData();

        // ── 데이터 로드 ────────────────────────────────────────────────
        private void LoadData()
        {
            var start = new DateTime(_selectedYear, 1, 1);
            var end = new DateTime(_selectedYear, 12, 31);

            var eduList = DatabaseHelper.GetEducationPlansInRange(start, end);
            var users = AuthDatabaseHelper.GetAllUsers();

            _allRows = eduList
                .Select(e =>
                {
                    var user = users.FirstOrDefault(u => u.RealName == e.MemberName);
                    return new EduDashboardRow
                    {
                        EduId = e.Id,
                        MemberName = e.MemberName,
                        Username = user?.Username ?? "-",
                        HireDate = user?.HireDate ?? "",
                        TeamName = user?.TeamName ?? "-",
                        JobTitle = user?.JobTitle ?? "-",
                        CourseName = e.CourseName,
                        StartDate = e.StartDate,
                        EndDate = e.EndDate,
                        Status = e.Status ?? "",
                        Progress = e.Progress,
                        EduMethod = e.EduMethod ?? "",
                        AttachmentPath = e.AttachmentPath ?? ""
                    };
                })
                .OrderBy(r => r.MemberName)
                .ThenBy(r => r.StartDate)
                .ToList();

            _view = CollectionViewSource.GetDefaultView(_allRows);
            _view.Filter = FilterRow;
            EduDataGrid.ItemsSource = _view;

            UpdateStatusCards();
        }

        private bool FilterRow(object obj)
        {
            if (obj is not EduDashboardRow row) return false;
            string selected = StatusFilter?.SelectedItem as string ?? "전체";
            return selected == "전체" || row.Status == selected;
        }

        // ── 상태 카드 숫자 갱신 ────────────────────────────────────────
        private void UpdateStatusCards()
        {
            int total = _allRows.Count;
            CountTotal.Text = total.ToString();
            CountWaiting.Text = _allRows.Count(r => r.Status == "대기").ToString();
            CountApplied.Text = _allRows.Count(r => r.Status == "신청완료").ToString();
            CountCancelled.Text = _allRows.Count(r => r.Status == "취소").ToString();
            CountInProgress.Text = _allRows.Count(r => r.Status == "진행").ToString();
            CountDone.Text = _allRows.Count(r => r.Status == "완료").ToString();
            TotalBadge.Text = $"전체 {total}건";
        }

        // ── 이벤트 ────────────────────────────────────────────────────
        private void BtnAddEdu_Click(object sender, RoutedEventArgs e)
        {
            var win = new ScheduleRegisterWindow(openEduTab: true)
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (win.ShowDialog() == true)
                LoadData();
        }

        private void BtnPrevYear_Click(object sender, RoutedEventArgs e)
        {
            _selectedYear--;
            YearText.Text = _selectedYear.ToString();
            LoadData();
        }

        private void BtnNextYear_Click(object sender, RoutedEventArgs e)
        {
            _selectedYear++;
            YearText.Text = _selectedYear.ToString();
            LoadData();
        }

        private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => _view?.Refresh();

        private void EduDataGrid_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void EduDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 0) return;

            var row = GetRowAtDropPoint(e.GetPosition(EduDataGrid));
            if (row == null) return;

            row.AttachmentPath = files[0];
            DatabaseHelper.UpdateEducationPlanAttachment(row.EduId, files[0]);
        }

        private void EduDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
            if (!Clipboard.ContainsFileDropList()) return;

            var files = Clipboard.GetFileDropList();
            if (files.Count == 0) return;

            var row = EduDataGrid.SelectedItem as EduDashboardRow;
            if (row == null) return;

            row.AttachmentPath = files[0]!;
            DatabaseHelper.UpdateEducationPlanAttachment(row.EduId, files[0]!);
            e.Handled = true;
        }

        private EduDashboardRow? GetRowAtDropPoint(Point point)
        {
            var hit = System.Windows.Media.VisualTreeHelper.HitTest(EduDataGrid, point);
            if (hit == null) return null;
            var dep = hit.VisualHit as System.Windows.DependencyObject;
            while (dep != null)
            {
                if (dep is DataGridRow dgRow) return dgRow.Item as EduDashboardRow;
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            }
            return null;
        }

        private void AttachBtn_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el) return;
            if (el.DataContext is not EduDashboardRow row) return;

            if (row.HasAttachment && File.Exists(row.AttachmentPath))
            {
                var menu = new ContextMenu { PlacementTarget = el };

                var openItem = new MenuItem { Header = "파일 열기" };
                openItem.Click += (_, _) => Process.Start(new ProcessStartInfo(row.AttachmentPath) { UseShellExecute = true });

                var changeItem = new MenuItem { Header = "파일 변경" };
                changeItem.Click += (_, _) => PickAndSaveAttachment(row);

                var removeItem = new MenuItem { Header = "첨부 제거" };
                removeItem.Click += (_, _) =>
                {
                    row.AttachmentPath = "";
                    DatabaseHelper.UpdateEducationPlanAttachment(row.EduId, "");
                };

                menu.Items.Add(openItem);
                menu.Items.Add(changeItem);
                menu.Items.Add(new Separator());
                menu.Items.Add(removeItem);

                el.ContextMenu = menu;
                menu.IsOpen = true;
            }
            else
            {
                PickAndSaveAttachment(row);
            }
            e.Handled = true;
        }

        private void PickAndSaveAttachment(EduDashboardRow row)
        {
            var dlg = new OpenFileDialog
            {
                Title = "첨부파일 선택",
                Filter = "모든 파일 (*.*)|*.*|PDF (*.pdf)|*.pdf|이미지 (*.png;*.jpg)|*.png;*.jpg"
            };
            if (dlg.ShowDialog() == true)
            {
                row.AttachmentPath = dlg.FileName;
                DatabaseHelper.UpdateEducationPlanAttachment(row.EduId, dlg.FileName);
            }
        }

        private void StatusPill_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement pill) return;
            if (pill.DataContext is not EduDashboardRow row) return;

            var menu = new ContextMenu { PlacementTarget = pill };

            var statuses = new[] { "대기", "신청완료", "진행", "완료", "취소" };
            var colors = new Dictionary<string, (string bg, string fg)>
            {
                ["대기"]    = ("#FEF3C7", "#D97706"),
                ["신청완료"] = ("#CCFBF1", "#0D9488"),
                ["진행"]    = ("#DBEAFE", "#2563EB"),
                ["완료"]    = ("#D1FAE5", "#059669"),
                ["취소"]    = ("#FEE2E2", "#DC2626"),
            };

            foreach (var s in statuses)
            {
                var (bg, fg) = colors[s];
                var item = new MenuItem
                {
                    Header = new Border
                    {
                        CornerRadius = new System.Windows.CornerRadius(10),
                        Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom(bg)!,
                        Padding = new Thickness(10, 3, 10, 3),
                        Child = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Children =
                            {
                                new System.Windows.Shapes.Ellipse
                                {
                                    Width = 7, Height = 7,
                                    Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom(fg)!,
                                    Margin = new Thickness(0,0,5,0),
                                    VerticalAlignment = VerticalAlignment.Center
                                },
                                new TextBlock
                                {
                                    Text = s, FontSize = 12, FontWeight = FontWeights.SemiBold,
                                    Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom(fg)!,
                                    VerticalAlignment = VerticalAlignment.Center
                                }
                            }
                        }
                    },
                    Tag = s
                };

                if (s == row.Status)
                    item.IsChecked = true;

                item.Click += (_, _) =>
                {
                    string newStatus = (string)item.Tag;
                    if (newStatus == row.Status) return;
                    row.Status = newStatus;
                    DatabaseHelper.UpdateEducationPlanStatus(row.EduId, newStatus);
                    UpdateStatusCards();
                    _view?.Refresh();
                };
                menu.Items.Add(item);
            }

            pill.ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }
}
