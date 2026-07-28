namespace Klonker.Desktop.Services;

public sealed record PrerequisiteProbeResult(
    PrerequisiteProbeState State,
    string Message);
