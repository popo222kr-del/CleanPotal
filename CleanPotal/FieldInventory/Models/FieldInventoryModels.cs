using System;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace CleanPotal.FieldInventory.Models
{
    public class FieldInventoryItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public long ItemId { get; set; }
        public int OrderNo { get; set; }

        private string _storageLocation = "";
        public string StorageLocation
        {
            get => _storageLocation;
            set { _storageLocation = value; OnPropChanged(nameof(StorageLocation)); }
        }

        // 그리드에서 같은 보관위치가 연속될 때 첫 행에만 표시(엑셀 셀 병합 효과)하기 위한 표시 전용 값
        private string _displayStorageLocation = "";
        public string DisplayStorageLocation
        {
            get => _displayStorageLocation;
            set { _displayStorageLocation = value; OnPropChanged(nameof(DisplayStorageLocation)); }
        }

        private string _itemName = "";
        public string ItemName
        {
            get => _itemName;
            set { _itemName = value; OnPropChanged(nameof(ItemName)); }
        }

        private string _currentStock = "";
        public string CurrentStock
        {
            get => _currentStock;
            set { _currentStock = value; OnPropChanged(nameof(CurrentStock)); OnPropChanged(nameof(IsLow)); }
        }

        private string _appropriateStock = "";
        public string AppropriateStock
        {
            get => _appropriateStock;
            set { _appropriateStock = value; OnPropChanged(nameof(AppropriateStock)); OnPropChanged(nameof(IsLow)); }
        }

        private string _minOrderQty = "";
        public string MinOrderQty
        {
            get => _minOrderQty;
            set { _minOrderQty = value; OnPropChanged(nameof(MinOrderQty)); }
        }

        private string _supplier = "";
        public string Supplier
        {
            get => _supplier;
            set { _supplier = value; OnPropChanged(nameof(Supplier)); }
        }

        private string _orderDate = "";
        public string OrderDate
        {
            get => _orderDate;
            set { _orderDate = value; OnPropChanged(nameof(OrderDate)); }
        }

        private string _orderQty = "";
        public string OrderQty
        {
            get => _orderQty;
            set { _orderQty = value; OnPropChanged(nameof(OrderQty)); }
        }

        private string _expectedReceipt = "";
        public string ExpectedReceipt
        {
            get => _expectedReceipt;
            set { _expectedReceipt = value; OnPropChanged(nameof(ExpectedReceipt)); }
        }

        private string _memo = "";
        public string Memo
        {
            get => _memo;
            set { _memo = value; OnPropChanged(nameof(Memo)); }
        }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // 현재재고 ≤ 적정재고이면 true → 행을 빨간색으로 표시
        public bool IsLow
        {
            get
            {
                double? cur = ParseNumber(_currentStock);
                double? apt = ParseNumber(_appropriateStock);
                if (cur == null || apt == null) return false;
                return cur.Value <= apt.Value;
            }
        }

        private static double? ParseNumber(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var m = Regex.Match(s.Trim(), @"^(\d+(?:\.\d+)?)");
            if (!m.Success) return null;
            return double.TryParse(m.Groups[1].Value, out double v) ? v : (double?)null;
        }
    }
}
