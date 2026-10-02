import 'package:flutter/material.dart';
import 'package:uuid/uuid.dart';

import 'app_document.dart';
import 'app_draft_controller.dart';
import 'app_spec_view.dart';
import 'apps_screen.dart';
import 'behavior_editor.dart';
import 'spec_vocabulary.dart';

class BehaviorAuthoringView extends StatefulWidget {
  const BehaviorAuthoringView({
    super.key,
    required this.draftId,
    required this.request,
    required this.initial,
  });
  final String draftId;
  final AppsRequest request;
  final Map<String, dynamic> initial;
  @override
  State<BehaviorAuthoringView> createState() => _BehaviorAuthoringViewState();
}

class _BehaviorAuthoringViewState extends State<BehaviorAuthoringView> {
  late final controller = AppDraftController(
    widget.draftId,
    widget.request,
    widget.initial,
  )..addListener(changed);
  void changed() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    controller.removeListener(changed);
    controller.dispose();
    super.dispose();
  }

  Future<void> perform(Future<void> Function() action) async {
    try {
      await action();
    } catch (_) {
      /* Controller exposes the error and retains pending edits. */
    }
  }

  Future<void> edit(AppBehavior? behavior) => showDialog<void>(
    context: context,
    barrierDismissible: false,
    builder: (_) => BehaviorEditor(
      behavior:
          behavior ??
          AppBehavior(
            id: const Uuid().v4().replaceAll('-', ''),
            title: '',
            description: '',
          ),
      scenarios: controller.document!.scenarios,
      onReload: controller.load,
      onSave: (value) async {
        final doc = controller.document!;
        final exists = doc.behaviors.any((b) => b.id == value.id);
        await controller.save(
          doc.withBehaviors(
            exists
                ? doc.behaviors
                      .map((b) => b.id == value.id ? value : b)
                      .toList()
                : [...doc.behaviors, value],
          ),
        );
      },
    ),
  );
  Future<void> delete(AppBehavior b) async {
    final yes = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Delete ${b.title}?'),
        content: const Text(
          'This removes the draft behavior. Its scenarios remain available. The installed app is unchanged.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
    if (yes == true) {
      await perform(
        () => controller.save(
          controller.document!.withBehaviors(
            controller.document!.behaviors.where((x) => x.id != b.id).toList(),
          ),
        ),
      );
    }
  }

  Future<void> editScenario([AppScenario? original]) async {
    final name = TextEditingController(text: original?.name);
    final body = TextEditingController(text: original?.body);
    var live = original?.isLive ?? false;
    String? error;
    var saving = false;
    await showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, update) => AlertDialog(
          title: Text(original == null ? 'Add scenario' : 'Edit scenario'),
          content: SizedBox(
            width: 520,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  TextField(
                    controller: name,
                    decoration: const InputDecoration(
                      labelText: 'Scenario name',
                    ),
                  ),
                  TextField(
                    controller: body,
                    minLines: 3,
                    maxLines: 8,
                    decoration: const InputDecoration(
                      labelText: 'What must be true?',
                    ),
                  ),
                  CheckboxListTile(
                    value: live,
                    title: const Text('Live documentation (not gated)'),
                    onChanged: saving ? null : (v) => update(() => live = v!),
                  ),
                  if (error != null) ...[
                    SelectableText(error!),
                    TextButton(
                      onPressed: saving
                          ? null
                          : () async {
                              update(() => saving = true);
                              try {
                                await controller.load();
                                if (context.mounted) update(() => error = null);
                              } catch (e) {
                                if (context.mounted) {
                                  update(() => error = e.toString());
                                }
                              } finally {
                                if (context.mounted) {
                                  update(() => saving = false);
                                }
                              }
                            },
                      child: const Text('Reload latest draft'),
                    ),
                  ],
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: saving ? null : () => Navigator.pop(context),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: saving
                  ? null
                  : () async {
                      update(() => saving = true);
                      final doc = controller.document!;
                      final value = AppScenario(
                        id:
                            original?.id ??
                            const Uuid().v4().replaceAll('-', ''),
                        name: name.text,
                        body: body.text,
                        isLive: live,
                      );
                      try {
                        await controller.save(
                          AppDocument(
                            preamble: doc.preamble,
                            behaviors: doc.behaviors,
                            scenarios:
                                original == null ||
                                    !doc.scenarios.any(
                                      (s) => s.id == original.id,
                                    )
                                ? [...doc.scenarios, value]
                                : doc.scenarios
                                      .map(
                                        (s) => s.id == original.id ? value : s,
                                      )
                                      .toList(),
                          ),
                        );
                        if (context.mounted) Navigator.pop(context);
                      } catch (e) {
                        if (context.mounted) {
                          update(() {
                            error = e.toString();
                            saving = false;
                          });
                        }
                      }
                    },
              child: const Text('Save'),
            ),
          ],
        ),
      ),
    );
    // Dialog routes finish their exit animation before their fields are disposed.
    await Future<void>.delayed(const Duration(milliseconds: 300));
    name.dispose();
    body.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final draft = controller.draft;
    final doc = controller.document!;
    final verification = appMap(controller.view['verification']);
    final busy = controller.busy || draft['status'] == 2;
    return Scaffold(
      appBar: AppBar(
        title: Text('${draft['title'] ?? 'Build app'}'),
        actions: [
          IconButton(
            tooltip: 'Reload draft',
            onPressed: controller.busy ? null : () => perform(controller.load),
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 960),
          child: ListView(
            padding: const EdgeInsets.all(24),
            children: [
              Text(
                'Small behaviors that work together.',
                style: Theme.of(context).textTheme.bodyLarge,
              ),
              const SizedBox(height: 16),
              if (busy) const LinearProgressIndicator(),
              if (controller.error != null)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 12),
                  child: SelectableText(
                    controller.error!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ),
              if (draft['status'] == 4)
                SelectableText('${draft['error'] ?? 'Build failed.'}'),
              if ((draft['attempts'] as List? ?? []).isNotEmpty)
                ExpansionTile(
                  title: const Text('Build diagnostics'),
                  subtitle: const Text(
                    'Previous attempts; results apply to their listed revisions.',
                  ),
                  children: [
                    for (final (index, value)
                        in (draft['attempts'] as List).indexed)
                      ListTile(
                        title: Text(
                          'Attempt ${index + 1} · ${appMap(value)['green'] == true ? 'Passed' : 'Failed'}',
                        ),
                        subtitle: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            if ('${appMap(value)['revision'] ?? ''}'.isNotEmpty)
                              SelectableText(
                                'Revision ${appMap(value)['revision']}',
                              ),
                            if ('${appMap(value)['failures'] ?? ''}'.isNotEmpty)
                              SelectableText('${appMap(value)['failures']}'),
                          ],
                        ),
                      ),
                  ],
                ),
              ...AppSpecView(
                spec: '${draft['spec'] ?? ''}',
                document: doc,
                run: verification.isEmpty ? null : appMap(verification['run']),
                current: controller.view['verificationCurrent'] == true,
                vocabulary: SpecToken.read(controller.view['vocabulary']),
                files: appMap(controller.view['files'])
                    .map((k, v) => MapEntry(k, '$v')),
                sourceRevision:
                    appMap(controller.view['filesRevision'])['revision']
                        as String?,
                onEdit: busy ? null : (b) => edit(b),
                onDelete: busy ? null : delete,
                onDuplicate: busy
                    ? null
                    : (b) => perform(
                        () => controller.save(
                          doc.withBehaviors([
                            ...doc.behaviors,
                            b.duplicate(const Uuid().v4().replaceAll('-', '')),
                          ]),
                        ),
                      ),
              ).childrenFor(context),
              Wrap(
                spacing: 12,
                children: [
                  TextButton.icon(
                    onPressed: busy ? null : () => edit(null),
                    icon: const Icon(Icons.add),
                    label: const Text('Add behavior'),
                  ),
                  TextButton(
                    onPressed: busy ? null : () => editScenario(),
                    child: const Text('Add scenario'),
                  ),
                  if (doc.scenarios.isNotEmpty)
                    PopupMenuButton<String>(
                      tooltip: 'Edit a scenario',
                      onSelected: (id) => editScenario(
                        doc.scenarios.firstWhere((s) => s.id == id),
                      ),
                      enabled: !busy,
                      itemBuilder: (_) => [
                        for (final s in doc.scenarios)
                          PopupMenuItem(value: s.id, child: Text(s.name)),
                      ],
                      child: const Padding(
                        padding: EdgeInsets.all(12),
                        child: Text('Edit scenarios'),
                      ),
                    ),
                ],
              ),
              const SizedBox(height: 24),
              const Text(
                'Building runs the app checks and publishes the app when they pass.',
              ),
              const SizedBox(height: 8),
              Align(
                alignment: Alignment.centerLeft,
                child: FilledButton.icon(
                  onPressed: busy ? null : () => perform(controller.build),
                  icon: const Icon(Icons.play_arrow),
                  label: const Text('Build and publish'),
                ),
              ),
              if (draft['status'] == 3)
                const Padding(
                  padding: EdgeInsets.only(top: 12),
                  child: Text(
                    'Published. Install or update this app from Apps.',
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
