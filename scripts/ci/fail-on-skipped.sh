#!/usr/bin/env bash
# Skip guard for the e2e job (04-16, T-04-48): fails when any framework e2e result in the TRX reports under <dir> was skipped,
# so a target that cannot run (Azurite down, a fact turned off) never lets the job pass silently. A missing or empty report fails too.
# Usage: scripts/ci/fail-on-skipped.sh <results-directory>
set -euo pipefail

dir="${1:?usage: fail-on-skipped.sh <results-directory>}"
shopt -s nullglob
reports=("$dir"/*.trx "$dir"/**/*.trx)
if [ "${#reports[@]}" -eq 0 ]; then
  echo "::error::no TRX report in $dir; the e2e facts did not report"
  exit 1
fi

status=0
total=0
for report in "${reports[@]}"; do
  # One UnitTestResult per line, so each outcome stays with its test name.
  results=$(tr -d '\r\n' < "$report" | sed 's/<UnitTestResult /\n<UnitTestResult /g' | grep '^<UnitTestResult ' || true)
  count=$(printf '%s\n' "$results" | grep -c '^<UnitTestResult ' || true)
  total=$((total + count))
  skipped=$(printf '%s\n' "$results" | grep -E 'outcome="(NotExecuted|Skipped|Inconclusive)"' || true)
  if [ -n "$skipped" ]; then
    status=1
    printf '%s\n' "$skipped" | sed -E 's/.*testName="([^"]*)".*/\1/' | sed 's/&quot;/"/g' | while IFS= read -r name; do
      echo "::error::skipped e2e fact: $name"
    done
  fi
  counters=$(grep -o '<Counters [^>]*' "$report" || true)
  for field in notExecuted inconclusive; do
    value=$(printf '%s' "$counters" | sed -nE "s/.*[[:space:]]${field}=\"([0-9]+)\".*/\1/p")
    if [ -n "$value" ] && [ "$value" -gt 0 ]; then
      echo "::error::$report counts $field=$value"
      status=1
    fi
  done
done

if [ "$total" -eq 0 ]; then
  echo "::error::the TRX reports in $dir hold no results"
  exit 1
fi
if [ "$status" -ne 0 ]; then
  echo "::error::e2e facts were skipped; every target must run"
  exit 1
fi
echo "e2e: $total results, none skipped"
