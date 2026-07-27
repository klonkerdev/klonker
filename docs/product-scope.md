# Product scope

## Motivation and initial user

Klonker removes repetitive setup from small experiments. Its initial user is a
beginner developer who repeatedly needs a clean, understandable project, wants
to choose a few meaningful settings, and wants to see exactly what will be
created before any files are written.

## Version-one scope

Version one is project generation only:

- discover template families and independently versioned variants from
  configured registries;
- cache downloaded packages for offline use;
- collect string, boolean, and choice configuration;
- validate values and known prerequisites;
- preview generated paths and text;
- generate a complete project into a new or empty directory;
- support Windows destinations first;
- support monolithic starter packages with declared source/dependency licenses.

The first implemented family is a C++ command-line application. Its official
registry provides CMake, GNU Make, and xmake variants targeting Windows and
Linux. The prepared registry source also provides a `gof2.modapi` Lua family
with event, ImGui menu, rendering hook, campaign mission, custom-content, and
all-in-one starters. These variants have no build system and generate the direct folder
layout consumed by the external Windows game ModAPI. A Linux-targeted template
still generates into the selected Windows destination; generation inside WSL
remains planned. Other useful families may include a small C# console app and
a minimal web app, but they are not implemented.

## Lifecycle

Planning is side-effect free. Generation installs one complete output tree.
When generation succeeds, every generated file belongs entirely to the user.
Klonker stores no project relationship and never manages, upgrades, builds, or
adopts the generated project.

## Non-goals

Klonker is not a compiler driver, build system, package manager, SDK installer,
Git client, project importer, project updater, plugin host, or existing-tree
merger. Version one does not run CMake, xmake, Make, compilers, npm, Gradle, or
template-supplied scripts. It has no MCP or coding-agent integration.

## Platform direction

The current desktop development and filesystem behavior are Windows-first.
Configured local registries, HTTPS remote registries, checksummed package
caching, and explicit offline reuse are implemented on the desktop path.
Generation inside a user-selected WSL distribution is planned after the
Windows flow is complete. WSL support will be an explicit destination boundary,
not an assumption that Windows path rules apply unchanged inside Linux.
