namespace Klonker.Core.Templates;

public sealed record TemplateSourceFile(
    string RelativePath,
    string FullPath,
    bool IsTextTemplate);
