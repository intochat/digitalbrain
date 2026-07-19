# dotnet-inspect Skill

Query .NET library APIs — the same commands work across NuGet packages, platform libraries (System.*, Microsoft.AspNetCore.*), and local .dll/.nupkg files.

## Quick Decision Tree

- **Code broken?** → `diff --package Foo@old..new` first, then `member --oneline`
- **Need API surface?** → `member Type --package Foo --oneline` (token-efficient)
- **Need signatures?** → `member Type --package Foo -m Method` (default shows full signatures + docs)
- **Need source/IL?** → `member Type --package Foo -m Method -v:d` (adds Source, Lowered C#, IL)
- **Need constructors?** → `member 'Type<T>' --package Foo -m .ctor` (use `<T>` not `<>`)
- **Need all overloads?** → `member Type --package Foo --select` (shows `Name:N` indices)

## When to Use This Skill

- **"What types are in this package?"** — `type` discovers types (terse), `find` searches by pattern
- **"What's the API surface?"** — `type` for discovery, `member` for detailed inspection (docs on)
- **"What changed between versions?"** — `diff` classifies breaking/additive changes
- **"This code uses an old API — fix it"** — `diff` the old..new version, then `member --oneline` to see the new API
- **"What extends this type?"** — `extensions` finds extension methods/properties
- **"What implements this interface?"** — `implements` finds concrete types
- **"What does this type depend on?"** — `depends` walks the type hierarchy upward
- **"What version/metadata does this have?"** — `package` and `library` inspect metadata
- **"Show me something cool"** — `demo` runs curated showcase queries

## Key Patterns

Use `--oneline` as the default for scanning — it works on `type`, `member`, `find`, `diff`, and `implements`:

```bash
dnx dotnet-inspect -y -- member JsonSerializer --package System.Text.Json --oneline  # scan members
dnx dotnet-inspect -y -- type --package System.Text.Json --oneline                   # scan types
dnx dotnet-inspect -y -- diff --package System.CommandLine@2.0.0-beta4.22272.1..2.0.3 --oneline  # triage changes
```
