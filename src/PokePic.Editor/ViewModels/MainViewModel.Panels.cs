using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using PokePic.Core;
using PokePic.Editor.Localization;
using PokePic.Editor.ViewModels.Docking;

namespace PokePic.Editor.ViewModels;

/// <summary>A range of the JSON text to select and reveal.</summary>
public readonly record struct JsonRange(int Start, int Length, bool Focus);

public sealed partial class MainViewModel
{
    private bool _syncingVisibility;

    /// <summary>
    /// The JSON text. It lives here, not in the view, so its undo history survives when the JSON panel
    /// is hidden, moved or floated.
    /// </summary>
    public TextDocument JsonDocument { get; } = new();

    /// <summary>Caret of the JSON editor, kept by the view: data keys are inserted there.</summary>
    public int JsonCaretOffset { get; set; }

    /// <summary>Selection the JSON view should show; raised again by <see cref="JsonRangeRequested"/>.</summary>
    public JsonRange? PendingJsonRange { get; private set; }

    public event Action<JsonRange>? JsonRangeRequested;

    public EditorDockFactory DockFactory { get; }

    [ObservableProperty]
    public partial IRootDock? DockLayout { get; set; }

    [ObservableProperty]
    public partial bool ShowLayers { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowJson { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowProperties { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowProblems { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowTextValues { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowNumberValues { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowBoolValues { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowColorValues { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowImageValues { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowFontValues { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowStyles { get; set; } = true;

    partial void OnShowLayersChanged(bool value) => SetToolVisible(ToolIds.Layers, value);

    partial void OnShowJsonChanged(bool value) => SetToolVisible(ToolIds.Json, value);

    partial void OnShowPropertiesChanged(bool value) => SetToolVisible(ToolIds.Properties, value);

    partial void OnShowProblemsChanged(bool value) => SetToolVisible(ToolIds.Problems, value);

    partial void OnShowTextValuesChanged(bool value) => SetToolVisible(ToolIds.TextValues, value);

    partial void OnShowNumberValuesChanged(bool value) => SetToolVisible(ToolIds.NumberValues, value);

    partial void OnShowBoolValuesChanged(bool value) => SetToolVisible(ToolIds.BoolValues, value);

    partial void OnShowColorValuesChanged(bool value) => SetToolVisible(ToolIds.ColorValues, value);

    partial void OnShowImageValuesChanged(bool value) => SetToolVisible(ToolIds.ImageValues, value);

    partial void OnShowFontValuesChanged(bool value) => SetToolVisible(ToolIds.FontValues, value);

    partial void OnShowStylesChanged(bool value) => SetToolVisible(ToolIds.Styles, value);

    /// <summary>
    /// Restores the layout saved at the last exit, or the default one.
    /// </summary>
    public void InitializeLayout()
    {
        var layout = LayoutStore.Load(DockFactory, LayoutStore.DefaultPath, out var error);
        if (error is not null)
            Status = Strings.Format("LayoutNotRestored", error);
        DockLayout = layout ?? DockFactory.CreateDefaultLayout();
    }

    /// <summary>Called by the view once the dock control has initialized the layout.</summary>
    public void OnLayoutReady() => SyncToolVisibility();

    public void SaveLayout()
    {
        try
        {
            LayoutStore.Save(DockFactory, LayoutStore.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = Strings.Format("LayoutNotSaved", ex.Message);
        }
    }

    [RelayCommand]
    private void ResetLayout()
    {
        DockLayout = DockFactory.CreateDefaultLayout();
        Avalonia.Threading.Dispatcher.UIThread.Post(SyncToolVisibility, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void SetToolVisible(string id, bool visible)
    {
        if (!_syncingVisibility)
            DockFactory.SetToolVisible(id, visible);
    }

    /// <summary>Called by the dock factory when a panel is closed, hidden or restored from the dock UI.</summary>
    private void OnToolVisibilityChanged(string id, bool visible)
    {
        _syncingVisibility = true;
        try
        {
            switch (id)
            {
                case ToolIds.Layers: ShowLayers = visible; break;
                case ToolIds.Json: ShowJson = visible; break;
                case ToolIds.Properties: ShowProperties = visible; break;
                case ToolIds.Problems: ShowProblems = visible; break;
                case ToolIds.TextValues: ShowTextValues = visible; break;
                case ToolIds.NumberValues: ShowNumberValues = visible; break;
                case ToolIds.BoolValues: ShowBoolValues = visible; break;
                case ToolIds.ColorValues: ShowColorValues = visible; break;
                case ToolIds.ImageValues: ShowImageValues = visible; break;
                case ToolIds.FontValues: ShowFontValues = visible; break;
                case ToolIds.Styles: ShowStyles = visible; break;
            }
        }
        finally
        {
            _syncingVisibility = false;
        }
    }

    private void SyncToolVisibility()
    {
        foreach (var id in DockFactory.Tools.Keys)
            OnToolVisibilityChanged(id, DockFactory.IsToolVisible(id));
    }

    // ----- JSON text -----

    /// <summary>
    /// Applies a visual edit as the smallest text replacement, so the JSON keeps its scroll position
    /// and the change is a single undo step.
    /// </summary>
    private void PatchJson(string text)
    {
        var old = JsonDocument.Text;
        int start = 0;
        while (start < old.Length && start < text.Length && old[start] == text[start])
            start++;
        int endOld = old.Length, endNew = text.Length;
        while (endOld > start && endNew > start && old[endOld - 1] == text[endNew - 1])
        {
            endOld--;
            endNew--;
        }
        JsonDocument.Replace(start, endOld - start, text[start..endNew]);
    }

    private void InsertIntoJson(string text)
    {
        ShowJson = true;
        int offset = Math.Clamp(JsonCaretOffset, 0, JsonDocument.TextLength);
        JsonDocument.Insert(offset, text);
        RequestJsonRange(new JsonRange(offset + text.Length, 0, Focus: true));
    }

    private void RevealJson(int line, int column)
    {
        ShowJson = true;
        if (line < 1 || line > JsonDocument.LineCount)
            return;
        var docLine = JsonDocument.GetLineByNumber(line);
        RequestJsonRange(new JsonRange(docLine.Offset + Math.Clamp(column - 1, 0, docLine.Length), 0, Focus: true));
    }

    /// <summary>Selects the JSON of the selected layer, without taking the focus.</summary>
    private void HighlightInJson(string? path)
    {
        if (path is null || TemplateLoader.FindSpan(_templateText, path) is not { } span)
            return;
        RequestJsonRange(new JsonRange(span.Start, span.Length, Focus: false));
    }

    private void RequestJsonRange(JsonRange range)
    {
        PendingJsonRange = range;
        JsonRangeRequested?.Invoke(range);
    }
}
