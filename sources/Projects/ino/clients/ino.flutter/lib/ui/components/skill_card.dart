import 'package:flutter/material.dart';
import 'package:ino_flutter/state/skills_bloc.dart';

IconData _domainIcon(String domain) {
  return switch (domain) {
    'travel' => Icons.flight,
    'coding' => Icons.code,
    'system' => Icons.settings,
    'finance' => Icons.attach_money,
    'health' => Icons.health_and_safety,
    _ => Icons.extension,
  };
}

class SkillCard extends StatelessWidget {
  const SkillCard({
    super.key,
    required this.skill,
    this.installing = false,
    this.onInstall,
  });

  final SkillItem skill;
  final bool installing;
  final VoidCallback? onInstall;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    return Card(
      color: colorScheme.surface,
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          children: [
            Icon(
              _domainIcon(skill.domain),
              color: colorScheme.primary,
              size: 32,
            ),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    skill.name,
                    style: TextStyle(
                      color: colorScheme.onSurface,
                      fontSize: 16,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    skill.description,
                    style: TextStyle(
                      color: colorScheme.onSurface.withAlpha(180),
                      fontSize: 13,
                    ),
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  const SizedBox(height: 8),
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 2,
                    ),
                    decoration: BoxDecoration(
                      color: colorScheme.primary.withAlpha(30),
                      borderRadius: BorderRadius.circular(4),
                    ),
                    child: Text(
                      skill.domain,
                      style: TextStyle(
                        color: colorScheme.primary,
                        fontSize: 11,
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            if (skill.installed)
              Chip(
                label: const Text('Installed'),
                backgroundColor: colorScheme.primary.withAlpha(30),
                labelStyle: TextStyle(
                  color: colorScheme.primary,
                  fontSize: 12,
                ),
                side: BorderSide.none,
              )
            else if (installing)
              const SizedBox(
                width: 24,
                height: 24,
                child: CircularProgressIndicator(strokeWidth: 2),
              )
            else
              FilledButton(
                onPressed: onInstall,
                child: const Text('Install'),
              ),
          ],
        ),
      ),
    );
  }
}
