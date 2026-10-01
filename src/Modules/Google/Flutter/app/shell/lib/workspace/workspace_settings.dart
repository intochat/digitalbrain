import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';

import '../integrations/connect_window.dart';
import '../integrations/connections_settings.dart';
import '../integrations/integrations_menu.dart';
import 'model_settings.dart';
import 'workspace_store.dart';

/// One settings surface for both connected and local workspaces.
class WorkspaceSettings extends StatefulWidget {
  const WorkspaceSettings({
    super.key,
    required this.store,
    this.initialSection = 'general',
    required this.onClose,
    this.kernelBaseUri,
    this.onOpen,
    this.connectionsRequest,
    this.serviceRequest,
    this.oauthUrl,
    this.loadModels,
  });
  final WorkspaceStore store;
  final String initialSection;
  final VoidCallback onClose;
  final Uri? kernelBaseUri, oauthUrl;
  final OpenUrl? onOpen;
  final ConnectionsRequest? connectionsRequest, serviceRequest;
  final ModelCatalogLoader? loadModels;
  @override
  State<WorkspaceSettings> createState() => _WorkspaceSettingsState();
}

class _WorkspaceSettingsState extends State<WorkspaceSettings> {
  static const _sections = <(String, String, IconData)>[
    ('general', 'General', Icons.tune),
    ('connections', 'Connections', Icons.link),
    ('models', 'Models', Icons.auto_awesome_outlined),
    ('ui-kit', 'UI Kit', Icons.widgets_outlined),
    ('advanced', 'Advanced', Icons.settings_outlined),
  ];
  late String _section;
  late final TextEditingController _name;
  bool _saving = false;
  String? _notice;
  @override
  void initState() {
    super.initState();
    _section = switch (widget.initialSection) {
      'profile' || 'appearance' => 'general',
      'agents' => 'models',
      'developer' => 'advanced',
      'gallery' => 'ui-kit',
      _ => widget.initialSection,
    };
    if (!_sections.any((s) => s.$1 == _section)) _section = 'general';
    _name = TextEditingController(text: widget.store.settings.displayName);
  }

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  Future<void> _saveName() async {
    setState(() {
      _saving = true;
      _notice = null;
    });
    widget.store.settings.displayName = _name.text.trim();
    await widget.store.save();
    if (mounted) {
      setState(() {
        _saving = false;
        _notice = widget.store.persistenceError ?? 'Name saved.';
      });
    }
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
          connectionsRequest: widget.connectionsRequest,
          serviceRequest: widget.serviceRequest,
          oauthUrl: widget.oauthUrl,
          loadModels: widget.loadModels,
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
          final content = _section == 'ui-kit'
              ? const UiGalleryScreen()
              : SingleChildScrollView(
                  padding: EdgeInsets.all(constraints.maxWidth < 700 ? 20 : 36),
                  child: Align(
                    alignment: Alignment.topLeft,
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 780),
                      child: _content(context),
                    ),
                  ),
                );
          if (constraints.maxWidth < 700) {
            return Column(
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(20, 8, 20, 12),
                  child: DropdownButtonFormField<String>(
                    key: ValueKey(_section),
                    initialValue: _section,
                    decoration: const InputDecoration(
                      labelText: 'Settings section',
                      border: OutlineInputBorder(),
                    ),
                    items: [
                      for (final s in _sections)
                        DropdownMenuItem(value: s.$1, child: Text(s.$2)),
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
              SizedBox(
                width: 220,
                child: ListView(
                  padding: const EdgeInsets.all(12),
                  children: [
                    for (final s in _sections)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 4),
                        child: ListTile(
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(12),
                          ),
                          selected: _section == s.$1,
                          selectedTileColor: Theme.of(context)
                              .colorScheme
                              .secondaryContainer,
                          leading: Icon(s.$3),
                          title: Text(s.$2),
                          onTap: () => _selectSection(s.$1),
                        ),
                      ),
                  ],
                ),
              ),
              const VerticalDivider(width: 1),
              Expanded(child: content),
            ],
          );
        },
      ),
    ),
  );
  Widget _heading(BuildContext context, String title, String description) =>
      Column(
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
      case 'connections':
        return ConnectionsSettings(
          request: widget.connectionsRequest,
          serviceRequest: widget.serviceRequest,
          onOpen: widget.onOpen,
        );
      case 'models':
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _heading(
              context,
              'Models',
              'Choose from the AI models configured for this workspace.',
            ),
            ModelSettings(
              load: widget.loadModels,
              selectedId: prefs.defaultModelProfile,
              onSelected: (id) {
                prefs.defaultModelProfile = id;
                widget.store.save();
              },
            ),
            if (widget.store.persistenceError != null)
              Text(widget.store.persistenceError!),
          ],
        );
      case 'advanced':
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _heading(context, 'Advanced', 'Storage and workspace diagnostics.'),
            const ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Icon(Icons.storage_outlined),
              title: Text('Local workspace storage'),
              subtitle: Text(
                'Projects, conversations, drafts and preferences are saved automatically on this device. Credentials are managed separately. This local copy is not a cloud backup.',
              ),
            ),
            const SizedBox(height: 16),
            Text(
              widget.store.persistenceError ??
                  'Local changes are saved automatically.',
            ),
            if (widget.store.persistenceError != null)
              TextButton.icon(
                onPressed: _saving
                    ? null
                    : () async {
                        setState(() => _saving = true);
                        await widget.store.save();
                        if (mounted) setState(() => _saving = false);
                      },
                icon: const Icon(Icons.refresh),
                label: const Text('Retry saving'),
              ),
            const Divider(height: 40),
            const Text(
              'Looking for app building blocks? Open UI Kit to explore Flutter neurons and interactive examples.',
            ),
            TextButton(
              onPressed: () => _selectSection('ui-kit'),
              child: const Text('Explore UI Kit'),
            ),
          ],
        );
      default:
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _heading(
              context,
              'General',
              'Personal preferences for all projects on this device.',
            ),
            TextField(
              controller: _name,
              maxLength: 120,
              decoration: const InputDecoration(
                labelText: 'Display name',
                helperText: 'A display preference; your signed-in account stays the same.',
                border: OutlineInputBorder(),
              ),
              onSubmitted: (_) => _saveName(),
            ),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: _saving ? null : _saveName,
              child: Text(_saving ? 'Saving…' : 'Save name'),
            ),
            if (_notice != null)
              Padding(
                padding: const EdgeInsets.only(top: 12),
                child: Text(_notice!),
              ),
            const Divider(height: 48),
            Text('Appearance', style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 20),
            DropdownButtonFormField<String>(
              initialValue: ['system', 'light', 'dark'].contains(prefs.theme)
                  ? prefs.theme
                  : 'system',
              decoration: const InputDecoration(
                labelText: 'Theme',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(value: 'system', child: Text('Follow system')),
                DropdownMenuItem(value: 'light', child: Text('Light')),
                DropdownMenuItem(value: 'dark', child: Text('Dark')),
              ],
              onChanged: (v) {
                if (v != null) {
                  prefs.theme = v;
                  widget.store.save();
                }
              },
            ),
            const SizedBox(height: 16),
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
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Dock at top'),
              value: prefs.dockTop,
              onChanged: (v) {
                prefs.dockTop = v;
                widget.store.save();
              },
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Assistant on the right'),
              value: prefs.assistantRight,
              onChanged: (v) {
                prefs.assistantRight = v;
                widget.store.save();
              },
            ),
            if (widget.store.persistenceError != null)
              Text(widget.store.persistenceError!),
          ],
        );
    }
  }
}
