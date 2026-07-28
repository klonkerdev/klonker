# Official template registry

The production catalog lives in the separate
[`klonkerdev/registry`](https://github.com/klonkerdev/registry) repository.
This application repository owns the runtime formats and reusable publisher;
it does not maintain a duplicate package list.

Published artifacts:

```text
https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json
https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json.sig.json
https://raw.githubusercontent.com/klonkerdev/registry/main/dist/catalog.json
```

The registry repository's generated
[`dist/catalog.md`](https://github.com/klonkerdev/registry/blob/main/dist/catalog.md)
is the human-readable source of truth for currently available packages and
variants. `dist/catalog.json` contains the same discovered metadata for a
static website or other tooling. Neither artifact is a handwritten array.

## Generic source layout

```text
registry.toml
keys/
  <key-id>.spki
templates/
  <namespace>/
    <package>/
      package.toml
      README.md                 optional package-specific documentation
      content/                  optional shared payload
      variants/
        <variant>/
          variant.toml
          content/              variant payload
modules/
  <namespace>/
    <module>/
      module.toml
      README.md                 optional module-specific documentation
      content/
dist/
  registry.json
  registry.json.sig.json
  catalog.json
  catalog.md
  packages/
```

The publisher discovers every exact
`templates/<namespace>/<package>/package.toml` and
`variants/<variant>/variant.toml`. Folder names derive family, variant, and
template identities. Adding another namespace, package, or variant therefore
requires no edit to validation code, this document, or a catalog table.
It independently discovers every
`modules/<namespace>/<module>/module.toml`, derives the module ID from the
matching folders, and emits the index's separate `modules` collection.

`package.toml` owns shared description, language, licensing, tags, parameters,
assets, and content. `variant.toml` owns target OS, build system, version,
variant tags, prerequisites, and variant-specific content. Favorite state is
never allowed in either source or the generated runtime manifest.

If a package or module needs long-form prose, its `README.md` stays beside its
source manifest. The generated Markdown catalog links that file, while
`catalog.json` exports its repository-relative path.

## Build and validation

In the standalone registry repository:

```powershell
.\eng\build.ps1 -SigningKeyPath D:\secure\publisher-key.pem
.\eng\validate.ps1
```

The generic publisher:

- discovers every package, variant, and module;
- validates source identity and required metadata;
- rejects unsafe paths, reparse points, case collisions, and
  file/directory collisions;
- merges shared and variant payloads;
- generates runtime manifests without user preferences;
- creates deterministic sorted fixed-timestamp ZIP archives;
- computes package SHA-256 and size;
- emits registry schema 1 plus generated Markdown/JSON catalogs;
- signs the exact UTF-8 `registry.json` bytes when a private key is supplied.

Validation publishes twice and compares the complete generated trees, compares
the result with committed `dist/` (excluding the separately verified
signature), checks every discovered source-to-index mapping and package
archive, and verifies the committed signature. It contains no template ID,
module ID, package count, language family, or required-file special case.

## Publisher trust and key rotation

`registry.toml` declares a publisher ID and current signing-key ID. The private
RSA key is supplied through `-SigningKeyPath` or
`KLONKER_REGISTRY_SIGNING_KEY` and is never committed. The corresponding
Base64 SPKI public key is versioned beneath `keys/` so repository validation
can verify publication.

That repository copy does not bootstrap application trust. Klonker pins the
official publisher key in first-run local configuration. Custom registries
are trusted only when the user configures their expected publisher and public
keys in Settings.

Rotation is additive:

1. generate and protect a new private key;
2. add its public SPKI to registry validation and to Klonker's trusted key set;
3. release the trust update before switching `signing_key_id`;
4. sign new index bytes with the new key;
5. retain retired or compromised key IDs as revoked app-local trust records.

Offline mode verifies cached signature/index bytes against the current local
trust policy and performs no network request.

## GitHub publication and branch policy

The `main` branch serves committed `dist/` files through raw GitHub URLs. Its
`validate` workflow is required and branch protection requires an up-to-date
check, code-owner review, approval of the latest push, resolved conversations,
linear history, and no force pushes or deletion. The standalone repository's
`eng/protect-main.ps1` applies that policy through an authenticated GitHub CLI
session.

Registry packages remain untrusted data even after publisher authentication.
They cannot declare hooks, commands, build invocations, installers, or other
execution. Source-code payloads remain inert until written into a detached
generated project.
