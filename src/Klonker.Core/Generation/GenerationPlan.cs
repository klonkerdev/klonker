using System.Collections.Immutable;
using Klonker.Core.Diagnostics;

namespace Klonker.Core.Generation;

public sealed record GenerationPlan(
    TemplateIdentity Template,
    ImmutableArray<string> Directories,
    ImmutableArray<PlannedFile> Files,
    ImmutableArray<ValidationIssue> Messages);
