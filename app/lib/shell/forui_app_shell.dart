import 'dart:async';
import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:go_router/go_router.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/endpoint.dart';
import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';
import 'package:digitalbrain_flutter/grpc/action_dispatch.dart';
import 'package:digitalbrain_flutter/rfw_host/inline_rfw_surface.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'package:digitalbrain_flutter/grpc/uigateway.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/uigateway.pb.dart' as ui;

/// Dynamic NeuroUI shell. Subscribes to WatchHomeFeed and renders chrome + nav + body
/// entirely from live UiSurface / widget-tree / rfw surfaces emitted by neurons.
/// This is the thin host: no static nav list in the final state.
class ForuiAppShell extends StatefulWidget {
  final Widget? child; // legacy, ignored in dynamic mode

  const ForuiAppShell({super.key, this.child});

  @override
  State<ForuiAppShell> createState() => _ForuiAppShellState();
}

/// Surface kinds that should immediately become the visible shell body the moment
/// they arrive over the home-feed stream, mirroring the existing gallery auto-switch
/// a few lines below. Returns the `_selectedTarget` to switch to, or null if [kind]
/// shouldn't trigger an auto-switch. A plain top-level function (not a method) so it's
/// unit-testable without pumping the full widget tree or mocking the gRPC connection.
String? autoSwitchTargetForKind(String kind) {
  if (kind == 'pack-config-form') return kind;
  return null;
}

class _ForuiAppShellState extends State<ForuiAppShell> {
  final RfwRuntimeHost _rfwHost = RfwRuntimeHost();
  dynamic _channel;
  DigitalBrainGatewayClient? _gatewayClient;
  UiGatewayClient? _uiClient;
  StreamController<ui.UiInputSynapse>? _uiInput;
  StreamSubscription<ui.UiStateSignal>? _uiSessionSub;
  StreamSubscription<gw.RfwCardEnvelope>? _homeFeedSub;
  StreamSubscription<dynamic>? _channelStateSub;

  // Live data from neurons (minimal state for composition; all chrome/content from neuron trees)
  Map<String, Object?>? _shellTree;
  final Map<String, gw.RfwCardEnvelope> _surfacesByKind = {};
  String? _selectedTarget; // from tree only; no hardcoded default
  String? _feedStatus;

  @override
  void initState() {
    super.initState();
    _connect();
  }

  @override
  void dispose() {
    _homeFeedSub?.cancel();
    _uiSessionSub?.cancel();
    _channelStateSub?.cancel();
    _uiInput?.close();
    _channel?.shutdown();
    super.dispose();
  }

  void _connect() {
    try {
      final (host, port, secure) = resolveKernelEndpoint();
      final endpoint = '${secure ? 'https' : 'http'}://$host:$port';
      debugPrint('DigitalBrain shell connecting WatchHomeFeed to $endpoint');
      final channel = createKernelChannel(
        host: host,
        port: port,
        secure: secure,
      );
      _channelStateSub?.cancel();
      _channelStateSub = channel.onConnectionStateChanged.listen(
        (state) => debugPrint('DigitalBrain gRPC channel state: $state'),
        onError: (Object error) =>
            debugPrint('DigitalBrain gRPC channel state error: $error'),
      );
      final client = DigitalBrainGatewayClient(
        channel,
        interceptors: kernelInterceptors(),
      );

      _homeFeedSub?.cancel();
      final sub = client
          .watchHomeFeed(gw.WatchHomeFeedRequest())
          .listen(_onCard, onError: _onFeedError, onDone: _onFeedDone);

      setState(() {
        _channel = channel;
        _gatewayClient = client;
        _homeFeedSub = sub;
        _feedStatus = 'Waiting for neuron UI feed from $endpoint';
        _uiClient = UiGatewayClient(
          channel,
          interceptors: kernelInterceptors(),
        );
      });
      _openUiSession();
    } catch (error, stackTrace) {
      debugPrint('DigitalBrain shell failed to open WatchHomeFeed: $error');
      debugPrintStack(stackTrace: stackTrace);
      setState(() {
        _feedStatus = 'Kernel UI feed connection failed: $error';
      });
    }
  }

