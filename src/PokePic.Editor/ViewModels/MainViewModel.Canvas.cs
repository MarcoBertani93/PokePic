using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokePic.Core.Editing;
using PokePic.Core.Model;
using PokePic.Core.Rendering;
using PokePic.Core.Resolution;
using PokePic.Editor.Localization;
using PokePic.Editor.Services;

namespace PokePic.Editor.ViewModels;

public enum HandleKind
{
    Move,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    LineFrom,
    LineTo,
}

/// <param name="X">Absolute canvas coordinate (template pixels).</param>
public readonly record struct HandlePoint(HandleKind Kind, float X, float Y);

public sealed partial class MainViewModel
{
    private IReadOnlyList<ElementBox> _boxes = [];
    private DragState? _drag;

    public const double MinZoom = 0.1;
    public const double MaxZoom = 32;

    /// <summary>Steps of the zoom buttons, Ctrl+wheel and the zoom menu.</summary>
    public static IReadOnlyList<double> ZoomPresets { get; } =
        [0.1, 0.15, 0.2, 0.25, 0.33, 0.5, 0.66, 1, 1.5, 2, 3, 4, 5, 6, 8, 10, 12, 16, 24, 32];

    /// <summary>
    /// Highest scale a high-resolution preview is rendered at; above it the view enlarges the bitmap.
    /// Pixel-perfect previews are always rendered at scale 1 and enlarged by the view with nearest-neighbor
    /// sampling, which gives the same pixels as the renderer's own upscale.
    /// </summary>
    private const double MaxPreviewRenderScale = 16;

    private bool _fitting;

    /// <summary>Preview zoom: screen (device) pixels per template pixel, 1 = 100%. Need not be an integer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText), nameof(ZoomSlider))]
    public partial double Zoom { get; set; } = 1;

    /// <summary>While set, the view keeps the card fitted to the canvas area; any other zoom clears it.</summary>
    [ObservableProperty]
    public partial bool IsFitToWindow { get; set; }

    /// <summary>Scale the bitmap currently shown was rendered at (it can lag behind <see cref="Zoom"/>).</summary>
    [ObservableProperty]
    public partial double RenderedScale { get; set; } = 1;

