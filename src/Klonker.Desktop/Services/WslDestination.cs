namespace Klonker.Desktop.Services;

public sealed record WslDestination(
    string DistributionName,
    string LinuxPath,
    string WindowsUncPath);
