import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

/// RFW v2 prototype — see docs/rfw-v2-design.md.
///
/// Self-contained: simulates a server-side LlmNeuron emitting a stream of
/// `UiPatch` messages over time. The on-screen tree mutates incrementally
/// via [_V2Tree.apply]. The right-hand panel tails the patch log so the
/// mechanism is visible alongside the rendered UI.
///
/// When v2 ships for real:
///   * `_UiPatch` and `_PatchOp` move to grpc/generated alongside ChatChunk.
///   * `_V2Tree` becomes per-message in InoBlocState.
///   * The widget palette below moves to ui/components/v2/.
class RfwV2DemoScreen extends StatefulWidget {
  const RfwV2DemoScreen({super.key});

  @override
  State<RfwV2DemoScreen> createState() => _RfwV2DemoScreenState();
}

class _RfwV2DemoScreenState extends State<RfwV2DemoScreen> {
  final _tree = _V2Tree();
  final _log = <_LoggedPatch>[];
  Timer? _ticker;
  Stopwatch? _clock;
  int _cursor = 0;
  double _speed = 1.0;

  @override
  void dispose() {
    _ticker?.cancel();
    _tree.dispose();
    super.dispose();
  }

  void _replay() {
    _ticker?.cancel();
    setState(() {
      _tree.clear();
      _log.clear();
      _cursor = 0;
    });
    _clock = Stopwatch()..start();
    _ticker = Timer.periodic(const Duration(milliseconds: 30), (_) {
      final elapsedMs = (_clock!.elapsedMilliseconds * _speed).round();
      var advanced = false;
      while (_cursor < _demoScript.length &&
          _demoScript[_cursor].atMs <= elapsedMs) {
        final patch = _demoScript[_cursor].patch;
        _tree.apply(patch);
        _log.add(_LoggedPatch(elapsedMs, patch));
        _cursor++;
        advanced = true;
      }
      if (advanced) setState(() {});
      if (_cursor >= _demoScript.length) {
        _ticker?.cancel();
        _ticker = null;
      }
    });
  }

  void _reset() {
    _ticker?.cancel();
    setState(() {
      _tree.clear();
      _log.clear();
      _cursor = 0;
    });
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        leading: IconButton(
          icon: const Icon(Icons.arrow_back),
          onPressed: () => context.go('/brain'),
        ),
        title: const Text('RFW v2 prototype'),
        actions: [
          PopupMenuButton<double>(
            icon: const Icon(Icons.speed),
            tooltip: 'Replay speed',
            initialValue: _speed,
            onSelected: (s) => setState(() => _speed = s),
            itemBuilder: (_) => const [
              PopupMenuItem(value: 0.5, child: Text('0.5×')),
              PopupMenuItem(value: 1.0, child: Text('1× (real-time)')),
              PopupMenuItem(value: 2.0, child: Text('2×')),
              PopupMenuItem(value: 4.0, child: Text('4× (review)')),
            ],
          ),
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Reset',
            onPressed: _reset,
          ),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 8),
            child: FilledButton.icon(
              icon: const Icon(Icons.play_arrow, size: 18),
              label: const Text('Replay'),
              onPressed: _replay,
            ),
          ),
        ],
      ),
      body: LayoutBuilder(
        builder: (context, constraints) {
          final wide = constraints.maxWidth >= 900;
          if (wide) {
            return Row(
              children: [
                Expanded(flex: 3, child: _RenderedPane(tree: _tree)),
                Container(width: 1, color: scheme.outlineVariant),
                Expanded(flex: 2, child: _PatchLogPane(log: _log)),
              ],
            );
          }
          return Column(
            children: [
              Expanded(flex: 3, child: _RenderedPane(tree: _tree)),
              Container(height: 1, color: scheme.outlineVariant),
              Expanded(flex: 2, child: _PatchLogPane(log: _log)),
            ],
          );
        },
      ),
    );
  }
}

