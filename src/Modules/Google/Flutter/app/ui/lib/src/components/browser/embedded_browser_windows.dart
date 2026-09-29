import 'dart:async';
import 'dart:io';
import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:webview_windows/webview_windows.dart';

// One native environment per process; each window has its own marked CDP page.
Future<int>? _environment;
Future<int> _initializeEnvironment() async {
  final socket = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
  final port = socket.port;
  await socket.close();
  final profile = await Directory.systemTemp.createTemp(
    'digitalbrain-browser-',
  );
  await WebviewController.initializeEnvironment(
    userDataPath: profile.path,
    additionalArguments:
        '--remote-debugging-port=$port '
        '--remote-debugging-address=127.0.0.1 --disable-background-networking '
        '--disable-features=ServiceWorker --force-webrtc-ip-handling-policy=disable_non_proxied_udp '
        '--proxy-server=http://127.0.0.1:9 --proxy-bypass-list=<-loopback>',
  );
  return port;
}

class EmbeddedBrowser extends StatefulWidget {
  const EmbeddedBrowser({
    super.key,
    required this.onConnect,
    required this.onDisconnect,
  });
  final Future<void> Function(int, String)? onConnect;
  final Future<void> Function(String)? onDisconnect;

  @override
  State<EmbeddedBrowser> createState() => _EmbeddedBrowserState();
}

class _EmbeddedBrowserState extends State<EmbeddedBrowser> {
  WebviewController? _controller;
  final List<StreamSubscription<dynamic>> _subscriptions = [];
  final String _session = List.generate(
    16,
    (_) => Random.secure().nextInt(256).toRadixString(16).padLeft(2, '0'),
  ).join();
  String? _error;
  String _url = '';
  String? _navigationError;
  bool _loading = false;
  bool _ready = false;
  bool _connected = false;
  bool _controllerDisposed = false;
  bool get _supported =>
      Platform.isWindows && defaultTargetPlatform == TargetPlatform.windows;

  @override
  void initState() {
    super.initState();
    if (_supported) unawaited(_initialize());
  }

  Future<void> _initialize() async {
    try {
      final port = await (_environment ??= _initializeEnvironment());
      if (!mounted) return;
      final controller = WebviewController();
      _controller = controller;
      await controller.initialize();
      if (!mounted) {
        await _disposeController();
        return;
      }
      await controller.setPopupWindowPolicy(WebviewPopupWindowPolicy.deny);
      _subscriptions.add(
        controller.url.listen((url) {
          if (mounted) {
            setState(() => _url = url.startsWith('about:blank') ? '' : url);
          }
        }),
      );
      _subscriptions.add(
        controller.loadingState.listen((state) {
          if (mounted) {
            setState(() {
              _loading = state == LoadingState.loading;
              if (_loading) _navigationError = null;
            });
          }
        }),
      );
      _subscriptions.add(
        controller.onLoadError.listen((error) {
          if (mounted) {
            setState(
              () => _navigationError =
                  'The browser could not load this page (${error.name}).',
            );
          }
        }),
      );
      await controller.loadUrl('about:blank#digitalbrain-$_session');
      if (!mounted) return;
      setState(() => _ready = true);
      _connected = true;
      await widget.onConnect?.call(port, _session);
      if (!mounted) await widget.onDisconnect?.call(_session);
    } catch (error) {
      if (mounted) {
        setState(
          () => _error =
              'Browser could not start. Check the WebView2 runtime and restart this window.\n$error',
        );
      }
    }
  }

  @override
  void dispose() {
    for (final subscription in _subscriptions) {
      unawaited(subscription.cancel());
    }
    if (_connected) unawaited(_disconnect());
    unawaited(_disposeController());
    super.dispose();
  }

  Future<void> _disposeController() async {
    final controller = _controller;
    if (!_controllerDisposed &&
        controller != null &&
        controller.value.isInitialized) {
      _controllerDisposed = true;
      await controller.dispose();
    }
  }

  Future<void> _disconnect() async {
    try {
      await widget.onDisconnect?.call(_session);
    } catch (_) {
      /* The native page is also closed below. */
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!_supported) {
      return const Center(
        child: Text('Live research requires the Windows desktop app.'),
      );
    }
    if (_error != null) return Center(child: SelectableText(_error!));
    if (!_ready) return const Center(child: CircularProgressIndicator());
    return Column(
      children: [
        if (_loading) const LinearProgressIndicator(),
        if (_url.isNotEmpty)
          Padding(
            padding: const EdgeInsets.all(6),
            child: Text(_url, maxLines: 1, overflow: TextOverflow.ellipsis),
          ),
        Expanded(
          child: Stack(
            fit: StackFit.expand,
            children: [
              Webview(
                _controller!,
                permissionRequested: (_, _, _) =>
                    WebviewPermissionDecision.deny,
              ),
              if (_url.isEmpty || _navigationError != null)
                ColoredBox(
                  color: Theme.of(context).colorScheme.surface,
                  child: Center(
                    child: Text(
                      _navigationError ?? 'Enter a company and choose Research to start browsing.',
                    ),
                  ),
                ),
            ],
          ),
        ),
      ],
    );
  }
}
