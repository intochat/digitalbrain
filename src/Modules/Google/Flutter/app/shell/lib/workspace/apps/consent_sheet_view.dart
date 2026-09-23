import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

/// The install consent sheet: what an app can be asked to do, the data classes it touches with
/// reasons, its permissions, its meters and a shadow Compute estimate. Approval is never a charge.
class ConsentSheetView extends StatelessWidget {
  const ConsentSheetView({
    super.key,
    required this.sheet,
    required this.onApprove,
    required this.onNotNow,
    this.permissions = const [],
  });

  final ConsentSheet sheet;
  final List<AppPermissionSummary> permissions;
  final VoidCallback onApprove;
  final VoidCallback onNotNow;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final muted = theme.colorScheme.onSurfaceVariant;
    return Semantics(
      container: true,
      explicitChildNodes: true,
      label: 'Consent for ${sheet.name}',
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 18, 20, 20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              'Add ${sheet.name}?',
              style: theme.textTheme.headlineSmall,
            ),
            const SizedBox(height: 6),
            Text(sheet.descriptionForPeople, style: theme.textTheme.bodyMedium),
            if (sheet.examples.isNotEmpty) ...[
              const SizedBox(height: 16),
              _SectionTitle('What you can ask'),
              for (final example in sheet.examples)
                _Bullet(example, icon: Icons.chat_bubble_outline),
            ],
            if (sheet.dataTypes.isNotEmpty) ...[
              const SizedBox(height: 16),
              _SectionTitle('Data it uses'),
              for (final dataType in sheet.dataTypes)
                _Bullet(
                  dataType.write
                      ? '${dataType.semanticTypeId} — ${dataType.reason} (can write)'
                      : '${dataType.semanticTypeId} — ${dataType.reason}',
                  icon: Icons.dataset_outlined,
                ),
            ],
            if (permissions.isNotEmpty) ...[
              const SizedBox(height: 16),
              _SectionTitle('Permissions'),
              for (final permission in permissions)
                _Bullet(
                  '${permission.semanticTypeId} — ${permission.reason}',
                  icon: Icons.verified_user_outlined,
                ),
            ],
            if (sheet.meters.isNotEmpty) ...[
              const SizedBox(height: 16),
              _SectionTitle('What it measures'),
              for (final meter in sheet.meters)
                _Bullet(
                  meter.proposedPriceInCompute == null
                      ? '${meter.meterId} (${meter.unit}, ${meter.aggregation})'
                      : '${meter.meterId} (${meter.unit}, ${meter.aggregation}) · ${meter.proposedPriceInCompute} Compute each',
                  icon: Icons.speed_outlined,
                ),
            ],
            const SizedBox(height: 16),
            Semantics(
              label:
                  'Estimated Compute ${_compute(sheet.estimatedCompute)}. Nothing is charged now.',
              child: Row(
                children: [
                  Icon(Icons.calculate_outlined, size: 18, color: muted),
                  const SizedBox(width: 8),
                  Text(
                    'Estimated ${_compute(sheet.estimatedCompute)} Compute',
                    style: theme.textTheme.bodyMedium,
                  ),
                ],
              ),
            ),
            const SizedBox(height: 4),
            Text(
              'Shadow estimate only — nothing is charged when you add the app.',
              style: theme.textTheme.bodySmall?.copyWith(color: muted),
            ),
            const SizedBox(height: 20),
            Row(
              children: [
                OutlinedButton(
                  onPressed: onNotNow,
                  child: const Text('Not now'),
                ),
                const Spacer(),
                FilledButton(
                  onPressed: onApprove,
                  child: const Text('Approve'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  static String _compute(double value) => value == value.roundToDouble()
      ? value.toStringAsFixed(0)
      : value.toString();
}

/// Shows the consent sheet and resolves true when the person approves, false otherwise.
Future<bool> showConsentSheet(
  BuildContext context, {
  required ConsentSheet sheet,
  List<AppPermissionSummary> permissions = const [],
}) async {
  final approved = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (sheetContext) => ConsentSheetView(
      sheet: sheet,
      permissions: permissions,
      onApprove: () => Navigator.of(sheetContext).pop(true),
      onNotNow: () => Navigator.of(sheetContext).pop(false),
    ),
  );
  return approved ?? false;
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 6),
    child: Text(
      text,
      style: Theme.of(
        context,
      ).textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w600),
    ),
  );
}

class _Bullet extends StatelessWidget {
  const _Bullet(this.text, {required this.icon});

  final String text;
  final IconData icon;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 4),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 16),
        const SizedBox(width: 8),
        Expanded(child: Text(text)),
      ],
    ),
  );
}