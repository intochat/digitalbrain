import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';
import 'package:forui/forui.dart';

class UiSpecialistSuggestions extends StatelessWidget {
  const UiSpecialistSuggestions({super.key, required this.onStart});
  final ValueChanged<UiSpecialist> onStart;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        'Start something new',
        style: Theme.of(context).textTheme.titleSmall,
      ),
      const SizedBox(height: 14),
      Wrap(
        spacing: 12,
        runSpacing: 12,
        children: [
          for (final specialist in uiSpecialists)
            ActionChip(
              avatar: Icon(specialist.icon, size: 18),
              label: Text(specialist.name),
              onPressed: () => onStart(specialist),
            ),
        ],
      ),
    ],
  );
}

@immutable
class UiSpecialist {
  const UiSpecialist(
    this.id,
    this.name,
    this.description,
    this.example,
    this.icon,
    this.color,
    this.note,
  );
  final String id, name, description, example, note;
  final IconData icon;
  final Color color;
}

const uiSpecialists = [
  UiSpecialist(
    'salesforce',
    'Salesforce Admin',
    'Bring clarity to your CRM. Understand your setup and plan better ways of working.',
    'Review my Salesforce setup',
    Icons.cloud_outlined,
    Color(0xFF629BDD),
    'Start with planning and guidance. Live Salesforce access requires a connected service.',
  ),
  UiSpecialist(
    'leads',
    'Lead Researcher',
    'Find promising companies, research prospects, and turn discoveries into useful lists.',
    'Research potential customers',
    FLucideIcons.scanSearch,
    Color(0xFFD4A553),
    'Research uses the tools available in your workspace. Review sources before using the results.',
  ),
  UiSpecialist(
    'automation',
    'Automation Builder',
    'Make repetitive work simpler. Shape an idea into a clear, connected workflow.',
    'Map an approval workflow',
    FLucideIcons.workflow,
    Color(0xFFA28AD8),
    'Workflows begin as drafts. Designing a workflow does not activate it.',
  ),
];
const uiCoordinator = UiSpecialist(
  'intocaht',
  'IntoChat',
  'Think through an idea, ask a question, or work across specialties.',
  'Help me think through a new idea',
  FLucideIcons.sparkles,
  Color(0xFF72B499),
  'You can change specialists at any time. Your project and saved work stay together.',
);

class UiSpecialistIcon extends StatelessWidget {
  const UiSpecialistIcon({super.key, required this.specialist, this.size = 52});
  final UiSpecialist specialist;
  final double size;
  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(
      color: specialist.color.withValues(alpha: .13),
      borderRadius: BorderRadius.circular(size * .27),
      border: Border.all(color: specialist.color.withValues(alpha: .23)),
    ),
    child: specialist.id == 'salesforce'
        ? Padding(
            padding: EdgeInsets.all(size * .12),
            child: SvgPicture.asset(
              'assets/brands/salesforce.svg',
              package: 'digitalbrain_ui',
              semanticsLabel: 'Salesforce',
            ),
          )
        : Icon(
            specialist.icon,
            size: size * .48,
            color: Theme.of(context).brightness == Brightness.dark
                ? specialist.color
                : Color.lerp(specialist.color, Colors.black, .3),
          ),
  );
}

