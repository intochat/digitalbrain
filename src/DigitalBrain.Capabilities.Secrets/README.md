# DigitalBrain.Capabilities.Secrets

The `secrets@1` capability contract: the named secrets of a brain.

- `ISecrets` (`[Capability("secrets", 1)]`): `Set(name, label, value)` returns a `SecretRef`, `Remove(name)`,
  `Resolve(SecretRef)` returns the value.
- `Seal(context, plaintext)` returns a `SealedSecret` (`cipher`, `keyId`; no plaintext, safe in neuron state) and
  `Open(context, sealed)` returns the plaintext; the context is bound as associated data, so another context or a
  tampered value is `SecretDenied` (09-03, for TOTP seeds).
- `Named(name)` returns the reference of a deployment-provided secret (`named:<name>`, unset when not configured) for
  `Resolve`. The caller's module must declare the secret in its manifest `Secrets` list (the declared
  `signin-issuer` covers `signin-issuer:SigningKey`); anything else is `SecretDenied`.
- `ISecretVaultPort` gains `Seal`, `Open` and `Named` with fail-closed defaults, so older ports keep compiling.
- No caller parameter: the implementation reads the caller the runtime stamped on the call (D-15).
- `SecretRef(Reference, Label, Status)` never holds the value; `ToString()` prints `secret <name> (set|unset)`.
- Errors derive from `SecretException` with stable codes `not-found` (`SecretNotFound`) and `denied` (`SecretDenied`);
  messages carry the name only.

References only `DigitalBrain`; no Orleans, no ASP.NET Core.
