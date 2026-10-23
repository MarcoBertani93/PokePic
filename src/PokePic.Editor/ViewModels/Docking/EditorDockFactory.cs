using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace PokePic.Editor.ViewModels.Docking;

/// <summary>
/// Builds the editor layout and shows/hides panels. A hidden panel goes back where it was
/// (Dock remembers its original dock); if that dock no longer exists, to its default side.
/// </summary>
public sealed class EditorDockFactory : Factory
{
    private readonly Action<string, bool> _visibilityChanged;

    public EditorDockFactory(MainViewModel main, Action<string, bool> visibilityChanged)
    {
        _visibilityChanged = visibilityChanged;
        Canvas = new CanvasDocument(main);
        EditorTool[] tools =
        [
            new LayersTool(main),
            new JsonTool(main),
            new PropertiesTool(main),
            new ProblemsTool(main),
            .. ToolIds.Values.Select(v => new ValuesTool(main, v.Id, v.TitleKey)),
            new StylesTool(main),
        ];
        Tools = tools.ToDictionary(t => t.Id, StringComparer.Ordinal);

        HideToolsOnClose = true;
        DefaultHostWindowLocator = () => new HostWindow();
    }

    public CanvasDocument Canvas { get; }

    public IReadOnlyDictionary<string, EditorTool> Tools { get; }

    public IRootDock? Root { get; private set; }

    /// <summary>
    /// Left: layers and JSON as tabs; center: the card with problems, values and styles below; right: properties.
    /// </summary>
    public IRootDock CreateDefaultLayout()
    {
        var left = ToolDock(ToolIds.LeftDock, PanelAnchor.Left, 0.2, Tools[ToolIds.Layers], Tools[ToolIds.Json]);
        var right = ToolDock(ToolIds.RightDock, PanelAnchor.Right, 0.22, Tools[ToolIds.Properties]);
        var bottom = ToolDock(ToolIds.BottomDock, PanelAnchor.Bottom, 0.25, [Tools[ToolIds.Problems], .. ToolIds.Values.Select(v => Tools[v.Id]), Tools[ToolIds.Styles]]);
        var center = new ProportionalDock
        {
            Id = ToolIds.CenterDock,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(DocumentDock(), new ProportionalDockSplitter(), bottom),
        };
        var main = new ProportionalDock
        {
            Id = ToolIds.MainDock,
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(left, new ProportionalDockSplitter(), center, new ProportionalDockSplitter(), right),
        };
        return RootFor(main);
    }

    public IRootDock RootFor(IDock main)
    {
        var root = CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(main);
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        root.HiddenDockables = CreateList<IDockable>();
        Root = root;
        return root;
    }

    public ToolDock ToolDock(string id, PanelAnchor anchor, double proportion, params IDockable[] tools) => new()
    {
        Id = id,
        Alignment = ToolIds.AlignmentFor(anchor),
        Proportion = proportion,
        VisibleDockables = CreateList(tools),
        ActiveDockable = tools.FirstOrDefault(),
    };

    public DocumentDock DocumentDock() => new()
    {
        Id = ToolIds.Documents,
        CanCreateDocument = false,
        IsCollapsable = false,
        VisibleDockables = CreateList<IDockable>(Canvas),
        ActiveDockable = Canvas,
    };

    public bool IsToolVisible(string id) =>
        Root is not null && Tools.TryGetValue(id, out var tool) && tool.Owner is not null
        && !(Root.HiddenDockables?.Contains(tool) ?? false);

    public void SetToolVisible(string id, bool visible)
    {
        if (Root is null || !Tools.TryGetValue(id, out var tool) || IsToolVisible(id) == visible)
            return;

        if (!visible)
        {
            HideDockable(tool);
            return;
        }

        if (Root.HiddenDockables?.Contains(tool) == true)
            RestoreDockable(tool);

        // The original dock may have been collapsed meanwhile: fall back to the default side.
        if (tool.Owner is not IDock owner || !IsInLayout(owner))
        {
            Root.HiddenDockables?.Remove(tool);
            var dock = EnsureDock(tool.DefaultAnchor);
            AddDockable(dock, tool);
        }
        if (tool.Owner is IDock d)
        {
            d.ActiveDockable = tool;
            SetActiveDockable(tool);
        }
        _visibilityChanged(id, true);
    }

