# Task Brief: Worker Milestone 4 (Sweep Orphaned Files)
- Working Directory: E:\digitalbrain\.agents\worker_m4\
- Role: Perform the Sweep Orphaned Files (Milestone 4) tasks in the implementation plan.
  1. Identify files reported by `flutter analyze` as unused/unreferenced (`unused_import`, `unused_element`, or completely dead code files).
  2. For each candidate file, check that there are no inbound imports from keepers (using `grep -rl`).
  3. Delete confirmed-orphaned widget, editor, and helper files in `UI/flutter/lib/` recursively.
  4. Run analyze & prune cycles repeatedly until no unused warnings remain from the deleted screens and all compilations are perfectly clean.
  5. Write your handoff report to `E:\digitalbrain\.agents\worker_m4\handoff.md`.
