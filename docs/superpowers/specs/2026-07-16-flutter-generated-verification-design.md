# Deterministic Flutter Generated Verification Design

## Problem

DigitalBrain requires every tracked source and configuration file to remain comment-free. Flutter rewrites seven desktop plugin registrants from comment-bearing SDK templates during ordinary commands such as `flutter test`. Tracking sanitized copies therefore makes a verified checkout dirty again and restores the repository-policy failure.

## Decision

Treat exactly these seven Flutter-owned registrants as generated build artifacts:

- `app/linux/flutter/generated_plugin_registrant.cc`
- `app/linux/flutter/generated_plugin_registrant.h`
- `app/linux/flutter/generated_plugins.cmake`
- `app/macos/Flutter/GeneratedPluginRegistrant.swift`
- `app/windows/flutter/generated_plugin_registrant.cc`
- `app/windows/flutter/generated_plugin_registrant.h`
- `app/windows/flutter/generated_plugins.cmake`

Remove them from Git tracking and add exact root-relative ignore entries. Flutter remains their sole producer. No other platform source, runner file, CMake file, or generated output changes classification.

## Enforcement

`RepositoryPolicyTests` owns the invariant. A focused test names the seven paths and proves each is both absent from `git ls-files` and matched by `git check-ignore --no-index`. This prevents accidental recommits and overly broad reliance on developer-local Git settings.

## Verification

Start from the tracked files absent, run the normal Flutter dependency/test workflow, and prove all seven files are regenerated while `git status --short` remains clean. Then run the policy suite, Studio goldens twice, full Flutter twice, the exact root .NET command with the AppHost stopped, Aspire doctor after restart, and `git diff --check`.

## Scope

No Flutter SDK patch, Git hook, wrapper command, package change, product behavior change, or comment-policy exemption is permitted. The existing seven corrected Studio goldens remain unchanged.
