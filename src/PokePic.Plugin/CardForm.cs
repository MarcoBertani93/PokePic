using System.Diagnostics;
using PKHeX.Core;

namespace PokePic.Plugin;

/// <summary>
/// The templates of the templates folder with the preview of the chosen one for the Pokémon (or the first
/// Pokémon of the box); exports the card(s), opens the template in the editor, creates a new template.
/// When the window comes back to the front (e.g. after saving in the editor) a changed template is redrawn.
/// </summary>
internal sealed class CardForm : Form
{
    private readonly CardMaker _maker;
    private readonly TemplateCatalog _catalog;
    private readonly EditorLauncher _editor;
    private readonly IReadOnlyList<PKM> _pokemon;
    private readonly string _language;

    private readonly ListBox _templates = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PixelPreview _preview = new() { Dock = DockStyle.Fill };
    private readonly TextBox _problems = new() { Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, Height = 72, ScrollBars = ScrollBars.Vertical, ForeColor = Color.Firebrick, Visible = false };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _export = new() { AutoSize = true };
    private readonly Button _edit = new() { AutoSize = true };

    private CardResult? _card;
    private DateTime _stamp;
    private int _render;

    /// <param name="pokemon">One Pokémon, or the Pokémon of a box (the preview shows the first).</param>
    public CardForm(CardMaker maker, TemplateCatalog catalog, IReadOnlyList<PKM> pokemon, string title, string language)
    {
        _maker = maker;
        _catalog = catalog;
        _editor = new EditorLauncher(catalog);
        _pokemon = pokemon;
        _language = language;
        bool box = pokemon.Count > 1;

        Text = title;
        Icon = Icons.Window;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(860, 560);
        MinimumSize = new Size(600, 400);

        _export.Text = Text_(box ? "ExportBox" : "Export");
        _edit.Text = Text_("Edit");
        var create = new Button { Text = Text_("New"), AutoSize = true };
        var folder = new Button { Text = Text_("OpenFolder"), AutoSize = true };
        var refresh = new Button { Text = Text_("Refresh"), AutoSize = true };

        var left = new Panel { Dock = DockStyle.Left, Width = 240, Padding = new Padding(8) };
        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        leftButtons.Controls.AddRange([create, folder, refresh]);
        left.Controls.Add(_templates);
        left.Controls.Add(new Label { Text = Text_("Templates"), Dock = DockStyle.Top, Height = 20, Font = new Font(Font, FontStyle.Bold) });
        left.Controls.Add(leftButtons);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bottom.Controls.AddRange([_export, _edit]);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 8, 0) };
        right.Controls.Add(_preview);
        right.Controls.Add(_problems);
        right.Controls.Add(_status);

        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(bottom);

        _templates.SelectedIndexChanged += async (_, _) => await RenderSelectedAsync();
        _export.Click += async (_, _) => await ExportAsync();
        _edit.Click += (_, _) => EditSelected();
        create.Click += (_, _) => CreateTemplate();
        folder.Click += (_, _) => OpenFolder();
        refresh.Click += async (_, _) =>
        {
            _maker.Refresh();
            LoadTemplates();
            await RenderSelectedAsync();
        };
        Activated += async (_, _) =>
        {
            if (Selected is { } t && Stamp(t.Folder) != _stamp)
                await RenderSelectedAsync();
        };

        LoadTemplates();
    }

    private TemplateEntry? Selected => _templates.SelectedItem as TemplateEntry;

    private static string Text_(string key, params object[] args) => Plugin.Text.Get(key, args);

    private void LoadTemplates(string? select = null)
    {
        var current = select ?? Selected?.Folder;
        var list = _catalog.List();
        _templates.BeginUpdate();
        _templates.Items.Clear();
        foreach (var t in list)
            _templates.Items.Add(t);
        _templates.EndUpdate();

        if (list.Count == 0)
        {
            ShowCard(null, Text_("NoTemplates", _catalog.Folder));
            return;
        }
        var index = list.ToList().FindIndex(t => t.Folder == current);
        _templates.SelectedIndex = index >= 0 ? index : 0;
    }

    private async Task RenderSelectedAsync()
    {
        if (Selected is not { } template)
            return;
        int render = ++_render;
        _status.Text = Text_("Rendering");
        _export.Enabled = false;
        _stamp = Stamp(template.Folder);
        var pkm = _pokemon[0];
        var card = await Task.Run(() => _maker.Make(template.TemplatePath, pkm, _language));
        if (render != _render || IsDisposed)
            return; // another template was chosen meanwhile
        ShowCard(card, card.Problems.Count > 0 ? string.Join(Environment.NewLine, card.Problems) : null);
    }

    private void ShowCard(CardResult? card, string? problems)
    {
        _card = card;
        _preview.Image = card?.Png is { } png ? Image.FromStream(new MemoryStream(png)) : null;
        _problems.Text = problems ?? "";
        _problems.Visible = problems is not null;
        _status.Text = card?.Png is null ? "" : card.FileName;
        _export.Enabled = card?.Png is not null;
        _edit.Enabled = Selected is not null;
    }

    private async Task ExportAsync()
    {
        if (Selected is not { } template || _card?.Png is not { } png)
            return;

        if (_pokemon.Count == 1)
        {
            using var dialog = new SaveFileDialog { Filter = Text_("PngFilter"), FileName = _card.FileName, DefaultExt = "png" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            File.WriteAllBytes(dialog.FileName, png);
            MessageBox.Show(this, Text_("Saved", dialog.FileName), Text_("Caption"));
            return;
        }

        using var folderDialog = new FolderBrowserDialog { Description = Text_("ChooseFolder"), UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (folderDialog.ShowDialog(this) != DialogResult.OK)
            return;
        var folder = folderDialog.SelectedPath;

        _export.Enabled = false;
        _status.Text = Text_("Rendering");
        var (saved, skipped) = await Task.Run(() =>
        {
            int count = 0;
            var problems = new List<string>();
            foreach (var pkm in _pokemon)
            {
                var card = _maker.Make(template.TemplatePath, pkm, _language);
                if (card.Png is null)
                {
                    problems.Add($"{pkm.Nickname}: {card.Problems.FirstOrDefault()}");
                    continue;
                }
                File.WriteAllBytes(Unique(Path.Combine(folder, card.FileName)), card.Png);
                count++;
            }
            return (count, problems);
        });
        _export.Enabled = true;
        _status.Text = _card.FileName;

        var message = Text_("BoxSaved", saved, folder);
        if (skipped.Count > 0)
            message += Environment.NewLine + Environment.NewLine + Text_("BoxSkipped", skipped.Count, string.Join(Environment.NewLine, skipped));
        MessageBox.Show(this, message, Text_("Caption"));
    }

    /// <summary>Two Pokémon of a box with the same name do not overwrite each other: "Name (2).png".</summary>
    private static string Unique(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name} ({i}).png");
        return path;
    }

    private void EditSelected()
    {
        if (Selected is not { } template)
            return;
        try
        {
            _editor.Open(template.TemplatePath, _maker.DataPack(_pokemon[0], _language), Plugin.Text.Language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, Text_("Caption"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateTemplate()
    {
        var name = Prompt(Text_("NewTitle"), Text_("NewName"));
        if (string.IsNullOrWhiteSpace(name))
            return;
        try
        {
            var created = _catalog.Create(name);
            LoadTemplates(created.Folder);
            EditSelected();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Text_("Caption"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(_catalog.Folder);
        Process.Start(new ProcessStartInfo(_catalog.Folder) { UseShellExecute = true });
    }

    /// <summary>The newest change in a template folder: the template or one of its files.</summary>
    private static DateTime Stamp(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max()
            : default;

    private string? Prompt(string title, string label)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(360, 110),
        };
        var text = new TextBox { Left = 12, Top = 36, Width = 336 };
        var ok = new Button { Text = Text_("Ok"), DialogResult = DialogResult.OK, Left = 192, Top = 72, Width = 75 };
        var cancel = new Button { Text = Text_("Cancel"), DialogResult = DialogResult.Cancel, Left = 273, Top = 72, Width = 75 };
        form.Controls.AddRange([new Label { Text = label, Left = 12, Top = 12, AutoSize = true }, text, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(this) == DialogResult.OK ? text.Text : null;
    }
}