class _RenderedPane extends StatelessWidget {
  const _RenderedPane({required this.tree});
  final _V2Tree tree;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return AnimatedBuilder(
      animation: tree,
      builder: (context, _) {
        final root = tree.root;
        if (root.children.isEmpty) {
          return Center(
            child: Padding(
              padding: const EdgeInsets.all(32),
              child: Text(
                'Press Replay to stream a simulated assistant turn.\n'
                'Watch the tree grow widget by widget — each entry on the right '
                'is one UiPatch synapse from the server.',
                textAlign: TextAlign.center,
                style: TextStyle(
                  color: scheme.onSurface.withValues(alpha: 0.6),
                  height: 1.5,
                ),
              ),
            ),
          );
        }
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            for (final node in root.children) _buildNode(context, node),
          ],
        );
      },
    );
  }

  Widget _buildNode(BuildContext context, _V2Node node) {
    final builder = _palette[node.widgetType];
    if (builder == null) {
      return _UnknownWidget(type: node.widgetType);
    }
    final children = [for (final c in node.children) _buildNode(context, c)];
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: builder(context, node.props, children),
    );
  }
}

class _PatchLogPane extends StatelessWidget {
  const _PatchLogPane({required this.log});
  final List<_LoggedPatch> log;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      color: const Color(0xFF0A0A0A),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: Row(
              children: [
                Icon(Icons.bolt, size: 16, color: scheme.primary),
                const SizedBox(width: 6),
                Text(
                  'UiPatch synapse log',
                  style: TextStyle(
                    color: scheme.onSurface.withValues(alpha: 0.8),
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const Spacer(),
                Text('${log.length} patches',
                    style: TextStyle(
                        color: scheme.onSurface.withValues(alpha: 0.5),
                        fontSize: 12)),
              ],
            ),
          ),
          Expanded(
            child: ListView.builder(
              padding: const EdgeInsets.symmetric(horizontal: 12),
              itemCount: log.length,
              itemBuilder: (context, i) {
                final entry = log[log.length - 1 - i];
                return _PatchRow(entry: entry);
              },
            ),
          ),
        ],
      ),
    );
  }
}

class _PatchRow extends StatelessWidget {
  const _PatchRow({required this.entry});
  final _LoggedPatch entry;

