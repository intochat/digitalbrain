import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';

class Atom3D {
  const Atom3D({
    required this.symbol,
    required this.x,
    required this.y,
    required this.z,
    required this.colorName,
    required this.radius,
  });

  final String symbol;
  final double x;
  final double y;
  final double z;
  final String colorName;
  final double radius;
}

class Bond3D {
  const Bond3D({
    required this.from,
    required this.to,
  });

  final int from;
  final int to;
}

class Canvas3DWidget extends StatefulWidget {
  const Canvas3DWidget({
    super.key,
    required this.sceneName,
    required this.atoms,
    required this.bonds,
    this.spinSpeed = 1.0,
  });

  final String sceneName;
  final List<Atom3D> atoms;
  final List<Bond3D> bonds;
  final double spinSpeed;

  @override
  State<Canvas3DWidget> createState() => _Canvas3DWidgetState();
}

class _Canvas3DWidgetState extends State<Canvas3DWidget> with SingleTickerProviderStateMixin {
  late final AnimationController _spinController;
  double _yaw = 0.0;
  double _pitch = 0.0;
  Offset? _lastPanOffset;

  @override
  void initState() {
    super.initState();
    _spinController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 10),
    )..repeat();
  }

  @override
  void dispose() {
    _spinController.dispose();
    super.dispose();
  }

  void _handlePanStart(DragStartDetails details) {
    _lastPanOffset = details.localPosition;
  }

  void _handlePanUpdate(DragUpdateDetails details) {
    if (_lastPanOffset == null) return;
    final diff = details.localPosition - _lastPanOffset!;
    setState(() {
      _yaw += diff.dx * 0.01;
      _pitch += diff.dy * 0.01;
    });
    _lastPanOffset = details.localPosition;
  }

  void _handlePanEnd(DragEndDetails details) {
    _lastPanOffset = null;
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: DigitalBrainColors.panel,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: DigitalBrainColors.hairline),
      ),
      clipBehavior: Clip.antiAlias,
      child: GestureDetector(
        onPanStart: _handlePanStart,
        onPanUpdate: _handlePanUpdate,
        onPanEnd: _handlePanEnd,
        child: AnimatedBuilder(
          animation: _spinController,
          builder: (context, child) {
            // Apply auto-spin to yaw based on spinSpeed
            final currentYaw = _yaw + (_spinController.value * 2.0 * math.pi * widget.spinSpeed);
            return CustomPaint(
              size: Size.infinite,
              painter: _Canvas3DPainter(
                atoms: widget.atoms,
                bonds: widget.bonds,
                yaw: currentYaw,
                pitch: _pitch,
                sceneName: widget.sceneName,
              ),
            );
          },
        ),
      ),
    );
  }
}

class _ProjectedItem implements Comparable<_ProjectedItem> {
  _ProjectedItem(this.depth, this.draw);
  final double depth;
  final void Function(Canvas canvas, Size size) draw;

  @override
  int compareTo(_ProjectedItem other) {
    // Sort from back to front (descending depth)
    return other.depth.compareTo(depth);
  }
}

class _Canvas3DPainter extends CustomPainter {
  _Canvas3DPainter({
    required this.atoms,
    required this.bonds,
    required this.yaw,
    required this.pitch,
    required this.sceneName,
  });

  final List<Atom3D> atoms;
  final List<Bond3D> bonds;
  final double yaw;
  final double pitch;
  final String sceneName;

  Color _resolveColor(String colorName) {
    switch (colorName.toLowerCase()) {
      case 'red':
        return Colors.redAccent.shade700;
      case 'white':
        return Colors.white;
      case 'grey':
      case 'gray':
        return Colors.grey.shade400;
      case 'blue':
        return Colors.blueAccent.shade700;
      case 'green':
        return Colors.greenAccent.shade700;
      case 'amber':
      case 'yellow':
        return Colors.amber.shade700;
      case 'indigo':
      default:
        return DigitalBrainColors.indigoSoft;
    }
  }

