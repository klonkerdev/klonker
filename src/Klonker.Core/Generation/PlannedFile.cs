using System.Collections.Immutable;

namespace Klonker.Core.Generation;

public sealed record PlannedFile(
    string RelativePath,
    ImmutableArray<byte> Content,
    bool IsText,
    string? TextContent,
    string SourceTemplatePath);
