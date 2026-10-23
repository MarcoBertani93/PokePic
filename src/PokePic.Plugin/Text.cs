using System.Globalization;

namespace PokePic.Plugin;

/// <summary>
/// The plugin's texts in English and Italian, following PKHeX's language (other languages: English).
/// In code rather than in resources: satellite assemblies would sit in the plugins folder, where PKHeX
/// tries to load every DLL as a plugin.
/// </summary>
internal static class Text
{
    private static readonly Dictionary<string, (string En, string It)> Texts = new(StringComparer.Ordinal)
    {
        ["Menu"] = ("PokePic", "PokePic"),
        ["MenuExport"] = ("Export card…", "Esporta card…"),
        ["MenuExportBox"] = ("Export box as cards…", "Esporta box come card…"),
        ["MenuImport"] = ("Import card…", "Importa card…"),
        ["WindowTitle"] = ("PokePic: {0}", "PokePic: {0}"),
        ["WindowTitleBox"] = ("PokePic: box {0} ({1} Pokémon)", "PokePic: box {0} ({1} Pokémon)"),
        ["Templates"] = ("Templates", "Template"),
        ["Export"] = ("Export PNG…", "Esporta PNG…"),
        ["ExportBox"] = ("Export box…", "Esporta box…"),
        ["Edit"] = ("Edit template", "Modifica template"),
        ["New"] = ("New template…", "Nuovo template…"),
        ["OpenFolder"] = ("Templates folder", "Cartella template"),
        ["Refresh"] = ("Refresh", "Aggiorna"),
        ["Rendering"] = ("Rendering…", "Creazione…"),
        ["NoTemplates"] = ("No templates in {0}.\nPut each template in its own folder there.", "Nessun template in {0}.\nMetti ogni template in una sua cartella lì."),
        ["NoPokemon"] = ("No Pokémon loaded in the editor.", "Nessun Pokémon caricato nell'editor."),
        ["BoxEmpty"] = ("The current box is empty.", "Il box corrente è vuoto."),
        ["NewName"] = ("Name of the new template:", "Nome del nuovo template:"),
        ["NewTitle"] = ("New template", "Nuovo template"),
        ["Ok"] = ("OK", "OK"),
        ["Cancel"] = ("Cancel", "Annulla"),
        ["Saved"] = ("Card saved: {0}", "Card salvata: {0}"),
        ["BoxSaved"] = ("{0} cards saved in {1}.", "{0} card salvate in {1}."),
        ["BoxSkipped"] = ("{0} Pokémon skipped:\n{1}", "{0} Pokémon saltati:\n{1}"),
        ["ChooseFolder"] = ("Folder for the cards of the box", "Cartella per le card del box"),
        ["PngFilter"] = ("PNG image (*.png)|*.png", "Immagine PNG (*.png)|*.png"),
        ["ImportTitle"] = ("Import a PokePic card", "Importa una card PokePic"),
        ["ImportFailed"] = ("This card cannot be imported: {0}", "Questa card non si può importare: {0}"),
        ["ImportConvert"] = ("The Pokémon of the card cannot be converted to this save file's format.", "Il Pokémon della card non si può convertire nel formato di questo salvataggio."),
        ["EditorMissing"] = ("The PokePic editor is not installed: {0} is missing.", "L'editor di PokePic non è installato: manca {0}."),
        ["Caption"] = ("PokePic", "PokePic"),
    };

    /// <summary>PKHeX's display language ("it", "en", "ja"…).</summary>
    public static string Language => PKHeX.Core.GameInfo.CurrentLanguage;

    public static string Get(string key, params object[] args)
    {
        var (en, it) = Texts[key];
        var text = Language == "it" ? it : en;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }
}
