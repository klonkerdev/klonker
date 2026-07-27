using CommunityToolkit.Mvvm.ComponentModel;
using Klonker.Core.Registry;
using Klonker.Core.Templates;

namespace Klonker.Desktop.ViewModels;

public sealed partial class TemplateListItemViewModel : ViewModelBase
{
    public TemplateListItemViewModel(RegistryTemplatePackage template)
    {
        Template = template;
        IsFavorite = Package.Manifest.IsFavorite;
        TagChips = Tags
            .Select(tag => new TemplateTagViewModel(tag))
            .ToArray();
    }

    public RegistryTemplatePackage Template { get; }

    public TemplatePackage Package => Template.Package;

    public string RegistryId => Template.RegistryId;

    public string RegistryName => Template.RegistryDisplayName;

    public string QualifiedId => Template.QualifiedId;

    public string Name => Package.Manifest.Name;

    public string Description => Package.Manifest.Description;

    public string Version => $"v{Package.Manifest.Version}";

    public string Family => Package.Manifest.FamilyId;

    public string SourceLine => $"{Family} · {RegistryName}";

    public string Variant => Package.Manifest.VariantId;

    public string VariantDisplayName => HasBuildSystem
        ? $"{Platform} · {BuildSystem}"
        : HumanizeVariantId(Variant);

    public IReadOnlyList<string> Tags =>
        Package.Manifest.Tags.IsDefault ? [] : Package.Manifest.Tags;

    public bool HasTags => Tags.Count > 0;

    public IReadOnlyList<TemplateTagViewModel> TagChips { get; }

    public string LanguageId
    {
        get
        {
            if (!Package.Manifest.Language.Equals(
                    "unknown",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Package.Manifest.Language;
            }

            if (Package.Manifest.Id.Contains("cpp", StringComparison.OrdinalIgnoreCase))
            {
                return "cpp";
            }

            if (Package.Manifest.Id.Contains("lua", StringComparison.OrdinalIgnoreCase))
            {
                return "lua";
            }

            return "unknown";
        }
    }

    public string Language => LanguageId switch
    {
        "cpp" => "C++",
        "lua" => "Lua",
        "unknown" => "Other",
        _ => HumanizeVariantId(LanguageId),
    };

    public string Badge => Language;

    public bool IsCpp => LanguageId == "cpp";

    public bool IsLua => LanguageId == "lua";

    public bool HasKnownLanguageIcon => IsCpp || IsLua;

    public string Platform => Package.Manifest.TargetOs.Equals(
        "windows",
        StringComparison.OrdinalIgnoreCase)
        ? "Windows"
        : Package.Manifest.TargetOs.Equals(
            "linux",
            StringComparison.OrdinalIgnoreCase)
            ? "Linux"
            : Package.Manifest.TargetOs;

    public string BuildSystem => Package.Manifest.BuildSystem.Equals(
        "cmake",
        StringComparison.OrdinalIgnoreCase)
        ? "CMake"
        : Package.Manifest.BuildSystem.Equals(
            "xmake",
            StringComparison.OrdinalIgnoreCase)
            ? "xmake"
            : Package.Manifest.BuildSystem.Equals(
                "make",
                StringComparison.OrdinalIgnoreCase)
                ? "GNU Make"
                : Package.Manifest.BuildSystem.Equals(
                    "none",
                    StringComparison.OrdinalIgnoreCase)
                    ? "None"
                : Package.Manifest.BuildSystem;

    public string Metadata => HasBuildSystem
        ? $"{Platform} · {BuildSystem}"
        : Platform;

    public bool HasBuildSystem =>
        !Package.Manifest.BuildSystem.Equals(
            "none",
            StringComparison.OrdinalIgnoreCase);

    public int PlatformColumnSpan => HasBuildSystem ? 1 : 2;

    public bool IsWindows =>
        Package.Manifest.TargetOs.Equals(
            "windows",
            StringComparison.OrdinalIgnoreCase);

    public bool IsLinux =>
        Package.Manifest.TargetOs.Equals(
            "linux",
            StringComparison.OrdinalIgnoreCase);

    public bool HasKnownPlatformIcon => IsWindows || IsLinux;

    public bool IsCMake =>
        Package.Manifest.BuildSystem.Equals(
            "cmake",
            StringComparison.OrdinalIgnoreCase);

    public bool IsMake =>
        Package.Manifest.BuildSystem.Equals(
            "make",
            StringComparison.OrdinalIgnoreCase);

    public bool IsXmake =>
        Package.Manifest.BuildSystem.Equals(
            "xmake",
            StringComparison.OrdinalIgnoreCase);

    public bool HasKnownBuildSystemIcon => IsCMake || IsMake || IsXmake;

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    public override string ToString() => Name;

    private static string HumanizeVariantId(string value)
    {
        var words = value
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Equals("imgui", StringComparison.OrdinalIgnoreCase)
                ? "ImGui"
                : char.ToUpperInvariant(word[0]) + word[1..]);
        return string.Join(' ', words);
    }
}
