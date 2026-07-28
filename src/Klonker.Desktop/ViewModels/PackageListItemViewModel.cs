using Avalonia.Media.Imaging;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed class PackageListItemViewModel : ViewModelBase, IDisposable
{
    public PackageListItemViewModel(
        IEnumerable<TemplateListItemViewModel> variants,
        TemplateTagPalette? tagPalette = null)
    {
        tagPalette ??= new TemplateTagPalette();
        Variants = variants
            .OrderBy(variant => variant.Platform, StringComparer.Ordinal)
            .ThenBy(variant => variant.BuildSystem, StringComparer.Ordinal)
            .ThenBy(variant => variant.Variant, StringComparer.Ordinal)
            .ToArray();

        if (Variants.Count == 0)
        {
            throw new ArgumentException(
                "A package must contain at least one template variant.",
                nameof(variants));
        }

        var first = Variants[0];
        RegistryId = first.RegistryId;
        RegistryName = first.RegistryName;
        Family = first.Family;
        Name = first.Name;
        LanguageId = first.LanguageId;
        Language = first.Language;
        Badge = first.Badge;
        Logo = LoadLogo(first.Package.LogoPath);
        Platforms = Variants
            .Select(variant => variant.Platform)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        BuildSystems = Variants
            .Select(variant => variant.BuildSystem)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        RealBuildSystems = Variants
            .Where(variant => variant.HasBuildSystem)
            .Select(variant => variant.BuildSystem)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        AllTags = Variants
            .SelectMany(variant => variant.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Tags = Variants[0].Tags
            .Where(tag => Variants.All(variant =>
                variant.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        TagChips = Tags
            .Select(tag => new TemplateTagViewModel(tag, tagPalette))
            .ToArray();
    }

    public IReadOnlyList<TemplateListItemViewModel> Variants { get; }

    public string RegistryId { get; }

    public string RegistryName { get; }

    public string Family { get; }

    public string QualifiedFamilyId => $"{RegistryId}:{Family}";

    public string Name { get; }

    public string LanguageId { get; }

    public string Language { get; }

    public string Badge { get; }

    public bool IsCpp => LanguageId == "cpp";

    public bool IsLua => LanguageId == "lua";

    public bool HasKnownLanguageIcon => IsCpp || IsLua;

    public Bitmap? Logo { get; }

    public bool HasLogo => Logo is not null;

    public IReadOnlyList<string> Platforms { get; }

    public IReadOnlyList<string> BuildSystems { get; }

    public IReadOnlyList<string> RealBuildSystems { get; }

    public bool HasBuildSystems => RealBuildSystems.Count > 0;

    public int PlatformColumnSpan => HasBuildSystems ? 1 : 2;

    public IReadOnlyList<string> Tags { get; }

    public IReadOnlyList<string> AllTags { get; }

    public IReadOnlyList<TemplateTagViewModel> TagChips { get; }

    public bool HasTags => Tags.Count > 0;

    public string Description =>
        $"{Name} provides {VariantCountText.ToLowerInvariant()} across " +
        $"{string.Join(" and ", Platforms)}.";

    public string SourceLine => $"{Family} · {RegistryName}";

    public string VariantCountText =>
        $"{Variants.Count} variant{(Variants.Count == 1 ? string.Empty : "s")}";

    public string PlatformSummary => string.Join(" · ", Platforms);

    public string BuildSystemSummary => HasBuildSystems
        ? string.Join(" · ", RealBuildSystems)
        : "No build system";

    public bool MatchesVariantFilters(
        string selectedLanguage,
        string selectedPlatform,
        string selectedBuildSystem,
        string selectedTag) =>
        Variants.Any(variant =>
            (selectedLanguage == MainViewModel.AllLanguages ||
             variant.Language == selectedLanguage) &&
            (selectedPlatform == MainViewModel.AllPlatforms ||
             variant.Platform == selectedPlatform) &&
            (selectedBuildSystem == MainViewModel.AllBuildSystems ||
             variant.BuildSystem == selectedBuildSystem) &&
            (selectedTag == MainViewModel.AllTags ||
             variant.Tags.Contains(
                 selectedTag,
                 StringComparer.OrdinalIgnoreCase)));

    public bool MatchesSearch(string search) =>
        search.Length == 0 ||
        Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        Family.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        RegistryName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        Platforms.Any(platform =>
            platform.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
        BuildSystems.Any(buildSystem =>
            buildSystem.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
        AllTags.Any(tag =>
            tag.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
        Variants.Any(variant =>
            variant.Description.Contains(
                search,
                StringComparison.OrdinalIgnoreCase) ||
            variant.Variant.Contains(
                search,
                StringComparison.OrdinalIgnoreCase));

    public override string ToString() => Name;

    public void Dispose()
    {
        Logo?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Bitmap? LoadLogo(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(
                stream,
                96,
                BitmapInterpolationMode.HighQuality);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                InvalidOperationException or
                NotSupportedException)
        {
            return null;
        }
    }
}
