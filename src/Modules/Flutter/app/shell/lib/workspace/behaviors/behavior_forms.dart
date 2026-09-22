import 'package:flutter/material.dart';

class BehaviorDescriptionDialog extends StatefulWidget {
  const BehaviorDescriptionDialog({super.key, this.current});
  final Map<String, dynamic>? current;
  @override
  State<BehaviorDescriptionDialog> createState() =>
      _BehaviorDescriptionDialogState();
}

class _BehaviorDescriptionDialogState extends State<BehaviorDescriptionDialog> {
  final form = GlobalKey<FormState>();
  late final name = TextEditingController(
    text: widget.current?['name'] as String? ?? '',
  );
  late final purpose = TextEditingController(
    text: widget.current?['purpose'] as String? ?? '',
  );
  late final triggers = TextEditingController(
    text: (widget.current?['triggers'] as List? ?? []).join('\n'),
  );
  late final effects = TextEditingController(
    text: (widget.current?['effects'] as List? ?? []).join('\n'),
  );
  @override
  void dispose() {
    name.dispose();
    purpose.dispose();
    triggers.dispose();
    effects.dispose();
    super.dispose();
  }

  List<String> lines(String value) => value
      .split('\n')
      .map((x) => x.trim())
      .where((x) => x.isNotEmpty)
      .toList();
  Widget descriptions(TextEditingController controller, String label) =>
      TextFormField(
        controller: controller,
        decoration: InputDecoration(
          labelText: label,
          helperText: 'Optional · one per line',
        ),
        minLines: 1,
        maxLines: 3,
        validator: (value) =>
            lines(value ?? '').length > 16 ||
                lines(value ?? '').any((x) => x.length > 240)
            ? 'Use up to 16 descriptions, 240 characters each.'
            : null,
      );
  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.current == null ? 'New behavior' : 'Behavior details'),
    content: SizedBox(
      width: 460,
      child: SingleChildScrollView(
        child: Form(
          key: form,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextFormField(
                controller: name,
                autofocus: true,
                maxLength: 120,
                decoration: const InputDecoration(labelText: 'Name'),
                validator: (v) => v?.trim().isNotEmpty == true
                    ? null
                    : 'Give this behavior a name.',
              ),
              TextFormField(
                controller: purpose,
                minLines: 3,
                maxLines: 5,
                maxLength: 4000,
                decoration: const InputDecoration(
                  labelText: 'What should happen, and when?',
                  hintText: 'When a timer ticks, update a status message.',
                ),
                validator: (v) =>
                    widget.current == null && (v?.trim().isEmpty ?? true)
                    ? 'Describe what you want to happen.'
                    : null,
              ),
              if (widget.current != null) ...[
                descriptions(triggers, 'Declared triggers'),
                descriptions(effects, 'Declared effects'),
              ],
            ],
          ),
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: () {
          if (form.currentState!.validate()) {
            Navigator.pop(context, <String, dynamic>{
              'expectedRevision': widget.current?['revision'] ?? 0,
              'name': name.text.trim(),
              'purpose': purpose.text.trim(),
              'triggers': lines(triggers.text),
              'effects': lines(effects.text),
            });
          }
        },
        child: Text(widget.current == null ? 'Create draft' : 'Save details'),
      ),
    ],
  );
}

class BehaviorFindDialog extends StatefulWidget {
  const BehaviorFindDialog({super.key});
  @override
  State<BehaviorFindDialog> createState() => _BehaviorFindDialogState();
}

class _BehaviorFindDialogState extends State<BehaviorFindDialog> {
  final id = TextEditingController();
  @override
  void dispose() {
    id.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Find an existing behavior'),
    content: TextField(
      controller: id,
      autofocus: true,
      decoration: const InputDecoration(
        labelText: 'Behavior ID',
        hintText: 'timer-status',
        helperText: 'Use the ID from a previous chat in this workspace.',
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: () {
          if (id.text.trim().isNotEmpty) Navigator.pop(context, id.text.trim());
        },
        child: const Text('Find'),
      ),
    ],
  );
}
