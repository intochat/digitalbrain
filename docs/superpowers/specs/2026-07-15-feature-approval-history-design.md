# Bounded Feature Approval History

## Goal

Keep durable Feature approval history bounded without deleting current authorization evidence or making installation reset fail because historical capacity is exhausted.

## Retention invariant

- Every approval whose status is not `Superseded` is mandatory and is never compacted or dropped.
- `Superseded` approvals are historical records. Their embedded `Release.Source` is redundant with the content-addressed `SourceReference` and is stripped; all other release metadata, grants, decision identity, decision actor, timestamps, and revisions remain intact.
- The ledger targets at most 64 total records and at most 4 MiB of deterministically serialized UTF-8 data. Mandatory current records consume this capacity first; only the remaining capacity is available to `Superseded` history.
- Byte accounting is a pure checked walk over every persisted approval, release, source, source-file, grant, and identity field. It counts exact UTF-8 payload bytes plus fixed object-header, field-tag, collection-length, string-length, nullable-marker, enum, integer, and timestamp overhead; it does not allocate serialized buffers.
- Historical candidates are ordered by descending approval revision and then by ordinal approval ID. The retained set is the newest prefix that fits both limits. Selection stops at the first record that would exceed the byte budget; older, smaller records are not opportunistically packed.
- The returned ledger preserves the original relative order of every retained record.
- If mandatory current records by themselves meet or exceed either target, all current records remain and no `Superseded` history is retained. Current records may therefore cause an unavoidable soft total overflow, but never authorize deletion of current evidence.

## Integration

A pure approval-ledger normalizer owns compaction, deterministic byte accounting, and retention. It runs after:

1. proposal appends a current approval;
2. decision adds bounded decision metadata to a current approval; and
3. reset marks the exact approval `Superseded`.

Reset does not reject because the history budget is full; normalization drops old history as needed. Proposal and decision keep their existing exact-reservation and actor checks. Decision IDs remain canonical, control-free, and at most 256 characters.

## Verification

Focused transition tests cover source stripping, 64-record retention, 4 MiB retention, large source/grant inputs, mandatory-current soft overflow, deterministic newest-prefix selection, decision-ID validation, and a real reset/reverify/reproposal loop.
