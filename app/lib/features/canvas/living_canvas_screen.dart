import 'dart:async';
import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'package:digitalbrain_flutter/grpc/brainwatch.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/uigateway.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/uigateway.pb.dart' as ui;
import 'package:digitalbrain_flutter/grpc/endpoint.dart';
import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';

import 'package:digitalbrain_flutter/features/neuron_constructor/visual_constructor_state.dart';
import 'package:digitalbrain_flutter/features/neuron_constructor/visual_constructor_canvas.dart';
import 'package:digitalbrain_flutter/features/canvas/panel/panel_manager.dart';
import 'package:digitalbrain_flutter/features/canvas/panel/floating_panel_layer.dart';
import 'package:digitalbrain_flutter/features/canvas/panel/ui_layout_bridge.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

class LivingCanvasScreen extends StatefulWidget {
  const LivingCanvasScreen({super.key});

  @override
  State<LivingCanvasScreen> createState() => _LivingCanvasScreenState();
}

class _LivingCanvasScreenState extends State<LivingCanvasScreen> {
  dynamic _channel;
  DigitalBrainGatewayClient? _client;
  BrainWatchClient? _brainWatchClient;
  UiGatewayClient? _uiClient;

  static const String _brainId = 'default';

  // Kernel library name for raw synapse-trace feed cards (no renderable surface).
  static const String _broadcastLibrary = 'synapse-broadcast';

  final VisualConstructorState _constructorState = VisualConstructorState();
  final RfwRuntimeHost _rfwHost = RfwRuntimeHost();

  // Window manager: each WatchHomeFeed card becomes a floating panel (W-1).
  late final PanelManager _panels = PanelManager(
    brainId: _brainId,
    onAutoLayoutSettled: _emitViewport,
  );

  StreamSubscription<gw.RfwCardEnvelope>? _homeFeedSub;
  StreamController<ui.UiInputSynapse>? _uiInput;
  StreamSubscription<ui.UiStateSignal>? _uiSessionSub;

  // Fallback / Initial templates in case of offline launch or silo restarts
  static const _fallbackTemplates = {
    'top_bar': '''import digitalbrain;
widget root = Panel(
  radius: 20.0,
  padding: 16.0,
  child: HStack(
    between: true,
    children: [
      HStack(
        gap: 12.0,
        children: [
          GlowIcon(seed: 1, size: 20.0, tone: "teal", shapeHint: "orb"),
          Text(text: data.title, variant: "title"),
        ]
      ),
      HStack(
        gap: 12.0,
        children: [
          Badge(text: data.statusText, tone: data.statusTone),
        ]
      )
    ]
  )
);''',
    'scenario_explorer': '''import digitalbrain;
widget root = Panel(
  radius: 24.0,
  padding: 20.0,
  child: VStack(
    gap: 16.0,
    cross: "stretch",
    children: [
      HStack(
        gap: 8.0,
        children: [
          GlowIcon(seed: 2, size: 16.0, tone: "gold", shapeHint: "orb"),
          SectionLabel(text: "Scenario Explorer"),
        ]
      ),
      Divider(),
      HStack(
        gap: 8.0,
        equal: true,
        children: [
          Button(label: "digitalbrain.ino", onTap: event "selectScenario" { key: "digitalbrain.ino" }),
          Button(label: "document_analysis.ino", onTap: event "selectScenario" { key: "document_analysis.ino" }),
        ]
      ),
      CodeEditor(text: data.codeText),
      HStack(
        gap: 12.0,
        equal: true,
        children: [
          Button(label: "Save Scenario", onTap: event "saveScenario" {}),
          Button(label: "Run Test", onTap: event "runScenarioTest" {}),
        ]
      )
    ]
  )
);''',
    'inspector_panel': '''import digitalbrain;
widget root = Panel(
  radius: 24.0,
  padding: 20.0,
  child: VStack(
    gap: 16.0,
    cross: "stretch",
    children: [
      HStack(
        between: true,
        children: [
          HStack(
            gap: 8.0,
            children: [
              GlowIcon(seed: 3, size: 16.0, tone: "amber", shapeHint: "orb"),
              Text(text: "Inspector", variant: "title"),
            ]
          ),
          Button(label: "X", onTap: event "closeSurface" {})
        ]
      ),
      Divider(),
      SectionLabel(text: "IDENTIFIER"),
      Text(text: data.label, variant: "heading"),
      Divider(),
      SectionLabel(text: "RFW SURFACE / SCRIPT"),
      CodeEditor(text: data.codePayload),
      Divider(),
      HStack(
        gap: 12.0,
        equal: true,
        children: [
          Button(label: "Fire Synapse", onTap: event "fireSynapse" {}),
          Button(label: "Delete Node", onTap: event "deleteNode" {})
        ]
      )
    ]
  )
);''',
    'notification_feed': '''import digitalbrain;
widget root = Panel(
  radius: 20.0,
  padding: 16.0,
  child: HStack(
    gap: 16.0,
    cross: "center",
    children: [
      Avatar(initials: data.initials, tone: data.tone, size: 40.0),
      VStack(
        cross: "start",
        gap: 4.0,
        children: [
          Text(text: data.title, variant: "title"),
          Text(text: data.body, variant: "dim"),
        ]
      )
    ]
  )
);''',
  };

