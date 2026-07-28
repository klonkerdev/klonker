using System.Collections.Immutable;

namespace Klonker.Core.Modules;

public sealed record ModuleLicenseReport(
    string ModuleLicense,
    ImmutableArray<ModuleDependency> Dependencies)
{
    public ImmutableArray<string> Licenses =>
        Dependencies
            .Select(dependency => dependency.License)
            .Prepend(ModuleLicense)
            .Where(license => !string.IsNullOrWhiteSpace(license))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

    public string Summary =>
        Dependencies.IsDefaultOrEmpty
            ? $"Generated module sources: {ModuleLicense}"
            : $"Generated module sources: {ModuleLicense}. Dependencies: " +
              string.Join(
                  "; ",
                  Dependencies
                      .OrderBy(dependency => dependency.Id, StringComparer.Ordinal)
                      .Select(dependency =>
                          $"{dependency.Name} {dependency.Version} ({dependency.License})"));
}
