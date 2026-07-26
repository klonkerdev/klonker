using System.Collections.Immutable;

namespace Klonker.Core.Templates;

public sealed record ResolvedParameters
{
    public ResolvedParameters(IEnumerable<KeyValuePair<string, object>> values)
    {
        Values = values.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    public ImmutableDictionary<string, object> Values { get; }
}
