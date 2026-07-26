using System.Collections.Immutable;
using Klonker.Core.Templates;

namespace Klonker.Core.Tests;

public sealed class ParameterValidationTests
{
    [Fact]
    public void Resolve_RequiredStringMissing_ReturnsError()
    {
        var manifest = CreateManifest(
            new TemplateParameterDefinition(
                "name",
                TemplateParameterType.Text,
                "Name",
                null,
                Required: true,
                DefaultValue: null,
                Validation: null,
                Values: []));

        var result = ParameterResolver.Resolve(manifest, null);

        Assert.Contains(result.Issues, issue => issue.Code == "parameter.required");
    }

    [Fact]
    public void Resolve_ValidCppIdentifier_Succeeds()
    {
        var manifest = CreateCppManifest();

        var result = ParameterResolver.Resolve(
            manifest,
            new Dictionary<string, object?> { ["name"] = "My_Cli2" });

        Assert.True(result.IsSuccess);
        Assert.Equal("My_Cli2", result.Value!.Values["name"]);
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("9lives")]
    [InlineData("class")]
    [InlineData("")]
    public void Resolve_InvalidCppIdentifier_ReturnsError(string value)
    {
        var result = ParameterResolver.Resolve(
            CreateCppManifest(),
            new Dictionary<string, object?> { ["name"] = value });

        Assert.Contains(
            result.Issues,
            issue => issue.Code is "parameter.cpp_identifier" or "parameter.required");
    }

    [Fact]
    public void Resolve_ValidChoice_Succeeds()
    {
        var manifest = CreateChoiceManifest(defaultValue: null);

        var result = ParameterResolver.Resolve(
            manifest,
            new Dictionary<string, object?> { ["standard"] = "20" });

        Assert.True(result.IsSuccess);
        Assert.Equal("20", result.Value!.Values["standard"]);
    }

    [Fact]
    public void Resolve_InvalidChoice_ReturnsError()
    {
        var manifest = CreateChoiceManifest(defaultValue: null);

        var result = ParameterResolver.Resolve(
            manifest,
            new Dictionary<string, object?> { ["standard"] = "17" });

        Assert.Contains(result.Issues, issue => issue.Code == "parameter.choice");
    }

    [Fact]
    public void Resolve_MissingValue_AppliesDefault()
    {
        var result = ParameterResolver.Resolve(
            CreateChoiceManifest(defaultValue: "23"),
            null);

        Assert.True(result.IsSuccess);
        Assert.Equal("23", result.Value!.Values["standard"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_Boolean_AcceptsBooleanValues(bool value)
    {
        var manifest = CreateManifest(
            new TemplateParameterDefinition(
                "enabled",
                TemplateParameterType.Boolean,
                "Enabled",
                null,
                Required: true,
                DefaultValue: null,
                Validation: null,
                Values: []));

        var result = ParameterResolver.Resolve(
            manifest,
            new Dictionary<string, object?> { ["enabled"] = value });

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value!.Values["enabled"]);
    }

    private static TemplateManifest CreateCppManifest() =>
        CreateManifest(
            new TemplateParameterDefinition(
                "name",
                TemplateParameterType.Text,
                "Name",
                null,
                Required: true,
                DefaultValue: null,
                Validation: "cpp_identifier",
                Values: []));

    private static TemplateManifest CreateChoiceManifest(string? defaultValue) =>
        CreateManifest(
            new TemplateParameterDefinition(
                "standard",
                TemplateParameterType.Choice,
                "Standard",
                null,
                Required: true,
                DefaultValue: defaultValue,
                Validation: null,
                Values: ["20", "23"]));

    private static TemplateManifest CreateManifest(
        params TemplateParameterDefinition[] parameters) =>
        new(
            SchemaVersion: 0,
            Id: "test",
            FamilyId: "test",
            VariantId: "default",
            Name: "Test",
            Description: "Test",
            Version: "1.0.0",
            TargetOs: "windows",
            BuildSystem: "none",
            SourceLicense: "MIT",
            Parameters: parameters.ToImmutableArray());
}
