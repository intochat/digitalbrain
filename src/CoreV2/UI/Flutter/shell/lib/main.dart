import 'dart:async';

import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';
import 'package:flutter/material.dart';

void main() {
  final productBase = DigitalBrainHostEnvironment.requireProductBase();
  runApp(DigitalBrainShell(productBase: productBase));
}

class DigitalBrainShell extends StatefulWidget {
  const DigitalBrainShell({required this.productBase, this.api, super.key});

  final Uri productBase;
  final DigitalBrainProductApi? api;

  @override
  State<DigitalBrainShell> createState() => _DigitalBrainShellState();
}

class _DigitalBrainShellState extends State<DigitalBrainShell> {
  late final DigitalBrainProductApi _api;
  late final bool _ownsApi;
  final TextEditingController _message = TextEditingController();
  final ScrollController _chatScroll = ScrollController();
  StreamSubscription<BrainSnapshot>? _brainEvents;
  StreamSubscription<BrainJournalRecord>? _journalEvents;
  List<ProductModule> _modules = const [];
  List<ProductOperation> _operations = const [];
  List<_ChatMessage> _messages = const [];
  List<BrainJournalRecord> _journal = const [];
  BrainSnapshot? _brain;
  String? _activityId;
  Object? _error;
  bool _loading = true;
  bool _sending = false;

