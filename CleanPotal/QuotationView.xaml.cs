using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Win32;

namespace CleanPotal
{
    public partial class QuotationView : UserControl, INotifyPropertyChanged
    {
        public ObservableCollection<QuotationModel> Quotations { get; } = new();

        // ─── 업체 검색 / 목록 ───

        private string _vendorSearch = "";
        public string VendorSearch
        {
            get => _vendorSearch;
            set { _vendorSearch = value; OnPropertyChanged(nameof(VendorSearch)); FilterVendors(); }
        }

        private List<VendorModel> _allVendors = new();
        public ObservableCollection<VendorModel> FilteredVendors { get; } = new();

        private VendorModel? _selectedVendor;
        public VendorModel? SelectedVendor
        {
            get => _selectedVendor;
            set
            {
                _selectedVendor = value;
                OnPropertyChanged(nameof(SelectedVendor));
                OnPropertyChanged(nameof(HasSelectedVendor));
                OnPropertyChanged(nameof(IsNoneView));
                OnPropertyChanged(nameof(IsListView));
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(ToolbarTitle));
                CurrentQuotation = null;
                RefreshVendorQuotations();
            }
        }

        public bool HasSelectedVendor => _selectedVendor != null;

        public ObservableCollection<QuotationModel> VendorQuotations { get; } = new();

        // ─── 뷰 상태 ───

        public bool IsNoneView => !HasSelectedVendor;
        public bool IsListView => HasSelectedVendor && CurrentQuotation == null;
        public bool IsEditing  => HasSelectedVendor && CurrentQuotation != null;

        public string ToolbarTitle
        {
            get
            {
                if (IsEditing) return "FIRM QUOTATION";
                if (IsListView && SelectedVendor != null)
                    return $"{SelectedVendor.VendorName}  ·  견적 {VendorQuotations.Count}건";
                return "업체 견적서";
            }
        }

