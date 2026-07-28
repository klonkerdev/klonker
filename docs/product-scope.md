# Product scope

## Motivation and initial user

Klonker removes repetitive setup from small experiments. Its initial user is a
beginner developer who repeatedly needs a clean, understandable project, wants
to choose a few meaningful settings, and wants to see exactly what will be
created before any files are written.

## Version-one scope

Version one is project generation plus detached template authoring:

- discover template families and independently versioned variants from
  configured registries;
- cache downloaded packages for offline use;
- collect string, boolean, and choice configuration;
- validate values and known prerequisites;
- preview generated paths and text;
- generate a complete project into a new or empty Windows or WSL directory;
- add reusable modules safely to an existing non-empty source tree after a
  complete no-overwrite preflight;
- aggregate declared module dependency licenses and show inert
  post-generation instructions;
- resolve multi-version registry conflicts with source priority, stable or
  prerelease preference, and app-local exact pins;
- support monolithic starter packages with declared source/dependency licenses.
- guide authors through registry-source package metadata, languages, build
  systems, platforms, preview, and transactional creation;
- inspect existing runtime/source packages or ordinary code folders without
  changing them, then optionally copy ordinary/runtime content into a new
  authoring package;
- derive an editable template from an installed catalog package;
- create development/production registry source workspaces, generically build
  local runtime indexes, register development indexes locally, and optionally
  emit detached publisher signatures.

Available families and variants are registry data rather than product-scope
claims. The official registry publishes a generated Markdown/JSON catalog from
its package manifests, so adding a reviewed template never requires revising
this document. A template may target a platform other than the current Windows
host. The user explicitly chooses a Windows destination or a selected running
WSL distribution.

## Lifecycle

Planning is side-effect free. Generation installs one complete output tree.
When generation succeeds, every generated file belongs entirely to the user.
Klonker stores no project relationship and never manages, upgrades, builds, or
adopts the generated project. Module generation is a one-time addition of
preflighted new paths and creates no management relationship.

## Non-goals

Klonker is not a compiler driver, project build system, package manager, SDK installer,
Git client, project adopter, project updater, plugin host, or general
existing-tree merger. Modules are the narrow exception: they may add only
previously absent planned paths and never overwrite or patch a file. The
authoring wizard can read an existing folder and copy inert files
into a detached new package, but it never edits or manages the source tree.
Version one does not run CMake, xmake, Make, compilers, npm, Gradle, or
template-supplied scripts. It has no MCP or coding-agent integration.
Registry building means packaging inert template data and producing an index;
it does not build the projects described by templates, configure hosting,
push Git changes, or upload CI secrets.

## Platform direction

The current desktop development and filesystem behavior are Windows-first.
Configured local registries, HTTPS remote registries, checksummed package
caching, and explicit offline reuse are implemented on the desktop path.
Generation inside a selected running WSL distribution is an explicit Desktop
boundary. Linux paths are validated, mapped through `\\wsl.localhost`, and
read back after generation. Registry-provided commands are never executed.