  final Map<String, String> _scenarios = {
    'digitalbrain.ino': '''neuron DigitalBrain.System
  "The distributed OS coordinator. Starts core services, manages dynamic resources, and binds the visual shell."

  using loaded            = synapse(DigitalBrain.Kernel.Loaded)
  using brains            = neuron(DigitalBrain.BrainRegistry)
  using aspire            = neuron(DigitalBrain.SDK.AspireRuntime)

  on loaded:
    log "system: initializing DigitalBrain substrate"
    ask brains to "list"
    ask aspire to "register-resource orleans-redis"''',
    'document_analysis.ino': '''neuron DocumentAnalysis.Cortex
  "Parses invoices, logs, or word files, extracts core structural concepts, and projects them directly to the visual constructor canvas."

  using docReceived      = synapse(ParseDocRequest)
  using parser           = neuron(DocumentParserNeuron)
  using mapper           = neuron(VisualCanvasNeuron)

  on docReceived it:
    log "cortex: document analysis triggered for {it.DocumentName}"
    let parsed = ask parser to parse it
    ask mapper to map parsed.ConceptsJson''',
  };

  late String _selectedScenario;
  late TextEditingController _inoCodeController;

  final Map<String, String> _rfwTemplates = {};
  final Map<String, Map<String, dynamic>> _rfwStates = {};

  @override
  void initState() {
    super.initState();
    _selectedScenario = _scenarios.keys.first;
    _inoCodeController = TextEditingController(
      text: _scenarios[_selectedScenario],
    );

    try {
      final (host, port, secure) = resolveKernelEndpoint();
      _channel = createKernelChannel(host: host, port: port, secure: secure);
      _client = DigitalBrainGatewayClient(
        _channel,
        interceptors: kernelInterceptors(),
      );
      _brainWatchClient = BrainWatchClient(
        _channel,
        interceptors: kernelInterceptors(),
      );
      _uiClient = UiGatewayClient(_channel, interceptors: kernelInterceptors());
      _startHomeFeedSubscription();
      _subscribeHomeFeedPanels();
      _openUiSession();
      _loadRfwLayouts();
      _panels.restoreLayout();
    } catch (_) {
      // Standalone run / Offline simulator
    }
  }

  // Each RfwCardEnvelope from WatchHomeFeed spawns/updates a floating panel.
  void _subscribeHomeFeedPanels() {
    final client = _client;
    if (client == null) return;
    _homeFeedSub = client.watchHomeFeed(gw.WatchHomeFeedRequest()).listen((
      env,
    ) {
      // Only neuron UI surfaces become floating panels. Raw `synapse-broadcast`
      // traces and observer feeds carry no renderable surface
      // and share a correlationId with the surface card — without this filter
      // they spawn empty "No surface" panels and clobber the real surface.
      if (!_isRenderableSurface(env)) return;
      _panels.upsertFromEnvelope(env);
      _panels.saveLayout();
    }, onError: (_) {});
  }

