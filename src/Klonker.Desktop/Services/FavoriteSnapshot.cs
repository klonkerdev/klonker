using System.Collections.Immutable;

namespace Klonker.Desktop.Services;

public sealed record FavoriteSnapshot(
    string StoragePath,
    ImmutableArray<string> TemplateIdentities)
{
    public bool Contains(string templateIdentity) =>
        TemplateIdentities.Contains(templateIdentity, StringComparer.Ordinal);
}
