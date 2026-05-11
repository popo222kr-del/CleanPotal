using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CleanPotal
{
    public partial class EduDashboardView : UserControl
    {
        private int _selectedYear = DateTime.Today.Year;
        private List<EduDashboardRow> _allRows = new();

        private static readonly string[] StatusOrder = { "전체", "대기", "신청완료", "진행", "완료", "취소" };

        public EduDashboardView()
        {
            InitializeComponent();

            StatusFilter.ItemsSource = StatusOrder;
            StatusFilter.SelectedIndex = 0;

            YearText.Text = _selectedYear.ToString();
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
                        TeamName = user?.TeamName ?? "-",
                        JobTitle = user?.JobTitle ?? "-",
                        CourseName = e.CourseName,
                        StartDate = e.StartDate,
                        EndDate = e.EndDate,
                        Status = e.Status ?? "",
                        Progress = e.Progress,
                        EduMethod = e.EduMethod ?? ""
                    };
                })
                .OrderBy(r => r.MemberName)
                .ThenBy(r => r.StartDate)
                .ToList();

            UpdateStatusCards();
            ApplyFilter();
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

        // ── 필터 적용 ──────────────────────────────────────────────────
        private void ApplyFilter()
        {
            string selected = StatusFilter.SelectedItem as string ?? "전체";
            var filtered = selected == "전체"
                ? _allRows
                : _allRows.Where(r => r.Status == selected).ToList();
            EduDataGrid.ItemsSource = filtered;
        }

        // ── 이벤트 ────────────────────────────────────────────────────
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

        private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

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
                    if (newStatus == "완료")
                    {
                        row.Progress = 100;
                        DatabaseHelper.UpdateEducationPlanStatus(row.EduId, newStatus, 100);
                    }
                    else
                    {
                        DatabaseHelper.UpdateEducationPlanStatus(row.EduId, newStatus);
                    }
                    UpdateStatusCards();
                    ApplyFilter();
                };
                menu.Items.Add(item);
            }

            pill.ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }
}
