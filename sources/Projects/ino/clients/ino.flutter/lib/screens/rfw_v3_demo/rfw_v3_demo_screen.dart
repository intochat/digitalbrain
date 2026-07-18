import 'dart:async';

import 'package:clock/clock.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:rfw/formats.dart' show parseLibraryFile;
import 'package:rfw/rfw.dart';

import '../../ui/ino_runtime.dart';
import '../../ui/rive/demo_rive_design_registry.dart';

// ---------------------------------------------------------------------------
// Public screen class
// ---------------------------------------------------------------------------

/// v3 Composer prototype — skeleton-then-data streaming pattern.
///
/// Mounts a single RemoteWidget with a DynamicContent that is mutated
/// incrementally as timed delta frames land. Each mutation calls
/// DynamicContent.update with the changed top-level key; rfw propagates
/// the change reactively to bound RiveArtboard bindings without
/// re-parsing the skeleton.
class RfwV3DemoScreen extends StatefulWidget {
  const RfwV3DemoScreen({super.key});

  @override
  State<RfwV3DemoScreen> createState() => _RfwV3DemoScreenState();
}

class _RfwV3DemoScreenState extends State<RfwV3DemoScreen> {
  final _registry = DemoRiveDesignRegistry();
  final _log = <_LoggedFrame>[];

  Timer? _ticker;
  Stopwatch? _clock;
  int _cursor = 0;
  double _speed = 1.0;
  int _selectedScenario = 0;
  bool _bannerDismissed = false;
  bool _isComplete = false;
  bool _hasStarted = false;

  // Single Runtime + DynamicContent pair — recreated on each Replay so the
  // rfw skeleton is freshly parsed, but NOT recreated during delta application.
  InoRuntime? _ino;
  DynamicContent? _data;

  // Shadow copy of DynamicContent state — DynamicContent has no read API,
  // so we keep this in sync with every update call to support nested writes.
  final _shadowState = <String, Object?>{};

  static const _composedLib = LibraryName(<String>['ino', 'composed']);

  @override
  void dispose() {
    _ticker?.cancel();
    super.dispose();
  }

  _DemoScenario get _scenario => _scenarios[_selectedScenario];

  void _selectScenario(int index) {
    setState(() => _selectedScenario = index);
    _replay();
  }

  void _replay() {
    _ticker?.cancel();
    final scenario = _scenario;

    final ino = createInoRuntime(riveRegistry: _registry);
    final data = DynamicContent();
    _seedContent(data, scenario.seedData);
    ino.runtime.update(_composedLib, parseLibraryFile(scenario.rfwSkeleton));

    setState(() {
      _ino = ino;
      _data = data;
      _log
        ..clear()
        ..add(const _LoggedFrame.skeleton(0));
      _cursor = 0;
      _isComplete = false;
      _hasStarted = true;
    });

    _clock = clock.stopwatch()..start();
    _ticker = Timer.periodic(const Duration(milliseconds: 30), (_) {
      final elapsedMs =
          (_clock!.elapsedMilliseconds * _speed).round();
      var advanced = false;
      final frames = scenario.frames;
      while (_cursor < frames.length &&
          frames[_cursor].atMs <= elapsedMs) {
        _applyFrame(frames[_cursor]);
        _cursor++;
        advanced = true;
      }
      if (advanced) setState(() {});
      if (_cursor >= frames.length) {
        _ticker?.cancel();
        _ticker = null;
      }
    });
  }

  void _reset() {
    _ticker?.cancel();
    setState(() {
      _ino = null;
      _data = null;
      _log.clear();
      _shadowState.clear();
      _cursor = 0;
      _isComplete = false;
      _hasStarted = false;
    });
  }

  void _seedContent(DynamicContent data, Map<String, Object?> seed) {
    _shadowState.clear();
    for (final e in seed.entries) {
      if (e.value != null) {
        _shadowState[e.key] = e.value;
        data.update(e.key, e.value!);
      }
    }
  }

