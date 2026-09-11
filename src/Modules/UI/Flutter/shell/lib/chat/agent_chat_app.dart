import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:gpt_markdown/gpt_markdown.dart';
import 'package:uuid/uuid.dart';

typedef AgentRunner = Stream<AgentEvent> Function({
  required String threadId,
  required String runId,
  String? parentRunId,
  required String text,
});

/// The default shell consumes one AG-UI run at a time. No graph, activity,
/// surface, or legacy turn subscriptions are needed for a conversation.
class AgentChatApp extends StatelessWidget {
  const AgentChatApp({
    super.key,
    this.onRun,
    this.onOpenUrl,
    this.statusMessage,
    this.onReadTable,
    this.onUpdateTableView,
    this.onListTables,
  });
  final AgentRunner? onRun;
  final Future<void> Function(Uri)? onOpenUrl;
  final String? statusMessage;
  final ReadTable? onReadTable;
  final UpdateTableView? onUpdateTableView;
  final ListTables? onListTables;

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'DigitalBrain',
    debugShowCheckedModeBanner: false,
    theme: KitTheme.light(),
    home: _Conversation(
      onRun: onRun,
      onOpenUrl: onOpenUrl,
      statusMessage: statusMessage,
      onReadTable: onReadTable,
      onUpdateTableView: onUpdateTableView,
      onListTables: onListTables,
    ),
  );
}

class _Entry {
  _Entry(this.id, this.role, {this.text = ''});
  final String id;
  final String role;
  String text;
  String? name;
  Object? result;
  bool complete = false;
  bool interrupted = false;
}

class _Conversation extends StatefulWidget {
  const _Conversation({
    this.onRun,
    this.onOpenUrl,
    this.statusMessage,
    this.onReadTable,
    this.onUpdateTableView,
    this.onListTables,
  });
  final AgentRunner? onRun;
  final Future<void> Function(Uri)? onOpenUrl;
  final String? statusMessage;
  final ReadTable? onReadTable;
  final UpdateTableView? onUpdateTableView;
  final ListTables? onListTables;
  @override
  State<_Conversation> createState() => _ConversationState();
}

class _ConversationState extends State<_Conversation> {
  static const _uuid = Uuid();
  final _composer = TextEditingController();
  final _scroll = ScrollController();
  final _entries = <_Entry>[];
  final _tables = <String, KitTableController>{};
  String? _activeTableId;
  int _tableGeneration = 0;
  bool _openingTable = false;
  StreamSubscription<AgentEvent>? _subscription;
  String _threadId = _uuid.v4();
  String? _parentRunId;
  String? _activeRunId;
  String? _notice;
  bool _running = false;
  bool _runFinished = false;
  int _generation = 0;

  @override
  void dispose() {
    _generation++;
    _tableGeneration++;
    for (final table in _tables.values) {
      table.dispose();
    }
    unawaited(_subscription?.cancel());
    _composer.dispose();
    _scroll.dispose();
    super.dispose();
  }

  void _send() {
    final text = _composer.text.trim();
    final run = widget.onRun;
    if (_running || text.isEmpty || run == null) return;
    final generation = ++_generation;
    final runId = _uuid.v4();
    setState(() {
      _running = true;
      _runFinished = false;
      _notice = null;
      _activeRunId = null;
      _entries.add(_Entry(_uuid.v4(), 'user', text: text));
      _composer.clear();
    });
    _followOutput(force: true);
    try {
      _subscription =
          run(
            threadId: _threadId,
            runId: runId,
            parentRunId: _parentRunId,
            text: _activeTableId == null
                ? text
                : '$text\n\n[Active table context: ${jsonEncode(_activeTableId)}. Read current state with read_table before answering or changing this table.]',
          ).listen(
            (event) {
              if (mounted && generation == _generation) _event(event);
            },
            onError: (Object error) {
              if (mounted && generation == _generation) {
                _finish('The response could not be completed. $error');
              }
            },
            onDone: () {
              if (mounted && generation == _generation && _running) {
                if (_runFinished) {
                  _parentRunId = _activeRunId ?? _parentRunId;
                  _subscription = null;
                  _finish(null);
                } else {
                  _finish(
                    'The connection ended before the response completed.',
                  );
                }
              }
            },
          );
    } catch (error) {
      _finish('The response could not be started. $error');
    }
  }

  _Entry _entry(String id, String role) => _entries.firstWhere(
    (entry) => entry.id == id && entry.role == role,
    orElse: () {
      final entry = _Entry(id, role);
      _entries.add(entry);
      return entry;
    },
  );

