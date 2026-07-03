import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';

import 'package:digitalbrain_flutter/features/surface_demo/activity_graph_view.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/endpoint.dart';
import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';

class SurfaceDemoApp extends StatelessWidget {
  const SurfaceDemoApp({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = buildDigitalBrainTheme();
    return MaterialApp(
      title: 'DigitalBrain Surface Demo',
      themeMode: ThemeMode.dark,
      theme: theme,
      darkTheme: theme,
      debugShowCheckedModeBanner: false,
      home: const SurfaceDemoScreen(),
    );
  }
}

class SurfaceDemoScreen extends StatefulWidget {
  const SurfaceDemoScreen({super.key});

  @override
  State<SurfaceDemoScreen> createState() => _SurfaceDemoScreenState();
}

class _SurfaceDemoScreenState extends State<SurfaceDemoScreen> {
  final RfwRuntimeHost _rfwHost = RfwRuntimeHost();
  dynamic _channel;
  DigitalBrainGatewayClient? _client;
  StreamSubscription<gw.RfwCardEnvelope>? _homeFeedSub;
  bool _homeFeedOpen = false;
  gw.RfwCardEnvelope? _latestSurface;
  Map<String, Object?>? _latestGraph;
  String? _connectionError;
  String? _streamError;
  bool _busy = false;
  bool _startupDemoQueued = false;

  @override
  void initState() {
    super.initState();
    _connect();
  }

  @override
  void dispose() {
    final subscription = _homeFeedSub;
    if (subscription != null) unawaited(subscription.cancel());
    final channel = _channel;
    if (channel != null) {
      channel.shutdown();
    }
    super.dispose();
  }

  void _connect() {
    try {
      final (host, port, secure) = resolveKernelEndpoint();
      final channel = createKernelChannel(
        host: host,
        port: port,
        secure: secure,
      );
      final client = DigitalBrainGatewayClient(
        channel,
        interceptors: kernelInterceptors(),
      );
      final oldSubscription = _homeFeedSub;
      if (oldSubscription != null) unawaited(oldSubscription.cancel());
      final homeFeedSub = client
          .watchHomeFeed(gw.WatchHomeFeedRequest())
          .listen(_handleHomeFeedCard, onError: _handleHomeFeedError, onDone: _handleHomeFeedDone);

      setState(() {
        _channel = channel;
        _client = client;
        _homeFeedSub = homeFeedSub;
        _homeFeedOpen = true;
        _latestSurface = null;
        _latestGraph = null;
        _connectionError = null;
        _streamError = null;
      });
      _queueStartupDemo();
    } catch (e) {
      setState(() => _connectionError = e.toString());
    }
  }

  void _handleHomeFeedCard(gw.RfwCardEnvelope envelope) {
    final data = _decodeData(envelope.dataJson);
    if (!mounted) return;

    setState(() {
      if (_isActivityGraph(data)) {
        _latestGraph = data;
      } else {
        _latestSurface = envelope;
      }
      _streamError = null;
    });
  }

  void _handleHomeFeedError(Object error) {
    if (!mounted) return;
    setState(() => _streamError = error.toString());
  }

  void _handleHomeFeedDone() {
    if (!mounted) return;
    setState(() => _homeFeedOpen = false);
  }

  void _queueStartupDemo() {
    if (_startupDemoQueued) return;

    _startupDemoQueued = true;
    Future<void>.delayed(const Duration(milliseconds: 900), () {
      if (mounted) _runDemo();
    });
  }

  Future<void> _runDemo() async {
    final client = _client;
    if (client == null || _busy) return;

    setState(() => _busy = true);
    try {
      final correlationId =
          'flutter-surface-${DateTime.now().millisecondsSinceEpoch}';
      await client.send(
        gw.SynapseEnvelope(
          correlationId: correlationId,
          typeName: 'DigitalBrain.Kernel.SurfaceDemoRequested',
          payload: Uint8List.fromList(
            utf8.encode(jsonEncode({'source': 'flutter'})),
          ),
        ),
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0D0E12),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _Toolbar(
                connected: _homeFeedOpen && _connectionError == null && _streamError == null,
                busy: _busy,
                onRun: _runDemo,
                onReconnect: _connect,
              ),
              const SizedBox(height: 16),
              Expanded(child: _body()),
            ],
          ),
        ),
      ),
    );
  }

  Widget _body() {
    if (_connectionError != null) {
      return _StatePanel(
        title: 'Kernel unavailable',
        body: _connectionError!,
        tone: const Color(0xFFFFB86B),
      );
    }

    if (_streamError != null) {
      return _StatePanel(
        title: 'Stream error',
        body: _streamError!,
        tone: const Color(0xFFFF6B7A),
      );
    }

    if (!_homeFeedOpen) {
      return const _StatePanel(
        title: 'Connecting',
        body: 'Opening surface stream',
        tone: Color(0xFF00FFD2),
      );
    }

    return LayoutBuilder(
      builder: (context, constraints) {
        final surface = _latestSurface == null
            ? const _StatePanel(
                title: 'Pack surface',
                body: 'Waiting for embodied pack output',
                tone: Color(0xFF00FFD2),
              )
            : _LiveSurfaceCard(host: _rfwHost, envelope: _latestSurface!);

        if (constraints.maxWidth < 980) {
          return Column(
            children: [
              Expanded(child: ActivityGraphView(data: _latestGraph)),
              const SizedBox(height: 16),
              SizedBox(height: 260, child: surface),
            ],
          );
        }

        return Row(
          children: [
            Expanded(flex: 7, child: ActivityGraphView(data: _latestGraph)),
            const SizedBox(width: 16),
            SizedBox(width: 430, child: surface),
          ],
        );
      },
    );
  }
}

