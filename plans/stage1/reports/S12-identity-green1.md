# S1.2-GREEN-1 — workspace membership grains report

## What changed
- `src/Kernel/DigitalBrain.Abstractions/Identity/PrincipalId.cs` — durable principal identity (non-empty GUID).
- `src/Kernel/DigitalBrain.Abstractions/Identity/ActorContext.cs` — `{PrincipalId, Username}` stamp for durable commands.
- `src/Kernel/DigitalBrain.Abstractions/Neurons/WorkspaceRole.cs` — Owner / Admin / Builder / Viewer.
- `src/Kernel/DigitalBrain.Abstractions/Neurons/WorkspaceMember.cs` — membership row shape.
- `src/Kernel/DigitalBrain.Abstractions/Neurons/IWorkspace.cs` — workspace neuron contract (`workspace` / `main`, ClientEntryPoint).
- `src/Kernel/DigitalBrain.Abstractions/Synapses/WorkspaceMembership.cs` — `db.workspace.*` RequestSynapse verbs + replies.
- `src/Kernel/DigitalBrain.Core/Neuron/WorkspaceNeuron.cs` — durable membership state, authz, last-Owner invariant, journaled replies with Actor+At.
- `src/Tests/DigitalBrain.Tests/WorkspaceMembershipProofs.cs` — NeuronTest-style proofs for verbs, refusals, audit stamps.

### Contracts & aliases
| Alias | Shape |
|-------|--------|
| `db.principal-id` | `PrincipalId` |
| `db.actor-context` | `ActorContext` |
| `db.workspace-role` | `WorkspaceRole` |
| `db.workspace-member` | `WorkspaceMember` |
| `db.workspace` | `IWorkspace` |
| `db.workspace.add-member` → `db.workspace.member-added` | `AddMember` / `MemberAdded` |
| `db.workspace.change-role` → `db.workspace.role-changed` | `ChangeRole` / `RoleChanged` |
| `db.workspace.remove-member` → `db.workspace.member-removed` | `RemoveMember` / `MemberRemoved` |
| `db.workspace.read-membership` → `db.workspace.membership` | `ReadMembership` / `Membership` |

### Invariants enforced
- Empty workspace bootstrap: first member must be Owner; actor principal must equal the new member.
- Only Owner/Admin members may mutate; non-members refused (`NeuronAuthorizationException`).
- Builder/Viewer mutations refused.
- Last Owner cannot be removed or demoted.
- Duplicate principal refused; remove/change of unknown member refused.
- ReadMembership: members only.
- No credentials/password material in grains.
- Audit: mutation commands carry `ActorContext` (incoming journal); replies stamp `Actor` + `At` (outgoing journal).

## Tests
Added (all green):

| Test | Covers |
|------|--------|
| `EmptyWorkspaceAcceptsTheFirstOwnerAsBootstrap` | bootstrap + read |
| `OwnerCanAddAdminBuilderAndViewer` | all roles |
| `AdminCanMutateMembership` | Admin mutator |
| `BuilderCannotMutateMembership` | role refusal |
| `NonMemberMutationsAreRefused` | non-member refusal |
| `LastOwnerCannotBeRemovedOrDemoted` | last-Owner invariant |
| `SecondOwnerCanBeRemovedAndFirstOwnerCanDemoteWhenAnotherOwnerRemains` | multi-Owner path |
| `MembershipMutationsJournalTheActingActor` | Actor stamp on command + reply journals |
| `NonMemberCannotReadMembership` | read refusal |
| `DuplicateMemberIsRefused` | duplicate principal |
| `EmptyWorkspaceRefusesNonOwnerBootstrap` | bootstrap role guard |

Flipped PIN-DEFECT pins: **none** (part 2 owns P0-3/P0-4).

## Gate
```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(Time Elapsed ~4.4s; node NO_COLOR noise from CodeGraph AppHost target is not a C# warning)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 91, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 41.282s
```

## Conflicts & risks
- Workspace name is the grain instance name (`main`) until a later rename verb exists — enough for MVP one-workspace.
- Bootstrap is self-Owner only (actor must be the first Owner). GREEN-2 host bootstrap will call `AddMember` as that Owner after creating the ASP.NET Identity account + `PrincipalId`.
- Invitations are not modeled (brief listed them under product state ownership; GREEN-1 scoped to membership list verbs only).
- `IWorkspace` is not in `CapabilityInvocation.FrameworkInterfaces` — no non-handle grain methods were added (all verbs are synapses).

## Out of scope
- HTTP, ASP.NET Identity, cookies, endpoints, chat identity, brain-factory (GREEN-2).
- PIN-DEFECT(P0-3)/P0-4 flips (GREEN-2).
- OAuth principal binding (S1.3).
- Invitations / AutomationPrincipal.
- No packages, no git, no aspire.
