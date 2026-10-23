using Avalonia;
using Avalonia.Headless;
using PokePic.Editor.Tests.Headless;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace PokePic.Editor.Tests.Headless;

/// <summary>
/// The real editor application (themes, data templates) on the headless platform: views are created
/// and laid out without opening windows on the screen.
/// </summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
