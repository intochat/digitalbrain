import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';

typedef LoadBehaviors = Future<List<BehaviorSummary>> Function();
typedef LoadBehaviorSteps = Future<List<BehaviorStepSuggestion>> Function();
typedef SaveBehavior = Future<void> Function(String name, String source);
typedef TestBehavior = Future<BehaviorTestReport> Function(String name);
typedef ActivateBehavior =
    Future<void> Function(String name, {required bool active});
typedef RunBehaviorFake = Future<String> Function(String name);
typedef GenerateBehavior = Future<BehaviorGeneration> Function(String request);

final class BehaviorWorkspace extends StatefulWidget {
  const BehaviorWorkspace({
    super.key,
    this.onLoad,
    this.onLoadSteps,
    this.onSave,
    this.onTest,
    this.onActivate,
    this.onRunFake,
    this.onGenerate,
  });

  final LoadBehaviors? onLoad;
  final LoadBehaviorSteps? onLoadSteps;
  final SaveBehavior? onSave;
  final TestBehavior? onTest;
  final ActivateBehavior? onActivate;
  final RunBehaviorFake? onRunFake;
  final GenerateBehavior? onGenerate;

  @override
  State<BehaviorWorkspace> createState() => _BehaviorWorkspaceState();
}

final class _BehaviorWorkspaceState extends State<BehaviorWorkspace> {
  final _source = GherkinEditingController();
  final _request = TextEditingController();
  List<BehaviorSummary> _behaviors = const [];
  List<BehaviorStepSuggestion> _steps = const [];
  BehaviorSummary? _selected;
  String _status = 'Loading Behaviors…';
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _source.addListener(_sourceChanged);
    _load();
  }

  void _sourceChanged() => setState(() {});

  Future<void> _load() async {
    if (widget.onLoad == null) {
      setState(
        () => _status =
            'Connect to the DigitalBrain edge to edit and run Behaviors.',
      );
      return;
    }
    try {
      final values = await Future.wait<Object>([
        widget.onLoad!(),
        widget.onLoadSteps?.call() ?? Future.value(<BehaviorStepSuggestion>[]),
      ]);
      _behaviors = values[0] as List<BehaviorSummary>;
      _steps = values[1] as List<BehaviorStepSuggestion>;
      if (_behaviors.isNotEmpty) _select(_behaviors.first);
      setState(() => _status = '${_behaviors.length} Reqnroll Behaviors ready');
    } catch (error) {
      setState(() => _status = 'Could not load Behaviors: $error');
    }
  }

  void _select(BehaviorSummary behavior) {
    _selected = behavior;
    _source.text = behavior.source;
    _source.selection = const TextSelection.collapsed(offset: 0);
    setState(() {});
  }

  Future<void> _perform(
    String progress,
    Future<String> Function() action,
  ) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _status = progress;
    });
    try {
      final result = await action();
      setState(() => _status = result);
    } catch (error) {
      setState(() => _status = '$progress failed: $error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  void dispose() {
    _source
      ..removeListener(_sourceChanged)
      ..dispose();
    _request.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Padding(
    key: const Key('behavior_workspace'),
    padding: const EdgeInsets.all(20),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text('Behavior IDE', style: BrainType.heading),
        const SizedBox(height: 4),
        Text(
          _status,
          key: const Key('behavior_status'),
          style: BrainType.bodyMuted,
        ),
        const SizedBox(height: 16),
        Expanded(
          child: LayoutBuilder(
            builder: (context, constraints) {
              final editor = _buildEditor();
              return constraints.maxWidth < 850
                  ? Column(
                      children: [
                        SizedBox(height: 150, child: _buildList()),
                        const SizedBox(height: 12),
                        Expanded(child: editor),
                      ],
                    )
                  : Row(
                      children: [
                        SizedBox(width: 280, child: _buildList()),
                        const SizedBox(width: 16),
                        Expanded(child: editor),
                      ],
                    );
            },
          ),
        ),
      ],
    ),
  );

  Widget _buildList() => Card(
    color: BrainPalette.surfaceRaised,
    child: _behaviors.isEmpty
        ? const Center(
            child: Text('No Behaviors loaded', style: BrainType.bodyMuted),
          )
        : ListView.builder(
            itemCount: _behaviors.length,
            itemBuilder: (context, index) {
              final item = _behaviors[index];
              return ListTile(
                key: Key('behavior_${item.name}'),
                selected: identical(item, _selected),
                leading: Icon(
                  item.active
                      ? Icons.play_circle_fill
                      : Icons.pause_circle_outline,
                  color: item.active ? Colors.greenAccent : BrainPalette.signal,
                ),
                title: Text(item.title, maxLines: 2),
                subtitle: Text(
                  '@behavior · ${item.lastTest?.scenarios ?? 0} test',
                ),
                onTap: () => _select(item),
              );
            },
          ),
  );

  Widget _buildEditor() {
    final selected = _selected;
    final query = _source.text.split('\n').last.trim().toLowerCase();
    final matches = _steps
        .where((step) {
          if (query.isEmpty) return false;
          final candidate = '${step.keyword} ${step.template}'.toLowerCase();
          return candidate.contains(query) ||
              step.description.toLowerCase().contains(query);
        })
        .take(4)
        .toList();
    return Card(
      color: BrainPalette.surfaceRaised,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    selected?.name ?? 'Select a Behavior',
                    style: BrainType.cardTitle,
                  ),
                ),
                if (selected != null)
                  Chip(
                    label: Text(selected.active ? 'ACTIVE' : 'DRAFT'),
                    avatar: const Icon(Icons.science_outlined, size: 16),
                  ),
              ],
            ),
            const SizedBox(height: 10),
            Expanded(
              child: TextField(
                key: const Key('behavior_editor'),
                controller: _source,
                expands: true,
                maxLines: null,
                minLines: null,
                style: const TextStyle(
                  fontFamily: 'Consolas',
                  fontSize: 14,
                  height: 1.5,
                ),
                decoration: const InputDecoration(
                  border: OutlineInputBorder(),
                  filled: true,
                  fillColor: BrainPalette.surfaceSunken,
                  hintText: 'Feature: My behavior\n  @behavior\n  Scenario: …',
                ),
              ),
            ),
            if (matches.isNotEmpty)
              SizedBox(
                height: 52,
                child: ListView(
                  scrollDirection: Axis.horizontal,
                  children: [
                    for (final step in matches)
                      Padding(
                        padding: const EdgeInsets.only(right: 8),
                        child: ActionChip(
                          label: Text('${step.keyword} ${step.template}'),
                          tooltip: step.description,
                          onPressed: () => _insertStep(step),
                        ),
                      ),
                  ],
                ),
              ),
            const SizedBox(height: 10),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                FilledButton.icon(
                  key: const Key('behavior_save'),
                  onPressed: selected == null || widget.onSave == null || _busy
                      ? null
                      : () => _perform('Compiling…', () async {
                          await widget.onSave!(selected.name, _source.text);
                          return 'Saved and compiled ${selected.name}';
                        }),
                  icon: const Icon(Icons.save_outlined),
                  label: const Text('Compile & save'),
                ),
                OutlinedButton.icon(
                  key: const Key('behavior_test'),
                  onPressed: selected == null || widget.onTest == null || _busy
                      ? null
                      : () => _perform('Running paired scenarios…', () async {
                          final report = await widget.onTest!(selected.name);
                          return report.allGreen
                              ? '${report.scenarios} scenarios green'
                              : report.failures.join('; ');
                        }),
                  icon: const Icon(Icons.fact_check_outlined),
                  label: const Text('Test'),
                ),
                OutlinedButton.icon(
                  key: const Key('behavior_fake'),
                  onPressed:
                      selected == null || widget.onRunFake == null || _busy
                      ? null
                      : () => _perform(
                          'Publishing fake event…',
                          () => widget.onRunFake!(selected.name),
                        ),
                  icon: const Icon(Icons.science_outlined),
                  label: const Text('Run fake'),
                ),
                OutlinedButton.icon(
                  key: const Key('behavior_toggle'),
                  onPressed:
                      selected == null || widget.onActivate == null || _busy
                      ? null
                      : () => _perform('Updating activation…', () async {
                          await widget.onActivate!(
                            selected.name,
                            active: !selected.active,
                          );
                          return selected.active
                              ? 'Behavior disabled'
                              : 'Behavior activated';
                        }),
                  icon: Icon(
                    selected?.active == true ? Icons.pause : Icons.play_arrow,
                  ),
                  label: Text(
                    selected?.active == true ? 'Disable' : 'Activate',
                  ),
                ),
              ],
            ),
            const Divider(height: 24),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    key: const Key('behavior_generate_request'),
                    controller: _request,
                    decoration: const InputDecoration(
                      labelText: 'Ask local Gemma to draft a Behavior',
                      border: OutlineInputBorder(),
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                IconButton.filled(
                  key: const Key('behavior_generate'),
                  tooltip: 'Generate with Gemma 4',
                  onPressed: widget.onGenerate == null || _busy
                      ? null
                      : () => _perform('Gemma 4 is compiling a draft…', () async {
                          final generated = await widget.onGenerate!(
                            _request.text,
                          );
                          _source.text = generated.source;
                          return generated.success
                              ? 'Generated by ${generated.model}; compiler is green'
                              : 'Generated draft has ${generated.diagnostics.length} diagnostics';
                        }),
                  icon: const Icon(Icons.auto_awesome),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  void _insertStep(BehaviorStepSuggestion step) {
    final lines = _source.text.split('\n');
    lines[lines.length - 1] = '    ${step.keyword} ${step.template}';
    _source.text = lines.join('\n');
    _source.selection = TextSelection.collapsed(offset: _source.text.length);
  }
}

final class GherkinEditingController extends TextEditingController {
  static final _tokens = RegExp(
    r'(@behavior|@test|\bFeature:|\bScenario:|\bGiven\b|\bWhen\b|\bThen\b|\bAnd\b|"[^"]*")',
  );

  @override
  TextSpan buildTextSpan({
    required BuildContext context,
    TextStyle? style,
    required bool withComposing,
  }) {
    final spans = <TextSpan>[];
    var cursor = 0;
    for (final match in _tokens.allMatches(text)) {
      if (match.start > cursor) {
        spans.add(
          TextSpan(text: text.substring(cursor, match.start), style: style),
        );
      }
      final token = match.group(0)!;
      final color = token.startsWith('"')
          ? Colors.amberAccent
          : token.startsWith('@')
          ? Colors.purpleAccent
          : token.endsWith(':')
          ? Colors.cyanAccent
          : Colors.lightBlueAccent;
      spans.add(
        TextSpan(
          text: token,
          style: style?.copyWith(color: color, fontWeight: FontWeight.w600),
        ),
      );
      cursor = match.end;
    }
    if (cursor < text.length) {
      spans.add(TextSpan(text: text.substring(cursor), style: style));
    }
    return TextSpan(style: style, children: spans);
  }
}
