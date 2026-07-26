using Klonker.Core.Generation;

namespace Klonker.Desktop.Services;

public sealed class CoreProjectGenerationService : IProjectGenerationService
{
    public Task<GenerationResult> GenerateAsync(
        GenerationPlan plan,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        GenerationExecutor.ExecuteAsync(plan, destinationPath, cancellationToken);
}
