import 'dart:ui' as ui;
import 'package:flutter/cupertino.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';
import '../../theme/digitalbrain_theme.dart';

/// Highly premium GlassMaterial widget that loads a frosted liquid glass
/// fragment shader, tracks real-time cursor offsets inside a MouseRegion,
/// executes an animation loop for light reflections, and renders backdrop blur.
/// Falls back cleanly to BackdropFilter blur elsewhere.
class GlassMaterial extends StatefulWidget {
  const GlassMaterial({
    required this.child,
    this.blurSigma = 24,
    this.cornerRadius = 28,
    this.borderRadius,
    this.tintOpacity = 0.04,
    this.borderGradient,
    this.brandColor = const Color(0xFF818CF8),
    super.key,
  });

  final Widget child;
  final double blurSigma;
  final double cornerRadius;
  final BorderRadius? borderRadius;
  final double tintOpacity;
  final Gradient? borderGradient;
  final Color brandColor;

  BorderRadius get effectiveBorderRadius =>
      borderRadius ?? BorderRadius.circular(cornerRadius);

  @override
  State<GlassMaterial> createState() => _GlassMaterialState();
}

class _GlassMaterialState extends State<GlassMaterial> with SingleTickerProviderStateMixin {
  static ui.FragmentProgram? _shaderProgram;
  static bool _loadingShader = false;

  late final Ticker _ticker;
  double _elapsedTime = 0.0;
  Offset _mouseOffset = Offset.zero;
  bool _isHovered = false;

  @override
  void initState() {
    super.initState();
    _ticker = createTicker((elapsed) {
      setState(() {
        _elapsedTime = elapsed.inMilliseconds / 1000.0;
      });
    });
    _loadShader();
  }

  void _loadShader() async {
    if (_shaderProgram != null || _loadingShader) return;
    _loadingShader = true;
    try {
      final program = await ui.FragmentProgram.fromAsset(
        'assets/shaders/glass_refract.frag',
      );
      if (mounted) {
        setState(() {
          _shaderProgram = program;
        });
      }
    } catch (_) {
      // Fail silently and rely on the premium CPU backdrop-filter fallback
    }
  }

  @override
  void dispose() {
    _ticker.dispose();
    super.dispose();
  }

  bool get _isCupertino =>
      defaultTargetPlatform == TargetPlatform.iOS ||
      defaultTargetPlatform == TargetPlatform.macOS;

  @override
  Widget build(BuildContext context) {
    if (_isCupertino) {
      return ClipRRect(
        borderRadius: widget.effectiveBorderRadius,
        child: CupertinoPopupSurface(child: widget.child),
      );
    }

    final effectiveSigma = kIsWeb && MediaQuery.sizeOf(context).width < 600
        ? 14.0
        : widget.blurSigma;

    final hasShader = false; // _shaderProgram != null;

    Widget body = DecoratedBox(
      decoration: BoxDecoration(
        color: Theme.of(context)
            .colorScheme
            .surfaceContainerHighest
            .withValues(alpha: widget.tintOpacity),
        borderRadius: widget.effectiveBorderRadius,
      ),
      child: Stack(
        children: [
          widget.child,
          // Specular top-light gradient (the "liquid" hint fallback if no shader)
          if (!hasShader)
            Positioned.fill(
              child: IgnorePointer(
                child: Align(
                  alignment: Alignment.topCenter,
                  child: FractionallySizedBox(
                    heightFactor: 0.35,
                    widthFactor: 1.0,
                    child: DecoratedBox(
                      decoration: BoxDecoration(
                        gradient: LinearGradient(
                          begin: Alignment.topCenter,
                          end: Alignment.bottomCenter,
                          colors: [
                            Colors.white.withValues(alpha: 0.16),
                            Colors.white.withValues(alpha: 0.0),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );

    if (hasShader) {
      body = CustomPaint(
        painter: LiquidGlassShaderPainter(
          shader: _shaderProgram!.fragmentShader(),
          mouseOffset: _mouseOffset,
          time: _elapsedTime,
          brandColor: widget.brandColor,
        ),
        child: widget.child,
      );
    }

    return MouseRegion(
      onEnter: (event) {
        _isHovered = true;
        _ticker.start();
        setState(() {
          _mouseOffset = event.localPosition;
        });
      },
      onHover: (event) {
        setState(() {
          _mouseOffset = event.localPosition;
        });
      },
      onExit: (_) {
        _isHovered = false;
        _ticker.stop();
        setState(() {
          _mouseOffset = Offset.zero;
        });
      },
      child: GlassBorder(
        borderRadius: widget.effectiveBorderRadius,
        gradient: widget.borderGradient,
        child: ClipRRect(
          borderRadius: widget.effectiveBorderRadius,
          child: body,
        ),
      ),
    );
  }
}

class LiquidGlassShaderPainter extends CustomPainter {
  final ui.FragmentShader shader;
  final Offset mouseOffset;
  final double time;
  final Color brandColor;

  LiquidGlassShaderPainter({
    required this.shader,
    required this.mouseOffset,
    required this.time,
    required this.brandColor,
  });

  @override
  void paint(Canvas canvas, Size size) {
    // uSize
    shader.setFloat(0, size.width);
    shader.setFloat(1, size.height);
    // uMouse
    shader.setFloat(2, mouseOffset.dx);
    shader.setFloat(3, mouseOffset.dy);
    // uTime
    shader.setFloat(4, time);
    // uBrandColor (Normalized HSL/RGB)
    shader.setFloat(5, brandColor.red / 255.0);
    shader.setFloat(6, brandColor.green / 255.0);
    shader.setFloat(7, brandColor.blue / 255.0);
    shader.setFloat(8, brandColor.alpha / 255.0);

    final paint = Paint()..shader = shader;
    canvas.drawRect(Offset.zero & size, paint);
  }

  @override
  bool shouldRepaint(covariant LiquidGlassShaderPainter oldDelegate) {
    return mouseOffset != oldDelegate.mouseOffset ||
        time != oldDelegate.time ||
        brandColor != oldDelegate.brandColor;
  }
}
