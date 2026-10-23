using PokePic.Core.Data;
using PokePic.Core.Model;
using PokePic.Core.Resolution;

namespace PokePic.Core.Editing;

public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;

    public RectF Union(RectF other)
    {
        float left = Math.Min(X, other.X), top = Math.Min(Y, other.Y);
        return new RectF(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }

    public RectF Offset(float dx, float dy) => this with { X = X + dx, Y = Y + dy };
}

/// <param name="Bounds">Absolute canvas bounds (group offsets applied).</param>
/// <param name="Origin">Absolute position of the element's coordinate space, i.e. the sum of its parents' offsets.</param>
/// <param name="Visible">True if the element and all its parents are visible.</param>
public sealed record ElementBox(Element Element, RectF Bounds, float OriginX, float OriginY, bool Visible, int Depth);

/// <summary>
/// Computes where each element is on the canvas, for selection and dragging in the editor.
/// </summary>
public static class ElementLayout
{
    /// <summary>
    /// Boxes in drawing order (parents before their children). Elements whose values cannot be
    /// resolved with the given data are skipped.
    /// </summary>
    public static IReadOnlyList<ElementBox> Compute(CardTemplate template, CardData data)
    {
        var resolver = BindingResolver.For(template, data);
        var boxes = new List<ElementBox>();
        Visit(template.Elements, 0, 0, true, 0);
        return boxes;

        RectF? Visit(IEnumerable<Element> elements, float ox, float oy, bool parentVisible, int depth)
        {
            RectF? union = null;
            foreach (var element in elements)
            {
                try
                {
                    bool visible = parentVisible && resolver.Resolve(element.Visible, element.Path);
                    RectF? local;
                    if (element is GroupElement group)
                    {
                        float gx = ox + resolver.Resolve(group.Position.X, group.Path);
                        float gy = oy + resolver.Resolve(group.Position.Y, group.Path);
                        int index = boxes.Count;
                        boxes.Add(null!); // placeholder: the group's bounds depend on its children
                        var children = Visit(group.Children, gx, gy, visible, depth + 1);
                        var bounds = children ?? new RectF(gx, gy, 0, 0);
                        boxes[index] = new ElementBox(element, bounds, ox, oy, visible, depth);
                        local = bounds;
                    }
                    else
                    {
                        var bounds = LocalBounds(element, resolver).Offset(ox, oy);
                        boxes.Add(new ElementBox(element, bounds, ox, oy, visible, depth));
                        local = bounds;
                    }
                    union = union is null ? local : union.Value.Union(local.Value);
                }
                catch (TemplateException)
                {
                    // Not resolvable with this data: it cannot be drawn, so it cannot be picked either.
                }
            }
            return union;
        }
    }

    /// <summary>
    /// The topmost visible, non-group element under the point, or null.
    /// </summary>
    public static ElementBox? HitTest(IReadOnlyList<ElementBox> boxes, float x, float y)
    {
        for (int i = boxes.Count - 1; i >= 0; i--)
        {
            var box = boxes[i];
            if (box.Visible && box.Element is not GroupElement && box.Bounds.Contains(x, y))
                return box;
        }
        return null;
    }

    private static RectF LocalBounds(Element element, BindingResolver r)
    {
        switch (element)
        {
            case LineElement line:
                float x1 = r.Resolve(line.From.X, line.Path), y1 = r.Resolve(line.From.Y, line.Path);
                float x2 = r.Resolve(line.To.X, line.Path), y2 = r.Resolve(line.To.Y, line.Path);
                float half = Math.Max(1, line.Width) / 2;
                float left = Math.Min(x1, x2), top = Math.Min(y1, y2);
                return new RectF(left - half + 0.5f, top - half + 0.5f, Math.Abs(x2 - x1) + 2 * half, Math.Abs(y2 - y1) + 2 * half);
            case ImageElement img:
                return Rect(img.Rect, img.Path, r);
            case ShapeElement shape:
                return Rect(shape.Rect, shape.Path, r);
            case TextElement text:
                return Rect(text.Rect, text.Path, r);
            default:
                return default;
        }
    }

    private static RectF Rect(RectValue rect, string path, BindingResolver r) => new(
        r.Resolve(rect.X, path), r.Resolve(rect.Y, path), r.Resolve(rect.Width, path), r.Resolve(rect.Height, path));
}
