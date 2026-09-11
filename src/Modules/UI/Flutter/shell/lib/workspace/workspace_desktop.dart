import 'dart:math' as math;

import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';

import 'workspace_store.dart';

/// Editors keep their identity as placement and stacking order change.
class WorkspaceDesktop extends StatefulWidget {
  const WorkspaceDesktop({
    super.key,
    required this.store,
    required this.editorBuilder,
  });
  final WorkspaceStore store;
  final Widget Function(WorkspaceArtifact) editorBuilder;
  @override
  State<WorkspaceDesktop> createState() => _WorkspaceDesktopState();
}

Rect workspaceWindowRect(
  Size area,
  String mode,
  List<double>? bounds,
  int index,
) {
  if (area.width < 600 || mode == 'maximized') return Offset.zero & area;
  if (mode == 'left') return Rect.fromLTWH(0, 0, area.width / 2, area.height);
  if (mode == 'right') {
    return Rect.fromLTWH(area.width / 2, 0, area.width / 2, area.height);
  }
  final b =
      bounds ??
      [
        24.0 + index * 24,
        24.0 + index * 24,
        area.width * .72,
        area.height * .78,
      ];
  final width = b[2].clamp(math.min(280, area.width), area.width).toDouble();
  final height = b[3].clamp(math.min(220, area.height), area.height).toDouble();
  return Rect.fromLTWH(
    b[0].clamp(0, area.width - width).toDouble(),
    b[1].clamp(0, area.height - height).toDouble(),
    width,
    height,
  );
}

class _WorkspaceDesktopState extends State<WorkspaceDesktop> {
  final _surface = GlobalKey();
  String? _dragging, _snap;
  bool _resizing = false;
  WorkspaceStore get store => widget.store;

