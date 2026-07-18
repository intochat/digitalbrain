# Local dogfood packages (digitalbraintech split)

This folder contains the packages produced by `dotnet pack` on the split projects under this prototype:

- DigitalBrain.Abstractions.1.0.0.nupkg
- DigitalBrain.Kernel.1.0.0.nupkg
- DigitalBrain.Sdk.1.0.0.nupkg

## Current status (as of this session)
- Packs succeed (IsPackable, metadata, etc. per NuGet guidance).
- A nuget.config exists at the digitalbraintech/ level with a local source + packageSourceMapping for DigitalBrain.* (following official NuGet docs for repeatable local feeds).
- An experimental switch of AppHost.csproj to PackageReference + the local feed was attempted.
- Result: mapping works and the local source is considered, but the packed nupkgs have transitive dependencies on root monorepo projects (e.g. DigitalBrain.Core) that are not (yet) packed into this feed.
- Therefore the slnx consumer currently stays on project references for the split pieces (with clear comment in AppHost.csproj).
- This is expected during the transition phase while the split is being extracted.

## How to try fuller dogfood later
1. Also pack the direct root dependencies the split projects reference (at minimum DigitalBrain.Core, and whatever the Kernel one pulls).
2. Put those additional .nupkgs here.
3. Switch the AppHost (or a dedicated consumer sample) to PackageReference.
4. Run `dotnet restore digitalbraintech/DigitalBrainTech.slnx` (or the AppHost project) and verify it pulls from the local source.

This directly exercises the plan goal: "pack silo as single experience and ship it like Aspire hosting integrations via NuGet".

See the root plan docs and the 01-...plan.md in docs/ for the full context.