  @override
  void paint(Canvas canvas, Size size) {
    if (atoms.isEmpty) {
      // Paint an elegant empty message
      final textPainter = TextPainter(
        text: TextSpan(
          text: 'Empty 3D Canvas ($sceneName)\nUse .ino scripts to load geometry',
          style: GoogleFonts.manrope(
            fontSize: 14,
            color: DigitalBrainColors.inkLow,
            height: 1.5,
          ),
        ),
        textDirection: TextDirection.ltr,
        textAlign: TextAlign.center,
      )..layout();
      textPainter.paint(
        canvas,
        Offset(
          (size.width - textPainter.width) / 2,
          (size.height - textPainter.height) / 2,
        ),
      );
      return;
    }

    final cx = size.width / 2;
    final cy = size.height / 2;
    final scale = math.min(cx, cy) * 0.6; // scale factor to fit viewport

    final cosY = math.cos(yaw);
    final sinY = math.sin(yaw);
    final cosP = math.cos(pitch);
    final sinP = math.sin(pitch);

    // 1. Project all atoms into 3D camera coordinates & Z-depth
    final projX = List<double>.filled(atoms.length, 0);
    final projY = List<double>.filled(atoms.length, 0);
    final projZ = List<double>.filled(atoms.length, 0);

    for (var i = 0; i < atoms.length; i++) {
      final a = atoms[i];
      // Rotate around Y-axis (Yaw)
      final x1 = a.x * cosY - a.z * sinY;
      final z1 = a.x * sinY + a.z * cosY;
      // Rotate around X-axis (Pitch)
      final y2 = a.y * cosP - z1 * sinP;
      final z2 = a.y * sinP + z1 * cosP;

      projX[i] = cx + x1 * scale;
      projY[i] = cy + y2 * scale;
      projZ[i] = z2; // larger Z is further away in camera depth
    }

    // 2. Prepare draw items for depth sorting
    final drawItems = <_ProjectedItem>[];

    // Add bonds as projected items
    for (final b in bonds) {
      if (b.from >= 0 && b.from < atoms.length && b.to >= 0 && b.to < atoms.length) {
        final avgDepth = (projZ[b.from] + projZ[b.to]) / 2;
        drawItems.add(_ProjectedItem(avgDepth, (canvas, size) {
          final p1 = Offset(projX[b.from], projY[b.from]);
          final p2 = Offset(projX[b.to], projY[b.to]);

          final paint = Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 6.0
            ..strokeCap = StrokeCap.round
            ..shader = LinearGradient(
              colors: [
                _resolveColor(atoms[b.from].colorName),
                _resolveColor(atoms[b.to].colorName),
              ],
            ).createShader(Rect.fromPoints(p1, p2));

          canvas.drawLine(p1, p2, paint);
          // Highlight overlay for a premium glowing 3D vibe
          canvas.drawLine(
            p1,
            p2,
            Paint()
              ..style = PaintingStyle.stroke
              ..strokeWidth = 2.0
              ..strokeCap = StrokeCap.round
              ..color = Colors.white.withValues(alpha: 0.6),
          );
        }));
      }
    }

    // Add atoms as projected items
    for (var i = 0; i < atoms.length; i++) {
      final a = atoms[i];
      final ax = projX[i];
      final ay = projY[i];
      final az = projZ[i];

      // Perspective correction factor for radius based on Z-depth
      final factor = 1.0 / (1.0 + az * 0.15);
      final r = a.radius * factor;
      final color = _resolveColor(a.colorName);

      drawItems.add(_ProjectedItem(az, (canvas, size) {
        final rect = Rect.fromCircle(center: Offset(ax, ay), radius: r);
        
        // Beautiful radial gradient shader to render the atom as a shiny 3D sphere
        final paint = Paint()
          ..shader = RadialGradient(
            center: const Alignment(-0.35, -0.35), // light source offset
            colors: [
              Colors.white,
              color,
              color.withValues(alpha: 0.7),
            ],
            stops: const [0.0, 0.6, 1.0],
          ).createShader(rect);

        // Shadow glow
        canvas.drawCircle(
          Offset(ax, ay),
          r + 3.0,
          Paint()
            ..color = color.withValues(alpha: 0.24)
            ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 3.0),
        );

        canvas.drawCircle(Offset(ax, ay), r, paint);

        // Draw chemical symbol text cleanly in monospaced font
        final textPainter = TextPainter(
          text: TextSpan(
            text: a.symbol,
            style: GoogleFonts.jetBrainsMono(
              fontSize: r * 0.9,
              fontWeight: FontWeight.w800,
              color: a.colorName.toLowerCase() == 'white' ? Colors.grey.shade800 : Colors.white,
            ),
          ),
          textDirection: TextDirection.ltr,
        )..layout();
        
        textPainter.paint(
          canvas,
          Offset(
            ax - textPainter.width / 2,
            ay - textPainter.height / 2,
          ),
        );
      }));
    }

    // 3. Sort draw items from back to front (Z-depth) and paint!
    drawItems.sort();
    for (final item in drawItems) {
      item.draw(canvas, size);
    }

    // Paint scene label floating in the top left
    final labelPainter = TextPainter(
      text: TextSpan(
        text: '3D CANVAS: ${sceneName.toUpperCase()}',
        style: GoogleFonts.jetBrainsMono(
          fontSize: 10,
          color: DigitalBrainColors.indigoSoft,
          fontWeight: FontWeight.w600,
          letterSpacing: 1.5,
        ),
      ),
      textDirection: TextDirection.ltr,
    )..layout();
    labelPainter.paint(canvas, const Offset(16, 16));
  }

  @override
  bool shouldRepaint(_Canvas3DPainter old) =>
      old.atoms != atoms ||
      old.bonds != bonds ||
      old.yaw != yaw ||
      old.pitch != pitch ||
      old.sceneName != sceneName;
}
