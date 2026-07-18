import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:ino_flutter/grpc/generated/ino.pbgrpc.dart';
import 'package:ino_flutter/persona/persona_state.dart';
import 'package:ino_flutter/services/brain_stream_service.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';
import 'package:ino_flutter/state/ino_bloc.dart';
import 'package:ino_flutter/state/persona_bloc.dart';
import 'package:three_js/three_js.dart' as three;

import 'brain_inspector_drawer.dart';
import 'brain_picking.dart';
import 'brain_pulse_animator.dart';
import 'brain_topology.dart';

class BrainHomeScreen extends StatefulWidget {
  const BrainHomeScreen({super.key});

  @override
  State<BrainHomeScreen> createState() => _BrainHomeScreenState();
}

class _BrainHomeScreenState extends State<BrainHomeScreen> {
  late three.ThreeJS _threeJs;
  three.OrbitControls? _controls;
  final TextEditingController _input = TextEditingController();

  final Map<String, List<three.Mesh>> _neuronsByDomain = {};
  final List<three.Mesh> _synapseMeshes = [];

  BrainStreamService? _brainStream;
  BrainPicker? _picker;
  BrainPulseAnimator? _pulseAnimator;
  BrainTopology? _topology;

  StreamSubscription<BrainInspectorState>? _inspectorSub;
  final Set<String> _spawnedFireIds = {};

