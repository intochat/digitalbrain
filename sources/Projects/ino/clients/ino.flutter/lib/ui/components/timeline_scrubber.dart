import 'package:flutter/material.dart';

class TimelineScrubber extends StatelessWidget {
  const TimelineScrubber({
    required this.current,
    required this.max,
    required this.onChanged,
    super.key,
  });

  final int current;
  final int max;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            children: [
              Icon(
                Icons.history,
                color: colorScheme.primary,
                size: 20,
              ),
              const SizedBox(width: 8),
              Text(
                'Sequence: $current / $max',
                style: TextStyle(
                  color: colorScheme.onSurface,
                  fontSize: 14,
                  fontWeight: FontWeight.w500,
                ),
              ),
            ],
          ),
          Slider(
            value: max > 0 ? current.toDouble() : 0,
            min: 0,
            max: max > 0 ? max.toDouble() : 1,
            divisions: max > 0 ? max : null,
            label: '$current',
            onChanged: max > 0
                ? (value) => onChanged(value.round())
                : null,
          ),
        ],
      ),
    );
  }
}