  static const _opColors = {
    _PatchOp.append: Color(0xFF66BB6A),
    _PatchOp.update: Color(0xFF42A5F5),
    _PatchOp.replace: Color(0xFFFFB74D),
    _PatchOp.remove: Color(0xFFEF5350),
  };

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final opColor = _opColors[entry.patch.op]!;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 56,
            child: Text(
              '${entry.atMs}ms',
              style: TextStyle(
                fontFamily: 'monospace',
                fontSize: 11,
                color: scheme.onSurface.withValues(alpha: 0.5),
              ),
            ),
          ),
          Container(
            margin: const EdgeInsets.only(top: 2, right: 8),
            padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
            decoration: BoxDecoration(
              color: opColor.withValues(alpha: 0.18),
              borderRadius: BorderRadius.circular(4),
            ),
            child: Text(
              entry.patch.op.name.toUpperCase(),
              style: TextStyle(
                fontFamily: 'monospace',
                fontSize: 10,
                color: opColor,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          Expanded(
            child: Text.rich(
              TextSpan(
                style: const TextStyle(
                    fontFamily: 'monospace', fontSize: 12, height: 1.4),
                children: [
                  TextSpan(
                    text: entry.patch.widgetType.isEmpty
                        ? '—'
                        : entry.patch.widgetType,
                    style: TextStyle(
                        color: scheme.primary, fontWeight: FontWeight.w600),
                  ),
                  TextSpan(
                    text: '  #${entry.patch.nodeId}',
                    style: TextStyle(
                        color: scheme.onSurface.withValues(alpha: 0.7)),
                  ),
                  if (entry.patch.props.isNotEmpty)
                    TextSpan(
                      text: '\n${_summarize(entry.patch.props)}',
                      style: TextStyle(
                          color: scheme.onSurface.withValues(alpha: 0.5)),
                    ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  String _summarize(Map<String, dynamic> props) {
    final pairs = props.entries.take(3).map((e) {
      final v = e.value;
      final s = v is String && v.length > 28 ? '${v.substring(0, 28)}…' : '$v';
      return '${e.key}=$s';
    }).join(', ');
    return props.length > 3 ? '$pairs, …' : pairs;
  }
}

// ---------------------------------------------------------------------------
// V2 widget palette — mirrors the table in docs/rfw-v2-design.md.
// Each builder takes (context, props, children) and returns a Widget.
// ---------------------------------------------------------------------------

typedef _V2WidgetBuilder = Widget Function(
    BuildContext context, Map<String, dynamic> props, List<Widget> children);

final Map<String, _V2WidgetBuilder> _palette = {
  'RoutingDecision': _routingDecision,
  'SynapseTimeline': _synapseTimeline,
  'ToolCallTrace': _toolCallTrace,
  'Suggestion': _suggestion,
  'Confirmation': _confirmation,
  'MetricStrip': _metricStrip,
  'ProgressNote': _progressNote,
  'Markdown': _markdown,
  'CodeBlock': _codeBlock,
  'KeyValueList': _keyValueList,
  'Embed': _embed,
  'Group': _group,
};

Widget _routingDecision(BuildContext ctx, Map<String, dynamic> p, List<Widget> _) {
  final scheme = Theme.of(ctx).colorScheme;
  final status = p['status'] as String? ?? 'pending';
  final confidence = (p['confidence'] as num?)?.toDouble() ?? 0;
  final isPending = status == 'pending';
  return Container(
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: scheme.surface,
      borderRadius: BorderRadius.circular(10),
      border: Border.all(color: scheme.outlineVariant),
    ),
    child: Row(
      children: [
        Icon(
          isPending ? Icons.bolt_outlined : Icons.check_circle,
          size: 18,
          color: isPending ? scheme.onSurface.withValues(alpha: 0.5) : scheme.primary,
        ),
        const SizedBox(width: 10),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text('Routed to ',
                      style: TextStyle(color: scheme.onSurface.withValues(alpha: 0.6))),
                  Text('${p['domain'] ?? '?'}',
                      style: TextStyle(
                          color: scheme.primary, fontWeight: FontWeight.w600)),
                  if (!isPending) ...[
                    const SizedBox(width: 8),
                    Text('${(confidence * 100).round()}%',
                        style: TextStyle(
                            color: scheme.onSurface.withValues(alpha: 0.5),
                            fontSize: 12)),
                  ],
                ],
              ),
              if (p['reason'] != null)
                Text('${p['reason']}',
                    style: TextStyle(
                        color: scheme.onSurface.withValues(alpha: 0.5),
                        fontSize: 12)),
            ],
          ),
        ),
      ],
    ),
  );
}

Widget _synapseTimeline(
    BuildContext ctx, Map<String, dynamic> p, List<Widget> children) {
  final scheme = Theme.of(ctx).colorScheme;
  final entries = (p['entries'] as List?) ?? const [];
  return Container(
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: scheme.surface.withValues(alpha: 0.4),
      borderRadius: BorderRadius.circular(10),
      border: Border.all(color: scheme.outlineVariant),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Synapses',
            style: TextStyle(
                color: scheme.onSurface.withValues(alpha: 0.7),
                fontWeight: FontWeight.w600,
                fontSize: 13)),
        const SizedBox(height: 8),
        if (entries.isEmpty && children.isEmpty)
          Text('(none yet)',
              style: TextStyle(
                  color: scheme.onSurface.withValues(alpha: 0.4), fontSize: 12)),
        for (final e in entries)
          Padding(
            padding: const EdgeInsets.only(bottom: 4),
            child: Text('• ${e['kind'] ?? ''}: ${e['summary'] ?? ''}',
                style: TextStyle(
                    color: scheme.onSurface.withValues(alpha: 0.8), fontSize: 12)),
          ),
        ...children,
      ],
    ),
  );
}

