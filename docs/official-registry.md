# Official template registry

The production official templates live in the separate
[`klonkerdev/registry`](https://github.com/klonkerdev/registry) GitHub
repository. This application repository contains a development sample, the
format contract, and reusable publishing tooling.

The canonical machine-readable index is:

```text
https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json
```

The endpoint is live and serves the most recently pushed `dist/`. The prepared
registry checkout has been loaded directly through Klonker's Core catalog
service as a local registry: all 12 packages passed checksum/extraction, six
`gof2.modapi` variants were discovered as Lua/no-build templates, and the
all-in-one variant produced a valid generation plan and output tree.

The prepared catalog contains six independently versioned `std.cpp-cli`
variants (CMake, GNU Make, and xmake for Windows and Linux) plus six
`gof2.modapi` Lua variants (event starter, ImGui menu, render hook, campaign
mission, custom content, and an all-in-one showcase). The application repository's checked-in
development sample remains the Windows CMake variant only. The new GOF2
entries become available from the public endpoint after the regenerated
registry `dist/` is pushed.

## Source repository layout

```text
registry.toml
templates/
  std/
    cpp-cli/
      package.toml
      content/
      variants/
        linux-cmake/
          variant.toml
          content/
        windows-cmake/
          variant.toml
          content/
      template-logo.png
  gof2/
    modapi/
      package.toml
      content/LICENSE.txt
      variants/
        event-starter/
        imgui-menu/
        render-hook/
        campaign-mission/
        custom-content/
        all-in-one/
dist/
  registry.json
  packages/
    std.cpp-cli.linux-cmake-0.1.1.zip
    std.cpp-cli.windows-cmake-0.1.1.zip
```

`registry.toml` contains only registry authority metadata. The publisher
recursively discovers `templates/<namespace>/<package>/package.toml` and
`variants/<variant>/variant.toml`. It derives family and template IDs from
those folders; there is no handwritten catalog array.

`package.toml` owns shared language/presentation metadata, licensing, tags,
parameters, assets, and reusable content. `variant.toml` owns target OS, build
system, version, variant tags, prerequisites, and variant content. For
example:

```text
templates/std/cpp-cli/variants/linux-cmake
```

becomes family `std.cpp-cli` and template
`std.cpp-cli.linux-cmake`. Additional reviewed namespaces such as `community`
or `android` can be added without flattening every variant into one directory.

The publisher merges:

```text
package/
    template-logo.png
    content/src/...
variant/
    content/CMakeLists.txt.sbn
```

into a runtime ZIP containing a generated `template.toml`. Shared and variant
paths may not collide. `dist/` is deterministically generated and committed so
GitHub raw content can serve it.

Do not put generator lifecycle hooks, setup executables, or commands for
Klonker to run in packages. Source-code payloads such as the GOF2 Lua starters
remain inert data during planning and generation; Klonker never executes them.
Registry review should treat every path and rendered expression as untrusted.

The GOF2 starters were derived from the GPL-3.0-only
[`KaamoClubModApi`](https://github.com/1337Skid/KaamoClubModApi) loader,
bindings, and example mods. They reproduce its direct
`mods/<mod-id>/init.lua` layout and lifecycle rules. The generated packages
include the GPL text. Example `.aei` files are deliberately excluded because
their accompanying notice attributes those assets to Fishlabs.

Each GOF2 package also carries inert LuaLS definitions plus `.vscode` and
`.luarc.json` configuration. These describe the API exposed by the upstream
C++ bindings for completion and diagnostics; Klonker never executes them.

## Build publication artifacts

In the standalone registry repository:

```powershell
.\eng\build.ps1
.\eng\validate.ps1
```

The repository's build and validation scripts:

- validates required source metadata and package shape;
- rejects package reparse points;
- creates byte-for-byte deterministic ZIPs with sorted entries and fixed
  timestamps;
- computes each ZIP's SHA-256 and size;
- emits registry schema version 1.

Validation builds twice and requires identical bytes, then compares that result
with the committed `dist/` tree and inspects package paths, collisions,
checksums, sizes, and required entries.

## GitHub publication

The `main` branch publishes `dist/registry.json` and `dist/packages/` together.
Relative package paths resolve from the raw index URL, and no GitHub API token
is required. The standalone repository includes Windows CI for deterministic
artifact and package-safety validation.

Publication status:

- [x] push the prepared repository to `klonkerdev/registry`;
- [x] test a clean online download and a fully offline cached load;
- [x] test the expanded 12-entry distribution as a local Klonker registry;
- [ ] push the GOF2 source and regenerated 12-entry `dist/`;
- [ ] protect `main` and require the validation workflow and review;
- [ ] retain a documented license-review gate for every generated source
  package;
- [ ] define signature and key-rotation policy before treating checksums as
  publisher authentication.

Klonker seeds this official URL for a new user configuration.
`KLONKER_OFFICIAL_REGISTRY_URL` remains a first-launch staging override.
