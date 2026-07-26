# Official registry publication example

This directory demonstrates the source metadata expected by
`eng/pack-registry.ps1`. The official catalog is maintained separately at
`https://github.com/klonkerdev/registry`; do not publish this development
fixture as the official catalog.

The publisher reads registry authority metadata from `registry.toml` and
discovers packages and variants from:

```text
templates/<namespace>/<package>/
  package.toml
  content/
  variants/<variant>/
    variant.toml
    content/
```

No manually maintained template array is required. The official C++ example
uses `templates/std/cpp-cli/variants/<platform>-<build-system>`.

From the Klonker application repository, produce deterministic publication
artifacts in a new directory:

```powershell
.\eng\pack-registry.ps1 `
  -SourceRoot D:\repos\klonker-registry `
  -OutputRoot D:\repos\klonker-registry-dist
```

Publish the generated `registry.json` and `packages/*.zip` files together.
The official repository commits them beneath `dist/`, making its index:

```text
https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json
```
