import 'package:flutter/material.dart';

import '../theme/brain_theme.dart';
import 'ui_surface_controller.dart';
import 'ui_surface_models.dart';

class UiSurfaceRenderer extends StatelessWidget {
  const UiSurfaceRenderer({required this.controller, super.key});

  final UiSurfaceController controller;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: controller,
      builder: (context, _) {
        final closed = controller.closedFailure;
        if (closed != null) {
          return Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Text(
                closed,
                style: const TextStyle(color: BrainColors.orange),
              ),
            ),
          );
        }

        final surfaces = controller.surfaces;
        final failure = controller.sanitizedFailure;
        if (surfaces.isEmpty && failure == null) {
          return const Center(child: Text('Waiting for surface…'));
        }

        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            if (failure != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Text(
                  failure,
                  style: const TextStyle(color: BrainColors.orange),
                ),
              ),
            ...surfaces.map(
              (surface) => _SurfaceCard(
                surface: surface,
                onAction: (action) => controller.sendAction(
                  surfaceId: surface.surfaceId,
                  actionId: action.id,
                  expectedRevision: action.expectedRevision,
                ),
              ),
            ),
          ],
        );
      },
    );
  }
}

class _SurfaceCard extends StatelessWidget {
  const _SurfaceCard({required this.surface, required this.onAction});

  final UiSurface surface;
  final ValueChanged<UiAction> onAction;

  @override
  Widget build(BuildContext context) {
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              surface.surfaceId,
              style: Theme.of(context).textTheme.labelSmall?.copyWith(
                color: BrainColors.inkMuted,
              ),
            ),
            Text(
              'rev ${surface.revision}',
              style: BrainTheme.mono(
                Theme.of(context).textTheme.labelSmall,
              ),
            ),
            const SizedBox(height: 12),
            ...surface.blocks.map(
              (block) => _BlockView(block: block, onAction: onAction),
            ),
          ],
        ),
      ),
    );
  }
}

class _BlockView extends StatelessWidget {
  const _BlockView({required this.block, required this.onAction});

  final UiBlock block;
  final ValueChanged<UiAction> onAction;

  @override
  Widget build(BuildContext context) {
    if (!block.isSupported) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 4),
        child: Text(
          'unsupported block',
          style: TextStyle(color: BrainColors.inkMuted),
        ),
      );
    }

    final isFailure = block.kind == 'failure';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            block.text,
            style: TextStyle(
              color: isFailure ? BrainColors.orange : BrainColors.ink,
            ),
          ),
          if (block.actions.isNotEmpty) ...[
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: block.actions
                  .map(
                    (action) => FilledButton(
                      onPressed: () => onAction(action),
                      child: Text(action.label),
                    ),
                  )
                  .toList(),
            ),
          ],
        ],
      ),
    );
  }
}
