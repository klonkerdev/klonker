using Klonker.Core.Generation;
using Klonker.Core.Templates;
using Klonker.Core.Tests.TestSupport;

namespace Klonker.Core.Tests;

public sealed class RenderingTests
{
    [Fact]
    public void Render_InterpolatesDeclaredValue()
    {
        var parameters = Parameters(("name", "Klonker"));

        var result = RestrictedTemplateRenderer.Render(
            "Hello {{ name }}!",
            "greeting.txt.sbn",
            parameters);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello Klonker!", result.Value);
    }

    [Fact]
    public void Render_ProvidesOnlyDeterministicKlonkerHelpers()
    {
        var parameters = Parameters(("name", "HTTP Server 2"));
        const string template = """
            {{ name | lower_case }}
            {{ name | upper_case }}
            {{ name | snake_case }}
            {{ name | kebab_case }}
            {{ name | pascal_case }}
            {{ "9 bad-name" | cpp_identifier }}
            """;

        var result = RestrictedTemplateRenderer.Render(
            template,
            "helpers.txt.sbn",
            parameters);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "http server 2\nHTTP SERVER 2\nhttp_server_2\nhttp-server-2\nHttpServer2\n_9_bad_name",
            result.Value!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Render_MissingVariable_ReturnsSourceAwareError()
    {
        var result = RestrictedTemplateRenderer.Render(
            "{{ missing }}",
            "missing.txt.sbn",
            Parameters());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "template.render" &&
            issue.Message.Contains("missing.txt.sbn", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_MalformedTemplate_ReturnsParseError()
    {
        var result = RestrictedTemplateRenderer.Render(
            "{{ if true }}",
            "broken.txt.sbn",
            Parameters());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == "template.parse");
    }

    [Fact]
    public void Render_TemplateDefinedFunction_IsRejected()
    {
        var result = RestrictedTemplateRenderer.Render(
            "{{ func answer; ret 42; end }}{{ answer }}",
            "function.txt.sbn",
            Parameters());

        Assert.Contains(
            result.Issues,
            issue => issue.Code == "template.function_not_allowed");
    }

    [Fact]
    public async Task Plan_TemplatedFilename_RemovesSbnSuffix()
    {
        using var package = new TestPackage(
            TestManifests.Valid,
            new Dictionary<string, byte[]>
            {
                ["{{ project_name }}.txt.sbn"] = TestPackage.Text("{{ project_name }}"),
            });

        var result = await TemplatePlanner.CreatePlanAsync(
            package.Load(),
            new Dictionary<string, object?> { ["project_name"] = "Demo" });

        Assert.True(result.IsSuccess);
        var file = Assert.Single(result.Value!.Files);
        Assert.Equal("Demo.txt", file.RelativePath);
        Assert.Equal("Demo", file.TextContent);
    }

    [Fact]
    public async Task Plan_NonSbnFile_CopiesBytesExactly()
    {
        byte[] bytes = [0, 1, 2, 254, 255];
        using var package = new TestPackage(
            TestManifests.Valid,
            new Dictionary<string, byte[]> { ["image.bin"] = bytes });

        var result = await TemplatePlanner.CreatePlanAsync(package.Load(), null);

        Assert.True(result.IsSuccess);
        var file = Assert.Single(result.Value!.Files);
        Assert.False(file.IsText);
        Assert.Equal(bytes, file.Content);
    }

    private static ResolvedParameters Parameters(
        params (string Key, object Value)[] values) =>
        new(values.Select(value =>
            new KeyValuePair<string, object>(value.Key, value.Value)));
}
