import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';

final class BehaviorRevisionsView extends StatelessWidget {
  const BehaviorRevisionsView({
    super.key,
    required this.document,
    this.onRestorePrior,
  });

  final BehaviorDocument document;
  final VoidCallback? onRestorePrior;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      key: const Key('behavior_revisions'),
      color: BrainPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 920),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 28),
            children: [
              const Text('Revisions', style: BrainType.heading),
              const SizedBox(height: 8),
              const Text(
                'Immutable signed history. Restore creates a new verified active revision from prior.',
                style: BrainType.bodyMuted,
              ),
              const SizedBox(height: 20),
              if (document.revisions.isEmpty)
                const Text('No revisions yet.', style: BrainType.bodyMuted)
              else
                for (final revision in document.revisions)
                  _RevisionCard(revision: revision),
              if (document.priorArtifactHash != null) ...[
                const SizedBox(height: 16),
                FilledButton(
                  key: const Key('behavior_restore_prior'),
                  onPressed: onRestorePrior,
                  child: const Text('Restore prior as active revision'),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

final class _RevisionCard extends StatelessWidget {
  const _RevisionCard({required this.revision});

  final BehaviorRevision revision;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(
          color: revision.isActive ? BrainPalette.signal : BrainPalette.line,
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(revision.role, style: BrainType.cardTitle),
              ),
              Text(revision.status, style: BrainType.metaStrong),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            revision.artifactHash ?? '—',
            style: BrainType.meta,
          ),
          if (revision.signatureHex != null) ...[
            const SizedBox(height: 6),
            Text(
              'sig ${revision.signatureHex}',
              style: BrainType.meta,
            ),
          ],
        ],
      ),
    );
  }
}
