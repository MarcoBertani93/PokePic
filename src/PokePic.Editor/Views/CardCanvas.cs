using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PokePic.Core.Editing;
using PokePic.Editor.ViewModels;

namespace PokePic.Editor.Views;

/// <summary>
/// Shows the rendered card at the preview zoom, with the selection overlay, and turns pointer input into
/// template coordinates for the view model. The bitmap is enlarged with nearest-neighbor sampling (so a
/// pixel-perfect render keeps its pixels) and reduced with smooth sampling.
/// </summary>
public sealed class CardCanvas : Control
{
    private const double HandleSize = 7;
    private const double HandleHitSize = 10;
    private const double Checker = 8;

    public static readonly StyledProperty<Bitmap?> SourceProperty = AvaloniaProperty.Register<CardCanvas, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<double> ZoomProperty = AvaloniaProperty.Register<CardCanvas, double>(nameof(Zoom), 1);
    public static readonly StyledProperty<double> RenderScaleProperty = AvaloniaProperty.Register<CardCanvas, double>(nameof(RenderScale), 1);
    public static readonly StyledProperty<RectF?> SelectionProperty = AvaloniaProperty.Register<CardCanvas, RectF?>(nameof(Selection));
    public static readonly StyledProperty<IReadOnlyList<HandlePoint>> HandlesProperty = AvaloniaProperty.Register<CardCanvas, IReadOnlyList<HandlePoint>>(nameof(Handles), []);
    public static readonly StyledProperty<IReadOnlyList<RectF>> PartsProperty = AvaloniaProperty.Register<CardCanvas, IReadOnlyList<RectF>>(nameof(Parts), []);