  @override
  void initState() {
    super.initState();
    _threeJs = three.ThreeJS(
      onSetupComplete: () => setState(() {}),
      setup: _setupScene,
      settings: three.Settings(antialias: false),
    );
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final stub = InoClient(context.read<InoBloc>().grpcClient.channel);
      _brainStream = BrainStreamService(stub, context.read<BrainInspectorBloc>());
      _brainStream!.start();

      final inspector = context.read<BrainInspectorBloc>();
      _inspectorSub = inspector.stream.listen(_onInspectorState);

      // Consume ?q= deep-link: auto-send the prompt so the C.3 chat entry still works.
      final uri = Uri.base;
      final q = uri.queryParameters['q'];
      if (q != null && q.isNotEmpty) {
        context.read<PersonaBloc>().add(PersonaEmotionChanged(PersonaEmotion.thinking));
        context.read<InoBloc>().add(SendMessage(q));
      }
    });
  }

  void _onInspectorState(BrainInspectorState state) {
    // Autorotate when nothing is selected.
    _controls?.autoRotate = state.selected == null;

    // Paused pulse forwarded to animator.
    _pulseAnimator?.setPaused(state.pausedPulse?.id);

    // Spawn any fires that arrived since the last state emission.
    for (final entry in state.recentByNodeId.entries) {
      if (entry.value.isEmpty) continue;
      final newest = entry.value.first;
      if (!_spawnedFireIds.contains(newest.id)) {
        _spawnedFireIds.add(newest.id);
        _pulseAnimator?.spawn(newest);
      }
    }
  }

  @override
  void dispose() {
    _inspectorSub?.cancel();
    _pulseAnimator?.dispose();
    unawaited(_brainStream?.stop() ?? Future.value());
    _input.dispose();
    _controls?.dispose();
    _threeJs.dispose();
    three.loading.clear();
    super.dispose();
  }

  Future<void> _setupScene() async {
    try {
      _threeJs.scene = three.Scene()
        ..background = three.Color.fromHex32(0x05060A);

      _threeJs.camera = three.PerspectiveCamera(
        45,
        _threeJs.width / _threeJs.height,
        0.1,
        200,
      );
      _threeJs.camera.position.setValues(0, 1.5, 11);

      _threeJs.scene.add(three.AmbientLight(0xFFFFFF, 0.45));
      final keyLight = three.PointLight(0xFFE9C7, 1.2, 30, 1.4)
        ..position.setValues(6, 8, 8);
      _threeJs.scene.add(keyLight);
      final rimLight = three.PointLight(0x7AA8FF, 0.7, 30, 1.6)
        ..position.setValues(-7, -3, -6);
      _threeJs.scene.add(rimLight);

      final topology = BrainTopology.load();
      _topology = topology;
      _addNodes(topology);
      _addEdges(topology);

      _controls = three.OrbitControls(_threeJs.camera, _threeJs.globalKey)
        ..target.setValues(0, 0.2, 0)
        ..enableDamping = true
        ..dampingFactor = 0.06
        ..enablePan = false
        ..minDistance = 5
        ..maxDistance = 22
        ..autoRotate = true
        ..autoRotateSpeed = 0.4;

      _picker = BrainPicker(_threeJs);
      _pulseAnimator = BrainPulseAnimator(_threeJs.scene, topology);

      _threeJs.addAnimationEvent(_animate);
    } catch (e, st) {
      // ignore: avoid_print
      print('[brain] setup failed: $e\n$st');
      rethrow;
    }
  }

  void _animate(double dt) {
    _pulseAnimator?.tick(dt);
    _controls?.update();
  }

  void _addNodes(BrainTopology topology) {
    for (final node in topology.nodes) {
      final mesh = _meshForNode(node);
      mesh.position.setValues(node.x, node.y, node.z);
      mesh.userData['nodeId'] = node.id;
      _threeJs.scene.add(mesh);
      if (node.kind == NodeKind.neuron) {
        _neuronsByDomain.putIfAbsent(node.domain, () => []).add(mesh);
      } else {
        _synapseMeshes.add(mesh);
      }
    }
  }

  three.Mesh _meshForNode(BrainNode node) {
    switch (node.kind) {
      case NodeKind.neuron:
        return three.Mesh(
          three.SphereGeometry(0.20, 24, 16),
          three.MeshStandardMaterial.fromMap({
            'color': domainColor(node.domain),
            'emissive': domainColor(node.domain),
            'emissiveIntensity': 0.35,
            'roughness': 0.45,
            'metalness': 0.10,
          }),
        );
      case NodeKind.synapse:
        return three.Mesh(
          three.SphereGeometry(0.10, 16, 12),
          three.MeshStandardMaterial.fromMap({
            'color': 0x5EEAD4,
            'emissive': 0x5EEAD4,
            'emissiveIntensity': 0.85,
            'roughness': 0.30,
            'metalness': 0.05,
          }),
        );
    }
  }

  void _addEdges(BrainTopology topology) {
    final byId = {for (final n in topology.nodes) n.id: n};
    final handlerPoints = <three.Vector3>[];

    for (final edge in topology.edges) {
      final a = byId[edge.from];
      final b = byId[edge.to];
      if (a == null || b == null) continue;
      handlerPoints.add(three.Vector3(a.x, a.y, a.z));
      handlerPoints.add(three.Vector3(b.x, b.y, b.z));
    }

    if (handlerPoints.isNotEmpty) {
      _threeJs.scene.add(
        three.LineSegments(
          three.BufferGeometry().setFromPoints(handlerPoints),
          three.LineBasicMaterial.fromMap({
            'color': 0x5EEAD4,
            'transparent': true,
            'opacity': 0.30,
          }),
        ),
      );
    }
  }

  void _handleTap(PointerDownEvent e) {
    if (_picker == null || _pulseAnimator == null) return;
    final box = context.findRenderObject() as RenderBox?;
    if (box == null) return;
    final local = box.globalToLocal(e.position);
    final all = <three.Object3D>[
      for (final list in _neuronsByDomain.values) ...list,
      ..._synapseMeshes,
      ..._pulseAnimator!.meshes,
    ];
    final result = _picker!.pick(local, all);
    final inspector = context.read<BrainInspectorBloc>();
    switch (result) {
      case null:
        inspector.add(Deselect());
        break;
      case NodePick p:
        final node = _topology!.nodes.firstWhere((n) => n.id == p.nodeId);
        if (node.kind == NodeKind.neuron) {
          inspector.add(SelectNeuron(nodeId: p.nodeId));
        } else {
          inspector.add(SelectSynapseType(nodeId: p.nodeId));
        }
        break;
      case PulsePick p:
        final fire = _pulseAnimator!.lookupFire(p.fireEventId);
        if (fire != null) {
          inspector.add(PausePulse(pulse: fire));
        }
        break;
    }
  }

  void _sendInput() {
    final text = _input.text.trim();
    if (text.isEmpty) return;
    _input.clear();
    _sendDemoPrompt(text);
  }

  void _sendDemoPrompt(String text) {
    context.read<PersonaBloc>().add(PersonaEmotionChanged(PersonaEmotion.thinking));
    context.read<InoBloc>().add(SendMessage(text));
  }

  static const String _travelDemoPrompt =
      'plan a week-long trip to Bali for two, first week of May';

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: Focus(
        autofocus: true,
        onKeyEvent: (node, event) {
          if (event is KeyDownEvent &&
              event.logicalKey == LogicalKeyboardKey.escape) {
            context.read<BrainInspectorBloc>().add(Deselect());
            return KeyEventResult.handled;
          }
          return KeyEventResult.ignored;
        },
        child: Stack(
          children: [
            Positioned.fill(
              child: Listener(
                onPointerDown: _handleTap,
                child: _threeJs.build(),
              ),
            ),
            Positioned(
              top: 12,
              left: 12,
              child: SafeArea(
                child: _BrainNavMenu(
                  onTravelDemo: () =>
                      _sendDemoPrompt(_travelDemoPrompt),
                ),
              ),
            ),
            const Positioned(
              top: 12,
              right: 12,
              child: SafeArea(child: _BrainLegend()),
            ),
            const Positioned.fill(child: BrainInspectorDrawer()),
            Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              child: SafeArea(
                child: _BrainComposer(controller: _input, onSend: _sendInput),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _BrainComposer extends StatelessWidget {
  const _BrainComposer({required this.controller, required this.onSend});

  final TextEditingController controller;
  final VoidCallback onSend;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.fromLTRB(12, 0, 12, 12),
      padding: const EdgeInsets.fromLTRB(8, 6, 6, 6),
      decoration: BoxDecoration(
        color: Colors.black.withAlpha(170),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: Colors.white.withAlpha(30)),
      ),
      child: BlocBuilder<InoBloc, InoBlocState>(
        buildWhen: (a, b) => a.isLoading != b.isLoading,
        builder: (context, state) {
          return Row(
            children: [
              IconButton(
                onPressed: null,
                tooltip: 'voice coming in slice 2',
                icon: const Icon(Icons.mic_none, color: Colors.white38),
              ),
              const SizedBox(width: 4),
              Expanded(
                child: TextField(
                  controller: controller,
                  enabled: !state.isLoading,
                  style: const TextStyle(color: Colors.white),
                  decoration: InputDecoration(
                    hintText: state.isLoading
                        ? 'ino is thinking...'
                        : 'Talk to ino...',
                    hintStyle: TextStyle(color: Colors.white.withAlpha(120)),
                    border: InputBorder.none,
                    contentPadding: const EdgeInsets.symmetric(horizontal: 12),
                  ),
                  onSubmitted: (_) => onSend(),
                ),
              ),
              IconButton(
                onPressed: state.isLoading ? null : onSend,
                icon: Icon(
                  Icons.arrow_upward_rounded,
                  color: Theme.of(context).colorScheme.primary,
                ),
              ),
            ],
          );
        },
      ),
    );
  }
}

class _BrainLegend extends StatelessWidget {
  const _BrainLegend();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      decoration: BoxDecoration(
        color: Colors.white.withAlpha(10),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.white.withAlpha(30)),
      ),
      child: const Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          _LegendDot(color: Color(0xFFF5B4A0), label: 'capability (domain-tinted)'),
          SizedBox(height: 4),
          _LegendDot(color: Color(0xFF5EEAD4), label: 'signal'),
        ],
      ),
    );
  }
}

