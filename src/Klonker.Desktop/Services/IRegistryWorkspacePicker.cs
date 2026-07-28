namespace Klonker.Desktop.Services;

public interface IRegistryWorkspacePicker
{
    Task<string?> PickFolderAsync(
        string title,
        CancellationToken cancellationToken = default);

    Task<string?> PickPrivateKeyDestinationAsync(
        CancellationToken cancellationToken = default);

    Task<string?> PickExistingPrivateKeyAsync(
        CancellationToken cancellationToken = default);
}
