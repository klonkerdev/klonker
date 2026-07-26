# Template package format version 0

Version zero is a local directory:

```text
package-root/
  template.toml
  template-logo.png  # optional
  content/
    ...
```

## Manifest

The identity, descriptive, target, build, and license properties below are
required. `schema_version` must be integer `0`; the other required properties
are non-empty strings.

```toml
schema_version = 0
id = "std.cpp-cli.windows-cmake"
family_id = "std.cpp-cli"
variant_id = "windows-cmake"
name = "C++ CLI"
description = "A small Windows C++ command-line application using CMake."
version = "0.1.0"
target_os = "windows"
build_system = "cmake"
source_license = "MIT"
```

Optional presentation metadata:

```toml
logo = "template-logo.png"
tags = ["cli", "native", "starter", "cpp"]
favorite = true
```

`logo` is a package-root-relative PNG, JPEG, or WebP path. It receives the
same path normalization and containment checks as generated paths, may not be
a reparse point, must exist, and is limited to 5 MiB. Desktop decodes it to a
small card image and falls back to a generated language badge when it cannot
be decoded.

`tags` is an optional array of unique, case-insensitively compared labels.
Each trimmed tag contains 1–40 visible characters. Tags are descriptive
catalog metadata: arbitrary names such as `graphics`, `gamedev`, `gof2`, and
`modding` do not affect rendering or grant capabilities.

`favorite` is an optional boolean defaulting to `false`. It marks the initial
favorite state; the current Desktop UI permits an in-memory user override but
does not persist that override yet.

Optional display-only prerequisites describe tools needed after Klonker has
generated and detached the project:

```toml
[[prerequisites]]
id = "cmake"
name = "CMake 3.20 or later"
description = "Required after generation to configure and build the project."
required_for = "build"
```

Each prerequisite requires `id`, `name`, `description`, and `required_for`.
IDs use the same identifier shape as parameter IDs and must be unique.
`required_for` is `build`, `run`, or `development`. Klonker displays this
information but does not probe, install, or execute the prerequisite.

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

id = "std.cpp-cli.windows-cmake"
family_id = "std.cpp-cli"
variant_id = "windows-cmake"
name = "C++ CLI"
description = "A small Windows C++ command-line application using CMake."
version = "0.1.0"
target_os = "windows"
build_system = "cmake"
source_license = "MIT"
logo = "template-logo.png"
tags = ["cli", "native", "starter", "cpp"]
favorite = true

[[prerequisites]]
id = "cmake"
name = "CMake 3.20 or later"
description = "Required after generation to configure and build the C++ project."
required_for = "build"

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