    public override void OnDockableHidden(IDockable? dockable)
    {
        base.OnDockableHidden(dockable);
        if (dockable is EditorTool tool)
            _visibilityChanged(tool.Id, false);
    }

    public override void OnDockableRestored(IDockable? dockable)
    {
        base.OnDockableRestored(dockable);
        if (dockable is EditorTool tool)
            _visibilityChanged(tool.Id, true);
    }

    public override void OnDockableClosed(IDockable? dockable)
    {
        base.OnDockableClosed(dockable);
        if (dockable is EditorTool tool)
            _visibilityChanged(tool.Id, IsToolVisible(tool.Id));
    }

    /// <summary>
    /// The tool dock for a side, recreated next to the card if the user removed it.
    /// </summary>
    private IDock EnsureDock(PanelAnchor anchor)
    {
        var id = ToolIds.DockFor(anchor);
        if (Find(id) is IDock existing && IsInLayout(existing))
            return existing;

        var dock = ToolDock(id, anchor, anchor == PanelAnchor.Bottom ? 0.25 : 0.2);
        var documents = Find(ToolIds.Documents) as IDock;

        if (anchor == PanelAnchor.Bottom && documents?.Owner is IDock docOwner)
        {
            if (docOwner is IProportionalDock { Orientation: Orientation.Vertical } vertical)
            {
                int index = vertical.VisibleDockables!.IndexOf(documents);
                InsertDockable(vertical, new ProportionalDockSplitter(), index + 1);
                InsertDockable(vertical, dock, index + 2);
            }
            else
            {
                // Wrap the document dock in a vertical split.
                int index = docOwner.VisibleDockables!.IndexOf(documents);
                RemoveDockable(documents, false);
                var split = new ProportionalDock
                {
                    Id = ToolIds.CenterDock,
                    Orientation = Orientation.Vertical,
                    Proportion = documents.Proportion,
                    VisibleDockables = CreateList<IDockable>(documents, new ProportionalDockSplitter(), dock),
                };
                documents.Proportion = double.NaN;
                InsertDockable(docOwner, split, index);
                InitDockable(split, docOwner);
            }
            return dock;
        }

        var main = MainHorizontalDock();
        if (anchor == PanelAnchor.Left)
        {
            InsertDockable(main, new ProportionalDockSplitter(), 0);
            InsertDockable(main, dock, 0);
        }
        else
        {
            AddDockable(main, new ProportionalDockSplitter());
            AddDockable(main, dock);
        }
        return dock;
    }

    private IDock MainHorizontalDock()
    {
        var top = Root!.VisibleDockables!.OfType<IDock>().First();
        if (top is IProportionalDock { Orientation: Orientation.Horizontal })
            return top;

        // The user's layout has another shape: put a horizontal split at the top.
        var main = new ProportionalDock
        {
            Id = ToolIds.MainDock,
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(),
        };
        Root.VisibleDockables!.Remove(top);
        AddDockable(Root, main);
        AddDockable(main, top);
        Root.ActiveDockable = main;
        Root.DefaultDockable = main;
        return main;
    }

    private IDockable? Find(string id) => Root is null ? null : FindDockable(Root, d => d.Id == id);

    private bool IsInLayout(IDockable dockable)
    {
        for (var d = dockable; d is not null; d = d.Owner)
        {
            if (d == Root)
                return true;
            if (d.Owner is IDock owner && !(owner.VisibleDockables?.Contains(d) ?? false))
                return false;
        }
        return false;
    }
}