Widget _toolCallTrace(BuildContext ctx, Map<String, dynamic> p, List<Widget> _) {
  final scheme = Theme.of(ctx).colorScheme;
  final status = p['status'] as String? ?? 'pending';
  final running = status == 'running';
  final ok = status == 'complete';
  final err = status == 'error';
  Color c;
  IconData icon;
  if (err) {
    c = const Color(0xFFEF5350);
    icon = Icons.error_outline;
  } else if (ok) {
    c = const Color(0xFF66BB6A);
    icon = Icons.check;
  } else {
    c = scheme.onSurface.withValues(alpha: 0.5);
    icon = Icons.hourglass_top;
  }
  return Container(
    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
    decoration: BoxDecoration(
      color: scheme.surface.withValues(alpha: 0.6),
      borderRadius: BorderRadius.circular(8),
    ),
    child: Row(
      children: [
        if (running)
          SizedBox(
            width: 14,
            height: 14,
            child: CircularProgressIndicator(strokeWidth: 2, color: c),
          )
        else
          Icon(icon, size: 16, color: c),
        const SizedBox(width: 10),
        Expanded(
          child: Text(
            '${p['tool'] ?? '?'}',
            style: TextStyle(
                fontFamily: 'monospace',
                color: scheme.onSurface,
                fontSize: 13),
          ),
        ),
        if (p['duration_ms'] != null)
          Text('${p['duration_ms']}ms',
              style: TextStyle(
                  color: scheme.onSurface.withValues(alpha: 0.5), fontSize: 12)),
      ],
    ),
  );
}

Widget _suggestion(BuildContext ctx, Map<String, dynamic> p, List<Widget> _) {
  final scheme = Theme.of(ctx).colorScheme;
  return InkWell(
    borderRadius: BorderRadius.circular(20),
    onTap: () => _toast(ctx, 'Suggestion accepted: ${p['text']}'),
    child: Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: scheme.primary.withValues(alpha: 0.4)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.auto_awesome, size: 14, color: scheme.primary),
          const SizedBox(width: 8),
          Text('${p['text'] ?? ''}',
              style: TextStyle(color: scheme.primary, fontSize: 13)),
        ],
      ),
    ),
  );
}

Widget _confirmation(BuildContext ctx, Map<String, dynamic> p, List<Widget> _) {
  final scheme = Theme.of(ctx).colorScheme;
  final options = (p['options'] as List?) ?? const [];
  return Container(
    padding: const EdgeInsets.all(14),
    decoration: BoxDecoration(
      color: scheme.surface,
      borderRadius: BorderRadius.circular(10),
      border: Border.all(color: scheme.primary.withValues(alpha: 0.5)),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('${p['prompt'] ?? ''}',
            style: TextStyle(color: scheme.onSurface, fontSize: 14)),
        const SizedBox(height: 10),
        Wrap(
          spacing: 8,
          children: [
            for (final o in options)
              FilledButton(
                onPressed: () => _toast(ctx, 'Confirmed: ${o['value']}'),
                style: FilledButton.styleFrom(
                  visualDensity: VisualDensity.compact,
                  padding: const EdgeInsets.symmetric(horizontal: 14),
                ),
                child: Text('${o['label']}'),
              ),
          ],
        ),
      ],
    ),
  );
}

Widget _metricStrip(BuildContext ctx, Map<String, dynamic> p, List<Widget> _) {
  final scheme = Theme.of(ctx).colorScheme;
  final entries = (p['entries'] as List?) ?? const [];
  return Container(
    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
    decoration: BoxDecoration(
      color: scheme.surface,
      borderRadius: BorderRadius.circular(10),
    ),
    child: Row(
      children: [
        for (final e in entries) ...[
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('${e['label'] ?? ''}',
                    style: TextStyle(
                        color: scheme.onSurface.withValues(alpha: 0.55),
                        fontSize: 11)),
                const SizedBox(height: 2),
                Row(
                  children: [
                    Text('${e['value'] ?? ''}',
                        style: TextStyle(
                            color: scheme.onSurface,
                            fontSize: 18,
                            fontWeight: FontWeight.w600)),
                    if (e['unit'] != null)
                      Padding(
                        padding: const EdgeInsets.only(left: 2, top: 4),
                        child: Text('${e['unit']}',
                            style: TextStyle(
                                color: scheme.onSurface.withValues(alpha: 0.5),
                                fontSize: 11)),
                      ),
                    if (e['trend'] == 'up')
                      const Icon(Icons.trending_up,
                          size: 14, color: Color(0xFF66BB6A)),
                    if (e['trend'] == 'down')
                      const Icon(Icons.trending_down,
                          size: 14, color: Color(0xFFEF5350)),
                  ],
                ),
              ],
            ),
          ),
        ],
      ],
    ),
  );
}

