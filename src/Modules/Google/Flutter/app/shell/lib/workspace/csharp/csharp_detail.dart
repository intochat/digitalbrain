import 'package:flutter/material.dart';

import 'csharp_forms.dart';
import 'csharp_manager.dart';

class CSharpDetailView extends StatefulWidget {
  const CSharpDetailView({
    super.key,
    required this.view,
    required this.allowActivation,
    required this.enabled,
    required this.stale,
    required this.onSave,
    required this.onStart,
    required this.onStop,
    required this.onDelete,
    required this.onRefresh,
    required this.onAsk,
    this.onShare,
  });
  final Map<String, dynamic> view;
  final bool allowActivation, enabled, stale;
  final Future<void> Function(Map<String, Object?> body) onSave;
  final Future<void> Function() onStart, onStop, onDelete, onRefresh;
  final ValueChanged<String> onAsk;
  final Future<void> Function(String packageName)? onShare;
  @override
  State<CSharpDetailView> createState() => _CSharpDetailViewState();
}

class _CSharpDetailViewState extends State<CSharpDetailView> {
  late final source = TextEditingController(text: savedSource(widget.view));
  late final name = TextEditingController(text: savedName(widget.view));
  late final purpose = TextEditingController(text: savedPurpose(widget.view));

  static Map<String, dynamic> descriptionOf(Map<String, dynamic> view) =>
      csharpMap(view['description']);
  static Map<String, dynamic> fileOf(Map<String, dynamic> view) =>
      csharpMap(view['file']);
  static String savedSource(Map<String, dynamic> view) =>
      fileOf(view)['source'] as String? ?? '';
  static String savedName(Map<String, dynamic> view) =>
      descriptionOf(view)['name'] as String? ?? '';
  static String savedPurpose(Map<String, dynamic> view) =>
      descriptionOf(view)['purpose'] as String? ?? '';

  Map<String, dynamic> get file => fileOf(widget.view);
  String get id =>
      descriptionOf(widget.view)['id'] as String? ?? file['id'] as String;
  bool get edited =>
      source.text != savedSource(widget.view) ||
      name.text != savedName(widget.view) ||
      purpose.text != savedPurpose(widget.view);

  @override
  void didUpdateWidget(CSharpDetailView oldWidget) {
    super.didUpdateWidget(oldWidget);
    // Follow server changes only in fields the user has not touched since the last read.
    void follow(
      TextEditingController field,
      String Function(Map<String, dynamic>) saved,
    ) {
      final before = saved(oldWidget.view);
      final after = saved(widget.view);
      if (field.text == before && before != after) field.text = after;
    }

    follow(source, savedSource);
    follow(name, savedName);
    follow(purpose, savedPurpose);
  }

  @override
  void dispose() {
    source.dispose();
    name.dispose();
    purpose.dispose();
    super.dispose();
  }

  Future<void> share() async {
    final chosen = await showDialog<String>(
      context: context,
      builder: (_) => CSharpShareDialog(
        suggestedName: id
            .toLowerCase()
            .replaceAll('_', '-')
            .replaceFirst(RegExp('^-+'), ''),
      ),
    );
    if (chosen != null && chosen.isNotEmpty) await widget.onShare!(chosen);
  }

  Future<void> delete() async {
    final label = name.text.isEmpty ? id : name.text;
    if (await confirmCSharpDelete(context, label) && mounted) {
      await widget.onDelete();
    }
  }

  Widget logs() {
    final text = widget.view['logs'] as String? ?? '';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            const Expanded(
              child: Text(
                'Logs',
                style: TextStyle(fontWeight: FontWeight.w600),
              ),
            ),
            IconButton(
              tooltip: 'Refresh logs',
              onPressed: widget.onRefresh,
              icon: const Icon(Icons.refresh, size: 18),
            ),
          ],
        ),
        Container(
          key: const ValueKey('csharp-logs'),
          constraints: const BoxConstraints(minHeight: 80, maxHeight: 320),
          padding: const EdgeInsets.all(12),
          color: Theme.of(context).colorScheme.surfaceContainerLowest,
          child: SingleChildScrollView(
            reverse: true,
            child: SelectableText(
              text.isEmpty ? 'No output yet.' : text,
              style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
            ),
          ),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final running = csharpIsRunning(file);
    final startedAt = file['startedAt'];
    return SingleChildScrollView(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            savedName(widget.view).isEmpty ? id : savedName(widget.view),
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 4),
          Text(
            widget.stale
                ? 'State unavailable · reconnecting'
                : [
                    csharpStatusLabel(file),
                    if (running && startedAt != null) 'since $startedAt',
                  ].join(' · '),
            style: const TextStyle(fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            runSpacing: 6,
            children: [
              FilledButton(
                onPressed: widget.enabled && edited
                    ? () => widget.onSave({
                        'source': source.text,
                        'name': name.text.trim(),
                        'purpose': purpose.text.trim(),
                      })
                    : null,
                child: const Text('Save'),
              ),
              OutlinedButton(
                onPressed: widget.enabled && widget.allowActivation
                    ? widget.onStart
                    : null,
                child: Text(running ? 'Restart' : 'Start'),
              ),
              OutlinedButton(
                onPressed: widget.enabled && running ? widget.onStop : null,
                child: const Text('Stop'),
              ),
              if (widget.onShare != null)
                OutlinedButton.icon(
                  onPressed: widget.enabled ? share : null,
                  icon: const Icon(Icons.ios_share, size: 16),
                  label: const Text('Share as package'),
                ),
              TextButton.icon(
                onPressed: widget.enabled
                    ? () => widget.onAsk(
                        'Help me with C# app "$id". Read it with csharp_run status, then ask what I want to change. Do not start it without my request.',
                      )
                    : null,
                icon: const Icon(Icons.auto_awesome, size: 16),
                label: const Text('Edit with assistant'),
              ),
              TextButton(
                onPressed: widget.enabled ? delete : null,
                child: const Text('Delete'),
              ),
            ],
          ),
          if (!widget.allowActivation)
            const Padding(
              padding: EdgeInsets.only(top: 6),
              child: Text(
                'Running C# apps is disabled by host settings. You can still edit and save them.',
              ),
            ),
          if (edited)
            const Padding(
              padding: EdgeInsets.only(top: 6),
              child: Text('Unsaved changes. Start runs the last saved source.'),
            ),
          const SizedBox(height: 16),
          TextField(
            key: const ValueKey('csharp-name'),
            controller: name,
            enabled: widget.enabled,
            maxLength: 120,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(labelText: 'Name'),
          ),
          TextField(
            key: const ValueKey('csharp-purpose'),
            controller: purpose,
            enabled: widget.enabled,
            minLines: 1,
            maxLines: 3,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(labelText: 'Purpose'),
          ),
          const SizedBox(height: 16),
          TextField(
            key: const ValueKey('csharp-source'),
            controller: source,
            enabled: widget.enabled,
            minLines: 12,
            maxLines: 30,
            keyboardType: TextInputType.multiline,
            onChanged: (_) => setState(() {}),
            style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
            decoration: const InputDecoration(
              labelText: 'Source (app.cs)',
              alignLabelWithHint: true,
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 16),
          logs(),
        ],
      ),
    );
  }
}
