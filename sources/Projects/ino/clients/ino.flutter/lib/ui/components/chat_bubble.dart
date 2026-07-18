import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:rfw/rfw.dart';

/// Shared visual for chat bubbles. Used by the inline `_ChatBubble` in
/// brain_home_screen.dart and by the RFW `ChatBubble` widget below. Assistant
/// bubbles get a hover-revealed copy icon; user bubbles don't (the user
/// already has the source). SelectableText covers select-and-copy on both.
Widget chatBubbleVisual({
  required BuildContext context,
  required String text,
  required bool isUser,
  double maxWidth = 280,
  double horizontalMargin = 12,
}) {
  final scheme = Theme.of(context).colorScheme;
  final bubble = Container(
    constraints: BoxConstraints(maxWidth: maxWidth),
    padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 14),
    decoration: BoxDecoration(
      color: isUser ? scheme.primary : scheme.surface,
      borderRadius: BorderRadius.only(
        topLeft: const Radius.circular(16),
        topRight: const Radius.circular(16),
        bottomLeft: Radius.circular(isUser ? 16 : 4),
        bottomRight: Radius.circular(isUser ? 4 : 16),
      ),
    ),
    child: SelectableText(
      text,
      style: TextStyle(
        color: isUser ? scheme.onPrimary : scheme.onSurface,
        fontSize: 15,
      ),
    ),
  );

  return Padding(
    padding: EdgeInsets.symmetric(vertical: 4, horizontal: horizontalMargin),
    child: Align(
      alignment: isUser ? Alignment.centerRight : Alignment.centerLeft,
      child: isUser ? bubble : _BubbleWithCopy(text: text, bubble: bubble),
    ),
  );
}

class _BubbleWithCopy extends StatefulWidget {
  const _BubbleWithCopy({required this.text, required this.bubble});
  final String text;
  final Widget bubble;

  @override
  State<_BubbleWithCopy> createState() => _BubbleWithCopyState();
}

class _BubbleWithCopyState extends State<_BubbleWithCopy> {
  bool _hovering = false;

  Future<void> _copy() async {
    await Clipboard.setData(ClipboardData(text: widget.text));
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('Copied'),
        duration: Duration(milliseconds: 900),
        behavior: SnackBarBehavior.floating,
        width: 120,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return MouseRegion(
      onEnter: (_) => setState(() => _hovering = true),
      onExit: (_) => setState(() => _hovering = false),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Flexible(child: widget.bubble),
          AnimatedOpacity(
            duration: const Duration(milliseconds: 120),
            opacity: _hovering ? 1.0 : 0.0,
            child: IgnorePointer(
              ignoring: !_hovering,
              child: IconButton(
                icon: const Icon(Icons.content_copy_outlined, size: 16),
                tooltip: 'Copy',
                color: scheme.onSurface.withValues(alpha: 0.6),
                onPressed: _copy,
                padding: const EdgeInsets.all(4),
                visualDensity: VisualDensity.compact,
                constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

LocalWidgetLibrary createChatWidgets() {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'ChatBubble': (BuildContext context, DataSource source) {
      return chatBubbleVisual(
        context: context,
        text: source.v<String>(['text']) ?? '',
        isUser: source.v<bool>(['isUser']) ?? false,
      );
    },
  });
}
