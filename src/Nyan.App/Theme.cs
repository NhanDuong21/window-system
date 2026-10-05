using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;

namespace Nyan.App;

internal static class Theme
{
    internal static void Apply(Window window, bool dark)
    {
        var colors = new Dictionary<string, string>
        {
            ["Page"] = dark ? "#17212B" : "#F6F7F9",
            ["Surface"] = dark ? "#202E3A" : "#FFFFFF",
            ["Text"] = dark ? "#EDF1F5" : "#17212B",
            ["Muted"] = dark ? "#BAC7D2" : "#566574",
            ["Border"] = dark ? "#455866" : "#D8DEE6",
            ["Accent"] = dark ? "#9DD9BE" : "#276A55",
            ["Selection"] = dark ? "#314E45" : "#E4F0E9",
            ["Danger"] = dark ? "#FFB8AE" : "#A12E26"
        };
        foreach (var item in colors)
            window.Resources[item.Key + "Brush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Value));
        window.SetResourceReference(Control.BackgroundProperty, "PageBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 14;

        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 12, 7)));
        button.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 8)));
        button.Setters.Add(new Setter(Control.MinHeightProperty, 34d));
        button.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["SurfaceBrush"]));
        button.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        button.Setters.Add(new Setter(Control.BorderBrushProperty, window.Resources["BorderBrush"]));
        button.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        button.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        window.Resources[typeof(Button)] = button;
        foreach (var type in new[] { typeof(TextBox), typeof(ListBox), typeof(DataGrid) })
        {
            var style = new Style(type);
            style.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["SurfaceBrush"]));
            style.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, window.Resources["BorderBrush"]));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
            window.Resources[type] = style;
        }
        var comboStyle = new Style(typeof(ComboBox));
        comboStyle.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["SurfaceBrush"]));
        comboStyle.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        comboStyle.Setters.Add(new Setter(Control.BorderBrushProperty, window.Resources["BorderBrush"]));
        comboStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        comboStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
              <Grid>
                <ToggleButton Focusable="False" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                  <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1"><TextBlock Text="⌄" Foreground="{DynamicResource TextBrush}" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0"/></Border></ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter Margin="10,6,30,6" VerticalAlignment="Center" IsHitTestVisible="False" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"/>
                <Popup Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False">
                  <Border Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="280">
                    <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
                  </Border>
                </Popup>
              </Grid>
            </ControlTemplate>
            """)));
        window.Resources[typeof(ComboBox)] = comboStyle;
        var comboItem = new Style(typeof(ComboBoxItem));
        comboItem.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        comboItem.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["SurfaceBrush"]));
        comboItem.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
        var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["SelectionBrush"]));
        comboItem.Triggers.Add(highlighted); window.Resources[typeof(ComboBoxItem)] = comboItem;
        var expander = new Style(typeof(Expander));
        expander.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        window.Resources[typeof(Expander)] = expander;
        var row = new Style(typeof(DataGridRow));
        row.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["SurfaceBrush"]));
        row.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        row.Setters.Add(new Setter(Control.MinHeightProperty, 36d));
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["SelectionBrush"]));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        row.Triggers.Add(selected);
        window.Resources[typeof(DataGridRow)] = row;
        var cell = new Style(typeof(DataGridCell));
        cell.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
        cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        var cellSelected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        cellSelected.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["SelectionBrush"]));
        cellSelected.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        cell.Triggers.Add(cellSelected);
        window.Resources[typeof(DataGridCell)] = cell;
        var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 9, 10, 9)));
        header.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        header.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["PageBrush"]));
        header.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        header.Setters.Add(new Setter(Control.BorderBrushProperty, window.Resources["BorderBrush"]));
        header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        window.Resources[typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)] = header;
        var listItem = new Style(typeof(ListBoxItem));
        listItem.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
        listItem.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        listItem.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        var listSelected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        listSelected.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["SelectionBrush"]));
        listSelected.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        listItem.Triggers.Add(listSelected);
        window.Resources[typeof(ListBoxItem)] = listItem;
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(TextBlock.ForegroundProperty, window.Resources["TextBrush"]));
        window.Resources[typeof(TextBlock)] = text;
        var check = new Style(typeof(CheckBox));
        check.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["TextBrush"]));
        check.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));
        window.Resources[typeof(CheckBox)] = check;
    }
}
