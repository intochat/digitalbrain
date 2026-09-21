import 'dart:ui' as ui;
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_ui/src/components/image_canvas/image_recipe.dart';
import 'package:digitalbrain_ui/src/components/image_canvas/image_exporter.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  test('JPEG decoding normalizes EXIF portrait coordinates', () async {
    final codec = await ui.instantiateImageCodec(
      base64Decode(
        '/9j/4AAQSkZJRgABAQAAAQABAAD/4QAiRXhpZgAATU0AKgAAAAgAAQESAAMAAAABAAYAAAAAAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAADAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDi6KKK+ZP3E//Z',
      ),
    );
    final image = (await codec.getNextFrame()).image;
    expect(image.width, 3);
    expect(image.height, 2);
    image.dispose();
    codec.dispose();
  });
  test('crop exports exact dimensions and pen in source coordinates', () async {
    final recorder = ui.PictureRecorder();
    Canvas(recorder).drawRect(
      const Rect.fromLTWH(0, 0, 120, 80),
      Paint()..color = Colors.white,
    );
    final picture = recorder.endRecording();
    final source = await picture.toImage(120, 80);
    final recipe = ImageRecipe(
      crop: const Rect.fromLTWH(10, 10, 60, 40),
      strokes: [
        PenStroke(
          color: const Color(0xffff0000),
          width: 6,
          points: [const Offset(20, 20), const Offset(40, 20)],
        ),
      ],
    );
    final png = await ImageExporter.renderPng(source, recipe);
    final codec = await ui.instantiateImageCodec(png);
    final output = (await codec.getNextFrame()).image;
    expect(output.width, 60);
    expect(output.height, 40);
    final pixels = (await output.toByteData())!;
    final redPixel = (10 * 60 + 15) * 4;
    expect(pixels.getUint8(redPixel), 255);
    expect(pixels.getUint8(redPixel + 1), 0);
    output.dispose();
    codec.dispose();
    source.dispose();
    picture.dispose();
  });
  test('recipe json round trip retains crop and strokes', () {
    final recipe = ImageRecipe(
      crop: const Rect.fromLTWH(1, 2, 30, 40),
      strokes: [
        PenStroke(color: Colors.blue, width: 3, points: [const Offset(4, 5)]),
      ],
    );
    expect(ImageRecipe.fromJson(recipe.toJson()).toJson(), recipe.toJson());
  });
}
