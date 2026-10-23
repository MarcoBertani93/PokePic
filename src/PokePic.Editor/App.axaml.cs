using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PokePic.Editor.ViewModels;
using PokePic.Editor.Views;

namespace PokePic.Editor;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var arguments = CommandLine.Parse(desktop.Args ?? []);
            if (arguments.Language is not null)
                CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(arguments.Language);

            var window = new MainWindow();
            var viewModel = new MainViewModel(window);
            window.Attach(viewModel);
            viewModel.Initialize(arguments.Template, arguments.Data);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// <c>PokePic.Editor [--template path] [--data path] [--lang it|en]</c>, or a single template path.
/// The PKHeX plugin launches the editor with the selected Pokémon's data pack.
/// </summary>
internal sealed record CommandLine(string? Template, string? Data, string? Language)
{
    public static CommandLine Parse(string[] args)
    {
        string? template = null, data = null, language = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--template" when i + 1 < args.Length:
                    template = args[++i];
                    break;
                case "--data" when i + 1 < args.Length:
                    data = args[++i];
                    break;
                case "--lang" when i + 1 < args.Length:
                    language = args[++i];
                    break;
                default:
                    template ??= args[i];
                    break;
            }
        }
        return new CommandLine(template, data, language);
    }
}
