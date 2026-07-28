# WSL generation

Klonker can generate a project or module inside a user-selected WSL
distribution that is already running.

## Boundary

Desktop discovers candidates with the host-owned command:

```text
wsl.exe --list --running --quiet
```

It obtains the selected distribution's default home with a direct
`wsl.exe --distribution <name> --exec printenv HOME` invocation. Arguments use
`ProcessStartInfo.ArgumentList`; no shell command string is built. Template or
module data never supplies an executable or command.

An absolute Linux destination is mapped through the supported Windows WSL file
provider:

```text
/home/alice/projects/demo
\\wsl.localhost\Ubuntu-24.04\home\alice\projects\demo
```

Klonker rejects Linux root, traversal, NUL, invalid distribution names, and
paths that cannot be represented safely through the Windows provider. Project
generation retains the new/empty-directory rule. Module generation retains
its existing-directory and no-conflict rule.

After transfer, Klonker asks the selected distribution to confirm that the
Linux destination directory exists using direct `test -d` arguments, then
reads every file back through `\\wsl.localhost` and compares its bytes with
the preview. It does not run generated code, build tools, template scripts, or
post-generation instructions.

The automated suite tests WSL output decoding, destination mapping, and unsafe
path rejection without requiring WSL. A manual end-to-end transfer test
requires an installed, running distribution.
