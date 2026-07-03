import 'package:flutter/material.dart';
import '../glass/glass_material.dart';

/// A premium, glassmorphic debug indicator designed like a high-end HUD widget.
/// Shows the real-time active neuron catalog count and historical synapse edge events
/// currently registered in the live memory.
class DebugBrainStats extends StatefulWidget {
  const DebugBrainStats({
    required this.neuronCount,
    required this.synapseCount,
    super.key,
  });

  final int neuronCount;
  final int synapseCount;

  @override
  State<DebugBrainStats> createState() => _DebugBrainStatsState();
}

class _DebugBrainStatsState extends State<DebugBrainStats>
    with SingleTickerProviderStateMixin {
  late final AnimationController _pulseController;
  late final Animation<double> _pulseAnimation;

  @override
  void initState() {
    super.initState();
    _pulseController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 2000),
    )..repeat(reverse: true);
    _pulseAnimation = Tween<double>(begin: 0.35, end: 1.0).animate(
      CurvedAnimation(parent: _pulseController, curve: Curves.easeInOut),
    );
  }

  @override
  void dispose() {
    _pulseController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return GlassMaterial(
      cornerRadius: 12,
      tintOpacity: 0.12,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
        decoration: BoxDecoration(
          border: Border.all(
            color: Colors.white.withValues(alpha: 0.06),
            width: 1,
          ),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            // Glowing Live Pulse Dot
            AnimatedBuilder(
              animation: _pulseAnimation,
              builder: (context, child) {
                return Container(
                  width: 6,
                  height: 6,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: const Color(0xFF10E5B2), // Emerald-Teal accent
                    boxShadow: [
                      BoxShadow(
                        color: const Color(0xFF10E5B2).withValues(alpha: 0.5 * _pulseAnimation.value),
                        blurRadius: 6 * _pulseAnimation.value,
                        spreadRadius: 1 * _pulseAnimation.value,
                      ),
                    ],
                  ),
                );
              },
            ),
            const SizedBox(width: 8),
            // Header Text
            Text(
              'LIVE MATRIX · ',
              style: TextStyle(
                fontFamily: 'Orbitron',
                color: Colors.white.withValues(alpha: 0.4),
                fontSize: 9,
                fontWeight: FontWeight.w600,
                letterSpacing: 1.0,
              ),
            ),
            // Neuron count
            Text(
              'N:',
              style: TextStyle(
                fontFamily: 'Outfit',
                color: Colors.white.withValues(alpha: 0.6),
                fontSize: 11,
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(width: 2),
            Text(
              '${widget.neuronCount}',
              style: const TextStyle(
                fontFamily: 'Outfit',
                color: Color(0xFF5A81FF), // Soft Indigo/Blue
                fontSize: 11,
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(width: 8),
            // Divider pipe
            Container(
              width: 1,
              height: 10,
              color: Colors.white.withValues(alpha: 0.1),
            ),
            const SizedBox(width: 8),
            // Synapse count
            Text(
              'S:',
              style: TextStyle(
                fontFamily: 'Outfit',
                color: Colors.white.withValues(alpha: 0.6),
                fontSize: 11,
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(width: 2),
            Text(
              '${widget.synapseCount}',
              style: const TextStyle(
                fontFamily: 'Outfit',
                color: Color(0xFFFF5A93), // Premium Rose
                fontSize: 11,
                fontWeight: FontWeight.bold,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
