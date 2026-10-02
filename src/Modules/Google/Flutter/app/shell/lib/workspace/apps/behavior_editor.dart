import 'package:flutter/material.dart';

import 'app_document.dart';

class BehaviorEditor extends StatefulWidget {
  const BehaviorEditor({
    super.key,
    required this.behavior,
    required this.scenarios,
    required this.onSave,
    required this.onReload,
  });
  final AppBehavior behavior;
  final List<AppScenario> scenarios;
  final Future<void> Function(AppBehavior) onSave;
  final Future<void> Function() onReload;
  @override
  State<BehaviorEditor> createState() => _BehaviorEditorState();
}

class _BehaviorEditorState extends State<BehaviorEditor> {
  late final title = TextEditingController(text: widget.behavior.title);
  late final description = TextEditingController(
    text: widget.behavior.description,
  );
  late final selected = widget.behavior.scenarioIds.toSet();
  bool busy = false;
  String? error;
  @override
  void dispose() {
    title.dispose();
    description.dispose();
    super.dispose();
  }

  Future<void> save() async {
    if (title.text.trim().isEmpty) {
      setState(() => error = 'Give this behavior a title.');
      return;
    }
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.onSave(
        widget.behavior.edit(
          title.text.trim(),
          description.text,
          selected.toList(),
        ),
      );
      if (mounted) Navigator.pop(context);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Edit behavior'),
    content: SizedBox(
      width: 540,
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            TextField(
              key: const ValueKey('behavior-title'),
              controller: title,
              decoration: const InputDecoration(labelText: 'Title'),
            ),
            const SizedBox(height: 16),
            TextField(
              key: const ValueKey('behavior-description'),
              controller: description,
              minLines: 3,
              maxLines: 8,
              decoration: const InputDecoration(
                labelText: 'What should happen?',
                alignLabelWithHint: true,
              ),
            ),
            const SizedBox(height: 16),
            const Text('Scenarios that check this behavior'),
            for (final scenario in widget.scenarios)
              CheckboxListTile(
                contentPadding: EdgeInsets.zero,
                title: Text(scenario.name),
                value: selected.contains(scenario.id),
                onChanged: busy
                    ? null
                    : (value) => setState(() {
                        if (value == true) {
                          selected.add(scenario.id);
                        } else {
                          selected.remove(scenario.id);
                        }
                      }),
              ),
            if (widget.scenarios.isEmpty)
              const Text('Add scenarios from the app to check this behavior.'),
            if (error != null) ...[
              SelectableText(
                error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
              TextButton(
                onPressed: busy
                    ? null
                    : () async {
                        setState(() => busy = true);
                        try {
                          await widget.onReload();
                          if (mounted) {
                            setState(
                              () => error = 'Latest draft loaded. Your text is preserved. Review it before saving.',
                            );
                          }
                        } catch (e) {
                          if (mounted) setState(() => error = e.toString());
                        } finally {
                          if (mounted) setState(() => busy = false);
                        }
                      },
                child: const Text('Reload latest draft; keep my text'),
              ),
            ],
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: busy ? null : () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: busy ? null : save,
        child: Text(busy ? 'Saving…' : 'Save'),
      ),
    ],
  );
}
