import 'package:flutter/material.dart';

import 'shell_card.dart';
import 'shell_theme.dart';

class _PendingEntry {
  _PendingEntry({
    required this.model,
    required this.tween,
    required this.controller,
  });

  ShellCardModel model;
  Tween<Offset> tween;
  final AnimationController controller;
  bool flashing = false;
}

/// Compose canvas that holds [ShellCardModel] instances and animates each new
/// card from its cluster's projected screen origin to its 2-col grid slot over
/// 240 ms using [Curves.easeOutBack] (slight overshoot, cleaner than elastic).
///
/// Public surface intentionally small — T10.x's DemoRunner drives [showCard]
/// via a [GlobalKey<ShellComposeState>].
class ShellCompose extends StatefulWidget {
  const ShellCompose({super.key, this.replayCallback});

  /// Invoked when a card's chevron is tapped. Receives the card id.
  final void Function(String cardId)? replayCallback;

  @override
  State<ShellCompose> createState() => ShellComposeState();
}

class ShellComposeState extends State<ShellCompose>
    with TickerProviderStateMixin {
  static const double _cardWidth = 320;
  static const double _cardGap = 16;
  static const int _columns = 2;

  final Map<String, _PendingEntry> _entries = {};

  // Slot positions recomputed on each layout pass — keyed by insertion order.
  final List<Offset> _slots = [];
  Size _lastConstraintSize = Size.zero;

  /// Adds or replaces a card.
  ///
  /// First appearance: animates from [originScreenOffset] to the card's grid
  /// slot over [InoShellTheme.cardEntryDur] with [Curves.easeOutBack].
  /// Subsequent calls with the same [model.id] replace the model in place
  /// without re-running the entry animation.
  void showCard({
    required ShellCardModel model,
    required Offset originScreenOffset,
  }) {
    final existing = _entries[model.id];
    if (existing != null) {
      setState(() => existing.model = model);
      return;
    }

    final slot = _slotForIndex(_entries.length, _lastConstraintSize);
    final controller = AnimationController(
      duration: InoShellTheme.cardEntryDur,
      vsync: this,
    );
    final tween = Tween<Offset>(begin: originScreenOffset, end: slot);

    _entries[model.id] = _PendingEntry(
      model: model,
      tween: tween,
      controller: controller,
    );

    controller.addListener(() {
      if (mounted) setState(() {});
    });
    controller.forward();
  }

  /// Removes the card with [id] and disposes its controller.
  void clearCard(String id) {
    final entry = _entries.remove(id);
    if (entry == null) return;
    entry.controller
      ..stop()
      ..dispose();
    if (mounted) setState(() {});
  }

  /// Removes all cards and disposes all controllers.
  void clearAll() {
    for (final entry in _entries.values) {
      entry.controller
        ..stop()
        ..dispose();
    }
    _entries.clear();
    if (mounted) setState(() {});
  }

  /// Replaces the row list of an existing card and flashes a gold border for
  /// 1200 ms. The entry animation does not re-trigger.
  ///
  /// No-op if no card with [id] is currently visible.
  void morphCard(String id, ShellCardModel newModel) {
    final entry = _entries[id];
    if (entry == null) return;
    setState(() {
      entry.model = newModel;
      entry.flashing = true;
    });
    Future.delayed(const Duration(milliseconds: 1200), () {
      if (!mounted) return;
      final current = _entries[id];
      if (current == null) return;
      setState(() => current.flashing = false);
    });
  }

  @override
  void dispose() {
    for (final entry in _entries.values) {
      entry.controller
        ..stop()
        ..dispose();
    }
    super.dispose();
  }

  /// Computes the grid slot [Offset] for a given zero-based [index] within
  /// [size]. Two-column layout, rows grow downward.
  Offset _slotForIndex(int index, Size size) {
    final col = index % _columns;
    final row = index ~/ _columns;

    final totalWidth = _columns * _cardWidth + (_columns - 1) * _cardGap;
    final originX = (size.width - totalWidth) / 2;
    final originY = (size.height - _cardWidth) / 2;

    return Offset(
      originX + col * (_cardWidth + _cardGap),
      originY + row * (_cardWidth + _cardGap),
    );
  }

  void _recomputeSlots(BoxConstraints constraints) {
    final size = Size(constraints.maxWidth, constraints.maxHeight);
    if (size == _lastConstraintSize) return;
    _lastConstraintSize = size;

    var i = 0;
    for (final entry in _entries.values) {
      final newSlot = _slotForIndex(i, size);
      entry.tween = Tween<Offset>(begin: entry.tween.begin, end: newSlot);
      i++;
    }
    _slots
      ..clear()
      ..addAll(List.generate(_entries.length, (n) => _slotForIndex(n, size)));
  }

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        _recomputeSlots(constraints);
        return Stack(
          clipBehavior: Clip.none,
          children: [
            for (final entry in _entries.values) _buildAnimated(entry),
          ],
        );
      },
    );
  }

  Widget _buildAnimated(_PendingEntry entry) {
    return AnimatedBuilder(
      animation: entry.controller,
      builder: (context, child) {
        final curvedValue = CurvedAnimation(
          parent: entry.controller,
          curve: Curves.easeOutBack,
        );
        final pos = entry.tween.evaluate(curvedValue);
        // Clamp opacity to [0,1] — easeOutBack slightly overshoots so the
        // raw controller value can transiently exceed 1.
        final opacity = entry.controller.value.clamp(0.0, 1.0);
        return Positioned(
          left: pos.dx,
          top: pos.dy,
          width: _cardWidth,
          child: Opacity(
            opacity: opacity,
            child: child,
          ),
        );
      },
      child: ShellCard(
        model: entry.model,
        highlight: entry.flashing,
        onReplayTrace: widget.replayCallback == null
            ? null
            : () => widget.replayCallback!(entry.model.id),
      ),
    );
  }
}
