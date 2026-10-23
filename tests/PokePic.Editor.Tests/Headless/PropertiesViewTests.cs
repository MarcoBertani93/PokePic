using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PokePic.Editor.Services;
using PokePic.Editor.ViewModels;
using PokePic.Editor.ViewModels.Docking;
using PokePic.Editor.ViewModels.Properties;
using PokePic.Editor.Views;

namespace PokePic.Editor.Tests.Headless;

/// <summary>
/// Edits made through the real properties panel controls must reach the JSON.
/// </summary>
public class PropertiesViewTests
{
    private const string MalePath = "elements[1].children[4]";

    private static (MainViewModel Main, PropertiesView View, Window Window) Open(string layerPath)
    {
        // The editor ships the samples next to its binaries; tests read that copy, never the repository files.
        var samples = Path.Combine(AppContext.BaseDirectory, "Samples");
        var main = new MainViewModel(new NoDialogs());
        main.Initialize(Path.Combine(samples, "templates", "pixelcard", "template.json"), Path.Combine(samples, "data", "charizard.json"));
        main.SelectPath(layerPath);

        var view = new PropertiesView { DataContext = new PropertiesTool(main) };
        var window = new Window { Width = 400, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (main, view, window);
    }

    private static T ControlFor<T>(Control view, object dataContext) where T : Control =>
        view.GetVisualDescendants().OfType<T>().First(c => c.DataContext == dataContext && c.IsEffectivelyVisible);

    private static ImageSourceField ImageField(MainViewModel main) => main.Properties!.Fields.OfType<ImageSourceField>().Single();

    [AvaloniaFact]
    public void Picking_a_resource_from_the_dropdown_changes_the_json()
    {
        var (main, view, window) = Open(MalePath);
        var field = ImageField(main).Image;
        Assert.Equal("{male}", field.Text);

        var combo = ControlFor<ComboBox>(view, field);
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        combo.SelectedItem = "{female}";
        combo.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("\"name\": \"Male\", \"type\": \"image\", \"rect\": [153, 5, 13, 13], \"image\": \"{female}\"", main.JsonDocument.Text, StringComparison.Ordinal);
        Assert.Equal("{female}", ImageField(main).Image.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Picking_twice_and_undoing_keeps_the_dropdown_in_sync()
    {
        var (main, view, window) = Open(MalePath);
        var combo = ControlFor<ComboBox>(view, ImageField(main).Image);

        Pick(combo, "{female}");
        Pick(ControlFor<ComboBox>(view, ImageField(main).Image), "{neutral}");
        Assert.Equal("{neutral}", ImageField(main).Image.Text);

        main.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("{female}", ImageField(main).Image.Text);

        // Opening and closing the list without picking must not re-apply the old selection.
        combo = ControlFor<ComboBox>(view, ImageField(main).Image);
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        combo.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("\"image\": \"{female}\", \"visible\": \"{gender.isMale}\"", main.JsonDocument.Text, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void Smooth_is_picked_from_auto_true_false_and_auto_removes_it()
    {
        var (main, view, window) = Open(MalePath);
        ValueField Smooth() => main.Properties!.Fields.OfType<ValueField>().Single(f => f.Label == Localization.Strings.Get("P_smooth"));
        Assert.Equal([Localization.Strings.Get("Auto"), "true", "false"], Smooth().Suggestions);

        Pick(ControlFor<ComboBox>(view, Smooth()), "false");
        Assert.Contains("\"image\": \"{male}\", \"visible\": \"{gender.isMale}\", \"smooth\": false", main.JsonDocument.Text, StringComparison.Ordinal);

        Pick(ControlFor<ComboBox>(view, Smooth()), Localization.Strings.Get("Auto"));
        Assert.DoesNotContain("\"smooth\"", main.JsonDocument.Text, StringComparison.Ordinal);
        Assert.Equal("", Smooth().Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Default_language_comes_from_the_data_pack_and_a_text_can_force_its_own()
    {
        const string movePath = "elements[6].children[0].children[2]"; // Moves / Move 1 / Name
        var (main, view, window) = Open(movePath);

        Assert.Equal("it", main.TextLanguage);
        Assert.Contains("ja", main.TextLanguages);
        Assert.Contains(main.ValueTabs.Tab("TextValues").DataItems, v => v.Key == "move1.name" && v.Display.StartsWith("Lanciafiamme", StringComparison.Ordinal));

        var language = main.Properties!.Fields.OfType<ValueField>().Single(f => f.Label == Localization.Strings.Get("P_language"));
        var combo = ControlFor<ComboBox>(view, language);
        Pick(combo, "ja");

        Assert.Contains("\"text\": \"{move1.name}\", \"language\": \"ja\"", main.JsonDocument.Text, StringComparison.Ordinal);

        main.PreviewLanguage = main.PreviewLanguages.Single(c => c.Code == "de");
        Assert.Contains(main.ValueTabs.Tab("TextValues").DataItems, v => v.Key == "move1.name" && v.Display.StartsWith("Flammenwurf", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void Image_set_keys_are_edited_one_per_line()
    {
        const string type1Path = "elements[1].children[2]"; // Header / Type 1
        var (main, view, window) = Open(type1Path);
        var keys = ImageField(main).SetKey;
        Assert.Equal("{type1.key}", keys.Text);

        var box = ControlFor<TextBox>(view, keys);
        box.Text = "{type1.key}\n normal ";
        Dispatcher.UIThread.RunJobs();
        main.FlushPendingEdits();
        Assert.Contains("\"image\": { \"set\": \"types\", \"key\": [\"{type1.key}\", \"normal\"] }", main.JsonDocument.Text, StringComparison.Ordinal);

        ControlFor<TextBox>(view, ImageField(main).SetKey).Text = "{type2.key}";
        Dispatcher.UIThread.RunJobs();
        main.FlushPendingEdits();
        Assert.Contains("\"image\": { \"set\": \"types\", \"key\": \"{type2.key}\" }", main.JsonDocument.Text, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void First_click_selects_and_only_a_press_on_the_selection_drags()
    {
        var (main, _, window) = Open(MalePath);
        main.SelectPath(null);
        WaitForPreview(main);
        string before = main.JsonDocument.Text;

        // Ball sprite at [5, 65, 20, 20]: first press selects, dragging does nothing.
        main.CanvasPressed(15, 75, null);
        main.CanvasDragged(25, 85);
        main.CanvasReleased();
        Assert.Equal("Ball", main.SelectedLayer?.Name);
        Assert.Equal(before, main.JsonDocument.Text);

        // Second press inside the selection: now it moves.
        main.CanvasPressed(15, 75, null);
        main.CanvasDragged(25, 85);
        main.CanvasReleased();
        Assert.Contains("\"name\": \"Ball\", \"type\": \"image\", \"rect\": [15, 75, 20, 20]", main.JsonDocument.Text, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void A_click_on_an_element_in_front_of_the_selection_selects_it()
    {
        var (main, _, window) = Open("elements[2]"); // Sprite [1, 22, 68, 56], behind Ball [5, 65, 20, 20]
        WaitForPreview(main);
        string before = main.JsonDocument.Text;

        main.CanvasPressed(15, 75, null);
        main.CanvasDragged(25, 85);
        main.CanvasReleased();

        Assert.Equal("Ball", main.SelectedLayer?.Name);
        Assert.Equal(before, main.JsonDocument.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_click_builds_a_selection_that_moves_and_is_edited_together()
    {
        var (main, view, window) = Open("elements[3]"); // Ball
        WaitForPreview(main);

        main.CanvasPressed(30, 40, null, toggle: true); // Sprite
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["elements[3]", "elements[2]"], main.SelectedPaths);
        Assert.Equal(PropertiesPanel.MultipleType, main.Properties!.Type);
        Assert.Contains("Ball", main.Properties.Fields.OfType<InfoField>().Single().Text, StringComparison.Ordinal);

        // Shared values are shown, different ones are marked.
        var flip = main.Properties.Fields.OfType<ValueField>().Single(f => f.Label == Localization.Strings.Get("P_flip"));
        Assert.True(flip.IsMixed);
        Assert.Equal(Localization.Strings.Get("MixedValues"), flip.Placeholder);
        var rect = main.Properties.Fields.OfType<TupleField>().Single();
        Assert.Equal("", rect.Parts[2].Text);
        Assert.True(rect.Parts[2].IsMixed);

        // Editing one part writes it to all the elements and keeps their other parts.
        ControlFor<TextBox>(view, rect.Parts[1]).Text = "30";
        Dispatcher.UIThread.RunJobs();
        main.FlushPendingEdits();
        Assert.Contains("\"name\": \"Sprite\", \"type\": \"image\", \"rect\": [1, 30, 68, 56]", main.JsonDocument.Text, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Ball\", \"type\": \"image\", \"rect\": [5, 30, 20, 20]", main.JsonDocument.Text, StringComparison.Ordinal);
        Assert.Equal(2, main.SelectedPaths.Count);

        // A press on any selected element drags them all.
        WaitForLayout(main);
        main.CanvasPressed(15, 40, null);
        main.CanvasDragged(25, 45);
        main.CanvasReleased();
        Assert.Contains("\"name\": \"Sprite\", \"type\": \"image\", \"rect\": [11, 35, 68, 56]", main.JsonDocument.Text, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Ball\", \"type\": \"image\", \"rect\": [15, 35, 20, 20]", main.JsonDocument.Text, StringComparison.Ordinal);

        // Ctrl+click on a selected element removes it.
        main.CanvasPressed(50, 70, null, toggle: true);
        Assert.Equal(["elements[3]"], main.SelectedPaths);
        window.Close();
    }

    [AvaloniaFact]
    public void Several_layers_are_grouped_at_once()
    {
        var (main, _, window) = Open("elements[2]");
        main.ToggleSelection("elements[3]");

        main.GroupLayerCommand.Execute(null);

        var group = Assert.Single(main.SelectedLayers);
        Assert.True(group.IsGroup);
        Assert.Equal(["Sprite", "Ball"], group.Children.Select(c => c.Name));
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_click_in_the_layer_tree_selects_several_layers()
    {
        var (main, _, window) = Open("elements[2]");
        var layers = new LayersView { DataContext = new LayersTool(main) };
        window.Content = layers;
        Dispatcher.UIThread.RunJobs();
        var tree = layers.GetVisualDescendants().OfType<TreeView>().Single();
        Assert.Equal([main.Layers[2]], tree.SelectedItems.Cast<LayerNode>());

        tree.SelectedItems.Add(main.Layers[3]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["elements[2]", "elements[3]"], main.SelectedPaths);

        // Selection made elsewhere (the template, from the gray background) reaches the tree.
        main.SelectPath(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(tree.SelectedItems);
        Assert.Equal(PropertiesPanel.TemplateType, main.Properties!.Type);
        window.Close();
    }

    [AvaloniaFact]
    public void Values_show_what_they_give_and_can_be_edited()
    {
        var samples = Path.Combine(AppContext.BaseDirectory, "Samples");
        var main = new MainViewModel(new NoDialogs());
        main.Initialize(Path.Combine(samples, "templates", "variables", "template.json"), Path.Combine(samples, "data", "charizard.json"));

        ViewModels.Values.ValueItem Value(string tab, string name) => main.ValueTabs.Tab(tab).Items.Single(v => v.Name == name);
        Assert.NotNull(Value("ColorValues", "accent").Rows[0].Swatch);
        Assert.Equal("CHARIZARD Lv. 50", Value("TextValues", "title").Rows[0].Preview);
        Assert.Equal("6", Value("NumberValues", "pad").Rows[0].Preview);

        Value("NumberValues", "pad").Rows[0].Cells[0].EditText = "8";
        main.FlushPendingEdits();
        Assert.Contains("\"pad\": 8", main.JsonDocument.Text, StringComparison.Ordinal);

        Value("NumberValues", "pad").EditName = "padding";
        main.FlushPendingEdits();
        Assert.Contains("\"padding\": 8", main.JsonDocument.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Zoom_steps_through_presets_and_fits_the_window()
    {
        var (main, _, window) = Open(MalePath);
        WaitForPreview(main);
        Assert.Equal(192, main.CanvasPixelWidth);

        main.Zoom = 1;
        main.ZoomInCommand.Execute(null);
        Assert.Equal(1.5, main.Zoom);
        Assert.Equal("150%", main.ZoomText);

        main.FitTo(800, 600, 1, 16); // (800 - 32) / 192 = 4, (600 - 32) / 128 = 4.43
        Assert.Equal(4, main.Zoom);
        Assert.False(main.IsFitToWindow); // FitTo alone does not turn the mode on

        main.ToggleFitCommand.Execute(null);
        Assert.True(main.IsFitToWindow);
        main.ToggleFitCommand.Execute(null);
        Assert.Equal(1, main.Zoom);
        Assert.False(main.IsFitToWindow);
        window.Close();
    }

    [AvaloniaFact]
    public void Copied_layers_keep_their_place_on_the_canvas_wherever_they_are_pasted()
    {
        var (main, _, window) = Open("elements[4].children[0]"); // Info (position [20, 89]) / Nature [0, 0, 60, 10]
        var text = main.CopySelection()!;
        Assert.Equal("[20,89,60,10]", System.Text.Json.Nodes.JsonNode.Parse(text)![0]!["rect"]!.ToJsonString());

        main.SelectPath("elements[2]"); // Sprite, at the top level
        Assert.True(main.Paste(text));
        Assert.Equal(["elements[3]"], main.SelectedPaths);
        Assert.Contains("\"name\": \"Nature\", \"type\": \"text\", \"style\": \"label\", \"rect\": [20, 89, 60, 10]", main.JsonDocument.Text, StringComparison.Ordinal);

        Assert.False(main.Paste("not json"));
        window.Close();
    }

    [AvaloniaFact]
    public void A_style_is_edited_in_the_properties_panel()
    {
        var (main, view, window) = Open(MalePath);

        main.EditStyle("label");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PropertiesPanel.StyleType, main.Properties!.Type);
        var size = main.Properties.Fields.OfType<ValueField>().Single(f => f.Label == Localization.Strings.Get("P_size"));
        Assert.Equal("8", size.Text);

        ControlFor<TextBox>(view, size).Text = "9";
        Dispatcher.UIThread.RunJobs();
        main.FlushPendingEdits();
        Assert.Contains("\"label\": { \"type\": \"text\", \"font\": \"{pixel}\", \"size\": 9", main.JsonDocument.Text, StringComparison.Ordinal);
        Assert.Equal(PropertiesPanel.StyleType, main.Properties!.Type);

        main.SelectPath(MalePath); // selecting an element ends the editing of the style
        Assert.Equal("image", main.Properties!.Type);
        window.Close();
    }

    [AvaloniaFact]
    public void A_set_lookup_typed_as_a_value_becomes_an_image_of_the_set()
    {
        var (main, _, window) = Open(MalePath);
        Assert.Equal(0, ImageField(main).Mode);

        ImageField(main).Image.Text = "{types[type1.key]}";
        main.FlushPendingEdits();
        Dispatcher.UIThread.RunJobs();

        var field = ImageField(main);
        Assert.Equal(1, field.Mode);
        Assert.Equal("types", field.SetName.Text);
        Assert.Equal("{type1.key}", field.SetKey.Text);
        Assert.Contains("\"image\": \"{types[type1.key]}\"", main.JsonDocument.Text, StringComparison.Ordinal);

        // One plain key keeps the short form; a formatted key needs the object form.
        field.SetKey.Text = "{type2.key}";
        main.FlushPendingEdits();
        Assert.Contains("\"image\": \"{types[type2.key]}\"", main.JsonDocument.Text, StringComparison.Ordinal);
        ImageField(main).SetKey.Text = "{species:D4}";
        main.FlushPendingEdits();
        Assert.Contains("\"image\": { \"set\": \"types\", \"key\": \"{species:D4}\" }", main.JsonDocument.Text, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void The_window_shows_the_template_name()
    {
        var (main, _, window) = Open(MalePath);
        Assert.StartsWith("Pixel Card", main.Title, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void The_exported_file_is_named_after_the_template_and_the_pokemon()
    {
        var (main, _, window) = Open(MalePath);
        // This sample has no "name": the nickname is used.
        main.OpenSampleDataCommand.Execute(main.SampleData.Single(s => s.Label == "charizard-mega-x"));
        Assert.Equal("Pixel Card_Drago.png", main.ExportFileName());
        window.Close();
    }

    private static void WaitForLayout(MainViewModel main)
    {
        // Boxes follow the committed text once its render is done.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (main.SelectionBounds is not { Y: 30 } && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
    }

    private static void WaitForPreview(MainViewModel main)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (main.Preview is null && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Assert.NotNull(main.Preview);
    }

    private static void Pick(ComboBox combo, string value)
    {
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        combo.SelectedItem = value;
        combo.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Typing_in_a_text_box_and_clicking_elsewhere_changes_the_json()
    {
        var (main, view, window) = Open(MalePath);
        var rect = main.Properties!.Fields.OfType<TupleField>().First();

        var box = ControlFor<TextBox>(view, rect.Parts[0]);
        box.Focus();
        box.Text = "120";
        Dispatcher.UIThread.RunJobs();
        main.FlushPendingEdits(); // what a click outside the field triggers

        Assert.Contains("\"name\": \"Male\", \"type\": \"image\", \"rect\": [120, 5, 13, 13]", main.JsonDocument.Text, StringComparison.Ordinal);
        window.Close();
    }

    internal sealed class NoDialogs : IEditorDialogs
    {
        public Task<string?> OpenFileAsync(string title, FileFilter filter) => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, FileFilter filter, string suggestedName) => Task.FromResult<string?>(null);

        public Task<bool> ConfirmDiscardAsync() => Task.FromResult(true);

        public Task ShowErrorAsync(string message) => throw new InvalidOperationException(message);
    }
}
