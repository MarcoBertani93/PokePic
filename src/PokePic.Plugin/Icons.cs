namespace PokePic.Plugin;

/// <summary>The plugin's icons, embedded in its DLL (Resources folder).</summary>
internal static class Icons
{
    public static Image Menu => Png("pikapic.png");

    public static Image Export => Png("export.png");

    public static Image ExportBox => Png("exportbox.png");

    public static Image Import => Png("import.png");

    public static Icon Window
    {
        get
        {
            using var stream = Open("icon.ico");
            return new Icon(stream);
        }
    }

    private static Image Png(string name)
    {
        using var stream = Open(name);
        // Image.FromStream keeps reading the stream: copy it into a bitmap that owns its pixels.
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static Stream Open(string name) =>
        typeof(Icons).Assembly.GetManifestResourceStream($"icons/{name}")
        ?? throw new InvalidOperationException($"Icon {name} is missing from the plugin.");
}
