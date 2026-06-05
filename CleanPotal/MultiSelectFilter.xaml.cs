using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CleanPotal
{
    /// <summary>
    /// 체크박스 드롭다운으로 여러 항목을 동시에 선택할 수 있는 필터 컨트롤.
    /// 아무것도 선택하지 않으면 '전체'(필터링 없음)로 동작한다.
    /// </summary>
    public partial class MultiSelectFilter : UserControl
    {
        public class Item : INotifyPropertyChanged
        {
            public string Name { get; set; } = "";
            internal MultiSelectFilter? Owner;
            private bool _checked;
            public bool IsChecked
            {
                get => _checked;
                set
                {
                    if (_checked == value) return;
                    _checked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                    Owner?.OnItemToggled();
                }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly ObservableCollection<Item> _items = new();
        private bool _suppress;

        /// <summary>사용자가 선택을 바꿀 때만 발생 (SetOptions/SetChecked/Clear에서는 발생하지 않음).</summary>
        public event EventHandler? SelectionChanged;

        public MultiSelectFilter()
        {
            InitializeComponent();
            ItemsHost.ItemsSource = _items;
            UpdateDisplay();
        }

        /// <summary>드롭다운 항목을 채운다 (기존 선택은 초기화).</summary>
        public void SetOptions(IEnumerable<string> options)
        {
            _suppress = true;
            _items.Clear();
            foreach (var o in options)
                _items.Add(new Item { Name = o, Owner = this });
            _suppress = false;
            UpdateDisplay();
        }

        /// <summary>지정한 값들만 체크 (이벤트 발생 안 함).</summary>
        public void SetChecked(params string[] values)
        {
            var set = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
            _suppress = true;
            foreach (var it in _items) it.IsChecked = set.Contains(it.Name);
            _suppress = false;
            UpdateDisplay();
        }

        /// <summary>모두 해제 → '전체' 상태 (이벤트 발생 안 함).</summary>
        public void Clear()
        {
            _suppress = true;
            foreach (var it in _items) it.IsChecked = false;
            _suppress = false;
            UpdateDisplay();
        }

        /// <summary>체크된 값 목록 (빈 목록 = 전체).</summary>
        public List<string> SelectedValues => _items.Where(i => i.IsChecked).Select(i => i.Name).ToList();

        public bool IsAll => !_items.Any(i => i.IsChecked);

        internal void OnItemToggled()
        {
            if (_suppress) return;
            UpdateDisplay();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ChkAll_Click(object sender, RoutedEventArgs e)
        {
            _suppress = true;
            foreach (var it in _items) it.IsChecked = false;
            _suppress = false;
            UpdateDisplay();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateDisplay()
        {
            var sel = SelectedValues;
            DisplayText.Text = sel.Count == 0 ? "전체"
                : sel.Count == 1 ? sel[0]
                : $"{sel[0]} 외 {sel.Count - 1}";
            ChkAll.IsChecked = sel.Count == 0;
        }
    }
}