        // ─── 현재 견적서 ───

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
                OnPropertyChanged(nameof(IsNoneView));
                OnPropertyChanged(nameof(IsListView));
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(ToolbarTitle));
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
        public ObservableCollection<ManagerModel> CurrentVendorManagers { get; } = new();
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

            RefreshVendorSuggestions();

            // 업체 목록 초기 로드
            _allVendors = VendorStore.Load().OrderBy(v => v.VendorName).ToList();
            FilterVendors();
        }

        // ─── 업체 필터링 ───

        private void FilterVendors()
        {
            FilteredVendors.Clear();
            foreach (var v in _allVendors.Where(v =>
                string.IsNullOrWhiteSpace(VendorSearch) ||
                (v.VendorName?.Contains(VendorSearch, StringComparison.OrdinalIgnoreCase) == true)))
                FilteredVendors.Add(v);
        }

        private void RefreshVendorQuotations()
        {
            VendorQuotations.Clear();
            if (_selectedVendor == null) return;
            foreach (var q in Quotations.Where(q =>
                string.Equals(q.Company, _selectedVendor.VendorName, StringComparison.OrdinalIgnoreCase)))
                VendorQuotations.Add(q);
            OnPropertyChanged(nameof(ToolbarTitle));
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
            CurrentVendorManagers.Clear();

            var matched = _cachedVendors.FirstOrDefault(v =>
                string.Equals(v.VendorName, vendorName, StringComparison.OrdinalIgnoreCase));
            var managers = matched != null
                ? matched.Managers.Where(m => !string.IsNullOrWhiteSpace(m.ManagerName)).ToList()
                : _cachedVendors.SelectMany(v => v.Managers)
                    .Where(m => !string.IsNullOrWhiteSpace(m.ManagerName))
                    .GroupBy(m => m.ManagerName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First()).ToList();

            foreach (var mgr in managers)
            {
                AttentionSuggestions.Add(mgr.ManagerName);
                CurrentVendorManagers.Add(mgr);
            }
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
            TotalQty    = CurrentQuotation.LineItems.Sum(x => x.Qty);
            TotalAmount = CurrentQuotation.LineItems.Sum(x => x.Amount);
        }

        private void UpdateItemNumbers()
        {
            if (CurrentQuotation == null) return;
            for (int i = 0; i < CurrentQuotation.LineItems.Count; i++)
                CurrentQuotation.LineItems[i].No = i + 1;
        }

        // ─── 업체 선택 ───

        private void VendorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VendorListBox.SelectedItem is VendorModel v)
                SelectedVendor = v;
        }

        // ─── 툴바 / 뷰 전환 ───

        private void BtnBackToList_Click(object sender, RoutedEventArgs e)
        {
            CurrentQuotation = null;
        }

        // ─── 새 견적서 ───

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
                Company     = _selectedVendor?.VendorName ?? "",
                AetsManager = aetsManager,
                AetsPhone   = SessionManager.CurrentPhoneNumber,
                BusinessNo  = _config.BusinessNo,
                Date        = DateTime.Today.ToString("yyyy-MM-dd")
            };
            Quotations.Insert(0, q);
            RefreshVendorQuotations();
            CurrentQuotation = q;
        }

        // ─── 견적 목록에서 편집/삭제 ───

        private void BtnEditQuotation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is QuotationModel q)
                CurrentQuotation = q;
        }

        private void BtnDeleteQuotationInList_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is QuotationModel q)
            {
                if (MessageBox.Show($"'{q.DisplayTitle}' 견적서를 삭제하시겠습니까?", "확인",
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

                Quotations.Remove(q);
                VendorQuotations.Remove(q);
                QuotationStore.SaveQuotations(Quotations);
                OnPropertyChanged(nameof(ToolbarTitle));
            }
        }

        private void QuotationListGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (VendorQuotationsGrid.SelectedItem is QuotationModel q)
                CurrentQuotation = q;
        }

        // ─── 내보내기 ───

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentQuotation == null) { MessageBox.Show("내보낼 견적서를 선택하세요."); return; }

            var dlg = new SaveFileDialog
            {
                Title    = "엑셀 파일로 저장",
                Filter   = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = QuotationExporter.MakeSafeFileName(
                    $"FIRM_QUOTATION_{CurrentQuotation.Company}_{CurrentQuotation.Date}.xlsx")
            };
            if (dlg.ShowDialog() != true) return;

            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                QuotationExporter.ExportToExcel(CurrentQuotation, dlg.FileName);
                System.Windows.Input.Mouse.OverrideCursor = null;
                var result = MessageBox.Show(
                    "엑셀 파일이 저장되었습니다.\n바로 열어보시겠습니까?",
                    "완료", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
                MessageBox.Show(
                    "엑셀 내보내기 오류: " + ex.Message + "\n\n" +
                    "Microsoft Excel이 설치되어 있어야 합니다.");
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

            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                QuotationExporter.ExportToPdf(CurrentQuotation, dlg.FileName);
                System.Windows.Input.Mouse.OverrideCursor = null;
                var result = MessageBox.Show(
                    "PDF 파일이 저장되었습니다.\n바로 열어보시겠습니까?",
                    "완료", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
                MessageBox.Show(
                    "PDF 내보내기 오류: " + ex.Message + "\n\n" +
                    "Microsoft Excel이 설치되어 있어야 합니다.");
            }
        }

        // ─── 저장 ───

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                QuotationStore.SaveQuotations(Quotations);
                RefreshVendorQuotations();
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

        // ─── 담당자 드롭다운 선택 → 연락처 자동 연동 ───

        private void AttentionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CurrentQuotation == null) return;
            if (((ComboBox)sender).SelectedItem is ManagerModel mgr && !string.IsNullOrWhiteSpace(mgr.ContactNumber))
                CurrentQuotation.Phone = mgr.ContactNumber;
        }

        // ─── 엑셀 가져오기 ───

        private void BtnImportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentQuotation == null) { MessageBox.Show("먼저 견적서를 선택하거나 새로 생성하세요."); return; }

            var dlg = new OpenFileDialog
            {
                Title  = "세정 의뢰 양식 가져오기",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ImportFromExcel(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("가져오기 오류: " + ex.Message);
            }
        }

        private void ImportFromExcel(string filePath)
        {
            using var doc = SpreadsheetDocument.Open(filePath, isEditable: false);
            var wbPart = doc.WorkbookPart!;
            var sheet  = wbPart.Workbook.Sheets!.Elements<Sheet>().First();
            var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);
            var sd     = wsPart.Worksheet.GetFirstChild<SheetData>()!;

            // 공유 문자열 테이블 구성
            var sharedStrings = new List<string>();
            if (wbPart.SharedStringTablePart != null)
                foreach (SharedStringItem item in wbPart.SharedStringTablePart.SharedStringTable.Elements<SharedStringItem>())
                    sharedStrings.Add(item.InnerText);

            string GetVal(Cell? cell)
            {
                if (cell == null) return "";
                if (cell.DataType?.Value == CellValues.SharedString &&
                    int.TryParse(cell.InnerText, out int idx) && idx < sharedStrings.Count)
                    return sharedStrings[idx];
                if (cell.GetFirstChild<InlineString>() is InlineString istr)
                    return istr.InnerText;
                return cell.CellValue?.Text ?? "";
            }

            Cell? FindCell(Row row, string col)
            {
                string key = $"{col}{row.RowIndex?.Value}";
                return row.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == key);
            }

            var newItems  = new List<QuotationLineItem>();
            string contactRaw = "";

            foreach (Row row in sd.Elements<Row>())
            {
                string bVal = GetVal(FindCell(row, "B")).Trim();

                // 품목 행: B열이 양의 정수
                if (int.TryParse(bVal, out int no) && no > 0)
                {
                    string partName = GetVal(FindCell(row, "C")).Trim();
                    string partCode = GetVal(FindCell(row, "D")).Trim();
                    string size     = GetVal(FindCell(row, "E")).Trim();
                    string qtyStr   = GetVal(FindCell(row, "F")).Trim();
                    int.TryParse(qtyStr, out int qty);

                    if (!string.IsNullOrEmpty(partName))
                        newItems.Add(new QuotationLineItem
                        {
                            No           = no,
                            Description  = partName,
                            PartCode     = partCode,
                            StandardSpec = size,
                            Qty          = qty > 0 ? qty : 1,
                            ListPrice    = 0
                        });
                }

                // 반출 정보 셀 탐색 (담당자/연락처 포함 여부)
                if (string.IsNullOrEmpty(contactRaw))
                {
                    foreach (Cell cell in row.Elements<Cell>())
                    {
                        string val = GetVal(cell);
                        if (val.Contains("담당자") && val.Contains("연락처"))
                        {
                            contactRaw = val;
                            break;
                        }
                    }
                }
            }

            // 품목 적용
            if (newItems.Count > 0)
            {
                bool replace = CurrentQuotation.LineItems.Count == 0 ||
                    MessageBox.Show(
                        $"기존 품목 {CurrentQuotation.LineItems.Count}개를 삭제하고 가져온 {newItems.Count}개로 교체하시겠습니까?",
                        "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
                if (!replace) return;

                CurrentQuotation.LineItems.Clear();
                foreach (var item in newItems)
                    CurrentQuotation.LineItems.Add(item);
            }

            // 반출 정보: 담당자(연락처) 파싱
            string managerName = "";
            string managerPhone = "";

            if (!string.IsNullOrEmpty(contactRaw))
            {
                var lineMatch = Regex.Match(contactRaw,
                    @"담당자\s*\(연락처\)\s*:\s*([^\n\r]*)",
                    RegexOptions.IgnoreCase);
                if (lineMatch.Success)
                {
                    (managerName, managerPhone) = ParseManagerContact(lineMatch.Groups[1].Value.Trim());
                }
            }

            // 고객사 정보 자동 반영
            if (!string.IsNullOrEmpty(managerName))
                CurrentQuotation.Attention = managerName;
            if (!string.IsNullOrEmpty(managerPhone))
                CurrentQuotation.Phone = managerPhone;

            // 거래처 담당자 자동 등록
            if (!string.IsNullOrEmpty(managerName) && _selectedVendor != null)
            {
                bool exists = _selectedVendor.Managers.Any(m =>
                    string.Equals(m.ManagerName?.Trim(), managerName, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    var allVendors = VendorStore.Load();
                    var target = allVendors.FirstOrDefault(v =>
                        string.Equals(v.VendorName, _selectedVendor.VendorName, StringComparison.OrdinalIgnoreCase));
                    if (target != null)
                    {
                        target.Managers.Add(new ManagerModel
                        {
                            ManagerName   = managerName,
                            ContactNumber = managerPhone
                        });
                        VendorStore.Save(allVendors);
                        // 로컬 캐시도 갱신
                        _selectedVendor.Managers.Add(new ManagerModel
                        {
                            ManagerName   = managerName,
                            ContactNumber = managerPhone
                        });
                        _allVendors = VendorStore.Load().ToList();
                        RefreshVendorSuggestions();
                        RefreshAttentionSuggestions(_selectedVendor.VendorName);
                    }
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("가져오기 완료");
            if (newItems.Count > 0) sb.AppendLine($"• 품목 {newItems.Count}개 반영");
            if (!string.IsNullOrEmpty(managerName))
                sb.AppendLine($"• 담당자: {managerName}" + (string.IsNullOrEmpty(managerPhone) ? "" : $" ({managerPhone})"));
            MessageBox.Show(sb.ToString().Trim(), "완료");
        }

        // ─── 담당자/연락처 파싱 ───

        /// <summary>
        /// "박주언 과장 /010-4083-8713" 같은 원본 문자열에서 이름과 전화번호를 분리한다.
        /// 지원 형식: (010 4083 8713)  (010-4083-8713)  /01040838713  /010-4083-8713  등
        /// </summary>
        private static (string name, string phone) ParseManagerContact(string raw)
        {
            raw = raw.Trim();
            if (string.IsNullOrEmpty(raw)) return ("", "");

            // 전화번호 세그먼트: 선택적 구분자(/, (, 공백) + 0으로 시작하는 10-11자리 숫자군
            // 숫자 사이에는 공백, 하이픈 허용
            var phoneRx = new Regex(
                @"(?<sep>[\/（(\s]+)?(?<num>0\d{1,2}[\s\-]?\d{3,4}[\s\-]?\d{4})(?<suf>[）)\s]*)");

            var m = phoneRx.Match(raw);
            if (!m.Success) return (raw, "");

            // 숫자만 추출 후 010-XXXX-XXXX 형식으로 정규화
            string digits = Regex.Replace(m.Groups["num"].Value, @"[\s\-]", "");
            string phone = digits.Length switch
            {
                11 => $"{digits[..3]}-{digits[3..7]}-{digits[7..]}",
                10 => $"{digits[..3]}-{digits[3..6]}-{digits[6..]}",
                _  => m.Groups["num"].Value
            };

            // 이름 = 전화번호 세그먼트 앞부분 (구분자 제거)
            string name = raw[..m.Index].TrimEnd('/', '(', '（', ' ', '\t');
            return (name, phone);
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