  void _onFeedError(Object error, StackTrace stackTrace) {
    debugPrint('DigitalBrain WatchHomeFeed error: $error');
    debugPrintStack(stackTrace: stackTrace);
    if (!mounted) return;
    setState(() {
      _feedStatus = 'Kernel UI feed stream failed: $error';
    });
  }

  void _onFeedDone() {
    debugPrint('DigitalBrain WatchHomeFeed stream closed.');
    if (!mounted) return;
    setState(() {
      _feedStatus = 'Kernel UI feed stream closed before any surface arrived.';
    });
  }

  // Receive-only: this bidi session carries server->client UiStateSignals. Client->server
  // actions go via the unary Send RPC (see _handleSurfaceEvent) because gRPC-Web cannot
  // client-stream. Do NOT route sends back through _uiInput here.
  void _openUiSession() {
    final c = _uiClient;
    if (c == null) return;
    final input = StreamController<ui.UiInputSynapse>();
    _uiInput = input;
    _uiSessionSub = c
        .engageUiSession(input.stream)
        .listen((_) {}, onError: (_) {});
  }

  void _onCard(gw.RfwCardEnvelope envelope) {
    if (!mounted) return;
    final data = _decode(envelope.dataJson);
    final kind = (data['kind'] ?? data['surfaceKind'] ?? '').toString();
    debugPrint('DigitalBrain received UI surface kind="$kind"');

    // Runtime-only ForUI notification from neuron/synapse (no static Flutter view).
    // Neuron emits UiSurface(kind: "toast" | "notification") with title/description.
    if (kind == 'toast' || kind == 'notification' || kind.contains('toast')) {
      final titleText = (data['title'] ?? data['message'] ?? 'Hello World!')
          .toString();
      final descText = data['description']?.toString();
      Future.microtask(() {
        if (mounted) {
          showFToast(
            context: context,
            title: Text(titleText),
            description: descText != null ? Text(descText) : null,
            duration: const Duration(seconds: 4),
          );
        }
      });
    }

    setState(() {
      final treeNode = data['tree'] as Map?;
      final hasShellMarker =
          kind == 'app-shell' ||
          kind == 'widget-tree' ||
          kind.contains('shell') ||
          data['activeContent'] != null;
      final treeLooksLikeShell =
          treeNode != null &&
          (treeNode['activeContent'] != null ||
              (treeNode['Props'] as Map?)?['activeContent'] != null ||
              (treeNode['Type']?.toString().toLowerCase().contains(
                    'scaffold',
                  ) ??
                  false) ||
              (treeNode['Type']?.toString().toLowerCase() == 'app-shell'));
      if (hasShellMarker || treeLooksLikeShell) {
        _shellTree = data;
        _feedStatus = null;
        final ac =
            data['activeContent'] ??
            (treeNode)?['activeContent'] ??
            ((treeNode)?['Props'] as Map?)?['activeContent'];
        if (ac is String && ac.isNotEmpty) {
          _selectedTarget = ac;
        }
      } else if (kind.isNotEmpty) {
        _surfacesByKind[kind] = envelope;
        _feedStatus = null;
      }

      // Auto-switch to a pack's config form the moment it's emitted post-install
      // (e.g. Telegram bot token + LLM provider/key), instead of leaving it sitting
      // unseen in _surfacesByKind.
      final autoSwitchTarget = autoSwitchTargetForKind(kind);
      if (autoSwitchTarget != null) {
        _selectedTarget = autoSwitchTarget;
      }
    });
  }

