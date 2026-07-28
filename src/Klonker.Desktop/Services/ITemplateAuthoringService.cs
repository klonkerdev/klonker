using Klonker.Core.Authoring;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;

namespace Klonker.Desktop.Services;

public interface ITemplateAuthoringService
{
    ExistingTemplateInspection Inspect(string path);

    OperationResult<string> ValidateDestination(
        string path,
        string? inspectedSourcePath = null);

    OperationResult<GenerationPlan> CreatePlan(
        TemplateAuthoringRequest request);

    Task<GenerationResult> GenerateAsync(
        GenerationPlan plan,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
