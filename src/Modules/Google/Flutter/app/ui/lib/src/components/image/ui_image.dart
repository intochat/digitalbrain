import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../../theme/ui_theme.dart';

final class UiImage extends StatelessWidget {
  const UiImage({super.key, required this.bytes, required this.caption});

  final Uint8List bytes;
  final String caption;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      key: Key('ui_image_$caption'),
      decoration: BoxDecoration(
        color: UiPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: UiPalette.line),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (caption.isNotEmpty) ...[
              Text(caption, style: UiType.title),
              const SizedBox(height: 10),
            ],
            ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: Image.memory(
                bytes,
                fit: BoxFit.cover,
                frameBuilder: (context, child, frame, wasSynchronouslyLoaded) {
                  if (wasSynchronouslyLoaded || frame != null) {
                    return child;
                  }
                  return const Padding(
                    key: Key('ui_image_loading'),
                    padding: EdgeInsets.all(24),
                    child: Center(
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  );
                },
                errorBuilder: (context, error, stackTrace) {
                  return const Padding(
                    key: Key('ui_image_error'),
                    padding: EdgeInsets.all(24),
                    child: Center(
                      child: Text(
                        'Image failed to load',
                        style: UiType.bodyMuted,
                      ),
                    ),
                  );
                },
              ),
            ),
          ],
        ),
      ),
    );
  }
}
