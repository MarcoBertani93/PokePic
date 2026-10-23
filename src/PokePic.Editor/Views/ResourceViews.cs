using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PokePic.Editor.ViewModels;
using PokePic.Editor.ViewModels.Docking;

namespace PokePic.Editor.Views;

/// <summary>
/// Like the properties panel: in a resource panel typed names and values are applied on Enter or when the focus
/// leaves a field.
/// </summary>
internal static class ResourceViewInput
{
    public static void Attach(UserControl view, Control root)
    {
        MainViewModel? Main() => (view.DataContext as EditorTool)?.Main;

        root.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.Source is TextBox)
            {
                Main()?.FlushPendingEdits();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.LostFocusEvent, (_, _) => Main()?.FlushPendingEdits(), RoutingStrategies.Bubble, handledEventsToo: true);
    }
}

/// <summary>
/// Items in equal columns, as many as fit with at least <see cref="MinColumnWidth"/> each: one column in a side
/// dock, several in the bottom dock. Each row is as tall as its tallest item.
/// </summary>
public sealed class ColumnsPanel : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<ColumnsPanel, double>(nameof(MinColumnWidth), 280);

    static ColumnsPanel() => AffectsMeasure<ColumnsPanel>(MinColumnWidthProperty);

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    private int ColumnCount(double width) =>
        double.IsInfinity(width) ? 1 : Math.Max(1, (int)(width / Math.Max(1, MinColumnWidth)));

    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = ColumnCount(availableSize.Width);
        double width = double.IsInfinity(availableSize.Width) ? MinColumnWidth : availableSize.Width / columns;
        double height = 0;
        foreach (var row in Rows(columns))
        {
            foreach (var child in row)
                child.Measure(new Size(width, double.PositiveInfinity));
            height += row.Max(c => c.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ColumnCount(finalSize.Width);
        double width = finalSize.Width / columns, y = 0;
        foreach (var row in Rows(columns))
        {
            double height = row.Max(c => c.DesiredSize.Height);
            for (int i = 0; i < row.Count; i++)
                row[i].Arrange(new Rect(i * width, y, width, height));
            y += height;
        }
        return finalSize;
    }

    private IEnumerable<List<Control>> Rows(int columns) => Children.Chunk(columns).Select(r => r.ToList());
}
