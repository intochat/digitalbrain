import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../../theme/kit_theme.dart';

final class KitImage extends StatelessWidget {
  const KitImage({super.key, required this.bytes, required this.caption});

  final Uint8List bytes;
  final String caption;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      key: Key('kit_image_$caption'),
      decoration: BoxDecoration(
        color: KitPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: KitPalette.line),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (caption.isNotEmpty) ...[
              Text(caption, style: KitType.title),
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
                    key: Key('kit_image_loading'),
                    padding: EdgeInsets.all(24),
                    child: Center(
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  );
                },
                errorBuilder: (context, error, stackTrace) {
                  return const Padding(
                    key: Key('kit_image_error'),
                    padding: EdgeInsets.all(24),
                    child: Center(
                      child: Text(
                        'Image failed to load',
                        style: KitType.bodyMuted,
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