    /// <summary>Template canvas size, from the last render.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasSizeText))]
    public partial int CanvasPixelWidth { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasSizeText))]
    public partial int CanvasPixelHeight { get; set; }

    public string CanvasSizeText => CanvasPixelWidth > 0 ? $"{CanvasPixelWidth} × {CanvasPixelHeight}" : "";

    public string ZoomText => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Round(Zoom * 100)}%");

    /// <summary>The zoom slider works on a logarithmic scale, like the presets.</summary>
    public double ZoomSlider
    {
        get => Math.Log2(Zoom);
        set => Zoom = Math.Round(Math.Pow(2, value), 2);
    }

    public static double ZoomSliderMinimum => Math.Log2(MinZoom);

    public static double ZoomSliderMaximum => Math.Log2(MaxZoom);

    partial void OnZoomChanged(double value)
    {
        if (value is < MinZoom or > MaxZoom || double.IsNaN(value))
        {
            Zoom = double.IsNaN(value) ? 1 : Math.Clamp(value, MinZoom, MaxZoom);
            return;
        }
        if (!_fitting)
            IsFitToWindow = false;
        // Pixel-perfect previews are enlarged by the view: only high resolution renders again.
        if (Mode == RenderMode.HighResolution && Math.Abs(PreviewRenderScale() - RenderedScale) > 0.001)
            ScheduleRender(0);
    }

    private double PreviewRenderScale() => Mode == RenderMode.HighResolution ? Math.Min(Zoom, MaxPreviewRenderScale) : 1;

    /// <summary>Sets the zoom that fits the card in a viewport of the given size (device-independent pixels).</summary>
    public void FitTo(double width, double height, double renderScaling, double margin)
    {
        if (CanvasPixelWidth <= 0 || CanvasPixelHeight <= 0 || width <= 2 * margin || height <= 2 * margin)
            return;
        double zoom = Math.Min((width - 2 * margin) / CanvasPixelWidth, (height - 2 * margin) / CanvasPixelHeight) * renderScaling;
        _fitting = true;
        try
        {
            Zoom = Math.Clamp(Math.Floor(zoom * 100) / 100, MinZoom, MaxZoom);
        }
        finally
        {
            _fitting = false;
        }
    }

    [RelayCommand]
    private void ZoomIn() => Zoom = ZoomPresets.FirstOrDefault(z => z > Zoom + 0.001, MaxZoom);

    [RelayCommand]
    private void ZoomOut() => Zoom = ZoomPresets.LastOrDefault(z => z < Zoom - 0.001, MinZoom);

    [RelayCommand]
    private void SetZoom(double zoom) => Zoom = zoom;

    [RelayCommand]
    private void ActualSize() => Zoom = 1;

    [RelayCommand]
    private void FitToWindow() => IsFitToWindow = true;

    /// <summary>The button next to the zoom: fitted ⇄ 100%.</summary>
    [RelayCommand]
    private void ToggleFit()
    {
        if (IsFitToWindow)
            Zoom = 1;
        else
            IsFitToWindow = true;
    }

    [ObservableProperty]
    public partial RectF? SelectionBounds { get; set; }

    /// <summary>With several elements selected: the outline of each one inside <see cref="SelectionBounds"/>.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RectF> SelectionParts { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<HandlePoint> SelectionHandles { get; set; } = [];

    public void OnCursorOverCanvas(float x, float y) =>
        CursorPosition = x < 0 || y < 0 ? "" : $"{(int)MathF.Floor(x)}, {(int)MathF.Floor(y)}";

    /// <summary>
    /// Press on the canvas. A handle of a single selection resizes. A press on a selected element (or on a child of
    /// a selected group) moves the whole selection. A press on another element only selects it, the topmost under
    /// the pointer: moving it takes a second press, so a click never moves something by accident.
    /// With <paramref name="toggle"/> (Ctrl) the element is added to or removed from the selection.
    /// </summary>
    public void CanvasPressed(float x, float y, HandleKind? handle, bool toggle = false)
    {
        FlushPendingEdits();
        if (_document is null)
        {
            Status = Strings.Get("VisualDisabled");
            return;
        }

        if (handle is { } h && !toggle && _selection.Count == 1)
        {
            BeginDrag(h, x, y);
            return;
        }

        var hit = ElementLayout.HitTest(_boxes, x, y)?.Element.Path;
        if (toggle)
        {
            if (hit is not null)
                ToggleSelection(hit);
            return;
        }

        bool onSelection = hit is not null
            ? _selection.Any(p => p == hit || TemplateDocument.IsInside(hit, p))
            : SelectionBounds is { } bounds && bounds.Contains(x, y); // empty area of a selected group
        if (onSelection)
            BeginDrag(HandleKind.Move, x, y);
        else
            SelectPath(hit);
    }

    /// <summary>
    /// Right press, before the context menu: an element outside the selection becomes the selection, so the menu
    /// acts on what is under the pointer; on the selection, the selection stays.
    /// </summary>
    public void CanvasContextPressed(float x, float y)
    {
        FlushPendingEdits();
        if (_document is null)
            return;
        var hit = ElementLayout.HitTest(_boxes, x, y)?.Element.Path;
        bool onSelection = hit is not null
            ? _selection.Any(p => p == hit || TemplateDocument.IsInside(hit, p))
            : SelectionBounds is { } bounds && bounds.Contains(x, y);
        if (!onSelection)
            SelectPath(hit);
    }

    public void CanvasDragged(float x, float y)
    {
        OnCursorOverCanvas(x, y);
        if (_drag is not { } drag)
            return;

        int dx = (int)MathF.Round(x - drag.StartX);
        int dy = (int)MathF.Round(y - drag.StartY);
        if ((dx, dy) == (drag.LastDx, drag.LastDy))
            return;
        drag.LastDx = dx;
        drag.LastDy = dy;

        var doc = drag.Snapshot.Clone();
        if (!ApplyDrag(doc, drag, dx, dy))
            return;
        drag.Current = doc;

        // Live feedback without touching the text: render and update the overlay from the dragged copy.
        var text = doc.ToJson();
        _boxes = PreviewRenderer.Layout(text, _data);
        UpdateOverlay();
        ScheduleRender(0, text);
        Properties?.Load(doc);
    }

    public void CanvasReleased()
    {
        if (_drag is not { } drag)
            return;
        _drag = null;
        if (drag.Current is { } doc)
            CommitDocument(doc, _selection, rebuildProperties: false);
    }

    /// <summary>Arrow keys: moves the selection by whole pixels.</summary>
    public void Nudge(int dx, int dy)
    {
        if (!HasSelection)
            return;
        ApplyEditMany(doc =>
        {
            if (!TemplateDocument.TopLevel(_selection).All(p => doc.Translate(p, dx, dy)))
                Status = Strings.Get("BoundCoordinates");
            return _selection;
        }, rebuildProperties: false);
    }

    private void BeginDrag(HandleKind kind, float x, float y)
    {
        var snapshot = _document!.Clone();
        var paths = TemplateDocument.TopLevel(_selection);
        if (paths.Count == 0 || paths.Any(p => snapshot.GetElement(p) is null))
            return;

        float[]? rect = null, point = null;
        if (kind != HandleKind.Move)
        {
            var element = snapshot.GetElement(paths[0])!;
            if (kind is HandleKind.LineFrom or HandleKind.LineTo)
                point = ReadLiteral(element[kind == HandleKind.LineFrom ? "from" : "to"], 2);
            else
                rect = ReadLiteral(element["rect"], 4);
            if (rect is null && point is null)
            {
                Status = Strings.Get("BoundCoordinates");
                return;
            }
        }

        _drag = new DragState(paths, kind, snapshot, x, y, rect, point);
    }

    private bool ApplyDrag(TemplateDocument doc, DragState drag, int dx, int dy)
    {
        switch (drag.Kind)
        {
            case HandleKind.Move:
                if (!drag.Paths.All(p => doc.Translate(p, dx, dy)))
                {
                    Status = Strings.Get("BoundCoordinates");
                    return false;
                }
                return true;

            case HandleKind.LineFrom:
            case HandleKind.LineTo:
                doc.SetPoint(drag.Paths[0], drag.Kind == HandleKind.LineFrom ? "from" : "to", drag.StartPoint![0] + dx, drag.StartPoint[1] + dy);
                return true;

            default:
                var (x, y, w, h) = Resize(drag.StartRect!, drag.Kind, dx, dy);
                doc.SetRect(drag.Paths[0], x, y, w, h);
                return true;
        }
    }


    internal static (float X, float Y, float W, float H) Resize(float[] r, HandleKind handle, int dx, int dy)
    {
        float x = r[0], y = r[1], w = r[2], h = r[3];
        bool left = handle is HandleKind.TopLeft or HandleKind.Left or HandleKind.BottomLeft;
        bool right = handle is HandleKind.TopRight or HandleKind.Right or HandleKind.BottomRight;
        bool top = handle is HandleKind.TopLeft or HandleKind.Top or HandleKind.TopRight;
        bool bottom = handle is HandleKind.BottomLeft or HandleKind.Bottom or HandleKind.BottomRight;

        if (left)
        {
            float nw = Math.Max(1, w - dx);
            x += w - nw;
            w = nw;
        }
        if (right)
            w = Math.Max(1, w + dx);
        if (top)
        {
            float nh = Math.Max(1, h - dy);
            y += h - nh;
            h = nh;
        }
        if (bottom)
            h = Math.Max(1, h + dy);
        return (x, y, w, h);
    }

    private static float[]? ReadLiteral(JsonNode? node, int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            if (TemplateDocument.Number(node, i) is not { } v)
                return null;
            values[i] = v;
        }
        return values;
    }

    /// <summary>
    /// Recomputes the selection rectangle and handles from the current element boxes.
    /// </summary>
    private void UpdateOverlay()
    {
        var boxes = _boxes.Where(b => _selection.Contains(b.Element.Path)).ToList();
        if (boxes.Count == 0)
        {
            SelectionBounds = null;
            SelectionParts = [];
            SelectionHandles = [];
            return;
        }
        if (boxes.Count > 1 || _selection.Count > 1)
        {
            // Several elements: they move together, but only a single element can be resized.
            SelectionBounds = boxes.Skip(1).Aggregate(boxes[0].Bounds, (u, b) => u.Union(b.Bounds));
            SelectionParts = [.. boxes.Select(b => b.Bounds)];
            SelectionHandles = [];
            return;
        }

        var box = boxes[0];
        SelectionBounds = box.Bounds;
        SelectionParts = [];
        switch (box.Element)
        {
            case GroupElement:
                SelectionHandles = [];
                break;
            case LineElement line:
                var r = new BindingResolver(_data);
                try
                {
                    SelectionHandles =
                    [
                        new(HandleKind.LineFrom, box.OriginX + r.Resolve(line.From.X, line.Path) + 0.5f, box.OriginY + r.Resolve(line.From.Y, line.Path) + 0.5f),
                        new(HandleKind.LineTo, box.OriginX + r.Resolve(line.To.X, line.Path) + 0.5f, box.OriginY + r.Resolve(line.To.Y, line.Path) + 0.5f),
                    ];
                }
                catch (Core.TemplateException)
                {
                    SelectionHandles = [];
                }
                break;
            default:
                var b = box.Bounds;
                float mx = b.X + b.Width / 2, my = b.Y + b.Height / 2;
                SelectionHandles =
                [
                    new(HandleKind.TopLeft, b.X, b.Y), new(HandleKind.Top, mx, b.Y), new(HandleKind.TopRight, b.Right, b.Y),
                    new(HandleKind.Right, b.Right, my), new(HandleKind.BottomRight, b.Right, b.Bottom), new(HandleKind.Bottom, mx, b.Bottom),
                    new(HandleKind.BottomLeft, b.X, b.Bottom), new(HandleKind.Left, b.X, my),
                ];
                break;
        }
    }

    /// <param name="paths">Top-level selected elements; resizing (any kind but Move) uses only the first.</param>
    private sealed class DragState(IReadOnlyList<string> paths, HandleKind kind, TemplateDocument snapshot, float startX, float startY, float[]? startRect, float[]? startPoint)
    {
        public IReadOnlyList<string> Paths { get; } = paths;
        public HandleKind Kind { get; } = kind;
        public TemplateDocument Snapshot { get; } = snapshot;
        public float StartX { get; } = startX;
        public float StartY { get; } = startY;
        public float[]? StartRect { get; } = startRect;
        public float[]? StartPoint { get; } = startPoint;
        public int LastDx { get; set; }
        public int LastDy { get; set; }
        public TemplateDocument? Current { get; set; }
    }
}
