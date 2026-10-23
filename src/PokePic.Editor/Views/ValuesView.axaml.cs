using Avalonia.Controls;
using PokePic.Editor.ViewModels.Docking;
using PokePic.Editor.ViewModels.Values;

namespace PokePic.Editor.Views;

/// <summary>One panel of values (texts, numbers…): the data pack's on the left, the template's on the right.</summary>
public partial class ValuesView : UserControl
{
    public ValuesView()
    {
        InitializeComponent();
        ResourceViewInput.Attach(this, Root);
        DataList.DoubleTapped += (_, _) =>
        {
            if (DataList.SelectedItem is DataItem item && DataContext is ValuesTool tool)
                tool.Tab.InsertDataCommand.Execute(item);
        };
    }
}
