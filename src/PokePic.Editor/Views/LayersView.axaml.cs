using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PokePic.Editor.ViewModels;
using PokePic.Editor.ViewModels.Docking;

namespace PokePic.Editor.Views;

public partial class LayersView : UserControl
{
    private LayerNode? _dragSource;
    private Point _dragStart;
    private bool _dragging;
    private LayerNode? _dropTarget;

    public LayersView()
    {
        InitializeComponent();

        LayersTree.SelectionChanged += OnTreeSelectionChanged;
        LayersTree.ContextMenu = LayerCommands.CreateMenu(LayersTree, () => Main);
        AttachedToVisualTree += (_, _) => SyncFromModel();
        LayersTree.DoubleTapped += OnDoubleTapped;
        LayersTree.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        LayersTree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        LayersTree.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        LayersTree.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        LayersTree.AddHandler(LostFocusEvent, OnRenameLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private MainViewModel? Main => (DataContext as LayersTool)?.Main;

    // ----- Selection (Ctrl+click adds or removes a layer) -----

    private MainViewModel? _subscribed;
    private bool _syncing;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
            _subscribed.SelectionChanged -= SyncFromModel;
        _subscribed = Main;
        if (_subscribed is not null)
            _subscribed.SelectionChanged += SyncFromModel;
        SyncFromModel();
    }

    private void SyncFromModel()
    {
        if (Main is not { } main)
            return;
        var wanted = main.SelectedLayers;
        var items = LayersTree.SelectedItems;
        if (items.Count == wanted.Count && wanted.All(items.Contains))
            return;
        _syncing = true;
        try
        {
            items.Clear();
            foreach (var node in wanted)
                items.Add(node);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// Only selections made by the user reach the view model: when the dock detaches this view
    /// (tab switch, float, hide) the tree clears its selection, and that must not deselect the layers.
    /// </summary>
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Main is not { } main || !this.IsAttachedToVisualTree())
            return;
        if (e.AddedItems.Count == 0 && !LayersTree.IsKeyboardFocusWithin)
            return;

        // Keep the existing order, so the layer just added becomes the primary one.
        var selected = LayersTree.SelectedItems.OfType<LayerNode>().Select(n => n.Path).ToList();
        main.SetSelection(main.SelectedPaths.Where(selected.Contains).Concat(selected.Where(p => !main.SelectedPaths.Contains(p))));
    }

    // ----- Keyboard: rename, delete -----

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox { DataContext: LayerNode node } box && box.Classes.Contains("rename"))
        {
            if (e.Key == Key.Enter)
            {
                node.CommitRename(box.Text ?? "");
                LayersTree.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                node.IsEditing = false;
                LayersTree.Focus();
                e.Handled = true;
            }
            return;
        }

        if (e.Source is TextBox || Main is null)
            return;
        if (LayerCommands.HandleKey(e, LayersTree, Main))
        {
            e.Handled = true;
            return;
        }
        if (Main.SelectedLayer is not { } selected)
            return;
        switch (e.Key)
        {
            case Key.F2 when !Main.IsMultiSelection:
                BeginRename(selected);
                e.Handled = true;
                break;
            case Key.Delete:
                Main.DeleteLayerCommand.Execute(null);
                LayersTree.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RowOf(e.Source) is { DataContext: LayerNode node })
            BeginRename(node);
    }

    private void BeginRename(LayerNode node)
    {
        node.IsEditing = true;
        Dispatcher.UIThread.Post(() =>
        {
            var box = LayersTree.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Classes.Contains("rename") && t.DataContext == node);
            if (box is null)
                return;
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: LayerNode { IsEditing: true } node } box && box.Classes.Contains("rename"))
            node.CommitRename(box.Text ?? "");
    }

    // ----- Drag & drop -----

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = false;
        _dragSource = null;
        if (e.Source is TextBox || e.Source is Visual v && v.FindAncestorOfType<CheckBox>(includeSelf: true) is not null)
            return;
        if (e.GetCurrentPoint(LayersTree).Properties.IsLeftButtonPressed && RowOf(e.Source) is { DataContext: LayerNode node })
        {
            _dragSource = node;
            _dragStart = e.GetPosition(LayersTree);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSource is null || !e.GetCurrentPoint(LayersTree).Properties.IsLeftButtonPressed)
            return;
        var position = e.GetPosition(LayersTree);
        if (!_dragging)
        {
            if (Math.Abs(position.X - _dragStart.X) + Math.Abs(position.Y - _dragStart.Y) < 6)
                return;
            _dragging = true;
        }

        var (target, hint) = DropAt(position);
        if (_dropTarget is not null && _dropTarget != target)
            _dropTarget.DropHint = DropPosition.None;
        _dropTarget = target;
        if (target is not null)
            target.DropHint = target == _dragSource ? DropPosition.None : hint;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var source = _dragSource;
        var target = _dropTarget;
        var hint = target?.DropHint ?? DropPosition.None;
        if (target is not null)
            target.DropHint = DropPosition.None;
        _dragSource = null;
        _dropTarget = null;

        if (_dragging && source is not null && target is not null && hint != DropPosition.None)
            Main?.DropLayer(source, target, hint);
        _dragging = false;
    }

    /// <summary>
    /// Layer row under the point and where to drop: top quarter above, bottom quarter below, middle into a group.
    /// </summary>
    private (LayerNode? Target, DropPosition Hint) DropAt(Point position)
    {
        var row = LayersTree.GetVisualsAt(position)
            .Select(v => RowOf(v))
            .FirstOrDefault(r => r is not null);
        if (row is not { DataContext: LayerNode node } || row.TranslatePoint(new Point(0, 0), LayersTree) is not { } origin)
            return (null, DropPosition.None);

        double fraction = (position.Y - origin.Y) / Math.Max(1, row.Bounds.Height);
        var hint = node.IsGroup
            ? fraction < 0.25 ? DropPosition.Above : fraction > 0.75 ? DropPosition.Below : DropPosition.Into
            : fraction < 0.5 ? DropPosition.Above : DropPosition.Below;
        return (node, hint);
    }

    private static Control? RowOf(object? source) =>
        source is Visual v ? v.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => c.Classes.Contains("layerRow")) : null;
}
