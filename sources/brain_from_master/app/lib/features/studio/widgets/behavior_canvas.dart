import 'package:flutter/material.dart';

import '../feature_studio_models.dart';

const Key featureStudioBehaviorCanvasKey = Key('feature-studio-behavior');

class BehaviorCanvas extends StatelessWidget {
  const BehaviorCanvas({
    super.key,
    required this.behavior,
    required this.errors,
    required this.onChanged,
    required this.enabled,
  });

  final FeatureStudioBehavior behavior;
  final List<String> errors;
  final ValueChanged<FeatureStudioBehavior> onChanged;
  final bool enabled;

  @override
  Widget build(BuildContext context) => Card(
    key: featureStudioBehaviorCanvasKey,
    margin: EdgeInsets.zero,
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Behavior', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 4),
          Text(
            'Describe the situations this Feature should handle and the outcome each one should produce.',
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
              color: Theme.of(context).colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 20),
          for (var index = 0; index < behavior.scenarios.length; index++) ...[
            _ScenarioEditor(
              key: ValueKey(behavior.scenarios[index].scenarioId),
              scenario: behavior.scenarios[index],
              ordinal: index + 1,
              canRemove: behavior.scenarios.length > 1,
              enabled: enabled,
              onChanged: (scenario) => _replace(index, scenario),
              onRemove: () => _remove(index),
            ),
            if (index != behavior.scenarios.length - 1)
              const SizedBox(height: 16),
          ],
          const SizedBox(height: 16),
          Align(
            alignment: Alignment.centerLeft,
            child: OutlinedButton.icon(
              onPressed: enabled ? _add : null,
              icon: const Icon(Icons.add),
              label: const Text('Add Scenario'),
            ),
          ),
          if (errors.isNotEmpty) ...[
            const SizedBox(height: 12),
            Semantics(
              liveRegion: true,
              child: Text(
                errors.first,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          ],
        ],
      ),
    ),
  );

  void _replace(int index, FeatureStudioScenario scenario) {
    final scenarios = behavior.scenarios.toList()..[index] = scenario;
    onChanged(FeatureStudioBehavior(scenarios: scenarios));
  }

  void _remove(int index) {
    final scenarios = behavior.scenarios.toList()..removeAt(index);
    onChanged(FeatureStudioBehavior(scenarios: scenarios));
  }

  void _add() {
    var suffix = behavior.scenarios.length + 1;
    var id = 'scenario-$suffix';
    final existing = behavior.scenarios
        .map((value) => value.scenarioId)
        .toSet();
    while (existing.contains(id)) {
      suffix++;
      id = 'scenario-$suffix';
    }
    onChanged(
      FeatureStudioBehavior(
        scenarios: [
          ...behavior.scenarios,
          FeatureStudioScenario(
            scenarioId: id,
            name: 'New Scenario',
            given: 'A starting situation',
            when: 'The Feature runs',
            then: 'The expected outcome is produced',
          ),
        ],
      ),
    );
  }
}

class _ScenarioEditor extends StatefulWidget {
  const _ScenarioEditor({
    super.key,
    required this.scenario,
    required this.ordinal,
    required this.canRemove,
    required this.enabled,
    required this.onChanged,
    required this.onRemove,
  });

  final FeatureStudioScenario scenario;
  final int ordinal;
  final bool canRemove;
  final bool enabled;
  final ValueChanged<FeatureStudioScenario> onChanged;
  final VoidCallback onRemove;

  @override
  State<_ScenarioEditor> createState() => _ScenarioEditorState();
}

class _ScenarioEditorState extends State<_ScenarioEditor> {
  late final TextEditingController _name;
  late final TextEditingController _given;
  late final TextEditingController _when;
  late final TextEditingController _then;

  @override
  void initState() {
    super.initState();
    _name = TextEditingController(text: widget.scenario.name);
    _given = TextEditingController(text: widget.scenario.given);
    _when = TextEditingController(text: widget.scenario.when);
    _then = TextEditingController(text: widget.scenario.then);
  }

  @override
  void didUpdateWidget(covariant _ScenarioEditor oldWidget) {
    super.didUpdateWidget(oldWidget);
    _sync(_name, widget.scenario.name);
    _sync(_given, widget.scenario.given);
    _sync(_when, widget.scenario.when);
    _sync(_then, widget.scenario.then);
  }

  @override
  void dispose() {
    _name.dispose();
    _given.dispose();
    _when.dispose();
    _then.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => DecoratedBox(
    decoration: BoxDecoration(
      color: Theme.of(context).colorScheme.surfaceContainerLow,
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: Theme.of(context).colorScheme.outlineVariant),
    ),
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  'Scenario ${widget.ordinal}',
                  style: Theme.of(context).textTheme.labelMedium,
                ),
              ),
              IconButton(
                tooltip: 'Remove Scenario',
                onPressed: widget.enabled && widget.canRemove
                    ? widget.onRemove
                    : null,
                icon: const Icon(Icons.delete_outline),
              ),
            ],
          ),
          TextFormField(
            key: ValueKey('scenario-${widget.scenario.scenarioId}-name'),
            controller: _name,
            enabled: widget.enabled,
            decoration: const InputDecoration(labelText: 'Scenario name'),
            textInputAction: TextInputAction.next,
            onChanged: (value) => widget.onChanged(_copy(name: value)),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: ValueKey('scenario-${widget.scenario.scenarioId}-given'),
            controller: _given,
            enabled: widget.enabled,
            decoration: const InputDecoration(labelText: 'Given'),
            minLines: 1,
            maxLines: 3,
            onChanged: (value) => widget.onChanged(_copy(given: value)),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: ValueKey('scenario-${widget.scenario.scenarioId}-when'),
            controller: _when,
            enabled: widget.enabled,
            decoration: const InputDecoration(labelText: 'When'),
            minLines: 1,
            maxLines: 3,
            onChanged: (value) => widget.onChanged(_copy(when: value)),
          ),
          const SizedBox(height: 12),
          TextFormField(
            key: ValueKey('scenario-${widget.scenario.scenarioId}-then'),
            controller: _then,
            enabled: widget.enabled,
            decoration: const InputDecoration(labelText: 'Then'),
            minLines: 1,
            maxLines: 3,
            onChanged: (value) => widget.onChanged(_copy(then: value)),
          ),
        ],
      ),
    ),
  );

  FeatureStudioScenario _copy({
    String? name,
    String? given,
    String? when,
    String? then,
  }) => FeatureStudioScenario(
    scenarioId: widget.scenario.scenarioId,
    name: name ?? widget.scenario.name,
    given: given ?? widget.scenario.given,
    when: when ?? widget.scenario.when,
    then: then ?? widget.scenario.then,
  );

  void _sync(TextEditingController controller, String value) {
    if (controller.text == value) return;
    controller.value = TextEditingValue(
      text: value,
      selection: TextSelection.collapsed(offset: value.length),
    );
  }
}
