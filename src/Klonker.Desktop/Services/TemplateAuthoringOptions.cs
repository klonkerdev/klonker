using System.Collections.Immutable;

namespace Klonker.Desktop.Services;

public sealed record TemplateAuthoringOptions(
    int SchemaVersion,
    ImmutableArray<TemplateLicenseOption> Licenses,
    ImmutableArray<TemplatePlatformOption> Platforms,
    ImmutableArray<TemplateBuildSystemOption> BuildSystems,
    ImmutableArray<TemplateLanguageOption> Languages);

public sealed record TemplateLicenseOption(
    string Id,
    string Name,
    string SourceLicense,
    string Summary);

public sealed record TemplatePlatformOption(
    string Id,
    string Name,
    string Description);

public sealed record TemplateBuildSystemOption(
    string Id,
    string Name,
    string Description,
    ImmutableArray<TemplateSeedFileOption> SeedFiles);

public sealed record TemplateLanguageOption(
    string Id,
    string Name,
    string Description,
    ImmutableArray<string> BuildSystems,
    ImmutableArray<TemplateSeedFileOption> SeedFiles);

public sealed record TemplateSeedFileOption(
    string Path,
    string Content);
