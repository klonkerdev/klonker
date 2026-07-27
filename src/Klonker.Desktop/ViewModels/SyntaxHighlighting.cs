namespace Klonker.Desktop.ViewModels;

public enum SyntaxTokenKind
{
    Plain,
    Keyword,
    Type,
    StringLiteral,
    Number,
    Comment,
    Preprocessor,
    Function,
    Property,
    Heading,
    Operator,
}

public sealed record SyntaxToken(string Text, SyntaxTokenKind Kind);

public static class SyntaxHighlighter
{
    private static readonly HashSet<string> CppKeywords = new(
        [
            "alignas", "alignof", "and", "asm", "auto", "break", "case",
            "catch", "class", "concept", "const", "constexpr", "continue",
            "co_await", "co_return", "co_yield", "default", "delete", "do",
            "else", "enum", "explicit", "export", "extern", "for", "friend",
            "goto", "if", "inline", "namespace", "new", "noexcept", "not",
            "nullptr", "operator", "or", "override", "private", "protected",
            "public", "requires", "return", "sizeof", "static", "struct",
            "switch", "template", "this", "throw", "try", "typedef",
            "typename", "union", "using", "virtual", "volatile", "while",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> CppTypes = new(
        [
            "bool", "char", "char8_t", "char16_t", "char32_t", "double",
            "float", "int", "long", "short", "signed", "size_t", "string",
            "unsigned", "void", "wchar_t",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> CMakeKeywords = new(
        [
            "AND", "COMMAND", "DEFINED", "ELSE", "ELSEIF", "ENDIF",
            "ENDFOREACH", "ENDFUNCTION", "ENDMACRO", "ENDWHILE", "EXISTS",
            "FALSE", "FOREACH", "FUNCTION", "IF", "IN_LIST", "MACRO", "MATCHES",
            "NOT", "OR", "POLICY", "TARGET", "TEST", "TRUE", "WHILE",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> LuaKeywords = new(
        [
            "and", "break", "do", "else", "elseif", "end", "false", "for",
            "function", "goto", "if", "in", "local", "nil", "not", "or",
            "repeat", "return", "then", "true", "until", "while",
        ],
        StringComparer.Ordinal);

    public static IReadOnlyList<SyntaxToken> Highlight(string? text, string? fileName)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var language = DetectLanguage(fileName);
        var tokens = new List<SyntaxToken>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            HighlightLine(lines[index], language, tokens);
            if (index < lines.Length - 1)
            {
                AddToken(tokens, "\n", SyntaxTokenKind.Plain);
            }
        }

        return tokens;
    }

    public static string GetLanguageName(string? fileName) =>
        DetectLanguage(fileName) switch
        {
            SyntaxLanguage.Cpp => "C++",
            SyntaxLanguage.Lua => "Lua",
            SyntaxLanguage.CMake => "CMake",
            SyntaxLanguage.Markdown => "Markdown",
            SyntaxLanguage.Configuration => "Config",
            _ => "Text",
        };

    private static SyntaxLanguage DetectLanguage(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        if (name.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(name).Equals(".cmake", StringComparison.OrdinalIgnoreCase))
        {
            return SyntaxLanguage.CMake;
        }

        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".c" or ".cc" or ".cpp" or ".cxx" or ".h" or ".hh" or ".hpp" or ".hxx" =>
                SyntaxLanguage.Cpp,
            ".lua" => SyntaxLanguage.Lua,
            ".md" or ".markdown" => SyntaxLanguage.Markdown,
            ".json" or ".toml" or ".yaml" or ".yml" or ".xml" =>
                SyntaxLanguage.Configuration,
            _ => SyntaxLanguage.Plain,
        };
    }

    private static void HighlightLine(
        string line,
        SyntaxLanguage language,
        List<SyntaxToken> tokens)
    {
        if (language == SyntaxLanguage.Markdown)
        {
            HighlightMarkdownLine(line, tokens);
            return;
        }

        if (language == SyntaxLanguage.Cpp &&
            line.AsSpan().TrimStart().StartsWith("#", StringComparison.Ordinal))
        {
            AddToken(tokens, line, SyntaxTokenKind.Preprocessor);
            return;
        }

        ScanLine(line, language, tokens);
    }

    private static void HighlightMarkdownLine(string line, List<SyntaxToken> tokens)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            AddToken(tokens, line, SyntaxTokenKind.Preprocessor);
            return;
        }

        if (trimmed.StartsWith('#'))
        {
            AddToken(tokens, line, SyntaxTokenKind.Heading);
            return;
        }

        var index = 0;
        while (index < line.Length)
        {
            if (line[index] == '`')
            {
                var end = line.IndexOf('`', index + 1);
                end = end < 0 ? line.Length - 1 : end;
                AddToken(tokens, line[index..(end + 1)], SyntaxTokenKind.StringLiteral);
                index = end + 1;
                continue;
            }

            if (line[index] is '*' or '_' or '[' or ']' or '(' or ')' or '>')
            {
                AddToken(tokens, line[index].ToString(), SyntaxTokenKind.Operator);
                index++;
                continue;
            }

            var next = index + 1;
            while (next < line.Length &&
                   line[next] != '`' &&
                   line[next] is not ('*' or '_' or '[' or ']' or '(' or ')' or '>'))
            {
                next++;
            }

            AddToken(tokens, line[index..next], SyntaxTokenKind.Plain);
            index = next;
        }
    }

    private static void ScanLine(
        string line,
        SyntaxLanguage language,
        List<SyntaxToken> tokens)
    {
        var index = 0;
        while (index < line.Length)
        {
            if (IsCommentStart(line, index, language))
            {
                AddToken(tokens, line[index..], SyntaxTokenKind.Comment);
                return;
            }

            if (char.IsWhiteSpace(line[index]))
            {
                var end = index + 1;
                while (end < line.Length && char.IsWhiteSpace(line[end]))
                {
                    end++;
                }

                AddToken(tokens, line[index..end], SyntaxTokenKind.Plain);
                index = end;
                continue;
            }

            if (line[index] is '"' or '\'')
            {
                var end = FindStringEnd(line, index);
                AddToken(tokens, line[index..end], SyntaxTokenKind.StringLiteral);
                index = end;
                continue;
            }

            if (char.IsDigit(line[index]))
            {
                var end = index + 1;
                while (end < line.Length &&
                       (char.IsLetterOrDigit(line[end]) || line[end] is '.' or '_'))
                {
                    end++;
                }

                AddToken(tokens, line[index..end], SyntaxTokenKind.Number);
                index = end;
                continue;
            }

            if (char.IsLetter(line[index]) || line[index] is '_' or '$')
            {
                var end = index + 1;
                while (end < line.Length &&
                       (char.IsLetterOrDigit(line[end]) || line[end] is '_' or '$'))
                {
                    end++;
                }

                var word = line[index..end];
                AddToken(tokens, word, ClassifyWord(line, end, word, language));
                index = end;
                continue;
            }

            AddToken(tokens, line[index].ToString(), SyntaxTokenKind.Operator);
            index++;
        }
    }

    private static bool IsCommentStart(string line, int index, SyntaxLanguage language) =>
        language switch
        {
            SyntaxLanguage.Cpp =>
                index + 1 < line.Length && line[index] == '/' && line[index + 1] == '/',
            SyntaxLanguage.Lua =>
                index + 1 < line.Length && line[index] == '-' && line[index + 1] == '-',
            SyntaxLanguage.CMake or SyntaxLanguage.Configuration => line[index] == '#',
            _ => false,
        };

    private static int FindStringEnd(string line, int start)
    {
        var quote = line[start];
        var index = start + 1;
        while (index < line.Length)
        {
            if (line[index] == '\\')
            {
                index = Math.Min(index + 2, line.Length);
                continue;
            }

            if (line[index] == quote)
            {
                return index + 1;
            }

            index++;
        }

        return line.Length;
    }

    private static SyntaxTokenKind ClassifyWord(
        string line,
        int wordEnd,
        string word,
        SyntaxLanguage language)
    {
        if (language == SyntaxLanguage.Cpp)
        {
            if (CppKeywords.Contains(word))
            {
                return SyntaxTokenKind.Keyword;
            }

            if (CppTypes.Contains(word))
            {
                return SyntaxTokenKind.Type;
            }

            return NextNonWhitespace(line, wordEnd) == '('
                ? SyntaxTokenKind.Function
                : SyntaxTokenKind.Plain;
        }

        if (language == SyntaxLanguage.CMake)
        {
            if (CMakeKeywords.Contains(word))
            {
                return SyntaxTokenKind.Keyword;
            }

            return NextNonWhitespace(line, wordEnd) == '('
                ? SyntaxTokenKind.Function
                : SyntaxTokenKind.Plain;
        }

        if (language == SyntaxLanguage.Lua)
        {
            if (LuaKeywords.Contains(word))
            {
                return SyntaxTokenKind.Keyword;
            }

            return NextNonWhitespace(line, wordEnd) == '('
                ? SyntaxTokenKind.Function
                : SyntaxTokenKind.Plain;
        }

        if (language == SyntaxLanguage.Configuration)
        {
            if (word is "true" or "false" or "null")
            {
                return SyntaxTokenKind.Keyword;
            }

            var next = NextNonWhitespace(line, wordEnd);
            return next is '=' or ':'
                ? SyntaxTokenKind.Property
                : SyntaxTokenKind.Plain;
        }

        return SyntaxTokenKind.Plain;
    }

    private static char? NextNonWhitespace(string line, int start)
    {
        for (var index = start; index < line.Length; index++)
        {
            if (!char.IsWhiteSpace(line[index]))
            {
                return line[index];
            }
        }

        return null;
    }

    private static void AddToken(
        List<SyntaxToken> tokens,
        string text,
        SyntaxTokenKind kind)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (tokens.Count > 0 && tokens[^1].Kind == kind)
        {
            var previous = tokens[^1];
            tokens[^1] = previous with { Text = previous.Text + text };
            return;
        }

        tokens.Add(new SyntaxToken(text, kind));
    }

    private enum SyntaxLanguage
    {
        Plain,
        Cpp,
        Lua,
        CMake,
        Markdown,
        Configuration,
    }
}
