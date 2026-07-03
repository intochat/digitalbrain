import 'package:flutter/material.dart';

import 'package:digitalbrain_flutter/features/canvas/panel/canvas_panel.dart';
import 'package:digitalbrain_flutter/features/canvas/panel/orbit_session_badge.dart';
import 'package:digitalbrain_flutter/features/canvas/panel/panel_manager.dart';
import 'package:digitalbrain_flutter/features/canvas/panel/ui_layout_bridge.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';

/// The generic, neuron-agnostic window chrome (W-1). One full-bleed layer that
/// paints every [CanvasPanel] as a draggable/resizable/minimizable floating
/// panel, a dock strip for minimized panels, and the one-click auto-layout
/// control. The panel BODY is the existing RFW renderer — unchanged.
class FloatingPanelLayer extends StatelessWidget {
  const FloatingPanelLayer({
    super.key,
    required this.manager,
    required this.host,
    required this.onPanelEvent,
  });

  final PanelManager manager;
  final RfwRuntimeHost host;

  /// A panel's RFW `event "name" { args }` bubbled up with the owning panel id
  /// so the host can fire the matching synapse back to the kernel.
  final void Function(String panelId, String event, Map<String, Object?> args)
  onPanelEvent;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        manager.setCanvasSize(constraints.biggest);
        return AnimatedBuilder(
          animation: manager,
          builder: (context, _) {
            final visible = manager.panels
                .where((p) => p.state == PanelState.normal)
                .toList();
            return Stack(
              children: [
                for (final panel in visible)
                  AnimatedPositioned(
                    key: ValueKey('panel-${panel.id}'),
                    duration: const Duration(milliseconds: 320),
                    curve: Curves.easeOutCubic,
                    left: panel.rect.left,
                    top: panel.rect.top,
                    width: panel.rect.width,
                    height: panel.rect.height,
                    child: _PanelFrame(
                      panel: panel,
                      host: host,
                      onRaise: () => manager.raise(panel.id),
                      onMove: (d) => manager.move(panel.id, d),
                      onResize: (d) => manager.resize(panel.id, d),
                      onMinimize: () => manager.toggleMinimize(panel.id),
                      onClose: () => manager.close(panel.id),
                      onEvent: (name, args) =>
                          onPanelEvent(panel.id, name, args),
                    ),
                  ),
                Positioned(
                  left: 0,
                  right: 0,
                  bottom: 0,
                  child: _DockStrip(manager: manager),
                ),
                Positioned(
                  right: 24,
                  bottom: 24,
                  child: _AutoLayoutButton(onTap: manager.autoLayout),
                ),
              ],
            );
          },
        );
      },
    );
  }
}

class _PanelFrame extends StatelessWidget {
  const _PanelFrame({
    required this.panel,
    required this.host,
    required this.onRaise,
    required this.onMove,
    required this.onResize,
    required this.onMinimize,
    required this.onClose,
    required this.onEvent,
  });

  final CanvasPanel panel;
  final RfwRuntimeHost host;
  final VoidCallback onRaise;
  final void Function(Offset delta) onMove;
  final void Function(Size delta) onResize;
  final VoidCallback onMinimize;
  final VoidCallback onClose;
  final void Function(String name, Map<String, Object?> args) onEvent;