class _LegendDot extends StatelessWidget {
  const _LegendDot({required this.color, required this.label});
  final Color color;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 10,
          height: 10,
          decoration: BoxDecoration(color: color, shape: BoxShape.circle),
        ),
        const SizedBox(width: 8),
        Text(
          label,
          style: const TextStyle(color: Colors.white70, fontSize: 11),
        ),
      ],
    );
  }
}

enum _BrainNavAction { travelDemo, persona, rfwV2, rfwV3 }

class _BrainNavMenu extends StatelessWidget {
  const _BrainNavMenu({required this.onTravelDemo});

  final VoidCallback onTravelDemo;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.transparent,
      child: PopupMenuButton<_BrainNavAction>(
        tooltip: 'Demos',
        icon: const Icon(Icons.menu, color: Colors.white70),
        color: Colors.black.withAlpha(220),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(10),
          side: BorderSide(color: Colors.white.withAlpha(30)),
        ),
        onSelected: (action) {
          switch (action) {
            case _BrainNavAction.travelDemo:
              onTravelDemo();
              break;
            case _BrainNavAction.persona:
              context.go('/persona');
              break;
            case _BrainNavAction.rfwV2:
              context.go('/rfw-v2');
              break;
            case _BrainNavAction.rfwV3:
              context.go('/rfw-v3');
              break;
          }
        },
        itemBuilder: (context) => const [
          PopupMenuItem(
            value: _BrainNavAction.travelDemo,
            child: ListTile(
              dense: true,
              leading: Icon(Icons.flight_takeoff, color: Colors.white70, size: 18),
              title: Text('Run Travel demo', style: TextStyle(color: Colors.white)),
            ),
          ),
          PopupMenuItem(
            value: _BrainNavAction.persona,
            child: ListTile(
              dense: true,
              leading: Icon(Icons.face_retouching_natural, color: Colors.white70, size: 18),
              title: Text('Rive persona', style: TextStyle(color: Colors.white)),
            ),
          ),
          PopupMenuItem(
            value: _BrainNavAction.rfwV2,
            child: ListTile(
              dense: true,
              leading: Icon(Icons.dashboard_outlined, color: Colors.white70, size: 18),
              title: Text('RFW v2 demo', style: TextStyle(color: Colors.white)),
            ),
          ),
          PopupMenuItem(
            value: _BrainNavAction.rfwV3,
            child: ListTile(
              dense: true,
              leading: Icon(Icons.auto_awesome, color: Colors.white70, size: 18),
              title: Text('RFW v3 demo', style: TextStyle(color: Colors.white)),
            ),
          ),
        ],
      ),
    );
  }
}
