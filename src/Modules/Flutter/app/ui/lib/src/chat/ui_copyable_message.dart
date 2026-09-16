import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../theme/ui_theme.dart';

/// Hosts that stream tokens expose current text through this scope.
final class UiStreamCopy extends InheritedNotifier<ChangeNotifier> {
  const UiStreamCopy({
    super.key,
    required ChangeNotifier super.notifier,
    required this.textFor,
    required super.child,
  });

  final String Function(String streamId) textFor;

  static String streamText(BuildContext context, String streamId) {
    final scope = context.dependOnInheritedWidgetOfExactType<UiStreamCopy>();
    return scope?.textFor(streamId) ?? '';
  }
}

/// Selectable message body plus a whole-message copy control.
final class UiCopyableMessage extends StatefulWidget {
  const UiCopyableMessage({
    super.key,
    required this.copyText,
    required this.child,
  });

  /// Resolved on build (button visibility) and again on tap (latest stream text).
  final String Function(BuildContext context) copyText;
  final Widget child;

  @override
  State<UiCopyableMessage> createState() => _UiCopyableMessageState();
}

final class _UiCopyableMessageState extends State<UiCopyableMessage> {
  var _hovered = false;
  var _copied = false;
  Timer? _copiedTimer;

  @override
  void dispose() {
    _copiedTimer?.cancel();
    super.dispose();
  }

  Future<void> _copy() async {
    final text = widget.copyText(context).trim();
    if (text.isEmpty) {
      return;
    }
    await Clipboard.setData(ClipboardData(text: text));
    if (!mounted) {
      return;
    }
    _copiedTimer?.cancel();
    setState(() => _copied = true);
    _copiedTimer = Timer(const Duration(milliseconds: 1400), () {
      if (mounted) {
        setState(() => _copied = false);
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final text = widget.copyText(context);
    final canCopy = text.trim().isNotEmpty;
    return MouseRegion(
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          SelectionArea(child: widget.child),
          if (canCopy)
            Positioned(
              top: 2,
              right: 2,
              child: AnimatedOpacity(
                duration: const Duration(milliseconds: 120),
                opacity: _hovered || _copied ? 1 : 0.55,
                child: Material(
                  color: UiPalette.surfaceRaised.withValues(alpha: 0.92),
                  shape: const CircleBorder(),
                  child: InkWell(
                    key: const Key('ui_copy_message'),
                    customBorder: const CircleBorder(),
                    onTap: _copy,
                    child: Tooltip(
                      message: _copied ? 'Copied' : 'Copy message',
                      child: Padding(
                        padding: const EdgeInsets.all(6),
                        child: Icon(
                          _copied ? Icons.check_rounded : Icons.copy_outlined,
                          size: 14,
                          color: _copied
                              ? UiPalette.success
                              : UiPalette.textMuted,
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
