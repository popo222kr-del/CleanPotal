using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

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
                    _currentQuotation.PropertyChanged -= CurrentQuotation_PropertyChanged;
                    _currentQuotation.LineItems.CollectionChanged -= LineItems_CollectionChanged;
                    foreach (var item in _currentQuotation.LineItems)
                        item.PropertyChanged -= LineItem_PropertyChanged;
                }
                _currentQuotation = value;
                if (_currentQuotation != null)
                {
                    _currentQuotation.PropertyChanged += CurrentQuotation_PropertyChanged;
                    _currentQuotation.LineItems.CollectionChanged += LineItems_CollectionChanged;
                    foreach (var item in _currentQuotation.LineItems)
                        item.PropertyChanged += LineItem_PropertyChanged;
                    LineItemsGrid.ItemsSource = _currentQuotation.LineItems;
                    RefreshAttentionSuggestions(_currentQuotation.Company);
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

        public ObservableCollection<string> VendorSuggestions   { get; } = new();
        public ObservableCollection<string> AttentionSuggestions { get; } = new();
        private List<VendorModel> _cachedVendors = new();

        private ObservableCollection<ProductMasterItem> _productMaster = new();
        private QuotationConfig _config = new();

        public QuotationView()
        {
            InitializeComponent();
            DataContext = this;

            _config = QuotationStore.LoadConfig();

            var saved = QuotationStore.LoadQuotations();
            foreach (var q in saved) Quotations.Add(q);

            _productMaster = QuotationStore.LoadProductMaster();
            ProductMasterGrid.ItemsSource = _productMaster;

            QuotationListBox.ItemsSource = Quotations;
            if (Quotations.Count > 0) QuotationListBox.SelectedIndex = 0;

            RefreshVendorSuggestions();
        }

        private void RefreshVendorSuggestions()
        {
            _cachedVendors = VendorStore.Load().ToList();
            VendorSuggestions.Clear();
            foreach (var v in _cachedVendors.Select(v => v.VendorName).Where(n => !string.IsNullOrWhiteSpace(n)))
                VendorSuggestions.Add(v);
        }

        private void RefreshAttentionSuggestions(string vendorName)
        {
            AttentionSuggestions.Clear();
            var matched = _cachedVendors.FirstOrDefault(v =>
                string.Equals(v.VendorName, vendorName, StringComparison.OrdinalIgnoreCase));
            var managers = matched != null
                ? matched.Managers.Select(m => m.ManagerName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                : _cachedVendors.SelectMany(v => v.Managers).Select(m => m.ManagerName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            foreach (var name in managers)
                AttentionSuggestions.Add(name);
        }

        public void TryRefresh() { }

        // ─── CurrentQuotation 필드 변경 감지 ───

        private void CurrentQuotation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(QuotationModel.Company))
                RefreshAttentionSuggestions(CurrentQuotation?.Company ?? "");
            else if (e.PropertyName == nameof(QuotationModel.Date))
                AutoFillValidity();
        }

        private void AutoFillValidity()
        {
            if (CurrentQuotation == null) return;
            if (DateTime.TryParse(CurrentQuotation.Date, out var date))
                CurrentQuotation.Validity = date.AddDays(7).ToString("yyyy-MM-dd");
        }

        // ─── 컬렉션/아이템 변경 이벤트 ───

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
            TotalQty  = CurrentQuotation.LineItems.Sum(x => x.Qty);
            TotalAmount = CurrentQuotation.LineItems.Sum(x => x.Amount);
        }

        private void UpdateItemNumbers()
        {
            if (CurrentQuotation == null) return;
            for (int i = 0; i < CurrentQuotation.LineItems.Count; i++)
                CurrentQuotation.LineItems[i].No = i + 1;
        }

        // ─── 목록 조작 ───

        private void QuotationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (QuotationListBox.SelectedItem is QuotationModel q)
                CurrentQuotation = q;
        }

        private void BtnNewQuotation_Click(object sender, RoutedEventArgs e)
        {
            // AETS 담당자: 이름 + 직위 조합
            string managerName = SessionManager.CurrentRealName;
            string jobTitle    = SessionManager.CurrentJobTitle;
            string aetsManager = string.IsNullOrWhiteSpace(jobTitle)
                ? managerName
                : $"{managerName} {jobTitle}";

            var q = new QuotationModel
            {
                AetsManager = aetsManager,
                AetsPhone   = SessionManager.CurrentPhoneNumber,
                BusinessNo  = _config.BusinessNo,
                Date        = DateTime.Today.ToString("yyyy-MM-dd")
            };
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

        // ─── 내보내기 ───

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentQuotation == null) { MessageBox.Show("내보낼 견적서를 선택하세요."); return; }

            var dlg = new SaveFileDialog
            {
                Title      = "엑셀 파일로 저장",
                Filter     = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName   = QuotationExporter.MakeSafeFileName(
                    $"FIRM_QUOTATION_{CurrentQuotation.Company}_{CurrentQuotation.Date}.xlsx")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                QuotationExporter.ExportToExcel(CurrentQuotation, dlg.FileName);
                var result = MessageBox.Show(
                    "엑셀 파일이 저장되었습니다.\n바로 열어보시겠습니까?",
                    "완료", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("엑셀 내보내기 오류: " + ex.Message);
            }
        }

        private void BtnExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentQuotation == null) { MessageBox.Show("내보낼 견적서를 선택하세요."); return; }

            var dlg = new SaveFileDialog
            {
                Title    = "PDF 파일로 저장",
                Filter   = "PDF 파일 (*.pdf)|*.pdf",
                FileName = QuotationExporter.MakeSafeFileName(
                    $"FIRM_QUOTATION_{CurrentQuotation.Company}_{CurrentQuotation.Date}.pdf")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                QuotationExporter.ExportToPdf(CurrentQuotation, dlg.FileName);
                var result = MessageBox.Show(
                    "PDF 파일이 저장되었습니다.\n바로 열어보시겠습니까?",
                    "완료", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "PDF 내보내기 오류: " + ex.Message + "\n\n" +
                    "PDF 내보내기는 Microsoft Excel이 설치되어 있어야 합니다.\n" +
                    "Excel이 없다면 엑셀 파일로 내보낸 후 PDF로 변환해 주세요.");
            }
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

        // 사업자등록번호를 기본값으로 저장 (새 견적서 생성 시 자동 입력)
        private void BtnSaveBusinessNo_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentQuotation == null) return;
            _config.BusinessNo = CurrentQuotation.BusinessNo;
            try
            {
                QuotationStore.SaveConfig(_config);
                MessageBox.Show("사업자등록번호가 기본값으로 저장되었습니다.\n이후 새 견적서 생성 시 자동 입력됩니다.");
            }
            catch (Exception ex) { MessageBox.Show("설정 저장 오류: " + ex.Message); }
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

        private void BtnInsertFromMasterModal_Click(object sender, RoutedEventArgs e)
        {
            if (ProductMasterGrid.SelectedItem is ProductMasterItem master)
                InsertMasterItem(master);
        }

        private void BtnInsertFromMasterInline_Click(object sender, RoutedEventArgs e)
        {
            ProductMasterOverlay.Visibility = Visibility.Visible;
        }

        private void InsertMasterItem(ProductMasterItem master)
        {
            if (CurrentQuotation == null) return;
            CurrentQuotation.LineItems.Add(new QuotationLineItem
            {
                No           = CurrentQuotation.LineItems.Count + 1,
                Description  = master.ProductName,
                ListPrice    = master.UnitPrice,
                StandardSpec = master.Spec,
                Qty          = 1
            });
        }

        // ─── INotifyPropertyChanged ───

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
