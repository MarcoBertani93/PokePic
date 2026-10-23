using Dock.Model.Core;
using PokePic.Editor.ViewModels.Docking;

namespace PokePic.Editor.Tests;

public class DockLayoutTests
{
    private readonly List<(string Id, bool Visible)> _events = [];

    private EditorDockFactory CreateFactory()
    {
        var factory = new EditorDockFactory(null!, (id, visible) => _events.Add((id, visible)));
        var root = factory.CreateDefaultLayout();
        factory.InitLayout(root);
        return factory;
    }

    private static string Describe(IDockable? d) => d is null ? "null" : $"{d.GetType().Name}({d.Id})";

    [Fact]
    public void Closed_tool_is_hidden_and_comes_back_to_its_dock()
    {
        var factory = CreateFactory();
        var layers = factory.Tools[ToolIds.Layers];
        var leftDock = (IDock)layers.Owner!;

        factory.CloseDockable(layers);

        Assert.False(factory.IsToolVisible(ToolIds.Layers),
            $"owner={Describe(layers.Owner)} original={Describe(layers.OriginalOwner)} hidden={factory.Root!.HiddenDockables?.Count} inLeft={leftDock.VisibleDockables?.Contains(layers)}");
        Assert.Contains((ToolIds.Layers, false), _events);

        factory.SetToolVisible(ToolIds.Layers, true);

        Assert.True(factory.IsToolVisible(ToolIds.Layers));
        var owner = Assert.IsAssignableFrom<IDock>(layers.Owner);
        Assert.Equal(ToolIds.LeftDock, owner.Id);
        Assert.Contains(layers, owner.VisibleDockables!);
    }

    [Fact]
    public void Saved_layout_is_restored_with_moved_and_hidden_panels()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pokepic-layout-{Guid.NewGuid():N}.json");
        try
        {
            var factory = CreateFactory();
            var bottom = (IDock)factory.Tools[ToolIds.Problems].Owner!;
            factory.MoveDockable((IDock)factory.Tools[ToolIds.Properties].Owner!, bottom, factory.Tools[ToolIds.Properties], factory.Tools[ToolIds.TextValues]);
            factory.SetToolVisible(ToolIds.Json, false);

            LayoutStore.Save(factory, path);

            var restored = new EditorDockFactory(null!, (_, _) => { });
            var root = LayoutStore.Load(restored, path, out var error);
            Assert.Null(error);
            restored.InitLayout(root!);

            Assert.Equal(ToolIds.BottomDock, restored.Tools[ToolIds.Properties].Owner?.Id);
            Assert.False(restored.IsToolVisible(ToolIds.Json));
            Assert.True(restored.IsToolVisible(ToolIds.Layers));

            restored.SetToolVisible(ToolIds.Json, true);
            Assert.Equal(ToolIds.LeftDock, restored.Tools[ToolIds.Json].Owner?.Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_layout_file_is_reported_and_ignored()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pokepic-layout-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.Null(LayoutStore.Load(new EditorDockFactory(null!, (_, _) => { }), path, out var error));
            Assert.NotNull(error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Hidden_from_the_menu_and_shown_again()
    {
        var factory = CreateFactory();
        var props = factory.Tools[ToolIds.Properties];

        factory.SetToolVisible(ToolIds.Properties, false);
        Assert.False(factory.IsToolVisible(ToolIds.Properties));

        factory.SetToolVisible(ToolIds.Properties, true);
        Assert.True(factory.IsToolVisible(ToolIds.Properties));
        Assert.Contains(props, ((IDock)props.Owner!).VisibleDockables!);
    }
}
