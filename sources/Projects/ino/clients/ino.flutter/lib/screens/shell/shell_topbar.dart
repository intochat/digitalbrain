import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import '../../persona/persona_widget.dart';
import 'shell_brain_topology.dart';
import 'shell_theme.dart';

class ShellTopbar extends StatelessWidget {
  const ShellTopbar({
    super.key,
    this.onTokens,
    this.onPlay,
    this.onReplay,
    this.onPause,
    this.onReplan,
    this.input,
    this.personaBuilder,
  });

  /// Opens the design-tokens panel (T9.1).
  final VoidCallback? onTokens;

  /// Plays the Tokyo storyboard.
  final VoidCallback? onPlay;

  /// Replays the storyboard from t=0.
  final VoidCallback? onReplay;

  /// Pauses / resumes the storyboard.
  final VoidCallback? onPause;

  /// Triggers the "make day 3 cheaper" replan.
  final VoidCallback? onReplan;

  /// Optional text controller for the ghost input. Defaults to a local
  /// controller owned by [_CenterPersona].
  final TextEditingController? input;

  /// Overrides the persona widget — inject a stub in tests to avoid
  /// requiring [PersonaBloc] + [TimelineBloc] in the widget tree.
  /// Defaults to [PersonaWidget] at size 130.
  final WidgetBuilder? personaBuilder;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(24, 20, 24, 0),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _LeftCluster(onTokens: onTokens),
          const Spacer(),
          _CenterPersona(
            input: input,
            onSubmit: onPlay,
            personaBuilder: personaBuilder,
          ),
          const Spacer(),
          _RightControls(
            onReplan: onReplan,
            onPause: onPause,
            onReplay: onReplay,
            onPlay: onPlay,
          ),
        ],
      ),
    );
  }
}

class _LeftCluster extends StatelessWidget {
  const _LeftCluster({required this.onTokens});

  final VoidCallback? onTokens;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        _GhostButton(
          onPressed: onTokens,
          label: 'tokens',
          leading: const _ConcentricCircles(),
        ),
        const SizedBox(width: 12),
        const _StatusPill(),
      ],
    );
  }
}

class _StatusPill extends StatelessWidget {
  const _StatusPill();

  @override
  Widget build(BuildContext context) {
    final clusters = ShellTopology.clusters.length;
    final neurons = ShellTopology.neurons.length;
    return _GlassPill(
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const _DotIndicator(color: InoShellTheme.cyan),
          const SizedBox(width: 8),
          const Text(
            'silo · system',
            style: TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 11,
              color: InoShellTheme.muted,
            ),
          ),
          const SizedBox(width: 6),
          const Text(
            '·',
            style: TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 11,
              color: InoShellTheme.muted2,
            ),
          ),
          const SizedBox(width: 6),
          Text(
            '$clusters clusters · $neurons neurons',
            style: const TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 11,
              color: InoShellTheme.text,
            ),
          ),
        ],
      ),
    );
  }
}

class _CenterPersona extends StatefulWidget {
  const _CenterPersona({
    required this.input,
    required this.onSubmit,
    required this.personaBuilder,
  });

  final TextEditingController? input;
  final VoidCallback? onSubmit;
  final WidgetBuilder? personaBuilder;

  @override
  State<_CenterPersona> createState() => _CenterPersonaState();
}

class _CenterPersonaState extends State<_CenterPersona> {
  late final TextEditingController _controller =
      widget.input ?? TextEditingController();

  @override
  void dispose() {
    if (widget.input == null) _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final persona = widget.personaBuilder != null
        ? widget.personaBuilder!(context)
        : const PersonaWidget(size: 130);

    return Column(
      children: [
        persona,
        const SizedBox(height: 8),
        const Text(
          'ino · idle',
          style: TextStyle(
            fontFamily: 'JetBrains Mono',
            fontSize: 11,
            color: InoShellTheme.muted,
          ),
        ),
        const SizedBox(height: 12),
        SizedBox(
          width: 360,
          child: _GhostInput(
            controller: _controller,
            onSubmit: widget.onSubmit,
          ),
        ),
      ],
    );
  }
}

class _GhostInput extends StatelessWidget {
  const _GhostInput({required this.controller, required this.onSubmit});