  @override
  void initState() {
    super.initState();
    _ownsApi = widget.api == null;
    _api = widget.api ?? DigitalBrainProductClient(baseUri: widget.productBase);
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final values = await Future.wait([
        _api.getModules(),
        _api.getOperations(),
        _api.getBrain(),
      ]);
      if (!mounted) return;
      final brain = values[2] as BrainSnapshot;
      setState(() {
        _modules = values[0] as List<ProductModule>;
        _operations = values[1] as List<ProductOperation>;
        _brain = brain;
        _loading = false;
      });
      _brainEvents = _api.watchBrain(afterSequence: brain.sequence).listen((
        snapshot,
      ) {
        if (mounted) setState(() => _brain = snapshot);
      }, onError: _showError);
    } on Object catch (error) {
      _showError(error);
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _send() async {
    final text = _message.text.trim();
    if (text.isEmpty || _sending) return;
    _message.clear();
    setState(() {
      _sending = true;
      _error = null;
      _activityId = null;
      _journal = const [];
      _messages = [..._messages, _ChatMessage.user(text)];
    });
    _scrollChat();

    try {
      if (!_operations.any((operation) => operation.id == 'Chat.Send@1')) {
        throw StateError('Chat.Send@1 is not installed in RuntimeHost.');
      }
      final receipt = await _api.invoke(
        'Chat.Send@1',
        {'message': text},
        idempotencyKey: 'flutter-${DateTime.now().microsecondsSinceEpoch}',
      );
      if (mounted) setState(() => _activityId = receipt.activityId);
      await _journalEvents?.cancel();
      _journalEvents = _api.watchJournal(receipt.activityId).listen((record) {
        if (!mounted) return;
        setState(() {
          if (_journal.every(
            (existing) => existing.sequence != record.sequence,
          )) {
            _journal = [..._journal, record]
              ..sort((a, b) => a.sequence.compareTo(b.sequence));
          }
        });
      }, onError: _showError);

      ProductActivity? terminal;
      await for (final activity in _api.watchActivity(receipt.activityId)) {
        if (activity.isTerminal) terminal = activity;
      }
      terminal ??= await _api.getActivity(receipt.activityId);
      if (!terminal.isCompleted) {
        throw StateError(
          terminal.problem ?? 'Chat ended as ${terminal.statusLabel}.',
        );
      }
      final result = terminal.result;
      if (result is! Map ||
          result['response'] is! String ||
          result['tools'] is! List) {
        throw const FormatException('Chat.Send@1 returned an invalid result.');
      }
      final tools = (result['tools'] as List)
          .map(
            (value) => ChatToolResult.fromJson(
              Map<String, Object?>.from(value as Map),
            ),
          )
          .toList(growable: false);
      final page = await _api.getJournal(receipt.activityId);
      final brain = await _api.getBrain();
      if (!mounted) return;
      setState(() {
        _journal = page.records;
        _brain = brain;
        _messages = [
          ..._messages,
          _ChatMessage.assistant(result['response'] as String, tools),
        ];
      });
      _scrollChat();
    } on Object catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  void _showError(Object error) {
    if (mounted) setState(() => _error = error);
  }

  void _scrollChat() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_chatScroll.hasClients) {
        _chatScroll.animateTo(
          _chatScroll.position.maxScrollExtent,
          duration: const Duration(milliseconds: 220),
          curve: Curves.easeOut,
        );
      }
    });
  }

  @override
  void dispose() {
    _brainEvents?.cancel();
    _journalEvents?.cancel();
    _message.dispose();
    _chatScroll.dispose();
    if (_ownsApi) _api.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'DigitalBrain CoreV2',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        brightness: Brightness.dark,
        scaffoldBackgroundColor: const Color(0xff070b12),
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xff5eead4),
          brightness: Brightness.dark,
          surface: const Color(0xff0d1420),
        ),
        useMaterial3: true,
      ),
      home: Scaffold(
        body: SafeArea(
          child: Column(
            children: [
              _header(),
              if (_error case final error?) _errorBar(error),
              Expanded(
                child: _loading
                    ? const Center(child: CircularProgressIndicator())
                    : _workspace(),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _header() => Container(
    height: 72,
    padding: const EdgeInsets.symmetric(horizontal: 22),
    decoration: const BoxDecoration(
      color: Color(0xff0a101a),
      border: Border(bottom: BorderSide(color: Color(0xff1d2b3c))),
    ),
    child: Row(
      children: [
        Container(
          width: 38,
          height: 38,
          decoration: BoxDecoration(
            color: const Color(0xff5eead4).withValues(alpha: .12),
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
              color: const Color(0xff5eead4).withValues(alpha: .4),
            ),
          ),
          child: const Icon(Icons.hub_outlined, color: Color(0xff5eead4)),
        ),
        const SizedBox(width: 12),
        const Column(
          mainAxisAlignment: MainAxisAlignment.center,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'DIGITALBRAIN',
              style: TextStyle(fontWeight: FontWeight.w800, letterSpacing: 1.4),
            ),
            Text(
              'CoreV2 neural observatory',
              style: TextStyle(fontSize: 11, color: Color(0xff7890a8)),
            ),
          ],
        ),
        const Spacer(),
        _statusDot('runtime', _modules.isNotEmpty),
        const SizedBox(width: 12),
        _statusDot('journal', true),
        const SizedBox(width: 18),
        Text(
          widget.productBase.authority,
          style: const TextStyle(fontSize: 12, color: Color(0xff7890a8)),
        ),
      ],
    ),
  );

  Widget _statusDot(String label, bool active) => Row(
    children: [
      Container(
        width: 7,
        height: 7,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          color: active ? const Color(0xff4ade80) : const Color(0xfff87171),
          boxShadow: active
              ? const [BoxShadow(color: Color(0x664ade80), blurRadius: 8)]
              : null,
        ),
      ),
      const SizedBox(width: 6),
      Text(
        label,
        style: const TextStyle(fontSize: 11, color: Color(0xff9fb0c3)),
      ),
    ],
  );

  Widget _errorBar(Object error) => Container(
    width: double.infinity,
    padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 9),
    color: const Color(0xff7f1d1d),
    child: Text('$error', style: const TextStyle(fontSize: 12)),
  );

  Widget _workspace() => LayoutBuilder(
    builder: (context, constraints) {
      final panels = [_chatPanel(), _graphPanel(), _journalPanel()];
      if (constraints.maxWidth >= 1050) {
        return Padding(
          padding: const EdgeInsets.all(12),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Expanded(flex: 4, child: panels[0]),
              const SizedBox(width: 12),
              Expanded(flex: 3, child: panels[1]),
              const SizedBox(width: 12),
              Expanded(flex: 3, child: panels[2]),
            ],
          ),
        );
      }
      return DefaultTabController(
        length: 3,
        child: Column(
          children: [
            const TabBar(
              tabs: [
                Tab(text: 'Chat'),
                Tab(text: 'BrainGraph'),
                Tab(text: 'Runtime journal'),
              ],
            ),
            Expanded(child: TabBarView(children: panels)),
          ],
        ),
      );
    },
  );

  Widget _panel({
    required String title,
    required String subtitle,
    required Widget child,
  }) => Container(
    decoration: BoxDecoration(
      color: const Color(0xff0d1420),
      borderRadius: BorderRadius.circular(16),
      border: Border.all(color: const Color(0xff1d2b3c)),
      boxShadow: const [
        BoxShadow(
          color: Color(0x33000000),
          blurRadius: 18,
          offset: Offset(0, 8),
        ),
      ],
    ),
    clipBehavior: Clip.antiAlias,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(18, 16, 18, 14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                title,
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 3),
              Text(
                subtitle,
                style: const TextStyle(fontSize: 11, color: Color(0xff7890a8)),
              ),
            ],
          ),
        ),
        const Divider(height: 1, color: Color(0xff1d2b3c)),
        Expanded(child: child),
      ],
    ),
  );

  Widget _chatPanel() => _panel(
    title: 'Chat',
    subtitle: _activityId == null
        ? 'Operation-backed assistant'
        : 'Activity ${_activityId!.substring(0, _activityId!.length.clamp(0, 12))}',
    child: Column(
      children: [
        Expanded(
          child: _messages.isEmpty
              ? const _EmptyChat()
              : ListView.builder(
                  controller: _chatScroll,
                  padding: const EdgeInsets.all(16),
                  itemCount: _messages.length,
                  itemBuilder: (context, index) =>
                      _messageBubble(_messages[index]),
                ),
        ),
        Container(
          padding: const EdgeInsets.all(14),
          decoration: const BoxDecoration(
            border: Border(top: BorderSide(color: Color(0xff1d2b3c))),
          ),
          child: Row(
            children: [
              Expanded(
                child: TextField(
                  key: const Key('chat-input'),
                  controller: _message,
                  minLines: 1,
                  maxLines: 4,
                  onSubmitted: (_) => _send(),
                  decoration: const InputDecoration(
                    hintText: 'Ask the brain to wire, run, or reason…',
                    filled: true,
                    fillColor: Color(0xff111b29),
                    border: OutlineInputBorder(borderSide: BorderSide.none),
                  ),
                ),
              ),
              const SizedBox(width: 10),
              IconButton.filled(
                key: const Key('chat-send'),
                onPressed: _sending ? null : _send,
                icon: _sending
                    ? const SizedBox.square(
                        dimension: 17,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.arrow_upward_rounded),
              ),
            ],
          ),
        ),
      ],
    ),
  );

  Widget _messageBubble(_ChatMessage message) => Align(
    alignment: message.user ? Alignment.centerRight : Alignment.centerLeft,
    child: Container(
      constraints: const BoxConstraints(maxWidth: 520),
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: message.user ? const Color(0xff153a45) : const Color(0xff152033),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(
          color: message.user
              ? const Color(0xff276a73)
              : const Color(0xff26384e),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(message.text),
          if (message.tools.isNotEmpty) ...[
            const SizedBox(height: 10),
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: message.tools
                  .map(
                    (tool) => Chip(
                      visualDensity: VisualDensity.compact,
                      avatar: const Icon(
                        Icons.bolt,
                        size: 14,
                        color: Color(0xfffbbf24),
                      ),
                      label: Text(
                        tool.operationId,
                        style: const TextStyle(fontSize: 10),
                      ),
                    ),
                  )
                  .toList(growable: false),
            ),
          ],
        ],
      ),
    ),
  );

  Widget _graphPanel() {
    final brain = _brain;
    return _panel(
      title: 'BrainGraph',
      subtitle: brain == null
          ? 'Waiting for projection'
          : 'live · seq ${brain.sequence} · ${brain.neurons.length} neurons · ${brain.synapses.length} synapses',
      child: brain == null
          ? const Center(child: CircularProgressIndicator())
          : Column(
              children: [
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: CustomPaint(
                      key: const Key('brain-graph'),
                      painter: _BrainGraphPainter(brain),
                      child: const SizedBox.expand(),
                    ),
                  ),
                ),
                SizedBox(
                  height: 128,
                  child: ListView(
                    padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
                    children: brain.neurons
                        .map((neuron) => _neuronRow(neuron))
                        .toList(growable: false),
                  ),
                ),
              ],
            ),
    );
  }

  Widget _neuronRow(BrainNeuron neuron) => Padding(
    padding: const EdgeInsets.only(top: 7),
    child: Row(
      children: [
        const Icon(Icons.circle, size: 9, color: Color(0xff5eead4)),
        const SizedBox(width: 8),
        Expanded(
          child: Text(
            neuron.roleId,
            style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
          ),
        ),
        Text(
          '${neuron.moduleId} · fired ${neuron.firingCount}',
          style: const TextStyle(fontSize: 10, color: Color(0xff7890a8)),
        ),
      ],
    ),
  );

  Widget _journalPanel() => _panel(
    title: 'Runtime journal',
    subtitle: _journal.isEmpty
        ? 'Causal records appear while chat runs'
        : '${_journal.length} ordered records · durable',
    child: _journal.isEmpty
        ? const Center(
            child: Padding(
              padding: EdgeInsets.all(28),
              child: Text(
                'Send a message to watch Operations, Neurons, and Synapses fire.',
                textAlign: TextAlign.center,
                style: TextStyle(color: Color(0xff7890a8)),
              ),
            ),
          )
        : ListView.separated(
            padding: const EdgeInsets.all(12),
            itemCount: _journal.length,
            separatorBuilder: (_, _) => const SizedBox(height: 8),
            itemBuilder: (context, index) => _journalRecord(_journal[index]),
          ),
  );

  Widget _journalRecord(BrainJournalRecord record) => Container(
    padding: const EdgeInsets.all(11),
    decoration: BoxDecoration(
      color: const Color(0xff101927),
      borderRadius: BorderRadius.circular(10),
      border: Border.all(color: const Color(0xff213248)),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Text(
              '#${record.sequence}',
              style: const TextStyle(
                fontSize: 10,
                color: Color(0xff5eead4),
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                record.contractId,
                style: const TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
            Text(
              record.directionLabel,
              style: const TextStyle(fontSize: 9, color: Color(0xffa78bfa)),
            ),
          ],
        ),
        const SizedBox(height: 6),
        Text(
          record.summary,
          style: const TextStyle(fontSize: 11, color: Color(0xffb5c2d2)),
        ),
        const SizedBox(height: 5),
        Text(
          record.neuronId,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontSize: 9, color: Color(0xff65798f)),
        ),
      ],
    ),
  );
}

