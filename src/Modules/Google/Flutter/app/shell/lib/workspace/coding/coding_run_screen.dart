import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

class CodingRunScreen extends StatefulWidget {
  const CodingRunScreen({
    super.key,
    required this.runId,
    required this.client,
    required this.active,
  });
  final String runId;
  final DigitalBrainUiClient client;
  final bool active;

  @override
  State<CodingRunScreen> createState() => _CodingRunScreenState();
}

class CodingRunView {
  CodingRunView(this.snapshot);

  final Map<String, dynamic> snapshot;

  String get status => codingRunStatusLabel(snapshot['status']);
  bool get stopped => (snapshot['status'] as num?)?.toInt() == 5;
  bool get approved => (snapshot['status'] as num?)?.toInt() == 4;
  String? get failureReason => snapshot['failureReason'] as String?;
  Map<String, dynamic>? get plan =>
      snapshot['plan'] is Map ? Map<String, dynamic>.from(snapshot['plan'] as Map) : null;
  String get summary => plan?['summary'] as String? ?? '';
  List<Map<String, dynamic>> get steps => plan?['steps'] is List
      ? (plan!['steps'] as List).whereType<Map>().map((s) => Map<String, dynamic>.from(s)).toList()
      : const [];
  List<String> get openQuestions => snapshot['plan'] is Map && (snapshot['plan'] as Map)['openQuestions'] is List
      ? ((snapshot['plan'] as Map)['openQuestions'] as List).whereType<String>().toList()
      : const [];
  List<String> get clarifications => snapshot['clarifications'] is List
      ? (snapshot['clarifications'] as List).whereType<String>().toList()
      : const [];
}

String codingRunStatusLabel(Object? value) {
  final status = value is num ? value.toInt() : 0;
  return switch (status) {
    0 => 'Requested',
    1 => 'Context ready',
    2 => 'Plan drafted',
    3 => 'Failed',
    4 => 'Plan approved',
    5 => 'Stopped',
    _ => 'Unknown',
  };
}

class _CodingRunScreenState extends State<CodingRunScreen> {
  final TextEditingController _clarification = TextEditingController();
  CodingRunView? run;
  String? error;
  bool busy = false;
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    _sync();
  }

  @override
  void didUpdateWidget(covariant CodingRunScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.runId != widget.runId || oldWidget.client != widget.client || oldWidget.active != widget.active) {
      _poll?.cancel();
      _sync();
    }
  }

  void _sync() {
    if (!widget.active) return;
    unawaited(_load());
    _poll = Timer.periodic(const Duration(seconds: 3), (_) => unawaited(_load()));
  }

  Future<void> _load() async {
    try {
      final snapshot = await widget.client.readCodingRun(widget.runId);
      if (!mounted) return;
      setState(() => run = CodingRunView(snapshot));
    } catch (failure) {
      if (!mounted) return;
      setState(() => error = '$failure');
    }
  }

  Future<void> _act(String action, {Map<String, Object?>? body}) async {
    if (busy) return;
    setState(() => busy = true);
    try {
      final snapshot = await widget.client.codingRunRequest(widget.runId, action, body: body);
      if (!mounted) return;
      setState(() {
        run = CodingRunView(snapshot);
        error = null;
        if (action == 'clarify') _clarification.clear();
      });
    } catch (failure) {
      if (!mounted) return;
      setState(() => error = '$failure');
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  void dispose() {
    _poll?.cancel();
    _clarification.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.active) {
      return const Center(child: Text('Coding run paused while hidden.'));
    }
    final current = run;
    if (current == null) {
      return Center(
        child: error == null ? const CircularProgressIndicator() : Text('Could not load the coding run: $error'),
      );
    }
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _header(context, current),
          if (error != null) ...[
            const SizedBox(height: 8),
            Text(error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
          ],
          const SizedBox(height: 12),
          Expanded(child: _body(context, current)),
          const SizedBox(height: 12),
          _clarify(context, current),
          const SizedBox(height: 8),
          _actions(context, current),
        ],
      ),
    );
  }

  Widget _header(BuildContext context, CodingRunView current) => Row(
    children: [
      Chip(
        key: const Key('coding-run-status'),
        label: Text(current.status),
      ),
      const Spacer(),
      IconButton(
        tooltip: 'Refresh',
        onPressed: busy ? null : _load,
        icon: const Icon(Icons.refresh),
      ),
    ],
  );

  Widget _body(BuildContext context, CodingRunView current) => ListView(
    children: [
      if (current.failureReason != null)
        Card(
          color: Theme.of(context).colorScheme.errorContainer,
          child: ListTile(title: Text(current.failureReason!)),
        ),
      if (current.summary.isNotEmpty)
        ListTile(title: Text(current.summary, style: Theme.of(context).textTheme.titleMedium)),
      for (final step in current.steps)
        ListTile(
          dense: true,
          leading: CircleAvatar(child: Text('${step['number'] ?? 0}')),
          title: Text(step['title'] as String? ?? ''),
          subtitle: Text((step['detail'] as String? ?? '') + _files(step)),
        ),
      if (current.openQuestions.isNotEmpty) ...[
        const Divider(),
        ListTile(title: Text('Open questions', style: Theme.of(context).textTheme.titleSmall)),
        for (final question in current.openQuestions) ListTile(dense: true, leading: const Icon(Icons.help_outline), title: Text(question)),
      ],
      if (current.clarifications.isNotEmpty) ...[
        const Divider(),
        ListTile(title: Text('Clarifications', style: Theme.of(context).textTheme.titleSmall)),
        for (final clarification in current.clarifications) ListTile(dense: true, leading: const Icon(Icons.chat_outlined), title: Text(clarification)),
      ],
    ],
  );

  static String _files(Map<String, dynamic> step) {
    final files = step['files'];
    if (files is! List || files.isEmpty) return '';
    return '\n${files.whereType<String>().join(', ')}';
  }

  Widget _clarify(BuildContext context, CodingRunView current) => TextField(
    key: const Key('coding-run-clarify'),
    controller: _clarification,
    enabled: !current.stopped,
    minLines: 1,
    maxLines: 3,
    onChanged: (_) => setState(() {}),
    decoration: const InputDecoration(
      border: OutlineInputBorder(),
      labelText: 'Clarify the plan',
    ),
  );

  Widget _actions(BuildContext context, CodingRunView current) => Row(
    children: [
      FilledButton(
        key: const Key('coding-run-send-clarify'),
        onPressed: current.stopped || busy || _clarification.text.trim().isEmpty
            ? null
            : () => _act('clarify', body: {'text': _clarification.text.trim()}),
        child: const Text('Send clarification'),
      ),
      const SizedBox(width: 8),
      FilledButton(
        key: const Key('coding-run-approve'),
        onPressed: current.plan == null || current.stopped || current.approved || busy ? null : () => _act('approve'),
        child: const Text('Approve'),
      ),
      const SizedBox(width: 8),
      OutlinedButton(
        key: const Key('coding-run-stop'),
        onPressed: current.stopped || busy ? null : () => _act('stop'),
        child: const Text('Stop'),
      ),
    ],
  );
}
