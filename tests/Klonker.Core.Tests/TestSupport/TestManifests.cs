namespace Klonker.Core.Tests.TestSupport;

internal static class TestManifests
{
    public const string Valid = """
        schema_version = 0
        id = "test.console.windows"
        family_id = "test.console"
        variant_id = "windows"
        name = "Test Console"
        description = "A test package."
        version = "1.0.0"
        target_os = "windows"
        build_system = "cmake"
        language = "cpp"
        source_license = "MIT"

        [[parameters]]
        id = "project_name"
        type = "string"
        label = "Project name"
        required = true
        default = "Example"
        validation = "cpp_identifier"

        [[parameters]]
        id = "standard"
        type = "choice"
        label = "Standard"
        required = true
        default = "23"
        values = ["20", "23"]
        """;
}
