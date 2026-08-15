import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import 'behavior_evidence.dart';

final class BehaviorSourceView extends StatefulWidget {
  const BehaviorSourceView({
    super.key,
    required this.document,
    this.onPropose,
    this.onRunTests,
    this.onApprove,
    this.onActivate,
  });

  final BehaviorDocument document;
  final void Function(String programSource, String featureText)? onPropose;
  final VoidCallback? onRunTests;
  final VoidCallback? onApprove;
  final VoidCallback? onActivate;

  @override
  State<BehaviorSourceView> createState() => _BehaviorSourceViewState();
}

final class _BehaviorSourceViewState extends State<BehaviorSourceView> {
  late final TextEditingController _program;
  late final TextEditingController _feature;
  var _editing = false;

  @override
  void initState() {
    super.initState();
    _program = TextEditingController(text: widget.document.programSource);
    _feature = TextEditingController(text: widget.document.featureText);
  }

  @override
  void didUpdateWidget(covariant BehaviorSourceView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.document.behaviorId != widget.document.behaviorId
        || oldWidget.document.proposedArtifactHash !=
            widget.document.proposedArtifactHash) {
      _program.text = widget.document.programSource;
      _feature.text = widget.document.featureText;
      _editing = false;
    }
  }

  @override
  void dispose() {
    _program.dispose();
    _feature.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final document = widget.document;
    return ColoredBox(
      key: const Key('behavior_source'),
      color: BrainPalette.surface,
      child: Align(
        alignment: Alignment.topCenter,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1200),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 28),
            children: [
              Row(
                children: [
                  const Expanded(
                    child: Text('Source + tests', style: BrainType.heading),
                  ),
                  TextButton(
                    key: const Key('behavior_source_edit_toggle'),
                    onPressed: () => setState(() => _editing = !_editing),
                    child: Text(_editing ? 'Read only' : 'Edit authored files'),
                  ),
                  if (_editing)
                    FilledButton(
                      key: const Key('behavior_source_save_proposal'),
                      onPressed: widget.onPropose == null
                          ? null
                          : () => widget.onPropose!(_program.text, _feature.text),
                      child: const Text('Save proposal'),
                    ),
                ],
              ),
              const SizedBox(height: 8),
              const Text(
                'Generated overview stays read-only. Only Behavior.cs and Behavior.feature are authored.',
                style: BrainType.bodyMuted,
              ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  FilledButton.tonal(
                    key: const Key('behavior_source_run_tests'),
                    onPressed: document.proposedArtifactHash == null
                        ? null
                        : widget.onRunTests,
                    child: const Text('Run tests'),
                  ),
                  FilledButton.tonal(
                    key: const Key('behavior_source_approve'),
                    onPressed: document.proposedArtifactHash == null || !document.testsPassed
                        ? null
                        : widget.onApprove,
                    child: const Text('Approve'),
                  ),
                  FilledButton(
                    key: const Key('behavior_source_activate'),
                    onPressed: document.proposedArtifactHash == null || !document.isApproved
                        ? null
                        : widget.onActivate,
                    child: const Text('Activate'),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              BehaviorEvidencePanel(document: document),
              const SizedBox(height: 16),
              _Section(
                title: 'Generated overview (read-only)',
                child: Text(document.overview, style: BrainType.body),
              ),
              const SizedBox(height: 16),
              _EditorPane(
                title: 'Behavior.cs',
                controller: _program,
                readOnly: !_editing,
                keyName: 'behavior_program_source',
              ),
              const SizedBox(height: 16),
              _EditorPane(
                title: 'Behavior.feature',
                controller: _feature,
                readOnly: !_editing,
                keyName: 'behavior_feature_source',
              ),
            ],
          ),
        ),
      ),
    );
  }
}

final class _EditorPane extends StatelessWidget {
  const _EditorPane({
    required this.title,
    required this.controller,
    required this.readOnly,
    required this.keyName,
  });

  final String title;
  final TextEditingController controller;
  final bool readOnly;
  final String keyName;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
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
          TextField(
            key: Key(keyName),
            controller: controller,
            readOnly: readOnly,
            minLines: 12,
            maxLines: 24,
            style: BrainType.body.copyWith(
              fontFamily: BrainType.monoFamily,
              fontFamilyFallback: BrainType.monoFallback,
              fontSize: 13,
            ),
            decoration: const InputDecoration(
              border: InputBorder.none,
              isCollapsed: true,
            ),
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
