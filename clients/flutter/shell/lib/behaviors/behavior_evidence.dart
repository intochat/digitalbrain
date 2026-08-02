import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';

final class BehaviorEvidencePanel extends StatelessWidget {
  const BehaviorEvidencePanel({super.key, required this.document});

  final BehaviorDocument document;

  @override
  Widget build(BuildContext context) {
    return Container(
      key: const Key('behavior_evidence'),
      width: double.infinity,
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Admission evidence', style: BrainType.metaStrong),
          const SizedBox(height: 12),
          _row('Compile', document.lastCompileFailure == null ? 'ok' : 'failed'),
          _row('Scenarios / tests', document.testsPassed ? 'passed' : 'not green'),
          _row('Approved', document.isApproved ? 'yes' : 'no'),
          _row('Status', document.status),
          if (document.lastCompileFailure != null) ...[
            const SizedBox(height: 10),
            Text(document.lastCompileFailure!, style: BrainType.bodyMuted),
          ],
          if (document.lastExecutionOutcome != null) ...[
            const SizedBox(height: 10),
            Text(
              'Last execution: ${document.lastExecutionOutcome}',
              style: BrainType.meta,
            ),
          ],
        ],
      ),
    );
  }

  Widget _row(String label, String value) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Row(
        children: [
          SizedBox(width: 140, child: Text(label, style: BrainType.meta)),
          Expanded(child: Text(value, style: BrainType.body)),
        ],
      ),
    );
  }
}
