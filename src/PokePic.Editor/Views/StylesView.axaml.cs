using Avalonia.Controls;

namespace PokePic.Editor.Views;

public partial class StylesView : UserControl
{
    public StylesView()
    {
        InitializeComponent();
        ResourceViewInput.Attach(this, Root);
    }
}
