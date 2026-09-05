import 'dart:convert';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'brain_chat_screen.dart';
import 'brain_graph_store.dart';
import 'chat_contracts.dart';
import 'graph_examples_screen.dart';

final class GraphHomeScreen extends StatefulWidget {
  const GraphHomeScreen({
    super.key,
    required this.chatName,
    required this.turns,
    this.onSend,
    this.onStream,
    this.onStreamVoice,
    this.onAttachmentTap,
    this.onOpenSignIn,
    this.kernelBaseUri,
    this.onCancelTurn,
    this.onReadChart,
    this.onReadImageBytes,
    this.onReadSpreadsheet,
    this.onReadGraph,
    this.sceneFactory,
    this.onReadBrain,
    this.onSetBrainSubscription,
    this.conversation = false,
  });
  final String chatName;
  final List<ChatTurnEvent> turns;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final StreamVoice? onStreamVoice;
  final VoidCallback? onAttachmentTap;
  final OpenUrl? onOpenSignIn;
  final Uri? kernelBaseUri;
  final CancelChatTurn? onCancelTurn;
  final ReadChart? onReadChart;
  final ReadImageBytes? onReadImageBytes;
  final ReadSpreadsheet? onReadSpreadsheet;
  final ReadGraph? onReadGraph;
  final GraphSceneFactory? sceneFactory;
  final ReadBrain? onReadBrain;
  final SetBrainSubscription? onSetBrainSubscription;
  final bool conversation;
  @override
  State<GraphHomeScreen> createState() => _GraphHomeScreenState();
}

final class _GraphHomeScreenState extends State<GraphHomeScreen> {
  late BrainGraphStore _brain;
  final _chatKey = GlobalKey();
  String? _selected;
  bool _directory = false;
  @override
  void initState() {
    super.initState();
    _createStore();
  }

  void _createStore() {
    _brain = BrainGraphStore(
      read: widget.onReadBrain,
      setSubscription: widget.onSetBrainSubscription,
    )..addListener(_changed);
  }

  void _changed() {
    if (mounted) setState(() {});
  }

