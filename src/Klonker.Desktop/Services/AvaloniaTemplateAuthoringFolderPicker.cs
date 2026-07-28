using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Klonker.Desktop.Services;

public sealed class AvaloniaTemplateAuthoringFolderPicker
    : ITemplateAuthoringFolderPicker
{
    private readonly Window owner;

    public AvaloniaTemplateAuthoringFolderPicker(Window owner)
    {
        this.owner = owner;
    }

    public async Task<string?> PickAsync(
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
        cancellationToken.ThrowIfCancellationRequested();
        return folders.Count == 0
            ? null
            : folders[0].TryGetLocalPath();
    }
}
