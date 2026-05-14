using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CleanPotal
{
    public partial class AutoCompleteBox : UserControl
    {
        private bool _suppressTextChanged;

        // ── Dependency Properties ──────────────────────────────────────────
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(AutoCompleteBox),
                new FrameworkPropertyMetadata(string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnTextPropertyChanged));

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<string>), typeof(AutoCompleteBox),
                new PropertyMetadata(null));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public IEnumerable<string>? ItemsSource
        {
            get => (IEnumerable<string>?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (AutoCompleteBox)d;
            var newText = (string)(e.NewValue ?? string.Empty);
            if (box.InputBox.Text != newText)
            {
                box._suppressTextChanged = true;
                box.InputBox.Text = newText;
                box.InputBox.CaretIndex = newText.Length;
                box._suppressTextChanged = false;
            }
        }

        public AutoCompleteBox()
        {
            InitializeComponent();
        }

        // ── Korean consonant matching ──────────────────────────────────────
        private static readonly char[] _initials =
            { 'ㄱ','ㄲ','ㄴ','ㄷ','ㄸ','ㄹ','ㅁ','ㅂ','ㅃ','ㅅ','ㅆ','ㅇ','ㅈ','ㅉ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ' };

        private static char GetInitialConsonant(char c)
        {
            if (c < 0xAC00 || c > 0xD7A3) return c;
            return _initials[(c - 0xAC00) / (21 * 28)];
        }

        private static bool IsKoreanConsonant(char c) => c >= 0x3131 && c <= 0x314E;

        // Matches if:
        //  • query is a single Korean consonant → first char of candidate shares that initial
        //  • otherwise → case-insensitive substring match
        private static bool MatchesQuery(string candidate, string query)
        {
            if (string.IsNullOrEmpty(query)) return false;

            if (query.Length == 1 && IsKoreanConsonant(query[0]))
                return candidate.Length > 0 && GetInitialConsonant(candidate[0]) == query[0];

            return candidate.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        // ── TextBox events ─────────────────────────────────────────────────
        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged) return;

            // Sync the bound Text property
            _suppressTextChanged = true;
            Text = InputBox.Text;
            _suppressTextChanged = false;

            ShowSuggestions(InputBox.Text.Trim());
        }

        private void ShowSuggestions(string query)
        {
            var source = ItemsSource?.ToList();
            if (source == null || source.Count == 0 || string.IsNullOrEmpty(query))
            {
                SuggestionPopup.IsOpen = false;
                return;
            }

            var matches = source.Where(s => MatchesQuery(s, query)).Take(12).ToList();
            if (matches.Count == 0)
            {
                SuggestionPopup.IsOpen = false;
                return;
            }

            SuggestionList.ItemsSource = matches;
            SuggestionList.SelectedIndex = -1;
            SuggestionPopup.MinWidth = InputBorder.ActualWidth;
            SuggestionPopup.IsOpen = true;
        }

        private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!SuggestionPopup.IsOpen) return;

            switch (e.Key)
            {
                case Key.Down:
                    if (SuggestionList.Items.Count > 0)
                    {
                        SuggestionList.SelectedIndex = Math.Min(SuggestionList.SelectedIndex + 1, SuggestionList.Items.Count - 1);
                        ScrollSelectedIntoView();
                    }
                    e.Handled = true;
                    break;

                case Key.Up:
                    SuggestionList.SelectedIndex = Math.Max(SuggestionList.SelectedIndex - 1, -1);
                    ScrollSelectedIntoView();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (SuggestionList.SelectedItem is string sel)
                        SelectItem(sel);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    SuggestionPopup.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        }

        private void InputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Delay so mouse click on the list can fire first
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                if (!SuggestionList.IsMouseOver)
                    SuggestionPopup.IsOpen = false;
            });
        }

        // ── ListBox events ─────────────────────────────────────────────────
        private void SuggestionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = FindListBoxItem(e.OriginalSource as DependencyObject);
            if (item?.Content is string text)
            {
                SelectItem(text);
                e.Handled = true;
            }
        }

        private void SuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SuggestionList.SelectedItem is string sel)
            {
                SelectItem(sel);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SuggestionPopup.IsOpen = false;
                InputBox.Focus();
                e.Handled = true;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────
        private void SelectItem(string item)
        {
            SuggestionPopup.IsOpen = false;
            _suppressTextChanged = true;
            InputBox.Text = item;
            InputBox.CaretIndex = item.Length;
            _suppressTextChanged = false;
            Text = item;
            InputBox.Focus();
        }

        private void ScrollSelectedIntoView()
        {
            if (SuggestionList.SelectedItem != null)
                SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
        }

        private static ListBoxItem? FindListBoxItem(DependencyObject? source)
        {
            while (source != null && source is not ListBoxItem)
                source = VisualTreeHelper.GetParent(source);
            return source as ListBoxItem;
        }
    }
}
