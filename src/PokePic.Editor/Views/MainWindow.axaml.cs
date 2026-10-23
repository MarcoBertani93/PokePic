using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PokePic.Editor.Localization;
using PokePic.Editor.Services;
using PokePic.Editor.ViewModels;

namespace PokePic.Editor.Views;

public partial class MainWindow : Window, IEditorDialogs
{
    private MainViewModel? _viewModel;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        // A click anywhere outside the field being edited confirms it, even on areas that take no focus.
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void Attach(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        viewModel.InitializeLayout();
        DataContext = viewModel;
        Dispatcher.UIThread.Post(viewModel.OnLayoutReady, DispatcherPriority.Loaded);
        BuildSamplesMenu(viewModel);
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null)
            return;
        var focused = FocusManager?.GetFocusedElement() as Visual;
        // Clicks inside the field being edited (e.g. to move the caret) do not confirm it.
        if (focused is TextBox && e.Source is Visual source && (source == focused || focused.IsVisualAncestorOf(source)))
            return;
        _viewModel.FlushPendingEdits();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        _viewModel?.FlushPendingEdits();
        if (_closeConfirmed || _viewModel?.IsDirty != true)
        {
            _viewModel?.SaveLayout();
            return;
        }

        e.Cancel = true;
        Dispatcher.UIThread.Post(async () =>
        {
            if (await ConfirmDiscardAsync())
            {
                _closeConfirmed = true;
                Close();
            }
        });
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void BuildSamplesMenu(MainViewModel viewModel)
    {
        var items = new List<Control>();
        foreach (var sample in viewModel.SampleTemplates)
        {
            items.Add(new MenuItem
            {
                Header = Strings.Format("SampleTemplate", sample.Label),
                Command = viewModel.OpenSampleTemplateCommand,
                CommandParameter = sample,
            });
        }
        if (items.Count > 0 && viewModel.SampleData.Count > 0)
            items.Add(new Separator());
        foreach (var sample in viewModel.SampleData)
        {
            items.Add(new MenuItem
            {
                Header = Strings.Format("SampleData", sample.Label),
                Command = viewModel.OpenSampleDataCommand,
                CommandParameter = sample,
            });
        }
        SamplesMenu.ItemsSource = items;
        SamplesMenu.IsEnabled = items.Count > 0;
    }

    public async Task<string?> OpenFileAsync(string title, FileFilter filter)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(filter.Name) { Patterns = filter.Pattern.Split(';') }],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> SaveFileAsync(string title, FileFilter filter, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = Path.GetExtension(filter.Pattern.Split(';')[0]).TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(filter.Name) { Patterns = filter.Pattern.Split(';') }],
        });
        return file?.TryGetLocalPath();
    }

    public Task<bool> ConfirmDiscardAsync() =>
        MessageDialog.ShowAsync(this, Strings.Get("DiscardTitle"), Strings.Get("DiscardMessage"), Strings.Get("Discard"), Strings.Get("Cancel"));

    public Task ShowErrorAsync(string message) =>
        MessageDialog.ShowAsync(this, Strings.Get("ErrorTitle"), message, "OK", null);
}

/// <summary>
/// Minimal modal message box.
/// </summary>
internal static class MessageDialog
{
    public static async Task<bool> ShowAsync(Window owner, string title, string message, string okText, string? cancelText)
    {
        bool result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var ok = new Button { Content = okText, IsDefault = true, Classes = { "accent" } };
        ok.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, IsCancel = true };
            cancel.Click += (_, _) => dialog.Close();
            buttons.Children.Add(cancel);
        }

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
        panel.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 500 });
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(owner);
        return result;
    }
}
