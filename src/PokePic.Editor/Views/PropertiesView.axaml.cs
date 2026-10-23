using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PokePic.Editor.ViewModels;
using PokePic.Editor.ViewModels.Docking;
using PokePic.Editor.ViewModels.Properties;

namespace PokePic.Editor.Views;

/// <summary>
/// Fields keep typed text pending; it is applied on Enter, when focus leaves a field,
/// when a suggestion or a color is picked, and before any other editor action.
/// </summary>
public partial class PropertiesView : UserControl
{
    public PropertiesView()
    {
        InitializeComponent();
        Scroll.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Scroll.AddHandler(LostFocusEvent, (_, _) => Main?.FlushPendingEdits(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private MainViewModel? Main => (DataContext as PropertiesTool)?.Main;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source is TextBox { AcceptsReturn: false })
        {
            Main?.FlushPendingEdits();
            e.Handled = true;
        }
    }

    private sealed record OpenedSelection(object? Item);

    private void OnComboDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox combo)
            combo.Tag = new OpenedSelection(combo.SelectedItem);
    }

    /// <summary>
    /// Applies a suggestion only if one was picked while the list was open. SelectedItem is never cleared:
    /// on an editable combo box that also clears the text, which would then be committed as an empty value.
    /// </summary>
    private void OnComboDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox { DataContext: ValueField field } combo)
            return;
        bool picked = combo.Tag is OpenedSelection opened && !Equals(opened.Item, combo.SelectedItem);
        combo.Tag = null;
        if (picked && combo.SelectedItem is string value)
            field.Text = value;
        Main?.FlushPendingEdits();
    }

    private void OnColorFlyoutClosed(object? sender, EventArgs e)
    {
        if (sender is Flyout { Target.DataContext: ValueField field })
            field.CommitPickerColor();
    }
}
