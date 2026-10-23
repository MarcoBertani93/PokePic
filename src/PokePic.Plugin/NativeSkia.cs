using System.Reflection;
using System.Runtime.InteropServices;

namespace PokePic.Plugin;

/// <summary>
/// PKHeX loads plugins in their own load context, which finds their .NET dependencies but not their native
/// libraries: without help SkiaSharp looks for libSkiaSharp.dll next to PKHeX.exe. This points it to a copy next
/// to the plugin (development builds: runtimes/win-{arch}/native), or to the one embedded in the plugin DLL.
/// </summary>
internal static class NativeSkia
{
    private const string Library = "libSkiaSharp";

    // Process-wide by nature: a DllImport resolver can be set once per assembly.
    private static readonly Lock Gate = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
                return;
            NativeLibrary.SetDllImportResolver(typeof(SkiaSharp.SKImage).Assembly, Resolve);
            _registered = true;
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!name.StartsWith(Library, StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero; // default resolution

        const string file = Library + ".dll";
        var folder = Path.GetDirectoryName(typeof(NativeSkia).Assembly.Location)!;
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        foreach (var path in new[]
        {
            Path.Combine(folder, "runtimes", $"win-{architecture}", "native", file),
            Path.Combine(folder, file),
        })
        {
            if (File.Exists(path))
                return NativeLibrary.Load(path);
        }
        return EmbeddedDependencies.ExtractNative(architecture, file) is { } extracted
            ? NativeLibrary.Load(extracted)
            : throw new DllNotFoundException($"{file} for {architecture} is not in the PokePic plugin ({folder}).");
    }
}