  // A card is renderable iff it ships RFW `source` or a `{name,...}` ui-layout
  // tree (mirrors the panel body's surface resolution in floating_panel_layer).
  bool _isRenderableSurface(gw.RfwCardEnvelope env) {
    if (env.libraryName == _broadcastLibrary) return false;
    if (env.rootWidget == 'TaskManagerCard') return true;
    if (env.dataJson.isEmpty) return false;
    try {
      final decoded = jsonDecode(env.dataJson);
      if (decoded is! Map) return false;
      final data = decoded.cast<String, Object?>();
      final source = data['source'];
      if (source is String && source.isNotEmpty) return true;
      return looksLikeUiLayout(data);
    } catch (_) {
      return false;
    }
  }

  // Open the bidi UI session; auto-layout settles the background camera via the
  // viewport signal the kernel streams back (W-5).
  void _openUiSession() {
    final client = _uiClient;
    if (client == null) return;
    final input = StreamController<ui.UiInputSynapse>();
    _uiInput = input;
    _uiSessionSub = client.engageUiSession(input.stream).listen((signal) {
      if (!signal.hasViewport()) return;
      final zoom = signal.viewport.zoomDepth;
      if (zoom > 0) _constructorState.zoomScale = zoom;
    }, onError: (_) {});
  }

  void _emitViewport(Offset target) {
    _uiInput?.add(
      ui.UiInputSynapse()
        ..brainId = _brainId
        ..elementId = 'auto-layout'
        ..interaction = ui.UiInputSynapse_InteractionType.DRAG_NODE
        ..coordinates = (ui.Point3D()
          ..x = target.dx
          ..y = target.dy
          ..z = 0),
    );
  }

  // A panel surface's RFW event. Known shell verbs are handled locally; any
  // other event with a `type` arg is fired back to the kernel as a synapse
  // (data-driven, zero per-intent Dart) — e.g. the reminder "snooze" button.
  void _handlePanelEvent(
    String panelId,
    String name,
    Map<String, Object?> args,
  ) {
    debugPrint('Panel $panelId event: $name $args');
    var type = args['type']?.toString();
    // Support surface action descriptors (installAction etc from kit): use synapseType if no top-level type.
    final synapseTypeVal = args['synapseType'];
    if ((type == null || type.isEmpty) && synapseTypeVal is String && synapseTypeVal.isNotEmpty) {
      type = synapseTypeVal;
    }
    final actionVal = args['action'];
    if ((type == null || type.isEmpty) && actionVal is Map && actionVal['synapseType'] is String) {
      final ass = actionVal['synapseType'] as String;
      if (ass.isNotEmpty) type = ass;
    }
    final client = _client;
    if (type == null || type.isEmpty || client == null) return;
    final payload = Map<String, Object?>.of(args)..remove('type');
    var typeName = type;
    if (type == 'cancelTask') typeName = 'CancelTask';
    final envelope = gw.SynapseEnvelope()
      ..correlationId = panelId
      ..typeName = typeName
      ..payload = Uint8List.fromList(utf8.encode(jsonEncode(payload)));
    client.send(envelope);
  }




  @override
  void dispose() {
    _homeFeedSub?.cancel();
    _uiSessionSub?.cancel();
    _uiInput?.close();
    _panels.saveLayout();
    _panels.dispose();
    _inoCodeController.dispose();
    super.dispose();
  }

  Future<void> _loadRfwLayouts() async {
    final client = _client;
    if (client == null) return;

    final layouts = [
      "top_bar",
      "scenario_explorer",
      "inspector_panel",
      "notification_feed",
    ];
    for (final layout in layouts) {
      try {
        final reply = await client.getRfwLayout(
          gw.RfwLayoutRequest()..layoutName = layout,
        );
        _rfwHost.ensureLoaded(layout, reply.rfwTemplate);
        setState(() {
          _rfwTemplates[layout] = reply.rfwTemplate;
          _rfwStates[layout] =
              jsonDecode(reply.dataJson) as Map<String, dynamic>;
        });
      } catch (e) {
        debugPrint("Error loading RFW layout $layout: $e");
      }
    }
  }

