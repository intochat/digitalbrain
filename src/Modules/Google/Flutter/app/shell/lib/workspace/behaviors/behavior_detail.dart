import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:uuid/uuid.dart';

import 'behavior_manager.dart';

class BehaviorDetailView extends StatefulWidget {
  const BehaviorDetailView({
    super.key,
    required this.detail,
    required this.enabled,
    required this.stale,
    required this.onCommand,
    required this.onDescribe,
    required this.onAsk,
  });
  final Map<String, dynamic> detail;
  final bool enabled, stale;
  final Future<void> Function(String path, Map<String, Object?> body) onCommand;
  final VoidCallback onDescribe;
  final ValueChanged<String> onAsk;
  @override
  State<BehaviorDetailView> createState() => _BehaviorDetailViewState();
}

class _BehaviorDetailViewState extends State<BehaviorDetailView> {
  int tab = 0;
  late final configuration = TextEditingController(text: currentConfiguration);
  String? configError;
  bool configurationEdited = false;
  Map<String, dynamic> get d => behaviorMap(widget.detail['description']);
  Map<String, dynamic> get program => behaviorMap(widget.detail['program']);
  Map<String, dynamic> get draft => behaviorMap(widget.detail['draft']);
  Map<String, dynamic> get check => behaviorMap(widget.detail['check']);
  List<Map<String, dynamic>> get versions =>
      behaviorMaps(program['deployments']);
  String get id => d['id'] as String;
  String get currentConfiguration => versions.isEmpty
      ? '{}'
      : versions.last['configurationJson'] as String? ?? '{}';
  bool get pending =>
      ['Queued', 'Building', 'Testing'].contains(checkState(check));
  @override
  void didUpdateWidget(BehaviorDetailView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (configuration.text == currentConfiguration) configurationEdited = false;
    if (!configurationEdited && configuration.text != currentConfiguration) {
      configuration.text = currentConfiguration;
    }
  }

  @override
  void dispose() {
    configuration.dispose();
    super.dispose();
  }

  Map<String, Object?> commandBody() => {
    'expectedRevision': program['revision'],
    'operationId': const Uuid().v4(),
  };
  Future<void> deploy() async {
    try {
      final parsed = jsonDecode(configuration.text);
      if (parsed is! Map ||
          parsed.entries.any(
            (entry) =>
                !(entry.key as String).startsWith('Behavior__') ||
                entry.value is! String,
          )) {
        throw const FormatException(
          'Use an object of string values with keys starting Behavior__.',
        );
      }
      setState(() => configError = null);
      await widget.onCommand('deploy', {
        ...commandBody(),
        'artifact': check['artifact'],
        'configurationJson': configuration.text,
      });
    } catch (error) {
      if (mounted) setState(() => configError = '$error');
    }
  }

