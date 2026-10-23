namespace PokePic.Editor.Services;

public sealed record FileFilter(string Name, string Pattern);

/// <summary>
/// Dialogs the view model needs, implemented by the main window.
/// </summary>
public interface IEditorDialogs
{
    Task<string?> OpenFileAsync(string title, FileFilter filter);

    Task<string?> SaveFileAsync(string title, FileFilter filter, string suggestedName);

    /// <summary>
    /// Asks whether unsaved changes can be discarded.
    /// </summary>
    Task<bool> ConfirmDiscardAsync();

    Task ShowErrorAsync(string message);
}
