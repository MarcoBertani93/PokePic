namespace PokePic.Core.Data;

/// <summary>
/// The name of an exported card: "{template name}_{Pokémon name}.png", e.g. "Spotlight_Drago.png".
/// </summary>
public static class CardFileName
{
    /// <param name="templateName">The template's "name", else its id.</param>
    /// <param name="dataName">The data pack's <see cref="DataPack.EffectiveName"/>; null: the template name alone.</param>
    public static string For(string templateName, string? dataName)
    {
        var name = dataName is { Length: > 0 } ? $"{templateName}_{dataName}" : templateName;
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Trim().Select(c => invalid.Contains(c) ? '_' : c)]) + ".png";
    }
}
