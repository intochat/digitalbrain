# Policies

Policies govern capability access and resource allowances across the DigitalBrain framework.

## Declarative Caps

Policy rules define caps and allowances without requiring custom compilation:
- **Allow**: Grants permission to call designated neurons or capabilities.
- **Release**: Relinquishes or scopes down granted capabilities upon completion or delegation.

## Evaluation

Calls dispatched through `CallFilter` evaluate the caller's active allowances against the policy model:
- `MissingGrant`: Caller lacks explicit permission for the capability.
- `MissingAllowance`: Resource quota or rate limit would be exceeded.
- `LimitReached`: Concurrency or lifetime limit reached.
- `OutsideWorkspace`: Target falls outside the allowed boundary or sandbox path.
