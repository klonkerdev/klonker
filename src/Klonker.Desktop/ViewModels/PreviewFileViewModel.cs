using System.Text;
using Klonker.Core.Generation;

namespace Klonker.Desktop.ViewModels;

public sealed class PreviewFileViewModel
{
    public PreviewFileViewModel(PlannedFile file)
    {
        File = file;
        Content = GetPreviewContent(file);
    }

    public PlannedFile File { get; }

    public string Path => File.RelativePath;

    public string Content { get; }

    public override string ToString() => Path;

    private static string GetPreviewContent(PlannedFile file)
    {
        if (file.IsText)
        {
            return file.TextContent ?? string.Empty;
        }

        if (!IsKnownTextFile(file.RelativePath))
        {
            return "[Binary file — content preview is unavailable.]";
        }

        try
        {
            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true)
                .GetString(file.Content.AsSpan());
        }
        catch (DecoderFallbackException)
        {
            return "[Binary file — content preview is unavailable.]";
        }
    }

    private static bool IsKnownTextFile(string path)
    {
        var fileName = System.IO.Path.GetFileName(path);
        if (fileName.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.IO.Path.GetExtension(fileName).ToLowerInvariant() is
            ".c" or ".cc" or ".cpp" or ".cxx" or
            ".h" or ".hh" or ".hpp" or ".hxx" or
            ".cs" or ".fs" or ".vb" or
            ".cmake" or ".md" or ".txt" or
            ".json" or ".toml" or ".yaml" or ".yml" or ".xml" or
            ".csproj" or ".fsproj" or ".vbproj" or ".props" or ".targets";
    }
}
