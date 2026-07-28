namespace Klonker.Desktop.Services;

public interface IPrerequisiteProbeService
{
    Task<PrerequisiteProbeResult> ProbeAsync(
        string prerequisiteId,
        CancellationToken cancellationToken = default);
}