Widget _progressNote(BuildContext ctx, Map<String, dynamic> p, List<Widget> _) {
  final scheme = Theme.of(ctx).colorScheme;
  return Row(
    children: [
      const SizedBox(
        width: 14,
        height: 14,
        child: CircularProgressIndicator(strokeWidth: 2),
      ),
      const SizedBox(width: 10),
      Text('${p['text'] ?? ''}',
          style: TextStyle(
              color: scheme.onSurface.withValues(alpha: 0.7), fontSize: 13)),
    ],
  );
}

Widget _markdown(BuildContext ctx, Map<String, dynamic> p, List<Widget> _) {
  // Real impl would use flutter_markdown; prototype renders as plain text.
  return SelectableText(
    '${p['body'] ?? ''}',
    style: TextStyle(color: Theme.of(ctx).colorScheme.onSurface, fontSize: 14, height: 1.4),
  );
}

Widget _codeBlock(BuildContext ctx, Map<String, dynamic> p, List<Widget> _) {
  final scheme = Theme.of(ctx).colorScheme;
  final body = '${p['body'] ?? ''}';
  final copyable = (p['copyable'] as bool?) ?? true;
  return Container(
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: const Color(0xFF1A1A1A),
      borderRadius: BorderRadius.circular(8),
      border: Border.all(color: scheme.outlineVariant),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Text('${p['language'] ?? ''}',
                style: TextStyle(
                    color: scheme.onSurface.withValues(alpha: 0.5),
                    fontSize: 11,
                    fontFamily: 'monospace')),
            const Spacer(),
            if (copyable)
              InkWell(
                onTap: () async {
                  await Clipboard.setData(ClipboardData(text: body));
                  if (ctx.mounted) _toast(ctx, 'Copied');
                },
                child: Padding(
                  padding: const EdgeInsets.all(4),
                  child: Icon(Icons.content_copy_outlined,
                      size: 14, color: scheme.onSurface.withValues(alpha: 0.6)),
                ),
              ),
          ],
        ),
        const SizedBox(height: 6),
        SelectableText(
          body,
          style: TextStyle(
              fontFamily: 'monospace',
              color: scheme.onSurface,
              fontSize: 12,
              height: 1.4),
        ),
      ],
    ),
  );
}

Widget _keyValueList(BuildContext ctx, Map<String, dynamic> p, List<Widget> _) {
  final scheme = Theme.of(ctx).colorScheme;
  final entries = (p['entries'] as List?) ?? const [];
  return Container(
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: scheme.surface,
      borderRadius: BorderRadius.circular(10),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final e in entries)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 3),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                SizedBox(
                  width: 110,
                  child: Text('${e['key'] ?? ''}',
                      style: TextStyle(
                          color: scheme.onSurface.withValues(alpha: 0.55),
                          fontSize: 12)),
                ),
                Expanded(
                  child: Text('${e['value'] ?? ''}',
                      style: TextStyle(color: scheme.onSurface, fontSize: 13)),
                ),
              ],
            ),
          ),
      ],
    ),
  );
}

Widget _embed(BuildContext ctx, Map<String, dynamic> p, List<Widget> _) {
  final scheme = Theme.of(ctx).colorScheme;
  return Container(
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: scheme.surface,
      borderRadius: BorderRadius.circular(10),
      border: Border.all(color: scheme.outlineVariant, style: BorderStyle.solid),
    ),
    child: Row(
      children: [
        Icon(Icons.open_in_new, size: 16, color: scheme.onSurface.withValues(alpha: 0.6)),
        const SizedBox(width: 8),
        Expanded(
          child: Text('${p['kind'] ?? 'embed'}: ${p['url'] ?? ''}',
              style: TextStyle(
                  fontFamily: 'monospace',
                  color: scheme.onSurface.withValues(alpha: 0.8),
                  fontSize: 12)),
        ),
      ],
    ),
  );
}

