import 'dart:async';
import 'dart:convert';
import 'dart:io' show Platform;
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart' show kIsWeb;

import 'ui/ui_widget.dart';
import 'ui/theme.dart';
import 'services/surface_stream_connection.dart';
import 'package:modular_ui/modular_ui.dart' as mu;
import 'package:getwidget/getwidget.dart';

void main() {
  // Wrap in MaterialApp so GFButton / InkWell / gestures / Theme have proper ancestors.
  // Without it, taps can be non-responsive on web + Windows desktop (hit testing, pressed states, material ink).
  // Tests wrapped it; real runApp did not. This makes any button responsive across platforms.
  runApp(
    const MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'DigitalBrain',
      home: DigitalBrainSurfaceViewer(),
    ),
  );
}

class DigitalBrainSurfaceViewer extends StatefulWidget {
  const DigitalBrainSurfaceViewer({super.key});

  @override
  State<DigitalBrainSurfaceViewer> createState() => _DigitalBrainSurfaceViewerState();
}

class _DigitalBrainSurfaceViewerState extends State<DigitalBrainSurfaceViewer> {
  final SurfaceStreamConnection _conn = SurfaceStreamConnection();
  final Map<String, UiWidget> _surfaces = <String, UiWidget>{};
  bool _isConnected = false;
  String _username = 'root';
  String _grpcHost = 'localhost';
  int _grpcPort = 8080;
  UiWidget? _demoShell;
  Timer? _demoTimer;
  bool _showDesignGallery = false;
  // Client-side tracking for pinned main surface (from PinSurface to 'main' region in shell chrome nav from YAML declarative).
  // Allows immediate "navigation" to marketplace view by injecting the actual _surfaces[ id ] (listings content) into the main pane of the rendered chrome.
  // Complements server placement (ShellNeuron) and any future composed ui-shell emission from the shell.yaml rule.
  String? _pinnedMainSurfaceId;
  UiWidget? _demoReaction;

  @override
  void initState() {
    super.initState();
    final env = kIsWeb ? const <String, String>{} : Platform.environment;
    _grpcHost = env['KERNEL_GRPC_HOST'] ?? const String.fromEnvironment('KERNEL_GRPC_HOST', defaultValue: 'localhost');
    final portStr = env['KERNEL_GRPC_PORT'] ?? const String.fromEnvironment('KERNEL_GRPC_PORT', defaultValue: '8080');
    _username = env['KERNEL_GRPC_USER'] ?? const String.fromEnvironment('KERNEL_GRPC_USER', defaultValue: 'root');

    _grpcPort = int.tryParse(portStr) ?? 8080;
    _connectInternal(_grpcHost, _grpcPort, brainId: 'main');

    // After short delay with no real ui-shell (common for web :5801 due to gRPC-web address or explicit-start timing),
    // populate a rich local demo shell so the spinner stops and user sees the declarative glass OS nav + regions immediately.
    // Real surfaces from kernel (when address matches) will override via _onSurfaceMessage.
    _demoTimer = Timer(const Duration(milliseconds: 2200), _maybeShowDemoShell);
  }

  void _maybeShowDemoShell() {
    if (!mounted || _surfaces.containsKey('ui-shell')) return;
    setState(() {
      _demoShell = _buildDemoOsShell();
    });
  }

