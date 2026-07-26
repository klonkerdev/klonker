# Template package format version 0

Version zero is a local directory:

```text
package-root/
  template.toml
  content/
    ...
```

## Manifest

All top-level properties below are required. `schema_version` must be integer
`0`; all other top-level properties are non-empty strings.

```toml
schema_version = 0
id = "official.cpp-cli.windows-cmake"
family_id = "official.cpp-cli"
variant_id = "windows-cmake"
name = "C++ CLI"
description = "A small Windows C++ command-line application using CMake."
version = "0.1.0"
target_os = "windows"
build_system = "cmake"
source_license = "MIT"
```

Each `[[parameters]]` table requires `id`, `type`, `label`, and `required`.
`description`, `default`, and `validation` are optional. Parameter IDs must
match `[A-Za-z_][A-Za-z0-9_]*` and be unique.

Supported types:

- `string`: text; a default must be a TOML string.
- `boolean`: true/false; a default must be a TOML boolean.
- `choice`: a string selected from the required non-empty `values` array; its
  default, when present, must appear in `values`.

The only version-zero named validation is `cpp_identifier`, which requires an
ASCII C++ identifier that is not a C++ keyword. Required text cannot be empty
or whitespace. Supplied undeclared parameters and values outside a choice are
errors. Missing values use defaults before validation.

## Rendering

Files under `content/` are recursively enumerated:

1. Every relative path segment is rendered with Scriban.
2. A rendered segment may not introduce `/` or `\`.
3. A file whose final suffix is `.sbn` is decoded as strict UTF-8, rendered as
   text, and emitted without the final `.sbn`.
4. Every other file is copied byte-for-byte.
5. Paths and files are sorted with ordinal ordering.
6. Case-insensitive duplicate destinations and file/directory collisions are
   errors.

The rendering model contains only resolved declared values. Available
Klonker-owned filters are:

- `lower_case` / `upper_case`: invariant whole-string casing;
- `snake_case` / `kebab_case`: ASCII word splitting and lowercase joining;
- `pascal_case`: ASCII word splitting and title joining;
- `cpp_identifier`: replace unsafe characters with `_`, make the first
  character legal, and avoid C++ keywords.

Example: `{{ project_name | snake_case }}`. Missing variables, malformed
templates, invalid UTF-8, or runtime failures are errors that identify the
source template. Template-defined `func` declarations are rejected. Includes
have no loader.

## Path and security rules

Source packages and rendered paths are untrusted. Version zero rejects:

- absolute, rooted, UNC, and drive-qualified paths;
- empty filenames/segments, `.`, `..`, NUL, alternate-separator tricks,
  invalid Windows characters, trailing spaces/periods, and reserved Windows
  device names;
- paths resolving outside the content/staging root;
- destinations that differ only by Windows casing;
- any file/directory collision;
- symbolic links and reparse points in payloads.

Containment is checked again when source and staging paths are resolved.
Scriban receives no default builtin object, filesystem, environment, network,
process, reflection, clock, random, dynamic loading, or arbitrary .NET
methods. Templates cannot define hooks, commands, patches, regex rewrites,
modules, or slots.

## Complete example

```toml
schema_version = 0

id = "official.cpp-cli.windows-cmake"
family_id = "official.cpp-cli"
variant_id = "windows-cmake"
name = "C++ CLI"
description = "A small Windows C++ command-line application using CMake."
version = "0.1.0"
target_os = "windows"
build_system = "cmake"
source_license = "MIT"

[[parameters]]
id = "project_name"
type = "string"
label = "Project name"
description = "C++ project and executable name."
required = true
default = "MyCliApp"
validation = "cpp_identifier"

[[parameters]]
id = "cpp_standard"
type = "choice"
label = "C++ standard"
required = true
default = "23"
values = ["20", "23"]
```

With `content/CMakeLists.txt.sbn`:

```scriban
cmake_minimum_required(VERSION 3.20)
project({{ project_name | cpp_identifier }} LANGUAGES CXX)
add_executable({{ project_name | cpp_identifier }} src/main.cpp)
target_compile_features({{ project_name | cpp_identifier }} PRIVATE cxx_std_{{ cpp_standard }})
```

the planned output path is `CMakeLists.txt`, with all expressions rendered.