  void _event(AgentEvent event) {
    if (event.type == 'RUN_FINISHED') {
      // The hosted adapter saves its session after this event. Drain to EOF
      // before enabling continuation; aborting here can cancel that save.
      _runFinished = true;
      return;
    }
    if (event.type == 'RUN_ERROR') {
      _finish(
        event.string('message') ??
            'The agent could not complete this response.',
      );
      return;
    }
    setState(() {
      switch (event.type) {
        case 'RUN_STARTED':
          _threadId = event.string('threadId') ?? _threadId;
          _activeRunId = event.string('runId');
        case 'TEXT_MESSAGE_START':
        case 'TEXT_MESSAGE_CONTENT':
        case 'TEXT_MESSAGE_END':
          final id = event.string('messageId');
          if (id == null) break;
          final entry = _entry(id, 'assistant');
          if (event.type == 'TEXT_MESSAGE_CONTENT') {
            entry.text += event.string('delta') ?? '';
          }
          if (event.type == 'TEXT_MESSAGE_END') entry.complete = true;
        case 'TOOL_CALL_START':
        case 'TOOL_CALL_ARGS':
        case 'TOOL_CALL_END':
        case 'TOOL_CALL_RESULT':
          final id = event.string('toolCallId');
          if (id == null) break;
          final entry = _entry(id, 'tool');
          entry.name = event.string('toolCallName') ?? entry.name;
          if (event.type == 'TOOL_CALL_ARGS') {
            entry.text += event.string('delta') ?? '';
          }
          if (event.type == 'TOOL_CALL_RESULT') {
            final content = event.data['content'];
            try {
              entry.result = content is String ? jsonDecode(content) : content;
            } on FormatException {
              entry.result = content;
            }
            final result = entry.result;
            if (result is Map && result['kind'] == 'table') {
              try {
                final table = TableSnapshot.fromJson(
                  Map<String, dynamic>.from(result),
                );
                _acceptTable(table);
                _activeTableId = table.id;
              } catch (_) {
                // A malformed tool result remains inspectable as ordinary JSON.
              }
            }
            entry.complete = true;
          }
      }
    });
    _followOutput();
  }

  void _finish(String? notice) {
    _generation++;
    unawaited(_subscription?.cancel());
    _subscription = null;
    setState(() {
      _running = false;
      _notice = notice;
      for (final entry in _entries.where((entry) => !entry.complete)) {
        entry.interrupted = true;
      }
    });
  }

  void _newConversation() {
    _generation++;
    _tableGeneration++;
    _openingTable = false;
    for (final table in _tables.values) {
      table.dispose();
    }
    _tables.clear();
    _activeTableId = null;
    unawaited(_subscription?.cancel());
    _subscription = null;
    setState(() {
      _threadId = _uuid.v4();
      _parentRunId = null;
      _activeRunId = null;
      _entries.clear();
      _composer.clear();
      _notice = null;
      _running = false;
    });
  }

