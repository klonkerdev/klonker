using Klonker.Core.Generation;

namespace Klonker.Desktop.ViewModels;

public sealed class PreviewFileViewModel
{
    public PreviewFileViewModel(PlannedFile file)
    {
        File = file;
    }

    public PlannedFile File { get; }

    public string Path => File.RelativePath;

    public string Content => File.IsText
        ? File.TextContent ?? string.Empty
        : "[Binary file — content preview is unavailable.]";
}
