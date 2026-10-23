using System.Diagnostics;
using PokePic.Core.Data;

namespace PokePic.Plugin;

/// <summary>
/// Opens the PokePic editor on a template with a Pokémon's data. The data travels as a file named on the command
/// line (<c>--data</c>): it contains images, too large for the command line itself. The editor watches that file,
/// so writing it again updates the editor.
/// </summary>
internal sealed class EditorLauncher(TemplateCatalog catalog)
{
    private static string DataFolder => Path.Combine(Path.GetTempPath(), "PokePic");

    public bool IsAvailable => File.Exists(catalog.EditorPath);

    /// <exception cref="FileNotFoundException">The editor is not in the plugin folder.</exception>
    public void Open(string templatePath, DataPack pack, string uiLanguage)
    {
        if (!IsAvailable)
            throw new FileNotFoundException(Text.Get("EditorMissing", catalog.EditorPath), catalog.EditorPath);

        Directory.CreateDirectory(DataFolder);
        var dataPath = Path.Combine(DataFolder, "pkhex-data.json");
        File.WriteAllText(dataPath, DataPackSerializer.Write(pack));

        var start = new ProcessStartInfo(catalog.EditorPath) { UseShellExecute = false };
        start.ArgumentList.Add("--template");
        start.ArgumentList.Add(templatePath);
        start.ArgumentList.Add("--data");
        start.ArgumentList.Add(dataPath);
        start.ArgumentList.Add("--lang");
        start.ArgumentList.Add(uiLanguage == "it" ? "it" : "en");
        Process.Start(start);
    }
}
