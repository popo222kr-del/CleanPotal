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
using WpfBorder = System.Windows.Controls.Border;

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

        public bool IsNoneView    => !HasSelectedVendor;
        public bool IsListView    => HasSelectedVendor && CurrentQuotation == null;
        public bool IsEditing     => HasSelectedVendor && CurrentQuotation != null;
        public bool IsNotEditing  => !IsEditing;

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
                OnPropertyChanged(nameof(IsNotEditing));
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
            MigrateSpecToPartCode(_productMaster);
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
                int newPrices = AutoRegisterNewPrices();
                QuotationStore.SaveQuotations(Quotations);
                RefreshVendorQuotations();
                string msg = newPrices > 0
                    ? $"저장되었습니다.\n(신규 단가 {newPrices}개 단가 관리에 자동 등록)"
                    : "저장되었습니다.";
                MessageBox.Show(msg);
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

        // ─── 드래그앤드롭 ───

        private void LineItemsCard_DragEnter(object sender, DragEventArgs e)
        {
            if (CurrentQuotation != null && IsXlsxDrop(e))
                ((WpfBorder)sender).BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(99, 102, 241));
        }

        private void LineItemsCard_DragLeave(object sender, DragEventArgs e)
        {
            ((WpfBorder)sender).ClearValue(WpfBorder.BorderBrushProperty);
        }

        private void LineItemsCard_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = (CurrentQuotation != null && IsXlsxDrop(e))
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void LineItemsCard_Drop(object sender, DragEventArgs e)
        {
            ((WpfBorder)sender).ClearValue(WpfBorder.BorderBrushProperty);
            if (CurrentQuotation == null || !IsXlsxDrop(e)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var xlsx  = files.FirstOrDefault(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
            if (xlsx == null) return;
            try { ImportFromExcel(xlsx); }
            catch (Exception ex) { MessageBox.Show("가져오기 오류: " + ex.Message); }
        }

        private static bool IsXlsxDrop(DragEventArgs e) =>
            e.Data.GetDataPresent(DataFormats.FileDrop) &&
            ((string[])e.Data.GetData(DataFormats.FileDrop))
                .Any(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));

        // ─── 폴더 일괄 가져오기 ───

        private void BtnImportFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "견적서 폴더 선택 (상위 폴더를 선택하세요)"
            };
            if (dlg.ShowDialog() != true) return;

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var (quotations, newPrices) = ImportFolderBatch(dlg.FolderName);
                Mouse.OverrideCursor = null;
                MessageBox.Show(
                    $"일괄 가져오기 완료\n• 견적서: {quotations}건\n• 신규 단가 등록: {newPrices}개",
                    "완료");
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show("가져오기 오류: " + ex.Message);
            }
        }

        private (int quotations, int newPrices) ImportFolderBatch(string rootPath)
        {
            int totalQuotations = 0;
            int totalNewPrices  = 0;
            bool masterChanged  = false;
            var errors          = new List<string>();

            var files = CollectXlsxFiles(rootPath);

            foreach (var (companyName, xlsxPath) in files)
            {
                try
                {
                    var (q, newPrices) = ParseXlsxAsQuotation(xlsxPath, companyName);
                    if (q == null) continue;

                    Quotations.Add(q);
                    totalQuotations++;
                    totalNewPrices += newPrices;
                    if (newPrices > 0) masterChanged = true;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(xlsxPath)}: {ex.Message}");
                }
            }

            if (masterChanged) QuotationStore.SaveProductMaster(_productMaster);
            if (totalQuotations > 0)
            {
                QuotationStore.SaveQuotations(Quotations);
                RefreshVendorQuotations();
            }

            if (errors.Count > 0)
            {
                string errMsg = $"다음 {errors.Count}개 파일을 처리하지 못했습니다:\n\n"
                              + string.Join("\n", errors.Take(10));
                if (errors.Count > 10) errMsg += $"\n... 외 {errors.Count - 10}개";
                MessageBox.Show(errMsg, "가져오기 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return (totalQuotations, totalNewPrices);
        }

        /// <summary>
        /// 폴더를 재귀 탐색해 (업체명, xlsx경로) 목록을 수집한다.
        /// 연도 폴더(25년, 26년, 2025 등)는 회사명으로 쓰지 않고 부모 폴더명을 업체명으로 사용한다.
        /// </summary>
        private static List<(string company, string filePath)> CollectXlsxFiles(string rootPath)
        {
            var result   = new List<(string, string)>();
            string rootName = Path.GetFileName(rootPath);

            // 루트 직속 xlsx
            foreach (var f in Directory.GetFiles(rootPath, "*.xlsx"))
                result.Add((rootName, f));

            foreach (var dir in Directory.GetDirectories(rootPath))
            {
                string dirName    = Path.GetFileName(dir);
                bool   isYearDir  = Regex.IsMatch(dirName, @"^\d{2,4}년?$");

                if (isYearDir)
                {
                    // 연도 폴더 → 부모 폴더명을 업체명으로
                    foreach (var f in Directory.GetFiles(dir, "*.xlsx"))
                        result.Add((rootName, f));
                }
                else
                {
                    // 업체 폴더 → 직속 xlsx 수집
                    foreach (var f in Directory.GetFiles(dir, "*.xlsx"))
                        result.Add((dirName, f));

                    // 업체 폴더 하위의 연도 폴더도 수집
                    foreach (var yearDir in Directory.GetDirectories(dir))
                    {
                        if (Regex.IsMatch(Path.GetFileName(yearDir), @"^\d{2,4}년?$"))
                            foreach (var f in Directory.GetFiles(yearDir, "*.xlsx"))
                                result.Add((dirName, f));
                    }
                }
            }

            return result;
        }

        /// <summary>xlsx 파일 하나를 QuotationModel로 변환. 견적서 출력 형식과 세정 의뢰 양식 모두 지원.</summary>
        private (QuotationModel? q, int newPrices) ParseXlsxAsQuotation(string filePath, string companyName)
        {
            filePath = EnsurePlainXlsx(filePath);
            using var doc = SpreadsheetDocument.Open(filePath, isEditable: false);
            var wbPart = doc.WorkbookPart!;
            var sheet  = wbPart.Workbook.Sheets!.Elements<Sheet>().First();
            var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);
            var sd     = wsPart.Worksheet.GetFirstChild<SheetData>()!;
            var ss     = BuildSharedStrings(wbPart);
            var cellMap = BuildCellMap(sd, ss);

            string Get(string r) => cellMap.TryGetValue(r, out var v) ? v : "";

            // 형식 감지: B6 에 "세정 의뢰" 포함 → 세정 의뢰 양식
            bool isRequestForm = Get("B6").Contains("세정 의뢰", StringComparison.OrdinalIgnoreCase);

            var items     = new List<QuotationLineItem>();
            string company   = companyName;
            string attention = "";
            string phone     = "";
            string date      = File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd");

            if (isRequestForm)
            {
                // ── 세정 의뢰 양식 파싱 (B=No, C=Name, D=Code, E=Size, F=Qty) ──
                foreach (Row row in sd.Elements<Row>())
                {
                    uint ri    = row.RowIndex?.Value ?? 0;
                    string bVal = Get($"B{ri}").Trim();

                    if (int.TryParse(bVal, out int no) && no > 0)
                    {
                        string name = Get($"C{ri}").Trim();
                        string dVal = Get($"D{ri}").Trim();
                        string eVal = Get($"E{ri}").Trim();
                        int.TryParse(Get($"F{ri}").Trim(), out int qty);
                        var (partCode, spec) = ParseCodeAndSize(dVal, eVal);
                        if (!string.IsNullOrEmpty(name))
                            items.Add(new QuotationLineItem
                            {
                                No = no, Description = name, PartCode = partCode,
                                StandardSpec = spec, Qty = qty > 0 ? qty : 1
                            });
                    }
                    // 담당자 정보 셀 탐색
                    if (string.IsNullOrEmpty(attention))
                    {
                        foreach (Cell cell in row.Elements<Cell>())
                        {
                            string val = XlsxCellText(cell, ss);
                            if (val.Contains("담당자") && val.Contains("연락처"))
                            {
                                var lm = Regex.Match(val, @"담당자\s*\(연락처\)\s*:\s*([^\n\r]*)", RegexOptions.IgnoreCase);
                                if (lm.Success) (attention, phone) = ParseManagerContact(lm.Groups[1].Value.Trim());
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                // ── 견적서 출력 형식 파싱 ──
                // 헤더 행("Product Description" 또는 "품명" 포함)을 찾아 컬럼 위치를 동적으로 결정
                string fileCompany = Get("E14").Trim();
                if (!string.IsNullOrEmpty(fileCompany)) company = fileCompany;
                attention = Get("E13").Trim();

                string dateStr = Get("K14").Trim();
                if (dateStr.StartsWith(": ")) dateStr = dateStr[2..].Trim();
                if (!string.IsNullOrEmpty(dateStr)) date = dateStr;

                // 기본값: 우리 앱 템플릿 레이아웃
                string noCol = "A", descCol = "B", priceCol = "I", specCol = "J", qtyCol = "K";
                uint dataStart = 22, dataEnd = 44;

                // 헤더 행 탐색
                foreach (Row hrow in sd.Elements<Row>())
                {
                    uint hri = hrow.RowIndex?.Value ?? 0;
                    bool foundDesc = false;
                    foreach (Cell hcell in hrow.Elements<Cell>())
                    {
                        string hval = XlsxCellText(hcell, ss).Trim();
                        bool isDescHeader =
                            hval.Equals("Product Description", StringComparison.OrdinalIgnoreCase) ||
                            hval.Equals("품명", StringComparison.OrdinalIgnoreCase) ||
                            hval.Equals("품목명", StringComparison.OrdinalIgnoreCase) ||
                            hval.Equals("제품명", StringComparison.OrdinalIgnoreCase);
                        if (!isDescHeader) continue;

                        foundDesc = true;
                        string descRef = hcell.CellReference?.Value ?? "B1";
                        descCol = Regex.Match(descRef, @"[A-Z]+").Value;
                        noCol   = ColPrev(descCol);
                        dataStart = hri + 1;
                        dataEnd   = hri + 60;

                        // 같은 행에서 단가/규격/수량 컬럼 탐색
                        foreach (Cell fc in hrow.Elements<Cell>())
                        {
                            string fref = fc.CellReference?.Value ?? "";
                            string fcol = Regex.Match(fref, @"[A-Z]+").Value;
                            string fval = XlsxCellText(fc, ss).Trim();
                            if (fval.IndexOf("price", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                fval.IndexOf("단가", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                fval.IndexOf("list", StringComparison.OrdinalIgnoreCase) >= 0)
                                priceCol = fcol;
                            else if (fval.Equals("Q'ty", StringComparison.OrdinalIgnoreCase) ||
                                     fval.Equals("Qty", StringComparison.OrdinalIgnoreCase) ||
                                     fval.Equals("수량", StringComparison.OrdinalIgnoreCase) ||
                                     fval.Equals("Q'TY", StringComparison.OrdinalIgnoreCase))
                                qtyCol = fcol;
                            else if (fval.Equals("규격", StringComparison.OrdinalIgnoreCase) ||
                                     fval.Equals("SIZE", StringComparison.OrdinalIgnoreCase) ||
                                     fval.Equals("Spec", StringComparison.OrdinalIgnoreCase))
                                specCol = fcol;
                        }
                        break;
                    }
                    if (foundDesc) break;
                }

                int seqNo = 0;
                for (uint r = dataStart; r <= dataEnd; r++)
                {
                    string descVal = Get($"{descCol}{r}").Trim();
                    if (string.IsNullOrEmpty(descVal)) continue;

                    string noStr = Get($"{noCol}{r}").Trim();
                    if (!int.TryParse(noStr, out int no)) { seqNo++; no = seqNo; }
                    else seqNo = no;

                    decimal.TryParse(Get($"{priceCol}{r}").Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out decimal price);
                    int.TryParse(Get($"{qtyCol}{r}").Trim(), out int qty);

                    string specRaw = Get($"{specCol}{r}").Trim();
                    var (partCode, spec) = ParseCodeAndSize("", specRaw);

                    items.Add(new QuotationLineItem
                    {
                        No = no, Description = descVal, PartCode = partCode,
                        StandardSpec = spec, ListPrice = price, Qty = qty > 0 ? qty : 1
                    });
                }
            }

            if (items.Count == 0) return (null, 0);

            // 자동 가격 적용 + 신규 단가 등록
            int newPrices = ApplyAndRegisterPrices(items, save: false, vendorName: companyName);

            var q = new QuotationModel { Company = company, Attention = attention, Phone = phone, Date = date };
            foreach (var item in items) q.LineItems.Add(item);
            return (q, newPrices);
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
            if (CurrentQuotation == null) return;
            string vendorName = _selectedVendor?.VendorName ?? "";

            // ParseXlsxAsQuotation 재사용 → 세정 의뢰 양식 + AETS 출력 형식 모두 지원
            var (parsed, newPrices) = ParseXlsxAsQuotation(filePath, vendorName);

            if (parsed == null || parsed.LineItems.Count == 0)
            {
                MessageBox.Show("가져올 품목을 찾을 수 없습니다.\n지원 형식: 세정 의뢰 양식 / AETS 견적서 출력");
                return;
            }

            bool replace = CurrentQuotation.LineItems.Count == 0 ||
                MessageBox.Show(
                    $"기존 품목 {CurrentQuotation.LineItems.Count}개를 삭제하고 가져온 {parsed.LineItems.Count}개로 교체하시겠습니까?",
                    "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
            if (!replace) return;

            CurrentQuotation.LineItems.Clear();
            foreach (var item in parsed.LineItems)
                CurrentQuotation.LineItems.Add(item);

            // 단가 저장 (ParseXlsxAsQuotation은 save:false로 호출되므로 여기서 저장)
            if (newPrices > 0) QuotationStore.SaveProductMaster(_productMaster);

            // 담당자 정보 적용
            string managerName  = parsed.Attention;
            string managerPhone = parsed.Phone;
            if (!string.IsNullOrEmpty(managerName))  CurrentQuotation.Attention = managerName;
            if (!string.IsNullOrEmpty(managerPhone)) CurrentQuotation.Phone     = managerPhone;

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
                        target.Managers.Add(new ManagerModel { ManagerName = managerName, ContactNumber = managerPhone });
                        VendorStore.Save(allVendors);
                        _selectedVendor.Managers.Add(new ManagerModel { ManagerName = managerName, ContactNumber = managerPhone });
                        _allVendors = VendorStore.Load().ToList();
                        RefreshVendorSuggestions();
                        RefreshAttentionSuggestions(_selectedVendor.VendorName);
                    }
                }
            }

            var sb = new System.Text.StringBuilder("가져오기 완료\n");
            sb.AppendLine($"• 품목 {parsed.LineItems.Count}개 반영");
            if (newPrices > 0) sb.AppendLine($"• 신규 단가 {newPrices}개 등록");
            if (!string.IsNullOrEmpty(managerName))
                sb.AppendLine($"• 담당자: {managerName}" + (string.IsNullOrEmpty(managerPhone) ? "" : $" ({managerPhone})"));
            MessageBox.Show(sb.ToString().Trim(), "완료");
        }

        // ─── xlsx 공통 헬퍼 ───

        private static List<string> BuildSharedStrings(WorkbookPart wbPart)
        {
            var list = new List<string>();
            if (wbPart.SharedStringTablePart != null)
                foreach (SharedStringItem item in wbPart.SharedStringTablePart.SharedStringTable.Elements<SharedStringItem>())
                    list.Add(item.InnerText);
            return list;
        }

        private static Dictionary<string, string> BuildCellMap(SheetData sd, List<string> ss)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Row row in sd.Elements<Row>())
                foreach (Cell cell in row.Elements<Cell>())
                    if (cell.CellReference?.Value is string r)
                        map[r] = XlsxCellText(cell, ss);
            return map;
        }

        private static string XlsxCellText(Cell cell, List<string> ss)
        {
            if (cell.DataType?.Value == CellValues.SharedString &&
                int.TryParse(cell.InnerText, out int idx) && idx < ss.Count)
                return ss[idx];
            if (cell.GetFirstChild<InlineString>() is InlineString istr)
                return istr.InnerText;
            return cell.CellValue?.Text ?? "";
        }

        // ─── 단가 자동 적용 / 신규 등록 ───

        /// <summary>품목 목록에 ProductMaster 가격 자동 적용 + 신규 품목 자동 등록. 등록 수 반환.</summary>
        private int ApplyAndRegisterPrices(IList<QuotationLineItem> items, bool save, string vendorName = "")
        {
            int newCount = 0;
            foreach (var item in items)
            {
                var master = FindMasterItem(item.Description, item.PartCode, vendorName);
                if (master != null)
                {
                    // 기존 단가 있으면 적용 (ListPrice가 0이거나 없을 때만)
                    if (item.ListPrice == 0)
                        item.ListPrice = master.UnitPrice;
                }
                else if (item.ListPrice > 0)
                {
                    // 신규 단가 등록 (1EA 기준)
                    _productMaster.Add(new ProductMasterItem
                    {
                        ProductName = item.Description,
                        PartCode    = item.PartCode,
                        Spec        = item.StandardSpec,
                        UnitPrice   = item.ListPrice,
                        Unit        = "EA",
                        VendorName  = vendorName
                    });
                    newCount++;
                }
            }
            if (newCount > 0 && save) QuotationStore.SaveProductMaster(_productMaster);
            return newCount;
        }

        /// <summary>현재 견적서 품목 중 ProductMaster에 없는 가격>0 항목을 자동 등록.</summary>
        private int AutoRegisterNewPrices()
        {
            if (CurrentQuotation == null) return 0;
            string vendorName = _selectedVendor?.VendorName ?? "";
            int count = 0;
            foreach (var item in CurrentQuotation.LineItems.Where(i => i.ListPrice > 0 &&
                (!string.IsNullOrEmpty(i.Description) || !string.IsNullOrEmpty(i.PartCode))))
            {
                if (FindMasterItem(item.Description, item.PartCode, vendorName) == null)
                {
                    _productMaster.Add(new ProductMasterItem
                    {
                        ProductName = item.Description,
                        PartCode    = item.PartCode,
                        Spec        = item.StandardSpec,
                        UnitPrice   = item.ListPrice,
                        Unit        = "EA",
                        VendorName  = vendorName
                    });
                    count++;
                }
            }
            if (count > 0) QuotationStore.SaveProductMaster(_productMaster);
            return count;
        }

        private ProductMasterItem? FindMasterItem(string description, string partCode, string vendorName = "")
        {
            // 업체명이 지정된 경우 해당 업체 항목 우선 탐색
            if (!string.IsNullOrEmpty(vendorName))
            {
                if (!string.IsNullOrEmpty(partCode))
                {
                    var byCode = _productMaster.FirstOrDefault(p =>
                        string.Equals(p.VendorName, vendorName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.PartCode, partCode, StringComparison.OrdinalIgnoreCase));
                    if (byCode != null) return byCode;
                }
                if (!string.IsNullOrEmpty(description))
                {
                    var byName = _productMaster.FirstOrDefault(p =>
                        string.Equals(p.VendorName, vendorName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.ProductName, description, StringComparison.OrdinalIgnoreCase));
                    if (byName != null) return byName;
                }
            }
            // 업체 미지정 또는 업체별 매칭 실패 시 전체 탐색
            if (!string.IsNullOrEmpty(partCode))
            {
                var byCode = _productMaster.FirstOrDefault(p =>
                    string.Equals(p.PartCode, partCode, StringComparison.OrdinalIgnoreCase));
                if (byCode != null) return byCode;
            }
            if (!string.IsNullOrEmpty(description))
                return _productMaster.FirstOrDefault(p =>
                    string.Equals(p.ProductName, description, StringComparison.OrdinalIgnoreCase));
            return null;
        }

        // 엑셀 컬럼 문자에서 이전 컬럼 반환 (B→A, C→B, AA→Z 등)
        private static string ColPrev(string col)
        {
            if (string.IsNullOrEmpty(col) || col == "A") return "A";
            char last = col[^1];
            if (last == 'A')
                return col.Length > 1 ? ColPrev(col[..^1]) + "Z" : "A";
            return col[..^1] + (char)(last - 1);
        }

        private static (string partCode, string standardSpec) ParseCodeAndSize(string dVal, string eVal)
        {
            bool eIsSize = Regex.IsMatch(eVal, @"^\d+(\.\d+)?\s*mm$", RegexOptions.IgnoreCase);
            string partCode = !string.IsNullOrEmpty(dVal) ? dVal
                            : (!eIsSize && !string.IsNullOrEmpty(eVal)) ? eVal : "";
            string standardSpec = eIsSize ? eVal : "";
            return (partCode, standardSpec);
        }

        /// <summary>
        /// 기존 ProductMaster 데이터 마이그레이션: Spec 값이 mm 형식이 아니면 PartCode로 이동.
        /// </summary>
        private void MigrateSpecToPartCode(ObservableCollection<ProductMasterItem> master)
        {
            bool changed = false;
            foreach (var item in master)
            {
                if (string.IsNullOrEmpty(item.Spec)) continue;
                bool specIsMm = Regex.IsMatch(item.Spec, @"^\d+(\.\d+)?\s*mm$", RegexOptions.IgnoreCase);
                if (!specIsMm)
                {
                    // Spec이 mm 형식이 아니면 → PartCode로 이동 (PartCode가 비어있을 때만)
                    if (string.IsNullOrEmpty(item.PartCode))
                        item.PartCode = item.Spec;
                    item.Spec = "";
                    changed = true;
                }
            }
            if (changed) QuotationStore.SaveProductMaster(master);
        }

        // DRM 컨테이너 감지: 표준 ZIP 매직(PK\x03\x04)이 아니면 DRM 파일로 판단.
        // Excel COM으로 열어 값을 복사한 새 워크북을 임시 파일로 저장 후 경로 반환.
        private static string EnsurePlainXlsx(string filePath)
        {
            // 파일 스트림을 먼저 닫고 magic byte만 확인
            bool isZip;
            {
                using var fs = File.OpenRead(filePath);
                Span<byte> magic = stackalloc byte[4];
                int read = fs.Read(magic);
                isZip = read >= 4 && magic[0] == 0x50 && magic[1] == 0x4B
                                   && magic[2] == 0x03 && magic[3] == 0x04;
            }
            if (isZip) return filePath;

            // DRM 파일 → Excel COM으로 읽어 새 워크북에 값 복사 후 저장
            string tempPath = Path.Combine(Path.GetTempPath(),
                $"__drm_plain_{Guid.NewGuid():N}.xlsx");

            Microsoft.Office.Interop.Excel.Application? app = null;
            Microsoft.Office.Interop.Excel.Workbook?    src = null;
            Microsoft.Office.Interop.Excel.Workbook?    dst = null;
            try
            {
                app = new Microsoft.Office.Interop.Excel.Application
                {
                    Visible = false,
                    DisplayAlerts = false,
                    ScreenUpdating = false
                };

                src = app.Workbooks.Open(
                    Filename: filePath,
                    ReadOnly: true,
                    IgnoreReadOnlyRecommended: true);

                // 전략 1: 직접 SaveAs (DRM이 허용할 경우)
                bool savedDirect = false;
                try
                {
                    src.SaveAs(
                        Filename: tempPath,
                        FileFormat: Microsoft.Office.Interop.Excel.XlFileFormat.xlOpenXMLWorkbook);
                    savedDirect = true;
                }
                catch { /* DRM이 SaveAs를 막는 경우 전략 2로 */ }

                if (!savedDirect)
                {
                    // 전략 2: 값만 복사해 새 워크북에 저장
                    var srcWs = (Microsoft.Office.Interop.Excel.Worksheet)src.Sheets[1];
                    var used  = srcWs.UsedRange;
                    int rows  = used.Rows.Count;
                    int cols  = used.Columns.Count;

                    dst  = app.Workbooks.Add();
                    var dstWs = (Microsoft.Office.Interop.Excel.Worksheet)dst.Sheets[1];
                    dstWs.Range["A1"]
                         .Resize[rows, cols]
                         .Value2 = used.Value2;

                    dst.SaveAs(
                        Filename: tempPath,
                        FileFormat: Microsoft.Office.Interop.Excel.XlFileFormat.xlOpenXMLWorkbook);
                }

                return tempPath;
            }
            finally
            {
                try { dst?.Close(false); } catch { }
                try { src?.Close(false); } catch { }
                try { app?.Quit(); } catch { }
                if (dst != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(dst);
                if (src != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(src);
                if (app != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(app);
            }
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
                PartCode     = master.PartCode,
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
