using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CleanPotal
{
    public class DispatchItemModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _displayOrder;
        private string _vendorName = string.Empty;
        private string _outgoingDetails = "-";
        private string _incomingDetails = string.Empty;
        private string _note = string.Empty;
        private string _managerName = string.Empty;
        private string _fullAddress = string.Empty;
        private string _contactNumber = string.Empty;
        private AddressModel? _selectedAddress;
        private ManagerModel? _selectedManager;
        private bool _isReferenceSyncing;

        public int Id { get; set; }

        public int DisplayOrder
        {
            get => _displayOrder;
            set { _displayOrder = value; OnPropertyChanged(); }
        }

        // 🔥 핵심 해결: 입력 즉시 지워버리던 Trim() 삭제
        public string VendorName
        {
            get => _vendorName;
            set { if (_vendorName == value) return; _vendorName = value ?? string.Empty; OnPropertyChanged(); NotifyDerivedState(); }
        }

        public string OutgoingDetails
        {
            get => _outgoingDetails;
            set { _outgoingDetails = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string IncomingDetails
        {
            get => _incomingDetails;
            set { _incomingDetails = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string Note
        {
            get => _note;
            set { _note = value ?? string.Empty; OnPropertyChanged(); NotifyDerivedState(); }
        }

        public string ManagerName
        {
            get => _managerName;
            set { if (_managerName == value) return; _managerName = value ?? string.Empty; OnPropertyChanged(); NotifyDerivedState(); }
        }

        public string FullAddress
        {
            get => _fullAddress;
            set { if (_fullAddress == value) return; _fullAddress = value ?? string.Empty; OnPropertyChanged(); NotifyDerivedState(); }
        }

        public string ContactNumber
        {
            get => _contactNumber;
            set { if (_contactNumber == value) return; _contactNumber = value ?? string.Empty; OnPropertyChanged(); NotifyDerivedState(); }
        }

        public ObservableCollection<AddressModel> AvailableAddresses { get; } = new();
        public ObservableCollection<ManagerModel> AvailableManagers { get; } = new();

        public ObservableCollection<string> AvailableAddressTexts { get; } = new();
        public ObservableCollection<string> AvailableManagerNames { get; } = new();
        public ObservableCollection<string> AvailableContactNumbers { get; } = new();

        public bool IsManagerMissing => string.IsNullOrWhiteSpace(ManagerName);
        public bool IsContactMissing => string.IsNullOrWhiteSpace(ContactNumber);
        public bool IsAddressMissing => string.IsNullOrWhiteSpace(FullAddress);
        public bool IsVendorMissing => string.IsNullOrWhiteSpace(VendorName);

        public bool IsEffectivelyEmpty => string.IsNullOrWhiteSpace(VendorName)
                                         && (string.IsNullOrWhiteSpace(OutgoingDetails) || OutgoingDetails.Trim() == "-")
                                         && string.IsNullOrWhiteSpace(IncomingDetails)
                                         && string.IsNullOrWhiteSpace(ManagerName)
                                         && string.IsNullOrWhiteSpace(ContactNumber)
                                         && string.IsNullOrWhiteSpace(FullAddress)
                                         && string.IsNullOrWhiteSpace(Note);

        public bool IsUrgentNote => !string.IsNullOrWhiteSpace(Note) &&
                                    (Note.Contains("긴급", StringComparison.OrdinalIgnoreCase) ||
                                     Note.Contains("당일", StringComparison.OrdinalIgnoreCase) ||
                                     Note.Contains("확인", StringComparison.OrdinalIgnoreCase));

        public bool HasValidationIssue => !IsEffectivelyEmpty && (IsVendorMissing || IsManagerMissing || IsContactMissing || IsAddressMissing);

        public string ValidationSummary
        {
            get
            {
                var missing = new List<string>();
                if (IsVendorMissing && !IsEffectivelyEmpty) missing.Add("업체명 필요");
                if (IsManagerMissing) missing.Add("담당자 필요");
                if (IsContactMissing) missing.Add("연락처 필요");
                if (IsAddressMissing) missing.Add("주소 필요");
                return IsEffectivelyEmpty ? "빈 행" : missing.Count == 0 ? "정상" : string.Join(", ", missing);
            }
        }

        public string DraftStatusSummary
        {
            get
            {
                if (IsEffectivelyEmpty) return "빈 행";
                if (IsVendorMissing) return "업체 선택 필요";
                if (IsManagerMissing || IsContactMissing || IsAddressMissing) return "확인필요";
                if (IsUrgentNote) return "비고 확인";
                return "작성완료";
            }
        }

        public void LoadComboboxData(string dbContact = "", bool preserveTypedValues = false)
        {
            string managerName = preserveTypedValues ? ManagerName : string.Empty;
            string address = preserveTypedValues ? FullAddress : string.Empty;
            string contact = preserveTypedValues ? ContactNumber : dbContact;

            var vendor = VendorStore.FindByName(VendorName.Trim());

            _isReferenceSyncing = true;
            try
            {
                AvailableAddresses.Clear();
                AvailableManagers.Clear();
                AvailableAddressTexts.Clear();
                AvailableManagerNames.Clear();
                AvailableContactNumbers.Clear();

                if (vendor != null)
                {
                    foreach (var item in vendor.Addresses)
                    {
                        AvailableAddresses.Add(new AddressModel { IsMain = item.IsMain, LocationName = item.LocationName, FullAddress = item.FullAddress });
                        if (!string.IsNullOrWhiteSpace(item.FullAddress)) AvailableAddressTexts.Add(item.FullAddress);
                    }

                    foreach (var item in vendor.Managers)
                    {
                        AvailableManagers.Add(new ManagerModel { ManagerName = item.ManagerName, ContactNumber = item.ContactNumber });
                        if (!string.IsNullOrWhiteSpace(item.ManagerName) && !AvailableManagerNames.Contains(item.ManagerName))
                            AvailableManagerNames.Add(item.ManagerName);
                        if (!string.IsNullOrWhiteSpace(item.ContactNumber) && !AvailableContactNumbers.Contains(item.ContactNumber))
                            AvailableContactNumbers.Add(item.ContactNumber);
                    }
                }

                _selectedManager = AvailableManagers.FirstOrDefault(m => string.Equals(m.ManagerName, managerName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (_selectedManager == null && !string.IsNullOrWhiteSpace(contact))
                    _selectedManager = AvailableManagers.FirstOrDefault(m => string.Equals(m.ContactNumber, contact.Trim(), StringComparison.OrdinalIgnoreCase));

                _selectedAddress = AvailableAddresses.FirstOrDefault(a => string.Equals(a.FullAddress, address.Trim(), StringComparison.OrdinalIgnoreCase));

                if (_selectedManager == null && string.IsNullOrWhiteSpace(managerName) && string.IsNullOrWhiteSpace(contact) && AvailableManagers.Count == 1)
                    _selectedManager = AvailableManagers[0];
                if (_selectedAddress == null && string.IsNullOrWhiteSpace(address))
                    _selectedAddress = AvailableAddresses.FirstOrDefault(a => a.IsMain) ?? AvailableAddresses.FirstOrDefault();
            }
            finally
            {
                _isReferenceSyncing = false;
            }

            if (_selectedManager != null) { ManagerName = _selectedManager.ManagerName; ContactNumber = _selectedManager.ContactNumber; }
            else { ManagerName = managerName; ContactNumber = contact; }
            if (_selectedAddress != null) FullAddress = _selectedAddress.FullAddress;
            else FullAddress = address;

            CommitReferenceSelection();
            NotifyDerivedState();
        }

        // 🔥 저장할 때만 앞뒤 쓸데없는 공백을 쳐내도록 변경
        public void Normalize()
        {
            VendorName = VendorName.Trim();
            OutgoingDetails = string.IsNullOrWhiteSpace(OutgoingDetails) ? "-" : OutgoingDetails.Trim();
            IncomingDetails = IncomingDetails.Trim();
            ManagerName = ManagerName.Trim();
            ContactNumber = ContactNumber.Trim();
            FullAddress = FullAddress.Trim();
            Note = Note.Trim();
        }

        public void CommitReferenceSelection()
        {
            if (_isReferenceSyncing) return;
            _isReferenceSyncing = true;
            try
            {
                var mMatch = AvailableManagers.FirstOrDefault(m => string.Equals(m.ManagerName, _managerName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (mMatch != null && !string.Equals(_contactNumber.Trim(), mMatch.ContactNumber, StringComparison.OrdinalIgnoreCase))
                {
                    _contactNumber = mMatch.ContactNumber ?? string.Empty;
                    OnPropertyChanged(nameof(ContactNumber));
                }

                var cMatch = AvailableManagers.FirstOrDefault(m => string.Equals(m.ContactNumber, _contactNumber.Trim(), StringComparison.OrdinalIgnoreCase));
                if (cMatch != null && !string.Equals(_managerName.Trim(), cMatch.ManagerName, StringComparison.OrdinalIgnoreCase))
                {
                    _managerName = cMatch.ManagerName ?? string.Empty;
                    OnPropertyChanged(nameof(ManagerName));
                }
            }
            finally { _isReferenceSyncing = false; }
            NotifyDerivedState();
        }

        public string BuildDuplicateKey() => string.Join("|", VendorName.Trim().ToUpperInvariant(), OutgoingDetails.Trim().ToUpperInvariant(), IncomingDetails.Trim().ToUpperInvariant());

        private void NotifyDerivedState()
        {
            OnPropertyChanged(nameof(IsManagerMissing)); OnPropertyChanged(nameof(IsContactMissing)); OnPropertyChanged(nameof(IsAddressMissing));
            OnPropertyChanged(nameof(IsVendorMissing)); OnPropertyChanged(nameof(IsEffectivelyEmpty)); OnPropertyChanged(nameof(IsUrgentNote));
            OnPropertyChanged(nameof(HasValidationIssue)); OnPropertyChanged(nameof(ValidationSummary)); OnPropertyChanged(nameof(DraftStatusSummary));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class DispatchWindow : Window, INotifyPropertyChanged
    {
        private ObservableCollection<DispatchItemModel> _pendingImportedItems;
        private bool _isLoadingDate;
        private bool _isDirty;
        private DateTime _currentDate = DateTime.Today;
        private string _summaryText = string.Empty;
        private string _validationSummaryText = string.Empty;
        private string _saveStateText = "초안 저장 전";

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<DispatchItemModel> DispatchItems { get; } = new();
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

        public DispatchWindow(ObservableCollection<DispatchItemModel> newItems)
        {
            InitializeComponent();
            DataContext = this;

            _pendingImportedItems = new ObservableCollection<DispatchItemModel>(newItems.Select(x => CloneAsEditableItem(x)));

            DispatchDataGrid.ItemsSource = DispatchItems;
            DispatchItems.CollectionChanged += DispatchItems_CollectionChanged;

            RefreshVendorOptions();
            Closing += DispatchWindow_Closing;

            _isLoadingDate = true;
            try { TargetDatePicker.SelectedDate = DateTime.Today.AddDays(1); }
            finally { _isLoadingDate = false; }

            LoadForDate(DateTime.Today.AddDays(1));
        }

        private static string BuildTitleDateText(DateTime targetDate) => $"{targetDate:yyyy년 M월 d일} ({targetDate:dddd}) 배차 LIST";

        private static DispatchItemModel CloneAsEditableItem(DispatchItemModel source)
        {
            var clone = new DispatchItemModel
            {
                Id = 0,
                VendorName = source.VendorName,
                OutgoingDetails = source.OutgoingDetails,
                IncomingDetails = source.IncomingDetails,
                Note = source.Note,
                ManagerName = source.ManagerName,
                ContactNumber = source.ContactNumber,
                FullAddress = source.FullAddress
            };
            clone.LoadComboboxData(source.ContactNumber, preserveTypedValues: true);
            return clone;
        }

        private void DispatchItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (DispatchItemModel item in e.OldItems) item.PropertyChanged -= DispatchItem_PropertyChanged;
            if (e.NewItems != null) foreach (DispatchItemModel item in e.NewItems) item.PropertyChanged += DispatchItem_PropertyChanged;

            RefreshDisplayOrder();
            if (!_isLoadingDate) MarkDirty("항목 변경됨");
            UpdateSummary();
        }

        private void DispatchItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isLoadingDate) return;
            if (sender is not DispatchItemModel item) return;

            MarkDirty($"{item.DisplayOrder}번 행 수정됨");
            UpdateSummary();
        }

        private void TargetDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingDate) return;
            if (TargetDatePicker.SelectedDate is not DateTime selectedDate) return;

            var unsavedNewItems = DispatchItems.Where(x => x.Id == 0 && !x.IsEffectivelyEmpty).ToList();

            _isLoadingDate = true;
            try
            {
                _currentDate = selectedDate.Date;
                TxtTitleDate.Text = BuildTitleDateText(selectedDate);

                DispatchItems.Clear();

                foreach (var item in DatabaseHelper.GetDispatchModelsByDate(selectedDate))
                {
                    item.LoadComboboxData(item.ContactNumber, preserveTypedValues: true);
                    DispatchItems.Add(item);
                }

                foreach (var newItem in unsavedNewItems)
                {
                    DispatchItems.Add(newItem);
                }

                if (_pendingImportedItems.Count > 0)
                {
                    MergePendingItems();
                    _pendingImportedItems.Clear();
                    SaveStateText = "새 항목 병합 완료";
                }
                else
                {
                    SaveStateText = unsavedNewItems.Count > 0 ? "날짜 이동됨 (저장 필요)" : "날짜별 배차표를 불러왔습니다.";
                }

                _isDirty = unsavedNewItems.Count > 0;
                RefreshDisplayOrder();
                UpdateSummary();
            }
            finally { _isLoadingDate = false; }
        }

        private void LoadForDate(DateTime targetDate)
        {
            _isLoadingDate = true;
            try
            {
                _currentDate = targetDate.Date;
                TxtTitleDate.Text = BuildTitleDateText(targetDate);

                DispatchItems.Clear();
                foreach (var item in DatabaseHelper.GetDispatchModelsByDate(targetDate))
                {
                    item.LoadComboboxData(item.ContactNumber, preserveTypedValues: true);
                    DispatchItems.Add(item);
                }

                if (_pendingImportedItems.Count > 0)
                {
                    MergePendingItems();
                    _pendingImportedItems.Clear();
                    SaveStateText = "새 항목 병합 완료";
                }
                else { SaveStateText = "날짜별 배차표를 불러왔습니다."; }

                _isDirty = false;
                RefreshDisplayOrder();
                UpdateSummary();
            }
            finally { _isLoadingDate = false; }
        }

        private void MergePendingItems()
        {
            foreach (var item in _pendingImportedItems)
            {
                item.Normalize();
                item.LoadComboboxData(item.ContactNumber, preserveTypedValues: true);
                DispatchItems.Add(item);
            }
        }

        // 🔥 완벽 우회: Alt+Enter 누르면 즉시 줄바꿈 후 가로채기 차단
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
            var item = new DispatchItemModel { VendorName = string.Empty, OutgoingDetails = "-", IncomingDetails = string.Empty, Note = string.Empty, ManagerName = string.Empty, ContactNumber = string.Empty, FullAddress = string.Empty };
            item.LoadComboboxData();
            DispatchItems.Add(item);
            DispatchDataGrid.SelectedItem = item;
            DispatchDataGrid.ScrollIntoView(item);
            MarkDirty("새 행 추가");
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not DispatchItemModel item) return;
            if (MessageBox.Show($"{item.VendorName} 행을 삭제하시겠습니까?", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            DispatchItems.Remove(item);
            if (item.Id != 0) DatabaseHelper.DeleteDispatch(item.Id);
            MarkDirty("행 삭제");
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveCurrentDate(showMessage: true);

        private void BtnTomorrow_Click(object sender, RoutedEventArgs e) => ChangeDate(1, absoluteToday: true);
        private void BtnDayAfterTomorrow_Click(object sender, RoutedEventArgs e) => ChangeDate(2, absoluteToday: true);

        private void ChangeDate(int dayOffset, bool absoluteToday = false)
        {
            DateTime baseDate = absoluteToday ? DateTime.Today : (TargetDatePicker.SelectedDate ?? DateTime.Today);
            TargetDatePicker.SelectedDate = absoluteToday ? DateTime.Today.AddDays(dayOffset) : baseDate.AddDays(dayOffset);
        }

        private bool SaveCurrentDate(bool showMessage)
        {
            try
            {
                foreach (var item in DispatchItems.ToList()) item.Normalize();

                foreach (var empty in DispatchItems.Where(x => x.IsEffectivelyEmpty).ToList())
                {
                    if (empty.Id != 0) DatabaseHelper.DeleteDispatch(empty.Id);
                    DispatchItems.Remove(empty);
                }

                var invalidRows = DispatchItems.Where(x => x.HasValidationIssue).Select(x => x.DisplayOrder).ToList();
                DateTime target = TargetDatePicker.SelectedDate ?? _currentDate;
                foreach (var item in DispatchItems)
                {
                    if (item.Id == 0) item.Id = DatabaseHelper.InsertDispatch(item, target);
                    else DatabaseHelper.UpdateDispatch(item, target);
                }

                _isDirty = false;

                SaveStateText = invalidRows.Count > 0
                    ? $"초안 저장 완료 · 확인 필요 행 {invalidRows.Count}건"
                    : $"초안 저장 완료: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                UpdateSummary();

                if (showMessage)
                {
                    string message = invalidRows.Count > 0
                        ? $"필수값 확인이 필요한 행 {invalidRows.Count}건이 있지만 초안으로 저장했습니다.\n대상 행: {string.Join(", ", invalidRows)}"
                        : "배차 초안이 저장되었습니다.";
                    MessageBox.Show(message, "초안 저장", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return true;
            }
            catch (Exception ex)
            {
                SaveStateText = "저장 실패";
                if (showMessage) MessageBox.Show($"초안 저장 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void DispatchWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_isDirty) return;
            if (!SaveCurrentDate(showMessage: false))
            {
                e.Cancel = true;
                MessageBox.Show("저장에 실패하여 창을 닫지 않았습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateSummary()
        {
            int total = DispatchItems.Count;
            int invalid = DispatchItems.Count(x => x.HasValidationIssue);
            int urgent = DispatchItems.Count(x => x.IsUrgentNote);
            SummaryText = $"총 {total}건 · 누락 행 {invalid}건 · 긴급/확인 비고 {urgent}건";
        }

        private void RefreshDisplayOrder()
        {
            for (int i = 0; i < DispatchItems.Count; i++) DispatchItems[i].DisplayOrder = i + 1;
        }

        private void RefreshVendorOptions()
        {
            var names = VendorStore.Load().Select(v => v.VendorName).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
            VendorNameOptions.Clear();
            foreach (var name in names) VendorNameOptions.Add(name);
        }

        private void MarkDirty(string reason)
        {
            if (_isLoadingDate) return;
            _isDirty = true;
            SaveStateText = $"초안 변경됨 · {reason}";
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
                    if (col.ActualWidth > 0)
                    {
                        col.Width = new DataGridLength(col.ActualWidth, DataGridLengthUnitType.Pixel);
                        exactTableWidth += col.ActualWidth;
                    }
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

                CaptureTarget.Width = double.NaN;
                CaptureTarget.Height = double.NaN;
                DispatchDataGrid.Width = exactTableWidth;
                ListContainer.Width = exactTableWidth + ListContainer.BorderThickness.Left + ListContainer.BorderThickness.Right;

                CaptureTarget.Background = Brushes.White;
                DispatchDataGrid.SelectedIndex = -1;
                Keyboard.ClearFocus();

                CaptureTarget.UpdateLayout();

                // Hide ComboBox dropdown arrows for a clean capture
                foreach (var combo in FindVisualChildren<ComboBox>(DispatchDataGrid))
                {
                    combo.ApplyTemplate();
                    foreach (var toggleBtn in FindVisualChildren<System.Windows.Controls.Primitives.ToggleButton>(combo))
                        toggleBtn.Visibility = Visibility.Collapsed;
                }

                CaptureTarget.UpdateLayout();
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                Point listTopLeft = ListContainer.TranslatePoint(new Point(0, 0), CaptureTarget);
                int cw = Math.Max(1, (int)Math.Ceiling(ListContainer.ActualWidth));
                int ch = Math.Max(1, (int)Math.Ceiling(listTopLeft.Y + ListContainer.ActualHeight));

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
                MessageBox.Show("배차 초안이 복사되었습니다.", "캡처 완료", MessageBoxButton.OK, MessageBoxImage.Information);
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

                // Restore ComboBox dropdown arrows
                foreach (var combo in FindVisualChildren<ComboBox>(DispatchDataGrid))
                {
                    foreach (var toggleBtn in FindVisualChildren<System.Windows.Controls.Primitives.ToggleButton>(combo))
                        toggleBtn.Visibility = Visibility.Visible;
                }

                CaptureTarget.UpdateLayout();
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var grandchild in FindVisualChildren<T>(child))
                    yield return grandchild;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}