import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:http/http.dart' as http;

import 'package:digitalbrain_flutter/grpc/digitalbrain.pb.dart' as gw;
import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/endpoint.dart';
import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';

/// Native chat shell: message list + input bar, wired to the real kernel over gRPC.
/// User bubbles are plain text; assistant bubbles render the neuron-emitted UiWidgetTree
/// via the existing UiSurfaceTreeRenderer, so anything a neuron can express as a tree
/// (plain text now, a dropped Excel's ui:Table later) renders with no new client code.
class ChatScreen extends StatefulWidget {
  const ChatScreen({
    super.key,
    DigitalBrainGatewayClient Function()? debugClientFactory,
  }) : _debugClientFactory = debugClientFactory;

  /// Test-only seam: lets a widget test force a deterministic connection failure
  /// instead of dialing the real kernel over gRPC.
  final DigitalBrainGatewayClient Function()? _debugClientFactory;

  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatMessage {
  final bool isUser;
  final String? text;
  final Map<String, Object?>? tree;

  const _ChatMessage.user(String this.text) : isUser = true, tree = null;
  const _ChatMessage.assistant(Map<String, Object?> this.tree)
    : isUser = false,
      text = null;
}

class _ChatScreenState extends State<ChatScreen> {
  final String _sessionId = 'chat-${Random().nextInt(1 << 31)}';
  final RfwRuntimeHost _rfwHost = RfwRuntimeHost();
  final TextEditingController _input = TextEditingController();
  final ScrollController _scroll = ScrollController();
  final List<_ChatMessage> _messages = [];

  dynamic _channel;
  DigitalBrainGatewayClient? _client;
  StreamSubscription<gw.RfwCardEnvelope>? _feedSub;
  String? _connectionError;
  bool _sending = false;

  @override
  void initState() {
    super.initState();
    _connect();
  }