  void _applyFrame(_DemoFrame frame) {
    final data = _data;
    if (data == null) return;
    switch (frame) {
      case _DeltaFrame(:final atMs, :final mutations):
        final summary = StringBuffer();
        for (final m in mutations) {
          _applyMutation(data, m);
          if (summary.isNotEmpty) summary.write(', ');
          summary.write(_describeMutation(m));
        }
        _log.add(_LoggedFrame.delta(atMs, summary.toString()));
      case _CompleteFrame(:final atMs):
        setState(() => _isComplete = true);
        _log.add(_LoggedFrame.complete(atMs));
    }
  }

  void _applyMutation(DynamicContent data, _DataMutation m) {
    switch (m) {
      case _UpdatePath(:final path, :final value):
        _doUpdatePath(data, path, value);
      case _ReplacePath(:final path, :final value):
        _doReplacePath(data, path, value);
      case _FireTrigger(:final widget, :final trigger):
        _doFireTrigger(widget, trigger);
    }
  }

  // Nested update — e.g. 'hero.title' → read shadow 'hero' map, patch 'title',
  // write back. Every path in the three .feature files is at most 2 segments.
  void _doUpdatePath(DynamicContent data, String path, Object value) {
    final parts = path.split('.');
    if (parts.length == 1) {
      _shadowState[parts[0]] = value;
      data.update(parts[0], value);
      return;
    }
    final topKey = parts[0];
    final subKey = parts[1];
    final current = _shadowState[topKey];
    final Map<String, Object?> updated = current is Map<String, Object?>
        ? (Map<String, Object?>.of(current)..[subKey] = value)
        : {subKey: value};
    _shadowState[topKey] = updated;
    data.update(topKey, updated);
  }

  // List-element replace — e.g. 'tiles.0' → read shadow list, replace index,
  // write back.
  void _doReplacePath(DynamicContent data, String path, Object value) {
    final parts = path.split('.');
    if (parts.length == 2) {
      final listKey = parts[0];
      final idx = int.tryParse(parts[1]);
      if (idx != null) {
        final current = _shadowState[listKey];
        final updated =
            current is List ? List<Object?>.of(current) : <Object?>[];
        while (updated.length <= idx) {
          updated.add(null);
        }
        updated[idx] = value;
        _shadowState[listKey] = updated;
        data.update(listKey, updated);
        return;
      }
    }
    // Fallback: top-level replace
    if (parts.length == 1) {
      _shadowState[parts[0]] = value;
      data.update(parts[0], value);
    }
  }

