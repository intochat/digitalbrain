import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_earth_globe/flutter_earth_globe.dart';
import 'package:flutter_earth_globe/flutter_earth_globe_controller.dart';
import 'package:flutter_earth_globe/globe_coordinates.dart';
import 'package:flutter_earth_globe/point_connection.dart';
import 'package:flutter_earth_globe/point_connection_style.dart';
import 'package:lottie/lottie.dart';

/// Slice 0 throwaway de-risk page (docs/redesign/05-ROADMAP.md).
///
/// Renders a shader-based [FlutterEarthGlobe] (needs `--wasm`) and a network
/// [Lottie] animation side by side to confirm they coexist under one renderer
/// on the `flutter-web` resource. Delete after the renderer decision is made.
class GlobeLottieSpike extends StatefulWidget {
  const GlobeLottieSpike({super.key});

  @override
  State<GlobeLottieSpike> createState() => _GlobeLottieSpikeState();
}

class _GlobeLottieSpikeState extends State<GlobeLottieSpike> {
  late final FlutterEarthGlobeController _globe;

  // 1x1 deep-blue PNG, stretched as an equirectangular surface. Avoids any
  // asset dependency for the spike — we only care that the shader path runs.
  static final _surface = MemoryImage(
    base64Decode(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
    ),
  );

  @override
  void initState() {
    super.initState();
    _globe = FlutterEarthGlobeController(
      rotationSpeed: 0.08,
      isRotating: true,
      isBackgroundFollowingSphereRotation: true,
      surface: _surface,
    );
    _globe.onLoaded = () {
      _globe.addPointConnection(
        PointConnection(
          id: 'lhr-jfk',
          start: const GlobeCoordinates(51.4700, -0.4543), // LHR
          end: const GlobeCoordinates(40.6413, -73.7781), // JFK
          isMoving: true,
          isLabelVisible: false,
          style: const PointConnectionStyle(
            type: PointConnectionType.dashed,
            color: Color(0xFF00FFD2),
            lineWidth: 2,
            dashSize: 6,
            spacing: 6,
          ),
        ),
        animateDraw: true,
      );
    };
  }

  @override
  void dispose() {
    _globe.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0D0E12),
      appBar: AppBar(
        title: const Text('Slice 0 — globe + lottie + wasm spike'),
      ),
      body: Row(
        children: [
          Expanded(
            child: Center(
              child: SizedBox(
                width: 360,
                height: 360,
                child: FlutterEarthGlobe(controller: _globe, radius: 140),
              ),
            ),
          ),
          Expanded(
            child: Center(
              child: Lottie.network(
                'https://raw.githubusercontent.com/xvrh/lottie-flutter/master/example/assets/Mobilo/A.json',
                width: 280,
                height: 280,
                repeat: true,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
