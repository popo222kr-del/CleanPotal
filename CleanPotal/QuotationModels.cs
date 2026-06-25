using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CleanPotal
{
    public class QuotationLineItem : INotifyPropertyChanged
    {
        private int _no = 1;
        private string _description = "";
        private string _partCode = "";
        private decimal _listPrice;
        private string _standardSpec = "";
        private int _qty = 1;

        public int No
        {
            get => _no;
            set { _no = value; OnPropertyChanged(nameof(No)); }
        }
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }
        public string PartCode
        {
            get => _partCode;
            set { _partCode = value; OnPropertyChanged(nameof(PartCode)); }
        }
        public decimal ListPrice
        {
            get => _listPrice;
            set { _listPrice = value; OnPropertyChanged(nameof(ListPrice)); OnPropertyChanged(nameof(Amount)); }
        }
        public string StandardSpec
        {
            get => _standardSpec;
            set { _standardSpec = value; OnPropertyChanged(nameof(StandardSpec)); }
        }
        public int Qty
        {
            get => _qty;
            set { _qty = value; OnPropertyChanged(nameof(Qty)); OnPropertyChanged(nameof(Amount)); }
        }

        [JsonIgnore]
        public decimal Amount => ListPrice * Qty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class QuotationModel : INotifyPropertyChanged
    {
        // 고객사 정보
        private string _attention = "";
        private string _company = "";
        private string _email = "";
        private string _phone = "";

        // 견적 정보
        private string _date = DateTime.Today.ToString("yyyy-MM-dd");
        private string _validity = "";
        private string _aetsManager = "";
        private string _aetsPhone = "";
        private string _businessNo = "";

        private string _rfqNo = "";
        private string _remarks = "1. VAT 별도.";

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        public string CreatedBy { get; set; } = "";
        public string LastModifiedBy { get; set; } = "";
        public string LastModifiedAt { get; set; } = "";
        public string QuoteNo { get; set; } = "";
        public string RfqNo { get => _rfqNo; set { _rfqNo = value; OnPropertyChanged(nameof(RfqNo)); } }

        // 고객사 정보
        public string Attention { get => _attention; set { _attention = value; OnPropertyChanged(nameof(Attention)); } }
        public string Company   { get => _company;   set { _company = value;   OnPropertyChanged(nameof(Company)); OnPropertyChanged(nameof(DisplayTitle)); } }
        public string Email     { get => _email;     set { _email = value;     OnPropertyChanged(nameof(Email)); } }
        public string Phone     { get => _phone;     set { _phone = value;     OnPropertyChanged(nameof(Phone)); } }

        // 견적 정보
        public string Date        { get => _date;        set { _date = value;        OnPropertyChanged(nameof(Date)); } }
        public string Validity    { get => _validity;    set { _validity = value;    OnPropertyChanged(nameof(Validity)); } }
        public string AetsManager { get => _aetsManager; set { _aetsManager = value; OnPropertyChanged(nameof(AetsManager)); } }
        public string AetsPhone   { get => _aetsPhone;   set { _aetsPhone = value;   OnPropertyChanged(nameof(AetsPhone)); } }
        public string BusinessNo  { get => _businessNo;  set { _businessNo = value;  OnPropertyChanged(nameof(BusinessNo)); } }

        public string Remarks { get => _remarks; set { _remarks = value; OnPropertyChanged(nameof(Remarks)); } }

        private string _memo = "";
        public string Memo { get => _memo; set { _memo = value; OnPropertyChanged(nameof(Memo)); } }

        public string SourceFileName { get; set; } = "";

        public ObservableCollection<QuotationLineItem> LineItems { get; set; } = new();

        [JsonIgnore]
        public decimal TotalAmount => LineItems.Sum(x => x.Amount);

        [JsonIgnore]
        public int TotalQty => LineItems.Sum(x => x.Qty);

        [JsonIgnore]
        public string DisplayTitle => !string.IsNullOrWhiteSpace(Company) ? Company : "(새 견적서)";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ProductMasterItem : INotifyPropertyChanged
    {
        private string _productName = "";
        private string _partCode = "";
        private string _spec = "";
        private decimal _unitPrice;
        private string _vendorName = "";

        private string _updatedBy = "";
        private string _updatedAt = "";

        public string ProductName { get => _productName; set { _productName = value; OnPropertyChanged(nameof(ProductName)); OnPropertyChanged(nameof(DisplayName)); } }
        public string PartCode    { get => _partCode;    set { _partCode = value;    OnPropertyChanged(nameof(PartCode)); } }
        public string Spec        { get => _spec;        set { _spec = value;        OnPropertyChanged(nameof(Spec)); } }
        public decimal UnitPrice  { get => _unitPrice;   set { _unitPrice = value;   OnPropertyChanged(nameof(UnitPrice)); } }
        public string VendorName  { get => _vendorName;  set { _vendorName = value;  OnPropertyChanged(nameof(VendorName)); OnPropertyChanged(nameof(DisplayName)); } }
        // 단위는 항상 EA (1EA 기준)
        public string Unit { get; set; } = "EA";

        // 항목을 마지막으로 등록/수정한 사람과 시각
        public string UpdatedBy { get => _updatedBy; set { _updatedBy = value; OnPropertyChanged(nameof(UpdatedBy)); OnPropertyChanged(nameof(UpdateInfo)); } }
        public string UpdatedAt { get => _updatedAt; set { _updatedAt = value; OnPropertyChanged(nameof(UpdatedAt)); OnPropertyChanged(nameof(UpdateInfo)); } }

        [JsonIgnore]
        public string DisplayName => !string.IsNullOrWhiteSpace(VendorName) ? $"[{VendorName}] {ProductName}" : ProductName;

        [JsonIgnore]
        public string UpdateInfo
        {
            get
            {
                if (string.IsNullOrWhiteSpace(UpdatedBy)) return "";
                string dateStr = DateTime.TryParse(UpdatedAt, out var dt) ? dt.ToString("yy.MM.dd HH:mm") : UpdatedAt;
                return string.IsNullOrEmpty(dateStr) ? $"수정: {UpdatedBy}" : $"수정: {UpdatedBy} ({dateStr})";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class QuotationConfig
    {
        public string BusinessNo { get; set; } = "";
    }
}
