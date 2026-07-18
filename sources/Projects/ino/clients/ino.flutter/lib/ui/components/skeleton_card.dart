import 'package:flutter/material.dart';

class SkeletonCard extends StatefulWidget {
  const SkeletonCard({super.key});

  @override
  State<SkeletonCard> createState() => _SkeletonCardState();
}

class _SkeletonCardState extends State<SkeletonCard>
    with SingleTickerProviderStateMixin {
  late final AnimationController _shimmerController;

  @override
  void initState() {
    super.initState();
    _shimmerController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1500),
    )..repeat();
  }

  @override
  void dispose() {
    _shimmerController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _shimmerController,
      builder: (context, child) {
        return Card(
          color: Colors.white.withValues(alpha: 0.05),
          margin: const EdgeInsets.symmetric(vertical: 4, horizontal: 8),
          shape:
              RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    _ShimmerBar(
                      width: 18,
                      height: 18,
                      borderRadius: 4,
                      progress: _shimmerController.value,
                    ),
                    const SizedBox(width: 8),
                    _ShimmerBar(
                      width: 120,
                      height: 14,
                      progress: _shimmerController.value,
                    ),
                    const Spacer(),
                    _ShimmerBar(
                      width: 60,
                      height: 16,
                      progress: _shimmerController.value,
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                _ShimmerBar(
                  width: 200,
                  height: 12,
                  progress: _shimmerController.value,
                ),
                const SizedBox(height: 8),
                _ShimmerBar(
                  width: 160,
                  height: 12,
                  progress: _shimmerController.value,
                ),
                const SizedBox(height: 12),
                Row(
                  children: [
                    _ShimmerBar(
                      width: 80,
                      height: 12,
                      progress: _shimmerController.value,
                    ),
                    const SizedBox(width: 12),
                    _ShimmerBar(
                      width: 60,
                      height: 12,
                      progress: _shimmerController.value,
                    ),
                  ],
                ),
              ],
            ),
          ),
        );
      },
    );
  }
}

class _ShimmerBar extends StatelessWidget {
  const _ShimmerBar({
    required this.width,
    required this.height,
    required this.progress,
    this.borderRadius = 6,
  });

  final double width;
  final double height;
  final double progress;
  final double borderRadius;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(borderRadius),
        gradient: LinearGradient(
          begin: Alignment(-1.0 + 2.0 * progress, 0),
          end: Alignment(-1.0 + 2.0 * progress + 1.0, 0),
          colors: [
            Colors.white.withValues(alpha: 0.06),
            Colors.white.withValues(alpha: 0.12),
            Colors.white.withValues(alpha: 0.06),
          ],
        ),
      ),
    );
  }
}