  UiWidget _buildDemoOsShell() {
    // Mirrors the structure declared in os/shell.ino (header, nav sidebar, main region, widgets dock) using the kit.
    // Buttons use PinSurface-shaped maps; when connected they will fire for real, otherwise demo only.
    final header = UiRow(children: [
      const UiIcon(name: 'widgets'),
      const UiText(value: 'DigitalBrain OS'),
      const UiDivider(),
      UiButton(label: '🔍 Search', onTap: {'Type': 'ListPublished'}),
      UiButton(label: '⎋ Sign out', onTap: {'Type': 'BeginGoogleAuth'}),
    ]);

    final navButtons = <UiWidget>[
      const UiText(value: '— VIEWS —'),
      UiButton(label: '🏠 Home', onTap: {'Type': 'PinSurface', 'SurfaceId': 'marketplace', 'Region': 'main', 'Order': 0}),
      UiButton(label: '✅ Tasks', onTap: {'Type': 'PinSurface', 'SurfaceId': 'kerneltasks', 'Region': 'main', 'Order': 1}),
      UiButton(label: '🛒 Marketplace', onTap: {'Type': 'PinSurface', 'SurfaceId': 'marketplace', 'Region': 'main', 'Order': 2}),
      UiButton(label: '✨ Creator', onTap: {'Type': 'PinSurface', 'SurfaceId': 'ui-def-creator', 'Region': 'main', 'Order': 3}),
      UiButton(label: '📧 Mail', onTap: {'Type': 'OpenWindow', 'SurfaceId': 'gmail-senders-chart', 'Title': '📧 Mail', 'X': 80, 'Y': 80, 'Width': 540, 'Height': 380}),
      UiButton(label: '🌤️ Weather', onTap: {'Type': 'PinSurface', 'SurfaceId': 'weather', 'Region': 'widgets', 'Order': 5}),
      UiButton(label: '🔐 Auth', onTap: {'Type': 'PinSurface', 'SurfaceId': 'ui-def-google-auth', 'Region': 'main', 'Order': 6}),
    ];
    final sidebar = UiContainer(
      padding: 8,
      decoration: 'glass',
      child: UiColumn(children: navButtons),
    );

    final mainArea = UiContainer(
      padding: 8,
      decoration: 'glass',
      child: UiColumn(children: [
        const UiText(value: '— MAIN WORKSPACE —'),
        UiCard(
          title: 'Welcome to Liquid Glass',
          body: UiColumn(children: [
            UiButton(label: '▶ DEMO', onTap: {'Type': 'Demo'}),
            const UiText(value: 'Declarative shell and regions live from shell.ino'),
            UiText(value: 'Pin experiences from nav to populate this area.'),
            UiText(value: 'Use Design System button for the supported kit catalog.'),
          ]),
        ),
        if (_demoReaction != null) _demoReaction!,
      ]),
    );

    final widgetsDock = UiContainer(
      padding: 8,
      decoration: 'glass',
      child: UiColumn(children: [
        const UiText(value: '— WIDGETS DOCK —'),
        UiCard(title: 'Dock', body: const UiText(value: 'Pinned widgets and live surfaces appear here')),
      ]),
    );

    final contentRow = UiRow(children: [sidebar, mainArea, widgetsDock]);

    final status = const UiText(value: 'Liquid Glass OS • declarative from shell.ino • web gRPC may need matching kernel port');

    return UiColumn(children: [
      UiContainer(padding: 8, decoration: 'glass', child: header),
      contentRow,
      UiContainer(padding: 4, child: status),
    ]);
  }

