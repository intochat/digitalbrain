import 'package:flutter/material.dart';
import 'package:forui/forui.dart';

import 'ui_explore.dart';
import 'ui_project_templates.dart';

/// Presentation-only project data; persistence and navigation belong to the shell.
@immutable
class UiProjectSummary {
  const UiProjectSummary({
    required this.id,
    required this.title,
    required this.conversations,
    required this.savedItems,
    this.workKinds = const {},
  });
  final String id;
  final String title;
  final int conversations;
  final int savedItems;
  final Map<String, int> workKinds;
}

class UiProjectLibrary extends StatefulWidget {
  const UiProjectLibrary({
    super.key,
    required this.projects,
    required this.onOpen,
    required this.onCreate,
    this.footer,
    this.onStartTemplate,
  });
  final List<UiProjectSummary> projects;
  final ValueChanged<String> onOpen;
  final VoidCallback onCreate;
  final Widget? footer;
  final ValueChanged<UiSpecialist>? onStartTemplate;
  @override
  State<UiProjectLibrary> createState() => _UiProjectLibraryState();
}

class _UiProjectLibraryState extends State<UiProjectLibrary> {
  final _search = TextEditingController();
  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final query = _search.text.trim().toLowerCase();
    final projects = widget.projects
        .where((p) => p.title.toLowerCase().contains(query))
        .toList();
    final projectList = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _heading(context),
        if (widget.onStartTemplate == null) ...[
          const SizedBox(height: 20),
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton.icon(
              onPressed: widget.onCreate,
              icon: const Icon(FLucideIcons.plus, size: 18),
              label: const Text('New project'),
            ),
          ),
        ],
        const SizedBox(height: 36),
        if (widget.projects.length >= 6 || query.isNotEmpty) ...[
          TextField(
            controller: _search,
            onChanged: (_) => setState(() {}),
            decoration: InputDecoration(
              hintText: 'Search projects',
              prefixIcon: const Icon(FLucideIcons.search, size: 19),
              suffixIcon: query.isEmpty
                  ? null
                  : IconButton(
                      tooltip: 'Clear search',
                      onPressed: () {
                        _search.clear();
                        setState(() {});
                      },
                      icon: const Icon(FLucideIcons.x, size: 18),
                    ),
            ),
          ),
          const SizedBox(height: 28),
        ],
        Padding(
          padding: const EdgeInsets.only(bottom: 16),
          child: Wrap(
            spacing: 10,
            runSpacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              Text(
                query.isEmpty ? 'All projects' : 'Search results',
                style: theme.textTheme.labelMedium?.copyWith(
                  fontWeight: FontWeight.w500,
                  color: colors.onSurfaceVariant,
                ),
              ),
              Text(
                projects.length.toString().padLeft(2, '0'),
                style: theme.textTheme.labelSmall?.copyWith(
                  color: colors.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
        Divider(height: 1, color: colors.outlineVariant),
        if (projects.isEmpty)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 56),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(
                  query.isEmpty
                      ? Icons.create_new_folder_outlined
                      : Icons.search_off,
                  size: 28,
                  color: colors.primary,
                ),
                const SizedBox(height: 24),
                Text(
                  query.isEmpty
                      ? 'Room for your next idea'
                      : 'No matching projects',
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w500,
                  ),
                ),
                const SizedBox(height: 10),
                Text(
                  query.isEmpty
                      ? 'Choose a template to give your conversations and discoveries a home.'
                      : 'Try another name or clear your search.',
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: colors.onSurfaceVariant,
                    height: 1.6,
                  ),
                ),
              ],
            ),
          )
        else
          for (final project in projects) ...[
            UiProjectCard(
              key: ValueKey(project.id),
              project: project,
              onOpen: () => widget.onOpen(project.id),
            ),
            Divider(height: 1, color: colors.outlineVariant),
          ],
        if (widget.footer != null) ...[
          const SizedBox(height: 40),
          widget.footer!,
        ],
      ],
    );
    return LayoutBuilder(
      builder: (context, constraints) {
        final stacked =
            constraints.maxWidth < 960 ||
            MediaQuery.textScalerOf(context).scale(14) > 22;
        final templates = widget.onStartTemplate == null
            ? null
            : UiProjectTemplates(onStart: widget.onStartTemplate!);
        return SingleChildScrollView(
          key: const PageStorageKey('project-library-scroll'),
          padding: EdgeInsets.fromLTRB(
            stacked ? 24 : 48,
            stacked ? 40 : 72,
            stacked ? 24 : 48,
            64,
          ),
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 1160),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'A LITTLE SPACE TO THINK BIG',
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: colors.onSurfaceVariant,
                      fontSize: 10,
                      letterSpacing: 2,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 30),
                  if (stacked || templates == null) ...[
                    projectList,
                    if (templates != null) ...[
                      const SizedBox(height: 40),
                      Divider(height: 1, color: colors.outlineVariant),
                      const SizedBox(height: 32),
                      templates,
                    ],
                  ] else
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(flex: 3, child: projectList),
                        const SizedBox(width: 40),
                        Expanded(
                          flex: 2,
                          child: Container(
                            padding: const EdgeInsets.only(left: 32),
                            decoration: BoxDecoration(
                              border: Border(
                                left: BorderSide(color: colors.outlineVariant),
                              ),
                            ),
                            child: templates,
                          ),
                        ),
                      ],
                    ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }

  Widget _heading(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        'Your projects',
        style: Theme.of(context).textTheme.headlineLarge?.copyWith(
          fontSize: 42,
          fontWeight: FontWeight.w400,
          letterSpacing: -1.8,
          height: 1.15,
        ),
      ),
      const SizedBox(height: 14),
      Text(
        'Pick up where you left off.',
        style: Theme.of(context).textTheme.bodyLarge?.copyWith(
          color: Theme.of(context).colorScheme.onSurfaceVariant,
          fontSize: 15,
          height: 1.5,
        ),
      ),
    ],
  );
}