  Future<void> rollback(Map<String, dynamic> version) async {
    final approved = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('Restore version ${version['revision']}?'),
        content: const Text(
          'This replaces the active automation with this verified version and its saved configuration.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Restore version'),
          ),
        ],
      ),
    );
    if (approved == true && mounted && widget.enabled) {
      await widget.onCommand('rollback', {
        ...commandBody(),
        'deploymentRevision': version['revision'],
      });
    }
  }

  Widget section(String title, Widget child) => Padding(
    padding: const EdgeInsets.only(bottom: 20),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(title, style: const TextStyle(fontWeight: FontWeight.w600)),
        const SizedBox(height: 8),
        child,
      ],
    ),
  );
  Widget connection(String title, List<dynamic> entries, IconData icon) =>
      Card.outlined(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(icon, size: 18),
                  const SizedBox(width: 6),
                  Text(title),
                ],
              ),
              const SizedBox(height: 8),
              Text(entries.isEmpty ? 'Not described yet' : entries.join('\n')),
            ],
          ),
        ),
      );
  Widget overview() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      section(
        'How it works',
        Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            connection(
              'When',
              d['triggers'] as List? ?? [],
              Icons.bolt_outlined,
            ),
            const Icon(Icons.arrow_downward, size: 18),
            Card(
              color: Theme.of(context).colorScheme.primaryContainer,
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Text(d['name'] as String, textAlign: TextAlign.center),
              ),
            ),
            const Icon(Icons.arrow_downward, size: 18),
            connection(
              'Then',
              d['effects'] as List? ?? [],
              Icons.output_outlined,
            ),
            const Padding(
              padding: EdgeInsets.all(8),
              child: Text(
                'Declared connections · authored descriptions, not an execution trace.',
                style: TextStyle(fontSize: 12),
              ),
            ),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton(
                onPressed: widget.enabled ? widget.onDescribe : null,
                child: const Text('Edit details'),
              ),
            ),
          ],
        ),
      ),
      section(
        'Validation',
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Draft ${draft['revision']} · ${checkState(check)}'),
            if (check['tests'] is Map)
              Text(
                '${check['tests']['passed']} of ${check['tests']['discovered']} tests passed',
              ),
            Text(
              widget.stale
                  ? 'Worker status unavailable'
                  : program['ready'] == true
                  ? 'Worker ready · subscriptions established'
                  : 'Worker: ${behaviorState(program)}',
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              children: [
                OutlinedButton(
                  onPressed:
                      widget.enabled &&
                          !pending &&
                          (draft['revision'] as num? ?? 0) > 0
                      ? () => widget.onCommand('checks', {
                          'revision': draft['revision'],
                          'operationId': const Uuid().v4(),
                        })
                      : null,
                  child: const Text('Check draft'),
                ),
                if (pending)
                  TextButton(
                    onPressed: widget.enabled
                        ? () => widget.onCommand(
                            'checks/${check['operationId']}/cancel',
                            {},
                          )
                        : null,
                    child: const Text('Cancel check'),
                  ),
              ],
            ),
          ],
        ),
      ),
      ExpansionTile(
        tilePadding: EdgeInsets.zero,
        title: const Text('Configuration'),
        subtitle: const Text('Applied with the next deployment'),
        children: [
          TextField(
            controller: configuration,
            minLines: 3,
            maxLines: 8,
            enabled: widget.enabled,
            onChanged: (_) => setState(() => configurationEdited = true),
            style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
            decoration: InputDecoration(
              border: const OutlineInputBorder(),
              errorText: configError,
              helperText: 'String values. Keys start with Behavior__. Do not store secrets.',
            ),
          ),
        ],
      ),
    ],
  );

  Widget activity() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      if (program['error'] != null)
        Card(
          color: Theme.of(context).colorScheme.errorContainer,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'Needs attention',
                  style: TextStyle(fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: 6),
                SelectableText('${program['error']}'),
              ],
            ),
          ),
        ),
      if (check.isNotEmpty)
        ListTile(
          contentPadding: EdgeInsets.zero,
          leading: const Icon(Icons.fact_check_outlined),
          title: Text('Draft ${check['revision']}: ${checkState(check)}'),
          subtitle: Text('${check['completedAt'] ?? check['createdAt'] ?? ''}'),
        ),
      for (final diagnostic in behaviorMaps(check['diagnostics']))
        Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: SelectableText(
            '${diagnostic['code']}: ${diagnostic['message']}',
          ),
        ),
      for (final version in versions.reversed)
        ListTile(
          contentPadding: EdgeInsets.zero,
          leading: const Icon(Icons.publish),
          title: Text('Version ${version['revision']} deployed'),
          subtitle: Text('${version['createdAt']}'),
        ),
      const Divider(),
      const Text('Worker logs', style: TextStyle(fontWeight: FontWeight.w600)),
      const Text(
        'Process output; it does not represent a complete history of effects.',
        style: TextStyle(fontSize: 12),
      ),
      if (behaviorMap(widget.detail['logs'])['truncated'] == true)
        const Text('Earlier log entries were omitted.'),
      if (behaviorMaps(behaviorMap(widget.detail['logs'])['entries']).isEmpty)
        const Padding(
          padding: EdgeInsets.all(16),
          child: Text('No worker output yet.'),
        ),
      for (final log in behaviorMaps(
        behaviorMap(widget.detail['logs'])['entries'],
      ))
        ExpansionTile(
          tilePadding: EdgeInsets.zero,
          title: Text(
            '${log['message']}'.replaceAll('\n', ' '),
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontSize: 12),
          ),
          subtitle: Text(
            '${log['at']} · ${log['stream']}',
            style: const TextStyle(fontSize: 11),
          ),
          children: [
            SelectableText(
              '${log['message']}',
              style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
            ),
          ],
        ),
    ],
  );
  Widget codeBlock(String text) => Container(
    width: double.infinity,
    padding: const EdgeInsets.all(12),
    color: Theme.of(context).colorScheme.surfaceContainerLowest,
    child: SelectableText(
      text.isEmpty ? 'No source yet. Ask the assistant to create it.' : text,
      style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
    ),
  );
  Widget code() {
    final deployed = behaviorMap(widget.detail['deployedSource']);
    final source = draft['source'] as String? ?? '';
    final before = deployed['source'] as String?;
    final comparison = <String>[];
    if (before != null) {
      final oldLines = before.split('\n');
      final newLines = source.split('\n');
      for (var i = 0; i < oldLines.length || i < newLines.length; i++) {
        final old = i < oldLines.length ? oldLines[i] : null;
        final next = i < newLines.length ? newLines[i] : null;
        if (old == next) {
          if (old != null) comparison.add('  $old');
        } else {
          if (old != null) comparison.add('- $old');
          if (next != null) comparison.add('+ $next');
        }
      }
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        section('Draft ${draft['revision']}', codeBlock(source)),
        ExpansionTile(
          title: const Text('Tests'),
          children: [codeBlock(draft['tests'] as String? ?? '')],
        ),
        ExpansionTile(
          title: const Text('Changes from the deployed source'),
          children: [
            before == null
                ? const Padding(
                    padding: EdgeInsets.all(12),
                    child: Text(
                      'No retained source for the active deployment.',
                    ),
                  )
                : codeBlock(
                    before == source
                        ? 'No source changes.'
                        : comparison.join('\n'),
                  ),
          ],
        ),
        const SizedBox(height: 16),
        section(
          'Deployed versions',
          Column(
            children: [
              if (versions.isEmpty) const Text('Not deployed yet.'),
              for (final version in versions.reversed)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  title: Text(
                    'Version ${version['revision']}${program['activeDeploymentRevision'] == version['revision'] ? ' · Active' : ''}',
                  ),
                  subtitle: Text('${version['createdAt']}'),
                  trailing:
                      program['activeDeploymentRevision'] == version['revision']
                      ? null
                      : TextButton(
                          onPressed:
                              widget.enabled &&
                                  widget.detail['allowActivation'] == true
                              ? () => rollback(version)
                              : null,
                          child: const Text('Restore'),
                        ),
                ),
            ],
          ),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final state = behaviorState(program);
    final running = [
      'Running',
      'Waiting for readiness',
      'Starting',
      'Stopping',
    ].contains(state);
    final artifact = behaviorMap(check['artifact']);
    final active = versions
        .where((v) => v['revision'] == program['activeDeploymentRevision'])
        .firstOrNull;
    final changed =
        artifact.isNotEmpty &&
        (artifact['id'] != behaviorMap(active?['artifact'])['id'] ||
            configuration.text != currentConfiguration);
    final hasNewDraft =
        versions.isNotEmpty &&
        (check.isEmpty || changed || check['revision'] != draft['revision']);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                d['name'] as String,
                style: Theme.of(context).textTheme.titleLarge,
              ),
              if ('${d['purpose'] ?? ''}'.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text(
                    '${d['purpose']}',
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              const SizedBox(height: 8),
              Text(
                widget.stale
                    ? 'State unavailable · reconnecting'
                    : '${versions.isEmpty ? 'Draft' : state}${hasNewDraft ? ' · Draft changes available' : ''}',
                style: const TextStyle(fontWeight: FontWeight.w600),
              ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 8,
                runSpacing: 6,
                children: [
                  if (widget.detail['canDeploy'] == true && changed)
                    FilledButton(
                      onPressed: widget.enabled ? deploy : null,
                      child: Text(
                        versions.isEmpty ? 'Deploy' : 'Deploy changes',
                      ),
                    ),
                  if (versions.isNotEmpty)
                    OutlinedButton(
                      onPressed:
                          widget.enabled &&
                              state != 'Stopping' &&
                              (running ||
                                  widget.detail['allowActivation'] == true)
                          ? () => widget.onCommand(
                              running ? 'stop' : 'start',
                              commandBody(),
                            )
                          : null,
                      child: Text(running ? 'Stop' : 'Start'),
                    ),
                  TextButton.icon(
                    onPressed: widget.enabled
                        ? () => widget.onAsk(
                            'Help me edit automation "$id". Read its details and current draft, then ask what I want to change. Do not deploy changes without my request.',
                          )
                        : null,
                    icon: const Icon(Icons.auto_awesome, size: 16),
                    label: const Text('Edit with assistant'),
                  ),
                  if (program['error'] != null || checkState(check) == 'Failed')
                    TextButton(
                      onPressed: widget.enabled
                          ? () => widget.onAsk(
                              'Repair automation "$id". Read its draft, latest check diagnostics, behavior state and worker logs. Save and validate the repair, then leave it ready for me to deploy from Automations.',
                            )
                          : null,
                      child: const Text('Ask assistant to repair'),
                    ),
                ],
              ),
              if (widget.detail['allowActivation'] != true)
                const Text(
                  'Execution is disabled by host settings. You can still create and check drafts.',
                ),
            ],
          ),
        ),
        Row(
          children: [
            for (var i = 0; i < 3; i++)
              Expanded(
                child: TextButton(
                  onPressed: () => setState(() => tab = i),
                  style: TextButton.styleFrom(
                    backgroundColor: tab == i
                        ? Theme.of(context).colorScheme.secondaryContainer
                        : null,
                  ),
                  child: Text(['Overview', 'Activity', 'Code & versions'][i]),
                ),
              ),
          ],
        ),
        Expanded(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(16),
            child: switch (tab) {
              0 => overview(),
              1 => activity(),
              _ => code(),
            },
          ),
        ),
      ],
    );
  }
}