Widget _group(BuildContext ctx, Map<String, dynamic> p, List<Widget> children) {
  final title = p['title'] as String?;
  if (title == null) return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: children);
  return Padding(
    padding: const EdgeInsets.symmetric(vertical: 4),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.only(bottom: 4),
          child: Text(title,
              style: TextStyle(
                  color: Theme.of(ctx).colorScheme.onSurface.withValues(alpha: 0.7),
                  fontWeight: FontWeight.w600,
                  fontSize: 12)),
        ),
        ...children,
      ],
    ),
  );
}

class _UnknownWidget extends StatelessWidget {
  const _UnknownWidget({required this.type});
  final String type;
  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(8),
      decoration: BoxDecoration(
        color: Colors.red.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(6),
      ),
      child: Text('unknown widget: $type',
          style: const TextStyle(color: Colors.redAccent, fontSize: 12)),
    );
  }
}

void _toast(BuildContext ctx, String text) {
  ScaffoldMessenger.of(ctx).showSnackBar(
    SnackBar(
      content: Text(text),
      duration: const Duration(milliseconds: 1200),
      behavior: SnackBarBehavior.floating,
    ),
  );
}

// ---------------------------------------------------------------------------
// V2 tree + patch model. Mirrors UiPatch in docs/rfw-v2-design.md.
// ---------------------------------------------------------------------------

enum _PatchOp { append, update, replace, remove }

class _UiPatch {
  const _UiPatch({
    required this.op,
    required this.nodeId,
    this.parentNodeId = '',
    this.widgetType = '',
    this.props = const {},
  });

  final _PatchOp op;
  final String nodeId;
  final String parentNodeId;
  final String widgetType;
  final Map<String, dynamic> props;
  // index field omitted in this prototype — see docs/rfw-v2-design.md wire
  // format. The Flutter side always appends to end-of-children for now.
}

class _LoggedPatch {
  const _LoggedPatch(this.atMs, this.patch);
  final int atMs;
  final _UiPatch patch;
}

class _V2Node {
  _V2Node({required this.id, required this.widgetType, required this.props});
  final String id;
  String widgetType;
  Map<String, dynamic> props;
  final List<_V2Node> children = [];
}

class _V2Tree extends ChangeNotifier {
  final _V2Node root =
      _V2Node(id: '__root__', widgetType: 'Group', props: const {});
  final Map<String, _V2Node> _byId = {};

  void apply(_UiPatch p) {
    switch (p.op) {
      case _PatchOp.append:
        final parent = p.parentNodeId.isEmpty ? root : _byId[p.parentNodeId];
        if (parent == null) return;
        if (_byId.containsKey(p.nodeId)) return; // idempotent
        final node = _V2Node(
          id: p.nodeId,
          widgetType: p.widgetType,
          props: Map.of(p.props),
        );
        _byId[p.nodeId] = node;
        parent.children.add(node);
      case _PatchOp.update:
        final node = _byId[p.nodeId];
        if (node == null) return;
        node.props = {...node.props, ...p.props};
      case _PatchOp.replace:
        final node = _byId[p.nodeId];
        if (node == null) return;
        if (p.widgetType.isNotEmpty) node.widgetType = p.widgetType;
        node.props = Map.of(p.props);
      case _PatchOp.remove:
        final node = _byId.remove(p.nodeId);
        if (node == null) return;
        _detach(root, node.id);
    }
    notifyListeners();
  }

  bool _detach(_V2Node parent, String id) {
    final i = parent.children.indexWhere((c) => c.id == id);
    if (i >= 0) {
      parent.children.removeAt(i);
      return true;
    }
    for (final c in parent.children) {
      if (_detach(c, id)) return true;
    }
    return false;
  }

  void clear() {
    _byId.clear();
    root.children.clear();
    notifyListeners();
  }
}

// ---------------------------------------------------------------------------
// Demo script — simulates one assistant turn for "plan a week-long trip to
// Bali for two". Patch timings are wall-clock milliseconds from Replay.
// ---------------------------------------------------------------------------

class _ScriptedPatch {
  const _ScriptedPatch(this.atMs, this.patch);
  final int atMs;
  final _UiPatch patch;
}

