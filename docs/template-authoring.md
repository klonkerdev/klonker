# Template authoring wizard

The Template wizard is available from the package button in Klonker's title
bar. It creates the same registry-source layout consumed by the official
publisher:

```text
<package>/
  package.toml
  content/
  variants/
    <platform>-<build-system>/
      variant.toml
      content/
```

One independently versioned variant is created for every selected platform ×
build-system combination. Language starter files are shared package content;
each build system's starter files are isolated to its variants. `Any platform`
is mutually exclusive with operating-system-specific targets, and `No build
system` is mutually exclusive with concrete build systems. The wizard adds a
declared `project_name` parameter and can create a restricted Scriban
`README.md.sbn`.

## New template

The guided flow chooses:

1. a new or empty destination;
2. an explicit generated-source license and optional README;
3. a language card;
4. one or more build systems allowed by that language;
5. one or more platform cards;
6. namespace/package IDs, display name, description, and initial version;
7. a complete file preview before transactional generation.

Licenses, platforms, languages, their allowed build systems, and starter files
come from
`src/Klonker.Desktop/Assets/template-authoring-options.json`. The document has
its own schema version and is validated for duplicate IDs, missing sections,
unknown build-system references, and invalid starter entries. Extending the
card catalog does not require changing wizard code.

## Catalog template

The third starting mode creates a detached authoring package from a template
already loaded in the catalog. Klonker copies inert content and pre-fills the
language, platform, build system, source license, identity, version, parameters,
prerequisites, and tags. The installed/cached catalog package is never changed.
Unknown language, build-system, and platform IDs are surfaced as editable
choices, so a registry may extend its vocabulary before the built-in card JSON
is updated.

## Existing folder

Inspection is read-only and recognizes:

- runtime packages with root `template.toml` plus `content/`;
- registry-source packages with root `package.toml` plus `variants/`;
- ordinary code or Scriban-template folders with no Klonker manifest.

Runtime manifests use the production package loader. Registry source
inspection checks TOML syntax, schema versions, required properties,
folder/identity agreement, variants, semantic versions, safe paths,
case-insensitive collisions, and forbidden package-local favorite state.
Every finding includes a readable correction and path where available.
Authors can edit the source with their normal editor and press **Refresh**
until errors are gone.

A valid registry-source package needs no conversion. An ordinary folder or
valid runtime package can be copied into a separate new authoring destination;
the original is never changed. The import preserves binary files and `.sbn`
payloads, skips `.git`, IDE state, dependency/build output, and other common
generated directories, and reports every exclusion.

## Safety

Planning performs no writes. Authoring rejects links/reparse points, unsafe
relative paths, case collisions, non-empty destinations, and destinations
inside the inspected source. Imports are limited to 10,000 files, 64 MiB per
file, and 512 MiB total. Generation uses the same sibling-staging and atomic
rename boundary as normal project generation. No source, generated file,
script, build system, compiler, package manager, or project is executed.
