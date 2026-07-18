import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../state/brain_inspector_bloc.dart';
import '../../state/persona_bloc.dart';
import 'cluster_label.dart';
import 'demo_runner.dart';
import 'shell_brain_canvas.dart';
import 'shell_brain_topology.dart';
import 'shell_compose.dart';
import 'shell_inspector_drawer.dart';
import 'shell_synapse_tooltip.dart';
import 'shell_theme.dart';
import 'shell_timeline.dart';
import 'shell_tokens_panel.dart';
import 'shell_topbar.dart';

class ShellScreen extends StatefulWidget {
  const ShellScreen({
    super.key,
    this.canvas,
    this.canvasKey,
    this.topbarPersonaBuilder,
    this.timelineBuilder,
    this.runnerEnabled = true,
  });

  /// Injected canvas widget — used by tests to supply a GL-free stub. When
  /// null the screen creates a real [ShellBrainCanvas] keyed by [_canvasKey].
  final Widget? canvas;

  /// External key forwarded to [ShellBrainCanvas]. When null the state creates
  /// its own key so [_buildClusterLabels] can reach [ShellBrainCanvasState].
  final GlobalKey<ShellBrainCanvasState>? canvasKey;

  /// Overrides the persona widget inside [ShellTopbar] — inject a stub in
  /// tests to avoid requiring [PersonaBloc] + [TimelineBloc] in the tree.
  final WidgetBuilder? topbarPersonaBuilder;

  /// Overrides the timeline footer — inject a stub in tests to avoid
  /// requiring a [TimelineBloc] ancestor for structural screen tests.
  final WidgetBuilder? timelineBuilder;

  /// Set to false in tests that don't provide [PersonaBloc] in the tree.
  /// When false the [DemoRunner] is not constructed and topbar/inspector
  /// callbacks are no-ops.
  final bool runnerEnabled;

  @override
  State<ShellScreen> createState() => _ShellScreenState();
}

