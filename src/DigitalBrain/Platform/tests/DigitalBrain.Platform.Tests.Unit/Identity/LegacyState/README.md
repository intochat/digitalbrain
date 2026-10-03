# Legacy identity fixtures

These are checked-in binary compatibility fixtures, not application data or build output.

- `directory.orleans`: synthetic accounts, memberships, invitations and password hashes.
- `grants.orleans`: synthetic grant records.
- `Capture.cs.txt`: the capture program and original source revision, `e39ecb35df23b80ef2f9340ea80a8bd6c8f8e3a5`.

`LegacyIdentityStateFacts` reads these bytes through `OrleansGrainStorageSerializer` to
verify that state produced before the refactor can still be deserialized and migrated.
The fixtures moved here from the Kernel test suite when tests were split by package.

Keep the binary files unchanged. Regenerating them with current classes would lose the
old-format compatibility check. They contain synthetic test credentials only.
