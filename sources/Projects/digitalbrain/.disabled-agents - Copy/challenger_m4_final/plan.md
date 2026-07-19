# Challenger M4 Verification Plan

This plan details the steps to empirically verify the codebase cleanup for Milestone 4 (Codebase Simplification & Audit).

## Steps

1. **Investigate Environment & Prerequisites**
   - Check if Dart SDK is available and working.
   - Verify that `UI/flutter/assets/ino-catalog.json` exists.
   - Verify that the Dart dependencies in `UI/flutter` are up to date.

2. **Execute M4 Stress Tests**
   - Run `dart run tool/challenger_m4_stress_test.dart` from the `UI/flutter` directory.
   - Capture output and examine if any assertions fail.

3. **Verify Performance and Catalog Parsing Under Stress**
   - Examine `tool/challenger_m4_stress_test.dart` execution logs.
   - Identify responsiveness, memory/parsing errors, and catalog parsing boundaries under simulated stress.
   - Verify wildcard matching rules and bounds behavior.

4. **Document Findings**
   - Create `challenger_report.md` in the working directory summarizing findings and verifying stability.
   - Create `handoff.md` with complete 5-component handoff report.
   - Send completion message to orchestrator.
