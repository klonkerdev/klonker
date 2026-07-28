using Klonker.Core.Authoring;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;

namespace Klonker.Desktop.Services;

public sealed class CoreTemplateAuthoringService : ITemplateAuthoringService
{
    public ExistingTemplateInspection Inspect(string path) =>
        ExistingTemplateInspector.Inspect(path);

    public OperationResult<string> ValidateDestination(
        string path,
        string? inspectedSourcePath = null) =>
        TemplateAuthoringDestinationValidator.Validate(
            path,
            inspectedSourcePath);

    public OperationResult<GenerationPlan> CreatePlan(
        TemplateAuthoringRequest request) =>
        TemplateAuthoringPlanner.CreatePlan(request);

    public Task<GenerationResult> GenerateAsync(
        GenerationPlan plan,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        GenerationExecutor.ExecuteAsync(
            plan,
            destinationPath,
            cancellationToken);
}
