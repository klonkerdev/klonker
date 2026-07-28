using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Klonker.Core.Diagnostics;

namespace Klonker.Core.Registry;

public static partial class RegistryVersionSelector
{
    public static OperationResult<RegistryModuleVersionSelectionResult> SelectModules(
        IEnumerable<RegistryModulePackage> packages,
        RegistryVersionPreference preference,
        IReadOnlyDictionary<string, string>? pins = null)
    {
        ArgumentNullException.ThrowIfNull(packages);
        pins ??= new Dictionary<string, string>();

        var issues = new List<ValidationIssue>();
        var selections = ImmutableArray.CreateBuilder<RegistryModuleVersionSelection>();
        foreach (var group in packages
                     .GroupBy(
                         package => $"{package.RegistryId}:{package.Entry.ModuleId}",
                         StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var candidates = group
                .OrderByDescending(
                    package => package.Entry.Version,
                    RegistryVersionComparer.Instance)
                .ThenBy(package => package.Entry.PackageSha256, StringComparer.Ordinal)
                .ToImmutableArray();
            var selected = SelectModuleByPreference(candidates, preference);
            var reason = preference == RegistryVersionPreference.LatestStable
                ? "Newest stable semantic version; prerelease is used only when no stable version exists."
                : "Newest semantic version including prereleases.";

            if (pins.TryGetValue(group.Key, out var pinnedVersion))
            {
                var pinned = candidates.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Entry.Version,
                        pinnedVersion,
                        StringComparison.Ordinal));
                if (pinned is not null)
                {
                    selected = pinned;
                    reason = $"Pinned explicitly to {pinnedVersion}.";
                }
                else
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "registry.version_pin_unavailable",
                        $"Pinned version '{pinnedVersion}' for module '{group.Key}' is unavailable. " +
                        $"Klonker selected '{selected.Entry.Version}' using the configured fallback policy."));
                }
            }

            foreach (var candidate in candidates.Where(candidate =>
                         !SemanticVersion.TryParse(candidate.Entry.Version, out _)))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "registry.version_non_semantic",
                    $"Module '{group.Key}' uses non-semantic version '{candidate.Entry.Version}'. " +
                    "It is ordered deterministically after semantic versions."));
            }

            selections.Add(new RegistryModuleVersionSelection(
                group.Key,
                selected,
                candidates,
                reason));
        }

        return new OperationResult<RegistryModuleVersionSelectionResult>(
            new RegistryModuleVersionSelectionResult(selections.ToImmutable()),
            issues);
    }

    public static OperationResult<RegistryVersionSelectionResult> Select(
        IEnumerable<RegistryTemplatePackage> packages,
        RegistryVersionPreference preference,
        IReadOnlyDictionary<string, string>? pins = null)
    {
        ArgumentNullException.ThrowIfNull(packages);
        pins ??= new Dictionary<string, string>();

        var issues = new List<ValidationIssue>();
        var selections = ImmutableArray.CreateBuilder<RegistryTemplateVersionSelection>();
        foreach (var group in packages
                     .GroupBy(
                         package => $"{package.RegistryId}:{package.Entry.TemplateId}",
                         StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var candidates = group
                .OrderByDescending(
                    package => package.Entry.Version,
                    RegistryVersionComparer.Instance)
                .ThenBy(
                    package => package.Entry.PackageSha256,
                    StringComparer.Ordinal)
                .ToImmutableArray();
            RegistryTemplatePackage selected;
            string reason;

            if (pins.TryGetValue(group.Key, out var pinnedVersion))
            {
                selected = candidates.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Entry.Version,
                        pinnedVersion,
                        StringComparison.Ordinal))!;
                if (selected is null)
                {
                    selected = SelectByPreference(candidates, preference);
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "registry.version_pin_unavailable",
                        $"Pinned version '{pinnedVersion}' for '{group.Key}' is unavailable. " +
                        $"Klonker selected '{selected.Entry.Version}' using the configured fallback policy."));
                    reason = "The configured pin was unavailable; the fallback policy was used.";
                }
                else
                {
                    reason = $"Pinned explicitly to {pinnedVersion}.";
                }
            }
            else
            {
                selected = SelectByPreference(candidates, preference);
                reason = preference == RegistryVersionPreference.LatestStable
                    ? "Newest stable semantic version; prerelease is used only when no stable version exists."
                    : "Newest semantic version including prereleases.";
            }

            foreach (var candidate in candidates.Where(candidate =>
                         !SemanticVersion.TryParse(candidate.Entry.Version, out _)))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "registry.version_non_semantic",
                    $"Template '{group.Key}' uses non-semantic version '{candidate.Entry.Version}'. " +
                    "It is ordered deterministically after semantic versions."));
            }

            selections.Add(new RegistryTemplateVersionSelection(
                group.Key,
                selected,
                candidates,
                reason));
        }

        return new OperationResult<RegistryVersionSelectionResult>(
            new RegistryVersionSelectionResult(selections.ToImmutable()),
            issues);
    }

    private static RegistryTemplatePackage SelectByPreference(
        ImmutableArray<RegistryTemplatePackage> candidates,
        RegistryVersionPreference preference)
    {
        if (preference == RegistryVersionPreference.LatestStable)
        {
            var stable = candidates.FirstOrDefault(candidate =>
                SemanticVersion.TryParse(candidate.Entry.Version, out var parsed) &&
                !parsed.IsPrerelease);
            if (stable is not null)
            {
                return stable;
            }
        }

        return candidates[0];
    }

    private static RegistryModulePackage SelectModuleByPreference(
        ImmutableArray<RegistryModulePackage> candidates,
        RegistryVersionPreference preference)
    {
        if (preference == RegistryVersionPreference.LatestStable)
        {
            var stable = candidates.FirstOrDefault(candidate =>
                SemanticVersion.TryParse(candidate.Entry.Version, out var parsed) &&
                !parsed.IsPrerelease);
            if (stable is not null)
            {
                return stable;
            }
        }

        return candidates[0];
    }

    private sealed class RegistryVersionComparer : IComparer<string>
    {
        public static RegistryVersionComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var leftParsed = SemanticVersion.TryParse(left, out var leftVersion);
            var rightParsed = SemanticVersion.TryParse(right, out var rightVersion);
            if (leftParsed && rightParsed)
            {
                return leftVersion.CompareTo(rightVersion);
            }

            if (leftParsed != rightParsed)
            {
                return leftParsed ? 1 : -1;
            }

            return StringComparer.Ordinal.Compare(left, right);
        }
    }

    private readonly record struct SemanticVersion(
        int Major,
        int Minor,
        int Patch,
        ImmutableArray<PrereleasePart> Prerelease)
        : IComparable<SemanticVersion>
    {
        public bool IsPrerelease => !Prerelease.IsDefaultOrEmpty;

        public static bool TryParse(string value, out SemanticVersion version)
        {
            var match = SemanticVersionPattern().Match(value);
            if (!match.Success ||
                !int.TryParse(
                    match.Groups["major"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var major) ||
                !int.TryParse(
                    match.Groups["minor"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var minor) ||
                !int.TryParse(
                    match.Groups["patch"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var patch))
            {
                version = default;
                return false;
            }

            var prerelease = match.Groups["pre"].Success
                ? match.Groups["pre"].Value
                    .Split('.')
                    .Select(PrereleasePart.Parse)
                    .ToImmutableArray()
                : [];
            version = new SemanticVersion(major, minor, patch, prerelease);
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            var result = Major.CompareTo(other.Major);
            if (result != 0)
            {
                return result;
            }

            result = Minor.CompareTo(other.Minor);
            if (result != 0)
            {
                return result;
            }

            result = Patch.CompareTo(other.Patch);
            if (result != 0)
            {
                return result;
            }

            if (IsPrerelease != other.IsPrerelease)
            {
                return IsPrerelease ? -1 : 1;
            }

            for (var index = 0;
                 index < Math.Min(Prerelease.Length, other.Prerelease.Length);
                 index++)
            {
                result = Prerelease[index].CompareTo(other.Prerelease[index]);
                if (result != 0)
                {
                    return result;
                }
            }

            return Prerelease.Length.CompareTo(other.Prerelease.Length);
        }
    }

    private readonly record struct PrereleasePart(
        bool Numeric,
        int Number,
        string Text) : IComparable<PrereleasePart>
    {
        public static PrereleasePart Parse(string value) =>
            int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number)
                ? new PrereleasePart(true, number, value)
                : new PrereleasePart(false, 0, value);

        public int CompareTo(PrereleasePart other)
        {
            if (Numeric != other.Numeric)
            {
                return Numeric ? -1 : 1;
            }

            return Numeric
                ? Number.CompareTo(other.Number)
                : StringComparer.Ordinal.Compare(Text, other.Text);
        }
    }

    [GeneratedRegex(
        @"\A(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();
}
