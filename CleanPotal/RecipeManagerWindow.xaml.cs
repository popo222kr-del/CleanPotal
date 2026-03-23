using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CleanPotal
{
    public partial class RecipeManagerWindow : Window
    {
        private ScheduleBoardViewModel _vm;

        public RecipeManagerWindow(ScheduleBoardViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            RefreshList();
        }

        private void RefreshList()
        {
            RecipeListPanel.Children.Clear();
            var recipes = _vm.GetRecipesOrdered();

            foreach (var recipe in recipes)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new TextBlock
                {
                    Text = recipe.DisplayText,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(text, 0);

                var delBtn = new Button
                {
                    Content = "삭제",
                    Tag = recipe,
                    Background = new SolidColorBrush(Color.FromRgb(254, 226, 226)),
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(12, 6, 12, 6),
                    Cursor = Cursors.Hand
                };

                // 삭제 버튼 둥글게 스타일링
                var template = new ControlTemplate(typeof(Button));
                var border = new FrameworkElementFactory(typeof(Border));
                border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));
                border.AppendChild(presenter);
                template.VisualTree = border;
                delBtn.Template = template;

                delBtn.Click += (s, e) =>
                {
                    _vm.SelectedRecipe = recipe;
                    if (_vm.TryDeleteSelectedRecipe(out string msg)) { RefreshList(); }
                    else { MessageBox.Show(msg, "알림", MessageBoxButton.OK, MessageBoxImage.Warning); }
                };
                Grid.SetColumn(delBtn, 1);

                row.Children.Add(text);
                row.Children.Add(delBtn);
                RecipeListPanel.Children.Add(row);
            }
        }

        private void AddRecipe_Click(object sender, RoutedEventArgs e)
        {
            string input = RecipeInputTextBox.Text;
            if (_vm.TryAddRecipe(input, out string msg))
            {
                RecipeInputTextBox.Clear();
                RefreshList();
            }
            else
            {
                MessageBox.Show(msg, "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}