class _ShellScreenState extends State<ShellScreen>
    with SingleTickerProviderStateMixin {
  late final GlobalKey<ShellBrainCanvasState> _canvasKey;
  final GlobalKey<ShellComposeState> composeKey = GlobalKey<ShellComposeState>();

  late final Ticker _ticker;

  DemoRunner? _runner;
  bool _runnerInitialized = false;

  bool _isTokensOpen = false;
  bool _autoFocus = true;

  @override
  void initState() {
    super.initState();
    _canvasKey = widget.canvasKey ?? GlobalKey<ShellBrainCanvasState>();
    _ticker = createTicker((_) {
      if (mounted) setState(() {});
    })..start();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_runnerInitialized || !widget.runnerEnabled) return;
    _runner = DemoRunner(
      refs: ShellRefs(
        canvasKey: _canvasKey,
        composeKey: composeKey,
        persona: context.read<PersonaBloc>(),
      ),
    );
    _runnerInitialized = true;
  }

  @override
  void dispose() {
    _runner?.stop();
    _ticker.dispose();
    super.dispose();
  }

  void _onPointerDown(PointerDownEvent event) {
    final canvas = _canvasKey.currentState;
    if (canvas == null) return;
    final box = context.findRenderObject() as RenderBox?;
    if (box == null) return;
    final local = box.globalToLocal(event.position);
    final pick = canvas.pickNode(local);

    final inspector = context.read<BrainInspectorBloc>();

    switch (pick) {
      case null:
        // Click outside any pickable — if a tooltip is open, dismiss + resume.
        if (inspector.state.pausedSynapse != null) {
          for (final s in canvas.activeSynapses) {
            if (s.paused) s.paused = false;
          }
          inspector.add(ResumeShellSynapse());
        }
      case SynapsePick(:final syn):
        inspector.add(PauseShellSynapse(
          info: PausedSynapseInfo(
            from: syn.from,
            to: syn.to,
            payload: Map<String, dynamic>.from(syn.payload),
            gold: syn.gold,
            screenX: event.position.dx,
            screenY: event.position.dy,
          ),
        ));
      case NeuronPick(:final alias):
        inspector.add(SelectNeuron(nodeId: alias));
    }
  }

  @override
  Widget build(BuildContext context) {
    // When a stub canvas is injected (tests), use it directly and skip the key
    // so the GL-free stub doesn't try to attach to ShellBrainCanvasState.
    final canvasWidget = widget.canvas != null
        ? widget.canvas!
        : Listener(
            onPointerDown: _onPointerDown,
            behavior: HitTestBehavior.translucent,
            child: ShellBrainCanvas(key: _canvasKey),
          );

    return Scaffold(
      backgroundColor: InoShellTheme.ink0,
      body: Stack(
        children: [
          Positioned.fill(child: canvasWidget),
          ..._buildClusterLabels(),
          // Compose canvas occupies the middle region — clear of the topbar
          // (~0-200 px) and the timeline bar (138 px from bottom, T7).
          Positioned(
            left: 0,
            right: 0,
            top: 200,
            bottom: 138,
            child: ShellCompose(
              key: composeKey,
              replayCallback: _runner == null
                  ? null
                  : (cardId) => _runner!.replayTrace(cardId),
            ),
          ),
          // Timeline footer — z-index ~15 (below topbar at ~20, drawers at ~30,
          // tooltip at ~40). timelineBuilder lets tests inject a stub to avoid
          // requiring a TimelineBloc ancestor for structural screen tests.
          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: widget.timelineBuilder != null
                ? widget.timelineBuilder!(context)
                : const ShellTimeline(),
          ),
          Positioned(
            top: 0,
            left: 0,
            right: 0,
            child: ShellTopbar(
              onTokens: () => setState(() => _isTokensOpen = !_isTokensOpen),
              onPlay: _runner == null ? null : () => _runner!.play(),
              onReplay: _runner == null
                  ? null
                  : () {
                      _runner!.stop();
                      _runner!.play();
                    },
              onPause: _runner == null ? null : () => _runner!.togglePause(),
              onReplan: _runner == null ? null : () => _runner!.replan(),
              personaBuilder: widget.topbarPersonaBuilder,
            ),
          ),
          ShellInspectorDrawer(
            onFireTest: _runner == null
                ? null
                : (alias) => _runner!.fireTest(alias),
          ),
          ShellTokensPanel(
            isOpen: _isTokensOpen,
            onClose: () => setState(() => _isTokensOpen = false),
            autoFocus: _autoFocus,
            onAutoFocusChanged: (v) {
              setState(() => _autoFocus = v);
              _canvasKey.currentState?.setAutoFocus(v);
            },
          ),
          BlocBuilder<BrainInspectorBloc, BrainInspectorState>(
            buildWhen: (a, b) => a.pausedSynapse != b.pausedSynapse,
            builder: (ctx, state) {
              final info = state.pausedSynapse;
              if (info == null) return const SizedBox.shrink();
              return ShellSynapseTooltip(info: info);
            },
          ),
        ],
      ),
    );
  }

  List<Widget> _buildClusterLabels() {
    // currentState is null when a stub canvas is injected (test path) or
    // before the GL scene has fully initialised — both are safe no-ops.
    final canvas = _canvasKey.currentState;
    if (canvas == null) return const [];

    final out = <Widget>[];
    for (final c in ShellTopology.clusters) {
      final rawLen = math.sqrt(
        c.position.x * c.position.x +
            c.position.y * c.position.y +
            c.position.z * c.position.z,
      );
      if (rawLen == 0) continue;

      // Place label 2.0 units from origin along the cluster's unit direction
      // (sphere R 1.55 + ~0.45 label-lift above the surface).
      final scale = 2.0 / rawLen;
      final result = canvas.projectVec3WithDepth(
        c.position.x * scale,
        c.position.y * scale,
        c.position.z * scale,
      );
      if (result == null) continue;

      // Matches brain.js line 366: opacity = max(0.2, 1 - ndcZ).
      final fadeOpacity = math.max(0.2, 1.0 - result.z);

      out.add(ClusterLabel(
        key: ValueKey('cluster-label-${c.id}'),
        label: c.label,
        count: c.aliases.length,
        position: result.offset,
        opacity: fadeOpacity,
      ));
    }
    return out;
  }
}
