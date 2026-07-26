using System.Text;
using Klonker.Core.Templates;

namespace Klonker.Core.Tests.TestSupport;

internal sealed class TestPackage : IDisposable
{
    private readonly TemporaryDirectory temporaryDirectory = new();

    public TestPackage(string manifest, IReadOnlyDictionary<string, byte[]>? files = null)
    {
        RootPath = System.IO.Path.Combine(temporaryDirectory.Path, "package");
        Directory.CreateDirectory(System.IO.Path.Combine(RootPath, "content"));
        File.WriteAllText(
            System.IO.Path.Combine(RootPath, "template.toml"),
            manifest,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (files is null)
        {
            return;
        }

        foreach (var file in files)
        {
            var fullPath = System.IO.Path.Combine(
                RootPath,
                "content",
                file.Key.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, file.Value);
        }
    }

    public string RootPath { get; }

    public TemplatePackage Load()
    {
        var result = TemplatePackageLoader.Load(RootPath);
        Assert.True(
            result.IsSuccess,
            string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        return result.Value!;
    }

    public void Dispose() => temporaryDirectory.Dispose();

    public static byte[] Text(string text) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
}
