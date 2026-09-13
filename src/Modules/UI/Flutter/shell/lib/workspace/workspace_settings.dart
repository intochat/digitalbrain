import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';

import '../integrations/integrations_menu.dart';
import 'workspace_store.dart';

/// Full-page settings, with the real component gallery nested in its route.
class WorkspaceSettings extends StatefulWidget {
  const WorkspaceSettings({
    super.key,
    required this.store,
    this.initialSection = 'profile',
    required this.onClose,
    this.kernelBaseUri,
    this.onOpen,
  });
  final WorkspaceStore store;
  final String initialSection;
  final VoidCallback onClose;
  final Uri? kernelBaseUri;
  final OpenUrl? onOpen;
  @override
  State<WorkspaceSettings> createState() => _WorkspaceSettingsState();
}

class _WorkspaceSettingsState extends State<WorkspaceSettings> {
  static const _sections = <(String, String, IconData)>[
    ('profile', 'Profile', Icons.person_outline),
    ('appearance', 'Appearance', Icons.palette_outlined),
    ('connections', 'Connections', Icons.link),
    ('agents', 'Agents', Icons.auto_awesome_outlined),
    ('developer', 'Developer tools', Icons.code),
  ];
  late String _section;
  late final TextEditingController _name;
  late final TextEditingController _role;
  bool _saving = false;
  @override
  void initState() {
    super.initState();
    _section = _sections.any((s) => s.$1 == widget.initialSection)
        ? widget.initialSection
        : 'profile';
    _name = TextEditingController(text: widget.store.settings.displayName);
    _role = TextEditingController(text: widget.store.settings.role);
  }

  @override
  void dispose() {
    _name.dispose();
    _role.dispose();
    super.dispose();
  }

