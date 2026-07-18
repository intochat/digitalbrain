// Demo-only widget; gated by `kShowDemoButtons` in brain_home_screen.dart.

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/state/ino_bloc.dart';

class TestButtonStrip extends StatefulWidget {
  const TestButtonStrip({super.key, this.onShowInspector});

  /// Optional callback to open the inspector drawer. The "Show last routing"
  /// chip calls this when set; otherwise the chip is a no-op.
  final VoidCallback? onShowInspector;

  @override
  State<TestButtonStrip> createState() => _TestButtonStripState();
}

class _TestButtonStripState extends State<TestButtonStrip> {
  // L1 cluster key is session-scoped: all four taps use the same string so
  // MissedIntentTracker.NormalizeForCluster actually clusters them. Re-tap
  // after the 4th does nothing — reload the page to re-arm.
  late final String _l1ClusterKey =
      'demo l1 marker ${DateTime.now().microsecondsSinceEpoch.toRadixString(36).substring(0, 8)}';
  int _l1Taps = 0;

  void _send(String text) {
    context.read<InoBloc>().add(SendMessage(text));
  }

  void _onL1Tap() {
    if (_l1Taps < 3) {
      setState(() => _l1Taps++);
      _send(_l1ClusterKey);
    } else if (_l1Taps == 3) {
      setState(() => _l1Taps++);
      Future.delayed(const Duration(seconds: 1), () {
        if (mounted) _send(_l1ClusterKey);
      });
    }
  }

  String _l1Label() {
    if (_l1Taps == 0) return 'Trigger L1';
    if (_l1Taps < 4) return 'Trigger L1 ($_l1Taps/4)';
    return 'L1 fired — reload';
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.fromLTRB(8, 6, 8, 6),
      decoration: BoxDecoration(
        color: scheme.surface,
        border: Border(top: BorderSide(color: scheme.outlineVariant)),
      ),
      child: Wrap(
        spacing: 6,
        runSpacing: 6,
        children: [
          _Chip(
            icon: Icons.alarm,
            label: 'Set reminder',
            onTap: () => _send('remind me to test ino in 60 seconds'),
          ),
          _Chip(
            icon: Icons.psychology,
            label: 'Recall',
            onTap: () => _send(
                'my favourite colour is purple. what\'s my favourite colour?'),
          ),
          _Chip(
            icon: Icons.flight,
            label: 'Find flights',
            onTap: () => _send('find flights to bali next month'),
          ),
          _Chip(
            icon: Icons.local_taxi,
            label: 'Get an uber',
            onTap: () => _send('get me an uber home'),
          ),
          _Chip(
            icon: Icons.auto_awesome,
            label: _l1Label(),
            onTap: _l1Taps < 4 ? _onL1Tap : null,
          ),
          _Chip(
            icon: Icons.insights,
            label: 'Show last routing',
            onTap: widget.onShowInspector,
          ),
        ],
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.icon, required this.label, this.onTap});

  final IconData icon;
  final String label;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return FilledButton.tonal(
      onPressed: onTap,
      style: FilledButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.all(Radius.circular(20)),
        ),
        textStyle: const TextStyle(fontSize: 13),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 16),
          const SizedBox(width: 6),
          Text(label),
        ],
      ),
    );
  }
}
