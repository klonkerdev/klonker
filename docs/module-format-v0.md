# Module package format version 0

A module is an independently versioned, registry-indexed set of inert files
that can be added to an existing project tree. It is not a template variant,
does not belong to a template family, and never runs setup commands.

```text
module-root/
  module.toml
  content/
    ...
```

## Manifest

```toml
schema_version = 0
id = "std.cpp-cmake-submodule"
name = "CMake C++ submodule"
description = "Adds a small C++ library target."
version = "1.0.0"
language = "cpp"
source_license = "MIT"
tags = ["cpp", "cmake"]
post_generation_instructions = """
Add this to the parent CMakeLists.txt:
add_subdirectory({{ module_root }})
"""

[[slots]]
id = "module_root"
label = "Module folder"
description = "Relative folder inside the selected project."
required = true
default = "src/example"

[[parameters]]
id = "class_name"
type = "string"
label = "Class name"
description = "Starter public class."
required = true
default = "Example"
validation = "cpp_identifier"

[[dependencies]]
id = "fmt"
name = "fmt"
version = "11.0.0"
license = "MIT"
project_url = "https://github.com/fmtlib/fmt"
```

Module IDs are lowercase dot-separated IDs and are qualified by the registry:
`<registry-id>:<module-id>@<version>`. Slots and parameters share one
identifier namespace. Parameters use the template version-zero string,
boolean, choice, default, and validation rules.

A slot value is a non-empty Windows-safe relative path. Unlike a normal text
parameter, it may contain directory separators, so a path such as
`{{ module_root }}/include/widget.hpp.sbn` can render multiple directories.
Normal text parameters cannot inject separators. All final paths still pass
the full rooted/traversal/reserved-name/collision checks.

`[[dependencies]]` entries are declarative. Klonker aggregates their name,
version, and license with `source_license` for review. It does not download,
install, resolve, or execute a dependency. `post_generation_instructions` is
rendered with the same restricted Scriban context and shown as text after
preview/generation; it is never executed.

## Planning and installation

`.sbn` paths/content and post-generation instructions receive restricted,
deterministic rendering. Other files remain byte-for-byte payloads. The
planner returns the ordered file tree, slot values, post-generation text, and
license report without writing.

A module destination must already be a directory, but it may be non-empty.
Before writing, Klonker resolves every planned directory/file and rejects:

- an existing planned file;
- a file occupying a planned directory;
- a directory occupying a planned file;
- case-insensitive duplicates or file/directory collisions;
- unsafe paths or an existing reparse point in the destination chain.

The complete preflight runs again after staging. Installation uses no
overwrite operation. A write race aborts and removes files/directories created
by that attempt. Every installed file is read back and compared with its
planned bytes. Unrelated existing files are untouched.
