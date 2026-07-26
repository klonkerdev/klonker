# 0003: No template script execution

**Status:** Accepted

## Context

Template-provided scripts, hooks, and build commands would turn untrusted
packages into arbitrary code execution and make previews nondeterministic.

## Decision

Version one never executes template scripts or commands. Rendering uses a
restricted Scriban context containing declared primitives and Klonker-owned
deterministic string helpers only.

## Consequences

Templates cannot run installers, build systems, Git, or post-generation hooks.
Some ecosystem templates will require declarative format additions instead.
Security review remains centered on parsing, rendering capability, path
containment, and filesystem transactions.