class _Toolbar extends StatelessWidget {
  const _Toolbar({
    required this.connected,
    required this.busy,
    required this.onRun,
    required this.onReconnect,
  });

  final bool connected;
  final bool busy;
  final VoidCallback onRun;
  final VoidCallback onReconnect;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 56,
      padding: const EdgeInsets.symmetric(horizontal: 14),
      decoration: BoxDecoration(
        color: const Color(0xFF151820),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.white.withValues(alpha: 0.08)),
      ),
      child: Row(
        children: [
          Icon(
            connected ? Icons.hub_outlined : Icons.hub,
            color: connected
                ? const Color(0xFF00FFD2)
                : const Color(0xFFFFB86B),
          ),
          const SizedBox(width: 10),
          const Expanded(
            child: Text(
              'Realtime UiSurface',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16),
            ),
          ),
          IconButton(
            tooltip: 'Reconnect',
            onPressed: onReconnect,
            icon: const Icon(Icons.refresh),
          ),
          const SizedBox(width: 8),
          FilledButton.icon(
            onPressed: connected && !busy ? onRun : null,
            icon: busy
                ? const SizedBox.square(
                    dimension: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.bolt),
            label: const Text('Run demo'),
          ),
        ],
      ),
    );
  }
}

class _LiveSurfaceCard extends StatelessWidget {
  const _LiveSurfaceCard({required this.host, required this.envelope});

  final RfwRuntimeHost host;
  final gw.RfwCardEnvelope envelope;

  @override
  Widget build(BuildContext context) {
    final data = _decodeData(envelope.dataJson);
    final source = data['source'] as String?;
    final key = envelope.correlationId.isEmpty
        ? 'surface-demo-latest'
        : envelope.correlationId;

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: const Color(0xFF151820),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(
          color: const Color(0xFF00FFD2).withValues(alpha: 0.18),
        ),
      ),
      child: source == null || source.isEmpty
          ? _StatePanel(
              title: data['title']?.toString() ?? envelope.rootWidget,
              body: envelope.dataJson,
              tone: const Color(0xFF00FFD2),
            )
          : _renderRfw(key, source, data),
    );
  }

  Widget _renderRfw(String key, String source, Map<String, Object?> data) {
    host.ensureLoaded(key, source);
    final error = host.parseError(key);
    if (error != null) {
      return _StatePanel(
        title: 'Surface parse error',
        body: error,
        tone: const Color(0xFFFF6B7A),
      );
    }

    return host.render(
      key,
      data: data,
      rootWidget: envelope.rootWidget.isEmpty ? 'root' : envelope.rootWidget,
      onEvent: (_, _) {},
    );
  }

}

Map<String, Object?> _decodeData(String json) {
  try {
    final decoded = jsonDecode(json);
    if (decoded is Map) {
      return decoded.map((key, value) => MapEntry(key.toString(), value));
    }
  } catch (_) {}
  return const {};
}

bool _isActivityGraph(Map<String, Object?> data) =>
    data['kind'] == 'activity-graph' ||
    data['surfaceKind'] == 'activity-graph' ||
    data['surfaceId'] == 'surface.kernel.live-observability';

class _StatePanel extends StatelessWidget {
  const _StatePanel({
    required this.title,
    required this.body,
    required this.tone,
  });

  final String title;
  final String body;
  final Color tone;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: const Color(0xFF151820),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: tone.withValues(alpha: 0.24)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.view_quilt_outlined, color: tone),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  title,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    fontWeight: FontWeight.w700,
                    fontSize: 16,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(body, style: const TextStyle(color: Colors.white70)),
        ],
      ),
    );
  }
}
