# DigitalBrain.Capabilities.Identity

The `identity@1` capability contract: accounts, brains, principals, membership and grants.

- `IIdentity` (`[Capability("identity", 1)]`): accounts (`ReadAccount`, `OwnerOfBrain`, `EnsureAccount`), brains
  (`CreateBrain`, `EnsureBrain`, `ProvisionOwner`, `BrainsOf`, `KindOf`, `BrainExists`), principals (`IsRegistered`,
  `RegisterPrincipal`, `CompleteRegistration`) and membership (`Membership`, `EnsureMember`, `RemoveMember`,
  `RemovePrincipalMember`).
- `IGrants` (same id): `Authorize(CallRequest)`, `ListGrants`, `GrantAsOwner`, `RevokeAsOwner`.
- `ISessions` (same id, 09-03): `Issue(SessionRequest)`, `End(sessionId)`, `Validate(sessionId)`. Privileged: the
  runtime refuses every member to a module whose manifest `Privileged` list does not grant it (fail closed). Extra
  claims (`sid`, `auth_time`, `amr`) never replace the identity-owned `principal`, `account`, `brain` and `role`
  (`SessionClaims`); a request naming one is rejected. The cookie write is `ISessionIssuer` in
  `DigitalBrain.Runtime.Http`, not in this package.
- `IIdentityProvider` (same id): `Describe()` answers `IdentityProviderDescriptor` (`ProviderId`, `Posture` Open or
  Secured, `AccountSurface` as a nullable `NeuronId`; null hides the Settings Account tab).
- `ExternalSignInTicket` and `ExternalSignInClaims`: the external sign-in ticket (ES256 on P-256, at most 60 s,
  at most 4096 characters, `aud` is the install origin), byte-compatible with the os issuer.
- `ISignInHandler`: invoked after the session is issued; a failing handler is logged and never fails sign-in.
- Ports: `IIdentityDirectoryPort` (directory and grants) and `IIdentitySessionPort` (sessions and description).
- No caller parameter anywhere: the implementation reads the runtime-stamped caller (D-15).
- Records: `Account`, `Member`, `MemberRole`, `BrainEntry`, `BrainKind`, `BrainProvision`, `Grant` (with `Covers`),
  `GrantMode`, `CallRequest`, `CallDecision`, `CallDenial`.

References only `DigitalBrain`; no Orleans, no ASP.NET Core.
