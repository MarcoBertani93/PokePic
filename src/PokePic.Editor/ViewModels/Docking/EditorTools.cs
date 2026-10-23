using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using PokePic.Editor.Localization;

namespace PokePic.Editor.ViewModels.Docking;

/// <summary>Where a panel goes when it is shown and its previous place no longer exists.</summary>
public enum PanelAnchor
{
    Left,
    Right,
    Bottom,
}

/// <summary>
/// A dockable panel of the editor. Views are bound to <see cref="Main"/>.
/// </summary>
public abstract class EditorTool : Tool
{
    protected EditorTool(MainViewModel main, string id, string titleKey, PanelAnchor anchor)
    {
        Main = main;
        Id = id;
        Title = Strings.Get(titleKey);
        DefaultAnchor = anchor;
        CanClose = true;
        CanFloat = true;
        CanPin = true;
    }

    public MainViewModel Main { get; }

    public PanelAnchor DefaultAnchor { get; }
}

public sealed class LayersTool(MainViewModel main) : EditorTool(main, ToolIds.Layers, "PanelLayers", PanelAnchor.Left);

public sealed class JsonTool(MainViewModel main) : EditorTool(main, ToolIds.Json, "PanelJson", PanelAnchor.Left);

public sealed class PropertiesTool(MainViewModel main) : EditorTool(main, ToolIds.Properties, "PanelProperties", PanelAnchor.Right);

public sealed class ProblemsTool(MainViewModel main) : EditorTool(main, ToolIds.Problems, "TabIssues", PanelAnchor.Bottom);

/// <summary>The values of one kind: the data pack's and the template's (see <see cref="Values.KindTab"/>).</summary>
public sealed class ValuesTool(MainViewModel main, string id, string titleKey) : EditorTool(main, id, titleKey, PanelAnchor.Bottom)
{
    public Values.KindTab Tab => Main.ValueTabs.Tab(Id);
}

public sealed class StylesTool(MainViewModel main) : EditorTool(main, ToolIds.Styles, "S_styles", PanelAnchor.Bottom);

/// <summary>
/// The card preview: the only document, always present.
/// </summary>
public sealed class CanvasDocument : Document
{
    public CanvasDocument(MainViewModel main)
    {
        Main = main;
        Id = ToolIds.Canvas;
        Title = Strings.Get("PanelCanvas");
        CanClose = false;
        CanFloat = false;
        CanPin = false;
    }

    public MainViewModel Main { get; }
}

public static class ToolIds
{
    public const string Layers = "Layers";
    public const string Json = "Json";
    public const string Properties = "Properties";
    public const string Problems = "Problems";
    public const string TextValues = "TextValues";
    public const string NumberValues = "NumberValues";
    public const string BoolValues = "BoolValues";
    public const string ColorValues = "ColorValues";
    public const string ImageValues = "ImageValues";
    public const string FontValues = "FontValues";
    public const string Styles = "Styles";

    /// <summary>The value panels, with their titles, in the order of the tabs.</summary>
    public static IReadOnlyList<(string Id, string TitleKey)> Values { get; } =
    [
        (TextValues, "S_texts"), (NumberValues, "S_numbers"), (BoolValues, "S_bools"),
        (ColorValues, "S_colors"), (ImageValues, "S_images"), (FontValues, "S_fonts"),
    ];
    public const string Canvas = "Canvas";

    public const string LeftDock = "LeftDock";
    public const string RightDock = "RightDock";
    public const string BottomDock = "BottomDock";
    public const string Documents = "Documents";
    public const string MainDock = "MainDock";
    public const string CenterDock = "CenterDock";

    public static string DockFor(PanelAnchor anchor) => anchor switch
    {
        PanelAnchor.Left => LeftDock,
        PanelAnchor.Right => RightDock,
        _ => BottomDock,
    };

    public static Alignment AlignmentFor(PanelAnchor anchor) => anchor switch
    {
        PanelAnchor.Left => Alignment.Left,
        PanelAnchor.Right => Alignment.Right,
        _ => Alignment.Bottom,
    };
}