  @override
  void dispose() {
    _feedSub?.cancel();
    _channel?.shutdown();
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  DigitalBrainGatewayClient _buildRealClient() {
    final (host, port, secure) = resolveKernelEndpoint();
    final channel = createKernelChannel(host: host, port: port, secure: secure);
    _channel = channel;
    return DigitalBrainGatewayClient(
      channel,
      interceptors: kernelInterceptors(),
    );
  }

  void _connect() {
    try {
      final client = widget._debugClientFactory?.call() ?? _buildRealClient();
      final sub = client
          .watchHomeFeed(gw.WatchHomeFeedRequest())
          .listen(_onCard, onError: _onFeedError);
      setState(() {
        _client = client;
        _feedSub = sub;
        _connectionError = null;
      });
    } catch (error) {
      setState(() => _connectionError = 'Could not reach the kernel: $error');
    }
  }

  void _onFeedError(Object error) {
    if (!mounted) return;
    setState(() => _connectionError = 'Kernel feed error: $error');
  }

  void _onCard(gw.RfwCardEnvelope envelope) {
    if (!mounted) return;
    final data = _decode(envelope.dataJson);
    if (data['role'] != 'assistant' || data['sessionId'] != _sessionId) return;
    final tree = data['tree'] as Map<String, Object?>?;
    if (tree == null) return;
    setState(() {
      _messages.add(_ChatMessage.assistant(tree));
      _sending = false;
    });
    _scrollToEnd();
  }

  Map<String, Object?> _decode(String json) {
    try {
      final d = jsonDecode(json);
      if (d is Map) return d.map((k, v) => MapEntry(k.toString(), v));
    } catch (_) {}
    return const {};
  }

  void _send() {
    final text = _input.text.trim();
    final client = _client;
    if (text.isEmpty || client == null || _sending) return;

    setState(() {
      _messages.add(_ChatMessage.user(text));
      _sending = true;
      _input.clear();
    });
    _scrollToEnd();

    final envelope = gw.SynapseEnvelope()
      ..typeName = 'InoRequest'
      ..payload = utf8.encode(
        jsonEncode({'prompt': text, 'sessionId': _sessionId}),
      );
    client.send(envelope).catchError((Object error) {
      if (!mounted) return null;
      setState(() {
        _sending = false;
        _connectionError = 'Failed to send: $error';
      });
      return null;
    });
  }

  Future<void> _attachFile() async {
    final client = _client;
    if (client == null || _sending) return;

    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: ['xlsx'],
      withData: true,
    );
    if (result == null || result.files.isEmpty) return;
    final file = result.files.first;
    final bytes = file.bytes;
    if (bytes == null) return;

    setState(() {
      _messages.add(_ChatMessage.user('\u{1F4CE} Attached ${file.name}'));
      _sending = true;
    });
    _scrollToEnd();

    try {
      final (host, port, secure) = resolveKernelEndpoint();
      final uri = Uri(
        scheme: secure ? 'https' : 'http',
        host: host,
        port: port,
        path: '/upload',
      );
      final request = http.MultipartRequest('POST', uri)
        ..fields['sessionId'] = _sessionId
        ..files.add(
          http.MultipartFile.fromBytes('file', bytes, filename: file.name),
        );
      final response = await request.send();
      if (response.statusCode >= 400) {
        throw Exception('Upload failed (HTTP ${response.statusCode})');
      }
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _sending = false;
        _connectionError = 'Failed to upload ${file.name}: $error';
      });
    }
  }

  void _scrollToEnd() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scroll.hasClients) {
        _scroll.animateTo(
          _scroll.position.maxScrollExtent + 80,
          duration: const Duration(milliseconds: 200),
          curve: Curves.easeOut,
        );
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final t = FTheme.of(context);
    const renderer = UiSurfaceTreeRenderer();

    return Column(
      children: [
        if (_connectionError != null)
          Container(
            width: double.infinity,
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            color: t.colors.destructive.withValues(alpha: 0.15),
            child: Text(
              _connectionError!,
              style: t.typography.xs.copyWith(color: t.colors.destructive),
            ),
          ),
        Expanded(
          child: _messages.isEmpty
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(32),
                    child: Text(
                      "I'm your DigitalBrain. Ask me anything, drop an Excel "
                      'file, or ask for the Bitcoin price.',
                      textAlign: TextAlign.center,
                      style: t.typography.sm.copyWith(
                        color: t.colors.mutedForeground,
                      ),
                    ),
                  ),
                )
              : ListView.builder(
                  controller: _scroll,
                  padding: const EdgeInsets.all(16),
                  itemCount: _messages.length + (_sending ? 1 : 0),
                  itemBuilder: (context, i) {
                    if (i >= _messages.length) {
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
                    final m = _messages[i];
                    return Padding(
                      padding: EdgeInsets.only(
                        bottom: 12,
                        left: m.isUser ? 48 : 0,
                        right: m.isUser ? 0 : 48,
                      ),
                      child: Align(
                        alignment: m.isUser
                            ? Alignment.centerRight
                            : Alignment.centerLeft,
                        child: m.isUser
                            ? Container(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 14,
                                  vertical: 10,
                                ),
                                decoration: BoxDecoration(
                                  color: t.colors.primary,
                                  borderRadius: BorderRadius.circular(14),
                                ),
                                child: Text(
                                  m.text!,
                                  style: t.typography.md.copyWith(
                                    color: t.colors.primaryForeground,
                                  ),
                                ),
                              )
                            : Container(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 14,
                                  vertical: 10,
                                ),
                                decoration: BoxDecoration(
                                  color: t.colors.card,
                                  borderRadius: BorderRadius.circular(14),
                                  border: Border.all(
                                    color: t.colors.border,
                                    width: 0.5,
                                  ),
                                ),
                                child: renderer.build(
                                  m.tree!,
                                  (name, args) {},
                                  rfwHost: _rfwHost,
                                  onNavSelected: (_) {},
                                ),
                              ),
                      ),
                    );
                  },
                ),
        ),
        Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            border: Border(top: BorderSide(color: t.colors.border, width: 0.5)),
            color: t.colors.background,
          ),
          child: Row(
            children: [
              FButton(
                onPress: _sending ? null : _attachFile,
                child: const Icon(Icons.attach_file),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: FTextField(
                  control: FTextFieldControl.managed(controller: _input),
                  hint: 'Ask INO anything, or attach an .xlsx...',
                  onSubmit: (_) => _send(),
                ),
              ),
              const SizedBox(width: 8),
              FButton(
                onPress: _sending ? null : _send,
                child: const Text('Send'),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
