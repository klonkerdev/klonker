using System.Collections.Immutable;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;

namespace Klonker.Core.Modules;

public sealed record ModuleGenerationPlan(
    string RegistryId,
    string ModuleId,
    string Version,
    GenerationPlan FilePlan,
    ImmutableDictionary<string, string> Slots,
    ModuleLicenseReport LicenseReport,
    string? PostGenerationInstructions,
    ImmutableArray<ValidationIssue> Messages);
