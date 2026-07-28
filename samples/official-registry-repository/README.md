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
is maintained in the separate registry repository. Each package declares a
lowercase language ID. Variants declare an explicit build system; use `none`
when no build-system concept applies.

From the Klonker application repository, produce deterministic publication
artifacts in a new directory:

```powershell
.\eng\pack-registry.ps1 `
  -SourceRoot D:\repos\klonker-registry `
  -OutputRoot D:\repos\klonker-registry-dist `
  -SigningKeyPath D:\secure\publisher-key.pem
```

Publish generated `registry.json`, `registry.json.sig.json`, `catalog.md`,
`catalog.json`, and `packages/*.zip` together.
The official repository commits them beneath `dist/`, making its index:

```text
https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json
```

The generated catalogs enumerate and describe all discovered packages. Custom
long-form documentation belongs at
`templates/<namespace>/<package>/README.md`, never in the registry root.
