using Avalonia.Controls;
using PokePic.Editor.ViewModels;
using PokePic.Editor.ViewModels.Docking;

namespace PokePic.Editor.Views;

public partial class ProblemsView : UserControl
{
    public ProblemsView()
    {
        InitializeComponent();
        IssuesList.DoubleTapped += (_, _) =>
        {
            if (IssuesList.SelectedItem is IssueItem issue)
                (DataContext as ProblemsTool)?.Main.GoToIssueCommand.Execute(issue);
        };
    }
}
