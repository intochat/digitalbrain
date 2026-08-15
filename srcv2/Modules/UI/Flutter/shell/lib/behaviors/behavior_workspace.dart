import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import '../user_actions/user_action_card.dart';
import 'behavior_assistant_change.dart';
import 'behavior_library.dart';
import 'behavior_overview.dart';
import 'behavior_revisions.dart';
import 'behavior_scenarios.dart';
import 'behavior_source.dart';
import 'behavior_view_model.dart';

final class BehaviorWorkspace extends StatefulWidget {
  const BehaviorWorkspace({
    super.key,
    this.client,
    this.userActions = const [],
    this.onOpenUserAction,
  });

  final BehaviorClient? client;
  final List<UserActionCardModel> userActions;
  final ValueChanged<Uri>? onOpenUserAction;

  @override
  State<BehaviorWorkspace> createState() => _BehaviorWorkspaceState();
}

final class _BehaviorWorkspaceState extends State<BehaviorWorkspace> {
  late final BehaviorStudioController _controller;

  @override
  void initState() {
    super.initState();
    _controller = BehaviorStudioController(client: widget.client);
    _controller.addListener(_onChanged);
    // Always refresh: offline → demo fixtures; empty edge → demo fixtures;
    // live grains → edge library.
    _controller.refreshLibrary();
  }

  @override
  void dispose() {
    _controller
      ..removeListener(_onChanged)
      ..dispose();
    super.dispose();
  }

  void _onChanged() {
    if (mounted) {
      setState(() {});
    }
  }

  @override
  Widget build(BuildContext context) {
    final selected = _controller.selected;
    return Column(
      key: const Key('behavior_workspace'),
      children: [
        if (selected != null)
          _DetailChrome(
            document: selected,
            view: _controller.view,
            onBack: _controller.backToLibrary,
            onSelectView: _controller.showView,
          ),
        if (_controller.statusMessage != null)
          Container(
            width: double.infinity,
            color: BrainPalette.surfaceSunken,
            padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 10),
            child: Text(_controller.statusMessage!, style: BrainType.meta),
          ),
        Expanded(child: _body()),
      ],
    );
  }

  Widget _body() {
    final selected = _controller.selected;
    switch (_controller.view) {
      case BehaviorStudioView.library:
        return BehaviorLibraryView(
          items: _controller.library,
          loading: _controller.loading,
          error: _controller.showingDemoFixtures
              ? null
              : _controller.statusMessage,
          onRefresh: _controller.refreshLibrary,
          onOpen: _controller.openBehavior,
        );
      case BehaviorStudioView.overview:
        return BehaviorOverviewView(
          document: selected!,
          lastRunOutcome: _controller.lastRunOutcome,
          userActions: widget.userActions,
          onStop: _controller.stopSelected,
          onStart: _controller.startSelected,
          onRunOnce: _controller.runOnceSelected,
          onAskAssistant: () => _controller.showView(BehaviorStudioView.assistantChange),
          onOpenScenarios: () => _controller.showView(BehaviorStudioView.scenarios),
          onOpenSource: () => _controller.showView(BehaviorStudioView.source),
          onOpenRevisions: () => _controller.showView(BehaviorStudioView.revisions),
          onToggleBinding: _controller.setBindingEnabled,
          onOpenUserAction: widget.onOpenUserAction,
        );
      case BehaviorStudioView.scenarios:
        return BehaviorScenariosView(document: selected!);
      case BehaviorStudioView.assistantChange:
        return BehaviorAssistantChangeView(
          document: selected!,
          proposal: _controller.pendingProposal,
          onPropose: _controller.proposeChange,
          onApproveScenario: () => _controller.approvePendingScenario(approved: true),
          onRejectScenario: () => _controller.approvePendingScenario(approved: false),
        );
      case BehaviorStudioView.source:
        return BehaviorSourceView(
          document: selected!,
          onPropose: (program, feature) => _controller.proposeSource(
            programSource: program,
            featureText: feature,
          ),
          onRunTests: _controller.runTestsSelected,
          onApprove: _controller.approveSelected,
          onActivate: _controller.activateSelected,
        );
      case BehaviorStudioView.revisions:
        return BehaviorRevisionsView(
          document: selected!,
          onRestorePrior: _controller.rollbackSelected,
        );
    }
  }
}

final class _DetailChrome extends StatelessWidget {
  const _DetailChrome({
    required this.document,
    required this.view,
    required this.onBack,
    required this.onSelectView,
  });

  final BehaviorDocument document;
  final BehaviorStudioView view;
  final VoidCallback onBack;
  final ValueChanged<BehaviorStudioView> onSelectView;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 52,
      padding: const EdgeInsets.symmetric(horizontal: 16),
      decoration: const BoxDecoration(
        color: BrainPalette.surfaceRaised,
        border: Border(bottom: BorderSide(color: BrainPalette.line)),
      ),
      child: Row(
        children: [
          TextButton.icon(
            key: const Key('behavior_back_library'),
            onPressed: onBack,
            icon: const Icon(Icons.arrow_back, size: 16),
            label: const Text('Library'),
          ),
          const SizedBox(width: 8),
          Text(document.displayName, style: BrainType.metaStrong),
          const Spacer(),
          for (final entry in _tabs)
            Padding(
              padding: const EdgeInsets.only(left: 4),
              child: TextButton(
                key: Key('behavior_tab_${entry.$1.name}'),
                onPressed: () => onSelectView(entry.$1),
                child: Text(
                  entry.$2,
                  style: view == entry.$1
                      ? BrainType.metaStrong.copyWith(color: BrainPalette.signal)
                      : BrainType.meta,
                ),
              ),
            ),
        ],
      ),
    );
  }
}

const _tabs = <(BehaviorStudioView, String)>[
  (BehaviorStudioView.overview, 'Overview'),
  (BehaviorStudioView.scenarios, 'Scenarios'),
  (BehaviorStudioView.assistantChange, 'Assistant'),
  (BehaviorStudioView.source, 'Source'),
  (BehaviorStudioView.revisions, 'Revisions'),
];
