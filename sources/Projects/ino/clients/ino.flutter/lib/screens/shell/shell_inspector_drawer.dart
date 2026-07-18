import 'dart:math' as math;
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../state/brain_inspector_bloc.dart';
import 'shell_brain_topology.dart';
import 'shell_theme.dart';

class ShellInspectorDrawer extends StatelessWidget {
  const ShellInspectorDrawer({super.key, this.onFireTest});

  /// Called when the user taps "Fire test synapse". Receives the alias of the
  /// currently-inspected neuron. Null disables the button.
  final void Function(String alias)? onFireTest;

  static const double _width = 420;

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<BrainInspectorBloc, BrainInspectorState>(
      buildWhen: (a, b) => a.selected != b.selected,
      builder: (context, state) {
        final alias = _aliasFromSelection(state.selected);
        final neuron = alias != null ? ShellTopology.aliasLookup(alias) : null;
        final isOpen = neuron != null;

        return AnimatedPositioned(
          duration: const Duration(milliseconds: 420),
          curve: InoShellTheme.easeOut,
          right: isOpen ? 0 : -_width,
          top: 0,
          bottom: 0,
          width: _width,
          child: ClipRRect(
            borderRadius: const BorderRadius.only(
              topLeft: Radius.circular(16),
              bottomLeft: Radius.circular(16),
            ),
            child: BackdropFilter(
              filter: ImageFilter.blur(
                sigmaX: InoShellTheme.glassBlurSigmaStrong,
                sigmaY: InoShellTheme.glassBlurSigmaStrong,
              ),
              child: Container(
                decoration: BoxDecoration(
                  color: InoShellTheme.glassFillStrong,
                  border: Border.all(color: InoShellTheme.lineStrong),
                ),
                child: neuron == null
                    ? const SizedBox.shrink()
                    : _DrawerBody(neuron: neuron, onFireTest: onFireTest),
              ),
            ),
          ),
        );
      },
    );
  }

  static String? _aliasFromSelection(Selection? selection) =>
      selection is NeuronSelection ? selection.nodeId : null;
}

class _DrawerBody extends StatelessWidget {
  const _DrawerBody({required this.neuron, this.onFireTest});
  final ShellNeuron neuron;
  final void Function(String alias)? onFireTest;

  @override
  Widget build(BuildContext context) {
    final events = ShellTopologyDrawerEvents.eventsFor(neuron.alias);
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 20, 20, 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _Header(neuron: neuron),
          const SizedBox(height: 16),
          Expanded(
            child: ListView(
              children: [
                _SectionLabel(
                  text: 'LAST ${events.length} SYNAPSES',
                  trailing: '${events.length} events',
                ),
                const SizedBox(height: 8),
                for (final e in events) _EventRow(event: e),
                const SizedBox(height: 20),
                const _SectionLabel(text: 'DECAY MAP · 24H'),
                const SizedBox(height: 8),
                _DecayCard(seed: neuron.alias),
                const SizedBox(height: 20),
                const _SectionLabel(text: 'PROMPT CORPUS · LLMNEURON'),
                const SizedBox(height: 8),
                _PromptCard(neuron: neuron),
                const SizedBox(height: 20),
                _FireTestButton(alias: neuron.alias, onFireTest: onFireTest),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _SectionLabel extends StatelessWidget {
  const _SectionLabel({required this.text, this.trailing});
  final String text;
  final String? trailing;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: Text(
            text,
            style: const TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 10,
              letterSpacing: 1.8,
              color: InoShellTheme.muted2,
            ),
          ),
        ),
        if (trailing != null)
          Text(
            trailing!,
            style: const TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 11,
              color: InoShellTheme.muted,
            ),
          ),
      ],
    );
  }
}

class _EventRow extends StatelessWidget {
  const _EventRow({required this.event});
  final DrawerEvent event;

