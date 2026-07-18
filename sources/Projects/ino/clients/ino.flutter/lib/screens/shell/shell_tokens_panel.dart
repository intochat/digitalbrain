import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import 'shell_theme.dart';

class ShellTokensPanel extends StatelessWidget {
  const ShellTokensPanel({
    super.key,
    required this.isOpen,
    required this.onClose,
    required this.autoFocus,
    required this.onAutoFocusChanged,
  });

  static const double _width = 360;

  final bool isOpen;
  final VoidCallback onClose;
  final bool autoFocus;
  final ValueChanged<bool> onAutoFocusChanged;

  @override
  Widget build(BuildContext context) {
    return AnimatedPositioned(
      duration: const Duration(milliseconds: 420),
      curve: InoShellTheme.easeOut,
      left: isOpen ? 0 : -_width,
      top: 0,
      bottom: 0,
      width: _width,
      child: ClipRRect(
        borderRadius: const BorderRadius.only(
          topRight: Radius.circular(16),
          bottomRight: Radius.circular(16),
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
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _PanelHeader(onClose: onClose),
                Expanded(
                  child: ListView(
                    padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
                    children: [
                      _PaletteSection(),
                      const SizedBox(height: 20),
                      _TypographySection(),
                      const SizedBox(height: 20),
                      _MotionSection(),
                      const SizedBox(height: 20),
                      _LatencySection(),
                      const SizedBox(height: 20),
                      _AutoFocusSection(
                        value: autoFocus,
                        onChanged: onAutoFocusChanged,
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _PanelHeader extends StatelessWidget {
  const _PanelHeader({required this.onClose});

  final VoidCallback onClose;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 20, 8, 16),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: const [
                Text(
                  'DESIGN TOKENS',
                  style: TextStyle(
                    fontFamily: 'JetBrains Mono',
                    fontSize: 10,
                    letterSpacing: 2,
                    color: InoShellTheme.muted2,
                  ),
                ),
                SizedBox(height: 4),
                Text(
                  'ino visual system',
                  style: TextStyle(
                    fontSize: 20,
                    fontWeight: FontWeight.w600,
                    letterSpacing: -0.4,
                    color: InoShellTheme.text,
                  ),
                ),
              ],
            ),
          ),
          IconButton(
            tooltip: 'Close tokens panel',
            iconSize: 16,
            color: InoShellTheme.muted,
            onPressed: onClose,
            icon: const Icon(Icons.close),
          ),
        ],
      ),
    );
  }
}

class _SectionLabel extends StatelessWidget {
  const _SectionLabel({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Text(
        text,
        style: const TextStyle(
          fontFamily: 'JetBrains Mono',
          fontSize: 10,
          letterSpacing: 1.8,
          color: InoShellTheme.muted2,
        ),
      ),
    );
  }
}

class _SwatchTile extends StatelessWidget {
  const _SwatchTile({
    required this.name,
    required this.hex,
    required this.color,
    this.fullWidth = false,
    this.gradient,
  });

  final String name;
  final String hex;
  final Color color;
  final bool fullWidth;
  final Gradient? gradient;

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: const Color(0x04FFFFFF),
        border: Border.all(color: InoShellTheme.line),
        borderRadius: BorderRadius.circular(8),
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Container(
            height: 40,
            decoration: gradient != null
                ? BoxDecoration(gradient: gradient)
                : BoxDecoration(color: color),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(8, 6, 8, 8),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  name,
                  style: const TextStyle(
                    fontFamily: 'JetBrains Mono',
                    fontSize: 10,
                    color: InoShellTheme.text,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  hex,
                  style: const TextStyle(
                    fontFamily: 'JetBrains Mono',
                    fontSize: 10,
                    color: InoShellTheme.muted2,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _PaletteSection extends StatelessWidget {
  const _PaletteSection();

  static const _swatches = [
    (name: 'ink-0', hex: '#0A0E14', color: InoShellTheme.ink0),
    (name: 'ink-1', hex: '#11161F', color: InoShellTheme.ink1),
    (name: 'cyan · neuron', hex: '#3DDCFF', color: InoShellTheme.cyan),
    (name: 'indigo · synapse', hex: '#7C8AFF', color: InoShellTheme.indigo),
    (name: 'incident', hex: '#FF6B6B', color: InoShellTheme.red),
  ];

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const _SectionLabel(text: 'PALETTE'),
        GridView.count(
          crossAxisCount: 2,
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          mainAxisSpacing: 8,
          crossAxisSpacing: 8,
          childAspectRatio: 1.6,
          children: [
            for (final s in _swatches)
              _SwatchTile(name: s.name, hex: s.hex, color: s.color),
          ],
        ),
        const SizedBox(height: 8),
        _SwatchTile(
          name: 'gold · recall (only warm)',
          hex: '#E8C56A',
          color: InoShellTheme.gold,
          fullWidth: true,
        ),
        const SizedBox(height: 8),
        _SwatchTile(
          name: 'orb iridescent',
          hex: 'cyan → indigo → pink',
          color: InoShellTheme.cyan,
          fullWidth: true,
          gradient: const LinearGradient(
            colors: [InoShellTheme.cyan, InoShellTheme.indigo, InoShellTheme.pink],
          ),
        ),
      ],
    );
  }
}

class _TypographySection extends StatelessWidget {
  const _TypographySection();

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const _SectionLabel(text: 'TYPOGRAPHY'),
        Container(
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            color: const Color(0x04FFFFFF),
            border: Border.all(color: InoShellTheme.line),
            borderRadius: BorderRadius.circular(12),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: const [
              Text(
                'A living calm intelligence.',
                style: TextStyle(
                  fontSize: 24,
                  fontWeight: FontWeight.w600,
                  letterSpacing: -0.48,
                  height: 1.0,
                  color: InoShellTheme.text,
                ),
              ),
              SizedBox(height: 4),
              Text(
                'Inter / 24px / 600 / -0.02em',
                style: TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 10,
                  color: InoShellTheme.muted2,
                ),
              ),
              SizedBox(height: 16),
              Text(
                'Decisions emerge from context, memory, and trust — '
                'not from prompts stitched together at runtime.',
                style: TextStyle(
                  fontSize: 14,
                  height: 22 / 14,
                  letterSpacing: -0.14,
                  color: InoShellTheme.text,
                ),
              ),
              SizedBox(height: 4),
              Text(
                'Inter / 14px / 400 / -0.01em / lh 22',
                style: TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 10,
                  color: InoShellTheme.muted2,
                ),
              ),
              SizedBox(height: 16),
              Text(
                'trace.0x9af2 · 320ms · synapse#7',
                style: TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 12,
                  color: InoShellTheme.cyan,
                ),
              ),
              SizedBox(height: 4),
              Text(
                'JetBrains Mono / 12px / 400',
                style: TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 10,
                  color: InoShellTheme.muted2,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _KeyValueRow extends StatelessWidget {
  const _KeyValueRow({required this.label, required this.value, this.last = false});

  final String label;
  final String value;
  final bool last;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        border: Border(
          bottom: last
              ? BorderSide.none
              : const BorderSide(color: InoShellTheme.line),
        ),
      ),
      child: Row(
        children: [
          Expanded(
            flex: 5,
            child: Text(
              label,
              style: const TextStyle(
                fontFamily: 'JetBrains Mono',
                fontSize: 11,
                color: InoShellTheme.muted,
              ),
            ),
          ),
          Expanded(
            flex: 5,
            child: Text(
              value,
              textAlign: TextAlign.right,
              style: const TextStyle(
                fontFamily: 'JetBrains Mono',
                fontSize: 11,
                color: InoShellTheme.text,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _MotionSection extends StatelessWidget {
  const _MotionSection();

  static const _rows = [
    (label: 'ease (default)', value: 'cubic-bezier(.22, 1, .36, 1)'),
    (label: 'synapse comet', value: '320–540ms'),
    (label: 'card entry', value: '240ms · spring · overshoot'),
    (label: 'brain idle pulse', value: '4.8s · ±6% scale'),
    (label: 'camera orbit', value: '0.05 rad/s'),
  ];

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const _SectionLabel(text: 'MOTION'),
        Container(
          decoration: BoxDecoration(
            color: const Color(0x04FFFFFF),
            border: Border.all(color: InoShellTheme.line),
            borderRadius: BorderRadius.circular(12),
          ),
          clipBehavior: Clip.antiAlias,
          child: Column(
            children: [
              for (var i = 0; i < _rows.length; i++)
                _KeyValueRow(
                  label: _rows[i].label,
                  value: _rows[i].value,
                  last: i == _rows.length - 1,
                ),
            ],
          ),
        ),
      ],
    );
  }
}

class _LatencySection extends StatelessWidget {
  const _LatencySection();

  static const _rows = [
    (label: 'utterance → first comet', value: '≤ 400ms'),
    (label: '→ first card', value: '≤ 2.5s'),
    (label: '→ complete plan', value: '≤ 6s'),
  ];

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const _SectionLabel(text: 'LATENCY BUDGET'),
        Container(
          decoration: BoxDecoration(
            color: const Color(0x04FFFFFF),
            border: Border.all(color: InoShellTheme.line),
            borderRadius: BorderRadius.circular(12),
          ),
          clipBehavior: Clip.antiAlias,
          child: Column(
            children: [
              for (var i = 0; i < _rows.length; i++)
                _KeyValueRow(
                  label: _rows[i].label,
                  value: _rows[i].value,
                  last: i == _rows.length - 1,
                ),
              const Divider(color: InoShellTheme.line, height: 1),
              Padding(
                padding: const EdgeInsets.fromLTRB(10, 10, 10, 12),
                child: Text(
                  'spinners are banned · brain is the loading screen',
                  style: const TextStyle(
                    fontFamily: 'JetBrains Mono',
                    fontSize: 10,
                    color: InoShellTheme.muted2,
                  ),
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _AutoFocusSection extends StatelessWidget {
  const _AutoFocusSection({
    required this.value,
    required this.onChanged,
  });

  final bool value;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const _SectionLabel(text: 'OPEN QUESTION · BRAIN AUTO-FOCUS'),
        Container(
          padding: const EdgeInsets.fromLTRB(12, 10, 12, 14),
          decoration: BoxDecoration(
            color: const Color(0x04FFFFFF),
            border: Border.all(color: InoShellTheme.line),
            borderRadius: BorderRadius.circular(12),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: const Text(
                      'auto-reorient camera on most-active cluster',
                      style: TextStyle(
                        fontSize: 13,
                        color: InoShellTheme.text,
                      ),
                    ),
                  ),
                  Switch(
                    value: value,
                    onChanged: onChanged,
                    activeThumbColor: InoShellTheme.cyan,
                    activeTrackColor: InoShellTheme.cyan.withValues(alpha: 0.3),
                    inactiveThumbColor: InoShellTheme.muted2,
                    inactiveTrackColor: InoShellTheme.glassFill,
                  ),
                ],
              ),
              const SizedBox(height: 8),
              const Text(
                'off = full manual control · on = subtle 0.05 rad/s nudge',
                style: TextStyle(
                  fontFamily: 'JetBrains Mono',
                  fontSize: 10,
                  color: InoShellTheme.muted2,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