  void _handleSurfaceEvent(String name, Map<String, Object?> args) {
    final target = (args['targetSurfaceKind'] ?? args['target'] ?? args['path'])
        ?.toString();
    if (target != null && target.isNotEmpty) {
      _goTo(target);
    }
    // Fire the action's synapse over the UNARY Send RPC. The browser channel is
    // gRPC-Web, which has no client/bidi streaming, so EngageUiSession cannot carry
    // input — only unary + server-streaming work there. Send is the gRPC-Web-safe path.
    final envelope = buildActionEnvelope(name, args);
    final client = _gatewayClient;
    if (envelope != null && client != null) {
      client
          .send(envelope)
          .then(
            (_) {},
            onError: (Object error) =>
                debugPrint('DigitalBrain action dispatch failed: $error'),
          );
    }
  }

  Map<String, Object?> _decode(String json) {
    try {
      final d = jsonDecode(json);
      if (d is Map) return d.map((k, v) => MapEntry(k.toString(), v));
    } catch (_) {}
    return const {};
  }

  Widget? _renderEnvelope(
    gw.RfwCardEnvelope? env,
    UiSurfaceTreeRenderer renderer,
    String emptyKey,
  ) {
    if (env == null) return null;

    final data = _decode(env.dataJson);
    final treeNode = data['tree'] as Map<String, Object?>?;
    if (treeNode != null) {
      return SizedBox.expand(
        child: renderer.build(
          treeNode,
          _handleSurfaceEvent,
          rfwHost: _rfwHost,
          onNavSelected: _goTo,
          activeTarget: _selectedTarget,
        ),
      );
    }

    return buildInlineRfwSurface(
      host: _rfwHost,
      data: data,
      fallbackKey: emptyKey,
      defaultRootWidget: env.rootWidget,
      onEvent: _handleSurfaceEvent,
      correlationId: env.correlationId,
    );
  }

  void _goTo(String target) {
    final t = target.trim().toLowerCase();
    if (t.isEmpty) return;
    if (t.contains('market') || t == 'marketplace' || t == '/marketplace') {
      setState(() => _selectedTarget = 'marketplace');
      // Also update location for deep links / history, but body driven by target
      if (GoRouterState.of(context).uri.path != '/marketplace') {
        context.go('/marketplace');
      }
      return;
    }
    // Exact match only: a substring check here also swallows absolute deep-links that merely
    // contain "gallery" (e.g. /experience/ui-gallery/ui-gallery), sending them to the blank
    // /gallery route instead of letting the absolute-path branch below navigate to them.
    if (t == 'gallery' || t == '/gallery') {
      context.go('/gallery');
      return;
    }
    if (t.contains('chat') || t.contains('ino') || t == '/chat') {
      context.go('/chat');
      return;
    }
    if (t.startsWith('/')) {
      context.go(t);
      return;
    }
    setState(() => _selectedTarget = target);
  }

