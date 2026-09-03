import 'dart:math' as math;

import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('focusing the origin leaves the angles alone', () {
    final camera = GraphCamera()..orbitBy(40, 0);
    final before = camera.target;
    camera.focusOn(const GraphPoint(0, 0, 0));
    expect(camera.target.yaw, before.yaw);
    expect(camera.target.pitch, before.pitch);
  });

  test('focusing +X turns the camera a quarter turn', () {
    final camera = GraphCamera()..focusOn(const GraphPoint(1, 0, 0));
    expect(camera.target.yaw, closeTo(-math.pi / 2, 1e-9));
    expect(camera.target.pitch, closeTo(0, 1e-9));
  });

  test('focus takes the short way round instead of unwinding', () {
    final camera = GraphCamera();
    for (var i = 0; i < 4; i++) {
      camera.orbitBy(250, 0);
    }
    for (var i = 0; i < 600; i++) {
      camera.tick(1 / 60);
    }
    camera.focusOn(const GraphPoint(0, 0, 1));
    expect((camera.target.yaw - camera.current.yaw).abs(), lessThan(math.pi));
  });

  test('pitch cannot flip over the poles', () {
    final camera = GraphCamera()..orbitBy(0, 100000);
    expect(camera.target.pitch, lessThanOrEqualTo(1.2));
    camera.orbitBy(0, -200000);
    expect(camera.target.pitch, greaterThanOrEqualTo(-1.2));
  });

  test('zoom is clamped to a usable range', () {
    final camera = GraphCamera();
    for (var i = 0; i < 60; i++) {
      camera.zoomBy(1.4);
    }
    expect(camera.target.zoom, lessThanOrEqualTo(3.0));
    for (var i = 0; i < 200; i++) {
      camera.zoomBy(0.7);
    }
    expect(camera.target.zoom, greaterThanOrEqualTo(0.5));
  });

  test('ticking converges on the target and then settles', () {
    final camera = GraphCamera()..focusOn(const GraphPoint(1, 0, 0));
    expect(camera.settled, isFalse);
    for (var i = 0; i < 400; i++) {
      camera.tick(1 / 60);
    }
    expect(camera.current.yaw, closeTo(camera.target.yaw, 1e-3));
    expect(camera.settled, isTrue);
  });
}
