using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

namespace CleanPotal
{
    public class DispatchItemModel : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string VendorName { get; set; } = string.Empty;

        private string _outgoingDetails = "-";
        public string OutgoingDetails { get => _outgoingDetails; set { _outgoingDetails = value; OnPropertyChanged(nameof(OutgoingDetails)); } }

        private string _incomingDetails = "";
        public string IncomingDetails { get => _incomingDetails; set { _incomingDetails = value; OnPropertyChanged(nameof(IncomingDetails)); } }

        private string _note = "";
        public string Note { get => _note; set { _note = value; OnPropertyChanged(nameof(Note)); } }

        private string _managerName = "";
        public string ManagerName
        {
            get => _managerName;
            set { _managerName = value; OnPropertyChanged(nameof(ManagerName)); }
        }

        private string _fullAddress = "";
        public string FullAddress
        {
            get => _fullAddress;
            set { _fullAddress = value; OnPropertyChanged(nameof(FullAddress)); }
        }

        private string _contactNumber = "";
        public string ContactNumber
        {
            get => _contactNumber;
            set { _contactNumber = value; OnPropertyChanged(nameof(ContactNumber)); }
        }

        public ObservableCollection<AddressModel> AvailableAddresses { get; set; } = new ObservableCollection<AddressModel>();
        private AddressModel? _selectedAddress;
        public AddressModel? SelectedAddress { get => _selectedAddress; set { _selectedAddress = value; OnPropertyChanged(nameof(SelectedAddress)); } }

        public ObservableCollection<ManagerModel> AvailableManagers { get; set; } = new ObservableCollection<ManagerModel>();

        private ManagerModel? _selectedManager;
        public ManagerModel? SelectedManager
        {
            get => _selectedManager;
            set
            {
                _selectedManager = value;
                if (value != null)
                {
                    ManagerName = value.ManagerName;
                    ContactNumber = value.ContactNumber;
                }
                OnPropertyChanged(nameof(SelectedManager));
            }
        }

        public void LoadComboboxData(string dbContact = "")
        {
            ContactNumber = dbContact;
            AvailableAddresses.Clear();
            AvailableManagers.Clear();

            if (VendorName == "우암")
            {
                AvailableAddresses.Add(new AddressModel { IsMain = true, LocationName = "본사", FullAddress = "경기 안성시 미양면 강덕1길 138-4" });
                AvailableManagers.Add(new ManagerModel { ManagerName = "최남용", ContactNumber = "010-9008-3089" });
            }
            else if (VendorName == "영신")
            {
                AvailableAddresses.Add(new AddressModel { IsMain = true, LocationName = "본사", FullAddress = "충북 진천군 광혜원면 용소1길 10" });
                AvailableManagers.Add(new ManagerModel { ManagerName = "정성수", ContactNumber = "010-2375-1930" });
            }

            SelectedManager = AvailableManagers.FirstOrDefault(m => m.ManagerName == ManagerName);
            SelectedAddress = AvailableAddresses.FirstOrDefault(a => a.FullAddress == FullAddress);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }


    public partial class DispatchWindow : Window
    {
        private ObservableCollection<DispatchItemModel>? _newItems;
        private DateTime? _previousDate = null;

        public ObservableCollection<DispatchItemModel> DispatchItems { get; set; }

        public DispatchWindow(ObservableCollection<DispatchItemModel> newItems)
        {
            InitializeComponent();
            _newItems = newItems;
            DispatchItems = new ObservableCollection<DispatchItemModel>();
            DispatchDataGrid.ItemsSource = DispatchItems;

            TargetDatePicker.SelectedDate = DateTime.Today;
            this.Closing += DispatchWindow_Closing;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        private void TargetDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            // 🔥 CS8602 널 참조 경고 해결을 위한 안전망(null 처리) 대폭 강화
            DateTime? selectedDate = TargetDatePicker.SelectedDate;
            if (selectedDate == null) return;

            if (_previousDate != null && DispatchItems != null)
            {
                foreach (var item in DispatchItems)
                {
                    if (item.Id != 0) DatabaseHelper.UpdateDispatch(item, _previousDate.Value);
                }
            }

            DateTime target = selectedDate.Value;
            _previousDate = target;
            TxtTitleDate.Text = target.ToString("yyyy년 M월 d일 dddd 배차 LIST");

            DispatchItems?.Clear();

            var existingItems = DatabaseHelper.GetDispatchModelsByDate(target);
            if (existingItems != null && DispatchItems != null)
            {
                foreach (var item in existingItems) DispatchItems.Add(item);
            }

            if (_newItems != null && DispatchItems != null)
            {
                foreach (var item in _newItems) DispatchItems.Add(item);
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DispatchItemModel item)
            {
                DispatchItems?.Remove(item);
                if (item.Id != 0) DatabaseHelper.DeleteDispatch(item.Id);
                if (_newItems != null && _newItems.Contains(item)) _newItems.Remove(item);
            }
        }

        private void DispatchWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                DateTime target = TargetDatePicker.SelectedDate ?? DateTime.Today;
                if (DispatchItems != null && DispatchItems.Count > 0)
                {
                    foreach (var item in DispatchItems)
                    {
                        if (item.Id == 0) DatabaseHelper.InsertDispatch(item, target);
                        else DatabaseHelper.UpdateDispatch(item, target);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show($"저장 오류: {ex.Message}"); }
        }

        private void BtnCapture_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ManageColumn.Visibility = Visibility.Collapsed;

                int currentSelectedIndex = DispatchDataGrid.SelectedIndex;
                DispatchDataGrid.SelectedIndex = -1;

                Keyboard.ClearFocus();
                CaptureTarget.UpdateLayout();
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                RenderTargetBitmap rtb = new RenderTargetBitmap(
                    (int)CaptureTarget.ActualWidth,
                    (int)CaptureTarget.ActualHeight,
                    96, 96, PixelFormats.Pbgra32);

                DrawingVisual dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(new Point(0, 0), new Size(CaptureTarget.ActualWidth, CaptureTarget.ActualHeight)));
                    VisualBrush vb = new VisualBrush(CaptureTarget);
                    dc.DrawRectangle(vb, null, new Rect(new Point(0, 0), new Size(CaptureTarget.ActualWidth, CaptureTarget.ActualHeight)));
                }
                rtb.Render(dv);

                Clipboard.SetImage(rtb);

                ManageColumn.Visibility = Visibility.Visible;

                if (currentSelectedIndex != -1)
                {
                    DispatchDataGrid.SelectedIndex = currentSelectedIndex;
                }

                CaptureTarget.UpdateLayout();

                MessageBox.Show("배차표가 파란색 잔상 없이 깨끗하게 복사되었습니다.", "캡처 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ManageColumn.Visibility = Visibility.Visible;
                MessageBox.Show($"캡처 중 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}