  @override
  Widget build(BuildContext context) {
    final tree = _shellTree;
    final activeEnvelope = _surfacesByKind[_selectedTarget];

    final renderer = const UiSurfaceTreeRenderer();

    if (tree != null) {
      // All chrome (sidebar, header) strictly from neuron tree children. No fallbacks or defaults.
      // Unwrap if the data is {tree: {Type: scaffold, Children: ...}} from widget-tree rfw.
      var root = tree;
      if (tree['tree'] is Map<String, Object?>) {
        root = (tree['tree'] as Map<String, Object?>).cast<String, Object?>();
      }
      Widget sidebarWidget = const SizedBox.shrink();
      Widget headerWidget = FHeader(title: const SizedBox.shrink());
      final children = root['Children'] ?? (root['Props'] as Map?)?['Children'];
      if (children is List && children.isNotEmpty) {
        for (final c in children) {
          final childMap = c as Map;
          final cType = (childMap['Type'] ?? childMap['type'] ?? '')
              .toString()
              .toLowerCase();
          if (cType.contains('sidebar') || cType.contains('menu')) {
            sidebarWidget = renderer.build(
              Map<String, Object?>.from(childMap),
              _handleSurfaceEvent,
              rfwHost: _rfwHost,
              onNavSelected: _goTo,
              activeTarget: _selectedTarget,
            );
          } else if (cType.contains('header')) {
            headerWidget = renderer.build(
              Map<String, Object?>.from(childMap),
              _handleSurfaceEvent,
              rfwHost: _rfwHost,
              onNavSelected: _goTo,
              activeTarget: _selectedTarget,
            );
          }
        }
      }

      Widget body;
      if (activeEnvelope != null) {
        body =
            _renderEnvelope(
              activeEnvelope,
              renderer,
              'shell-content-$_selectedTarget',
            ) ??
            const SizedBox.shrink();
      } else {
        body = const SizedBox.shrink();
      }

      final loc = GoRouterState.of(context).uri.path;

      // All UI is 100% from neuron trees / kit. No more .dart screens.
      final effectiveTarget = (_selectedTarget ?? '').toLowerCase();
      if (effectiveTarget.contains('market') || loc == '/marketplace') {
        final env =
            _surfacesByKind['marketplace'] ??
            _surfacesByKind[_selectedTarget ?? ''];
        body =
            _renderEnvelope(env, renderer, 'marketplace-surface') ??
            const Center(child: Text('Marketplace (neuron kit tree)'));
      }

      // Stable anchor: a neuron-emitted shell tree only arrives after sign-in, so
      // this identifier marks the signed-in state for tests and assistive tech.
      return Semantics(
        identifier: 'app-shell-ready',
        explicitChildNodes: true,
        child: FScaffold(
          sidebar: sidebarWidget,
          header: headerWidget,
          child: body,
        ),
      );
    }

    final loc = GoRouterState.of(context).uri.path;

    // Pure minimal fallback. Real UI (nav, content, all screens) comes exclusively from neuron-emitted UiWidgetTree / kit.
    Widget fallbackBody =
        _renderEnvelope(_surfacesByKind['login'], renderer, 'login-surface') ??
        _renderEnvelope(
          _surfacesByKind['installed-bundles'],
          renderer,
          'installed-fallback',
        ) ??
        _renderEnvelope(
          _surfacesByKind['marketplace-list'] ?? _surfacesByKind['marketplace'],
          renderer,
          'marketplace-fallback',
        ) ??
        Center(
          child: Text(
            _feedStatus ??
                'Waiting for full neuron tree (UI kit from synapses)',
            textAlign: TextAlign.center,
          ),
        );

    // Marketplace migration to backend UI also applies in pure fallback.
    // If a marketplace surface arrived (possible even before full shell tree), render it.
    // Marketplace is now fully from neuron-emitted UiWidgetTree using rich forui kit (no static screen).
    final effectiveTarget = (_selectedTarget ?? '').toLowerCase();
    if (effectiveTarget.contains('market') || loc == '/marketplace') {
      final env =
          _surfacesByKind['marketplace'] ??
          _surfacesByKind[_selectedTarget ?? ''];
      fallbackBody =
          _renderEnvelope(env, renderer, 'marketplace-fallback') ??
          const Center(
            child: Text(
              'Marketplace (neuron kit tree - use dev authoring via dispatch or MCP)',
            ),
          );
    }
    if (effectiveTarget.contains('install') ||
        effectiveTarget.contains('bundle') ||
        loc == '/installed') {
      gw.RfwCardEnvelope? env =
          _surfacesByKind['installed-bundles'] ??
          _surfacesByKind[_selectedTarget ?? ''];
      if (env == null) {
        for (final e in _surfacesByKind.values) {
          final dk = _decode(e.dataJson)['kind']?.toString() ?? '';
          if (dk.contains('install') || dk.contains('bundle')) {
            env = e;
            break;
          }
        }
      }
      fallbackBody =
          _renderEnvelope(env, renderer, 'installed-fallback') ?? fallbackBody;
    }

    return FScaffold(
      header: const FHeader(title: Text('DigitalBrain')),
      sidebar:
          const SizedBox.shrink(), // sidebar + nav fully from emitted shell tree (neuron kit)
      child: fallbackBody,
    );
  }
}