  @override
  Widget build(BuildContext context) {
    final dirArrow = event.from != null ? '←' : '→';
    final peer = event.from ?? event.to ?? '—';
    final tone = event.recall ? InoShellTheme.gold : InoShellTheme.indigo;
    return Container(
      margin: const EdgeInsets.only(bottom: 6),
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
      decoration: BoxDecoration(
        color: const Color(0x04FFFFFF),
        border: Border.all(color: InoShellTheme.line),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        children: [
          SizedBox(
            width: 38,
            child: Text(
              event.t,
              style: const TextStyle(
                fontFamily: 'JetBrains Mono',
                fontSize: 11,
                color: InoShellTheme.muted2,
              ),
            ),
          ),
          Text(
            dirArrow,
            style: TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 11,
              color: tone,
            ),
          ),
          const SizedBox(width: 8),
          SizedBox(
            width: 90,
            child: Text(
              peer,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                fontFamily: 'JetBrains Mono',
                fontSize: 11,
                color: InoShellTheme.text,
              ),
            ),
          ),
          Expanded(
            child: Text(
              event.payload,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                fontFamily: 'JetBrains Mono',
                fontSize: 11,
                color: InoShellTheme.muted,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _DecayCard extends StatelessWidget {
  const _DecayCard({required this.seed});
  final String seed;

  @override
  Widget build(BuildContext context) {
    final decay = 60 + (seed.hashCode.abs() % 40);
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: const Color(0x04FFFFFF),
        border: Border.all(color: InoShellTheme.line),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.baseline,
            textBaseline: TextBaseline.alphabetic,
            children: [
              Text(
                '$decay',
                style: const TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 20,
                  color: InoShellTheme.text,
                ),
              ),
              const SizedBox(width: 8),
              const Text(
                '/100 · brightening on access',
                style: TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 11,
                  color: InoShellTheme.muted,
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          SizedBox(
            height: 56,
            child: CustomPaint(
              painter: _SparkPainter(seed: seed.hashCode.abs()),
              size: Size.infinite,
            ),
          ),
        ],
      ),
    );
  }
}

class _SparkPainter extends CustomPainter {
  const _SparkPainter({required this.seed});
  final int seed;

  @override
  void paint(Canvas canvas, Size size) {
    const n = 28;
    final pts = <Offset>[];
    final rng = math.Random(seed);
    for (var i = 0; i < n; i++) {
      final v = 0.3 + 0.4 * math.sin(i * 0.7 + seed % 7) + rng.nextDouble() * 0.18;
      final x = (i / (n - 1)) * size.width;
      final y = size.height - v.clamp(0.0, 1.0) * size.height;
      pts.add(Offset(x, y));
    }

    final fill = Path()..moveTo(0, size.height);
    for (final p in pts) {
      fill.lineTo(p.dx, p.dy);
    }
    fill
      ..lineTo(size.width, size.height)
      ..close();
    final fillShader = const LinearGradient(
      begin: Alignment.topCenter,
      end: Alignment.bottomCenter,
      colors: [Color(0x803DDCFF), Color(0x003DDCFF)],
    ).createShader(Rect.fromLTWH(0, 0, size.width, size.height));
    canvas.drawPath(fill, Paint()..shader = fillShader);

    final strokePaint = Paint()
      ..color = InoShellTheme.cyan
      ..strokeWidth = 1.25
      ..style = PaintingStyle.stroke
      ..strokeJoin = StrokeJoin.round;
    final strokePath = Path()..moveTo(pts.first.dx, pts.first.dy);
    for (final p in pts.skip(1)) {
      strokePath.lineTo(p.dx, p.dy);
    }
    canvas.drawPath(strokePath, strokePaint);
  }

  @override
  bool shouldRepaint(_SparkPainter old) => old.seed != seed;
}

class _Header extends StatelessWidget {
  const _Header({required this.neuron});
  final ShellNeuron neuron;

  @override
  Widget build(BuildContext context) {
    final colorInt = neuron.color.toARGB32();
    final hex = colorInt.toRadixString(16).padLeft(8, '0').substring(2);
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'INSPECTOR · NEURON',
                style: TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 10,
                  letterSpacing: 2,
                  color: InoShellTheme.muted2,
                ),
              ),
              const SizedBox(height: 4),
              Row(
                children: [
                  Container(
                    width: 6,
                    height: 6,
                    decoration: BoxDecoration(
                      color: neuron.color,
                      shape: BoxShape.circle,
                      boxShadow: [BoxShadow(color: neuron.color, blurRadius: 8)],
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text(
                    neuron.alias,
                    style: const TextStyle(
                      fontSize: 20,
                      fontWeight: FontWeight.w600,
                      letterSpacing: -0.4,
                      color: InoShellTheme.text,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 4),
              Text(
                '${neuron.domain} · grain://${neuron.domain}/${neuron.alias.toLowerCase()}/0x$hex',
                style: const TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 11,
                  color: InoShellTheme.muted2,
                ),
              ),
            ],
          ),
        ),
        IconButton(
          tooltip: 'Close inspector',
          iconSize: 16,
          color: InoShellTheme.muted,
          onPressed: () =>
              context.read<BrainInspectorBloc>().add(Deselect()),
          icon: const Icon(Icons.close),
        ),
      ],
    );
  }
}

class _PromptCard extends StatelessWidget {
  const _PromptCard({required this.neuron});
  final ShellNeuron neuron;

  @override
  Widget build(BuildContext context) {
    final prompt = ShellTopologyDrawerEvents.promptFor(neuron.alias);
    final body = prompt
        ?? '// ${neuron.alias} — pure-code neuron · no LLM. '
           'Implementation in domains/${neuron.domain}/...';

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      constraints: const BoxConstraints(maxHeight: 180),
      decoration: BoxDecoration(
        color: const Color(0x04FFFFFF),
        border: Border.all(color: InoShellTheme.line),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Scrollbar(
        child: SingleChildScrollView(
          child: Text(
            body,
            style: const TextStyle(
              fontFamily: 'JetBrains Mono',
              fontSize: 12,
              height: 1.45,
              color: InoShellTheme.muted,
            ),
          ),
        ),
      ),
    );
  }
}

class _FireTestButton extends StatelessWidget {
  const _FireTestButton({required this.alias, this.onFireTest});
  final String alias;
  final void Function(String alias)? onFireTest;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: double.infinity,
      child: TextButton.icon(
        onPressed: onFireTest == null ? null : () => onFireTest!(alias),
        style: TextButton.styleFrom(
          padding: const EdgeInsets.symmetric(vertical: 14),
          backgroundColor: InoShellTheme.glassFill,
          side: const BorderSide(color: InoShellTheme.line),
          shape: const StadiumBorder(),
          foregroundColor: InoShellTheme.text,
        ),
        icon: Container(
          width: 6,
          height: 6,
          decoration: BoxDecoration(
            color: InoShellTheme.cyan,
            shape: BoxShape.circle,
            boxShadow: [BoxShadow(color: InoShellTheme.cyan, blurRadius: 8)],
          ),
        ),
        label: const Text(
          'Fire test synapse',
          style: TextStyle(fontSize: 12),
        ),
      ),
    );
  }
}