  void _doFireTrigger(String widgetName, String triggerName) {
    _registry.fireTrigger(widgetName, triggerName);
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      backgroundColor: Colors.black,
      body: Column(
        children: [
          _buildAppBar(scheme),
          if (!_bannerDismissed) _InfoBanner(onDismiss: () {
            setState(() => _bannerDismissed = true);
          }),
          _ScenarioChipRow(
            scenarios: _scenarios,
            selected: _selectedScenario,
            onSelect: _selectScenario,
          ),
          Expanded(
            child: LayoutBuilder(
              builder: (context, constraints) {
                final wide = constraints.maxWidth >= 900;
                if (wide) {
                  return Row(
                    children: [
                      Expanded(flex: 3, child: _buildRenderPane(context)),
                      Container(width: 1, color: scheme.outlineVariant),
                      Expanded(
                          flex: 2, child: _FrameLogPane(log: _log)),
                    ],
                  );
                }
                return Column(
                  children: [
                    Expanded(
                        flex: 3, child: _buildRenderPane(context)),
                    Container(height: 1, color: scheme.outlineVariant),
                    Expanded(
                        flex: 2, child: _FrameLogPane(log: _log)),
                  ],
                );
              },
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildAppBar(ColorScheme scheme) {
    return AppBar(
      backgroundColor: Colors.black,
      leading: Builder(
        builder: (context) => IconButton(
          icon: const Icon(Icons.arrow_back),
          onPressed: () => context.go('/brain'),
        ),
      ),
      title: const Text('v3 Composer prototype'),
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
    );
  }

  Widget _buildRenderPane(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    if (!_hasStarted) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Text(
            'Pick a scenario above and press Replay to stream a v3 Composer turn. '
            'Each delta updates DynamicContent in place; the rfw RemoteWidget '
            'reflows reactively.',
            textAlign: TextAlign.center,
            style: TextStyle(
              color: scheme.onSurface.withValues(alpha: 0.5),
              height: 1.6,
            ),
          ),
        ),
      );
    }

    final ino = _ino;
    final data = _data;
    if (ino == null || data == null) return const SizedBox.shrink();

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        _ChatBubble(text: _scenario.userPrompt),
        const SizedBox(height: 12),
        _StreamStatusBadge(isComplete: _isComplete),
        const SizedBox(height: 12),
        // The single RemoteWidget — stays mounted across all deltas.
        RemoteWidget(
          runtime: ino.runtime,
          data: data,
          widget: const FullyQualifiedWidgetName(_composedLib, 'root'),
          onEvent: (name, args) {},
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Info banner
// ---------------------------------------------------------------------------

class _InfoBanner extends StatelessWidget {
  const _InfoBanner({required this.onDismiss});
  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) {
    return Container(
      color: const Color(0xFF1A1A2E),
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Row(
        children: [
          Icon(Icons.info_outline,
              size: 16,
              color: Colors.white.withValues(alpha: 0.6)),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              'Rive components mocked with Flutter equivalents — '
              'designer-authored .riv lights up the same widget tree when it lands.',
              style: TextStyle(
                fontSize: 12,
                color: Colors.white.withValues(alpha: 0.7),
              ),
            ),
          ),
          IconButton(
            icon: const Icon(Icons.close, size: 16),
            onPressed: onDismiss,
            padding: EdgeInsets.zero,
            constraints: const BoxConstraints(),
          ),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Scenario chips row
// ---------------------------------------------------------------------------

class _ScenarioChipRow extends StatelessWidget {
  const _ScenarioChipRow({
    required this.scenarios,
    required this.selected,
    required this.onSelect,
  });

  final List<_DemoScenario> scenarios;
  final int selected;
  final ValueChanged<int> onSelect;

  @override
  Widget build(BuildContext context) {
    return Container(
      color: const Color(0xFF0D0D1A),
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Row(
          children: [
            for (var i = 0; i < scenarios.length; i++) ...[
              if (i > 0) const SizedBox(width: 8),
              _ScenarioChip(
                scenario: scenarios[i],
                isSelected: i == selected,
                onTap: () => onSelect(i),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _ScenarioChip extends StatelessWidget {
  const _ScenarioChip({
    required this.scenario,
    required this.isSelected,
    required this.onTap,
  });

  final _DemoScenario scenario;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return GestureDetector(
      onTap: onTap,
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 180),
        padding:
            const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        decoration: BoxDecoration(
          color: isSelected
              ? scheme.primary.withValues(alpha: 0.22)
              : Colors.white.withValues(alpha: 0.06),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(
            color: isSelected
                ? scheme.primary.withValues(alpha: 0.7)
                : Colors.white.withValues(alpha: 0.12),
          ),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              scenario.icon,
              size: 16,
              color: isSelected
                  ? scheme.primary
                  : Colors.white.withValues(alpha: 0.6),
            ),
            const SizedBox(width: 8),
            Text(
              scenario.chipLabel,
              style: TextStyle(
                fontSize: 13,
                color: isSelected
                    ? scheme.primary
                    : Colors.white.withValues(alpha: 0.8),
                fontWeight: isSelected
                    ? FontWeight.w600
                    : FontWeight.w400,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Chat bubble
// ---------------------------------------------------------------------------

class _ChatBubble extends StatelessWidget {
  const _ChatBubble({required this.text});
  final String text;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerRight,
      child: Container(
        constraints: const BoxConstraints(maxWidth: 340),
        padding:
            const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        decoration: const BoxDecoration(
          color: Color(0xFF1565C0),
          borderRadius: BorderRadius.only(
            topLeft: Radius.circular(18),
            topRight: Radius.circular(18),
            bottomLeft: Radius.circular(18),
            bottomRight: Radius.circular(4),
          ),
        ),
        child: Text(
          text,
          style: const TextStyle(
            color: Colors.white,
            fontSize: 14,
            height: 1.4,
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Stream status badge
// ---------------------------------------------------------------------------

class _StreamStatusBadge extends StatelessWidget {
  const _StreamStatusBadge({required this.isComplete});
  final bool isComplete;

  @override
  Widget build(BuildContext context) {
    return AnimatedSwitcher(
      duration: const Duration(milliseconds: 300),
      child: isComplete
          ? _badge(
              key: const ValueKey('complete'),
              icon: Icons.check_circle,
              label: 'stream complete',
              color: const Color(0xFF4CAF50),
            )
          : _badge(
              key: const ValueKey('skeleton'),
              icon: Icons.view_stream,
              label: 'skeleton',
              color: const Color(0xFF42A5F5),
            ),
    );
  }

  Widget _badge({
    required Key key,
    required IconData icon,
    required String label,
    required Color color,
  }) {
    return Row(
      key: key,
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 14, color: color),
        const SizedBox(width: 6),
        Text(
          label,
          style: TextStyle(
            fontSize: 12,
            color: color,
            fontWeight: FontWeight.w600,
          ),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------------------
// Frame log pane
// ---------------------------------------------------------------------------

class _FrameLogPane extends StatelessWidget {
  const _FrameLogPane({required this.log});
  final List<_LoggedFrame> log;

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
                  'DynamicContent frame log',
                  style: TextStyle(
                    color: scheme.onSurface.withValues(alpha: 0.8),
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const Spacer(),
                Text(
                  '${log.length} frames',
                  style: TextStyle(
                    color: scheme.onSurface.withValues(alpha: 0.5),
                    fontSize: 12,
                  ),
                ),
              ],
            ),
          ),
          Expanded(
            child: ListView.builder(
              padding: const EdgeInsets.symmetric(horizontal: 12),
              itemCount: log.length,
              itemBuilder: (context, i) {
                final entry = log[log.length - 1 - i]; // newest at top
                return _FrameLogRow(entry: entry);
              },
            ),
          ),
        ],
      ),
    );
  }
}

class _FrameLogRow extends StatelessWidget {
  const _FrameLogRow({required this.entry});
  final _LoggedFrame entry;

  static const _typeColors = {
    _FrameType.skeleton: Color(0xFF42A5F5),
    _FrameType.delta: Color(0xFF66BB6A),
    _FrameType.complete: Color(0xFFFFB74D),
  };

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final typeColor = _typeColors[entry.type]!;

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
            padding:
                const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
            decoration: BoxDecoration(
              color: typeColor.withValues(alpha: 0.18),
              borderRadius: BorderRadius.circular(4),
            ),
            child: Text(
              entry.type.name.toUpperCase(),
              style: TextStyle(
                fontFamily: 'monospace',
                fontSize: 10,
                color: typeColor,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          Expanded(
            child: Text(
              entry.summary,
              style: TextStyle(
                fontFamily: 'monospace',
                fontSize: 12,
                color: scheme.onSurface.withValues(alpha: 0.7),
                height: 1.4,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// Frame log model
// ---------------------------------------------------------------------------

enum _FrameType { skeleton, delta, complete }

class _LoggedFrame {
  const _LoggedFrame.skeleton(this.atMs)
      : type = _FrameType.skeleton,
        summary = 'rfwtxt skeleton mounted';

  const _LoggedFrame.delta(this.atMs, this.summary)
      : type = _FrameType.delta;

  const _LoggedFrame.complete(this.atMs)
      : type = _FrameType.complete,
        summary = 'stream-complete';

  final int atMs;
  final _FrameType type;
  final String summary;
}

// ---------------------------------------------------------------------------
// Demo scenario + frame model
// ---------------------------------------------------------------------------

class _DemoScenario {
  const _DemoScenario({
    required this.id,
    required this.chipLabel,
    required this.userPrompt,
    required this.icon,
    required this.rfwSkeleton,
    required this.seedData,
    required this.frames,
  });

  final String id;
  final String chipLabel;
  final String userPrompt;
  final IconData icon;
  final String rfwSkeleton;
  final Map<String, Object?> seedData;
  final List<_DemoFrame> frames;
}

sealed class _DemoFrame {
  int get atMs;
}

class _DeltaFrame implements _DemoFrame {
  const _DeltaFrame({required this.atMs, required this.mutations});

  @override
  final int atMs;
  final List<_DataMutation> mutations;
}

class _CompleteFrame implements _DemoFrame {
  const _CompleteFrame({required this.atMs});

  @override
  final int atMs;
}

sealed class _DataMutation {}

class _UpdatePath extends _DataMutation {
  _UpdatePath(this.path, this.value);
  final String path;
  final Object value;
}

class _ReplacePath extends _DataMutation {
  _ReplacePath(this.path, this.value);
  final String path;
  final Object value;
}

class _FireTrigger extends _DataMutation {
  _FireTrigger(this.widget, this.trigger);
  final String widget;
  final String trigger;
}

String _describeMutation(_DataMutation m) => switch (m) {
      _UpdatePath(:final path, :final value) => '$path = $value',
      _ReplacePath(:final path) => 'replace $path',
      _FireTrigger(:final widget, :final trigger) =>
        'trigger $widget.$trigger',
    };

// ---------------------------------------------------------------------------
// Scenario 1: Tokyo trip plan
// Source: ui-compose-tokyo-trip-plan.feature
// ---------------------------------------------------------------------------

const _skeletonTokyoTripPlan = r'''
import ino.rive;
import core.widgets;
widget root = Column(children: [
  PersonaInline(domain: "kernel", mood: data.persona.mood, energy: data.persona.energy, energyAnimDurMs: data.persona.energyAnim.durMs, energyAnimCurve: data.persona.energyAnim.curve),
  Hero(domain: "kernel", title: data.hero.title, subtitle: data.hero.subtitle, mood: data.hero.mood),
  Spacer(domain: "kernel", height: 24, motif: data.spacer.motif),
  Tile(domain: "kernel", kind: data.tiles.0.kind, line1: data.tiles.0.line1, line2: data.tiles.0.line2, line3: data.tiles.0.line3),
  Tile(domain: "kernel", kind: data.tiles.1.kind, line1: data.tiles.1.line1, line2: data.tiles.1.line2, line3: data.tiles.1.line3),
  Tile(domain: "kernel", kind: data.tiles.2.kind, line1: data.tiles.2.line1, line2: data.tiles.2.line2, line3: data.tiles.2.line3),
  Badge(domain: "kernel", label: data.badge.label, value0to1: data.badge.value0to1, value0to1AnimDurMs: data.badge.value0to1Anim.durMs, value0to1AnimCurve: data.badge.value0to1Anim.curve),
]);
''';

final _seedTokyoTripPlan = <String, Object?>{
  'persona': <String, Object?>{'mood': 'discovering', 'energy': 0.55},
  'hero': <String, Object?>{
    'title': 'Searching…',
    'subtitle': '',
    'mood': 'discovering',
  },
  'spacer': <String, Object?>{'motif': 'wave'},
  'tiles': <Object?>[
    <String, Object?>{'kind': 'flight', 'line1': '', 'line2': '', 'line3': ''},
    <String, Object?>{'kind': 'hotel', 'line1': '', 'line2': '', 'line3': ''},
    <String, Object?>{'kind': 'place', 'line1': '', 'line2': '', 'line3': ''},
  ],
  'badge': <String, Object?>{'label': 'Budget', 'value0to1': 0},
};

final _framesTokyoTripPlan = <_DemoFrame>[
  _DeltaFrame(
    atMs: 280,
    mutations: [_UpdatePath('hero.title', 'Tokyo, May 1–7')],
  ),
  _DeltaFrame(
    atMs: 480,
    mutations: [_UpdatePath('hero.subtitle', 'Cherry blossom finale week')],
  ),
  _DeltaFrame(
    atMs: 760,
    mutations: [
      _ReplacePath('tiles.0', <String, Object?>{
        'kind': 'flight',
        'line1': 'ANA NH106 09:50',
        'line2': '11h direct AMS→HND',
        'line3': '¥85,400',
      }),
    ],
  ),
  _DeltaFrame(
    atMs: 980,
    mutations: [
      _ReplacePath('tiles.1', <String, Object?>{
        'kind': 'hotel',
        'line1': 'Park Hyatt Shinjuku',
        'line2': '5★ • garden suite',
        'line3': '¥48,800/night',
      }),
    ],
  ),
  _DeltaFrame(
    atMs: 1180,
    mutations: [
      _ReplacePath('tiles.2', <String, Object?>{
        'kind': 'place',
        'line1': 'Shibuya Sky',
        'line2': 'sunset viewing 18:30',
        'line3': '2h',
      }),
    ],
  ),
  _DeltaFrame(
    atMs: 1480,
    mutations: [
      _UpdatePath('badge.value0to1Anim', <String, Object?>{
        'durMs': 500,
        'curve': 'easeOutCubic',
      }),
      _UpdatePath('badge.value0to1', 0.62),
    ],
  ),
  _DeltaFrame(
    atMs: 1700,
    mutations: [
      _UpdatePath('persona.energyAnim', <String, Object?>{
        'durMs': 350,
        'curve': 'easeOut',
      }),
      _UpdatePath('persona.energy', 0.85),
      _UpdatePath('persona.mood', 'happy'),
    ],
  ),
  const _CompleteFrame(atMs: 1700),
];

// ---------------------------------------------------------------------------
// Scenario 2: Daily persona check-in
// Source: ui-compose-persona-checkin.feature
// ---------------------------------------------------------------------------

const _skeletonPersonaCheckin = r'''
import ino.rive;
import core.widgets;
widget root = Column(children: [
  PersonaInline(domain: "kernel", mood: data.persona.mood, energy: data.persona.energy, energyAnimDurMs: data.persona.energyAnim.durMs, energyAnimCurve: data.persona.energyAnim.curve),
  Hero(domain: "kernel", title: data.hero.title, subtitle: data.hero.subtitle, mood: data.hero.mood),
  Spacer(domain: "kernel", height: 16, motif: data.spacer.motif),
  Tile(domain: "kernel", kind: data.tiles.0.kind, line1: data.tiles.0.line1, line3: data.tiles.0.line3),
  Tile(domain: "kernel", kind: data.tiles.1.kind, line1: data.tiles.1.line1, line3: data.tiles.1.line3),
  Tile(domain: "kernel", kind: data.tiles.2.kind, line1: data.tiles.2.line1, line3: data.tiles.2.line3),
  Badge(domain: "kernel", label: data.badge.label, value0to1: data.badge.value0to1, value0to1AnimDurMs: data.badge.value0to1Anim.durMs, value0to1AnimCurve: data.badge.value0to1Anim.curve),
]);
''';

final _seedPersonaCheckin = <String, Object?>{
  'persona': <String, Object?>{'mood': 'centered', 'energy': 0.5},
  'hero': <String, Object?>{
    'title': "Today's pulse",
    'subtitle': '',
    'mood': 'centered',
  },
  'spacer': <String, Object?>{'motif': 'wave'},
  'tiles': <Object?>[
    <String, Object?>{'kind': 'task', 'line1': '', 'line3': ''},
    <String, Object?>{'kind': 'task', 'line1': '', 'line3': ''},
    <String, Object?>{'kind': 'task', 'line1': '', 'line3': ''},
  ],
  'badge': <String, Object?>{'label': 'Streak', 'value0to1': 0},
};

final _framesPersonaCheckin = <_DemoFrame>[
  _DeltaFrame(
    atMs: 220,
    mutations: [
      _UpdatePath('hero.subtitle', 'You shipped 3 things and slept 7h'),
    ],
  ),
  _DeltaFrame(
    atMs: 420,
    mutations: [
      _UpdatePath('persona.energyAnim', <String, Object?>{
        'durMs': 400,
        'curve': 'easeOut',
      }),
      _UpdatePath('persona.energy', 0.78),
    ],
  ),
  _DeltaFrame(
    atMs: 700,
    mutations: [
      _ReplacePath('tiles.0', <String, Object?>{
        'kind': 'task',
        'line1': 'Closed PR #142',
        'line3': '2h',
      }),
    ],
  ),
  _DeltaFrame(
    atMs: 900,
    mutations: [
      _ReplacePath('tiles.1', <String, Object?>{
        'kind': 'task',
        'line1': 'Cooked dinner',
        'line3': '35m',
      }),
    ],
  ),
  _DeltaFrame(
    atMs: 1100,
    mutations: [
      _ReplacePath('tiles.2', <String, Object?>{
        'kind': 'task',
        'line1': 'Walked 6km',
        'line3': '1h',
      }),
    ],
  ),
  _DeltaFrame(
    atMs: 1300,
    mutations: [
      _UpdatePath('badge.value0to1Anim', <String, Object?>{
        'durMs': 500,
        'curve': 'easeOutCubic',
      }),
      _UpdatePath('badge.value0to1', 0.71),
    ],
  ),
  _DeltaFrame(
    atMs: 1500,
    mutations: [_FireTrigger('persona-inline', 'pulse')],
  ),
  const _CompleteFrame(atMs: 1500),
];

// ---------------------------------------------------------------------------
// Scenario 3: Tokyo rainy-day pivot
// Source: ui-compose-tokyo-rain-pivot.feature
// ---------------------------------------------------------------------------

const _skeletonTokyoRainPivot = r'''
import ino.rive;
import core.widgets;
widget root = Column(children: [
  PersonaInline(domain: "kernel", mood: data.persona.mood, energy: data.persona.energy, energyAnimDurMs: data.persona.energyAnim.durMs, energyAnimCurve: data.persona.energyAnim.curve),
  Hero(domain: "kernel", title: data.hero.title, subtitle: data.hero.subtitle, mood: data.hero.mood),
  Spacer(domain: "kernel", height: data.spacer.height, motif: data.spacer.motif, heightAnimDurMs: data.spacer.heightAnim.durMs, heightAnimCurve: data.spacer.heightAnim.curve),
  Tile(domain: "kernel", kind: data.tiles.0.kind, line1: data.tiles.0.line1, line2: data.tiles.0.line2, line3: data.tiles.0.line3),
  Tile(domain: "kernel", kind: data.tiles.1.kind, line1: data.tiles.1.line1, line2: data.tiles.1.line2, line3: data.tiles.1.line3),
  Tile(domain: "kernel", kind: data.tiles.2.kind, line1: data.tiles.2.line1, line2: data.tiles.2.line2, line3: data.tiles.2.line3),
  Badge(domain: "kernel", label: data.badge.label, value0to1: data.badge.value0to1, value0to1AnimDurMs: data.badge.value0to1Anim.durMs, value0to1AnimCurve: data.badge.value0to1Anim.curve),
]);
''';

final _seedTokyoRainPivot = <String, Object?>{
  'persona': <String, Object?>{'mood': 'discovering', 'energy': 0.6},
  'hero': <String, Object?>{
    'title': 'Reshuffling…',
    'subtitle': '',
    'mood': 'rethinking',
  },
  'spacer': <String, Object?>{'motif': 'rain', 'height': 0},
  'tiles': <Object?>[
    <String, Object?>{'kind': 'place', 'line1': '', 'line2': '', 'line3': ''},
    <String, Object?>{'kind': 'place', 'line1': '', 'line2': '', 'line3': ''},
    <String, Object?>{'kind': 'place', 'line1': '', 'line2': '', 'line3': ''},
  ],
  'badge': <String, Object?>{'label': 'Indoor coverage', 'value0to1': 0},
};

final _framesTokyoRainPivot = <_DemoFrame>[
  _DeltaFrame(
    atMs: 120,
    mutations: [
      _UpdatePath('spacer.heightAnim', <String, Object?>{
        'durMs': 600,
        'curve': 'easeOutCubic',
      }),
      _UpdatePath('spacer.height', 48),
    ],
  ),
  _DeltaFrame(
    atMs: 250,
    mutations: [
      _UpdatePath(
          'hero.subtitle', 'Rain through Wednesday — switching to indoor pivots'),
    ],
  ),
  _DeltaFrame(
    atMs: 450,
    mutations: [
      _UpdatePath('persona.mood', 'thoughtful'),
      _UpdatePath('persona.energyAnim', <String, Object?>{
        'durMs': 200,
        'curve': 'easeOut',
      }),
      _UpdatePath('persona.energy', 0.62),
    ],
  ),
  _DeltaFrame(
    atMs: 700,
    mutations: [
      _ReplacePath('tiles.0', <String, Object?>{
        'kind': 'place',
        'line1': 'teamLab Borderless',
        'line2': 'indoor • ★4.9',
        'line3': '3h',
      }),
    ],
  ),
  _DeltaFrame(
    atMs: 900,
    mutations: [
      _ReplacePath('tiles.1', <String, Object?>{
        'kind': 'place',
        'line1': 'Edo-Tokyo Museum',
        'line2': 'indoor • ★4.5',
        'line3': '2h',
      }),
    ],
  ),
  _DeltaFrame(
    atMs: 1100,
    mutations: [
      _ReplacePath('tiles.2', <String, Object?>{
        'kind': 'place',
        'line1': 'Tsukiji Outer Market',
        'line2': 'covered • ★4.7',
        'line3': '1.5h',
      }),
    ],
  ),
  _DeltaFrame(
    atMs: 1300,
    mutations: [
      _UpdatePath('badge.value0to1Anim', <String, Object?>{
        'durMs': 600,
        'curve': 'easeOutCubic',
      }),
      _UpdatePath('badge.value0to1', 0.92),
    ],
  ),
  _DeltaFrame(
    atMs: 1500,
    mutations: [_UpdatePath('hero.title', 'Tokyo — Rain pivot')],
  ),
  const _CompleteFrame(atMs: 1500),
];

// ---------------------------------------------------------------------------
// Scenario list — order matches chip order on screen
// ---------------------------------------------------------------------------

final _scenarios = <_DemoScenario>[
  _DemoScenario(
    id: 'tokyo-trip-plan',
    chipLabel: 'Plan Tokyo trip',
    userPrompt: 'plan my Tokyo trip for May 1-7',
    icon: Icons.flight_takeoff,
    rfwSkeleton: _skeletonTokyoTripPlan,
    seedData: _seedTokyoTripPlan,
    frames: _framesTokyoTripPlan,
  ),
  _DemoScenario(
    id: 'persona-checkin',
    chipLabel: 'How am I today?',
    userPrompt: 'how am I today?',
    icon: Icons.self_improvement,
    rfwSkeleton: _skeletonPersonaCheckin,
    seedData: _seedPersonaCheckin,
    frames: _framesPersonaCheckin,
  ),
  _DemoScenario(
    id: 'tokyo-rain-pivot',
    chipLabel: 'Rain pivot',
    userPrompt: "it's raining — pivot to indoors",
    icon: Icons.umbrella,
    rfwSkeleton: _skeletonTokyoRainPivot,
    seedData: _seedTokyoRainPivot,
    frames: _framesTokyoRainPivot,
  ),
];
