using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CleanPotal
{
    public partial class DispatchHistoryWindow : Window
    {
        public ObservableCollection<DispatchItemModel> HistoryItems { get; set; } = new ObservableCollection<DispatchItemModel>();

        public DispatchHistoryWindow()
        {
            InitializeComponent();
            DispatchDataGrid.ItemsSource = HistoryItems;
            HistoryDatePicker.SelectedDate = DateTime.Today;
            this.Closing += DispatchHistoryWindow_Closing;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();

        private void HistoryDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            DateTime? selectedDate = HistoryDatePicker.SelectedDate;
            if (selectedDate == null) return;

            DateTime target = selectedDate.Value;
            TxtTitleDate.Text = target.ToString("yyyy년 M월 d일 dddd 배차 LIST");

            HistoryItems.Clear();
            var dbItems = DatabaseHelper.GetDispatchModelsByDate(target);
            if (dbItems != null)
            {
                foreach (var item in dbItems) HistoryItems.Add(item);
            }
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DispatchItemModel item)
            {
                if (MessageBox.Show("해당 업체를 배차표에서 삭제하시겠습니까?", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    HistoryItems.Remove(item);
                    if (item.Id != 0) DatabaseHelper.DeleteDispatch(item.Id);
                }
            }
        }

        private void DispatchHistoryWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                if (HistoryDatePicker.SelectedDate == null) return;
                DateTime target = HistoryDatePicker.SelectedDate.Value;
                if (HistoryItems != null)
                {
                    foreach (var item in HistoryItems)
                    {
                        if (item.Id != 0) DatabaseHelper.UpdateDispatch(item, target);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show($"수정 내역 저장 중 오류: {ex.Message}"); }
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
                MessageBox.Show($"캡처 오류: {ex.Message}");
            }
        }
    }
}