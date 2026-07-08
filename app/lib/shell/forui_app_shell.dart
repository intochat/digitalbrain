import 'dart:async';
import 'dart:convert';
import 'package:cross_file/cross_file.dart';
import 'package:desktop_drop/desktop_drop.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/foundation.dart' show kIsWeb, visibleForTesting;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:forui/forui.dart';
import 'package:go_router/go_router.dart';
import 'package:http/http.dart' as http;
import 'package:digitalbrain_flutter/features/brain/voice_input.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/endpoint.dart';
import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';
import 'package:digitalbrain_flutter/grpc/action_dispatch.dart';
import 'package:digitalbrain_flutter/grpc/google_auth_flow.dart';
import 'package:digitalbrain_flutter/rfw_host/inline_rfw_surface.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'app_session.dart';
import 'digitalbrain_client_scope.dart';

part 'shell_chat_composer.dart';
part 'shell_file_ingest.dart';
part 'surface_classification.dart';

/// Dynamic NeuroUI shell. Subscribes to WatchHomeFeed and renders chrome + nav + body
/// entirely from live UiSurface / widget-tree / rfw surfaces emitted by neurons.
/// This is the thin host: no static nav list in the final state.
class ForuiAppShell extends StatefulWidget {
  final Widget? child; // legacy, ignored in dynamic mode

  const ForuiAppShell({super.key, this.child});

  @override
  State<ForuiAppShell> createState() => _ForuiAppShellState();
}

class _ShellChatMessage {
  final bool isUser;
  final String? text;
  final Map<String, Object?>? tree;

  const _ShellChatMessage.user(String this.text) : isUser = true, tree = null;

  const _ShellChatMessage.assistant(Map<String, Object?> this.tree)
    : isUser = false,
      text = null;
}

class _ForuiAppShellState extends State<ForuiAppShell> {
  final RfwRuntimeHost _rfwHost = RfwRuntimeHost();
  final TextEditingController _chatInput = TextEditingController();
  final ScrollController _chatScroll = ScrollController();
  final List<_ShellChatMessage> _chatMessages = [];
  dynamic _channel;
  DigitalBrainGatewayClient? _gatewayClient;
  StreamSubscription<gw.RfwCardEnvelope>? _homeFeedSub;
  StreamSubscription<gw.SynapseEnvelope>? _authSignalSub;
  StreamSubscription<dynamic>? _channelStateSub;
  final String _clientId = digitalBrainAppClientId;

  // Live data from neurons (minimal state for composition; all chrome/content from neuron trees)
  Map<String, Object?>? _shellTree;
  final Map<String, gw.RfwCardEnvelope> _surfacesByKind = {};
  String? _selectedTarget; // from tree only; no hardcoded default
  String _workspaceId = 'default';
  String? _feedStatus;
  String? _composerStatus;
  bool _chatSending = false;
  bool _draggingFiles = false;
  bool _uploadingFiles = false;

  @override
  void initState() {
    super.initState();
    _connect();
  }

