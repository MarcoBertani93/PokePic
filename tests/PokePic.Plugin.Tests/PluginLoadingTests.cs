using System.Reflection;
using System.Runtime.Loader;
using PKHeX.Core;

namespace PokePic.Plugin.Tests;

/// <summary>
/// Loads the plugin the way PKHeX 26.10.18 does (PKHeX.WinForms/Util/Plugins): every DLL of the plugins folder
/// in its own <see cref="PluginLoadContext"/>, PKHeX.Core shared from the default context.
/// </summary>
public sealed class PluginLoadingTests : IDisposable
{
    private readonly DirectoryInfo _pkhex = Directory.CreateTempSubdirectory("pokepic-pkhex");

    private string Plugins => Path.Combine(_pkhex.FullName, "plugins");

    public PluginLoadingTests()
    {
        // plugins/: the plugin DLL alone (its dependencies are inside it); plugins/PokePic/templates/: the templates.
        Directory.CreateDirectory(Plugins);
        File.Copy(Path.Combine(PluginBuildFolder, "PokePic.Plugin.dll"), Path.Combine(Plugins, "PokePic.Plugin.dll"));
        Copy(Path.Combine(RepositoryRoot, "resources", "templates", "variables"), Path.Combine(Plugins, "PokePic", "templates", "variables"));
    }

    public void Dispose()
    {
        try
        {
            _pkhex.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The native SkiaSharp library stays loaded until the test process ends.
        }
    }

    [Fact]
    public void Pkhex_finds_the_plugin_and_the_plugin_adds_its_menu()
    {
        var plugins = LoadPlugins();

        var plugin = Assert.Single(plugins);
        Assert.Equal("PokePic", plugin.Name);

        var menu = new ToolStrip();
        var tools = new ToolStripMenuItem { Name = "Menu_Tools" };
        menu.Items.Add(tools);
        plugin.Initialize(Fake<ISaveFileProvider>(), Fake<IPKMView>(), menu);

        var pokePic = Assert.IsType<ToolStripMenuItem>(Assert.Single(tools.DropDownItems.Cast<ToolStripItem>()), exactMatch: false);
        Assert.Equal(3, pokePic.DropDownItems.Count);
        Assert.NotNull(pokePic.Image); // the "pikapic" icon
        Assert.All(pokePic.DropDownItems.Cast<ToolStripItem>(), item => Assert.NotNull(item.Image));
    }

    [Fact]
    public void A_card_is_rendered_inside_the_plugin_load_context()
    {
        var plugin = Assert.Single(LoadPlugins());
        var assembly = plugin.GetType().Assembly;
        Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(assembly));

        var maker = Activator.CreateInstance(assembly.GetType("PokePic.Plugin.CardMaker")!)!;
        var pkm = new PK9 { Species = (ushort)Species.Hypno, CurrentLevel = 42, Nickname = "Hypno" };
        var template = Path.Combine(Plugins, "PokePic", "templates", "variables", "template.json");
        var card = maker.GetType().GetMethod("Make")!.Invoke(maker, [template, pkm, "it"])!;

        var problems = (IReadOnlyList<string>)card.GetType().GetProperty("Problems")!.GetValue(card)!;
        Assert.Empty(problems);
        var png = (byte[])card.GetType().GetProperty("Png")!.GetValue(card)!;
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        // Named like PKHeX names the Pokémon's file when it saves it ("0097 - Hypno - <checksum and id>")…
        var saved = pkm.Clone();
        saved.RefreshChecksum();
        Assert.Equal($"Variables demo_{saved.FileNameWithoutExtension}.png", card.GetType().GetProperty("FileName")!.GetValue(card));
        // …and the Pokémon given by PKHeX is not changed by making the card.
        Assert.Equal(0, pkm.Checksum);
    }

    private List<IPlugin> LoadPlugins()
    {
        // As PluginLoader.LoadPluginAssemblies: every DLL, also native ones (their failure is ignored).
        var assemblies = new List<Assembly>();
        foreach (var file in Directory.EnumerateFiles(Plugins, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                assemblies.Add(new PluginLoadContext(file).LoadFromAssemblyPath(file));
            }
            catch (BadImageFormatException)
            {
                // Native libraries (libSkiaSharp.dll…): PKHeX skips them the same way.
            }
        }
        return [.. assemblies
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => !t.IsAbstract && typeof(IPlugin).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IPlugin)Activator.CreateInstance(t)!)];
    }

    /// <summary>PKHeX's PluginLoadContext (26.10.18), unchanged.</summary>
    private sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver Resolver = new(pluginPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var assemblyPath = Resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
                return LoadFromAssemblyPath(assemblyPath);
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch
            {
                return null;
            }
        }
    }

    private static T Fake<T>() where T : class => DispatchProxy.Create<T, NullProxy>();

    public class NullProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }

    private static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PokePic.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (PokePic.slnx) not found.");
        }
    }

    /// <summary>The plugin build of the same configuration as the tests.</summary>
    private static string PluginBuildFolder
    {
        get
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name; // bin/<Configuration>/<tfm>/
            var folder = Path.Combine(RepositoryRoot, "src", "PokePic.Plugin", "bin", configuration, "net10.0-windows");
            return File.Exists(Path.Combine(folder, "PokePic.Plugin.dll")) ? folder : throw new FileNotFoundException($"Plugin not built in {folder}.");
        }
    }

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