  void _toggle(String id) {
    if (store.currentProject.presentation.modeOf(id) == 'maximized') {
      store.restoreWindow(id);
    } else {
      store.maximizeWindow(id);
    }
  }

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final area = Size(constraints.maxWidth, constraints.maxHeight);
      final layout = store.currentProject.presentation;
      final colors = Theme.of(context).colorScheme;
      final active = layout.activeArtifactId;
      final maximized = active != null && layout.modeOf(active) == 'maximized';
      final compact = area.width < 600;
      return Stack(
        key: _surface,
        fit: StackFit.expand,
        children: [
          if (active == null)
            Center(
              child: Padding(
                padding: const EdgeInsets.all(28),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(
                      Icons.space_dashboard_outlined,
                      size: 32,
                      color: colors.onSurfaceVariant,
                    ),
                    const SizedBox(height: 20),
                    Text(
                      'Space for your work',
                      style: TextStyle(
                        fontSize: 23,
                        fontWeight: FontWeight.w400,
                        letterSpacing: -.5,
                        color: colors.onSurface,
                      ),
                    ),
                    const SizedBox(height: 12),
                    Text(
                      'Open something from Project files,\nor create it in your conversation.',
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        fontSize: 14,
                        height: 1.6,
                        color: colors.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          for (final id in layout.openArtifactIds)
            if (store.currentProject.artifacts.any((a) => a.id == id))
              _window(
                store.currentProject.artifacts.firstWhere((a) => a.id == id),
                store.currentProject.artifacts.indexWhere((a) => a.id == id),
                area,
                hidden:
                    layout.minimizedArtifactIds.contains(id) ||
                    ((maximized || compact) && id != active),
              ),
          if (_snap != null && _dragging != null)
            Positioned.fromRect(
              rect: workspaceWindowRect(area, _snap!, null, 0),
              child: IgnorePointer(
                child: Container(
                  margin: const EdgeInsets.all(6),
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: colors.primary.withValues(alpha: .14),
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: colors.primary, width: 2),
                  ),
                  child: Text(
                    _snap == 'maximized'
                        ? 'Maximize'
                        : _snap == 'left'
                        ? 'Snap left'
                        : 'Snap right',
                    style: TextStyle(
                      color: colors.onSurface,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
              ),
            ),
        ],
      );
    },
  );

  Widget _window(
    WorkspaceArtifact artifact,
    int index,
    Size area, {
    required bool hidden,
  }) {
    final id = artifact.id;
    final p = store.currentProject.presentation;
    final mode = p.modeOf(id);
    final rect = workspaceWindowRect(area, mode, p.windowBounds[id], index);
    final colors = Theme.of(context).colorScheme;
    final active = p.activeArtifactId == id;
    final narrow = area.width < 600;
    return AnimatedPositioned(
      key: ValueKey('window-$id'),
      duration:
          _dragging != null ||
              _resizing ||
              MediaQuery.disableAnimationsOf(context)
          ? Duration.zero
          : const Duration(milliseconds: 160),
      curve: Curves.easeOutCubic,
      left: rect.left,
      top: rect.top,
      width: rect.width,
      height: rect.height,
      child: Offstage(
        offstage: hidden,
        child: Listener(
          onPointerDown: (_) => store.focusWindow(id),
          child: Container(
            color: mode == 'floating' && !narrow
                ? Colors.transparent
                : colors.surface,
            padding: EdgeInsets.all(mode == 'floating' && !narrow ? 4 : 3),
            child: Material(
              color: colors.surfaceContainerLow,
              elevation: mode == 'floating' ? (active ? 8 : 3) : 0,
              shadowColor: colors.shadow.withValues(alpha: .25),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(10),
                side: BorderSide(
                  color: active
                      ? colors.primary.withValues(alpha: .4)
                      : colors.outlineVariant,
                ),
              ),
              clipBehavior: Clip.antiAlias,
              child: Stack(
                children: [
                  Column(
                    children: [
                      SizedBox(
                        height: 44,
                        child: Row(
                          children: [
                            Expanded(
                              child: MouseRegion(
                                cursor: narrow
                                    ? SystemMouseCursors.basic
                                    : SystemMouseCursors.move,
                                child: GestureDetector(
                                  key: ValueKey('window-drag-$id'),
                                  behavior: HitTestBehavior.opaque,
                                  onDoubleTap: narrow
                                      ? null
                                      : () => _toggle(id),
                                  onPanStart: narrow
                                      ? null
                                      : (_) {
                                          store.focusWindow(id);
                                          if (mode != 'floating') {
                                            store.snapWindow(id, 'floating');
                                            final restored =
                                                workspaceWindowRect(
                                                  area,
                                                  'floating',
                                                  p.windowBounds[id],
                                                  index,
                                                );
                                            store.setWindowBounds(id, [
                                              restored.left,
                                              restored.top,
                                              restored.width,
                                              restored.height,
                                            ]);
                                          }
                                          setState(() => _dragging = id);
                                        },
                                  onPanUpdate: narrow
                                      ? null
                                      : (details) {
                                          final current = workspaceWindowRect(
                                            area,
                                            'floating',
                                            p.windowBounds[id],
                                            index,
                                          );
                                          final moved = workspaceWindowRect(
                                            area,
                                            'floating',
                                            [
                                              current.left + details.delta.dx,
                                              current.top + details.delta.dy,
                                              current.width,
                                              current.height,
                                            ],
                                            index,
                                          );
                                          store.setWindowBounds(id, [
                                            moved.left,
                                            moved.top,
                                            moved.width,
                                            moved.height,
                                          ]);
                                          final box =
                                              _surface.currentContext!
                                                      .findRenderObject()
                                                  as RenderBox;
                                          final local = box.globalToLocal(
                                            details.globalPosition,
                                          );
                                          setState(
                                            () => _snap = local.dy < 24
                                                ? 'maximized'
                                                : local.dx < 28
                                                ? 'left'
                                                : local.dx > area.width - 28
                                                ? 'right'
                                                : null,
                                          );
                                        },
                                  onPanEnd: narrow
                                      ? null
                                      : (_) {
                                          if (_snap == 'maximized') {
                                            store.maximizeWindow(id);
                                          } else if (_snap != null) {
                                            store.snapWindow(id, _snap!);
                                          }
                                          setState(() {
                                            _dragging = null;
                                            _snap = null;
                                          });
                                        },
                                  onPanCancel: () => setState(() {
                                    _dragging = null;
                                    _snap = null;
                                  }),
                                  child: Padding(
                                    padding: const EdgeInsets.only(left: 12),
                                    child: Row(
                                      children: [
                                        Icon(
                                          uiArtifactIcon(artifact.kind),
                                          size: 17,
                                          color: colors.onSurfaceVariant,
                                        ),
                                        const SizedBox(width: 9),
                                        Expanded(
                                          child: Text(
                                            artifact.title,
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                            style: const TextStyle(
                                              fontSize: 13,
                                              fontWeight: FontWeight.w500,
                                            ),
                                          ),
                                        ),
                                      ],
                                    ),
                                  ),
                                ),
                              ),
                            ),
                            PopupMenuButton<String>(
                              tooltip: 'Window options',
                              iconSize: 17,
                              icon: const Icon(Icons.more_horiz),
                              onSelected: (value) {
                                if (value == 'attach') {
                                  store.attachArtifact(id);
                                } else {
                                  store.snapWindow(id, value);
                                }
                              },
                              itemBuilder: (_) => [
                                if (!narrow) ...[
                                  const PopupMenuItem(
                                    value: 'left',
                                    child: Text('Snap left'),
                                  ),
                                  const PopupMenuItem(
                                    value: 'right',
                                    child: Text('Snap right'),
                                  ),
                                ],
                                const PopupMenuItem(
                                  value: 'attach',
                                  child: Text('Attach to conversation'),
                                ),
                              ],
                            ),
                            _control(
                              'Minimize editor',
                              Icons.remove,
                              () => store.minimizeArtifact(id),
                            ),
                            if (!narrow)
                              _control(
                                mode == 'maximized'
                                    ? 'Restore window'
                                    : 'Maximize editor',
                                mode == 'maximized'
                                    ? Icons.filter_none
                                    : Icons.crop_square,
                                () => _toggle(id),
                              ),
                            _control(
                              'Close editor',
                              Icons.close,
                              () => store.closeArtifact(id),
                            ),
                            const SizedBox(width: 3),
                          ],
                        ),
                      ),
                      Divider(
                        height: 1,
                        color: colors.outlineVariant.withValues(alpha: .6),
                      ),
                      Expanded(child: widget.editorBuilder(artifact)),
                    ],
                  ),
                  if (mode == 'floating' && !narrow)
                    Positioned(
                      right: 0,
                      bottom: 0,
                      child: MouseRegion(
                        cursor: SystemMouseCursors.resizeUpLeftDownRight,
                        child: GestureDetector(
                          key: ValueKey('window-resize-$id'),
                          behavior: HitTestBehavior.opaque,
                          onPanStart: (_) => setState(() => _resizing = true),
                          onPanEnd: (_) => setState(() => _resizing = false),
                          onPanCancel: () => setState(() => _resizing = false),
                          onPanUpdate: (d) {
                            final current = workspaceWindowRect(
                              area,
                              mode,
                              p.windowBounds[id],
                              index,
                            );
                            store.setWindowBounds(id, [
                              current.left,
                              current.top,
                              (current.width + d.delta.dx).clamp(
                                280,
                                area.width - current.left,
                              ),
                              (current.height + d.delta.dy).clamp(
                                220,
                                area.height - current.top,
                              ),
                            ]);
                          },
                          child: SizedBox(
                            width: 24,
                            height: 24,
                            child: Icon(
                              Icons.south_east,
                              size: 12,
                              color: colors.onSurfaceVariant,
                            ),
                          ),
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

  Widget _control(String tooltip, IconData icon, VoidCallback onPressed) =>
      IconButton(
        tooltip: tooltip,
        onPressed: onPressed,
        icon: Icon(icon, size: 16),
        style: IconButton.styleFrom(
          minimumSize: const Size(32, 40),
          padding: const EdgeInsets.all(7),
          tapTargetSize: MaterialTapTargetSize.shrinkWrap,
        ),
      );
}
