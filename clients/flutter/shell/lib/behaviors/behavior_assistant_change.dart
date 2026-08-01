import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';

final class BehaviorAssistantChangeView extends StatefulWidget {
  const BehaviorAssistantChangeView({
    super.key,
    required this.document,
    this.proposal,
    this.onPropose,
    this.onApproveScenario,
    this.onRejectScenario,
  });

  final BehaviorDocument document;
  final BehaviorChangeProposal? proposal;
  final ValueChanged<String>? onPropose;
  final VoidCallback? onApproveScenario;
  final VoidCallback? onRejectScenario;

  @override
  State<BehaviorAssistantChangeView> createState() =>
      _BehaviorAssistantChangeViewState();
}

final class _BehaviorAssistantChangeViewState
    extends State<BehaviorAssistantChangeView> {
  late final TextEditingController _request;

  @override
  void initState() {
    super.initState();
    _request = TextEditingController();
  }

  @override
  void dispose() {
    _request.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final proposal = widget.proposal;
    return ColoredBox(
      key: const Key('behavior_assistant_change'),
      color: BrainPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 920),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 28),
            children: [
              const Text('Assistant change', style: BrainType.heading),
              const SizedBox(height: 8),
              const Text(
                'Describe the change in plain language. Scenario diffs must be approved before any source generation.',
                style: BrainType.bodyMuted,
              ),
              const SizedBox(height: 20),
              TextField(
                key: const Key('behavior_change_request'),
                controller: _request,
                minLines: 3,
                maxLines: 6,
                style: BrainType.body,
                decoration: InputDecoration(
                  hintText: 'What should this behavior do differently?',
                  filled: true,
                  fillColor: BrainPalette.surfaceRaised,
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(12),
                    borderSide: const BorderSide(color: BrainPalette.line),
                  ),
                ),
              ),
              const SizedBox(height: 12),
              Align(
                alignment: Alignment.centerLeft,
                child: FilledButton(
                  key: const Key('behavior_change_propose'),
                  onPressed: widget.onPropose == null
                      ? null
                      : () => widget.onPropose!(_request.text),
                  child: const Text('Propose scenario change'),
                ),
              ),
              if (proposal != null) ...[
                const SizedBox(height: 24),
                Container(
                  key: const Key('behavior_change_proposal'),
                  padding: const EdgeInsets.all(18),
                  decoration: BoxDecoration(
                    color: BrainPalette.surfaceRaised,
                    borderRadius: BorderRadius.circular(14),
                    border: Border.all(color: BrainPalette.line),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(proposal.status, style: BrainType.metaStrong),
                      if (proposal.diffSummary != null) ...[
                        const SizedBox(height: 8),
                        Text(proposal.diffSummary!, style: BrainType.body),
                      ],
                      const SizedBox(height: 12),
                      SelectableText(
                        proposal.proposedFeatureText,
                        style: BrainType.body.copyWith(
                          fontFamily: BrainType.monoFamily,
                          fontFamilyFallback: BrainType.monoFallback,
                        ),
                      ),
                      const SizedBox(height: 16),
                      if (proposal.awaitsScenarioApproval)
                        Row(
                          children: [
                            FilledButton(
                              key: const Key('behavior_change_approve_scenario'),
                              onPressed: widget.onApproveScenario,
                              child: const Text('Approve scenarios'),
                            ),
                            const SizedBox(width: 10),
                            OutlinedButton(
                              key: const Key('behavior_change_reject_scenario'),
                              onPressed: widget.onRejectScenario,
                              child: const Text('Reject'),
                            ),
                          ],
                        )
                      else
                        Text(
                          'Code generation is blocked until scenarios are approved.',
                          style: BrainType.bodyMuted,
                          key: const Key('behavior_change_blocked'),
                        ),
                    ],
                  ),
                ),
              ],
              const SizedBox(height: 20),
              Text(
                'Current status: ${widget.document.status}. Red admission evidence never auto-publishes.',
                style: BrainType.meta,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
