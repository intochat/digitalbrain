import 'dart:convert';

import 'package:flutter/material.dart';

class PenStroke {
  const PenStroke({
    required this.color,
    required this.width,
    required this.points,
  });
  final Color color;
  final double width;
  final List<Offset> points;
  Map<String, dynamic> toJson() => {
    'colorArgb': color.toARGB32(),
    'width': width,
    'points': points.map((p) => {'x': p.dx, 'y': p.dy}).toList(),
  };
  factory PenStroke.fromJson(Map<String, dynamic> json) => PenStroke(
    color: Color((json['colorArgb'] as num).toInt()),
    width: (json['width'] as num).toDouble(),
    points: (json['points'] as List)
        .map(
          (p) => Offset((p['x'] as num).toDouble(), (p['y'] as num).toDouble()),
        )
        .toList(),
  );
}

class ImageRecipe {
  const ImageRecipe({this.crop, this.strokes = const []});
  final Rect? crop;
  final List<PenStroke> strokes;
  Map<String, dynamic> toJson() => {
    'crop': crop == null
        ? null
        : {
            'x': crop!.left.round(),
            'y': crop!.top.round(),
            'width': crop!.width.round(),
            'height': crop!.height.round(),
          },
    'strokes': strokes.map((s) => s.toJson()).toList(),
  };
  factory ImageRecipe.fromJson(Map<String, dynamic> json) {
    final c = json['crop'];
    return ImageRecipe(
      crop: c == null
          ? null
          : Rect.fromLTWH(
              (c['x'] as num).toDouble(),
              (c['y'] as num).toDouble(),
              (c['width'] as num).toDouble(),
              (c['height'] as num).toDouble(),
            ),
      strokes: (json['strokes'] as List? ?? [])
          .map((s) => PenStroke.fromJson(Map<String, dynamic>.from(s)))
          .toList(),
    );
  }

  bool sameAs(ImageRecipe other) => jsonEncode(toJson()) == jsonEncode(other.toJson());
}