  void _followOutput({bool force = false}) {
    if (!force && _scroll.hasClients && _scroll.position.extentAfter > 120) {
      return;
    }
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted && _scroll.hasClients) {
        _scroll.jumpTo(_scroll.position.maxScrollExtent);
      }
    });
  }

  Uri? _webUri(Object? value) {
    if (value is! String) return null;
    final uri = Uri.tryParse(value);
    return uri != null &&
            (uri.isScheme('https') || uri.isScheme('http')) &&
            uri.host.isNotEmpty &&
            uri.userInfo.isEmpty
        ? uri
        : null;
  }

  Future<void> _open(String url) async {
    final uri = _webUri(url);
    if (uri == null || widget.onOpenUrl == null) return;
    try {
      await widget.onOpenUrl!(uri);
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('The link could not be opened.')),
        );
      }
    }
  }

  Widget _markdown(String text) => SelectionArea(
    child: GptMarkdown(text, onLinkTap: (url, _) => _open(url)),
  );

  Widget _tool(_Entry entry) {
    final result = entry.result;
    final map = result is Map ? result : null;
    if (map?['kind'] == 'tableError') {
      return Card(
        margin: EdgeInsets.zero,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(
                    Icons.error_outline,
                    size: 18,
                    color: Theme.of(context).colorScheme.error,
                  ),
                  const SizedBox(width: 8),
                  const Expanded(
                    child: Text(
                      'Table request could not be completed',
                      style: TextStyle(fontWeight: FontWeight.w600),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Text(
                map?['message'] is String
                    ? map!['message'] as String
                    : 'Read the current table and try again.',
              ),
            ],
          ),
        ),
      );
    }
    final table = map?['kind'] == 'table' ? _tables[map?['id']] : null;
    if (table != null) {
      return KitDataTable(
        controller: table,
        active: _activeTableId == table.snapshot.id,
        onActivate: () => setState(() => _activeTableId = table.snapshot.id),
      );
    }
    final sources = map?['results'];
    final isSearch = entry.name == 'search_web' && sources is List;
    return Card(
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  entry.complete
                      ? Icons.check_circle_outline
                      : Icons.travel_explore,
                  size: 18,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    entry.name == 'search_web'
                        ? (entry.complete
                              ? 'Web search complete'
                              : (_running && !entry.interrupted
                                    ? 'Searching the web…'
                                    : 'Web search interrupted'))
                        : '${entry.name ?? 'Tool'}${entry.complete ? ' · Complete' : (_running && !entry.interrupted ? ' · Working…' : ' · Interrupted')}',
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                ),
              ],
            ),
            if (isSearch) ...[
              if (map?['answer'] case final String answer
                  when answer.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: _markdown(answer),
                ),
              for (final source in sources.whereType<Map>())
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: ListTile(
                    contentPadding: EdgeInsets.zero,
                    title: Text(
                      source['title'] is String
                          ? source['title'] as String
                          : 'Source',
                    ),
                    subtitle: Text(
                      [
                        if (source['url'] is String) source['url'],
                        if (source['content'] is String) source['content'],
                      ].join('\n'),
                      maxLines: 3,
                      overflow: TextOverflow.ellipsis,
                    ),
                    trailing: _webUri(source['url']) == null
                        ? null
                        : const Icon(Icons.open_in_new, size: 16),
                    onTap: _webUri(source['url']) == null
                        ? null
                        : () => _open(source['url'] as String),
                  ),
                ),
            ] else if (result != null)
              Padding(
                padding: const EdgeInsets.only(top: 12),
                child: SelectableText(
                  result is String
                      ? result
                      : const JsonEncoder.withIndent('  ').convert(result),
                ),
              ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text(
        'DigitalBrain',
        style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
      ),
      actions: [
        if (widget.onListTables != null && widget.onReadTable != null)
          IconButton(
            tooltip: 'Saved tables',
            onPressed: _openingTable ? null : _openSavedTables,
            icon: const Icon(Icons.table_chart_outlined),
          ),
        IconButton(
          tooltip: 'New conversation',
          onPressed: _newConversation,
          icon: const Icon(Icons.edit_square),
        ),
        const SizedBox(width: 16),
      ],
    ),
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 860),
          child: Column(
            children: [
              if (_activeTableId case final activeId?)
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 24),
                  child: Row(
                    children: [
                      Expanded(
                        child: Text(
                          'Active table: ${_tables[activeId]?.snapshot.title ?? activeId}',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      IconButton(
                        tooltip: 'Clear active table',
                        onPressed: () => setState(() => _activeTableId = null),
                        icon: const Icon(Icons.close, size: 18),
                      ),
                    ],
                  ),
                ),
              Expanded(
                child: _entries.isEmpty
                    ? const Center(
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Icon(Icons.auto_awesome_outlined, size: 32),
                            SizedBox(height: 20),
                            Text(
                              'How can I help?',
                              style: TextStyle(
                                fontSize: 28,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            SizedBox(height: 10),
                            Padding(
                              padding: EdgeInsets.symmetric(horizontal: 24),
                              child: Text(
                                'Think something through, ask a question, or search the web.',
                                textAlign: TextAlign.center,
                              ),
                            ),
                          ],
                        ),
                      )
                    : ListView.builder(
                        controller: _scroll,
                        padding: const EdgeInsets.symmetric(
                          horizontal: 24,
                          vertical: 24,
                        ),
                        itemCount: _entries.length,
                        itemBuilder: (context, index) {
                          final entry = _entries[index];
                          return Padding(
                            padding: const EdgeInsets.only(bottom: 24),
                            child: entry.role == 'tool'
                                ? _tool(entry)
                                : Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      Text(
                                        entry.role == 'user'
                                            ? 'You'
                                            : 'DigitalBrain',
                                        style: TextStyle(
                                          fontSize: 12,
                                          fontWeight: FontWeight.w600,
                                          color: Theme.of(context)
                                              .colorScheme
                                              .primary,
                                        ),
                                      ),
                                      const SizedBox(height: 8),
                                      if (entry.role == 'user')
                                        SelectableText(entry.text)
                                      else
                                        _markdown(
                                          entry.text.isEmpty ? '…' : entry.text,
                                        ),
                                    ],
                                  ),
                          );
                        },
                      ),
              ),
              if (_notice ?? widget.statusMessage case final String notice)
                Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 24,
                    vertical: 8,
                  ),
                  child: Column(
                    children: [
                      Text(
                        notice,
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.error,
                        ),
                      ),
                      if (_notice != null)
                        TextButton(
                          onPressed: _newConversation,
                          child: const Text('Start a new conversation'),
                        ),
                    ],
                  ),
                ),
              if (_running)
                const Padding(
                  padding: EdgeInsets.symmetric(horizontal: 24),
                  child: LinearProgressIndicator(minHeight: 2),
                ),
              Padding(
                padding: const EdgeInsets.fromLTRB(24, 12, 24, 20),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Expanded(
                      child: TextField(
                        controller: _composer,
                        enabled: !_running && widget.onRun != null,
                        minLines: 1,
                        maxLines: 6,
                        textInputAction: TextInputAction.send,
                        onSubmitted: (_) => _send(),
                        decoration: InputDecoration(
                          hintText: widget.onRun == null
                              ? 'Connect to start a conversation'
                              : 'Message DigitalBrain',
                          border: OutlineInputBorder(
                            borderRadius: BorderRadius.circular(18),
                          ),
                          contentPadding: const EdgeInsets.all(18),
                        ),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Padding(
                      padding: const EdgeInsets.only(bottom: 5),
                      child: IconButton.filled(
                        tooltip: _running ? 'Stop response' : 'Send message',
                        onPressed: _running
                            ? () => _finish('Response stopped.')
                            : (widget.onRun == null ? null : _send),
                        icon: Icon(
                          _running ? Icons.stop_rounded : Icons.arrow_upward,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );

  void _acceptTable(TableSnapshot snapshot) {
    final existing = _tables[snapshot.id];
    if (existing != null) {
      existing.accept(snapshot);
    } else {
      _tables[snapshot.id] = KitTableController(
        snapshot: snapshot,
        read: widget.onReadTable,
        update: widget.onUpdateTableView,
      );
    }
  }

  Future<void> _openSavedTables() async {
    final list = widget.onListTables;
    final read = widget.onReadTable;
    if (list == null || read == null || _openingTable) return;
    final generation = _tableGeneration;
    final future = list();
    final id = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Saved tables'),
        content: SizedBox(
          width: 420,
          child: FutureBuilder<List<TableSummary>>(
            future: future,
            builder: (context, snapshot) {
              if (snapshot.hasError) {
                return Text('Could not load saved tables. ${snapshot.error}');
              }
              if (!snapshot.hasData) {
                return const SizedBox(
                  height: 80,
                  child: Center(child: CircularProgressIndicator()),
                );
              }
              if (snapshot.data!.isEmpty) {
                return const Text(
                  'No saved tables yet. Ask DigitalBrain to create one.',
                );
              }
              return ConstrainedBox(
                constraints: const BoxConstraints(maxHeight: 400),
                child: ListView(
                  shrinkWrap: true,
                  children: [
                    for (final table in snapshot.data!)
                      ListTile(
                        leading: const Icon(Icons.table_chart_outlined),
                        title: Text(table.title),
                        subtitle: Text(table.id),
                        onTap: () => Navigator.pop(context, table.id),
                      ),
                  ],
                ),
              );
            },
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Close'),
          ),
        ],
      ),
    );
    if (!mounted || generation != _tableGeneration || id == null) return;
    setState(() => _openingTable = true);
    try {
      final snapshot = await read(id);
      if (!mounted || generation != _tableGeneration) return;
      setState(() {
        _acceptTable(snapshot);
        _activeTableId = snapshot.id;
        final entry = _Entry(_uuid.v4(), 'tool')
          ..name = 'read_table'
          ..result = snapshot.toJson()
          ..complete = true;
        _entries.add(entry);
      });
      _followOutput(force: true);
    } catch (e) {
      if (mounted && generation == _tableGeneration) {
        setState(() => _notice = 'Could not open the table. $e');
      }
    } finally {
      if (mounted && generation == _tableGeneration) {
        setState(() => _openingTable = false);
      }
    }
  }
}
