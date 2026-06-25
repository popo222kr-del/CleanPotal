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

        private string _itemCode = "";
        public string ItemCode
        {
            get => _itemCode;
            set { _itemCode = value; OnPropChanged(nameof(ItemCode)); }
        }

        private string _category = "";
        public string Category
        {
            get => _category;
            set { _category = value; OnPropChanged(nameof(Category)); }
        }

        private string _unit = "";
        public string Unit
        {
            get => _unit;
            set { _unit = value; OnPropChanged(nameof(Unit)); OnPropChanged(nameof(CurrentStockDisplay)); OnPropChanged(nameof(PreviousStockDisplay)); }
        }

        public DateTime RegisteredDate { get; set; } = DateTime.Now.Date;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropChanged(nameof(IsSelected)); }
        }

        private string _storageLocation = "";
        public string StorageLocation
        {
            get => _storageLocation;
            set { _storageLocation = value; OnPropChanged(nameof(StorageLocation)); }
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
            set
            {
                _currentStock = value;
                OnPropChanged(nameof(CurrentStock));
                OnPropChanged(nameof(CurrentStockDisplay));
                OnPropChanged(nameof(IsLow));
                NotifyWeeklyDelta();
            }
        }

        // 화면 표시용 현재 재고: 순수 숫자(콤마 허용)면 단위를 붙이고,
        // "50EA 이상" 같이 이미 단위/문구가 포함된 특수 표기는 그대로 표시
        public string CurrentStockDisplay => WithUnit(_currentStock);

        // 화면 표시용 이전 재고: 현재 재고와 동일한 규칙으로 단위 자동 표시
        public string PreviousStockDisplay => WithUnit(_previousStock);

        // 순수 숫자(콤마 허용)면 단위를 붙이고, 그 외 특수 표기는 그대로 반환
        private string WithUnit(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            string trimmed = value.Trim();
            bool numericOnly = Regex.IsMatch(trimmed.Replace(",", ""), @"^\d+(?:\.\d+)?$");
            if (numericOnly && !string.IsNullOrWhiteSpace(_unit))
                return trimmed + _unit;
            return value;
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

        // 발주 완료 여부 (체크박스)
        private bool _isOrdered;
        public bool IsOrdered
        {
            get => _isOrdered;
            set { _isOrdered = value; OnPropChanged(nameof(IsOrdered)); }
        }

        // 전주 마감 스냅샷의 재고값 (DB 컬럼 아님 — 로드 시 스냅샷에서 주입)
        private string _previousStock = "";
        public string PreviousStock
        {
            get => _previousStock;
            set { _previousStock = value; OnPropChanged(nameof(PreviousStock)); OnPropChanged(nameof(PreviousStockDisplay)); NotifyWeeklyDelta(); }
        }

        private void NotifyWeeklyDelta()
        {
            OnPropChanged(nameof(WeeklyDelta));
            OnPropChanged(nameof(WeeklyDeltaText));
            OnPropChanged(nameof(HasWeeklyDelta));
            OnPropChanged(nameof(WeeklyDeltaIsDecrease));
        }

        // 전주 대비 증감 (현재고 - 전주재고). 둘 다 숫자로 해석될 때만 계산
        public double? WeeklyDelta
        {
            get
            {
                double? cur = ParseNumber(_currentStock);
                double? prev = ParseNumber(_previousStock);
                if (cur == null || prev == null) return null;
                return cur.Value - prev.Value;
            }
        }

        public bool HasWeeklyDelta => WeeklyDelta is double d && d != 0;
        public bool WeeklyDeltaIsDecrease => WeeklyDelta is double d && d < 0;

        public string WeeklyDeltaText
        {
            get
            {
                if (WeeklyDelta is not double d) return "-";
                if (d == 0) return "0";
                double abs = Math.Abs(d);
                string n = abs % 1 == 0 ? ((long)abs).ToString() : abs.ToString("0.##");
                return d > 0 ? $"+{n}" : $"-{n}";
            }
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
            // 천단위 콤마 제거 후 선행 숫자 추출 ("1,200매" → 1200, "600매 이상" → 600)
            string cleaned = s.Trim().Replace(",", "");
            var m = Regex.Match(cleaned, @"^(\d+(?:\.\d+)?)");
            if (!m.Success) return null;
            return double.TryParse(m.Groups[1].Value, out double v) ? v : (double?)null;
        }
    }
}
