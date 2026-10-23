using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace PokePic.Plugin;

/// <summary>
/// The plugin is a single DLL: its .NET dependencies (PokePic.*, SkiaSharp) are embedded as "deps/{name}.dll"
/// resources (see the csproj) and loaded from there when the plugin's load context cannot find them on disk.
/// Registered by the module initializer, before any type that uses them is loaded.
/// </summary>
internal static class EmbeddedDependencies
{
    // A plugin is the case module initializers exist for: no host code runs before the plugin's types are used.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register()
    {
        var plugin = typeof(EmbeddedDependencies).Assembly;
        var context = AssemblyLoadContext.GetLoadContext(plugin) ?? AssemblyLoadContext.Default;
        context.Resolving += (ctx, name) => Load(ctx, plugin, name);
    }

    private static Assembly? Load(AssemblyLoadContext context, Assembly plugin, AssemblyName name)
    {
        using var stream = plugin.GetManifestResourceStream($"deps/{name.Name}.dll");
        return stream is null ? null : context.LoadFromStream(stream);
    }

    /// <summary>
    /// A native library embedded as "native/win-{arch}/{file}", written once to the user's local data
    /// (a native library can only be loaded from a file); null if the plugin does not contain it.
    /// </summary>
    public static string? ExtractNative(string architecture, string file)
    {
        var plugin = typeof(EmbeddedDependencies).Assembly;
        using var stream = plugin.GetManifestResourceStream($"native/win-{architecture}/{file}");
        if (stream is null)
            return null;

        // One folder per plugin build: a new version never uses an old library.
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PokePic", "native", plugin.ManifestModule.ModuleVersionId.ToString("N"), architecture);
        var path = Path.Combine(folder, file);
        if (File.Exists(path) && new FileInfo(path).Length == stream.Length)
            return path;

        Directory.CreateDirectory(folder);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var output = File.Create(temporary))
            stream.CopyTo(output);
        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(temporary); // another PKHeX started at the same time wrote it first
        }
        return path;
    }
}
