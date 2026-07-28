using Avalonia.Media;

namespace Klonker.Desktop.Services;

public sealed class TemplateTagPalette
{
    private readonly TagBrushes[] brushes =
        Enumerable.Range(0, DarkPalette.Count)
            .Select(_ => new TagBrushes(
                new SolidColorBrush(),
                new SolidColorBrush()))
            .ToArray();

    public TemplateTagPalette()
    {
        Apply(useLightPalette: false);
    }

    public TagBrushes GetBrushes(int index)
    {
        if (index < 0 || index >= brushes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return brushes[index];
    }

    public void Apply(bool useLightPalette)
    {
        var palette = useLightPalette ? LightPalette : DarkPalette;
        for (var index = 0; index < palette.Count; index++)
        {
            brushes[index].Background.Color =
                Color.Parse(palette[index].Background);
            brushes[index].Foreground.Color =
                Color.Parse(palette[index].Foreground);
        }
    }

    private static IReadOnlyList<TagColors> DarkPalette { get; } =
    [
        new("#123034", "#55D6BE"),
        new("#172A3D", "#70AFFF"),
        new("#29223D", "#B59BFF"),
        new("#352A16", "#E7B64C"),
        new("#371E2A", "#FF8FB1"),
        new("#183322", "#68D779"),
    ];

    private static IReadOnlyList<TagColors> LightPalette { get; } =
    [
        new("#D7F3EF", "#17665D"),
        new("#DDEBFA", "#245D96"),
        new("#EAE3F8", "#65469A"),
        new("#FAEDCF", "#7A5712"),
        new("#F9E0E8", "#9A3C5E"),
        new("#DDF2E2", "#2D713C"),
    ];

    private sealed record TagColors(string Background, string Foreground);

    public sealed record TagBrushes(
        SolidColorBrush Background,
        SolidColorBrush Foreground);
}
