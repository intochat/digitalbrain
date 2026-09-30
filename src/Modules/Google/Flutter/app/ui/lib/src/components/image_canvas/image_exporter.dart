import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/painting.dart';

import 'image_recipe.dart';
import 'image_scene_painter.dart';

abstract final class ImageExporter {
  static Future<Uint8List> renderPng(
    ui.Image source,
    ImageRecipe recipe,
  ) async {
    final crop =
        recipe.crop ??
        Rect.fromLTWH(0, 0, source.width.toDouble(), source.height.toDouble());
    if (crop.width <= 0 ||
        crop.height <= 0 ||
        crop.left < 0 ||
        crop.top < 0 ||
        crop.right > source.width ||
        crop.bottom > source.height) {
      throw ArgumentError('Crop must be inside the source image.');
    }
    final recorder = ui.PictureRecorder();
    final canvas = Canvas(recorder)
      ..clipRect(Rect.fromLTWH(0, 0, crop.width, crop.height))
      ..translate(-crop.left, -crop.top);
    paintImageScene(canvas, source, recipe);
    final picture = recorder.endRecording();
    try {
      final output = await picture.toImage(
        crop.width.round(),
        crop.height.round(),
      );
      try {
        final data = await output.toByteData(format: ui.ImageByteFormat.png);
        if (data == null) {
          throw StateError(
            'PNG export failed. Your edits are still available.',
          );
        }
        return data.buffer.asUint8List(data.offsetInBytes, data.lengthInBytes);
      } finally {
        output.dispose();
      }
    } finally {
      picture.dispose();
    }
  }
}
