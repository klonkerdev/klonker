using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Modules;

namespace Klonker.Desktop.Services;

public interface IWslGenerationService
{
    Task<OperationResult<WslDistributionSnapshot>> DiscoverRunningAsync(
        CancellationToken cancellationToken = default);

    OperationResult<WslDestination> ResolveDestination(
        string distributionName,
        string linuxPath);

    Task<GenerationResult> GenerateProjectAsync(
        GenerationPlan plan,
        string distributionName,
        string linuxPath,
        CancellationToken cancellationToken = default);

    Task<GenerationResult> GenerateModuleAsync(
        ModuleGenerationPlan plan,
        string distributionName,
        string linuxPath,
        CancellationToken cancellationToken = default);
}
