using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    public partial class QuotationView : UserControl, INotifyPropertyChanged
    {
        public ObservableCollection<QuotationModel> Quotations { get; } = new();

        private QuotationModel? _currentQuotation;
        public QuotationModel? CurrentQuotation
        {
            get => _currentQuotation;
            set
            {
                if (_currentQuotation != null)
                {
                    _currentQuotation.LineItems.CollectionChanged -= LineItems_CollectionChanged;
                    foreach (var item in _currentQuotation.LineItems)
                        item.PropertyChanged -= LineItem_PropertyChanged;
                }
                _currentQuotation = value;
                if (_currentQuotation != null)
                {
                    _currentQuotation.LineItems.CollectionChanged += LineItems_CollectionChanged;
                    foreach (var item in _currentQuotation.LineItems)
                        item.PropertyChanged += LineItem_PropertyChanged;
                    LineItemsGrid.ItemsSource = _currentQuotation.LineItems;
                }
                else
                {
                    LineItemsGrid.ItemsSource = null;
                }
                OnPropertyChanged(nameof(CurrentQuotation));
                OnPropertyChanged(nameof(HasQuotation));
                UpdateTotals();
            }
        }

        public bool HasQuotation => CurrentQuotation != null;

        private decimal _totalAmount;
        public decimal TotalAmount { get => _totalAmount; set { _totalAmount = value; OnPropertyChanged(nameof(TotalAmount)); } }

        private int _totalQty;
        public int TotalQty { get => _totalQty; set { _totalQty = value; OnPropertyChanged(nameof(TotalQty)); } }

        private ObservableCollection<ProductMasterItem> _productMaster = new();

        public QuotationView()
        {
            InitializeComponent();
            DataContext = this;

            var saved = QuotationStore.LoadQuotations();
            foreach (var q in saved) Quotations.Add(q);

            _productMaster = QuotationStore.LoadProductMaster();
            ProductMasterGrid.ItemsSource = _productMaster;

            QuotationListBox.ItemsSource = Quotations;
            if (Quotations.Count > 0) QuotationListBox.SelectedIndex = 0;
        }

        public void TryRefresh() { }

        // ─── 이벤트 핸들러: 컬렉션/아이템 변경 ───

        private void LineItems_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (QuotationLineItem item in e.NewItems)
                    item.PropertyChanged += LineItem_PropertyChanged;
            if (e.OldItems != null)
                foreach (QuotationLineItem item in e.OldItems)
                    item.PropertyChanged -= LineItem_PropertyChanged;

            UpdateItemNumbers();
            UpdateTotals();
        }

        private void LineItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(QuotationLineItem.Amount)
                or nameof(QuotationLineItem.Qty)
                or nameof(QuotationLineItem.ListPrice))
                UpdateTotals();
        }

        private void UpdateTotals()
        {
            if (CurrentQuotation == null) { TotalQty = 0; TotalAmount = 0; return; }
            TotalQty = CurrentQuotation.LineItems.Sum(x => x.Qty);
            TotalAmount = CurrentQuotation.LineItems.Sum(x => x.Amount);
        }

        private void UpdateItemNumbers()
        {
            if (CurrentQuotation == null) return;
            for (int i = 0; i < CurrentQuotation.LineItems.Count; i++)
                CurrentQuotation.LineItems[i].No = i + 1;
        }

        // ─── 목록 관련 ───

        private void QuotationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (QuotationListBox.SelectedItem is QuotationModel q)
                CurrentQuotation = q;
        }

        private void BtnNewQuotation_Click(object sender, RoutedEventArgs e)
        {
            var q = new QuotationModel();
            Quotations.Insert(0, q);
            QuotationListBox.SelectedItem = q;
        }

        private void BtnDeleteQuotation_Click(object sender, RoutedEventArgs e)
        {
            if (QuotationListBox.SelectedItem is not QuotationModel q) return;
            if (MessageBox.Show($"'{q.DisplayTitle}' 견적서를 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            Quotations.Remove(q);
            QuotationStore.SaveQuotations(Quotations);
            CurrentQuotation = Quotations.Count > 0 ? Quotations[0] : null;
            if (CurrentQuotation != null) QuotationListBox.SelectedItem = CurrentQuotation;
        }

        // ─── 저장 ───

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                QuotationStore.SaveQuotations(Quotations);
                MessageBox.Show("저장되었습니다.");
            }
            catch (Exception ex) { MessageBox.Show("저장 오류: " + ex.Message); }
        }

        // ─── 품목 행 조작 ───

        private void BtnAddLineItem_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentQuotation == null) return;
            CurrentQuotation.LineItems.Add(new QuotationLineItem { No = CurrentQuotation.LineItems.Count + 1 });
        }

        private void BtnDeleteLineItem_Click(object sender, RoutedEventArgs e)
        {
            if (LineItemsGrid.SelectedItem is QuotationLineItem item)
                CurrentQuotation?.LineItems.Remove(item);
        }

        // ─── 단가 관리 모달 ───

        private void BtnProductMaster_Click(object sender, RoutedEventArgs e)
        {
            ProductMasterOverlay.Visibility = Visibility.Visible;
        }

        private void BtnCloseProductMaster_Click(object sender, RoutedEventArgs e)
        {
            try { QuotationStore.SaveProductMaster(_productMaster); }
            catch (Exception ex) { MessageBox.Show("단가 저장 오류: " + ex.Message); }
            ProductMasterOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnAddMasterItem_Click(object sender, RoutedEventArgs e)
        {
            _productMaster.Add(new ProductMasterItem());
        }

        private void BtnDeleteMasterItem_Click(object sender, RoutedEventArgs e)
        {
            if (ProductMasterGrid.SelectedItem is ProductMasterItem item)
                _productMaster.Remove(item);
        }

        // 모달에서 "견적에 추가" 버튼
        private void BtnInsertFromMasterModal_Click(object sender, RoutedEventArgs e)
        {
            if (ProductMasterGrid.SelectedItem is ProductMasterItem master)
                InsertMasterItem(master);
        }

        // 품목 카드의 인라인 "단가에서 추가" 버튼 → 모달 열기
        private void BtnInsertFromMasterInline_Click(object sender, RoutedEventArgs e)
        {
            ProductMasterOverlay.Visibility = Visibility.Visible;
        }

        private void InsertMasterItem(ProductMasterItem master)
        {
            if (CurrentQuotation == null) return;
            CurrentQuotation.LineItems.Add(new QuotationLineItem
            {
                No = CurrentQuotation.LineItems.Count + 1,
                Description = master.ProductName,
                ListPrice = master.UnitPrice,
                StandardSpec = master.Spec,
                Qty = 1
            });
        }

        // ─── INotifyPropertyChanged ───

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
