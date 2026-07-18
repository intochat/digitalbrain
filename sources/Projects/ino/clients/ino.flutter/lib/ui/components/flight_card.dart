import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

LocalWidgetLibrary createFlightWidgets() {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'FlightCard': (BuildContext context, DataSource source) {
      final airline = source.v<String>(['airline']) ?? '';
      final from = source.v<String>(['from']) ?? '';
      final to = source.v<String>(['to']) ?? '';
      final price = source.v<int>(['price']) ?? 0;
      final date = source.v<String>(['date']) ?? '';
      final duration = source.v<String>(['duration']) ?? '';

      // Skeleton detection: the gateway's BuildSkeleton emits every card field
      // as empty/zero. Once real data arrives, the RFW data JSON is swapped
      // and these fields populate — the card rebuilds from the same widget
      // identity and animates via AnimatedSwitcher + TweenAnimationBuilder.
      final isSkeleton = airline.isEmpty && from.isEmpty && to.isEmpty;

      // Slice 4 — when the DSL binds `onSelect: event '...' { ... }`, the
      // card grows a Select button. source.handler returns null when the
      // attribute isn't bound, so existing pre-Slice-4 templates render
      // unchanged.
      final onSelect = source.handler(['onSelect'], (HandlerTrigger trigger) => trigger);

      return _FlightCard(
        airline: airline,
        from: from,
        to: to,
        price: price,
        date: date,
        duration: duration,
        isSkeleton: isSkeleton,
        onSelect: onSelect,
      );
    },
  });
}

class _FlightCard extends StatelessWidget {
  const _FlightCard({
    required this.airline,
    required this.from,
    required this.to,
    required this.price,
    required this.date,
    required this.duration,
    required this.isSkeleton,
    this.onSelect,
  });

  final String airline;
  final String from;
  final String to;
  final int price;
  final String date;
  final String duration;
  final bool isSkeleton;
  final VoidCallback? onSelect;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      color: theme.colorScheme.surface,
      margin: const EdgeInsets.symmetric(vertical: 4, horizontal: 12),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: AnimatedSwitcher(
          duration: const Duration(milliseconds: 320),
          switchInCurve: Curves.easeOutCubic,
          switchOutCurve: Curves.easeInCubic,
          transitionBuilder: (child, animation) => FadeTransition(
            opacity: animation,
            child: SlideTransition(
              position: Tween<Offset>(
                begin: const Offset(0, 0.04),
                end: Offset.zero,
              ).animate(animation),
              child: child,
            ),
          ),
          child: isSkeleton
              ? const _SkeletonBody(key: ValueKey('skeleton'))
              : Column(
                  key: const ValueKey('data'),
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    _DataBody(
                      airline: airline,
                      from: from,
                      to: to,
                      price: price,
                      date: date,
                      duration: duration,
                      theme: theme,
                    ),
                    if (onSelect != null) ...[
                      const SizedBox(height: 10),
                      FilledButton(
                        onPressed: onSelect,
                        child: const Text('Select'),
                      ),
                    ],
                  ],
                ),
        ),
      ),
    );
  }
}

class _DataBody extends StatelessWidget {
  const _DataBody({
    required this.airline,
    required this.from,
    required this.to,
    required this.price,
    required this.date,
    required this.duration,
    required this.theme,
  });

  final String airline;
  final String from;
  final String to;
  final int price;
  final String date;
  final String duration;
  final ThemeData theme;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Row(
          children: [
            Icon(Icons.flight, size: 18, color: theme.colorScheme.primary),
            const SizedBox(width: 8),
            Expanded(
              child: _TypedText(
                text: airline,
                style: TextStyle(
                  color: theme.colorScheme.onSurface,
                  fontWeight: FontWeight.w600,
                  fontSize: 14,
                ),
              ),
            ),
            _PriceCountUp(
              price: price,
              style: TextStyle(
                color: theme.colorScheme.primary,
                fontWeight: FontWeight.bold,
                fontSize: 16,
              ),
            ),
          ],
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            _TypedText(
              text: from,
              style: TextStyle(
                color: theme.colorScheme.onSurface,
                fontSize: 15,
                fontWeight: FontWeight.w500,
              ),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: Icon(
                Icons.arrow_forward,
                size: 16,
                color: theme.colorScheme.onSurface.withAlpha(150),
              ),
            ),
            _TypedText(
              text: to,
              style: TextStyle(
                color: theme.colorScheme.onSurface,
                fontSize: 15,
                fontWeight: FontWeight.w500,
              ),
            ),
          ],
        ),
        const SizedBox(height: 6),
        Row(
          children: [
            Icon(
              Icons.calendar_today,
              size: 13,
              color: theme.colorScheme.onSurface.withAlpha(150),
            ),
            const SizedBox(width: 4),
            _TypedText(
              text: date,
              style: TextStyle(
                color: theme.colorScheme.onSurface.withAlpha(150),
                fontSize: 13,
              ),
            ),
            const SizedBox(width: 12),
            Icon(
              Icons.schedule,
              size: 13,
              color: theme.colorScheme.onSurface.withAlpha(150),
            ),
            const SizedBox(width: 4),
            _TypedText(
              text: duration,
              style: TextStyle(
                color: theme.colorScheme.onSurface.withAlpha(150),
                fontSize: 13,
              ),
            ),
          ],
        ),
      ],
    );
  }
}

