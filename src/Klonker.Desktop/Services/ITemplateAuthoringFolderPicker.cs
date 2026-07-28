namespace Klonker.Desktop.Services;

public interface ITemplateAuthoringFolderPicker
{
    Task<string?> PickAsync(
        string title,
        CancellationToken cancellationToken = default);
}
