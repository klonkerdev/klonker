using CommunityToolkit.Mvvm.ComponentModel;
using Klonker.Core.Modules;
using Klonker.Core.Registry;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed partial class ModuleListItemViewModel : ViewModelBase
{
    public ModuleListItemViewModel(
        RegistryModulePackage module,
        bool isFavorite = false,
        TemplateTagPalette? tagPalette = null)
    {
        Module = module;
        IsFavorite = isFavorite;
        tagPalette ??= new TemplateTagPalette();
        TagChips = Tags
            .Select(tag => new TemplateTagViewModel(tag, tagPalette))
            .ToArray();
    }

    public RegistryModulePackage Module { get; }

    public ModulePackage Package => Module.Package;

    public string RegistryId => Module.RegistryId;

    public string RegistryName => Module.RegistryDisplayName;

    public string ModuleId => Module.Entry.ModuleId;

    public string QualifiedId => Module.QualifiedId;

    public string FavoriteIdentity =>
        $"module:{Module.RegistryId}:{Module.Entry.ModuleId}";

    public string Name => Package.Manifest.Name;

    public string Description => Package.Manifest.Description;

    public string Version => $"v{Package.Manifest.Version}";

    public string LanguageId => Package.Manifest.Language;

    public string Language => LanguageId switch
    {
        "cpp" => "C++",
        "csharp" => "C#",
        "lua" => "Lua",
        _ => Humanize(LanguageId),
    };

    public IReadOnlyList<string> Tags =>
        Package.Manifest.Tags.IsDefault ? [] : Package.Manifest.Tags;

    public IReadOnlyList<TemplateTagViewModel> TagChips { get; }

    public bool HasTags => Tags.Count > 0;

    public string SlotSummary =>
        Package.Manifest.Slots.Length == 0
            ? "No configurable slots"
            : $"{Package.Manifest.Slots.Length} destination slot" +
              (Package.Manifest.Slots.Length == 1 ? string.Empty : "s");

    public string DependencySummary =>
        Package.Manifest.Dependencies.Length == 0
            ? "No declared dependencies"
            : $"{Package.Manifest.Dependencies.Length} declared " +
              (Package.Manifest.Dependencies.Length == 1
                  ? "dependency"
                  : "dependencies");

    public bool HasPostGenerationInstructions =>
        !string.IsNullOrWhiteSpace(
            Package.Manifest.PostGenerationInstructions);

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    public bool Matches(string search, string language, string tag) =>
        (language == MainViewModel.AllLanguages || Language == language) &&
        (tag == MainViewModel.AllTags ||
         Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) &&
        (search.Length == 0 ||
         Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
         Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
         ModuleId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
         RegistryName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
         Tags.Any(value =>
             value.Contains(search, StringComparison.OrdinalIgnoreCase)));

    private static string Humanize(string value) =>
        string.Join(
            ' ',
            value
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(word =>
                    char.ToUpperInvariant(word[0]) + word[1..]));
}