  Widget _buildDesignGallery(BuildContext context, void Function(Map<String, dynamic>) onFire) {
    final tokens = Theme.of(context).extension<LiquidGlassTokens>() ?? LiquidGlassTokens.fallback;
    return Container(
      color: tokens.backgroundColor,
      child: SingleChildScrollView(
        padding: EdgeInsets.all(tokens.spacingMedium),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Text('UI Kit & Design System', style: TextStyle(color: tokens.textColor, fontSize: 24, fontWeight: FontWeight.bold)),
                const Spacer(),
                IconButton(
                  icon: Icon(Icons.close, color: tokens.primaryColor),
                  onPressed: () => setState(() => _showDesignGallery = false),
                ),
              ],
            ),
            SizedBox(height: tokens.spacingMedium),
            _buildGallerySection('Buttons (from modular_ui kit)', [
              mu.MUIPrimaryButton(text: 'Primary Action', onPressed: () {}),
              // secondary etc if available
            ], tokens),
            _buildGallerySection('Cards (clean modern)', [
              Container(
                decoration: BoxDecoration(
                  color: tokens.cardColor.withOpacity(tokens.backgroundOpacity),
                  borderRadius: BorderRadius.circular(tokens.borderRadiusLarge),
                ),
                padding: EdgeInsets.all(tokens.spacingMedium),
                child: Text('Example Card - Declarative from .ino + tokens'),
              ),
            ], tokens),
            _buildGallerySection('From .ino (live declarative)', [
              buildFromUiWidget(UiButton(label: 'Demo .ino Button', onTap: {'Type': 'DemoTap'}), context: context, onFire: onFire),
              buildFromUiWidget(UiCard(title: 'Demo .ino Card', body: UiText(value: 'Rendered via shell.ino rule')), context: context, onFire: onFire),
            ], tokens),
            _buildGallerySection('Tabs, Lists from getwidget kit', [
              Container(
                padding: EdgeInsets.all(tokens.spacingSmall),
                child: Text('Tabs from getwidget (GFTabBar requires TabController in full integration)'),
              ),
              const SizedBox(height: 8),
              GFListTile(
                title: const Text('List item for tasks/mail - Rich declarative lists'),
                icon: Icon(Icons.list, color: tokens.primaryColor),
              ),
            ], tokens),
            _buildGallerySection('Chat preview (bubbles with tokens)', [
              Container(
                padding: EdgeInsets.all(tokens.spacingSmall),
                decoration: BoxDecoration(
                  color: tokens.cardColor.withOpacity(tokens.backgroundOpacity),
                  borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
                ),
                child: Column(
                  children: [
                    Align(
                      alignment: Alignment.centerLeft,
                      child: Container(
                        padding: const EdgeInsets.all(8),
                        decoration: BoxDecoration(
                          color: tokens.buttonColor,
                          borderRadius: BorderRadius.circular(tokens.borderRadiusSmall),
                        ),
                        child: Text('User: Hello from .ino!', style: TextStyle(color: tokens.textColor)),
                      ),
                    ),
                    const SizedBox(height: 4),
                    Align(
                      alignment: Alignment.centerRight,
                      child: Container(
                        padding: const EdgeInsets.all(8),
                        decoration: BoxDecoration(
                          color: tokens.primaryColor,
                          borderRadius: BorderRadius.circular(tokens.borderRadiusSmall),
                        ),
                        child: Text('AI: Response here', style: const TextStyle(color: Colors.white)),
                      ),
                    ),
                  ],
                ),
              ),
            ], tokens),
            _buildGallerySection('Lists & Tiles (getwidget for rich declarative)', [
              GFListTile(
                title: const Text('Task item from .ino - With icon, status'),
                icon: Icon(Icons.check_circle, color: tokens.primaryColor),
              ),
              GFListTile(
                title: const Text('Mail sender example - Declarative list support'),
                icon: Icon(Icons.mail, color: tokens.primaryColor),
              ),
            ], tokens),
            _buildGallerySection('Loaders, Ratings, Avatars (kit polish)', [
              GFLoader(type: GFLoaderType.circle),
              GFRating(
                value: 4.5,
                onChanged: (_) {},
                size: 20,
                color: tokens.primaryColor,
              ),
              GFAvatar(
                backgroundColor: tokens.primaryColor,
                child: const Icon(Icons.person, color: Colors.white),
              ),
            ], tokens),
            _buildGallerySection('Progress & Toggles (enhanced with tokens)', [
              GFProgressBar(
                percentage: 0.75,
                backgroundColor: Colors.white.withOpacity(0.1),
                progressBarColor: tokens.primaryColor,
              ),
              GFToggle(
                onChanged: (_) {},
                value: true,
                enabledTrackColor: tokens.primaryColor.withOpacity(0.3),
                enabledThumbColor: tokens.primaryColor,
              ),
            ], tokens),
            Text(
              'Clean, production-grade UI kit gallery. Components from getwidget/modular_ui + custom LiquidGlassTokens for .ino driven surfaces. Fluid responsive. Matches BrainOS style catalog.',
              style: TextStyle(color: tokens.textColor.withOpacity(0.7), fontSize: 12),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildGallerySection(String title, List<Widget> children, LiquidGlassTokens tokens) {
    return Padding(
      padding: EdgeInsets.only(bottom: tokens.spacingMedium),
      child: Container(
        decoration: BoxDecoration(
          color: tokens.cardColor.withOpacity(tokens.backgroundOpacity),
          borderRadius: BorderRadius.circular(tokens.borderRadiusLarge),
          border: Border.all(color: Colors.white.withOpacity(tokens.borderOpacity)),
        ),
        padding: EdgeInsets.all(tokens.spacingMedium),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: TextStyle(color: tokens.primaryColor, fontWeight: FontWeight.w600)),
            SizedBox(height: tokens.spacingSmall),
            Wrap(
              spacing: tokens.spacingSmall,
              runSpacing: tokens.spacingSmall,
              children: children,
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _connectInternal(String host, int port, {String brainId = 'main'}) async {
    try {
      await _conn.connect(host, port, _username, brainId: brainId);
      setState(() => _isConnected = true);
      _conn.messages.listen(_onSurfaceMessage, onError: (_) {});

      // Pre-populate the marketplace surface on connect (via ListPublished) so that
      // the dynamic YAML marketplace nav button can immediately show the content
      // (via client override or server re-emit of ui-shell with resolved main region)
      // instead of placeholder. Complements the on-click trigger.
      _conn.sendClientTap('ui-shell', {'Type': 'ListPublished'}, _username).catchError((_) {});
    } catch (_) {
      // Silent (original behavior). The 2.2s demo timer below will still populate a rich glass OS UI so the spinner does not stay forever.
    }
  }

  void _onSurfaceMessage(msg) {
    try {
      final json = jsonDecode(msg.widgetJson) as Map<String, dynamic>;
      final w = UiWidget.fromJson(json);
      setState(() {
        _surfaces[msg.surfaceId] = w;
        if (msg.surfaceId == "demo-executed") {
          _demoReaction = w; // ensure server-emitted Demo Executed card is visible in the main workspace area
        }
        // If we had a demo, a real surface overrides it.
        if (msg.surfaceId == 'ui-shell') _demoShell = null;
      });
    } catch (_) {}
  }

  void _fire(Map<String, dynamic> onTapJson) {
    // Special DEMO path for headline E2E: press in browser (5801) always surfaces a reaction card locally.
    // When connected also sends ClientTap so server timeline/logs see it.
    final tapType = (onTapJson['Type']?.toString() ?? onTapJson['type']?.toString() ?? '');
    if (tapType.contains('Demo')) {
      final now = DateTime.now().toIso8601String().substring(11, 19);
      setState(() {
        _demoReaction = UiCard(
          title: 'Demo Executed',
          body: UiColumn(children: [
            UiText(value: 'Time: $now'),
            UiText(value: 'Browser DEMO press → card surfaced. Update visible.'),
          ]),
        );
      });
      if (_isConnected) {
        _conn.sendClientTap('demo', {'Type': 'Demo'}, _username).catchError((_) {});
      }
      return;
    }

    // Normalize yaml action buttons { type: "PinSurface", args: { SurfaceId: ..., Region: ... } } (from os-on-yaml/shell.yaml etc.)
    // to flat map like demo hard-coded { 'Type': ..., 'SurfaceId': ..., ... } so client logic + server reconstruction (PinSurface etc in DigitalBrainGrain) find the keys at top level.
    if (onTapJson['args'] is Map<String, dynamic>) {
      final args = Map<String, dynamic>.from(onTapJson['args'] as Map);
      onTapJson = {
        'Type': onTapJson['type'] ?? onTapJson['Type'] ?? '',
        ...args,
      };
    }

    final surfaceIdForCheck = onTapJson['SurfaceId']?.toString() ?? onTapJson['surfaceId']?.toString() ?? '';
    if (surfaceIdForCheck == 'ui-kit') {
      setState(() => _showDesignGallery = true);
      return;
    }
    // Track PinSurface to main region for client-side main content injection (makes YAML chrome nav "clickable" and navigates the view).
    // The payload from shell.yaml button action has the SurfaceId (target) and Region.
    final type = onTapJson['Type']?.toString() ?? onTapJson['type']?.toString() ?? '';
    if (type.contains('PinSurface')) {
      final region = onTapJson['Region']?.toString() ?? onTapJson['region']?.toString() ?? 'main';
      if (region == 'main') {
        final sid = onTapJson['SurfaceId']?.toString() ?? onTapJson['surfaceId']?.toString();
        if (sid != null && sid != _pinnedMainSurfaceId) {
          setState(() => _pinnedMainSurfaceId = sid);
        }
      }
    }
    if (_isConnected) {
      // Determine ClientTap surfaceId (origin of the tap) correctly:
      // - Shell chrome actions (PinSurface nav from sidebar, OpenWindow etc.): force 'ui-shell'.
      //   Payload 'SurfaceId' for Pin is the *target* marketplace, not the tap source. This makes the nav click send PinSurface with correct ClientTap sid='ui-shell'.
      //   Server (ShellNeuron) then does ApplyPlacement to "main" + OpenWindowInternal for marketplace, emits WorkspaceChanged + updated ui-shell (with content in main pane).
      //   Client receives the surface msg and shows the marketplace view (or window) as expected.
      // - Content actions inside marketplace surface (InstallFromMarketplace buttons): 'marketplace'.
      // - Else prefer payload or 'ui-shell'.
      // (Previous payload-prefer broke the marketplace *nav* button.)
      final type = onTapJson['Type']?.toString() ?? '';
      String sid;
      if (type.contains('PinSurface') || type.contains('OpenWindow') || type.contains('CloseWindow') ||
          type.contains('MoveResizeWindow') || type.contains('RaiseWindow') ||
          type.contains('ListPublished') || type.contains('BeginGoogleAuth')) {
        sid = 'ui-shell';  // chrome nav / system
      } else if (type.contains('InstallFromMarketplace')) {
        sid = 'marketplace';
      } else {
        sid = (onTapJson['SurfaceId'] as String?) ?? 'ui-shell';
      }
      _conn.sendClientTap(sid, onTapJson, _username).catchError((_) {});

      // When the YAML dynamic nav button for marketplace is clicked (PinSurface with SurfaceId=marketplace, sent as ui-shell),
      // also fire ListPublished. This ensures MarketplaceNeuron (re)emits the UiSurface("marketplace", listings card)
      // so it arrives via the gRPC stream (or replay), lands in _surfaces, and the client main view (pinned override
      // or server-resolved region in the next ui-shell) actually shows the marketplace content instead of placeholder.
      // Fixes "click marketplace nothing happens" for the dynamic-from-.yaml buttons.
      if (sid == 'ui-shell' && (onTapJson['SurfaceId']?.toString() == 'marketplace' || onTapJson['surfaceId']?.toString() == 'marketplace')) {
        _conn.sendClientTap('ui-shell', {'Type': 'ListPublished'}, _username).catchError((_) {});
      }
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: const Text(
            'Cannot fire action: Client disconnected from DigitalBrain kernel.',
          ),
          backgroundColor: Theme.of(context).extension<LiquidGlassTokens>()?.cardColor.withOpacity(0.9) ?? Colors.grey[900],
          behavior: SnackBarBehavior.floating,
          duration: const Duration(seconds: 2),
        ),
      );
    }
  }

  @override
  void dispose() {
    _demoTimer?.cancel();
    _conn.disconnect();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final shellWidget = _surfaces['ui-shell'];
    final effectiveShell = shellWidget ?? _demoShell;
    final tokens = Theme.of(context).extension<LiquidGlassTokens>() ?? LiquidGlassTokens.fallback;

    if (_showDesignGallery) {
      return _buildDesignGallery(context, _fire);
    }

    Widget viewportWidget;
    if (effectiveShell != null) {
      var contentForRender = effectiveShell;
      // For the ui-shell surface (declared via "show card "Liquid Glass Desktop", column(...)" in shell.ino), unwrap the outer Card body.
      // This ensures the declarative chrome (top header container + 3-panel row with nav/main/widgets + status) renders full-screen as a real desktop OS shell.
      // The Card wrapper from the show-card grammar is kept in .ino for rule compatibility but stripped here for the shell case so it is not boxed in GFCard visuals.
      // The _buildDemoOsShell already produces a bare UiColumn, so it is unaffected. Matches thinned pure-renderer intent + special shell handling.
      if (contentForRender is UiCard) {
        contentForRender = contentForRender.body;
      }
      final mainOverride = (_pinnedMainSurfaceId != null && _surfaces.containsKey(_pinnedMainSurfaceId))
          ? _surfaces[_pinnedMainSurfaceId]
          : null;
      viewportWidget = buildFromUiWidget(contentForRender, context: context, onFire: _fire, mainContentOverride: mainOverride);
    } else {
      viewportWidget = Container(
        color: tokens.backgroundColor,
        child: Center(
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              CircularProgressIndicator(color: tokens.primaryColor),
              const SizedBox(height: 16),
              Text(
                'Loading declarative shell from .ino...',
                style: TextStyle(color: tokens.textColor, fontSize: 14, decoration: TextDecoration.none),
              ),
              const SizedBox(height: 8),
              Text(
                'gRPC target: $_grpcHost:$_grpcPort (web :5801 often needs Aspire flutter-web start for correct port + grpc-web)',
                style: const TextStyle(color: Colors.white54, fontSize: 11, decoration: TextDecoration.none),
                textAlign: TextAlign.center,
              ),
            ],
          ),
        ),
      );
    }
    final viewport = viewportWidget;

    final List<Widget> windowWidgets = [];
    final windowsSurface = _surfaces['ui-windows'];
    if (windowsSurface is UiColumn) {
      for (final child in windowsSurface.children) {
        if (child is UiWindowFrame) {
          windowWidgets.add(
            FloatingWindow(
              key: ValueKey(child.windowId),
              frame: child,
              onFire: _fire,
            ),
          );
        }
      }
    }

    final designTokens = LiquidGlassTokens.fallback;
    return MaterialApp(
      title: 'DigitalBrain',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        brightness: Brightness.dark,
        scaffoldBackgroundColor: designTokens.backgroundColor,
        colorScheme: ColorScheme.dark(
          primary: designTokens.primaryColor,
          secondary: designTokens.secondaryColor,
          surface: designTokens.cardColor,
          onSurface: designTokens.textColor,
        ),
        useMaterial3: true,
        extensions: [designTokens],
      ),
      home: Scaffold(
        body: SafeArea(
          child: Stack(
            children: [
              Positioned.fill(child: viewport),
              ...windowWidgets,
            ],
          ),
        ),
      ),
    );
  }
}

