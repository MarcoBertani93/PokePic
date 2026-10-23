using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PokePic.Editor.ViewModels;
using PokePic.Editor.ViewModels.Docking;
using PokePic.Editor.ViewModels.Values;
using PokePic.Editor.Views;

namespace PokePic.Editor.Tests.Headless;

/// <summary>
/// The value panels (one per kind): the data pack's values, and the template's as tables of keys × languages.
/// </summary>
public class ResourceViewsTests
{
    private static MainViewModel Open(string template = "pixelcard")
    {
        var samples = Path.Combine(AppContext.BaseDirectory, "Samples");
        var main = new MainViewModel(new PropertiesViewTests.NoDialogs());
        main.Initialize(Path.Combine(samples, "templates", template, "template.json"), Path.Combine(samples, "data", "charizard.json"));
        return main;
    }

    private static Window Show(Control view)
    {
        var window = new Window { Width = 1400, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Commit(MainViewModel main)
    {
        main.FlushPendingEdits();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Every_panel_has_its_view()
    {
        var main = Open();
        var factory = new EditorDockFactory(main, (_, _) => { });
        foreach (var id in ToolIds.Values.Select(v => v.Id).Append(ToolIds.Styles))
        {
            var host = new ContentControl { Content = factory.Tools[id] };
            var window = Show(host);
            Assert.NotEmpty(host.GetVisualDescendants().OfType<UserControl>());
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Data_values_are_listed_by_kind_and_template_values_with_their_form()
    {
        var main = Open("variables");
        var colors = main.ValueTabs.Tab(ToolIds.ColorValues);

        Assert.Contains(colors.DataItems, d => d.Key == "type1.color" && d.Swatch is not null);
        Assert.Contains(main.ValueTabs.Tab(ToolIds.ImageValues).DataItems, d => d.Key == "species.sprite" && d.Thumbnail is not null);
        var tint = colors.Items.Single(i => i.Name == "typeTint");
        Assert.Equal(ValueForm.Items, tint.Form);
        Assert.Equal(["fire", "water", "grass", "psychic", null], tint.Rows.Select(r => r.Key));
        Assert.Equal(RowKind.Fallback, tint.Rows[^1].Kind);
        Assert.Equal("#F08030", tint.Rows[0].Preview); // with the current data pack and language

        var numbers = main.ValueTabs.Tab(ToolIds.NumberValues);
        Assert.Equal(["int"], numbers.Items.Select(i => i.Section).Distinct());
        var label = main.ValueTabs.Tab(ToolIds.TextValues).Items.Single(i => i.Name == "lvLabel");
        Assert.Equal(["en", "de", "es", "fr", "it"], label.Columns.Select(c => c.Code));
        Assert.Equal("Liv.", label.Rows.Single().Preview); // charizard.json is in Italian
    }

    [AvaloniaFact]
    public void A_value_gets_languages_and_becomes_a_set_from_its_table()
    {
        var main = Open();
        var window = Show(new ValuesView { DataContext = new ValuesTool(main, ToolIds.TextValues, "S_texts") });
        var texts = main.ValueTabs.Tab(ToolIds.TextValues);

        texts.AddCommand.Execute(null);
        Commit(main);
        var item = Assert.Single(texts.Items);
        Assert.Equal("text", item.Name);
        Assert.Contains("\"values\": {", main.JsonDocument.Text, StringComparison.Ordinal);

        item.Rows[0].Cells[0].EditText = "Level {level}";
        Commit(main);
        item = texts.Items.Single();
        item.NewLanguage = "it";
        item.AddLanguageCommand.Execute(null);
        Assert.DoesNotContain("\"it\"", main.JsonDocument.Text, StringComparison.Ordinal); // an empty column changes nothing
        item.Rows[0].Cells[1].EditText = "Livello {level}";
        Commit(main);
        Assert.Contains("\"text\": { \"text\": { \"en\": \"Level {level}\", \"it\": \"Livello {level}\" } }", main.JsonDocument.Text, StringComparison.Ordinal);
        Assert.Equal("Livello 50", texts.Items.Single().Rows[0].Preview);

        // A set: the value becomes the first item; a key is renamed; removing a language collapses to one value.
        texts.Items.Single().FormIndex = 1;
        Commit(main);
        item = texts.Items.Single();
        Assert.Equal(ValueForm.Items, item.Form);
        item.Rows[0].EditKey = "fire";
        Commit(main);
        texts.Items.Single().Columns.Single(c => c.Code == "it").RemoveCommand.Execute(null);
        Commit(main);
        Assert.Contains("\"text\": { \"items\": { \"fire\": \"Level {level}\" } }", main.JsonDocument.Text, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Numbers_and_true_false_are_written_as_json_numbers_and_booleans()
    {
        var main = Open();
        main.ValueTabs.Tab(ToolIds.NumberValues).AddCommand.Execute("float");
        main.ValueTabs.Tab(ToolIds.BoolValues).AddCommand.Execute(null);
        Commit(main);

        main.ValueTabs.Tab(ToolIds.NumberValues).Items.Single().Rows[0].Cells[0].EditText = "2.5";
        Commit(main);
        main.ValueTabs.Tab(ToolIds.BoolValues).Items.Single().Rows[0].Cells[0].EditText = "{!hasType2}";
        Commit(main);

        Assert.Contains("\"float\": { \"float\": 2.5 }", main.JsonDocument.Text, StringComparison.Ordinal);
        Assert.Contains("\"bool\": { \"bool\": \"{!hasType2}\" }", main.JsonDocument.Text, StringComparison.Ordinal);
    }
}