  void _startHomeFeedSubscription() {
    if (_brainWatchClient == null) return;

    // Listen to gRPC synapse events to catch RenderAsync UI cards
    _brainWatchClient!
        .watchSynapses(WatchSynapsesRequest(brainId: 'default'))
        .listen((edge) {
          if (edge.typeName.contains("SystemAlertFiredEvent") ||
              edge.typeName.contains("ConceptsExtractedEvent")) {
            try {
              final data = jsonDecode(utf8.decode(edge.payload));
              setState(() {
                _rfwStates['notification_feed'] = {
                  'initials': (data['Title'] ?? data['title'] ?? 'D')
                      .toString()
                      .substring(0, 1),
                  'tone': data['Tone'] ?? data['tone'] ?? 'cyan',
                  'title':
                      data['Title'] ?? data['title'] ?? 'Orleans Event Fired',
                  'body':
                      data['Body'] ??
                      data['body'] ??
                      'Cortex synapse completed successfully.',
                };
              });
            } catch (_) {}
          }
        }, onError: (_) {});
  }

  void _selectScenario(String key) {
    setState(() {
      _selectedScenario = key;
      _inoCodeController.text = _scenarios[key] ?? '';
      _rfwStates['scenario_explorer'] = {'codeText': _scenarios[key] ?? ''};
    });
  }