    private static readonly IBrush CheckerLight = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static readonly IBrush CheckerDark = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    private static readonly IPen SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x9C, 0xFF)), 1);
    private static readonly IPen SelectionHalo = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), 3);
    private static readonly IPen PartPen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0x00, 0x9C, 0xFF)), 1, new DashStyle([2, 2], 0));
    private static readonly IBrush HandleFill = Brushes.White;

    private bool _pressed;

    static CardCanvas()
    {
        AffectsMeasure<CardCanvas>(SourceProperty, ZoomProperty, RenderScaleProperty);
        AffectsRender<CardCanvas>(SourceProperty, ZoomProperty, RenderScaleProperty, SelectionProperty, HandlesProperty, PartsProperty);
        FocusableProperty.OverrideDefaultValue<CardCanvas>(true);
    }

    public CardCanvas()
    {
        ClipToBounds = false;
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Screen (device) pixels per template pixel.</summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Bitmap pixels per template pixel (the scale <see cref="Source"/> was rendered at).</summary>
    public double RenderScale
    {
        get => GetValue(RenderScaleProperty);
        set => SetValue(RenderScaleProperty, value);
    }

    public RectF? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    public IReadOnlyList<HandlePoint> Handles
    {
        get => GetValue(HandlesProperty);
        set => SetValue(HandlesProperty, value);
    }

    /// <summary>Outlines of the single elements of a multiple selection.</summary>
    public IReadOnlyList<RectF> Parts
    {
        get => GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    public MainViewModel? Controller { get; set; }

    private double Scaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    /// <summary>Device-independent pixels per template pixel.</summary>
    private double Factor => Math.Max(0.01, Zoom) / Scaling;

    /// <summary>The card in device-independent pixels.</summary>
    private Size CardSize(Bitmap bitmap)
    {
        double scale = Math.Max(0.01, RenderScale);
        return new Size(bitmap.PixelSize.Width / scale * Factor, bitmap.PixelSize.Height / scale * Factor);
    }

    protected override Size MeasureOverride(Size availableSize) => Source is null ? default : CardSize(Source);

    public override void Render(DrawingContext context)
    {
        if (Source is not { } bitmap)
            return;

        var size = new Rect(CardSize(bitmap));

        // Checkerboard behind transparent pixels.
        context.FillRectangle(CheckerLight, size);
        for (double y = 0; y < size.Height; y += Checker)
        {
            for (double x = ((int)(y / Checker) % 2) * Checker; x < size.Width; x += 2 * Checker)
                context.FillRectangle(CheckerDark, new Rect(x, y, Math.Min(Checker, size.Width - x), Math.Min(Checker, size.Height - y)));
        }

        // Enlarging: whole pixels (fractional zooms give pixels of uneven size, as in any image editor). Reducing: smooth.
        var interpolation = Zoom >= RenderScale - 0.001 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = interpolation }))
            context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), size);

        foreach (var part in Parts)
            context.DrawRectangle(null, PartPen, ToView(part));
        if (Selection is { } s)
        {
            var r = ToView(s);
            context.DrawRectangle(null, SelectionHalo, r);
            context.DrawRectangle(null, SelectionPen, r);
        }
        foreach (var h in Handles)
        {
            var c = ToView(h.X, h.Y);
            var box = new Rect(c.X - HandleSize / 2, c.Y - HandleSize / 2, HandleSize, HandleSize);
            context.DrawRectangle(HandleFill, SelectionPen, box);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Controller is null)
            return;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // The context menu opens on release: first make sure it acts on what is under the pointer.
            Focus();
            var (cx, cy) = ToTemplate(e.GetPosition(this));
            Controller.CanvasContextPressed(cx, cy);
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus();
        e.Pointer.Capture(this);
        _pressed = true;
        var p = e.GetPosition(this);
        var (x, y) = ToTemplate(p);
        Controller.CanvasPressed(x, y, HandleAt(p), e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Controller is null)
            return;
        var p = e.GetPosition(this);
        var (x, y) = ToTemplate(p);
        if (_pressed)
        {
            Controller.CanvasDragged(x, y);
        }
        else
        {
            Controller.OnCursorOverCanvas(x, y);
            // Handles resize; inside the selection the four-arrow cursor shows it can be dragged.
            Cursor = HandleAt(p) is { } h ? CursorFor(h)
                : Selection is { } s && s.Contains(x, y) ? MoveCursor
                : Cursor.Default;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed)
            return;
        _pressed = false;
        e.Pointer.Capture(null);
        Controller?.CanvasReleased();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_pressed)
        {
            _pressed = false;
            Controller?.CanvasReleased();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Controller?.OnCursorOverCanvas(-1, -1);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && Controller is not null)
        {
            (e.Delta.Y > 0 ? Controller.ZoomInCommand : Controller.ZoomOutCommand).Execute(null);
            e.Handled = true;
            return;
        }
        base.OnPointerWheelChanged(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Controller is null)
            return;
        if (LayerCommands.HandleKey(e, this, Controller))
        {
            e.Handled = true;
            return;
        }
        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Left:
                Controller.Nudge(-step, 0);
                break;
            case Key.Right:
                Controller.Nudge(step, 0);
                break;
            case Key.Up:
                Controller.Nudge(0, -step);
                break;
            case Key.Down:
                Controller.Nudge(0, step);
                break;
            case Key.Delete:
                Controller.DeleteLayerCommand.Execute(null);
                break;
            case Key.Escape:
                Controller.ClearSelectionCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private (float X, float Y) ToTemplate(Point p) => ((float)(p.X / Factor), (float)(p.Y / Factor));

    private Point ToView(float x, float y) => new(x * Factor, y * Factor);

    private Rect ToView(RectF r) => new(r.X * Factor, r.Y * Factor, r.Width * Factor, r.Height * Factor);

    private HandleKind? HandleAt(Point p)
    {
        foreach (var h in Handles)
        {
            var c = ToView(h.X, h.Y);
            if (Math.Abs(p.X - c.X) <= HandleHitSize / 2 && Math.Abs(p.Y - c.Y) <= HandleHitSize / 2)
                return h.Kind;
        }
        return null;
    }

    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);

    private static Cursor CursorFor(HandleKind h) => new(h switch
    {
        HandleKind.TopLeft => StandardCursorType.TopLeftCorner,
        HandleKind.TopRight => StandardCursorType.TopRightCorner,
        HandleKind.BottomLeft => StandardCursorType.BottomLeftCorner,
        HandleKind.BottomRight => StandardCursorType.BottomRightCorner,
        HandleKind.Top or HandleKind.Bottom => StandardCursorType.SizeNorthSouth,
        HandleKind.Left or HandleKind.Right => StandardCursorType.SizeWestEast,
        _ => StandardCursorType.Cross,
    });
}
