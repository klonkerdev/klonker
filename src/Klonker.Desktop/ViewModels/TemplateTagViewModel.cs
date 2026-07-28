using Avalonia.Media;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed class TemplateTagViewModel
{
    public TemplateTagViewModel(
        string label,
        TemplateTagPalette tagPalette)
    {
        Label = label;
        var brushes = tagPalette.GetBrushes(StablePaletteIndex(label));
        Background = brushes.Background;
        Foreground = brushes.Foreground;
    }

    public string Label { get; }

    public IBrush Background { get; }

    public IBrush Foreground { get; }

    private static int StablePaletteIndex(string label)
    {
        var hash = 17;
        foreach (var character in label)
        {
            hash = unchecked((hash * 31) + char.ToLowerInvariant(character));
        }

        return (hash & int.MaxValue) % 6;
    }
}
