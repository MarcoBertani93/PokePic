using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using PokePic.Editor.Localization;
using PokePic.Editor.ViewModels;

namespace PokePic.Editor.Views;

/// <summary>
/// What the layer tree and the canvas share: the context menu on the selected layers and the clipboard shortcuts.
/// </summary>
internal static class LayerCommands
{
    public static async Task CopyAsync(Visual visual, MainViewModel main, bool cut)
    {
        var text = cut ? main.CutSelection() : main.CopySelection();
        if (text is not null && TopLevel.GetTopLevel(visual)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public static async Task PasteAsync(Visual visual, MainViewModel main)
    {
        if (TopLevel.GetTopLevel(visual)?.Clipboard is { } clipboard && await clipboard.TryGetTextAsync() is { Length: > 0 } text)
            main.Paste(text);
    }

    /// <summary>Ctrl+C, Ctrl+X, Ctrl+V on the focused tree or canvas.</summary>
    public static bool HandleKey(KeyEventArgs e, Visual visual, MainViewModel main)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            return false;
        switch (e.Key)
        {
            case Key.C:
                _ = CopyAsync(visual, main, cut: false);
                return true;
            case Key.X:
                _ = CopyAsync(visual, main, cut: true);
                return true;
            case Key.V:
                _ = PasteAsync(visual, main);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The menu of the right click; the items follow the selection when it opens.</summary>
    public static ContextMenu CreateMenu(Control owner, Func<MainViewModel?> main)
    {
        MenuItem Item(string key, string? gesture, Action<MainViewModel> action) => new()
        {
            Header = Strings.Get(key),
            InputGesture = gesture is null ? null : KeyGesture.Parse(gesture),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
            {
                if (main() is { } m)
                    action(m);
            }),
        };

        var cut = Item("Cut", "Ctrl+X", m => _ = CopyAsync(owner, m, cut: true));
        var copy = Item("Copy", "Ctrl+C", m => _ = CopyAsync(owner, m, cut: false));
        var paste = Item("Paste", "Ctrl+V", m => _ = PasteAsync(owner, m));
        var duplicate = Item("Duplicate", "Ctrl+D", m => m.DuplicateLayerCommand.Execute(null));
        var group = Item("Group", "Ctrl+G", m => m.GroupLayerCommand.Execute(null));
        var ungroup = Item("Ungroup", "Ctrl+Shift+G", m => m.UngroupLayerCommand.Execute(null));
        var delete = Item("Delete", "Delete", m => m.DeleteLayerCommand.Execute(null));

        var menu = new ContextMenu
        {
            ItemsSource = new Control[] { cut, copy, paste, new Separator(), duplicate, group, ungroup, new Separator(), delete },
        };
        menu.Opening += (_, _) =>
        {
            var m = main();
            bool selection = m?.HasSelection == true && m.CanEditVisually;
            cut.IsEnabled = copy.IsEnabled = duplicate.IsEnabled = group.IsEnabled = delete.IsEnabled = selection;
            ungroup.IsEnabled = selection && m!.SelectedIsGroup;
            paste.IsEnabled = m?.CanEditVisually == true;
        };
        return menu;
    }
}