  @override
  void didUpdateWidget(covariant GraphHomeScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.chatName != widget.chatName ||
        oldWidget.onReadBrain != widget.onReadBrain ||
        oldWidget.onSetBrainSubscription != widget.onSetBrainSubscription) {
      _brain.removeListener(_changed);
      _brain.dispose();
      _createStore();
      _selected = null;
    }
  }

  @override
  void dispose() {
    _brain.removeListener(_changed);
    _brain.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Material(
    key: const Key('graph_home_screen'),
    color: LumenPalette.background,
    child: LayoutBuilder(
      builder: (context, constraints) {
        final narrow = constraints.maxWidth < 680;
        final snapshot = _brain.snapshot;
        final inspector = _selected != null || _directory;
        return Stack(
          children: [
            Column(
              children: [
                if (!widget.conversation) ...[
                  Padding(
                    padding: EdgeInsets.fromLTRB(
                      narrow ? 20 : 36,
                      22,
                      narrow ? 20 : 36,
                      6,
                    ),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                'A little more headspace.',
                                style: TextStyle(
                                  fontFamily: 'Georgia',
                                  fontSize: narrow ? 27 : 34,
                                  letterSpacing: -1,
                                  color: LumenPalette.ink,
                                ),
                              ),
                              const SizedBox(height: 7),
                              Text(
                                snapshot == null
                                    ? 'Your assistant, and the connections behind it.'
                                    : '${snapshot.nodes.length} neurons · ${snapshot.synapses.length} synapses · your current conversation',
                                style: const TextStyle(
                                  fontSize: 12,
                                  color: LumenPalette.muted,
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(width: 10),
                        LumenIconButton(
                          key: const Key('brain_directory'),
                          icon: const Icon(Icons.list_alt_rounded, size: 18),
                          label: 'Neuron directory',
                          selected: _directory,
                          onPressed: () => setState(() {
                            _directory = !_directory;
                            _selected = null;
                          }),
                        ),
                      ],
                    ),
                  ),
                  Expanded(
                    key: const Key('graph_brain_panel'),
                    child: snapshot == null
                        ? _emptyGraph()
                        : LumenBrainGraph(
                            snapshot: snapshot,
                            selectedId: _selected,
                            activeNodes: _brain.activeNodes,
                            stale: _brain.stale,
                            activeEdges: _brain.activeEdges,
                            onNeuron: (node) => setState(() {
                              _selected = node.id;
                              _directory = false;
                            }),
                            onSynapse: (edge) => setState(() {
                              _selected = edge.id;
                              _directory = false;
                            }),
                          ),
                  ),
                  Padding(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 24,
                      vertical: 8,
                    ),
                    child: Wrap(
                      alignment: WrapAlignment.center,
                      spacing: 16,
                      runSpacing: 6,
                      children: [
                        const Text(
                          '━━ Bound   ┄┄ Learned   → Signal direction',
                          style: TextStyle(
                            color: LumenPalette.muted,
                            fontSize: 10,
                          ),
                        ),
                        Text(
                          _brain.failure != null
                              ? 'Observation unavailable'
                              : snapshot == null
                              ? (widget.onReadBrain == null
                                    ? 'Not connected'
                                    : 'Connecting…')
                              : 'Observed ${_time(snapshot.observedAt)}${snapshot.truncated ? ' · limited view' : ''}',
                          style: TextStyle(
                            fontSize: 10,
                            color: _brain.failure == null
                                ? LumenPalette.muted
                                : LumenPalette.error,
                          ),
                        ),
                        InkWell(
                          onTap: _examples,
                          child: const Text(
                            'Play an example ↗',
                            style: TextStyle(
                              fontSize: 10,
                              color: LumenPalette.accent,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
                if (_brain.failure != null && !widget.conversation)
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 24),
                    child: Row(
                      children: [
                        Expanded(
                          child: Text(
                            _brain.failure!,
                            style: const TextStyle(
                              color: LumenPalette.error,
                              fontSize: 12,
                            ),
                          ),
                        ),
                        TextButton(
                          onPressed: _brain.refresh,
                          child: const Text('Retry'),
                        ),
                      ],
                    ),
                  ),
                if (widget.conversation)
                  Expanded(child: _chat())
                else
                  Padding(
                    key: const Key('graph_chat_panel'),
                    padding: EdgeInsets.fromLTRB(
                      narrow ? 12 : 28,
                      0,
                      narrow ? 12 : 28,
                      16,
                    ),
                    child: Center(
                      child: ConstrainedBox(
                        constraints: const BoxConstraints(maxWidth: 780),
                        child: _chat(),
                      ),
                    ),
                  ),
              ],
            ),
            if (inspector && !widget.conversation) ...[
              if (narrow)
                Positioned.fill(
                  child: GestureDetector(
                    onTap: () => setState(() {
                      _selected = null;
                      _directory = false;
                    }),
                    child: ColoredBox(
                      color: Colors.black.withValues(alpha: .16),
                    ),
                  ),
                ),
              Positioned(
                top: narrow ? 50 : 12,
                bottom: 12,
                right: 12,
                left: narrow ? 12 : null,
                width: narrow ? null : 350,
                child: _inspector(snapshot),
              ),
            ],
          ],
        );
      },
    ),
  );

  Widget _chat() => BrainChatScreen(
    key: _chatKey,
    chatName: widget.chatName,
    presentation: widget.conversation
        ? BrainChatPresentation.full
        : BrainChatPresentation.compact,
    compactReplyMaxHeight: 140,
    turns: widget.turns,
    onSend: widget.onSend,
    onStream: widget.onStream,
    onStreamVoice: widget.onStreamVoice,
    onAttachmentTap: widget.onAttachmentTap,
    onOpenSignIn: widget.onOpenSignIn,
    kernelBaseUri: widget.kernelBaseUri,
    onCancelTurn: widget.onCancelTurn,
    onReadChart: widget.onReadChart,
    onReadImageBytes: widget.onReadImageBytes,
    onReadSpreadsheet: widget.onReadSpreadsheet,
    onReadGraph: widget.onReadGraph,
  );

  Widget _emptyGraph() => Center(
    child: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          InoPresence(
            size: 86,
            state: widget.onReadBrain == null || _brain.failure != null
                ? InoPresenceState.disconnected
                : InoPresenceState.idle,
          ),
          const SizedBox(height: 18),
          Text(
            _brain.failure != null
                ? 'The brain is taking a moment to reconnect.'
                : widget.onReadBrain == null
                ? 'Connect to bring your brain into view.'
                : 'Getting to know your brain…',
            textAlign: TextAlign.center,
            style: const TextStyle(color: LumenPalette.muted),
          ),
          const SizedBox(height: 8),
          const Text(
            'Ino',
            style: TextStyle(
              color: LumenPalette.ink,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    ),
  );

  Widget _inspector(BrainSnapshot? snapshot) {
    BrainNeuron? node;
    BrainSynapse? edge;
    for (final value in snapshot?.nodes ?? <BrainNeuron>[]) {
      if (value.id == _selected) node = value;
    }
    for (final value in snapshot?.synapses ?? <BrainSynapse>[]) {
      if (value.id == _selected) edge = value;
    }
    return LumenSurface(
      padding: const EdgeInsets.all(22),
      child: Material(
        type: MaterialType.transparency,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    _directory
                        ? 'Your neurons'
                        : node != null
                        ? 'Neuron'
                        : 'Synapse',
                    style: const TextStyle(
                      fontSize: 20,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
                LumenIconButton(
                  icon: const Icon(Icons.close, size: 16),
                  label: 'Close inspector',
                  onPressed: () => setState(() {
                    _selected = null;
                    _directory = false;
                  }),
                ),
              ],
            ),
            const SizedBox(height: 18),
            Expanded(
              child: SingleChildScrollView(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: _directory
                      ? [
                          Text(
                            snapshot?.scope ?? 'Connect to see neurons.',
                            style: const TextStyle(
                              fontSize: 12,
                              color: LumenPalette.muted,
                            ),
                          ),
                          const SizedBox(height: 18),
                          for (final item in snapshot?.nodes ?? <BrainNeuron>[])
                            ListTile(
                              contentPadding: EdgeInsets.zero,
                              leading: NeuronIcon(
                                kind: brainNeuronIcon(item),
                                size: 28,
                              ),
                              title: Text(item.label),
                              subtitle: Text(
                                '${item.module} · ${item.name}',
                                style: const TextStyle(fontSize: 11),
                              ),
                              onTap: () => setState(() {
                                _directory = false;
                                _selected = item.id;
                              }),
                            ),
                        ]
                      : node != null
                      ? _nodeDetails(node, snapshot!)
                      : edge != null
                      ? _edgeDetails(edge, snapshot!)
                      : [
                          const Text(
                            'This connection is no longer in the current graph.',
                          ),
                          const SizedBox(height: 12),
                          const Text(
                            'Unsubscribing removes the entire synapse.',
                            style: TextStyle(color: LumenPalette.muted),
                          ),
                        ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  List<Widget> _nodeDetails(BrainNeuron node, BrainSnapshot snapshot) => [
    Align(
      alignment: Alignment.centerLeft,
      child: NeuronIcon(kind: brainNeuronIcon(node), size: 44),
    ),
    const SizedBox(height: 14),
    Text(
      node.label,
      style: const TextStyle(fontFamily: 'Georgia', fontSize: 27),
    ),
    _detail('Module', node.module),
    _detail('Instance', node.name),
    _detail('Status', node.status),
    _detail('Identity', node.id),
    _detail(
      'Observed signals',
      '${node.incomingSequence} incoming · ${node.outgoingSequence} outgoing',
    ),
    if (node.lastActivityAt != null)
      _detail('Last activity', _time(node.lastActivityAt!)),
    const SizedBox(height: 12),
    LumenActionButton(
      label: 'Create subscription',
      icon: const Icon(Icons.add, size: 16),
      onPressed: widget.onSetBrainSubscription == null || _brain.mutating
          ? null
          : () => _subscribeDialog(node, snapshot),
    ),
    const SizedBox(height: 20),
    const Text('Connections', style: TextStyle(fontWeight: FontWeight.w600)),
    for (final edge in snapshot.synapses.where(
      (e) => e.sourceId == node.id || e.targetId == node.id,
    ))
      ListTile(
        contentPadding: EdgeInsets.zero,
        dense: true,
        title: Text(_short(edge.signalType)),
        subtitle: Text(
          '${edge.kind} · ${edge.sourceId == node.id ? 'outgoing' : 'incoming'}',
        ),
        trailing: const Icon(Icons.arrow_forward, size: 16),
        onTap: () => setState(() => _selected = edge.id),
      ),
    const SizedBox(height: 16),
    const Text(
      'Recent activity',
      style: TextStyle(fontWeight: FontWeight.w600),
    ),
    if (!snapshot.activity.any((e) => e.neuronId == node.id))
      const Padding(
        padding: EdgeInsets.only(top: 12),
        child: Text(
          'No recorded activity in this observation.',
          style: TextStyle(fontSize: 12, color: LumenPalette.muted),
        ),
      ),
    for (final activity
        in snapshot.activity.where((e) => e.neuronId == node.id).take(8))
      _activity(activity),
  ];

  List<Widget> _edgeDetails(BrainSynapse edge, BrainSnapshot snapshot) => [
    const Icon(Icons.route_outlined, size: 36, color: LumenPalette.accent),
    const SizedBox(height: 16),
    Text(
      _short(edge.signalType),
      style: const TextStyle(fontFamily: 'Georgia', fontSize: 24),
    ),
    _detail('Source', edge.sourceId),
    _detail('Subscriber', edge.targetId),
    _detail('Signal type', edge.signalType),
    _detail('Connection', edge.kind),
    _detail('Recorded deliveries', '${edge.fireCount}'),
    if (edge.lastFiredAt != null && edge.fireCount > 0)
      _detail('Last delivery', _time(edge.lastFiredAt!)),
    _detail('Delivery', edge.isBlocking ? 'Blocking' : 'Non-blocking'),
    _detail('Weight', edge.weight.toStringAsFixed(3)),
    const SizedBox(height: 16),
    Text(
      edge.kind == 'Bound'
          ? 'An explicit subscription, owned by the source neuron. Unsubscribing removes this connection completely.'
          : edge.kind == 'Learned'
          ? 'Reinforced by handled direct delivery. This is not an explicit subscription.'
          : 'A kernel-defined connection.',
      style: const TextStyle(
        fontSize: 12,
        color: LumenPalette.muted,
        height: 1.5,
      ),
    ),
    if (edge.canUnsubscribe) ...[
      const SizedBox(height: 16),
      LumenActionButton(
        key: const Key('unsubscribe_synapse'),
        label: _brain.mutating ? 'Confirming…' : 'Unsubscribe',
        onPressed: _brain.mutating || widget.onSetBrainSubscription == null
            ? null
            : () async {
                final done = await _brain.subscribe(
                  sourceId: edge.sourceId,
                  targetId: edge.targetId,
                  signalType: edge.signalType,
                  subscribed: false,
                );
                if (mounted && done) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('Subscription removed.')),
                  );
                }
              },
      ),
    ],
    if (_brain.failure != null) _detail('Could not confirm', _brain.failure!),
    const SizedBox(height: 20),
    const Text(
      'Recent source signals',
      style: TextStyle(fontWeight: FontWeight.w600),
    ),
    const Text(
      'A source journal entry alone does not prove delivery across this edge.',
      style: TextStyle(fontSize: 11, color: LumenPalette.muted),
    ),
    for (final activity
        in snapshot.activity
            .where(
              (a) =>
                  a.neuronId == edge.sourceId &&
                  a.signalType == edge.signalType &&
                  a.direction == 'Outgoing',
            )
            .take(4))
      _activity(activity),
  ];

  Widget _activity(BrainActivity value) => Padding(
    padding: const EdgeInsets.only(top: 14),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          '${value.direction} · ${_short(value.signalType)}',
          style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
        ),
        Text(
          '${_time(value.timestamp)} · #${value.sequence}',
          style: const TextStyle(fontSize: 10, color: LumenPalette.muted),
        ),
        if (value.summary.isNotEmpty)
          Text(value.summary, style: const TextStyle(fontSize: 12)),
        if (value.payloadPreview != null)
          Padding(
            padding: const EdgeInsets.only(top: 6),
            child: SelectableText(
              _preview(value.payloadPreview),
              style: const TextStyle(fontSize: 11, color: LumenPalette.muted),
            ),
          ),
      ],
    ),
  );
  Widget _detail(String label, String text) => Padding(
    padding: const EdgeInsets.only(top: 14),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label.toUpperCase(),
          style: const TextStyle(
            fontSize: 9,
            letterSpacing: 1.4,
            color: LumenPalette.muted,
          ),
        ),
        const SizedBox(height: 4),
        SelectableText(text, style: const TextStyle(fontSize: 12, height: 1.5)),
      ],
    ),
  );

  Future<void> _subscribeDialog(
    BrainNeuron source,
    BrainSnapshot snapshot,
  ) async {
    // Match the kernel's principal partition ({32 hex digits}.{local name}).
    // This only limits offered choices; the server authorizes every mutation.
    final partition = RegExp(
      r'^[^:]+:([0-9a-fA-F]{32}\.)',
    ).firstMatch(snapshot.rootId)?.group(1)?.toLowerCase();
    final targets = snapshot.nodes
        .where(
          (n) =>
              n.id != source.id &&
              n.handledSignals.isNotEmpty &&
              partition != null &&
              n.id.split(':').last.toLowerCase().startsWith(partition),
        )
        .toList();
    if (targets.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('No eligible subscribers in this graph.')),
      );
      return;
    }
    var target = targets.first;
    var signal = target.handledSignals.first;
    var busy = false;
    String? error;
    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, update) => AlertDialog(
          title: const Text('Create a subscription'),
          content: SizedBox(
            width: 380,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'Source: ${source.label}',
                  style: const TextStyle(color: LumenPalette.muted),
                ),
                const SizedBox(height: 14),
                DropdownButtonFormField<String>(
                  initialValue: target.id,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Subscriber'),
                  items: [
                    for (final node in targets)
                      DropdownMenuItem(
                        value: node.id,
                        child: Text(
                          '${node.label} · ${node.name}',
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                  ],
                  onChanged: busy
                      ? null
                      : (value) => update(() {
                          target = targets.firstWhere((n) => n.id == value);
                          signal = target.handledSignals.first;
                        }),
                ),
                const SizedBox(height: 14),
                DropdownButtonFormField<String>(
                  key: ValueKey(target.id),
                  initialValue: signal,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Signal'),
                  items: [
                    for (final type in target.handledSignals)
                      DropdownMenuItem(
                        value: type,
                        child: Text(
                          _short(type),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                  ],
                  onChanged: busy
                      ? null
                      : (value) => update(() => signal = value!),
                ),
                const SizedBox(height: 18),
                const Text(
                  'The subscriber will receive matching signals broadcast by this source.',
                  style: TextStyle(fontSize: 12),
                ),
                if (error != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 12),
                    child: Text(
                      error!,
                      style: const TextStyle(color: LumenPalette.error),
                    ),
                  ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: busy ? null : () => Navigator.pop(dialogContext),
              child: const Text('Cancel'),
            ),
            LumenActionButton(
              label: busy ? 'Confirming…' : 'Subscribe',
              primary: true,
              onPressed: busy
                  ? null
                  : () async {
                      update(() {
                        busy = true;
                        error = null;
                      });
                      final done = await _brain.subscribe(
                        sourceId: source.id,
                        targetId: target.id,
                        signalType: signal,
                        subscribed: true,
                      );
                      if (!dialogContext.mounted) return;
                      if (done) {
                        Navigator.pop(dialogContext);
                      } else {
                        update(() {
                          busy = false;
                          error =
                              _brain.failure ??
                              'Could not confirm the subscription.';
                        });
                      }
                    },
            ),
          ],
        ),
      ),
    );
  }

  void _examples() => Navigator.of(context).push(
    MaterialPageRoute<void>(
      builder: (context) => Theme(
        data: KitTheme.dark(),
        child: Scaffold(
          appBar: AppBar(title: const Text('Graph examples · simulation')),
          body: GraphExamplesScreen(
            chatName: widget.chatName,
            turns: const [],
            sceneFactory: widget.sceneFactory,
          ),
        ),
      ),
    ),
  );
  String _short(String value) => value.split('.').last;
  String _time(DateTime time) {
    final t = time.toLocal();
    return '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}:${t.second.toString().padLeft(2, '0')}';
  }

  String _preview(Object? value) {
    final text = value is String
        ? value
        : const JsonEncoder.withIndent('  ').convert(value);
    return text.length > 1200 ? '${text.substring(0, 1200)}…' : text;
  }
}
