# Agent instructions

The conventions, toolchain details and known traps for this repo live in
**[CLAUDE.md](CLAUDE.md)**. Read it before making changes; it is written for any
agent or contributor, not just Claude, and it exists because most of what is in
it cost someone a wasted afternoon to discover.

Points that catch people out fastest:

- **Compile-gate before pushing.** The SDK is pinned by `global.json` to
  10.0.400 with `rollForward: disable`, and on the Mac that SDK is in
  `~/.dotnet`, not the system root, so a bare `dotnet` wrongly reports
  `sdk-not-found`.
- **Fabian's Win10 VM is the build and test machine.** Ask him to produce builds
  there rather than building on it yourself.
- **Pre-release tags deliberately create no pipeline.** Only `vX.Y.Z` publishes,
  and the strict tag rules on the publish jobs are load-bearing.
- **No em dashes** in anything written for this repo.

The macOS sibling (`filehasher-macos`) has its own `CLAUDE.md`; output formats
and the scan model are kept compatible between the two, while the defaults
deliberately differ.