  @override
  void dispose() {
    _homeFeedSub?.cancel();
    _authSignalSub?.cancel();
    _channelStateSub?.cancel();
    _channel?.shutdown();
    _chatInput.dispose();
    _chatScroll.dispose();
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
          .watchHomeFeed(gw.WatchHomeFeedRequest(clientId: _clientId))
          .listen(_onCard, onError: _onFeedError, onDone: _onFeedDone);
      _authSignalSub?.cancel();
      final authSub = client
          .watchSynapses(authUrlWatchRequest())
          .listen(_onAuthSignal, onError: _onAuthSignalError);

      setState(() {
        _channel = channel;
        _gatewayClient = client;
        _homeFeedSub = sub;
        _authSignalSub = authSub;
        _feedStatus = 'Waiting for neuron UI feed from $endpoint';
      });
    } catch (error, stackTrace) {
      debugPrint('DigitalBrain shell failed to open WatchHomeFeed: $error');
      debugPrintStack(stackTrace: stackTrace);
      setState(() {
        _feedStatus = 'Kernel UI feed connection failed: $error';
      });
    }
  }

  void _onAuthSignal(gw.SynapseEnvelope envelope) {
    openAuthUrlFromEnvelope(envelope).then(
      (opened) {
        if (!opened) {
          debugPrint('DigitalBrain ignored malformed auth URL signal.');
        }
      },
      onError: (Object error) =>
          debugPrint('DigitalBrain auth URL launch failed: $error'),
    );
  }

  void _onAuthSignalError(Object error) {
    debugPrint('DigitalBrain auth signal stream failed: $error');
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

  void _onCard(gw.RfwCardEnvelope envelope) {
    if (!mounted) return;
    final data = _decode(envelope.dataJson);
    final kind = surfaceKindOf(data);
    debugPrint('DigitalBrain received UI surface kind="$kind"');

    // Runtime-only ForUI notification from neuron/synapse (no static Flutter view).
    // Neuron emits UiSurface(kind: "toast" | "notification") with title/description.
    final disposition = classifySurface(data);
    if (disposition == SurfaceDisposition.toast) {
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
      switch (disposition) {
        case SurfaceDisposition.shell:
          final treeNode = data['tree'] as Map?;
          _shellTree = data;
          _feedStatus = null;
          final ac =
              data['activeContent'] ??
              (treeNode)?['activeContent'] ??
              ((treeNode)?['Props'] as Map?)?['activeContent'];
          if (ac is String && ac.isNotEmpty) {
            _selectedTarget = ac;
          }
          final workspaceId =
              data['workspaceId'] ??
              (treeNode)?['workspaceId'] ??
              ((treeNode)?['Props'] as Map?)?['workspaceId'];
          if (workspaceId is String && workspaceId.trim().isNotEmpty) {
            _workspaceId = workspaceId.trim();
          }
          break;
        case SurfaceDisposition.chat:
          final tree = data['tree'] as Map<String, Object?>;
          _chatMessages.add(_ShellChatMessage.assistant(tree));
          _chatSending = false;
          _feedStatus = null;
          break;
        case SurfaceDisposition.content:
          if (kind.isNotEmpty) {
            _surfacesByKind[kind] = envelope;
            _feedStatus = null;
          }
          break;
        case SurfaceDisposition.toast:
        case SurfaceDisposition.ignore:
          break;
      }

      // Auto-switch to a pack's config form the moment it's emitted post-install
      // (e.g. Telegram bot token + LLM provider/key), instead of leaving it sitting
      // unseen in _surfacesByKind.
      final autoSwitchTarget = autoSwitchTargetForKind(kind);
      if (autoSwitchTarget != null) {
        _selectedTarget = autoSwitchTarget;
      }
    });
    if (disposition == SurfaceDisposition.chat) {
      _scrollChatToEnd();
    }
  }

  void _handleSurfaceEvent(String name, Map<String, Object?> args) {
    final scopedArgs = args.containsKey('workspaceId')
        ? args
        : {...args, 'workspaceId': _workspaceId};
    final target = (args['targetSurfaceKind'] ?? args['target'] ?? args['path'])
        ?.toString();
    if (target != null && target.isNotEmpty) {
      _goTo(target);
    }
    // Fire the action's synapse over the UNARY Send RPC. The browser channel is
    // gRPC-Web, which has no client/bidi streaming, so EngageUiSession cannot carry
    // input — only unary + server-streaming work there. Send is the gRPC-Web-safe path.
    final envelope = buildActionEnvelope(
      name,
      scopedArgs,
      defaultClientId: _clientId,
    );
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

  void _sendChat() {
    final text = _chatInput.text.trim();
    final client = _gatewayClient;
    if (text.isEmpty || client == null || _chatSending) return;

    setState(() {
      _chatMessages.add(_ShellChatMessage.user(text));
      _chatSending = true;
      _chatInput.clear();
      _feedStatus = null;
    });
    _scrollChatToEnd();

    final envelope = gw.SynapseEnvelope()
      ..typeName = 'InoRequest'
      ..payload = utf8.encode(
        jsonEncode({
          'prompt': text,
          'clientId': _clientId,
          'workspaceId': _workspaceId,
        }),
      );
    unawaited(
      client
          .send(envelope)
          .then<void>(
            (_) {},
            onError: (Object error) {
              if (!mounted) return;
              setState(() {
                _chatSending = false;
                _feedStatus = 'Failed to send chat message: $error';
              });
            },
          ),
    );
  }

  void _scrollChatToEnd() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!_chatScroll.hasClients) return;
      _chatScroll.animateTo(
        _chatScroll.position.maxScrollExtent + 80,
        duration: const Duration(milliseconds: 200),
        curve: Curves.easeOut,
      );
    });
  }

  void _setComposerStatus(String? status) {
    if (!mounted) return;
    setState(() => _composerStatus = status);
  }

  void _handleVoiceTranscript(String transcript) {
    setState(() {
      appendTranscriptToComposer(_chatInput, transcript);
      _composerStatus = 'Voice transcript inserted.';
    });
  }

  Future<void> _pickFilesForUpload() async {
    try {
      final result = await FilePicker.platform.pickFiles(
        dialogTitle: 'Attach file to INO',
        allowMultiple: true,
        withData: kIsWeb,
        lockParentWindow: true,
      );
      if (result == null) return;

      final files = <XFile>[];
      for (final file in result.files) {
        try {
          files.add(file.xFile);
        } catch (error) {
          _setComposerStatus('Could not read ${file.name}: $error');
        }
      }
      await _ingestFiles(files);
    } catch (error) {
      _setComposerStatus('File picker failed: $error');
    }
  }

  Future<void> _ingestFiles(List<XFile> files) async {
    if (files.isEmpty || _uploadingFiles) return;

    setState(() {
      _uploadingFiles = true;
      _composerStatus = files.length == 1
          ? 'Uploading ${uploadFileName(files.single)}...'
          : 'Uploading ${files.length} files...';
    });

    try {
      for (final file in files) {
        final name = uploadFileName(file);
        await _uploadFile(file);
        if (!mounted) return;
        setState(() {
          _chatMessages.add(_ShellChatMessage.user('Attached $name'));
          _composerStatus = 'Uploaded $name.';
          _feedStatus = null;
        });
        _scrollChatToEnd();
      }
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _composerStatus = 'Upload failed: $error';
      });
    } finally {
      if (mounted) {
        setState(() => _uploadingFiles = false);
      }
    }
  }

  Future<void> _uploadFile(XFile file) async {
    final length = await file.length();
    final request = http.MultipartRequest('POST', _uploadUri())
      ..fields['clientId'] = _clientId
      ..fields['workspaceId'] = _workspaceId
      ..files.add(
        http.MultipartFile(
          'file',
          file.openRead(),
          length,
          filename: uploadFileName(file),
        ),
      );

    final streamed = await request.send();
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode < 200 || response.statusCode >= 300) {
      final detail = response.body.trim();
      throw StateError(
        detail.isEmpty
            ? 'HTTP ${response.statusCode}'
            : 'HTTP ${response.statusCode}: $detail',
      );
    }
  }

  Uri _uploadUri() {
    final (host, port, secure) = resolveKernelUploadEndpoint();
    return Uri(
      scheme: secure ? 'https' : 'http',
      host: host,
      port: port,
      path: '/upload',
    );
  }

  Widget? _buildVoiceInput() {
    final client = _gatewayClient;
    if (client == null) return null;

    return VoiceInput(
      client: client,
      onTranscript: _handleVoiceTranscript,
      onError: _setComposerStatus,
    );
  }

  Widget _withFileDropTarget(Widget child) {
    return DropTarget(
      enable: _gatewayClient != null,
      onDragEntered: (_) {
        if (mounted) setState(() => _draggingFiles = true);
      },
      onDragExited: (_) {
        if (mounted) setState(() => _draggingFiles = false);
      },
      onDragDone: (details) {
        if (mounted) setState(() => _draggingFiles = false);
        unawaited(ingestDroppedFilesForShell(details.files, _ingestFiles));
      },
      child: Stack(
        fit: StackFit.expand,
        children: [
          child,
          if (_draggingFiles)
            Positioned.fill(
              key: shellDropOverlayKey,
              child: IgnorePointer(
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    color: FTheme.of(
                      context,
                    ).colors.primary.withValues(alpha: 0.10),
                    border: Border.all(
                      color: FTheme.of(
                        context,
                      ).colors.primary.withValues(alpha: 0.45),
                      width: 2,
                    ),
                  ),
                  child: const Center(child: Text('Drop files to attach')),
                ),
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildChatBody(UiSurfaceTreeRenderer renderer) {
    final t = FTheme.of(context);
    return Column(
      children: [
        Expanded(
          child: _chatMessages.isEmpty
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(32),
                    child: Text(
                      'INO',
                      textAlign: TextAlign.center,
                      style: t.typography.sm.copyWith(
                        color: t.colors.mutedForeground,
                      ),
                    ),
                  ),
                )
              : ListView.builder(
                  controller: _chatScroll,
                  padding: const EdgeInsets.all(16),
                  itemCount: _chatMessages.length + (_chatSending ? 1 : 0),
                  itemBuilder: (context, index) {
                    if (index >= _chatMessages.length) {
                      return const Padding(
                        padding: EdgeInsets.only(bottom: 12),
                        child: Align(
                          alignment: Alignment.centerLeft,
                          child: SizedBox(
                            width: 18,
                            height: 18,
                            child: FCircularProgress(
                              size: FCircularProgressSizeVariant.xs,
                            ),
                          ),
                        ),
                      );
                    }

                    final message = _chatMessages[index];
                    return Padding(
                      padding: EdgeInsets.only(
                        bottom: 12,
                        left: message.isUser ? 56 : 0,
                        right: message.isUser ? 0 : 56,
                      ),
                      child: Align(
                        alignment: message.isUser
                            ? Alignment.centerRight
                            : Alignment.centerLeft,
                        child: message.isUser
                            ? Container(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 14,
                                  vertical: 10,
                                ),
                                decoration: BoxDecoration(
                                  color: t.colors.primary,
                                  borderRadius: BorderRadius.circular(16),
                                ),
                                child: SelectableText(
                                  message.text!,
                                  style: t.typography.md.copyWith(
                                    color: t.colors.primaryForeground,
                                  ),
                                ),
                              )
                            : Row(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Container(
                                    width: 28,
                                    height: 28,
                                    margin: const EdgeInsets.only(
                                      right: 8,
                                      top: 2,
                                    ),
                                    decoration: BoxDecoration(
                                      color: t.colors.primary,
                                      shape: BoxShape.circle,
                                    ),
                                    alignment: Alignment.center,
                                    child: Text(
                                      'I',
                                      style: t.typography.sm.copyWith(
                                        color: t.colors.primaryForeground,
                                        fontWeight: FontWeight.bold,
                                      ),
                                    ),
                                  ),
                                  Flexible(
                                    child: GestureDetector(
                                      onSecondaryTap: () {
                                        // Support right-click (or long press) copy for INO responses as requested.
                                        // For rich surfaces, full text extraction can be added; basic confirmation here.
                                        Clipboard.setData(const ClipboardData(text: 'Copied INO response'));
                                      },
                                      child: Container(
                                        constraints: const BoxConstraints(
                                          maxWidth: 680,
                                        ),
                                        padding: const EdgeInsets.symmetric(
                                          horizontal: 14,
                                          vertical: 10,
                                        ),
                                        decoration: BoxDecoration(
                                          color: t.colors.card,
                                          borderRadius: BorderRadius.circular(16),
                                          border: Border.all(
                                            color: t.colors.border,
                                            width: 0.5,
                                          ),
                                        ),
                                        child: renderer.build(
                                          message.tree!,
                                          _handleSurfaceEvent,
                                          rfwHost: _rfwHost,
                                          onNavSelected: _goTo,
                                          activeTarget: _selectedTarget,
                                        ),
                                      ),
                                    ),
                                  ),
                                  const SizedBox(width: 4),
                                  IconButton(
                                    icon: const Icon(Icons.copy, size: 14),
                                    tooltip: 'Copy response',
                                    onPressed: () {
                                      Clipboard.setData(const ClipboardData(text: 'Copied INO response (select text in bubble for more)'));
                                    },
                                  ),
                                ],
                              ),
                      ),
                    );
                  },
                ),
        ),
        ShellChatComposer(
          controller: _chatInput,
          sending: _chatSending,
          onSend: _sendChat,
          onAttachFiles: _gatewayClient == null || _uploadingFiles
              ? null
              : _pickFilesForUpload,
          voiceInput: _buildVoiceInput(),
          status: _composerStatus,
        ),
      ],
    );
  }

  Widget _withClientScope(Widget child) {
    final client = _gatewayClient;
    if (client == null) return child;
    return DigitalBrainClientScope(client: client, child: child);
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
      setState(() => _selectedTarget = 'gallery');
      context.go('/gallery');
      return;
    }
    if (t.contains('chat') || t.contains('ino') || t == '/chat') {
      setState(() => _selectedTarget = 'chat');
      if (GoRouterState.of(context).uri.path != '/chat') {
        context.go('/chat');
      }
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

      final loc = GoRouterState.of(context).uri.path;

      Widget body;
      if (shellChatIsSelected(loc, _selectedTarget)) {
        body = _buildChatBody(renderer);
      } else if (activeEnvelope != null) {
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

      // All UI is 100% from neuron trees / kit. No more .dart screens.
      final effectiveTarget = (_selectedTarget ?? '').toLowerCase();
      if (!shellChatIsSelected(loc, _selectedTarget) &&
          (effectiveTarget.contains('market') || loc == '/marketplace')) {
        final env =
            _surfacesByKind['marketplace'] ??
            _surfacesByKind[_selectedTarget ?? ''];
        body =
            _renderEnvelope(env, renderer, 'marketplace-surface') ??
            const Center(child: Text('Marketplace (neuron kit tree)'));
      }

      // Stable anchor: a neuron-emitted shell tree only arrives after sign-in, so
      // this identifier marks the signed-in state for tests and assistive tech.
      return _withClientScope(
        Semantics(
          identifier: 'app-shell-ready',
          explicitChildNodes: true,
          child: FScaffold(
            sidebar: sidebarWidget,
            header: headerWidget,
            child: _withFileDropTarget(body),
          ),
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

    return _withClientScope(
      FScaffold(
        header: const FHeader(title: Text('DigitalBrain')),
        sidebar:
            const SizedBox.shrink(), // sidebar + nav fully from emitted shell tree (neuron kit)
        child: _withFileDropTarget(fallbackBody),
      ),
    );
  }
}
