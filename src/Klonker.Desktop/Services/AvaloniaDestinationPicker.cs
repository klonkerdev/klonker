using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Klonker.Desktop.Services;

public sealed class AvaloniaDestinationPicker : IDestinationPicker
{
    private readonly Window owner;

    public AvaloniaDestinationPicker(Window owner)
    {
        this.owner = owner;
    }

    public async Task<string?> PickAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Choose a generation destination",
            });
        cancellationToken.ThrowIfCancellationRequested();
        return folders.Count == 0
            ? null
            : folders[0].TryGetLocalPath();
    }
}
