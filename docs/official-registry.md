# Official template registry

The production official templates live in the separate
[`klonkerdev/registry`](https://github.com/klonkerdev/registry) GitHub
repository. This application repository contains a development sample, the
format contract, and reusable publishing tooling.

The canonical machine-readable index is:

```text
https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json
```

The endpoint is live. Klonker's Core catalog service has been exercised against
a fresh temporary cache, downloaded and verified all six packages, and then
loaded the same catalog in offline mode through an HTTP client that rejected
every request.

The prepared catalog currently contains six independently versioned variants
in the `std.cpp-cli` family: CMake, GNU Make, and xmake for both Windows
and Linux. The application repository's checked-in development sample remains
the Windows CMake variant only.

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
dist/
  registry.json
  packages/
    std.cpp-cli.linux-cmake-0.1.0.zip
    std.cpp-cli.windows-cmake-0.1.0.zip
```

`registry.toml` contains only registry authority metadata. The publisher
recursively discovers `templates/<namespace>/<package>/package.toml` and
`variants/<variant>/variant.toml`. It derives family and template IDs from
those folders; there is no handwritten catalog array.

`package.toml` owns shared presentation, licensing, tags, parameters, assets,
and reusable content. `variant.toml` owns target OS, build system, version,
prerequisites, and variant content. For example:

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

Do not put hooks, executables to run, build commands, or arbitrary scripts in
packages. Registry review should treat every path and rendered expression as
untrusted.

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
- [ ] protect `main` and require the validation workflow and review;
- [ ] retain a documented license-review gate for every generated source
  package;
- [ ] define signature and key-rotation policy before treating checksums as
  publisher authentication.

Klonker seeds this official URL for a new user configuration.
`KLONKER_OFFICIAL_REGISTRY_URL` remains a first-launch staging override.
