using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using PokePic.Editor.ViewModels;
using PokePic.Editor.ViewModels.Docking;

namespace PokePic.Editor.Views;

/// <summary>
/// The JSON text of the template. The document belongs to the view model, so this view can be
/// recreated by the dock (moved, floated, hidden) without losing text or undo history.
/// </summary>
public partial class JsonView : UserControl
{
    private MainViewModel? _main;

    public JsonView()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Json")
            ?? HighlightingManager.Instance.GetDefinition("JavaScript");
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 2;
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_main is not null)
                _main.JsonCaretOffset = Editor.CaretOffset;
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Attach((DataContext as JsonTool)?.Main);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Attach(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (TopLevel.GetTopLevel(this) is not null)
            Attach((DataContext as JsonTool)?.Main);
    }

    private void Attach(MainViewModel? main)
    {
        if (_main == main)
            return;
        if (_main is not null)
            _main.JsonRangeRequested -= ShowRange;
        _main = main;
        if (main is null)
            return;

        Editor.Document = main.JsonDocument;
        main.JsonRangeRequested += ShowRange;
        if (main.PendingJsonRange is { } pending)
            Dispatcher.UIThread.Post(() => ShowRange(pending), DispatcherPriority.Loaded);
    }

    private void ShowRange(JsonRange range)
    {
        int length = Editor.Document.TextLength;
        int start = Math.Clamp(range.Start, 0, length);
        int end = Math.Clamp(range.Start + range.Length, start, length);
        Editor.CaretOffset = start;
        Editor.Select(start, end - start);
        var location = Editor.Document.GetLocation(start);
        Editor.ScrollTo(location.Line, location.Column);
        if (range.Focus)
            Editor.Focus();
    }
}