/// A project entry with the same activation behavior for mouse and keyboard.
class UiProjectCard extends StatefulWidget {
  const UiProjectCard({
    super.key,
    required this.project,
    required this.onOpen,
    this.focusNode,
  });
  final UiProjectSummary project;
  final VoidCallback onOpen;
  final FocusNode? focusNode;
  @override
  State<UiProjectCard> createState() => _UiProjectCardState();
}

class _UiProjectCardState extends State<UiProjectCard> {
  bool _hovered = false, _focused = false;
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final p = widget.project;
    final active = _hovered || _focused;
    return LayoutBuilder(
      builder: (context, constraints) {
        final narrow =
            constraints.maxWidth < 600 ||
            MediaQuery.textScalerOf(context).scale(14) > 22;
        final detail =
            '${p.conversations} ${p.conversations == 1 ? 'conversation' : 'conversations'}';
        final saved =
            '${p.savedItems} saved ${p.savedItems == 1 ? 'item' : 'items'}';
        return Semantics(
          button: true,
          child: Material(
            color: active ? colors.surfaceContainerLow : Colors.transparent,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(8),
              side: BorderSide(
                color: _focused ? colors.primary : Colors.transparent,
              ),
            ),
            clipBehavior: Clip.antiAlias,
            child: InkWell(
              onTap: widget.onOpen,
              focusNode: widget.focusNode,
              onHover: (value) => setState(() => _hovered = value),
              onFocusChange: (value) => setState(() => _focused = value),
              hoverColor: colors.primary.withValues(alpha: 0.025),
              child: Padding(
                padding: EdgeInsets.symmetric(
                  horizontal: narrow ? 8 : 16,
                  vertical: theme.visualDensity == VisualDensity.compact
                      ? 22
                      : 30,
                ),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.center,
                  children: [
                    Container(
                      width: 44,
                      height: 48,
                      alignment: Alignment.center,
                      decoration: BoxDecoration(
                        color: colors.primaryContainer.withValues(alpha: 0.55),
                        borderRadius: BorderRadius.circular(7),
                        border: Border(
                          left: BorderSide(
                            color: colors.primary.withValues(alpha: 0.55),
                            width: 3,
                          ),
                        ),
                      ),
                      child: Icon(
                        FLucideIcons.folder,
                        size: 20,
                        color: colors.onPrimaryContainer,
                      ),
                    ),
                    SizedBox(width: narrow ? 16 : 24),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            p.title,
                            maxLines: 3,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.titleMedium?.copyWith(
                              fontSize: 18,
                              fontWeight: FontWeight.w500,
                              letterSpacing: -0.3,
                            ),
                          ),
                          const SizedBox(height: 7),
                          Wrap(
                            spacing: 16,
                            runSpacing: 5,
                            children: [
                              Text(
                                detail,
                                style: theme.textTheme.bodySmall?.copyWith(
                                  color: colors.onSurfaceVariant,
                                ),
                              ),
                              if (narrow)
                                Text(
                                  saved,
                                  style: theme.textTheme.bodySmall?.copyWith(
                                    color: colors.onSurfaceVariant,
                                  ),
                                ),
                            ],
                          ),
                        ],
                      ),
                    ),
                    if (!narrow) ...[
                      const SizedBox(width: 24),
                      Tooltip(
                        message: p.workKinds.entries
                            .map((e) => '${e.value} ${e.key}')
                            .join(', '),
                        child: Text(
                          saved,
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: colors.onSurfaceVariant,
                          ),
                        ),
                      ),
                    ],
                    SizedBox(width: narrow ? 12 : 40),
                    Icon(
                      FLucideIcons.arrowRight,
                      size: 18,
                      color: active ? colors.primary : colors.onSurfaceVariant,
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
}

/// Owns its controller for the entire dialog route, including dismissal.
class UiProjectNameDialog extends StatefulWidget {
  const UiProjectNameDialog({super.key});
  @override
  State<UiProjectNameDialog> createState() => _UiProjectNameDialogState();
}

class _UiProjectNameDialogState extends State<UiProjectNameDialog> {
  final _input = TextEditingController();
  final _form = GlobalKey<FormState>();
  @override
  void dispose() {
    _input.dispose();
    super.dispose();
  }

  void _submit() {
    if (_form.currentState!.validate()) {
      Navigator.pop(context, _input.text.trim());
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('New project'),
    content: SizedBox(
      width: 400,
      child: Form(
        key: _form,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Give your conversations and saved work a home.',
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 24),
            TextFormField(
              controller: _input,
              autofocus: true,
              textCapitalization: TextCapitalization.sentences,
              textInputAction: TextInputAction.done,
              decoration: const InputDecoration(
                labelText: 'Project name',
                hintText: 'e.g. Product research',
              ),
              validator: (value) => value == null || value.trim().isEmpty
                  ? 'Enter a project name.'
                  : null,
              onFieldSubmitted: (_) => _submit(),
            ),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(onPressed: _submit, child: const Text('Create project')),
    ],
  );
}
