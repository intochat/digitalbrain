import 'package:flutter/material.dart';
import 'package:forui/forui.dart';

import 'ui_explore.dart';

/// Quick starts share the same specialist definitions as the creation dialog.
class UiProjectTemplates extends StatelessWidget {
  const UiProjectTemplates({super.key, required this.onStart});
  final ValueChanged<UiSpecialist> onStart;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'Explore',
          style: theme.textTheme.headlineLarge?.copyWith(
            fontSize: 32,
            fontWeight: FontWeight.w400,
            letterSpacing: -1,
            height: 1.5,
          ),
        ),
        const SizedBox(height: 14),
        Text(
          'Start with a template. Make it yours.',
          style: theme.textTheme.bodyLarge?.copyWith(
            color: colors.onSurfaceVariant,
            fontSize: 15,
            height: 1.5,
          ),
        ),
        const SizedBox(height: 36),
        for (final specialist in [uiCoordinator, ...uiSpecialists]) ...[
          _TemplateRow(
            specialist: specialist,
            onTap: () => onStart(specialist),
          ),
          const SizedBox(height: 12),
        ],
      ],
    );
  }
}

class _TemplateRow extends StatefulWidget {
  const _TemplateRow({required this.specialist, required this.onTap});
  final UiSpecialist specialist;
  final VoidCallback onTap;
  @override
  State<_TemplateRow> createState() => _TemplateRowState();
}

class _TemplateRowState extends State<_TemplateRow> {
  bool _focused = false;
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final specialist = widget.specialist;
    final general = specialist.id == uiCoordinator.id;
    return Semantics(
      button: true,
      child: Material(
        color: general ? colors.surfaceContainerLow : Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(10),
          side: BorderSide(
            color: _focused
                ? colors.primary
                : general
                ? colors.outlineVariant
                : Colors.transparent,
          ),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: widget.onTap,
          onFocusChange: (value) => setState(() => _focused = value),
          hoverColor: colors.primary.withValues(alpha: .045),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 20),
            child: Row(
              children: [
                UiSpecialistIcon(specialist: specialist, size: 44),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        general ? 'New IntoChat project' : specialist.name,
                        style: theme.textTheme.titleSmall?.copyWith(
                          fontSize: 15,
                          fontWeight: FontWeight.w600,
                          letterSpacing: -.15,
                        ),
                      ),
                      const SizedBox(height: 6),
                      Text(
                        switch (specialist.id) {
                          'salesforce' => 'Understand and improve your CRM.',
                          'leads' => 'Find companies. Research prospects.',
                          'automation' =>
                            'Turn repetitive work into a workflow.',
                          _ => 'Start with an idea. See where it goes.',
                        },
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: colors.onSurfaceVariant,
                          height: 1.5,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 12),
                Icon(
                  FLucideIcons.arrowRight,
                  size: 16,
                  color: colors.onSurfaceVariant,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
