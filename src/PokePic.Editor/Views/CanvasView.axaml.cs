using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PokePic.Editor.ViewModels;
using PokePic.Editor.ViewModels.Docking;

namespace PokePic.Editor.Views;

public partial class CanvasView : UserControl
{
    /// <summary>Space around the card inside the gray area.</summary>
    public const double CardMargin = 16;

    public static Thickness CardMarginThickness { get; } = new(CardMargin);

    public static double ZoomSliderMinimum => MainViewModel.ZoomSliderMinimum;

    public static double ZoomSliderMaximum => MainViewModel.ZoomSliderMaximum;

    private MainViewModel? _main;

    public CanvasView()
    {
        InitializeComponent();
        Scroll.SizeChanged += (_, _) => Refit();
        Canvas.ContextMenu = LayerCommands.CreateMenu(Canvas, () => _main);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_main is not null)
            _main.PropertyChanged -= OnMainPropertyChanged;
        _main = (DataContext as CanvasDocument)?.Main;
        Canvas.Controller = _main;
        if (_main is not null)
            _main.PropertyChanged += OnMainPropertyChanged;
        BuildZoomMenu();
        Refit();
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsFitToWindow) or nameof(MainViewModel.CanvasPixelWidth) or nameof(MainViewModel.CanvasPixelHeight))
            Dispatcher.UIThread.Post(Refit, DispatcherPriority.Loaded);
    }

    /// <summary>Keeps the card fitted to the gray area while "fit" is on.</summary>
    private void Refit()
    {
        if (_main is not { IsFitToWindow: true } main || Scroll.Bounds.Width <= 0)
            return;
        main.FitTo(Scroll.Bounds.Width, Scroll.Bounds.Height, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1, CardMargin);
    }

    private void BuildZoomMenu()
    {
        var menu = (MenuFlyout)ZoomButton.Flyout!;
        menu.Items.Clear();
        if (_main is null)
            return;
        menu.Items.Add(new MenuItem { Header = Localization.Strings.Get("ZoomFit"), Command = _main.FitToWindowCommand });
        menu.Items.Add(new Separator());
        foreach (var zoom in MainViewModel.ZoomPresets.Reverse())
        {
            menu.Items.Add(new MenuItem
            {
                Header = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(zoom * 100)}%"),
                Command = _main.SetZoomCommand,
                CommandParameter = zoom,
            });
        }
    }

    /// <summary>A press on the gray area around the card selects the template (the card handles its own presses).</summary>
    private void OnBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _main is null)
            return;
        _main.FlushPendingEdits();
        _main.SelectPath(null);
        Canvas.Focus();
    }
}