  final TextEditingController controller;
  final VoidCallback? onSubmit;

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(999),
      child: BackdropFilter(
        filter: ImageFilter.blur(
          sigmaX: InoShellTheme.glassBlurSigma,
          sigmaY: InoShellTheme.glassBlurSigma,
        ),
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          decoration: BoxDecoration(
            color: InoShellTheme.glassFill,
            border: Border.all(color: InoShellTheme.line),
            borderRadius: BorderRadius.circular(999),
          ),
          child: Row(
            children: [
              const Icon(Icons.mic_none, size: 13, color: InoShellTheme.muted2),
              const SizedBox(width: 8),
              Expanded(
                child: TextField(
                  controller: controller,
                  style: const TextStyle(fontSize: 13, color: InoShellTheme.text),
                  decoration: const InputDecoration(
                    hintText: 'Hold to talk · or type a synapse',
                    hintStyle: TextStyle(color: InoShellTheme.muted2),
                    border: InputBorder.none,
                    isDense: true,
                    contentPadding: EdgeInsets.zero,
                  ),
                  onSubmitted: (_) => onSubmit?.call(),
                ),
              ),
              const SizedBox(width: 8),
              const Text(
                '↵',
                style: TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 10,
                  color: InoShellTheme.muted2,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _RightControls extends StatelessWidget {
  const _RightControls({
    required this.onReplan,
    required this.onPause,
    required this.onReplay,
    required this.onPlay,
  });

  final VoidCallback? onReplan;
  final VoidCallback? onPause;
  final VoidCallback? onReplay;
  final VoidCallback? onPlay;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        _GhostButton(
          onPressed: onReplan,
          label: 'make day 3 cheaper',
          leading: const _DotIndicator(color: InoShellTheme.gold),
        ),
        const SizedBox(width: 8),
        _GhostIconButton(onPressed: onPause, icon: Icons.pause),
        const SizedBox(width: 8),
        _GhostIconButton(onPressed: onReplay, icon: Icons.replay),
        const SizedBox(width: 8),
        _PrimaryButton(onPressed: onPlay, label: 'Play demo · Tokyo, 6s'),
      ],
    );
  }
}

class _GlassPill extends StatelessWidget {
  const _GlassPill({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(999),
      child: BackdropFilter(
        filter: ImageFilter.blur(
          sigmaX: InoShellTheme.glassBlurSigma,
          sigmaY: InoShellTheme.glassBlurSigma,
        ),
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          decoration: BoxDecoration(
            color: InoShellTheme.glassFill,
            border: Border.all(color: InoShellTheme.line),
            borderRadius: BorderRadius.circular(999),
          ),
          child: child,
        ),
      ),
    );
  }
}

class _GhostButton extends StatelessWidget {
  const _GhostButton({
    required this.onPressed,
    required this.label,
    this.leading,
  });

  final VoidCallback? onPressed;
  final String label;
  final Widget? leading;

  @override
  Widget build(BuildContext context) {
    return TextButton(
      onPressed: onPressed,
      style: TextButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
        backgroundColor: InoShellTheme.glassFill,
        side: const BorderSide(color: InoShellTheme.line),
        shape: const StadiumBorder(),
        foregroundColor: InoShellTheme.text,
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (leading != null) ...[
            leading!,
            const SizedBox(width: 8),
          ],
          Text(label, style: const TextStyle(fontSize: 12, letterSpacing: 0.2)),
        ],
      ),
    );
  }
}

class _GhostIconButton extends StatelessWidget {
  const _GhostIconButton({required this.onPressed, required this.icon});

  final VoidCallback? onPressed;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return TextButton(
      onPressed: onPressed,
      style: TextButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        backgroundColor: InoShellTheme.glassFill,
        side: const BorderSide(color: InoShellTheme.line),
        shape: const StadiumBorder(),
        foregroundColor: InoShellTheme.text,
      ),
      child: Icon(icon, size: 12),
    );
  }
}

class _PrimaryButton extends StatelessWidget {
  const _PrimaryButton({required this.onPressed, required this.label});

  final VoidCallback? onPressed;
  final String label;

  @override
  Widget build(BuildContext context) {
    return ElevatedButton(
      onPressed: onPressed,
      style: ElevatedButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
        backgroundColor: InoShellTheme.cyan,
        foregroundColor: InoShellTheme.ink0,
        shape: const StadiumBorder(),
        textStyle: const TextStyle(fontSize: 12, fontWeight: FontWeight.w500),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.play_arrow, size: 13),
          const SizedBox(width: 6),
          Text(label),
        ],
      ),
    );
  }
}

class _DotIndicator extends StatelessWidget {
  const _DotIndicator({required this.color});

  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 6,
      height: 6,
      decoration: BoxDecoration(
        color: color,
        shape: BoxShape.circle,
        boxShadow: [BoxShadow(color: color, blurRadius: 8)],
      ),
    );
  }
}

class _ConcentricCircles extends StatelessWidget {
  const _ConcentricCircles();

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      size: const Size(14, 14),
      painter: _ConcentricCirclesPainter(),
    );
  }
}

class _ConcentricCirclesPainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = InoShellTheme.text
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.25;
    final c = Offset(size.width / 2, size.height / 2);
    canvas.drawCircle(c, size.width / 2 - 1, paint);
    canvas.drawCircle(c, size.width / 6, paint);
  }

  @override
  bool shouldRepaint(_) => false;
}
