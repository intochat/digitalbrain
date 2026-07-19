# Status

DigitalBrain v2 is a ground-up rebuild, in progress on the `master` branch. The previous
implementation was rejected wholesale and survives only as git history. No packages are published to
NuGet yet, and nothing on this site describes shipped behavior until the milestone table below says
so.

## Milestones

| Milestone | State |
| --- | --- |
| Demolition and clean skeleton | done |
| Recorded architecture decisions | done |
| Neuron kernel | done |
| Durable synapse fabric | done |
| Multi-silo delivery and recovery | done |
| AI model binding | done |
| Client package | done |
| Aspire integration | not started |
| Hosts, dev tools, quickstart | not started |
| Hosted restart proof | not started |
| Release engineering | not started |
| Final verification and docs | not started |

## How to follow along

The repository gate is `dotnet test DigitalBrain.slnx -c Release` plus this website's `npm test` and
`npm run build`. Every commit keeps those green. The executable Tier-1 specifications will be
published here as the framework grows.
