# Registry workspace wizard

The Registry workspace button in Klonker's title bar opens three workflows:

- **Development registry** creates a local source workspace and can import a
  registry-source package from the Template wizard.
- **Production registry** creates publisher metadata, a public-key file,
  signing guidance, and a 3072-bit RSA private key at an explicitly selected
  path outside the workspace.
- **Existing source** validates and rebuilds a folder containing
  `registry.toml` and `templates/`.

New workspaces use the publisher-compatible hierarchy:

```text
registry.toml
templates/
  <namespace>/
    <package>/
      package.toml
      content/
      variants/
        <variant>/
          variant.toml
          content/
dist/
  registry.json
  registry.json.sig.json   # when signing
  packages/
```

## Development loop

The builder discovers every `package.toml` and `variant.toml`; it has no
hardcoded template IDs. It combines shared and variant content into one local
runtime package per variant, validates each runtime manifest, computes the
canonical directory hash and byte size, and writes the generated index.
`dist` is built in a sibling staging folder and replaced as one unit only after
all packages and the index validate.

An enabled local registry source can be added to app-local
`registries.json` after a successful build. Refreshing the catalog then loads
the development variants. Rebuilding the same workspace updates `dist`
without executing package content, build systems, scripts, Git, or generated
programs.

## Signing and rotation

Production setup writes the Base64 SPKI public key under
`keys/<key-id>.spki`. The PKCS#8 private PEM is written only to the explicitly
selected external path, is never overwritten, and is rejected if that path is
inside the registry workspace. The builder signs the SHA-256 of the exact
`registry.json` bytes with RSA PKCS#1 SHA-256 and writes the detached
`registry.json.sig.json` document understood by Klonker.

Keep the private PEM in a secret manager and publish the complete `dist`
folder. For rotation, introduce a new key ID and public key, add it to consumer
trust before switching signatures, and revoke the previous key only after the
new key has propagated.

The wizard does not configure a hosting provider, push Git changes, or upload
secrets. Those are explicit external administrative actions.
