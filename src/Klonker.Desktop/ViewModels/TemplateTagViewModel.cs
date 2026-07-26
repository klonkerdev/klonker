using Avalonia.Media;

namespace Klonker.Desktop.ViewModels;

public sealed class TemplateTagViewModel
{
    public TemplateTagViewModel(string label)
    {
        Label = label;
        var (background, foreground) = GetPalette(StablePaletteIndex(label));
        Background = Brush.Parse(background);
        Foreground = Brush.Parse(foreground);
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

    private static (string Background, string Foreground) GetPalette(int index) =>
        index switch
        {
            0 => ("#123034", "#55D6BE"),
            1 => ("#172A3D", "#70AFFF"),
            2 => ("#29223D", "#B59BFF"),
            3 => ("#352A16", "#E7B64C"),
            4 => ("#371E2A", "#FF8FB1"),
            _ => ("#183322", "#68D779"),
        };
}