final class _ChatMessage {
  const _ChatMessage({
    required this.user,
    required this.text,
    this.tools = const [],
  });
  final bool user;
  final String text;
  final List<ChatToolResult> tools;
  factory _ChatMessage.user(String text) =>
      _ChatMessage(user: true, text: text);
  factory _ChatMessage.assistant(String text, List<ChatToolResult> tools) =>
      _ChatMessage(user: false, text: text, tools: tools);
}

class _EmptyChat extends StatelessWidget {
  const _EmptyChat();
  @override
  Widget build(BuildContext context) => const Center(
    child: Padding(
      padding: EdgeInsets.all(28),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.auto_awesome, size: 34, color: Color(0xff5eead4)),
          SizedBox(height: 14),
          Text(
            'Talk to the living graph',
            style: TextStyle(fontWeight: FontWeight.w700),
          ),
          SizedBox(height: 6),
          Text(
            'Every answer is an Operation. Every firing is journaled.',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 12, color: Color(0xff7890a8)),
          ),
        ],
      ),
    ),
  );
}

class _BrainGraphPainter extends CustomPainter {
  const _BrainGraphPainter(this.brain);
  final BrainSnapshot brain;

  @override
  void paint(Canvas canvas, Size size) {
    final nodes = <String, Offset>{};
    final center = Offset(size.width / 2, size.height / 2);
    final radius = (size.shortestSide * .34).clamp(45.0, 150.0);
    for (var index = 0; index < brain.neurons.length; index++) {
      final angle = brain.neurons.length == 1
          ? 0.0
          : (index / brain.neurons.length) * 6.283185307;
      nodes[brain.neurons[index].id] =
          center + Offset.fromDirection(angle, radius);
    }
    final edgePaint = Paint()
      ..color = const Color(0xff38bdf8).withValues(alpha: .58)
      ..strokeWidth = 2;
    for (final synapse in brain.synapses) {
      final source = nodes[synapse.sourceNeuronId];
      final target = nodes[synapse.targetNeuronId];
      if (source == null || target == null) continue;
      canvas.drawLine(source, target, edgePaint);
      final midpoint = Offset(
        (source.dx + target.dx) / 2,
        (source.dy + target.dy) / 2,
      );
      canvas.drawCircle(
        midpoint,
        3 + synapse.usageCount.clamp(0, 5).toDouble(),
        Paint()..color = const Color(0xfffbbf24),
      );
    }
    for (final neuron in brain.neurons) {
      final point = nodes[neuron.id]!;
      canvas.drawCircle(point, 24, Paint()..color = const Color(0x225eead4));
      canvas.drawCircle(point, 14, Paint()..color = const Color(0xff5eead4));
      canvas.drawCircle(point, 5, Paint()..color = const Color(0xff071018));
    }
  }

  @override
  bool shouldRepaint(covariant _BrainGraphPainter oldDelegate) =>
      oldDelegate.brain.sequence != brain.sequence;
}
