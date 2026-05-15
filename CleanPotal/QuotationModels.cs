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

        private string _remarks = "1. VAT 별도.";

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

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

        public ObservableCollection<QuotationLineItem> LineItems { get; set; } = new();

        [JsonIgnore]
        public string DisplayTitle => !string.IsNullOrWhiteSpace(Company) ? Company : "(새 견적서)";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ProductMasterItem : INotifyPropertyChanged
    {
        private string _productName = "";
        private string _spec = "";
        private decimal _unitPrice;
        private string _unit = "EA";

        public string ProductName { get => _productName; set { _productName = value; OnPropertyChanged(nameof(ProductName)); } }
        public string Spec        { get => _spec;        set { _spec = value;        OnPropertyChanged(nameof(Spec)); } }
        public decimal UnitPrice  { get => _unitPrice;   set { _unitPrice = value;   OnPropertyChanged(nameof(UnitPrice)); } }
        public string Unit        { get => _unit;        set { _unit = value;        OnPropertyChanged(nameof(Unit)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class QuotationConfig
    {
        public string BusinessNo { get; set; } = "";
    }
}
