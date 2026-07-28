using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Klonker.Core.Diagnostics;

namespace Klonker.Desktop.Services;

public sealed class CatalogTabStore
{
    private const int SchemaVersion = 0;
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public CatalogTabStore(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        Path = System.IO.Path.Combine(
            System.IO.Path.GetFullPath(applicationDataRoot),
            "catalog-tabs.json");
    }

    public string Path { get; }

    public OperationResult<CatalogTabSnapshot> Load()
    {
        if (!File.Exists(Path))
        {
            return new OperationResult<CatalogTabSnapshot>(
                new CatalogTabSnapshot([], Path),
                []);
        }

        try
        {
            var dto = JsonSerializer.Deserialize<SnapshotDto>(
                File.ReadAllText(Path, Utf8),
                JsonOptions);
            if (dto is null || dto.SchemaVersion != SchemaVersion)
            {
                return Failure(
                    "catalog_tabs.schema_invalid",
                    "Personal catalog tabs use an unsupported settings schema.");
            }

            var issues = new List<ValidationIssue>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var tabs = ImmutableArray.CreateBuilder<CatalogTabDefinition>();
            foreach (var item in dto.Tabs ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Id) ||
                    string.IsNullOrWhiteSpace(item.Name) ||
                    item.Name.Length > 40 ||
                    !Enum.TryParse<CatalogTabKind>(
                        item.Kind,
                        ignoreCase: false,
                        out var kind) ||
                    !ids.Add(item.Id))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "catalog_tabs.entry_ignored",
                        "An invalid personal catalog tab was ignored."));
                    continue;
                }

                tabs.Add(new CatalogTabDefinition(
                    item.Id,
                    item.Name,
                    kind,
                    (item.ItemIdentities ?? [])
                        .Where(identity => !string.IsNullOrWhiteSpace(identity))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToImmutableArray()));
            }

            return new OperationResult<CatalogTabSnapshot>(
                new CatalogTabSnapshot(tabs.ToImmutable(), Path),
                issues);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException)
        {
            return Failure(
                "catalog_tabs.read_failed",
                $"Personal catalog tabs could not be loaded: {exception.Message}");
        }
    }

    public OperationResult<CatalogTabSnapshot> Save(
        IEnumerable<CatalogTabDefinition> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var ordered = tabs
            .OrderBy(tab => tab.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tab => tab.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        var dto = new SnapshotDto
        {
            SchemaVersion = SchemaVersion,
            Tabs = ordered.Select(tab => new TabDto
            {
                Id = tab.Id,
                Name = tab.Name,
                Kind = tab.Kind.ToString(),
                ItemIdentities = tab.ItemIdentities.ToArray(),
            }).ToList(),
        };

        var temporary = $"{Path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(dto, JsonOptions) + "\n",
                Utf8);
            File.Move(temporary, Path, overwrite: true);
            return new OperationResult<CatalogTabSnapshot>(
                new CatalogTabSnapshot(ordered, Path),
                []);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "catalog_tabs.write_failed",
                $"Personal catalog tabs could not be saved: {exception.Message}");
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private OperationResult<CatalogTabSnapshot> Failure(
        string code,
        string message) =>
        new(
            null,
            [
                new ValidationIssue(
                    ValidationSeverity.Error,
                    code,
                    message,
                    Path: Path),
            ]);

    private sealed class SnapshotDto
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("tabs")]
        public List<TabDto>? Tabs { get; init; } = [];
    }

    private sealed class TabDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("kind")]
        public string? Kind { get; init; }

        [JsonPropertyName("item_identities")]
        public string[]? ItemIdentities { get; init; } = [];
    }
}
