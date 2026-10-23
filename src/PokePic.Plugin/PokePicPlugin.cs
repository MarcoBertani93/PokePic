using PKHeX.Core;

namespace PokePic.Plugin;

/// <summary>
/// PokePic in PKHeX: Tools → PokePic exports the Pokémon being edited, or the current box, as cards made from the
/// templates in plugins/PokePic, and imports a card back. A card dropped on PKHeX is imported too.
/// </summary>
public sealed class PokePicPlugin : IPlugin
{
    private ISaveFileProvider? _saveFileEditor;
    private IPKMView? _pkmEditor;
    private CardMaker? _maker;
    private ToolStripMenuItem? _menu;

    public string Name => "PokePic";

    public int Priority => 100;

    public ISaveFileProvider SaveFileEditor => _saveFileEditor ?? throw new InvalidOperationException("The plugin is not initialized.");

    private IPKMView PkmEditor => _pkmEditor ?? throw new InvalidOperationException("The plugin is not initialized.");

    private CardMaker Maker => _maker ??= new CardMaker();

    private TemplateCatalog Catalog => new(Path.GetDirectoryName(typeof(PokePicPlugin).Assembly.Location)!);

    public void Initialize(params object[] args)
    {
        _saveFileEditor = args.OfType<ISaveFileProvider>().FirstOrDefault() ?? throw new ArgumentException("PKHeX did not pass a save file provider.", nameof(args));
        _pkmEditor = args.OfType<IPKMView>().FirstOrDefault() ?? throw new ArgumentException("PKHeX did not pass the Pokémon editor.", nameof(args));
        var menu = args.OfType<ToolStrip>().FirstOrDefault() ?? throw new ArgumentException("PKHeX did not pass its menu.", nameof(args));
        AddMenu(menu);
    }

    public void NotifySaveLoaded()
    {
    }

    public void NotifyDisplayLanguageChanged(string language) => SetMenuTexts();

    /// <summary>
    /// A PokePic card dropped on PKHeX (or opened with it) loads its Pokémon into the editor, with the same messages
    /// as Import card. Any other file, PNGs that are not cards included, is left to PKHeX.
    /// </summary>
    public bool TryLoadFile(string filePath)
    {
        if (!filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || _saveFileEditor is null)
            return false;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // PKHeX tries the file itself and reports the error
        }
        var result = Maker.Import(bytes, SaveFileEditor.SAV);
        if (!result.IsCard)
            return false;
        Show(result);
        return true;
    }

    private void AddMenu(ToolStrip menu)
    {
        if (menu.Items.Find("Menu_Tools", false).FirstOrDefault() is not ToolStripDropDownItem tools)
            throw new InvalidOperationException("PKHeX's Tools menu was not found.");

        _menu = new ToolStripMenuItem { Name = "Menu_PokePic", Image = Icons.Menu };
        var export = new ToolStripMenuItem { Name = "Menu_PokePic_Export", Image = Icons.Export };
        var exportBox = new ToolStripMenuItem { Name = "Menu_PokePic_ExportBox", Image = Icons.ExportBox };
        var import = new ToolStripMenuItem { Name = "Menu_PokePic_Import", Image = Icons.Import };
        export.Click += (_, _) => Run(ExportCurrent);
        exportBox.Click += (_, _) => Run(ExportBox);
        import.Click += (_, _) => Run(Import);
        _menu.DropDownItems.AddRange([export, exportBox, import]);
        tools.DropDownItems.Add(_menu);
        SetMenuTexts();
    }

    private void SetMenuTexts()
    {
        if (_menu is null)
            return;
        _menu.Text = Text.Get("Menu");
        _menu.DropDownItems[0].Text = Text.Get("MenuExport");
        _menu.DropDownItems[1].Text = Text.Get("MenuExportBox");
        _menu.DropDownItems[2].Text = Text.Get("MenuImport");
    }

    /// <summary>Errors are shown, never swallowed: a plugin exception would otherwise reach PKHeX.</summary>
    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text.Get("Caption"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCurrent()
    {
        var pkm = PkmEditor.PreparePKM();
        if (pkm.Species == 0)
        {
            MessageBox.Show(Text.Get("NoPokemon"), Text.Get("Caption"));
            return;
        }
        var title = Text.Get("WindowTitle", pkm.Nickname);
        using var form = new CardForm(Maker, Catalog, [pkm], title, GameInfo.CurrentLanguage);
        form.ShowDialog();
    }

    private void ExportBox()
    {
        var sav = SaveFileEditor.SAV;
        int box = SaveFileEditor.CurrentBox;
        var pokemon = sav.GetBoxData(box).Where(p => p.Species != 0).ToList();
        if (pokemon.Count == 0)
        {
            MessageBox.Show(Text.Get("BoxEmpty"), Text.Get("Caption"));
            return;
        }
        var title = Text.Get("WindowTitleBox", box + 1, pokemon.Count);
        using var form = new CardForm(Maker, Catalog, pokemon, title, GameInfo.CurrentLanguage);
        form.ShowDialog();
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog { Title = Text.Get("ImportTitle"), Filter = Text.Get("PngFilter") };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;
        Show(Maker.Import(File.ReadAllBytes(dialog.FileName), SaveFileEditor.SAV));
    }

    /// <summary>Loads the imported Pokémon, or says why it cannot be loaded.</summary>
    private void Show(ImportResult result)
    {
        if (result.Pkm is not { } pkm)
            MessageBox.Show(Text.Get("ImportFailed", result.Error!), Text.Get("Caption"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else if (!Load(pkm))
            MessageBox.Show(Text.Get("ImportConvert"), Text.Get("Caption"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Shows the Pokémon in PKHeX's editor, converted to the save file's format.</summary>
    private bool Load(PKM pkm)
    {
        var sav = SaveFileEditor.SAV;
        var converted = EntityConverter.ConvertToType(pkm, sav.PKMType, out _);
        if (converted is null)
            return false;
        sav.AdaptToSaveFile(converted);
        PkmEditor.PopulateFields(converted);
        return true;
    }
}