  Future<void> _saveProfile() async {
    setState(() => _saving = true);
    widget.store.settings
      ..displayName = _name.text.trim()
      ..role = _role.text.trim();
    await widget.store.save();
    if (!mounted) return;
    setState(() => _saving = false);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          widget.store.persistenceError ??
              'Profile preferences saved on this device.',
        ),
      ),
    );
  }

  void _selectSection(String section) {
    if (section == _section) return;
    Navigator.of(context).pushReplacement(
      MaterialPageRoute<void>(
        settings: RouteSettings(name: '/settings/$section'),
        builder: (_) => WorkspaceSettings(
          store: widget.store,
          initialSection: section,
          onClose: widget.onClose,
          kernelBaseUri: widget.kernelBaseUri,
          onOpen: widget.onOpen,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.store,
    builder: (context, _) => Scaffold(
      appBar: AppBar(
        leading: IconButton(
          tooltip: 'Back to workspace',
          onPressed: widget.onClose,
          icon: const Icon(Icons.arrow_back),
        ),
        title: const Text('Settings'),
      ),
      body: LayoutBuilder(
        builder: (context, constraints) {
          final navigation = ListView(
            children: [
              for (final section in _sections)
                ListTile(
                  selected: _section == section.$1,
                  leading: Icon(section.$3),
                  title: Text(section.$2),
                  onTap: () => _selectSection(section.$1),
                ),
            ],
          );
          final content = ListView(
            padding: EdgeInsets.all(constraints.maxWidth < 650 ? 20 : 36),
            children: [
              Align(
                alignment: Alignment.topLeft,
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 780),
                  child: _content(context),
                ),
              ),
            ],
          );
          if (constraints.maxWidth < 700) {
            return Column(
              children: [
                Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 20,
                    vertical: 8,
                  ),
                  child: DropdownButtonFormField<String>(
                    initialValue: _section,
                    decoration: const InputDecoration(
                      labelText: 'Settings section',
                      border: OutlineInputBorder(),
                    ),
                    items: [
                      for (final section in _sections)
                        DropdownMenuItem(
                          value: section.$1,
                          child: Text(section.$2),
                        ),
                    ],
                    onChanged: (value) {
                      if (value != null) _selectSection(value);
                    },
                  ),
                ),
                Expanded(child: content),
              ],
            );
          }
          return Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SizedBox(width: 240, child: navigation),
              const VerticalDivider(width: 1),
              Expanded(child: content),
            ],
          );
        },
      ),
    ),
  );
  Widget _heading(String title, String description) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(title, style: Theme.of(context).textTheme.headlineSmall),
      const SizedBox(height: 8),
      Text(description),
      const SizedBox(height: 28),
    ],
  );
  Widget _content(BuildContext context) {
    final prefs = widget.store.settings;
    switch (_section) {
      case 'appearance':
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _heading(
              'Appearance',
              'Preferences apply across all your projects on this device.',
            ),
            DropdownButtonFormField<String>(
              initialValue: prefs.theme,
              decoration: const InputDecoration(
                labelText: 'Theme',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(value: 'system', child: Text('Follow system')),
                DropdownMenuItem(value: 'light', child: Text('Light')),
                DropdownMenuItem(value: 'dark', child: Text('Dark')),
              ],
              onChanged: (value) {
                if (value != null) {
                  prefs.theme = value;
                  widget.store.save();
                }
              },
            ),
            const SizedBox(height: 20),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Compact spacing'),
              subtitle: const Text(
                'Use a denser layout for workspace controls.',
              ),
              value: prefs.compactDensity,
              onChanged: (v) {
                prefs.compactDensity = v;
                widget.store.save();
              },
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Reduce motion'),
              subtitle: const Text(
                'Minimize transitions and animated movement.',
              ),
              value: prefs.reducedMotion,
              onChanged: (v) {
                prefs.reducedMotion = v;
                widget.store.save();
              },
            ),
            if (widget.store.persistenceError != null)
              Text(widget.store.persistenceError!),
          ],
        );
      case 'connections':
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _heading(
              'Connections',
              'Service access is shared across projects. Connection status is not available in this view; a service is not marked connected until verified by the backend.',
            ),
            if (widget.kernelBaseUri != null && widget.onOpen != null) ...[
              const Text(
                'Open a service’s authorization flow to connect your account.',
              ),
              const SizedBox(height: 12),
              IntegrationsMenu(
                kernelBaseUri: widget.kernelBaseUri,
                onOpen: widget.onOpen,
              ),
            ] else ...[
              for (final name in ['Salesforce', 'Gmail', 'GitHub'])
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(Icons.link_off),
                  title: Text(name),
                  subtitle: const Text(
                    'Connection setup is unavailable in this session.',
                  ),
                ),
            ],
          ],
        );
      case 'agents':
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _heading(
              'Agents',
              'Choose a capability beside the composer. Changing agents keeps the same conversation and attached work.',
            ),
            for (final agent in workspaceAgents)
              Card(
                margin: const EdgeInsets.only(bottom: 12),
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        agent.name,
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      const SizedBox(height: 8),
                      Text(agent.description),
                      const SizedBox(height: 8),
                      const Text(
                        'Uses the current session’s available model and tools. Service access is not implied.',
                      ),
                    ],
                  ),
                ),
              ),
          ],
        );
      case 'developer':
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _heading(
              'Developer tools',
              'Inspect IntoCaht UI components and their interactive states.',
            ),
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.widgets_outlined),
              title: const Text('UI components gallery'),
              subtitle: const Text(
                'Components with normal, loading, error and disabled states.',
              ),
              trailing: const Icon(Icons.chevron_right),
              onTap: () => Navigator.of(context).push(
                MaterialPageRoute<void>(
                  settings: const RouteSettings(
                    name: '/settings/developer/gallery',
                  ),
                  builder: (context) => Scaffold(
                    appBar: AppBar(title: const Text('UI components gallery')),
                    body: const UiGalleryScreen(),
                  ),
                ),
              ),
            ),
            const Divider(height: 36),
            Text(
              'Local workspace storage',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            const Text(
              'Projects, conversations, artifacts, view state and preferences are saved on this device. Credentials are managed separately. This local copy is not a cloud backup.',
            ),
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed: () async {
                await widget.store.save();
                if (context.mounted) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(
                      content: Text(
                        widget.store.persistenceError ?? 'Workspace saved.',
                      ),
                    ),
                  );
                }
              },
              icon: const Icon(Icons.save_outlined),
              label: const Text('Save workspace now'),
            ),
          ],
        );
      default:
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _heading(
              'Profile',
              'Personalize how you appear in this workspace. These preferences are stored on this device.',
            ),
            TextField(
              controller: _name,
              textInputAction: TextInputAction.next,
              decoration: const InputDecoration(
                labelText: 'Display name',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 20),
            TextField(
              controller: _role,
              decoration: const InputDecoration(
                labelText: 'Role or team',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 24),
            FilledButton.icon(
              onPressed: _saving ? null : _saveProfile,
              icon: const Icon(Icons.check),
              label: Text(_saving ? 'Saving…' : 'Save profile'),
            ),
            const SizedBox(height: 28),
            const Text(
              'Account authentication is managed by your current session. Editing these preferences does not change your account identity.',
            ),
          ],
        );
    }
  }
}
