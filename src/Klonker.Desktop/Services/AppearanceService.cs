using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Klonker.Desktop.Services;

public sealed class AppearanceService
{
    private readonly Func<Application?> applicationAccessor;
    private readonly TemplateTagPalette tagPalette;

    public AppearanceService()
        : this(new TemplateTagPalette())
    {
    }

    public AppearanceService(TemplateTagPalette tagPalette)
        : this(() => Application.Current, tagPalette)
    {
    }

    internal AppearanceService(
        Func<Application?> applicationAccessor,
        TemplateTagPalette tagPalette)
    {
        this.applicationAccessor = applicationAccessor;
        this.tagPalette = tagPalette;
    }

    public void Apply(AppAppearance appearance)
    {
        var application = applicationAccessor();
        if (application is null)
        {
            tagPalette.Apply(appearance == AppAppearance.Light);
            return;
        }

        application.RequestedThemeVariant = appearance switch
        {
            AppAppearance.Dark => ThemeVariant.Dark,
            AppAppearance.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };

        var useLightPalette = appearance == AppAppearance.Light ||
            (appearance == AppAppearance.System &&
             application.ActualThemeVariant == ThemeVariant.Light);
        var palette = useLightPalette ? LightPalette : DarkPalette;
        tagPalette.Apply(useLightPalette);
        foreach (var item in palette)
        {
            var color = Color.Parse(item.Value);
            if (application.Resources[item.Key] is SolidColorBrush brush)
            {
                brush.Color = color;
            }
            else
            {
                application.Resources[item.Key] = new SolidColorBrush(color);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> DarkPalette { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ChromeBrush"] = "#070B0E",
            ["BackgroundBrush"] = "#090E12",
            ["SurfaceBrush"] = "#0E151B",
            ["SurfaceRaisedBrush"] = "#131C22",
            ["InputBrush"] = "#0A1014",
            ["SurfaceHoverBrush"] = "#1A252D",
            ["SelectionBrush"] = "#173622",
            ["BorderBrush"] = "#202D35",
            ["BorderStrongBrush"] = "#344650",
            ["AccentMutedBrush"] = "#173622",
            ["TextBrush"] = "#F3F6F8",
            ["TextSecondaryBrush"] = "#BAC4CA",
            ["TextMutedBrush"] = "#7F8D96",
            ["ErrorSurfaceBrush"] = "#351B1E",
            ["WarningSurfaceBrush"] = "#302814",
            ["SuccessSurfaceBrush"] = "#102B19",
        };

    private static IReadOnlyDictionary<string, string> LightPalette { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ChromeBrush"] = "#F2F6F3",
            ["BackgroundBrush"] = "#E9EFEB",
            ["SurfaceBrush"] = "#FFFFFF",
            ["SurfaceRaisedBrush"] = "#F5F8F6",
            ["InputBrush"] = "#F7FAF8",
            ["SurfaceHoverBrush"] = "#E7F1E9",
            ["SelectionBrush"] = "#D7EFDC",
            ["BorderBrush"] = "#D2DDD5",
            ["BorderStrongBrush"] = "#A9BBB0",
            ["AccentMutedBrush"] = "#D7EFDC",
            ["TextBrush"] = "#17201A",
            ["TextSecondaryBrush"] = "#435148",
            ["TextMutedBrush"] = "#6C7B71",
            ["ErrorSurfaceBrush"] = "#FDE8E7",
            ["WarningSurfaceBrush"] = "#FFF3D5",
            ["SuccessSurfaceBrush"] = "#DFF5E4",
        };
}
