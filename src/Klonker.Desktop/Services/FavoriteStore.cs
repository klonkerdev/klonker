using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Klonker.Core.Diagnostics;

namespace Klonker.Desktop.Services;

public sealed class FavoriteStore : IFavoriteStore
{
    public const int SupportedSchemaVersion = 0;

    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private readonly Lock syncRoot = new();
    private readonly string applicationDataRoot;

    public FavoriteStore(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        this.applicationDataRoot = Path.GetFullPath(applicationDataRoot);
    }

    public static FavoriteStore CreateDefault() =>
        new(GetDefaultApplicationDataRoot());

    public OperationResult<FavoriteSnapshot> Load()
    {
        lock (syncRoot)
        {
            return LoadCore();
        }
    }

    public OperationResult<FavoriteSnapshot> SetFavorite(
        string templateIdentity,
        bool isFavorite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateIdentity);

        lock (syncRoot)
        {
            var loaded = LoadCore();
            if (!loaded.IsSuccess)
            {
                return loaded;
            }

            var identities = loaded.Value!.TemplateIdentities.ToHashSet(
                StringComparer.Ordinal);
            if (isFavorite)
            {
                identities.Add(templateIdentity);
            }
            else
            {
                identities.Remove(templateIdentity);
            }

            return SaveCore(identities);
        }
    }

    public OperationResult<FavoriteSnapshot> Reset()
    {
        lock (syncRoot)
        {
            return SaveCore([]);
        }
    }

    internal static string GetDefaultApplicationDataRoot()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "Klonker");
    }

    private OperationResult<FavoriteSnapshot> LoadCore()
    {
        var storagePath = GetStoragePath();
        try
        {
            Directory.CreateDirectory(applicationDataRoot);
            if (!File.Exists(storagePath))
            {
                return SaveCore([]);
            }

            var dto = JsonSerializer.Deserialize<FavoriteDto>(
                File.ReadAllText(storagePath),
                ReadOptions);
            if (dto is null)
            {
                return Failure(
                    "favorites.empty",
                    "The local favorites file is empty.",
                    storagePath);
            }

            if (dto.TemplateIdentities is null)
            {
                return Failure(
                    "favorites.identities_required",
                    "The local favorites property 'favorite_template_ids' must be an array.",
                    storagePath);
            }

            var issues = new List<ValidationIssue>();
            if (dto.SchemaVersion != SupportedSchemaVersion)
            {
                issues.Add(Error(
                    "favorites.schema_unsupported",
                    $"Favorites schema version {dto.SchemaVersion} is not supported.",
                    storagePath));
            }

            var identities = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var identity in dto.TemplateIdentities)
            {
                if (string.IsNullOrWhiteSpace(identity) ||
                    identity.Any(char.IsControl))
                {
                    issues.Add(Error(
                        "favorites.identity_invalid",
                        "Favorite template identities must be non-empty visible strings.",
                        storagePath));
                    continue;
                }

                if (!seen.Add(identity))
                {
                    issues.Add(Error(
                        "favorites.identity_duplicate",
                        $"Favorite template identity '{identity}' is duplicated.",
                        storagePath));
                    continue;
                }

                identities.Add(identity);
            }

            if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
            {
                return new OperationResult<FavoriteSnapshot>(null, issues);
            }

            return new OperationResult<FavoriteSnapshot>(
                new FavoriteSnapshot(
                    storagePath,
                    identities
                        .Order(StringComparer.Ordinal)
                        .ToImmutableArray()),
                issues);
        }
        catch (JsonException exception)
        {
            return Failure(
                "favorites.json_invalid",
                $"The local favorites file is invalid JSON: {exception.Message}",
                storagePath);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
        {
            return Failure(
                "favorites.read_failed",
                $"The local favorites could not be read: {exception.Message}",
                storagePath);
        }
    }

    private OperationResult<FavoriteSnapshot> SaveCore(
        IEnumerable<string> templateIdentities)
    {
        var storagePath = GetStoragePath();
        var identities = templateIdentities
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var dto = new FavoriteDto
        {
            SchemaVersion = SupportedSchemaVersion,
            TemplateIdentities = identities.ToList(),
        };

        try
        {
            Directory.CreateDirectory(applicationDataRoot);
            WriteAtomically(
                storagePath,
                JsonSerializer.Serialize(dto, WriteOptions) + Environment.NewLine);
            return new OperationResult<FavoriteSnapshot>(
                new FavoriteSnapshot(storagePath, identities),
                []);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
        {
            return Failure(
                "favorites.write_failed",
                $"The local favorites could not be saved: {exception.Message}",
                storagePath);
        }
    }

    private string GetStoragePath() =>
        Path.Combine(applicationDataRoot, "favorites.json");

    private static void WriteAtomically(string path, string content)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, Utf8);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static OperationResult<FavoriteSnapshot> Failure(
        string code,
        string message,
        string path) =>
        new(null, [Error(code, message, path)]);

    private static ValidationIssue Error(
        string code,
        string message,
        string path) =>
        new(ValidationSeverity.Error, code, message, Path: path);

    private sealed class FavoriteDto
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("favorite_template_ids")]
        public List<string>? TemplateIdentities { get; set; } = [];
    }
}
