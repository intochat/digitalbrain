import 'dart:convert';
import 'dart:math' as math;
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import '../../state/brain_inspector_bloc.dart';
import 'shell_theme.dart';

class ShellSynapseTooltip extends StatelessWidget {
  const ShellSynapseTooltip({required this.info, super.key});

  final PausedSynapseInfo info;

  @override
  Widget build(BuildContext context) {
    final tone = info.gold ? InoShellTheme.gold : InoShellTheme.indigo;
    final traceParent = _traceParent(info);
    final decay = _syntheticDecay(info);
    final payloadJson = const JsonEncoder.withIndent('  ').convert(info.payload);

    return Positioned(
      left: info.screenX,
      top: info.screenY,
      child: FractionalTranslation(
        translation: const Offset(-0.5, -1.1),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(12),
          child: BackdropFilter(
            filter: ImageFilter.blur(
              sigmaX: InoShellTheme.glassBlurSigmaStrong,
              sigmaY: InoShellTheme.glassBlurSigmaStrong,
            ),
            child: Container(
              constraints: const BoxConstraints(minWidth: 280, maxWidth: 360),
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: InoShellTheme.glassFillStrong,
                border: Border.all(color: InoShellTheme.lineStrong),
                borderRadius: BorderRadius.circular(12),
              ),
              child: DefaultTextStyle(
                style: const TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 11,
                  color: InoShellTheme.text,
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    _Header(tone: tone),
                    const SizedBox(height: 8),
                    Text('${info.from} → ${info.to}'),
                    Text(
                      'traceparent: $traceParent',
                      style: const TextStyle(color: InoShellTheme.muted2),
                    ),
                    Text(
                      'decay: $decay · ${info.gold ? "recall" : "compute"}',
                      style: TextStyle(color: tone),
                    ),
                    const SizedBox(height: 8),
                    Container(
                      width: double.infinity,
                      padding: const EdgeInsets.all(8),
                      decoration: BoxDecoration(
                        color: const Color(0x59000000),
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Text(payloadJson),
                    ),
                    const SizedBox(height: 6),
                    const Text(
                      'click to resume',
                      style: TextStyle(color: InoShellTheme.muted2),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  static String _traceParent(PausedSynapseInfo info) {
    final seed = (info.from.hashCode ^ info.to.hashCode).abs();
    final rand = math.Random(seed);
    String hex(int len) {
      final buf = StringBuffer();
      for (var i = 0; i < len; i++) {
        buf.write(rand.nextInt(16).toRadixString(16));
      }
      return buf.toString();
    }
    return '00-${hex(16)}-${hex(8)}-01';
  }

  static int _syntheticDecay(PausedSynapseInfo info) =>
      60 + (info.to.hashCode.abs() % 30);
}

class _Header extends StatelessWidget {
  const _Header({required this.tone});
  final Color tone;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 6,
          height: 6,
          decoration: BoxDecoration(
            color: tone,
            shape: BoxShape.circle,
            boxShadow: [BoxShadow(color: tone, blurRadius: 8)],
          ),
        ),
        const SizedBox(width: 6),
        const Text(
          'synapse · paused mid-flight',
          style: TextStyle(fontWeight: FontWeight.w600),
        ),
      ],
    );
  }
}
