using System.Collections.Immutable;
using Klonker.Core.Diagnostics;
using Klonker.Core.Registry;
using Klonker.Core.Templates;

namespace Klonker.Desktop.Services;

public sealed class LocalSampleTemplateCatalog : ITemplateCatalog
{
    public OperationResult<TemplateCatalogSnapshot> Load()
    {
        var registryPath = DevelopmentSampleRegistryLocator.FindRegistryIndex();
        if (registryPath is null)
        {
            return new OperationResult<TemplateCatalogSnapshot>(
                null,
                [
                    new ValidationIssue(
                        ValidationSeverity.Error,
                        "samples.not_found",
                        "The development sample registry could not be found. Run Klonker from inside the repository."),
                ]);
        }

        var registryResult = LocalRegistryLoader.Load(registryPath);
        if (!registryResult.IsSuccess)
        {
            return new OperationResult<TemplateCatalogSnapshot>(null, registryResult.Issues);
        }

        var issues = new List<ValidationIssue>(registryResult.Issues);
        var packages = ImmutableArray.CreateBuilder<TemplatePackage>();
        foreach (var entry in registryResult.Value!.Templates)
        {
            var packagePath = LocalRegistryLoader.ResolvePackagePath(
                registryResult.Value,
                entry);
            issues.AddRange(packagePath.Issues);
            if (!packagePath.IsSuccess)
            {
                continue;
            }

            var packageResult = TemplatePackageLoader.Load(packagePath.Value!);
            issues.AddRange(packageResult.Issues);
            if (!packageResult.IsSuccess)
            {
                continue;
            }

            var manifest = packageResult.Value!.Manifest;
            if (!string.Equals(manifest.Id, entry.TemplateId, StringComparison.Ordinal) ||
                !string.Equals(manifest.FamilyId, entry.FamilyId, StringComparison.Ordinal) ||
                !string.Equals(manifest.VariantId, entry.VariantId, StringComparison.Ordinal) ||
                !string.Equals(manifest.Version, entry.Version, StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "registry.package_identity_mismatch",
                    $"Registry entry '{entry.TemplateId}' does not match its package manifest."));
                continue;
            }

            packages.Add(packageResult.Value);
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<TemplateCatalogSnapshot>(null, issues);
        }

        return new OperationResult<TemplateCatalogSnapshot>(
            new TemplateCatalogSnapshot(packages.ToImmutable()),
            issues);
    }
}