/// Reveals a string one character at a time. Drives the "ordering-a-taxi"
/// typing effect as real field values replace the skeleton.
class _TypedText extends StatelessWidget {
  const _TypedText({required this.text, required this.style});
  final String text;
  final TextStyle style;

  @override
  Widget build(BuildContext context) {
    if (text.isEmpty) {
      return Text('', style: style);
    }
    // ~18ms per glyph — fast enough that a 10-char airline name finishes
    // inside ~180ms, slow enough that the reveal reads as typing.
    final duration = Duration(milliseconds: (text.length * 18).clamp(120, 600));
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0.0, end: 1.0),
      duration: duration,
      curve: Curves.easeOut,
      builder: (context, progress, _) {
        final visible = (text.length * progress).round().clamp(0, text.length);
        return Text(text.substring(0, visible), style: style);
      },
    );
  }
}

/// Counts the price up from 0 so the dollar amount has the same "filling in"
/// feel as the text fields rather than popping fully formed.
class _PriceCountUp extends StatelessWidget {
  const _PriceCountUp({required this.price, required this.style});
  final int price;
  final TextStyle style;

  @override
  Widget build(BuildContext context) {
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0.0, end: 1.0),
      duration: const Duration(milliseconds: 420),
      curve: Curves.easeOutCubic,
      builder: (context, progress, _) {
        final current = (price * progress).round();
        return Text('\$$current', style: style);
      },
    );
  }
}

/// Three-bar shimmer that fills the same vertical space as the data body so
/// the card doesn't resize when real data swaps in.
class _SkeletonBody extends StatefulWidget {
  const _SkeletonBody({super.key});

  @override
  State<_SkeletonBody> createState() => _SkeletonBodyState();
}

class _SkeletonBodyState extends State<_SkeletonBody>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1200),
    )..repeat(reverse: true);
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return AnimatedBuilder(
      animation: _controller,
      builder: (context, _) {
        final t = Curves.easeInOut.transform(_controller.value);
        final base = theme.colorScheme.onSurface.withAlpha(30);
        final highlight = theme.colorScheme.onSurface.withAlpha(60);
        final color = Color.lerp(base, highlight, t) ?? base;
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                _Bar(width: 18, height: 18, color: color, radius: 4),
                const SizedBox(width: 8),
                Expanded(child: _Bar(width: double.infinity, height: 14, color: color)),
                const SizedBox(width: 12),
                _Bar(width: 56, height: 16, color: color),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                _Bar(width: 60, height: 15, color: color),
                const SizedBox(width: 8),
                Icon(
                  Icons.arrow_forward,
                  size: 16,
                  color: theme.colorScheme.onSurface.withAlpha(80),
                ),
                const SizedBox(width: 8),
                _Bar(width: 60, height: 15, color: color),
              ],
            ),
            const SizedBox(height: 8),
            Row(
              children: [
                _Bar(width: 13, height: 13, color: color, radius: 3),
                const SizedBox(width: 4),
                _Bar(width: 90, height: 11, color: color),
                const SizedBox(width: 12),
                _Bar(width: 13, height: 13, color: color, radius: 3),
                const SizedBox(width: 4),
                _Bar(width: 60, height: 11, color: color),
              ],
            ),
          ],
        );
      },
    );
  }
}

class _Bar extends StatelessWidget {
  const _Bar({
    required this.width,
    required this.height,
    required this.color,
    this.radius = 6,
  });
  final double width;
  final double height;
  final Color color;
  final double radius;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        color: color,
        borderRadius: BorderRadius.circular(radius),
      ),
    );
  }
}
