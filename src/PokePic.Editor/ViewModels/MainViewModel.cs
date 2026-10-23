using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Editing;
using PokePic.Core.Model;
using PokePic.Core.Rendering;
using PokePic.Editor.Localization;
using PokePic.Editor.Services;
using PokePic.Editor.ViewModels.Properties;
using PokePic.Editor.ViewModels.Resources;
using PokePic.Editor.ViewModels.Values;

namespace PokePic.Editor.ViewModels;

public sealed record IssueItem(string Location, string Message, int? Line, int? Column);


public sealed record SampleItem(string Label, string Path);

/// <summary>A choice of the preview language: a code, or null for the data pack's language.</summary>
public sealed record LanguageChoice(string? Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The JSON text is the single source of truth. Visual edits change a <see cref="TemplateDocument"/>,
/// which is written back to the text as one undoable change.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IPropertyHost
{
    private const int RenderDelayMilliseconds = 300;

    private readonly IEditorDialogs _dialogs;
    private readonly PreviewRenderer _previewRenderer = new();

    /// <summary>Web images and fonts, downloaded once per session.</summary>
    private readonly WebAssetCache _web = new();
    private CancellationTokenSource? _renderCancellation;
    private string _templateText = "";
    private TemplateDocument? _document;
    private CardData _data = CardData.Empty;
    private bool _suppressRender;
    private string? _dataPath;
    private readonly FolderWatcher _assetWatcher;
    private readonly FolderWatcher _dataWatcher;

    public MainViewModel(IEditorDialogs dialogs)
    {
        _dialogs = dialogs;
        Resources = new ResourcesPanel(this);
        ValueTabs = new ValueTabs(this);
        SampleTemplates = [.. FindSamples("templates", "template.json")];
        SampleData = [.. FindSamples("data", "*.json")];

        // Assets edited in another program, or a data pack rewritten by the PKHeX plugin, refresh the preview.
        _assetWatcher = new FolderWatcher(_ => ScheduleRender(0));
        _dataWatcher = new FolderWatcher(_ =>
        {
            if (_dataPath is not null)
                LoadDataPack(_dataPath, showErrors: false);
        });

        JsonDocument.TextChanged += (_, _) => OnTemplateTextChanged(JsonDocument.Text);
        DockFactory = new Docking.EditorDockFactory(this, OnToolVisibilityChanged);
    }

    public IReadOnlyList<SampleItem> SampleTemplates { get; }

    public IReadOnlyList<SampleItem> SampleData { get; }

    public ObservableCollection<IssueItem> Issues { get; } = [];

    public CardData Data => _data;

    /// <summary>The JSON being edited, or null when it is not valid JSON.</summary>
    internal TemplateDocument? Document => _document;

    /// <summary>The template's styles (the Styles panel).</summary>
    public ResourcesPanel Resources { get; }

    /// <summary>The values of the data pack and of the template, a panel per kind.</summary>
    public ValueTabs ValueTabs { get; }

    /// <summary>Called whenever the template, the data or the preview language change.</summary>
    private void RefreshResources()
    {
        var template = _document is null ? null : Core.Json.TemplateParser.Parse(_templateText).Template;
        Resources.Load(_document);
        ValueTabs.Load(_document, template, _data, TextLanguage, CurrentAssets());
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(TemplateName))]
    public partial string? TemplatePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial Bitmap? Preview { get; set; }

    [ObservableProperty]
    public partial bool PreviewOutdated { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial string CursorPosition { get; set; } = "";

    [ObservableProperty]
    public partial string DataDescription { get; set; } = Strings.Get("NoData");

    /// <summary>Set when the JSON text does not parse: visual editing is disabled until it is fixed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditVisually))]
    public partial string? DocumentError { get; set; }

    public bool CanEditVisually => DocumentError is null;


    /// <summary>
    /// The language the card is shown in (elements without their own): the preview language when one is chosen,
    /// else the template's, else the data pack's, else English. Set by <see cref="UpdateTextLanguage"/>.
    /// </summary>
    [ObservableProperty]
    public partial string TextLanguage { get; private set; } = Languages.Fallback;

    /// <summary>Languages offered for "force language" and for the preview: common ones and those in the data.</summary>
    public ObservableCollection<string> TextLanguages { get; } = [Languages.Fallback];

    private static readonly string[] CommonLanguages = ["en", "fr", "de", "it", "es", "ja", "ko", "zh-Hans", "zh-Hant"];

    /// <summary>The data pack's language (it cannot be changed here: it belongs to the pack).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataLanguageText))]
    public partial string? DataLanguage { get; set; }

    public string DataLanguageText => DataLanguage ?? Strings.Format("LanguageNotSet", Languages.Fallback);

    /// <summary>Preview language: the pack's, or another one to try the graphics in that language.</summary>
    public ObservableCollection<LanguageChoice> PreviewLanguages { get; } = [];

    [ObservableProperty]
    public partial LanguageChoice? PreviewLanguage { get; set; }

    partial void OnPreviewLanguageChanged(LanguageChoice? value) => UpdateTextLanguage();

    /// <summary>The template's own language ("language" in the JSON), if any.</summary>
    private string? TemplateLanguage =>
        _document?.Root["language"] is System.Text.Json.Nodes.JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? v.GetValue<string>()
            : null;

    /// <summary>From the weakest: English, the data pack's language, the template's, the preview language (not "auto").</summary>
    private void UpdateTextLanguage() =>
        TextLanguage = PreviewLanguage?.Code ?? TemplateLanguage ?? DataLanguage ?? Languages.Fallback;

    /// <summary>What the card is rendered with: the pack's language as the default, the preview language as override.</summary>
    private string RenderLanguage => DataLanguage ?? Languages.Fallback;

    partial void OnTextLanguageChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            TextLanguage = Languages.Fallback;
            return;
        }
        RefreshResources(); // values are shown in the preview language
        ScheduleRender(0);
    }

    /// <summary>Rendering mode: the template's "canvas.mode" (it belongs to the template, like the export scale).</summary>
    public RenderMode Mode =>
        (_document?.Root["canvas"] as System.Text.Json.Nodes.JsonObject)?["mode"] is System.Text.Json.Nodes.JsonValue mode
        && mode.GetValueKind() == System.Text.Json.JsonValueKind.String && string.Equals(mode.GetValue<string>(), "pixelPerfect", StringComparison.OrdinalIgnoreCase)
            ? RenderMode.PixelPerfect
            : RenderMode.HighResolution;

    /// <summary>The template's "name", else its file name.</summary>
    public string TemplateName =>
        _document?.Root["name"] is System.Text.Json.Nodes.JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String && v.GetValue<string>().Trim() is { Length: > 0 } name
            ? name
            : TemplatePath is null ? Strings.Get("Untitled") : System.IO.Path.GetFileName(TemplatePath);

    public string Title => $"{TemplateName}{(IsDirty ? " *" : "")} — {Strings.Get("AppTitle")}";

    public bool HasIssues => Issues.Count > 0;

    /// <summary>
    /// Opens the template and data pack given on the command line, or the first samples.
    /// </summary>
    public void Initialize(string? templatePath, string? dataPath)
    {
        dataPath ??= SampleData.FirstOrDefault()?.Path;
        if (dataPath is not null)
            LoadDataPack(dataPath, showErrors: false);

        templatePath ??= SampleTemplates.FirstOrDefault()?.Path;
        if (templatePath is not null)
            LoadTemplate(templatePath);
        else
            ReplaceTemplate(NewTemplateText, null);
    }

    /// <summary>
    /// Called by the view on every edit of the JSON text.
    /// </summary>
    public void OnTemplateTextChanged(string text)
    {
        if (text == _templateText)
            return;
        _templateText = text;
        if (_suppressRender)
            return;
        IsDirty = true;
        ReparseDocument();
        ScheduleRender(RenderDelayMilliseconds);
    }


    partial void OnTemplatePathChanged(string? value) =>
        _assetWatcher.Watch(value is null ? null : Path.GetDirectoryName(value), value);

    [RelayCommand]
    private async Task NewAsync()
    {
        if (IsDirty && !await _dialogs.ConfirmDiscardAsync())
            return;
        ReplaceTemplate(NewTemplateText, null);
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (IsDirty && !await _dialogs.ConfirmDiscardAsync())
            return;
        var path = await _dialogs.OpenFileAsync(Strings.Get("Open"), TemplateFilter);
        if (path is not null)
            LoadTemplate(path);
    }

    [RelayCommand]
    private async Task OpenSampleTemplateAsync(SampleItem sample)
    {
        if (IsDirty && !await _dialogs.ConfirmDiscardAsync())
            return;
        LoadTemplate(sample.Path);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        FlushPendingEdits();
        if (TemplatePath is null)
        {
            await SaveAsAsync();
            return;
        }
        await WriteTemplateAsync(TemplatePath);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        FlushPendingEdits();
        var path = await _dialogs.SaveFileAsync(Strings.Get("SaveAs"), TemplateFilter, TemplatePath is null ? "template.json" : System.IO.Path.GetFileName(TemplatePath));
        if (path is null)
            return;
        await WriteTemplateAsync(path);
        TemplatePath = path;
        ScheduleRender(0); // assets are now resolved relative to the new folder
    }

    [RelayCommand]
    private async Task OpenDataAsync()
    {
        var path = await _dialogs.OpenFileAsync(Strings.Get("OpenData"), DataPackFilter);
        if (path is not null)
            LoadDataPack(path, showErrors: true);
    }

    [RelayCommand]
    private void OpenSampleData(SampleItem sample) => LoadDataPack(sample.Path, showErrors: true);

    /// <summary>Name of the Pokémon of the data pack, for file names (see <see cref="DataPack.EffectiveName"/>).</summary>
    private string? _dataName;

    /// <summary>"{template name}_{data name}.png", e.g. "Spotlight_Drago.png".</summary>
    public string ExportFileName()
    {
        string? Text(System.Text.Json.Nodes.JsonNode? node) => node is System.Text.Json.Nodes.JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String ? v.GetValue<string>() : null;
        var template = Text(_document?.Root["name"]) ?? Text(_document?.Root["id"])
            ?? (TemplatePath is null ? "card" : System.IO.Path.GetFileNameWithoutExtension(TemplatePath));
        return CardFileName.For(template, _dataName);
    }

    [RelayCommand]
    private async Task ExportPngAsync()
    {
        FlushPendingEdits();
        var suggested = ExportFileName();
        var path = await _dialogs.SaveFileAsync(Strings.Get("ExportPng"), PngFilter, suggested);
        if (path is null)
            return;

        // Mode and scale of the export are the template's (canvas.mode, canvas.scale).
        if (Core.Json.TemplateParser.Parse(_templateText).Template is not { } template)
        {
            await _dialogs.ShowErrorAsync(Strings.Get("ExportNeedsValidTemplate"));
            return;
        }
        // The export is what the preview shows, preview language included.
        var options = RenderOptions.FromTemplate(template, RenderLanguage) with { LanguageOverride = PreviewLanguage?.Code };
        var outcome = await Task.Run(() => _previewRenderer.Render(_templateText, _data, CurrentAssets(), options));
        if (outcome.Card is null)
        {
            await _dialogs.ShowErrorAsync(string.Join(Environment.NewLine, outcome.Issues));
            return;
        }

        try
        {
            await File.WriteAllBytesAsync(path, outcome.Card.Png);
            Status = Strings.Format("StatusExported", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(ex.Message);
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        _web.ForgetFailures(); // F5 retries the downloads that failed
        ValueTabs.Forget();
        RefreshResources();
        ScheduleRender(0);
    }

    [RelayCommand]
    private void Undo()
    {
        FlushPendingEdits();
        if (JsonDocument.UndoStack.CanUndo)
            JsonDocument.UndoStack.Undo();
    }

    [RelayCommand]
    private void Redo()
    {
        FlushPendingEdits();
        if (JsonDocument.UndoStack.CanRedo)
            JsonDocument.UndoStack.Redo();
    }

    /// <summary>A reference ("{level}", "{frame}") where the JSON caret is.</summary>
    internal void InsertBindingText(string text) => InsertIntoJson(text);

    [RelayCommand]
    private void GoToIssue(IssueItem issue)
    {
        if (issue.Line is { } line)
        {
            RevealJson(line, issue.Column ?? 1);
        }
    }

    // ----- Document -----

    /// <summary>
    /// Applies a visual edit. <paramref name="edit"/> returns the path to select afterwards,
    /// null to keep the current selection, or "" to clear it.
    /// </summary>
    internal void ApplyEdit(Func<TemplateDocument, string?> edit, bool rebuildProperties) =>
        ApplyEditMany(doc => edit(doc) switch
        {
            null => null,
            "" => [],
            var path => [path],
        }, rebuildProperties);

    /// <summary>
    /// Like <see cref="ApplyEdit"/> for operations on several elements: returns the paths to select, or null to keep the selection.
    /// </summary>
    internal void ApplyEditMany(Func<TemplateDocument, IReadOnlyList<string>?> edit, bool rebuildProperties)
    {
        FlushPendingEdits();
        if (_document is null)
        {
            Status = Strings.Get("VisualDisabled");
            return;
        }
        var doc = _document.Clone();
        var select = edit(doc);
        CommitDocument(doc, select ?? _selection, rebuildProperties);
    }

    private bool _flushing;

    /// <summary>
    /// Applies property fields typed but not yet confirmed. Called before every action and when focus moves.
    /// </summary>
    public void FlushPendingEdits()
    {
        if (_flushing)
            return;
        _flushing = true;
        try
        {
            Properties?.CommitPending();
            Resources.CommitPending();
            ValueTabs.CommitPending();
        }
        finally
        {
            _flushing = false;
        }
    }

    private void CommitDocument(TemplateDocument doc, IReadOnlyList<string> select, bool rebuildProperties)
    {
        var text = doc.ToJson();
        if (text == _templateText)
            return;

        _document = doc;
        _templateText = text;
        PatchJson(text);
        IsDirty = true;

        RebuildLayers(select);
        UpdateTextLanguage();
        OnPropertyChanged(nameof(TemplateName));
        OnPropertyChanged(nameof(Title));
        RefreshResources();
        if (rebuildProperties || Properties?.Key != CurrentPropertiesKey(doc))
            RebuildProperties();
        else
            Properties?.Load(doc);
        ScheduleRender(0);
    }

    private void ReparseDocument()
    {
        var doc = TemplateDocument.TryParse(_templateText, out var error);
        if (doc is null)
        {
            _document = null;
            DocumentError = Strings.Format("JsonInvalid", error ?? "");
            RefreshResources();
            return;
        }
        _document = doc;
        DocumentError = null;
        RebuildLayers(_selection);
        RebuildProperties();
        UpdateTextLanguage(); // the template's language and name may have changed
        OnPropertyChanged(nameof(TemplateName));
        OnPropertyChanged(nameof(Title));
        RefreshResources();
    }

    void IPropertyHost.Apply(Action<TemplateDocument> edit, bool rebuild) => ApplyEdit(doc =>
    {
        edit(doc);
        return null;
    }, rebuild);

    private void LoadTemplate(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = _dialogs.ShowErrorAsync(ex.Message);
            return;
        }

        IsFitToWindow = true; // the view fits the new card in the canvas area
        ReplaceTemplate(text, Path.GetFullPath(path));
    }

    private void ReplaceTemplate(string text, string? path)
    {
        _suppressRender = true;
        _templateText = text;
        JsonDocument.Text = text;
        JsonDocument.UndoStack.ClearAll();
        _suppressRender = false;

        TemplatePath = path;
        IsDirty = false;
        _selection = [];
        EditedStyle = null;
        ReparseDocument();
        ScheduleRender(0);
    }

    private async Task WriteTemplateAsync(string path)
    {
        try
        {
            await File.WriteAllTextAsync(path, _templateText);
            IsDirty = false;
            Status = Strings.Format("StatusSaved", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(ex.Message);
        }
    }

    // ----- Data -----

    private void LoadDataPack(string path, bool showErrors)
    {
        DataPackReadResult result;
        try
        {
            result = DataPackSerializer.ReadFile(path, _web);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportDataError(ex.Message);
            return;
        }

        if (!result.Success)
        {
            ReportDataError(Strings.Get("DataPackErrors") + Environment.NewLine + string.Join(Environment.NewLine, result.Issues));
            return;
        }

        var pack = result.Pack!;
        _data = pack.Data;
        if (_dataPath != path)
        {
            _dataPath = path;
            _dataWatcher.Watch(Path.GetDirectoryName(Path.GetFullPath(path)));
        }
        DataDescription = pack.Description ?? Path.GetFileNameWithoutExtension(path);
        if (pack.Source is not null)
            DataDescription += " · " + Strings.Format("Source", pack.Source);

        // The preview uses the pack's language, unless another one was chosen to try the graphics.
        _suppressRender = true;
        var overridden = PreviewLanguage?.Code;
        DataLanguage = pack.Language;
        _dataName = pack.EffectiveName;
        TextLanguages.Clear();
        foreach (var language in _data.Values.Values.OfType<LocalizedValue>()
                     .SelectMany(l => l.Values.Keys)
                     .Concat(CommonLanguages)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.Ordinal))
            TextLanguages.Add(language);
        PreviewLanguages.Clear();
        PreviewLanguages.Add(new LanguageChoice(null, Strings.Get("LanguageAuto")));
        foreach (var language in TextLanguages)
            PreviewLanguages.Add(new LanguageChoice(language, language));
        PreviewLanguage = PreviewLanguages.FirstOrDefault(c => c.Code is not null && c.Code == overridden) ?? PreviewLanguages[0];
        UpdateTextLanguage();
        _suppressRender = false;
        RefreshResources();

        // Suggestions in the properties panel depend on the data.
        RebuildProperties();
        ScheduleRender(0);

        void ReportDataError(string message)
        {
            if (showErrors)
                _ = _dialogs.ShowErrorAsync(message);
            else
                Status = message.ReplaceLineEndings(" ");
        }
    }


    // ----- Rendering -----

    private sealed record RenderRequest(string Text, CardData Data, IAssetProvider Assets, RenderOptions Options);

    private RenderRequest? _queuedRender;
    private bool _rendering;

    /// <summary>
    /// Renders the preview. A delay debounces typing (a newer edit cancels the wait). Renders never cancel
    /// each other: one runs at a time and, when it ends, the latest request starts, so while dragging the
    /// preview keeps up with the mouse instead of waiting for it to stop.
    /// </summary>
    private void ScheduleRender(int delayMilliseconds, string? textOverride = null)
    {
        if (_suppressRender)
            return;

        _renderCancellation?.Cancel();
        var request = new RenderRequest(textOverride ?? _templateText, _data, CurrentAssets(), new RenderOptions(Mode, (float)PreviewRenderScale(), RenderLanguage, PreviewLanguage?.Code));
        if (delayMilliseconds <= 0)
        {
            EnqueueRender(request);
            return;
        }

        var cancellation = _renderCancellation = new CancellationTokenSource();
        _ = DelayThenRenderAsync(request, delayMilliseconds, cancellation.Token);
    }

    private async Task DelayThenRenderAsync(RenderRequest request, int delay, CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(delay, cancellation);
            EnqueueRender(request);
        }
        catch (OperationCanceledException)
        {
            // A newer edit superseded this one before its delay ended.
        }
    }

    private void EnqueueRender(RenderRequest request)
    {
        _queuedRender = request; // only the latest request matters
        if (!_rendering)
            _ = RenderQueuedAsync();
    }

    private async Task RenderQueuedAsync()
    {
        _rendering = true;
        try
        {
            while (_queuedRender is { } request)
            {
                _queuedRender = null;
                var outcome = await Task.Run(() => _previewRenderer.Render(request.Text, request.Data, request.Assets, request.Options));
                Apply(outcome, request.Options.Scale);
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private void Apply(PreviewOutcome outcome, float scale)
    {
        Issues.Clear();
        foreach (var issue in outcome.Issues)
        {
            var location = issue.Line is null ? issue.Path : $"{Strings.Format("LineNumber", issue.Line)} · {issue.Path}";
            Issues.Add(new IssueItem(location, issue.Message, issue.Line, issue.Column));
        }
        OnPropertyChanged(nameof(HasIssues));

        if (_drag is null && outcome.Boxes.Count > 0)
        {
            _boxes = outcome.Boxes;
            UpdateOverlay();
        }

        if (outcome.Card is { } card)
        {
            var old = Preview;
            using (var stream = new MemoryStream(card.Png))
                Preview = new Bitmap(stream);
            old?.Dispose();
            RenderedScale = scale;
            CanvasPixelWidth = (int)Math.Round(card.Width / scale);
            CanvasPixelHeight = (int)Math.Round(card.Height / scale);
            PreviewOutdated = false;
            Status = Strings.Format("StatusRendered", card.Width, card.Height, outcome.ElapsedMilliseconds);
        }
        else
        {
            PreviewOutdated = Preview is not null;
            Status = Strings.Format("StatusProblems", outcome.Issues.Count);
        }
    }

    private IAssetProvider CurrentAssets() =>
        TemplatePath is null ? NoAssetProvider.Instance : new FileSystemAssetProvider(Path.GetDirectoryName(TemplatePath)!, _web);

    private static IEnumerable<SampleItem> FindSamples(string folder, string pattern)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Samples", folder);
        if (!Directory.Exists(root))
            return [];
        return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(p => new SampleItem(
                pattern == "template.json" ? Path.GetFileName(Path.GetDirectoryName(p))! : Path.GetFileNameWithoutExtension(p),
                p));
    }

    private static FileFilter TemplateFilter => new(Strings.Get("FilterTemplate"), "*.json");

    private static FileFilter DataPackFilter => new(Strings.Get("FilterDataPack"), "*.json");

    private static FileFilter PngFilter => new(Strings.Get("FilterPng"), "*.png");

    private const string NewTemplateText = """
        {
          "id": "new-template",
          "version": 1,
          "name": "New template",
          "canvas": { "width": 192, "height": 128, "mode": "pixelPerfect", "scale": 4 },
          "fonts": {},
          "images": {},
          "elements": [
            { "name": "Background", "type": "shape", "rect": [0, 0, 192, 128], "radius": 8, "fill": "#F0F0F0", "stroke": { "color": "#303030", "width": 2 } }
          ]
        }

        """;
}
