using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace Klonker.Core.Templates;

public static partial class StringHelpers
{
    private static readonly ImmutableHashSet<string> CppKeywords =
        new[]
        {
            "alignas", "alignof", "and", "and_eq", "asm", "atomic_cancel",
            "atomic_commit", "atomic_noexcept", "auto", "bitand", "bitor", "bool",
            "break", "case", "catch", "char", "char8_t", "char16_t", "char32_t",
            "class", "compl", "concept", "const", "consteval", "constexpr",
            "constinit", "const_cast", "continue", "co_await", "co_return",
            "co_yield", "decltype", "default", "delete", "do", "double",
            "dynamic_cast", "else", "enum", "explicit", "export", "extern",
            "false", "float", "for", "friend", "goto", "if", "inline", "int",
            "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq",
            "nullptr", "operator", "or", "or_eq", "private", "protected", "public",
            "reflexpr", "register", "reinterpret_cast", "requires", "return",
            "short", "signed", "sizeof", "static", "static_assert", "static_cast",
            "struct", "switch", "synchronized", "template", "this", "thread_local",
            "throw", "true", "try", "typedef", "typeid", "typename", "union",
            "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while",
            "xor", "xor_eq",
        }.ToImmutableHashSet(StringComparer.Ordinal);

    public static string LowerCase(string value) =>
        value?.ToLowerInvariant() ?? string.Empty;

    public static string UpperCase(string value) =>
        value?.ToUpperInvariant() ?? string.Empty;

    public static string SnakeCase(string value) =>
        string.Join('_', GetWords(value).Select(word => word.ToLowerInvariant()));

    public static string KebabCase(string value) =>
        string.Join('-', GetWords(value).Select(word => word.ToLowerInvariant()));

    public static string PascalCase(string value)
    {
        var result = new StringBuilder();
        foreach (var word in GetWords(value))
        {
            result.Append(char.ToUpperInvariant(word[0]));
            result.Append(word.AsSpan(1).ToString().ToLowerInvariant());
        }

        return result.ToString();
    }

    public static string CppIdentifier(string value)
    {
        value ??= string.Empty;
        var result = new StringBuilder(value.Length + 1);
        var previousWasUnderscore = false;

        foreach (var character in value)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character == '_';
            var output = allowed ? character : '_';
            if (output == '_' && previousWasUnderscore)
            {
                continue;
            }

            result.Append(output);
            previousWasUnderscore = output == '_';
        }

        var identifier = result.ToString().Trim('_');
        if (identifier.Length == 0)
        {
            identifier = "_";
        }
        else if (char.IsAsciiDigit(identifier[0]))
        {
            identifier = $"_{identifier}";
        }

        return CppKeywords.Contains(identifier) ? $"_{identifier}" : identifier;
    }

    public static bool IsValidCppIdentifier(string value) =>
        !string.IsNullOrEmpty(value) &&
        CppIdentifierPattern().IsMatch(value) &&
        !CppKeywords.Contains(value);

    private static IEnumerable<string> GetWords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return WordPattern()
            .Matches(value)
            .Select(match => match.Value);
    }

    [GeneratedRegex(
        "[A-Z]+(?=[A-Z][a-z]|[0-9]|\\b)|[A-Z]?[a-z]+|[0-9]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CppIdentifierPattern();
}