  void _saveScenario() {
    setState(() {
      _scenarios[_selectedScenario] = _inoCodeController.text;
    });
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          'Scenario "$_selectedScenario" saved successfully to local substrate!',
        ),
        backgroundColor: const Color(0xFF00FFD2),
      ),
    );
  }

  Future<void> _runScenarioTest() async {
    final client = _client;
    final correlationId = 'test-${DateTime.now().millisecondsSinceEpoch}';

    // Instantly trigger visual pulse on the local canvas
    _constructorState.onSynapseFiredLocally?.call(
      "DigitalBrain.SDK.Ai.Contracts.ParseDocRequest",
    );

    if (client == null) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Offline mode: Simulating document extraction pipeline...',
          ),
          backgroundColor: Color(0xFF00FFD2),
        ),
      );

      Future.delayed(const Duration(milliseconds: 800), () {
        _constructorState.onSynapseFiredLocally?.call(
          "DigitalBrain.SDK.Ai.Contracts.ConceptsExtractedEvent",
        );
      });

      Future.delayed(const Duration(milliseconds: 1400), () {
        final mockConcepts = [
          'Neuron',
          'Synapse',
          'Timeline',
          'Orleans',
          'Aspire',
        ];
        for (var i = 0; i < mockConcepts.length; i++) {
          Future.delayed(Duration(milliseconds: i * 200), () {
            final x = 120.0 + (i * 90.0);
            final y = 280.0 + (i % 2 * 60.0);
            _constructorState.addConceptNode(
              'concept_mock_$i',
              mockConcepts[i],
              Offset(x, y),
              'cyan',
            );
            _constructorState.onSynapseFiredLocally?.call(
              "DigitalBrain.SDK.Ai.Contracts.CanvasRenderEvent",
            );
          });
        }
      });
      return;
    }

    try {
      final envelope = gw.SynapseEnvelope()
        ..correlationId = correlationId
        ..typeName = 'DigitalBrain.SDK.Ai.Contracts.ParseDocRequest'
        ..payload = Uint8List.fromList(
          utf8.encode(
            jsonEncode({
              'DocumentName': _selectedScenario,
              'TextContent': _inoCodeController.text,
            }),
          ),
        );

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Dispatched ParseDocRequest to Orleans parser silos...',
          ),
          duration: Duration(seconds: 1),
        ),
      );

      await client.send(envelope);
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text('Error dispatching synapse: $e')));
    }
  }

  Map<String, dynamic> _getFallbackState(String key) {
    switch (key) {
      case 'top_bar':
        return {
          'title': 'DigitalBrain Substrate v5',
          'statusText': _client == null
              ? 'Offline Simulator'
              : 'Orleans Connected',
          'statusTone': _client == null ? 'indigo' : 'teal',
        };
      case 'scenario_explorer':
        return {'codeText': _scenarios[_selectedScenario] ?? ''};
      case 'inspector_panel':
        final selectedId = _constructorState.selectedNodeId;
        if (selectedId != null) {
          final node = _constructorState.nodes[selectedId];
          if (node != null) {
            return {'label': node.label, 'codePayload': node.codePayload};
          }
        }
        return {'label': '', 'codePayload': ''};
      case 'notification_feed':
        return {
          'initials': 'D',
          'tone': 'cyan',
          'title': 'Cortex Active',
          'body': 'Frosted RFW layers rendered dynamically from host.',
        };
      default:
        return {};
    }
  }

  void _handleRfwEvent(String key, String name, dynamic args) {
    debugPrint("RFW Event from $key: $name, args: $args");
    if (name == 'selectScenario') {
      final scenarioKey = args['key'] as String;
      _selectScenario(scenarioKey);
    } else if (name == 'saveScenario') {
      _saveScenario();
    } else if (name == 'runScenarioTest') {
      _runScenarioTest();
    } else if (name == 'closeSurface') {
      setState(() {
        _constructorState.selectNode(null);
      });
    } else if (name == 'deleteNode') {
      final nodeId = _constructorState.selectedNodeId;
      if (nodeId != null) {
        setState(() {
          _constructorState.removeNode(nodeId);
          _constructorState.selectNode(null);
        });
      }
    } else if (name == 'fireSynapse') {
      _runScenarioTest();
    }
  }

  Widget _buildRfwPanel(String key) {
    final template = _rfwTemplates[key] ?? _fallbackTemplates[key]!;
    _rfwHost.ensureLoaded(key, template);

    final data = _rfwStates[key] ?? _getFallbackState(key);

    return _rfwHost.render(
      key,
      data: data,
      onEvent: (name, args) {
        _handleRfwEvent(key, name, args);
      },
      semanticsId: key,
      semanticsLabel: key,
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0D0E12),
      body: Stack(
        children: [
          // 1. Full-screen visual node constructor background canvas
          Positioned.fill(
            child: VisualConstructorCanvas(
              state: _constructorState,
              brainWatchClient: _brainWatchClient,
              gatewayClient: _client,
            ),
          ),

          // 2. Premium dynamic Top Bar RFW
          Positioned(
            top: 20,
            left: 24,
            right: 24,
            child: _buildRfwPanel('top_bar'),
          ),

          // 3. Left area: main neuron-driven workspace RFW (tab chrome removed; all UI from neurons)
          Positioned(
            top: 70,
            bottom: 40,
            left: 24,
            width: 380,
            child: _buildRfwPanel('scenario_explorer'),
          ),

          // 4. Right Sidebar (Inspector Panel RFW)
          Positioned(
            top: 100,
            bottom: 40,
            right: 24,
            width: 380,
            child: ListenableBuilder(
              listenable: _constructorState,
              builder: (context, _) {
                final selectedId = _constructorState.selectedNodeId;
                if (selectedId == null) return const SizedBox.shrink();
                final node = _constructorState.nodes[selectedId];
                if (node == null) return const SizedBox.shrink();

                // Sync selected node fields into RFW state
                final inspectorState = _rfwStates['inspector_panel'] ?? {};
                _rfwStates['inspector_panel'] = {
                  ...inspectorState,
                  'label': node.label,
                  'codePayload': node.codePayload,
                };

                return _buildRfwPanel('inspector_panel');
              },
            ),
          ),

          // 5. Floating Bottom notification Feed RFW
          Positioned(
            bottom: 40,
            left: 428,
            right: 428,
            height: 120,
            child: _buildRfwPanel('notification_feed'),
          ),

          // 6. Window-manager layer: WatchHomeFeed cards as draggable panels,
          // a dock strip, and the auto-layout button (docs/redesign Slice 1).
          Positioned.fill(
            child: FloatingPanelLayer(
              manager: _panels,
              host: _rfwHost,
              onPanelEvent: _handlePanelEvent,
            ),
          ),
        ],
      ),
    );
  }

}
