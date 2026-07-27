# Registry index format version 1

Registry indexes are machine-generated UTF-8 JSON. A registry identifies one
catalog authority; template identity is qualified as
`<registry_id>:<template_id>@<version>`. Two registries may publish the same
template ID without being silently merged.

## Index

```json
{
  "schema_version": 1,
  "registry_id": "klonker.official",
  "display_name": "Klonker official templates",
  "templates": [
    {
      "family_id": "std.cpp-cli",
      "variant_id": "windows-cmake",
      "template_id": "std.cpp-cli.windows-cmake",
      "name": "C++ CLI",
      "description": "A small Windows C++ command-line application using CMake.",
      "version": "0.1.0",
      "target_os": "windows",
      "build_system": "cmake",
      "language": "cpp",
      "package_path": "packages/std.cpp-cli.windows-cmake-0.1.0.zip",
      "license_summary": "Generated source: MIT",
      "package_sha256": "64-lowercase-hex-characters",
      "package_size_bytes": 12345
    }
  ]
}
```

Every property shown except `language` is required. `language` is emitted by
the current publisher and is a lowercase identifier; indexes created before
this field was introduced load as `unknown`. `package_path` is a safe relative
path. A remote index resolves it relative to the index URL and requires the
resulting URL to use HTTPS. A local index resolves it beneath the directory
containing `registry.json`.

Remote package artifacts are ZIP files whose root contains `template.toml`,
optional presentation assets, and `content/`. `package_sha256` and
`package_size_bytes` describe the ZIP bytes exactly.

The development local registry may reference a directory rather than a ZIP.
For a directory:

1. files are sorted by ordinal `/`-separated relative path;
2. each hash record contains UTF-8 path, NUL, signed 64-bit big-endian file
   length, NUL, file bytes, and `0xFF`;
3. SHA-256 is computed over the concatenated records;
4. package size is the sum of file lengths.

This directory form is for editable local registries. Published remote
registries should use deterministic ZIP artifacts.

## User registry configuration

On Windows, Desktop reads:

```text
%LOCALAPPDATA%\Klonker\registries.json
```

The configuration format is:

```json
{
  "schema_version": 0,
  "offline": false,
  "sources": [
    {
      "name": "Personal templates",
      "kind": "local",
      "location": "D:\\templates\\registry.json",
      "enabled": true
    },
    {
      "name": "Klonker official templates",
      "kind": "remote",
      "location": "https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json",
      "enabled": true
    }
  ]
}
```

Relative local locations resolve beside `registries.json`. The application
creates this file on first launch and seeds the canonical official HTTPS
source. Repository development also adds the checked-in sample registry.
`KLONKER_OFFICIAL_REGISTRY_URL` can override the official URL before the first
launch for staging tests. Once created, the JSON file is authoritative and may
be edited to add, disable, or remove sources.

## Cache and offline behavior

The cache root is:

```text
%LOCALAPPDATA%\Klonker\cache\v1\
  indexes\<sha256-of-index-url>.json
  packages\<sha256-of-qualified-package-identity>\
    package.zip
    package\
    complete.sha256
```

Opaque hash directory names prevent untrusted registry/template IDs from
becoming filesystem paths.

Online behavior:

1. fetch and validate a remote index;
2. atomically replace its cached index only after validation;
3. reuse a package archive only when size and SHA-256 still match;
4. otherwise download to a temporary file, enforce limits, verify, then move;
5. safely extract into a staging directory and install the cache atomically.

If index refresh fails, a valid cached index is used with a visible warning.
When `offline` is `true`, no HTTP request is made. Each remote index and package
must already be cached; unavailable templates are reported rather than
silently substituted.

## Security restrictions

Remote downloads have index/package byte limits. ZIP extraction rejects
rooted, drive-qualified, UNC, traversal, invalid Windows, case-colliding, and
file/directory-colliding paths. It also rejects symbolic-link/reparse entries,
large entries, excessive expanded size, and excessive entry count. Every
extracted path receives a final containment check.

SHA-256 detects corruption and a server returning bytes different from its
index. It does not establish publisher trust. Cryptographic signatures and
key-rotation policy remain future work.
