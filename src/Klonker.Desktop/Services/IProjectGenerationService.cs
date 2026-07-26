using Klonker.Core.Generation;

namespace Klonker.Desktop.Services;

public interface IProjectGenerationService
{
    Task<GenerationResult> GenerateAsync(
        GenerationPlan plan,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