final _demoScript = <_ScriptedPatch>[
  _ScriptedPatch(0,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'progress',
        widgetType: 'ProgressNote',
        props: {'text': 'Looking at your request…'},
      )),
  _ScriptedPatch(280,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'routing',
        widgetType: 'RoutingDecision',
        props: {
          'domain': 'travel',
          'confidence': 0,
          'reason': 'matched "plan a week-long trip"',
          'status': 'pending',
        },
      )),
  _ScriptedPatch(520,
      _UiPatch(
        op: _PatchOp.update,
        nodeId: 'routing',
        props: {'confidence': 0.87, 'status': 'complete'},
      )),
  _ScriptedPatch(580,
      _UiPatch(op: _PatchOp.remove, nodeId: 'progress')),
  _ScriptedPatch(640,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'tools-group',
        widgetType: 'Group',
        props: {'title': 'Tool calls'},
      )),
  _ScriptedPatch(720,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'tool-flights',
        parentNodeId: 'tools-group',
        widgetType: 'ToolCallTrace',
        props: {
          'tool': 'tripradar.search_flights(AMS→DPS, May 1–8)',
          'status': 'running',
        },
      )),
  _ScriptedPatch(900,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'tool-hotels',
        parentNodeId: 'tools-group',
        widgetType: 'ToolCallTrace',
        props: {
          'tool': 'tripradar.search_hotels(Ubud, 7 nights, 2 guests)',
          'status': 'running',
        },
      )),
  _ScriptedPatch(1450,
      _UiPatch(
        op: _PatchOp.update,
        nodeId: 'tool-flights',
        props: {'status': 'complete', 'duration_ms': 730},
      )),
  _ScriptedPatch(1820,
      _UiPatch(
        op: _PatchOp.update,
        nodeId: 'tool-hotels',
        props: {'status': 'complete', 'duration_ms': 1100},
      )),
  _ScriptedPatch(1900,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'metrics',
        widgetType: 'MetricStrip',
        props: {
          'entries': [
            {'label': 'Flights', 'value': '12', 'trend': 'up'},
            {'label': 'Hotels', 'value': '28'},
            {'label': 'Avg / night', 'value': '184', 'unit': '€'},
          ],
        },
      )),
  _ScriptedPatch(2100,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'kv',
        widgetType: 'KeyValueList',
        props: {
          'entries': [
            {'key': 'Best flight', 'value': 'KL833 09:15 AMS → DPS, 1 stop SIN'},
            {'key': 'Hotel', 'value': 'Alaya Resort Ubud, garden suite'},
            {'key': 'Trip total', 'value': '€1,420 for two'},
          ],
        },
      )),
  _ScriptedPatch(2350,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'md',
        widgetType: 'Markdown',
        props: {
          'body':
              'Strong match. Booking the morning flight gets you in by sunset. '
                  'Ubud over Seminyak because you mentioned "quiet" last week.',
        },
      )),
  _ScriptedPatch(2600,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'code',
        widgetType: 'CodeBlock',
        props: {
          'language': 'json',
          'body':
              '{\n  "flight": "KL833",\n  "hotel": "alaya-ubud",\n  "dates": ["2026-05-01", "2026-05-08"]\n}',
          'copyable': true,
        },
      )),
  _ScriptedPatch(2850,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'sug-group',
        widgetType: 'Group',
        props: {'title': 'Next steps'},
      )),
  _ScriptedPatch(2900,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'sug1',
        parentNodeId: 'sug-group',
        widgetType: 'Suggestion',
        props: {
          'text': 'Book the 09:15 flight',
          'action': {'kind': 'book', 'payload': {'id': 'KL833'}},
        },
      )),
  _ScriptedPatch(2980,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'sug2',
        parentNodeId: 'sug-group',
        widgetType: 'Suggestion',
        props: {'text': 'Show me cheaper options'},
      )),
  _ScriptedPatch(3060,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'sug3',
        parentNodeId: 'sug-group',
        widgetType: 'Suggestion',
        props: {'text': 'Try Seminyak instead'},
      )),
  _ScriptedPatch(3300,
      _UiPatch(
        op: _PatchOp.append,
        nodeId: 'confirm',
        widgetType: 'Confirmation',
        props: {
          'prompt': 'Lock in the trip?',
          'options': [
            {'label': 'Confirm', 'value': 'yes'},
            {'label': 'Wait, hold off', 'value': 'no'},
          ],
        },
      )),
];
