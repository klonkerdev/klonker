using System.Collections.Immutable;

namespace Klonker.Desktop.Services;

public sealed record WslDistributionSnapshot(
    ImmutableArray<WslDistribution> Distributions);
