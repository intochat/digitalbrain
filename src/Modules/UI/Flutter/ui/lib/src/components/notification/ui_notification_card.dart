import 'package:flutter/material.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';
import '../button/ui_button.dart';

/// A durable inbox entry. Provider text is always rendered as plain text.
final class UiNotificationCard extends StatelessWidget {
  const UiNotificationCard({
    super.key,
    required this.eventId,
    required this.title,
    required this.message,
    required this.kind,
    required this.createdUnixSeconds,
    this.dismissed = false,
    this.dismissing = false,
    this.onDismiss,
  });

  final String eventId;
  final String title;
  final String message;
  final String kind;
  final int createdUnixSeconds;
  final bool dismissed;
  final bool dismissing;
  final VoidCallback? onDismiss;

  @override
  Widget build(BuildContext context) {
    final created = DateTime.fromMillisecondsSinceEpoch(
      createdUnixSeconds * 1000,
      isUtc: true,
    ).toLocal();
    final time =
        '${created.year}-${created.month.toString().padLeft(2, '0')}-'
        '${created.day.toString().padLeft(2, '0')} '
        '${created.hour.toString().padLeft(2, '0')}:'
        '${created.minute.toString().padLeft(2, '0')}';
    return DecoratedBox(
      key: Key('ui_notification_$eventId'),
      decoration: BoxDecoration(
        color: UiPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: UiPalette.line),
      ),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: UiType.title),
            const SizedBox(height: 8),
            Text(message, style: UiType.body),
            const SizedBox(height: 8),
            Text('$kind · $time', style: UiType.meta),
            const SizedBox(height: 8),
            if (dismissed)
              const Text('Dismissed', style: UiType.meta)
            else if (onDismiss != null)
              UiButton(
                part: UiButtonPart(
                  buttonId: 'dismiss_$eventId',
                  label: dismissing ? 'Dismissing…' : 'Dismiss',
                  action: 'dismiss',
                ),
                dense: true,
                onPressed: dismissing ? null : (_) => onDismiss!(),
              ),
          ],
        ),
      ),
    );
  }
}
