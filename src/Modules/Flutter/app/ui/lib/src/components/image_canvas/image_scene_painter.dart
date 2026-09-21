import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import 'image_recipe.dart';

void paintImageScene(Canvas canvas, ui.Image image, ImageRecipe recipe) {
  canvas.drawImage(image, Offset.zero, Paint());
  for (final stroke in recipe.strokes) {
    if (stroke.points.isEmpty) continue;
    final paint = Paint()
      ..color = stroke.color
      ..strokeWidth = stroke.width
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round
      ..style = PaintingStyle.stroke;
    if (stroke.points.length == 1) {
      canvas.drawCircle(
        stroke.points.first,
        stroke.width / 2,
        Paint()..color = stroke.color,
      );
    } else {
      final path = Path()
        ..moveTo(stroke.points.first.dx, stroke.points.first.dy);
      for (final point in stroke.points.skip(1)) {
        path.lineTo(point.dx, point.dy);
      }
      canvas.drawPath(path, paint);
    }
  }
}

class ImageScenePainter extends CustomPainter {
  ImageScenePainter(this.image, this.recipe, {this.draft});
  final ui.Image image;
  final ImageRecipe recipe;
  final PenStroke? draft;
  @override
  void paint(Canvas canvas, Size size) {
    paintImageScene(
      canvas,
      image,
      ImageRecipe(strokes: [...recipe.strokes, ?draft]),
    );
    if (recipe.crop case final crop?) {
      final outside = Path.combine(
        PathOperation.difference,
        Path()..addRect(Offset.zero & size),
        Path()..addRect(crop),
      );
      canvas.drawPath(outside, Paint()..color = const Color(0x99000000));
      canvas.drawRect(
        crop,
        Paint()
          ..color = const Color(0xffa4e1ca)
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2,
      );
    }
  }

  @override
  bool shouldRepaint(ImageScenePainter old) =>
      old.image != image || old.recipe != recipe || old.draft != draft;
}
