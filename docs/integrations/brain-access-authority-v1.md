# Brain Access Authority v1

Brain Access Authority v1 is the issuer-neutral authentication boundary for a DigitalBrain product host. The host passes only an authentication scheme and opaque credential to `IBrainAccessAuthority`. The authority validates that evidence and returns a short-lived `BrainAccessGrant`; callers cannot select a workspace or principal in a request body or header.

## JWT and OIDC profile

An OIDC-backed implementation must validate the compact JWT before reading authorization claims. Validation must include the configured issuer, a trusted issuer signing key, the exact DigitalBrain audience, a signature-bearing token, `nbf` when present, required expiry, and configured clock skew. Failed validation must produce an unauthenticated result without logging or returning the credential.

`OidcClaimsAuthority` receives `TokenValidationParameters` from product-host composition. The parameters may use fixed signing keys or an OIDC `ConfigurationManager` for discovery and signing-key rotation. The adapter clones those parameters and forces issuer, audience, signature, signing-key, expiry, and lifetime validation on. A successful library validation is still mapped through `BrainAccessGrant.Create`, which enforces `IssuedAt <= evaluatedAt < ExpiresAt` and a maximum lifetime of exactly 15 minutes. Deployments should issue access tokens for five minutes and must never issue them for longer than 15 minutes.

The default closed claim schema is:

| Meaning | Default claim | Cardinality |
| --- | --- | --- |
| Principal | `sub` | exactly one non-empty value |
| Workspace | `brain_workspace` | exactly one non-empty value |
| Roles | `brain_role` | zero or more unique, non-empty values |
| Grants | `brain_grant` | zero or more unique, non-empty values |
| Connection references | `brain_connection` | zero or more unique, non-empty opaque values |
| Policy version | `brain_policy_version` | exactly one positive integer |
| Issued time | `iat` | exactly one NumericDate |
| Expiry | `exp` | exactly one NumericDate |

`AuthorityOptions` can rename the six authorization claims and authentication scheme. Claim names must be non-empty and mutually distinct. Unknown claims are not interpreted as authority. Duplicate or empty configured values are rejected rather than merged, so issuer-specific ambiguity cannot expand access.

## Policy change and revocation

The positive `brain_policy_version` binds a grant to the issuer's authorization-policy revision. A product host must compare that version wherever policy can change during a token lifetime and reject a stale version. Emergency revocation requires the issuer or gateway to deny the credential immediately; otherwise the maximum revocation delay is the remaining short token lifetime. Connection references are opaque identifiers, not provider credentials, and revoking a connection must invalidate its use even when an older grant still contains the reference.

## Authorization and presentation

`AuthenticateAsync` is the only authorizing operation. It derives workspace, principal, roles, grants, connections, policy version, and validity times solely from validated evidence.

`GetWorkspacePresentationsAsync` is display-only. Its names cannot add a workspace, role, grant, connection, or policy version and must never be used for an authorization decision. The built-in adapter returns only the authenticated workspace and uses its opaque ID as the fallback display name.

## OAuth client profiles

Machine-to-machine callers using client credentials must use a confidential client authenticated by a private key or managed workload identity. The issuer must bind the service principal to one workspace and least-privilege grants, issue the same exact audience, omit user impersonation claims, and keep the token within the 15-minute maximum. Shared client secrets in source, configuration files, or fixture tokens are not permitted.

Interactive public clients must use Authorization Code with PKCE using `S256`, a fresh high-entropy verifier and state for every attempt, exact redirect-URI matching, and no client secret. Implicit and resource-owner-password flows are not part of this profile. The resulting access token is presented as opaque evidence; the DigitalBrain caller must not decode it to choose a workspace.

## Local authority

`LocalTestAuthority` is a deterministic fixture issuer for development and tests only. Construction accepts only `Development`, `Test`, or `Testing`; production and staging selection fail closed. Its fixed signing material is public test data, is not a secret, and must never protect a deployment.

## Adapter conformance

External implementations should add a concrete subclass of `AuthorityConformanceTests`, supply their public `IBrainAccessAuthority`, and issue fixture credentials for each `AuthorityFixture`. Run the suite from the DigitalBrain repository root with this exact command:

```powershell
dotnet test tests/CoreV2/Brain.Authority.Conformance.Tests/Brain.Authority.Conformance.Tests.csproj --no-restore
```

The suite verifies rejection of wrong issuer, untrusted signing key, wrong audience, expiry, missing workspace, duplicate and empty closed-schema values; complete grant mapping; presentation separation; the opaque-evidence request shape; the local production guard; and the library-only runtime boundary.

`IntoChatAuthorityExample` is a test fixture contract example inside the conformance project. IntoChat is an external implementation, not a DigitalBrain project reference, runtime dependency, identity provider requirement, or privileged integration. Community adapters have the same public SPI and conformance obligations.
