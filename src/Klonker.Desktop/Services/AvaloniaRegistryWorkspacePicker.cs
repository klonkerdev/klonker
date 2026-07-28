using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Klonker.Desktop.Services;

public sealed class AvaloniaRegistryWorkspacePicker : IRegistryWorkspacePicker
{
    private static readonly FilePickerFileType PemFile = new("PEM private key")
    {
        Patterns = ["*.pem"],
    };
    private readonly Window owner;

    public AvaloniaRegistryWorkspacePicker(Window owner)
    {
        this.owner = owner;
    }

    public async Task<string?> PickFolderAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = title,
            });
        return folders.Count == 0
            ? null
            : folders[0].TryGetLocalPath();
    }

    public async Task<string?> PickPrivateKeyDestinationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = await owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                DefaultExtension = "pem",
                FileTypeChoices = [PemFile],
                ShowOverwritePrompt = true,
                SuggestedFileName = "registry-signing-key.pem",
                Title = "Save private signing key outside the registry",
            });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickExistingPrivateKeyAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = await owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = [PemFile],
                Title = "Choose registry private signing key",
            });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
