#!/bin/sh
# IntoChat restore drill / restore runner (C17).
#
# Restores a nightly snapshot into a fresh stack. RPO 24h, RTO 4h.
#
# Usage:
#   ops/backup/restore.sh --snapshot <dir> [--target grain-storage|ledger|qdrant] [--dry-run]
#
# A snapshot directory must contain:
#   ledger.dump        PostgreSQL custom-format dump of the Compute ledger
#   qdrant.snapshot    Qdrant collection snapshot
#   grain-state.txt    listing of the copied grain-state blobs
#
# Required env for a real restore (not needed for --dry-run):
#   LEDGER_DATABASE_URL, QDRANT_URL, QDRANT_COLLECTION, STORAGE_ACCOUNT, STORAGE_RESTORE_SAS
set -eu

SNAPSHOT=""
TARGET="all"
DRY_RUN=0

while [ $# -gt 0 ]; do
    case "$1" in
        --snapshot) SNAPSHOT="${2:-}"; shift 2 ;;
        --target) TARGET="${2:-}"; shift 2 ;;
        --dry-run) DRY_RUN=1; shift ;;
        -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

if [ -z "$SNAPSHOT" ]; then
    echo "a snapshot directory is required (--snapshot <dir>)" >&2
    exit 2
fi
if [ ! -d "$SNAPSHOT" ]; then
    echo "snapshot directory not found: $SNAPSHOT" >&2
    exit 2
fi

require_snapshot_file() {
    if [ ! -f "$SNAPSHOT/$1" ]; then
        echo "snapshot is missing $1; it is not restorable" >&2
        exit 3
    fi
}

run() {
    if [ "$DRY_RUN" = "1" ]; then
        echo "[dry-run] $*"
        return 0
    fi
    echo "[restore] $*"
    sh -c "$*"
}

restore_grain_storage() {
    require_snapshot_file grain-state.txt
    run "az storage blob copy start-batch \
        --source-container digitalbrain-v2-state-restore \
        --destination-container digitalbrain-v2-state \
        --account-name \"\$STORAGE_ACCOUNT\" --dest-sas \"\$STORAGE_RESTORE_SAS\""
}

restore_ledger() {
    require_snapshot_file ledger.dump
    run "pg_restore --clean --if-exists --no-owner --dbname \"\$LEDGER_DATABASE_URL\" \"$SNAPSHOT/ledger.dump\""
}

restore_qdrant() {
    require_snapshot_file qdrant.snapshot
    run "curl -fsS -X POST \"\$QDRANT_URL/collections/\$QDRANT_COLLECTION/snapshots/upload\" \
        -H \"api-key: \$QDRANT_API_KEY\" --form snapshot=@\"$SNAPSHOT/qdrant.snapshot\""
}

case "$TARGET" in
    all)
        restore_grain_storage
        restore_ledger
        restore_qdrant
        ;;
    grain-storage) restore_grain_storage ;;
    ledger) restore_ledger ;;
    qdrant) restore_qdrant ;;
    *) echo "unknown target: $TARGET" >&2; exit 2 ;;
esac

echo "restore completed for target=$TARGET snapshot=$SNAPSHOT dry_run=$DRY_RUN"