class FloatingWindow extends StatefulWidget {
  final UiWindowFrame frame;
  final void Function(Map<String, dynamic> synapseJson) onFire;

  const FloatingWindow({
    super.key,
    required this.frame,
    required this.onFire,
  });

  @override
  State<FloatingWindow> createState() => _FloatingWindowState();
}

class _FloatingWindowState extends State<FloatingWindow> {
  late double _x;
  late double _y;
  late double _width;
  late double _height;

  @override
  void initState() {
    super.initState();
    _x = widget.frame.x;
    _y = widget.frame.y;
    _width = widget.frame.width;
    _height = widget.frame.height;
  }

  @override
  void didUpdateWidget(FloatingWindow oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.frame.x != widget.frame.x || oldWidget.frame.y != widget.frame.y ||
        oldWidget.frame.width != widget.frame.width || oldWidget.frame.height != widget.frame.height) {
      _x = widget.frame.x;
      _y = widget.frame.y;
      _width = widget.frame.width;
      _height = widget.frame.height;
    }
  }

  void _onDragUpdate(DragUpdateDetails details) {
    setState(() {
      _x += details.delta.dx;
      _y += details.delta.dy;
    });
  }

  void _onDragEnd(DragEndDetails details) {
    widget.onFire({
      'Type': 'MoveResizeWindow',
      'WindowId': widget.frame.windowId,
      'X': _x,
      'Y': _y,
      'Width': _width,
      'Height': _height,
    });
  }

  void _onResizeUpdate(DragUpdateDetails details) {
    setState(() {
      _width = (_width + details.delta.dx).clamp(200.0, 1400.0);
      _height = (_height + details.delta.dy).clamp(150.0, 1000.0);
    });
  }

  void _onResizeEnd(DragEndDetails details) {
    widget.onFire({
      'Type': 'MoveResizeWindow',
      'WindowId': widget.frame.windowId,
      'X': _x,
      'Y': _y,
      'Width': _width,
      'Height': _height,
    });
  }

  void _raiseWindow() {
    widget.onFire({
      'Type': 'RaiseWindow',
      'WindowId': widget.frame.windowId,
    });
  }

  @override
  Widget build(BuildContext context) {
    final tokens = Theme.of(context).extension<LiquidGlassTokens>() ?? LiquidGlassTokens.fallback;
    final titleBarBackground = tokens.cardColor.withOpacity(tokens.backgroundOpacity * 0.5);
    final closeIconColor = tokens.textColor.withOpacity(0.7);
    return Positioned(
      left: _x,
      top: _y,
      child: GestureDetector(
        onTapDown: (_) => _raiseWindow(),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: tokens.blurSigma, sigmaY: tokens.blurSigma),
            child: Container(
              width: _width,
              height: _height,
              decoration: BoxDecoration(
                color: tokens.cardColor.withOpacity(tokens.backgroundOpacity),
                borderRadius: BorderRadius.circular(tokens.borderRadiusMedium),
                border: Border.all(color: tokens.primaryColor.withOpacity(0.3), width: 1.5),
                boxShadow: const [
                  BoxShadow(color: Colors.black54, blurRadius: 12, offset: Offset(2, 4)),
                ],
              ),
              child: Stack(
                children: [
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      GestureDetector(
                        onPanUpdate: _onDragUpdate,
                        onPanEnd: _onDragEnd,
                        onPanDown: (_) => _raiseWindow(),
                        child: Container(
                          height: 36,
                          padding: const EdgeInsets.symmetric(horizontal: 10),
                          color: titleBarBackground,
                          child: Row(
                            children: [
                              Icon(Icons.drag_indicator, color: tokens.primaryColor, size: 16),
                              const SizedBox(width: 8),
                              Expanded(
                                child: Text(
                                  widget.frame.title,
                                  style: TextStyle(
                                    fontSize: 13,
                                    fontWeight: FontWeight.bold,
                                    color: tokens.textColor,
                                    decoration: TextDecoration.none,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ),
                              ),
                              IconButton(
                                icon: Icon(Icons.close, color: closeIconColor, size: 16),
                                padding: EdgeInsets.zero,
                                constraints: const BoxConstraints(),
                                onPressed: () {
                                  widget.onFire({
                                    'Type': 'CloseWindow',
                                    'WindowId': widget.frame.windowId,
                                  });
                                },
                              ),
                            ],
                          ),
                        ),
                      ),
                      Expanded(
                        child: Container(
                          color: Colors.transparent,
                          child: SingleChildScrollView(
                            padding: const EdgeInsets.all(12),
                            child: buildFromUiWidget(widget.frame.content, context: context, onFire: widget.onFire),
                          ),
                        ),
                      ),
                    ],
                  ),
                  Positioned(
                    right: 4,
                    bottom: 4,
                    child: GestureDetector(
                      onPanUpdate: _onResizeUpdate,
                      onPanEnd: _onResizeEnd,
                      onPanDown: (_) => _raiseWindow(),
                      child: Container(
                        width: 22,
                        height: 22,
                        decoration: BoxDecoration(
                          color: tokens.primaryColor.withOpacity(0.15),
                          borderRadius: BorderRadius.circular(4),
                        ),
                        child: Icon(Icons.drag_indicator, color: tokens.primaryColor, size: 14),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}