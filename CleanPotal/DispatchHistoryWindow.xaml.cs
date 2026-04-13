using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CleanPotal
{
    public partial class DispatchHistoryWindow : Window, INotifyPropertyChanged
    {
        private bool _isLoadingDate;
        private bool _isDirty;
        private DateTime _currentDate = DateTime.Today;
        private string _summaryText = string.Empty;
        private string _validationSummaryText = string.Empty;
        private string _saveStateText = "저장 전";

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<DispatchItemModel> HistoryItems { get; } = new();
        public ObservableCollection<string> VendorNameOptions { get; } = new();

        public string SummaryText
        {
            get => _summaryText;
            set { _summaryText = value; OnPropertyChanged(); }
        }

        public string ValidationSummaryText
        {
            get => _validationSummaryText;
            set { _validationSummaryText = value; OnPropertyChanged(); }
        }

        public string SaveStateText
        {
            get => _saveStateText;
            set { _saveStateText = value; OnPropertyChanged(); }
        }

        public DispatchHistoryWindow()
        {
            InitializeComponent();
            DataContext = this;
            DispatchDataGrid.ItemsSource = HistoryItems;
            HistoryItems.CollectionChanged += HistoryItems_CollectionChanged;
            RefreshVendorOptions();
            Closing += DispatchHistoryWindow_Closing;

            _isLoadingDate = true;
            try
            {
                HistoryDatePicker.SelectedDate = DateTime.Today;
            }
            finally
            {
                _isLoadingDate = false;
            }

            LoadForDate(DateTime.Today);
        }

        private static string BuildTitleDateText(DateTime targetDate) => $"{targetDate:yyyy년 M월 d일} ({targetDate:dddd}) 배차 LIST";

        private void HistoryItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (DispatchItemModel item in e.OldItems)
                    item.PropertyChanged -= HistoryItem_PropertyChanged;

            if (e.NewItems != null)
                foreach (DispatchItemModel item in e.NewItems)
                    item.PropertyChanged += HistoryItem_PropertyChanged;

            RefreshDisplayOrder();
            if (!_isLoadingDate) MarkDirty("이력 변경됨");
            UpdateSummary();
        }

        private void HistoryItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isLoadingDate) return;
            if (sender is not DispatchItemModel item) return;

            MarkDirty($"{item.DisplayOrder}번 행 수정됨");
            UpdateSummary();
        }

        private void HistoryDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingDate) return;

            if (HistoryDatePicker.SelectedDate is not DateTime selectedDate) return;

            if (!_isLoadingDate && _isDirty)
            {
                if (!SaveCurrentDate(showMessage: false))
                {
                    _isLoadingDate = true;
                    try { HistoryDatePicker.SelectedDate = _currentDate; }
                    finally { _isLoadingDate = false; }
                    return;
                }
            }
            LoadForDate(selectedDate);
        }

        private void LoadForDate(DateTime targetDate)
        {
            _isLoadingDate = true;
            try
            {
                _currentDate = targetDate.Date;
                TxtTitleDate.Text = BuildTitleDateText(targetDate);
                HistoryItems.Clear();
                foreach (var item in DatabaseHelper.GetDispatchModelsByDate(targetDate))
                {
                    item.LoadComboboxData(item.ContactNumber, preserveTypedValues: true);
                    HistoryItems.Add(item);
                }
                _isDirty = false;
                SaveStateText = "이력 데이터를 불러왔습니다.";
                RefreshDisplayOrder();
                UpdateSummary();
            }
            finally
            {
                _isLoadingDate = false;
            }
        }

        private bool SaveCurrentDate(bool showMessage)
        {
            try
            {
                foreach (var item in HistoryItems.ToList()) item.Normalize();

                foreach (var empty in HistoryItems.Where(x => x.IsEffectivelyEmpty).ToList())
                {
                    if (empty.Id != 0) DatabaseHelper.DeleteDispatch(empty.Id);
                    HistoryItems.Remove(empty);
                }

                var invalidRows = HistoryItems.Where(x => x.HasValidationIssue).Select(x => x.DisplayOrder).ToList();
                if (invalidRows.Count > 0)
                {
                    SaveStateText = "저장 보류 · 필수값 확인 필요";
                    if (showMessage)
                        MessageBox.Show($"필수값이 비어 있는 행이 있습니다: {string.Join(", ", invalidRows)}", "검증 필요", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                DateTime target = HistoryDatePicker.SelectedDate ?? _currentDate;
                foreach (var item in HistoryItems)
                {
                    if (item.Id == 0) item.Id = DatabaseHelper.InsertDispatch(item, target);
                    else DatabaseHelper.UpdateDispatch(item, target);
                }
                _isDirty = false;
                SaveStateText = $"변경 저장 완료: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                UpdateSummary();
                if (showMessage)
                    MessageBox.Show("배차 이력이 저장되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                SaveStateText = "저장 실패";
                if (showMessage)
                    MessageBox.Show($"수정 내역 저장 중 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // 🔥 확실한 우회: Alt+Enter를 누르면 무조건 줄을 바꾸고 DataGrid 뺏어가는 걸 차단
        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                if (sender is TextBox textBox)
                {
                    int caretIndex = textBox.CaretIndex;
                    textBox.Text = textBox.Text.Insert(caretIndex, Environment.NewLine);
                    textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                    e.Handled = true;
                }
            }
        }

        private void BtnAddRow_Click(object sender, RoutedEventArgs e)
        {
            var item = new DispatchItemModel
            {
                VendorName = string.Empty,
                OutgoingDetails = "-",
                IncomingDetails = string.Empty,
                Note = string.Empty,
                ManagerName = string.Empty,
                ContactNumber = string.Empty,
                FullAddress = string.Empty
            };
            item.LoadComboboxData();
            HistoryItems.Add(item);
            DispatchDataGrid.SelectedItem = item;
            DispatchDataGrid.ScrollIntoView(item);
            MarkDirty("새 행 추가");
        }

        private void BtnCarryOver_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not DispatchItemModel item) return;

            if (MessageBox.Show($"[{item.VendorName}] 배차 항목을 내일 날짜({_currentDate.AddDays(1):MM-dd})로 이월하시겠습니까?\n(현재 표에서는 삭제됩니다.)", "이월 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                if (item.Id != 0) DatabaseHelper.DeleteDispatch(item.Id);
                HistoryItems.Remove(item);

                item.Id = 0;
                DatabaseHelper.InsertDispatch(item, _currentDate.AddDays(1));

                MarkDirty("이월 처리됨");
                MessageBox.Show("내일 배차표로 이월이 완료되었습니다.", "이월 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"이월 처리 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not DispatchItemModel item) return;
            if (MessageBox.Show("해당 업체를 배차표에서 삭제하시겠습니까?", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            HistoryItems.Remove(item);
            if (item.Id != 0) DatabaseHelper.DeleteDispatch(item.Id);
            MarkDirty("행 삭제");
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveCurrentDate(showMessage: true);
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void BtnYesterday_Click(object sender, RoutedEventArgs e) => ChangeDate(-1);
        private void BtnToday_Click(object sender, RoutedEventArgs e) => ChangeDate(0, absoluteToday: true);
        private void BtnTomorrow_Click(object sender, RoutedEventArgs e) => ChangeDate(1);

        private void ChangeDate(int dayOffset, bool absoluteToday = false)
        {
            DateTime baseDate = absoluteToday ? DateTime.Today : (HistoryDatePicker.SelectedDate ?? DateTime.Today);
            HistoryDatePicker.SelectedDate = absoluteToday ? DateTime.Today : baseDate.AddDays(dayOffset);
        }

        private void DispatchHistoryWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_isDirty) return;
            if (!SaveCurrentDate(showMessage: false))
            {
                e.Cancel = true;
                MessageBox.Show("저장에 실패하여 창을 닫지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshDisplayOrder()
        {
            for (int i = 0; i < HistoryItems.Count; i++)
                HistoryItems[i].DisplayOrder = i + 1;
        }

        private void RefreshVendorOptions()
        {
            var names = VendorStore.Load().Select(v => v.VendorName).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
            VendorNameOptions.Clear();
            foreach (var name in names) VendorNameOptions.Add(name);
        }

        private void UpdateSummary()
        {
            int total = HistoryItems.Count;
            int invalid = HistoryItems.Count(x => x.HasValidationIssue);
            int urgent = HistoryItems.Count(x => x.IsUrgentNote);
            SummaryText = $"총 {total}건 · 누락 행 {invalid}건 · 긴급/확인 비고 {urgent}건";

            int missingVendor = HistoryItems.Count(x => x.IsVendorMissing && !x.IsEffectivelyEmpty);
            int missingManager = HistoryItems.Count(x => x.IsManagerMissing && !x.IsEffectivelyEmpty);
            int missingContact = HistoryItems.Count(x => x.IsContactMissing && !x.IsEffectivelyEmpty);
            int missingAddress = HistoryItems.Count(x => x.IsAddressMissing && !x.IsEffectivelyEmpty);
            ValidationSummaryText = $"업체명 누락 {missingVendor}건 · 담당자 누락 {missingManager}건 · 연락처 누락 {missingContact}건 · 주소 누락 {missingAddress}건";
        }

        private void MarkDirty(string reason)
        {
            if (_isLoadingDate) return;
            _isDirty = true;
            SaveStateText = $"변경 필요 · {reason}";
        }

        private static void UpdateComboTextBinding(ComboBox combo)
        {
            combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        }

        private static DispatchItemModel? GetItemFromCombo(object sender) => (sender as FrameworkElement)?.DataContext as DispatchItemModel;

        private void VendorCombo_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox combo) UpdateComboTextBinding(combo);
            if (GetItemFromCombo(sender) is DispatchItemModel item) item.LoadComboboxData(item.ContactNumber, preserveTypedValues: true);
        }

        private void VendorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.SelectedItem != null)
            {
                combo.Text = combo.SelectedItem.ToString();
                UpdateComboTextBinding(combo);
            }
            if (GetItemFromCombo(sender) is DispatchItemModel item) item.LoadComboboxData(item.ContactNumber, preserveTypedValues: true);
        }

        private void ManagerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.SelectedItem != null)
            {
                combo.Text = combo.SelectedItem.ToString();
                UpdateComboTextBinding(combo);
            }
            GetItemFromCombo(sender)?.CommitReferenceSelection();
        }

        private void ManagerCombo_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox combo) UpdateComboTextBinding(combo);
            GetItemFromCombo(sender)?.CommitReferenceSelection();
        }

        private void ContactCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.SelectedItem != null)
            {
                combo.Text = combo.SelectedItem.ToString();
                UpdateComboTextBinding(combo);
            }
            GetItemFromCombo(sender)?.CommitReferenceSelection();
        }

        private void ContactCombo_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox combo) UpdateComboTextBinding(combo);
            GetItemFromCombo(sender)?.CommitReferenceSelection();
        }

        private void AddressCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.SelectedItem != null)
            {
                combo.Text = combo.SelectedItem.ToString();
                UpdateComboTextBinding(combo);
            }
            GetItemFromCombo(sender)?.CommitReferenceSelection();
        }

        private void AddressCombo_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox combo) UpdateComboTextBinding(combo);
            GetItemFromCombo(sender)?.CommitReferenceSelection();
        }

        private void ReferenceCombo_DropDownClosed(object sender, EventArgs e)
        {
            if (sender is ComboBox combo) UpdateComboTextBinding(combo);
            GetItemFromCombo(sender)?.CommitReferenceSelection();
        }

        private void ReferenceCombo_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is ComboBox combo) UpdateComboTextBinding(combo);
            GetItemFromCombo(sender)?.CommitReferenceSelection();
        }

        private void BtnCapture_Click(object sender, RoutedEventArgs e)
        {
            Brush originalBg = CaptureTarget.Background;
            Visibility originalMainTitleVisibility = MainTitleText.Visibility;
            Visibility originalActionButtonsVisibility = ActionButtonsPanel.Visibility;
            Visibility originalSummaryVisibility = SummaryCardsPanel.Visibility;
            Visibility originalStatusVisibility = StatusColumn.Visibility;
            Visibility originalManageVisibility = ManageColumn.Visibility;

            HorizontalAlignment origTargetAlign = CaptureTarget.HorizontalAlignment;
            VerticalAlignment origTargetVAlign = CaptureTarget.VerticalAlignment;
            HorizontalAlignment origTitleAlign = TitleGrid.HorizontalAlignment;
            HorizontalAlignment origListAlign = ListContainer.HorizontalAlignment;
            HorizontalAlignment origGridAlign = DispatchDataGrid.HorizontalAlignment;

            Thickness origTitleMargin = TitleGrid.Margin;
            Thickness origListMargin = ListContainer.Margin;
            Thickness origDateMargin = TxtTitleDate.Margin;

            var origColWidths = DispatchDataGrid.Columns.ToDictionary(c => c, c => c.Width);
            GridLength origRow2Height = CaptureTarget.RowDefinitions[2].Height;
            int selectedIndex = DispatchDataGrid.SelectedIndex;

            try
            {
                CaptureTarget.UpdateLayout();

                double exactTableWidth = 0;
                foreach (var col in DispatchDataGrid.Columns)
                {
                    if (col == StatusColumn || col == ManageColumn) continue;
                    if (col.ActualWidth <= 0) continue;

                    col.Width = new DataGridLength(col.ActualWidth, DataGridLengthUnitType.Pixel);
                    exactTableWidth += col.ActualWidth;
                }

                MainTitleText.Visibility = Visibility.Collapsed;
                ActionButtonsPanel.Visibility = Visibility.Collapsed;
                SummaryCardsPanel.Visibility = Visibility.Collapsed;
                StatusColumn.Visibility = Visibility.Collapsed;
                ManageColumn.Visibility = Visibility.Collapsed;

                TitleGrid.Margin = new Thickness(0, 0, 0, 10);
                ListContainer.Margin = new Thickness(0);
                TxtTitleDate.Margin = new Thickness(0);

                CaptureTarget.HorizontalAlignment = HorizontalAlignment.Left;
                CaptureTarget.VerticalAlignment = VerticalAlignment.Top;
                TitleGrid.HorizontalAlignment = HorizontalAlignment.Left;
                ListContainer.HorizontalAlignment = HorizontalAlignment.Left;
                DispatchDataGrid.HorizontalAlignment = HorizontalAlignment.Left;
                CaptureTarget.RowDefinitions[2].Height = GridLength.Auto;

                if (exactTableWidth > 0)
                {
                    DispatchDataGrid.Width = exactTableWidth;
                    ListContainer.Width = exactTableWidth + ListContainer.BorderThickness.Left + ListContainer.BorderThickness.Right;
                }

                CaptureTarget.Background = Brushes.White;
                DispatchDataGrid.SelectedIndex = -1;
                Keyboard.ClearFocus();

                CaptureTarget.UpdateLayout();
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                Point listTopLeft = ListContainer.TranslatePoint(new Point(0, 0), CaptureTarget);
                double clipWidth = Math.Ceiling(ListContainer.ActualWidth);
                double clipHeight = Math.Ceiling(listTopLeft.Y + ListContainer.ActualHeight);

                int cw = Math.Max(1, (int)clipWidth);
                int ch = Math.Max(1, (int)clipHeight);

                RenderTargetBitmap rtb = new(cw, ch, 96, 96, PixelFormats.Pbgra32);
                DrawingVisual dv = new();

                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, cw, ch));
                    var vb = new VisualBrush(CaptureTarget)
                    {
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top
                    };
                    dc.DrawRectangle(vb, null, new Rect(0, 0, cw, ch));
                }

                rtb.Render(dv);
                Clipboard.SetImage(rtb);

                MessageBox.Show("배차 이력이 클립보드에 복사되었습니다.", "캡처 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"캡처 중 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                foreach (var pair in origColWidths) pair.Key.Width = pair.Value;

                MainTitleText.Visibility = originalMainTitleVisibility;
                ActionButtonsPanel.Visibility = originalActionButtonsVisibility;
                SummaryCardsPanel.Visibility = originalSummaryVisibility;
                StatusColumn.Visibility = originalStatusVisibility;
                ManageColumn.Visibility = originalManageVisibility;

                TitleGrid.Margin = origTitleMargin;
                ListContainer.Margin = origListMargin;
                TxtTitleDate.Margin = origDateMargin;

                CaptureTarget.HorizontalAlignment = origTargetAlign;
                CaptureTarget.VerticalAlignment = origTargetVAlign;
                TitleGrid.HorizontalAlignment = origTitleAlign;
                ListContainer.HorizontalAlignment = origListAlign;
                DispatchDataGrid.HorizontalAlignment = origGridAlign;

                CaptureTarget.RowDefinitions[2].Height = origRow2Height;
                CaptureTarget.Background = originalBg;

                CaptureTarget.Width = double.NaN;
                CaptureTarget.Height = double.NaN;
                DispatchDataGrid.Width = double.NaN;
                ListContainer.Width = double.NaN;

                if (selectedIndex >= 0 && selectedIndex < DispatchDataGrid.Items.Count)
                    DispatchDataGrid.SelectedIndex = selectedIndex;

                CaptureTarget.UpdateLayout();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}