# Brain Access Authority v1

Brain Access Authority v1 is the issuer-neutral authentication boundary for a DigitalBrain product host. The host passes only an authentication scheme and opaque credential to `IBrainAccessAuthority`. The authority validates that evidence and returns a short-lived `BrainAccessGrant`; callers cannot select a workspace or principal in a request body or header.

## JWT and OIDC profile

An OIDC-backed implementation must validate the compact JWT before reading authorization claims. Validation must include the configured issuer, a trusted issuer signing key, exactly one audience equal to the configured DigitalBrain audience, a signature-bearing token, `nbf` when present, required expiry, and configured clock skew. A matching audience accompanied by any second audience is invalid. The `aud` JSON value may be the single configured string or a one-element string array. Failed validation must produce an unauthenticated result without logging or returning the credential.

`OidcClaimsAuthority` receives `TokenValidationParameters` from product-host composition. The parameters may use fixed signing keys or an OIDC `ConfigurationManager` for discovery and signing-key rotation. The adapter clones those parameters, clears validators/readers/transforms that could bypass its exact checks, and forces issuer, audience, signature, signing-key, expiry, and lifetime validation on. Trusted host-supplied `IssuerSigningKeyResolver` and `IssuerSigningKeyResolverUsingConfiguration` delegates are retained as key-resolution inputs and remain part of the ProductHost trust boundary. Clock skew defaults to 30 seconds and can be configured from zero through five minutes inclusive. A successful library validation is still mapped through `BrainAccessGrant.Create`, which enforces `IssuedAt <= evaluatedAt < ExpiresAt` and a maximum lifetime of exactly 15 minutes. Deployments should issue access tokens for five minutes and must never issue them for longer than 15 minutes.

The default closed claim schema is:

| Meaning | Default claim | Approved JSON shape and cardinality |
| --- | --- | --- |
| Principal | `sub` | exactly one non-empty JSON string |
| Principal kind | `brain_principal_kind` | exactly one JSON string: `human` or `service` |
| Workspace | `brain_workspace` | exactly one non-empty JSON string |
| Roles | `brain_role` | an omitted value, one non-empty JSON string, or an array of unique non-empty JSON strings |
| Grants | `brain_grant` | an omitted value, one non-empty JSON string, or an array of unique non-empty JSON strings |
| Connection references | `brain_connection` | an omitted value, one non-empty opaque JSON string, or an array of unique non-empty opaque JSON strings |
| Policy version | `brain_policy_version` | exactly one positive canonical JSON integer in the Int32 range; quoted, fractional, or exponential forms are invalid |
| Not-before time | `nbf` | optional single canonical integral JSON NumericDate in seconds; quoted, fractional, or exponential forms are invalid |
| Issued time | `iat` | exactly one canonical integral JSON NumericDate in seconds; quoted, fractional, or exponential forms are invalid |
| Expiry | `exp` | exactly one canonical integral JSON NumericDate in seconds; quoted, fractional, or exponential forms are invalid |

`AuthorityOptions` can rename the seven authorization claims and authentication scheme. Claim names are case-sensitive, must be non-empty, and must be mutually distinct. The registered JWT claims `iss`, `sub`, `aud`, `exp`, `nbf`, `iat`, and `jti`; OIDC protocol claims `auth_time`, `nonce`, `acr`, `amr`, `azp`, `at_hash`, `c_hash`, `s_hash`, and `sid`; OIDC standard identity claims `name`, `given_name`, `family_name`, `middle_name`, `nickname`, `preferred_username`, `profile`, `picture`, `website`, `email`, `email_verified`, `gender`, `birthdate`, `zoneinfo`, `locale`, `phone_number`, `phone_number_verified`, `address`, and `updated_at`; and OAuth protocol claims `client_id`, `scope`, `cnf`, `act`, `may_act`, and `events` are reserved. `sub` is permitted only as `SubjectClaim`; no other configurable mapping may use it, and `SubjectClaim` may not use any other reserved name. Unknown claims are not interpreted as authority. Duplicate, empty, or incorrectly shaped configured values are rejected rather than coerced or merged, so issuer-specific ambiguity cannot expand access. Principal kind is signed authority data and must never be accepted from a request body, header, or caller-selected string.

## Policy change and revocation

The positive `brain_policy_version` binds a grant to the issuer's authorization-policy revision. A product host must compare that version wherever policy can change during a token lifetime and reject a stale version. Emergency revocation requires the issuer or gateway to deny the credential immediately; otherwise the maximum revocation delay is the remaining short token lifetime. Connection references are opaque identifiers, not provider credentials, and revoking a connection must invalidate its use even when an older grant still contains the reference.

## Authorization and presentation

`AuthenticateAsync` is the only authorizing operation. It derives workspace, principal, principal kind, roles, grants, connections, policy version, and validity times solely from validated evidence.

`GetWorkspacePresentationsAsync` is display-only. Its names cannot add a workspace, role, grant, connection, or policy version and must never be used for an authorization decision. The built-in adapter returns only the authenticated workspace and uses its opaque ID as the fallback display name.

## OAuth client profiles

Machine-to-machine callers using client credentials must use a confidential client authenticated by a private key or managed workload identity. The issuer must bind the service principal to one workspace and least-privilege grants, issue the same exact audience, omit user impersonation claims, and keep the token within the 15-minute maximum. Shared client secrets in source, configuration files, or fixture tokens are not permitted.

Interactive public clients must use Authorization Code with PKCE using `S256`, a fresh high-entropy verifier and state for every attempt, exact redirect-URI matching, and no client secret. Implicit and resource-owner-password flows are not part of this profile. The resulting access token is presented as opaque evidence; the DigitalBrain caller must not decode it to choose a workspace.

## Local authority

`LocalTestAuthority` is an internal deterministic fixture issuer compiled only into Debug builds. Non-Debug ProductHost assemblies do not contain its type, issuer literal, or fixed signing material, so it cannot be selected or resolved from a Release production surface. Its signing material is public test data, is not a secret, and must never protect a deployment.

## Adapter conformance

External implementations should add a concrete subclass of `AuthorityConformanceTests`, supply their public `IBrainAccessAuthority`, and issue fixture credentials for each `AuthorityFixture`. Run the suite from the DigitalBrain repository root with this exact command:

```powershell
dotnet test tests/CoreV2/Brain.Authority.Conformance.Tests/Brain.Authority.Conformance.Tests.csproj --no-restore
```

The suite verifies rejection of wrong issuer, untrusted signing key, wrong or additional audience, expiry, missing workspace, reserved mapping collisions, duplicate/empty/wrongly shaped closed-schema values; complete grant mapping; presentation separation; the opaque-evidence request shape; Debug fixture usability and Release fixture absence; and the library-only runtime boundary.

`IntoChatAuthorityExample` is a test fixture contract example inside the conformance project. IntoChat is an external implementation, not a DigitalBrain project reference, runtime dependency, identity provider requirement, or privileged integration. Community adapters have the same public SPI and conformance obligations.
