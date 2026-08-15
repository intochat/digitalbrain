import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import '../user_actions/user_action_card.dart';

final class BehaviorOverviewView extends StatelessWidget {
  const BehaviorOverviewView({
    super.key,
    required this.document,
    this.lastRunOutcome,
    this.userActions = const [],
    this.onStop,
    this.onStart,
    this.onRunOnce,
    this.onAskAssistant,
    this.onOpenScenarios,
    this.onOpenSource,
    this.onOpenRevisions,
    this.onToggleBinding,
    this.onOpenUserAction,
  });

  final BehaviorDocument document;
  final String? lastRunOutcome;
  final List<UserActionCardModel> userActions;
  final VoidCallback? onStop;
  final VoidCallback? onStart;
  final VoidCallback? onRunOnce;
  final VoidCallback? onAskAssistant;
  final VoidCallback? onOpenScenarios;
  final VoidCallback? onOpenSource;
  final VoidCallback? onOpenRevisions;
  final void Function(String bindingId, bool enabled)? onToggleBinding;
  final ValueChanged<Uri>? onOpenUserAction;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      key: const Key('behavior_overview'),
      color: BrainPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1080),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 28),
            children: [
              Text(document.displayName, style: BrainType.heading),
              const SizedBox(height: 8),
              Text(document.description, style: BrainType.bodyMuted),
              const SizedBox(height: 16),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  _MetaChip(label: 'status ${document.status}'),
                  _MetaChip(label: 'run ${document.runState}'),
                  _MetaChip(
                    label: document.activationGateOpen ? 'gate open' : 'gate closed',
                  ),
                  if (document.activeArtifactHash != null)
                    _MetaChip(label: 'rev ${document.activeArtifactHash!.substring(0, 8)}…'),
                ],
              ),
              const SizedBox(height: 20),
              _Section(
                title: 'Overview',
                child: Text(document.overview, style: BrainType.body),
              ),
              const SizedBox(height: 16),
              _Section(
                title: 'Scenarios',
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (document.scenarios.isEmpty)
                      const Text('No scenarios yet.', style: BrainType.bodyMuted)
                    else
                      for (final scenario in document.scenarios)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 6),
                          child: Text('• ${scenario.title}', style: BrainType.body),
                        ),
                    const SizedBox(height: 8),
                    TextButton(
                      key: const Key('behavior_open_scenarios'),
                      onPressed: onOpenScenarios,
                      child: const Text('Open scenarios'),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 16),
              _Section(
                title: 'Activation bindings',
                child: document.bindings.isEmpty
                    ? const Text(
                        'No explicit bindings registered yet.',
                        style: BrainType.bodyMuted,
                      )
                    : Column(
                        children: [
                          for (final binding in document.bindings)
                            _BindingRow(
                              binding: binding,
                              onToggle: onToggleBinding == null
                                  ? null
                                  : (enabled) =>
                                        onToggleBinding!(binding.bindingId, enabled),
                            ),
                        ],
                      ),
              ),
              if (userActions.isNotEmpty) ...[
                const SizedBox(height: 16),
                _Section(
                  title: 'User actions',
                  child: Column(
                    children: [
                      for (final action in userActions)
                        UserActionCard(
                          model: action,
                          onAuthorize: onOpenUserAction == null
                              ? null
                              : () => onOpenUserAction!(action.actionUrl),
                        ),
                    ],
                  ),
                ),
              ],
              if (lastRunOutcome != null) ...[
                const SizedBox(height: 16),
                _Section(
                  title: 'Last run',
                  child: Text(lastRunOutcome!, style: BrainType.metaStrong),
                ),
              ],
              const SizedBox(height: 24),
              Wrap(
                spacing: 10,
                runSpacing: 10,
                children: [
                  FilledButton(
                    key: const Key('behavior_run_once'),
                    onPressed: onRunOnce,
                    child: const Text('Run once'),
                  ),
                  if (document.canStop)
                    OutlinedButton(
                      key: const Key('behavior_stop'),
                      onPressed: onStop,
                      child: const Text('Stop'),
                    ),
                  if (document.canStart)
                    OutlinedButton(
                      key: const Key('behavior_start'),
                      onPressed: onStart,
                      child: const Text('Start'),
                    ),
                  OutlinedButton(
                    key: const Key('behavior_ask_assistant'),
                    onPressed: onAskAssistant,
                    child: const Text('Ask assistant to change'),
                  ),
                  TextButton(
                    key: const Key('behavior_open_source'),
                    onPressed: onOpenSource,
                    child: const Text('Source + tests'),
                  ),
                  TextButton(
                    key: const Key('behavior_open_revisions'),
                    onPressed: onOpenRevisions,
                    child: const Text('Revisions'),
                  ),
                ],
              ),
              if (document.canStop) ...[
                const SizedBox(height: 12),
                const Text(
                  key: Key('behavior_stop_confirm'),
                  'Stop cancels active Tasks and closes the activation gate. The behavior and its revision stay installed.',
                  style: BrainType.bodyMuted,
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

final class _BindingRow extends StatelessWidget {
  const _BindingRow({required this.binding, this.onToggle});

  final BehaviorBinding binding;
  final ValueChanged<bool>? onToggle;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${binding.sourceModule} → ${binding.targetCase}',
                  style: BrainType.body,
                ),
                Text(
                  '${binding.sourceSynapse} · v${binding.contractVersion} · ${binding.configurationHint}',
                  style: BrainType.meta,
                ),
              ],
            ),
          ),
          Switch(
            key: Key('behavior_binding_${binding.bindingId}'),
            value: binding.enabled,
            onChanged: onToggle,
          ),
        ],
      ),
    );
  }
}

final class _Section extends StatelessWidget {
  const _Section({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Container(
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
          Text(title, style: BrainType.metaStrong),
          const SizedBox(height: 10),
          child,
        ],
      ),
    );
  }
}

final class _MetaChip extends StatelessWidget {
  const _MetaChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceSunken,
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: BrainPalette.line),
      ),
      child: Text(label, style: BrainType.meta),
    );
  }
}