class UiExplore extends StatelessWidget {
  const UiExplore({super.key, required this.onStart, this.firstVisit = false});
  final ValueChanged<UiSpecialist> onStart;
  final bool firstVisit;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final theme = Theme.of(context);
      final colors = theme.colorScheme;
      final narrow = constraints.maxWidth < 650;
      return SingleChildScrollView(
        child: Padding(
          padding: EdgeInsets.fromLTRB(
            narrow ? 20 : 40,
            narrow ? 32 : 64,
            narrow ? 20 : 40,
            48,
          ),
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 1120),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    firstVisit
                        ? 'WELCOME TO INTOCHAT'
                        : 'EXPLORE YOUR WORKSPACE',
                    style: theme.textTheme.labelSmall?.copyWith(
                      letterSpacing: 1.8,
                      color: colors.primary,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 18),
                  ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 700),
                    child: Text(
                      'What would you like to work on?',
                      style: theme.textTheme.displaySmall?.copyWith(
                        fontSize: narrow ? 32 : 42,
                        height: 1.15,
                        letterSpacing: -1.5,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                  const SizedBox(height: 18),
                  Text(
                    'A little expertise. A fresh perspective. A place to make progress.',
                    style: theme.textTheme.bodyLarge?.copyWith(
                      color: colors.onSurfaceVariant,
                      height: 1.6,
                    ),
                  ),
                  const SizedBox(height: 44),
                  Row(
                    children: [
                      Flexible(
                        child: Text(
                          'Start with a specialist',
                          style: theme.textTheme.titleSmall?.copyWith(
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                      const SizedBox(width: 16),
                      Expanded(child: Divider(color: colors.outlineVariant)),
                    ],
                  ),
                  const SizedBox(height: 20),
                  LayoutBuilder(
                    builder: (context, grid) {
                      final cols =
                          grid.maxWidth >= 900 &&
                              MediaQuery.textScalerOf(context).scale(14) < 22
                          ? 3
                          : 1;
                      return Wrap(
                        spacing: 18,
                        runSpacing: 18,
                        children: [
                          for (final specialist in uiSpecialists)
                            SizedBox(
                              width: (grid.maxWidth - (cols - 1) * 18) / cols,
                              child: _SpecialistCard(
                                specialist: specialist,
                                onTap: () => onStart(specialist),
                              ),
                            ),
                        ],
                      );
                    },
                  ),
                  const SizedBox(height: 28),
                  Material(
                    color: Colors.transparent,
                    child: InkWell(
                      borderRadius: BorderRadius.circular(14),
                      onTap: () => onStart(uiCoordinator),
                      child: Padding(
                        padding: const EdgeInsets.symmetric(
                          vertical: 18,
                          horizontal: 8,
                        ),
                        child: Row(
                          children: [
                            const UiSpecialistIcon(
                              specialist: uiCoordinator,
                              size: 42,
                            ),
                            const SizedBox(width: 16),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    'Start with IntoChat',
                                    style: theme.textTheme.titleSmall?.copyWith(
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                  const SizedBox(height: 5),
                                  Text(
                                    'For a new idea, an open question, or a bit of everything.',
                                    style: theme.textTheme.bodyMedium?.copyWith(
                                      color: colors.onSurfaceVariant,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                            const SizedBox(width: 12),
                            Icon(
                              Icons.arrow_forward_rounded,
                              size: 20,
                              color: colors.onSurfaceVariant,
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 28),
                  Text(
                    'Every project keeps your conversations and saved work together. You can change specialists as you go.',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: colors.onSurfaceVariant,
                      height: 1.6,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      );
    },
  );
}

class _SpecialistCard extends StatelessWidget {
  const _SpecialistCard({required this.specialist, required this.onTap});
  final UiSpecialist specialist;
  final VoidCallback onTap;
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    return Material(
      color: colors.surfaceContainerLow,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(18),
        side: BorderSide(color: colors.outlineVariant),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        hoverColor: specialist.color.withValues(alpha: .07),
        focusColor: specialist.color.withValues(alpha: .16),
        child: Padding(
          padding: const EdgeInsets.all(26),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiSpecialistIcon(specialist: specialist),
              const SizedBox(height: 30),
              Text(
                specialist.name,
                style: theme.textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.w600,
                  letterSpacing: -.5,
                ),
              ),
              const SizedBox(height: 12),
              ConstrainedBox(
                constraints: const BoxConstraints(minHeight: 76),
                child: Text(
                  specialist.description,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: colors.onSurfaceVariant,
                    height: 1.65,
                  ),
                ),
              ),
              const SizedBox(height: 24),
              Divider(color: colors.outlineVariant, height: 1),
              const SizedBox(height: 20),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      specialist.example,
                      style: theme.textTheme.bodySmall?.copyWith(
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Icon(
                    Icons.arrow_forward_rounded,
                    size: 18,
                    color: colors.onSurfaceVariant,
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class UiProjectStart {
  const UiProjectStart(this.name, this.intent, this.specialist);
  final String name, intent;
  final UiSpecialist specialist;
}

class UiProjectStartDialog extends StatefulWidget {
  const UiProjectStartDialog({super.key, this.specialist = uiCoordinator});
  final UiSpecialist specialist;
  @override
  State<UiProjectStartDialog> createState() => _UiProjectStartDialogState();
}

class _UiProjectStartDialogState extends State<UiProjectStartDialog> {
  late UiSpecialist _specialist = widget.specialist;
  final _intent = TextEditingController(), _name = TextEditingController();
  final _form = GlobalKey<FormState>();
  @override
  void dispose() {
    _intent.dispose();
    _name.dispose();
    super.dispose();
  }

  void _start() {
    if (!_form.currentState!.validate()) return;
    final intent = _intent.text.trim();
    final name = _name.text.trim().isNotEmpty
        ? _name.text.trim()
        : intent.isNotEmpty
        ? intent.split('\n').first
        : '${_specialist.name} project';
    Navigator.pop(
      context,
      UiProjectStart(
        name.length > 60 ? '${name.substring(0, 57)}…' : name,
        intent,
        _specialist,
      ),
    );
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Start a project'),
    content: SizedBox(
      width: 480,
      child: SingleChildScrollView(
        child: Form(
          key: _form,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              DropdownButtonFormField<String>(
                initialValue: _specialist.id,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Start with'),
                items: [
                  for (final s in [uiCoordinator, ...uiSpecialists])
                    DropdownMenuItem(
                      value: s.id,
                      child: Row(
                        children: [
                          Icon(s.icon, size: 20),
                          const SizedBox(width: 12),
                          Expanded(
                            child: Text(
                              s.name,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                        ],
                      ),
                    ),
                ],
                onChanged: (id) => setState(
                  () => _specialist = [
                    uiCoordinator,
                    ...uiSpecialists,
                  ].firstWhere((s) => s.id == id),
                ),
              ),
              const SizedBox(height: 20),
              Text(
                'What would you like to accomplish?',
                style: Theme.of(context).textTheme.titleSmall,
              ),
              const SizedBox(height: 10),
              TextFormField(
                key: const Key('project-intent'),
                controller: _intent,
                autofocus: true,
                minLines: 3,
                maxLines: 5,
                decoration: const InputDecoration(
                  hintText: 'Describe your idea or the task ahead…',
                ),
              ),
              const SizedBox(height: 10),
              ActionChip(
                label: Text(
                  _specialist.example,
                  overflow: TextOverflow.ellipsis,
                ),
                onPressed: () {
                  _intent.text = _specialist.example;
                },
              ),
              const SizedBox(height: 20),
              TextFormField(
                controller: _name,
                decoration: const InputDecoration(
                  labelText: 'Project name (optional)',
                  hintText: 'We’ll use your idea as a starting point',
                ),
              ),
              const SizedBox(height: 18),
              Text(
                _specialist.note,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                  height: 1.5,
                ),
              ),
              const SizedBox(height: 10),
              Text(
                'Your request opens as a draft. Send it when you’re ready.',
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(onPressed: _start, child: const Text('Start project')),
    ],
  );
}
