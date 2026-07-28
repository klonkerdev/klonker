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

Available families and variants are registry data rather than product-scope
claims. The official registry publishes a generated Markdown/JSON catalog from
its package manifests, so adding a reviewed template never requires revising
this document. A template may target a platform other than the current Windows
host; it still generates into the selected Windows destination. Generation
inside WSL remains planned.

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