  @override
  Widget build(BuildContext context) {
    return Listener(
      onPointerDown: (_) => onRaise(),
      child: Material(
        color: Colors.transparent,
        child: Container(
          decoration: BoxDecoration(
            color: const Color(0xFF101218).withValues(alpha: 0.94),
            borderRadius: BorderRadius.circular(16),
            border: Border.all(
              color: DigitalBrainColors.tealSoft.withValues(alpha: 0.28),
            ),
            boxShadow: [
              BoxShadow(
                color: Colors.black.withValues(alpha: 0.45),
                blurRadius: 24,
                offset: const Offset(0, 12),
              ),
            ],
          ),
          clipBehavior: Clip.antiAlias,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _titleBar(),
              Expanded(child: _body()),
            ],
          ),
        ),
      ),
    );
  }

  Widget _titleBar() {
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onPanStart: (_) => onRaise(),
      onPanUpdate: (d) => onMove(d.delta),
      child: Container(
        height: 34,
        padding: const EdgeInsets.symmetric(horizontal: 12),
        decoration: BoxDecoration(
          color: Colors.white.withValues(alpha: 0.04),
          border: Border(
            bottom: BorderSide(color: Colors.white.withValues(alpha: 0.06)),
          ),
        ),
        child: Row(
          children: [
            const OrbitSessionBadge(size: 20),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                panel.title,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                  color: Colors.white,
                  fontSize: 13,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
            _iconButton(Icons.remove, onMinimize, 'Minimize'),
            _iconButton(Icons.close, onClose, 'Close'),
          ],
        ),
      ),
    );
  }

  Widget _iconButton(IconData icon, VoidCallback onTap, String tip) {
    return Tooltip(
      message: tip,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(6),
        child: Padding(
          padding: const EdgeInsets.all(4),
          child: Icon(icon, size: 16, color: Colors.white70),
        ),
      ),
    );
  }

  Widget _body() {
    // A `uikit` card carries the compiled .ino `ui:` surface as a
    // {name, arguments, children} tree rather than inline RFW source; bridge it
    // into RFW source text the host renders (W-4, zero per-neuron Dart).
    var source = panel.surface.source;
    var rootWidget = panel.surface.rootWidget;
    if ((source == null || source.isEmpty) &&
        looksLikeUiLayout(panel.surface.data)) {
      source = uiLayoutToRfwSource(panel.surface.data);
      // The bridge always emits `widget root = …`; the kernel's card-level
      // RootWidget ("UiKit") names the source dialect, not the widget — using it
      // here would miss `widget root` and fall back to a grey error box.
      rootWidget = 'root';
    }
    if (source == null || source.isEmpty) {
      return const Center(
        child: Text('No surface', style: TextStyle(color: Colors.white38)),
      );
    }
    host.ensureLoaded(panel.id, source);
    final err = host.parseError(panel.id);
    return Stack(
      children: [
        Positioned.fill(
          child: err != null
              ? Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text(
                    'Surface error: $err',
                    style: const TextStyle(
                      color: Colors.redAccent,
                      fontSize: 12,
                    ),
                  ),
                )
              : host.render(
                  panel.id,
                  data: panel.surface.data,
                  rootWidget: rootWidget,
                  onEvent: onEvent,
                  semanticsId: panel.id,
                  semanticsLabel: panel.id,
                ),
        ),
        // Bottom-right resize grip — dedicated handle avoids gesture conflicts
        // with scrollables/buttons inside the RFW surface.
        Positioned(
          right: 0,
          bottom: 0,
          child: GestureDetector(
            behavior: HitTestBehavior.opaque,
            onPanUpdate: (d) => onResize(Size(d.delta.dx, d.delta.dy)),
            child: MouseRegion(
              cursor: SystemMouseCursors.resizeDownRight,
              child: Padding(
                padding: const EdgeInsets.all(4),
                child: Icon(
                  Icons.south_east,
                  size: 14,
                  color: Colors.white.withValues(alpha: 0.35),
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _DockStrip extends StatelessWidget {
  const _DockStrip({required this.manager});

  final PanelManager manager;

  @override
  Widget build(BuildContext context) {
    final docked = manager.minimized;
    if (docked.isEmpty) return const SizedBox.shrink();
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      child: Wrap(
        spacing: 8,
        children: [
          for (final p in docked)
            ActionChip(
              avatar: Icon(
                Icons.crop_square,
                size: 14,
                color: DigitalBrainColors.tealSoft,
              ),
              label: Text(p.title, style: const TextStyle(fontSize: 12)),
              backgroundColor: const Color(0xFF161922),
              onPressed: () => manager.toggleMinimize(p.id),
            ),
        ],
      ),
    );
  }
}

class _AutoLayoutButton extends StatelessWidget {
  const _AutoLayoutButton({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return FloatingActionButton.extended(
      heroTag: 'canvas-auto-layout',
      backgroundColor: const Color(0xFF161922),
      foregroundColor: DigitalBrainColors.tealSoft,
      onPressed: onTap,
      icon: const Icon(Icons.grid_view_rounded, size: 18),
      label: const Text('Tidy'),
    );
  }
}
