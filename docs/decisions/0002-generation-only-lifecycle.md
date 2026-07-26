# 0002: Generation-only lifecycle

**Status:** Accepted

## Context

Project ownership and long-term upgrade semantics become complex if Klonker
tracks or manages output after creation.

## Decision

Klonker owns planning and one transactional generation operation only. A
successful generated project is immediately detached and belongs to the user.

## Consequences

Klonker does not import, build, run, update, merge, or maintain generated
projects. Templates must generate complete understandable output, and future
features cannot assume persistent project metadata.
