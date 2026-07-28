# 0004: Registry-qualified identity and content-addressed cache

## Status

Accepted

## Context

Klonker needs local registries outside its repository and remote registries
that remain useful offline. Template IDs can collide across publishers, and
downloaded packages are untrusted input.

## Decision

Template identity is qualified by registry ID and version. Registry version 1
requires package SHA-256 and size. Remote indexes use HTTPS, validated indexes
and packages are cached transactionally under opaque hash keys, and package
ZIPs are verified before restricted extraction. Offline mode performs no
network requests and requires existing valid cache entries.

## Consequences

Multiple registries remain distinct and cache paths do not depend on
publisher-controlled names. Corrupt downloads are rejected and cached packages
support offline startup. Checksums remain package-integrity controls. Detached
index signatures now provide the separate authentication layer: each source
can pin a publisher and multiple active/revoked RSA keys in local
configuration, and signature-required indexes are verified before caching.
