namespace PokePic.Core.Assets;

/// <summary>
/// Read access to the files referenced by a template or a data pack: a path relative to the template
/// ("types/fire.png"), a file URI ("file:///C:/cards/frame.png") or a web address ("https://…/6.png").
/// </summary>
public interface IAssetProvider
{
    /// <exception cref="IOException">Whether the asset exists cannot be known (e.g. a download failed).</exception>
    bool Exists(string path);

    /// <summary>True if the folder exists (and, for file system assets, is inside the root).</summary>
    bool DirectoryExists(string path);

    /// <exception cref="FileNotFoundException">The asset does not exist.</exception>
    byte[] Read(string path);

    /// <summary>
    /// Human readable location of the asset, for error messages.
    /// </summary>
    string Describe(string path);
}

/// <summary>
/// Assets of a template folder. A plain path is relative to the folder and cannot leave it; "file://" with an
/// absolute path reaches any file; "http://" and "https://" are downloaded through <paramref name="web"/>.
/// </summary>
public sealed class FileSystemAssetProvider(string rootDirectory, WebAssetCache? web = null) : IAssetProvider
{
    public const string FileScheme = "file://";

    private readonly string _root = Path.GetFullPath(rootDirectory);

    public string RootDirectory => _root;

    public bool Exists(string path) =>
        WebAssetCache.IsWebUrl(path) ? Web.TryGet(path) is not null : TryResolve(path, out var full) && File.Exists(full);

    /// <summary>A web folder cannot be listed: it is assumed to exist, and each file is checked when used.</summary>
    public bool DirectoryExists(string path) =>
        WebAssetCache.IsWebUrl(path) || (path.Length == 0 ? Directory.Exists(_root) : TryResolve(path, out var full) && Directory.Exists(full));

    public byte[] Read(string path)
    {
        if (WebAssetCache.IsWebUrl(path))
            return Web.TryGet(path) ?? throw new FileNotFoundException($"Not found on the server: {path}", path);
        if (!TryResolve(path, out var full))
            throw new FileNotFoundException($"Asset path \"{path}\" is outside the folder \"{_root}\".", path);
        return File.ReadAllBytes(full);
    }

    public string Describe(string path) => !WebAssetCache.IsWebUrl(path) && TryResolve(path, out var full) ? full : path;

    private WebAssetCache Web => web ?? throw new WebAssetException("Web addresses (http/https) cannot be used here.");

    private bool TryResolve(string path, out string fullPath)
    {
        if (path.StartsWith(FileScheme, StringComparison.OrdinalIgnoreCase))
        {
            // "file:///C:/x.png" and "file:///home/x.png" are absolute; "file://images/x.png" is relative.
            var rest = Uri.UnescapeDataString(path[FileScheme.Length..]);
            if (rest.Length >= 3 && rest[0] == '/' && char.IsAsciiLetter(rest[1]) && rest[2] == ':')
                rest = rest[1..];
            if (Path.IsPathRooted(rest))
            {
                fullPath = Path.GetFullPath(rest);
                return true;
            }
            path = rest;
        }

        fullPath = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Provider with no assets, for templates that have not been saved yet.
/// </summary>
public sealed class NoAssetProvider : IAssetProvider
{
    public static NoAssetProvider Instance { get; } = new();

    public bool Exists(string path) => false;

    public bool DirectoryExists(string path) => false;

    public byte[] Read(string path) => throw new FileNotFoundException($"Asset \"{path}\" not available: the template has no folder yet.", path);

    public string Describe(string path) => path;
}
