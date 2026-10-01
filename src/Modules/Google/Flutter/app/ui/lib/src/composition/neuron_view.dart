import 'dart:async';
import 'dart:convert';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:gpt_markdown/gpt_markdown.dart';
import 'package:url_launcher/url_launcher.dart';

import '../voice_input/ui_voice_input.dart';
import '../components/browser/ui_web_browser.dart';
import '../models/ui_part.dart';
import 'renderer_registry.dart';
import 'ui_collection_view.dart';

typedef NeuronLoader = Future<Map<String, dynamic>> Function(
  String kind,
  String name,
);
typedef SecretSaver = Future<String?> Function(String fieldName, String value);

/// One ordered event channel for a composed tree. A button is processed only
/// after preceding field edits, regardless of network latency or input focus.
class NeuronEventQueue extends ChangeNotifier {
  Future<void> _tail = Future.value();
  int version = 0, pending = 0;
  bool _disposed = false;
  Future<void> flush() => _tail;
  Future<void> dispatch(
    Future<void> Function(Map<String, dynamic>) send,
    Map<String, dynamic> event,
  ) {
    version++;
    pending++;
    final operation = _tail.then((_) => send(event));
    _tail = operation.catchError((Object _) {});
    return operation.whenComplete(() {
      pending--;
      version++;
      if (!_disposed && pending == 0) notifyListeners();
    });
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}

class NeuronView extends StatefulWidget {
  const NeuronView({
    super.key,
    required this.kind,
    required this.name,
    required this.load,
    this.onActivate,
    this.onAction,
    this.imageBuilder,
    this.voiceBuilder,
    this.secretSaver,
    this.ancestors = const {},
    this.revision = 0,
    this.enabled = true,
    this.events,
    this.refreshEvery = const Duration(seconds: 2),
  });
  final String kind, name;
  final NeuronLoader load;
  final ValueChanged<Map<String, dynamic>>? onActivate;
  final Future<void> Function(Map<String, dynamic>)? onAction;
  final Widget Function(Map<String, dynamic>)? imageBuilder;
  final Widget Function(String name, Map<String, dynamic> state)? voiceBuilder;
  final SecretSaver? secretSaver;
  final Set<String> ancestors;
  final int revision;
  final bool enabled;
  final NeuronEventQueue? events;
  final Duration refreshEvery;
  @override
  State<NeuronView> createState() => _NeuronViewState();
}

class _NeuronViewState extends State<NeuronView> {
  Map<String, dynamic>? _data;
  Object? _error;
  String? selection;
  Timer? _timer;
  bool _loading = false, _reloadAgain = false;
  int _generation = 0;
  late final NeuronEventQueue _events = widget.events ?? NeuronEventQueue();
  @override
  void initState() {
    super.initState();
    _events.addListener(_changed);
    _reload();
    _timer = Timer.periodic(widget.refreshEvery, (_) => _reload());
  }

  void _changed() {
    _reload();
  }

  @override
  void didUpdateWidget(NeuronView old) {
    super.didUpdateWidget(old);
    if (old.name != widget.name || old.kind != widget.kind) {
      _generation++;
      _data = null;
      _error = null;
    }
    if (old.name != widget.name ||
        old.kind != widget.kind ||
        old.revision != widget.revision) {
      _reload();
    }
  }

  Future<void> _reload() async {
    if (_events.pending > 0) return;
    if (_loading) {
      _reloadAgain = true;
      return;
    }
    _loading = true;
    final generation = _generation;
    final version = _events.version;
    try {
      final data = await widget.load(widget.kind, widget.name);
      if (mounted && generation == _generation && version == _events.version) {
        setState(() {
          _data = data;
          _error = null;
        });
      }
    } catch (error) {
      if (mounted && _data == null) setState(() => _error = error);
    } finally {
      _loading = false;
      if (mounted && _reloadAgain) {
        _reloadAgain = false;
        unawaited(_reload());
      }
    }
  }

  Future<void> _dispatch(Map<String, dynamic> event) async {
    final send = widget.onAction;
    if (!widget.enabled || send == null) return;
    try {
      await _events.dispatch(send, event);
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.maybeOf(context)?.showSnackBar(
          SnackBar(content: Text('Could not save change: $error')),
        );
      }
      rethrow;
    }
  }

  Future<void> _button(Map<String, dynamic> definition) async {
    FocusScope.of(context).unfocus();
    try {
      if (definition['activation'] case final String json) {
        await _events.flush();
        if (!mounted) return;
        final activation = Map<String, dynamic>.from(jsonDecode(json) as Map);
        final uri = Uri.tryParse('${activation['url'] ?? ''}');
        if (uri != null && const {'http', 'https'}.contains(uri.scheme)) {
          await launchUrl(uri);
        } else {
          widget.onActivate?.call(activation);
        }
      } else {
        await _dispatch({
          'kind': 'button',
          'name': widget.name,
          'action': definition['action'],
        });
      }
    } catch (_) {
      /* Dispatch presents the failure and keeps the projection. */
    }
  }

  Future<void> _file() async {
    try {
      final result = await FilePicker.platform.pickFiles(withData: true);
      if (!mounted || result == null || result.files.isEmpty) return;
      final file = result.files.first;
      if (file.bytes == null) return;
      await _dispatch({
        'kind': 'fileinput',
        'name': widget.name,
        'value': utf8.decode(file.bytes!, allowMalformed: true),
        'field': file.name,
      });
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.maybeOf(context)?.showSnackBar(
          SnackBar(content: Text('Could not attach file: $error')),
        );
      }
    }
  }

  Widget _child(Map child) => NeuronView(
    key: ValueKey('${child['kind']}:${child['name']}'),
    kind: child['kind'] as String,
    name: child['name'] as String,
    load: widget.load,
    onActivate: widget.onActivate,
    onAction: widget.onAction,
    imageBuilder: widget.imageBuilder,
    voiceBuilder: widget.voiceBuilder,
    secretSaver: widget.secretSaver,
    events: _events,
    refreshEvery: widget.refreshEvery,
    ancestors: {...widget.ancestors, '${widget.kind}:${widget.name}'},
    revision: widget.revision,
    enabled: widget.enabled,
  );
  Widget _markdown(String text) => SelectionArea(
    child: GptMarkdown(
      text,
      onLinkTap: (url, _) async {
        final uri = Uri.tryParse(url);
        if (uri != null && const {'https', 'http'}.contains(uri.scheme)) {
          await launchUrl(uri);
        }
      },
    ),
  );
  @override
  Widget build(BuildContext context) {
    final identity = '${widget.kind}:${widget.name}';
    if (widget.ancestors.contains(identity) || widget.ancestors.length >= 16) {
      return const Center(child: Text('This component cannot be displayed.'));
    }
    if (_data == null) {
      return WindowStateView(
        state: WindowState(
          _error == null ? WindowStatus.loading : WindowStatus.failed,
        ),
        onRetry: _error == null ? null : _reload,
      );
    }
    final data = _data!;
    final declared = WindowState.fromMetadata(data);
    if (declared != null) {
      return WindowStateView(state: declared, onRetry: _reload);
    }
    final definition = Map<String, dynamic>.from(
      data['definition'] as Map? ?? data,
    );
    final rawChildren = (definition['children'] as List? ?? [])
        .take(128)
        .toList();
    final children = [
      for (final child in rawChildren)
        if (child is Map && child['kind'] is String && child['name'] is String)
          _child(child)
        else
          const Text('This component cannot be displayed.'),
    ];
    final enabled = widget.enabled && definition['enabled'] != false;
    switch (widget.kind) {
      case 'surface':
      case 'layout':
        if (children.isEmpty) return const SizedBox.shrink();
        final gap = (definition['gap'] as num? ?? 0).toDouble().clamp(
          0.0,
          128.0,
        );
        if (definition['mode'] == 'stack') {
          return Stack(fit: StackFit.expand, children: children);
        }
        if (definition['mode'] == 'list') {
          return LayoutBuilder(
            builder: (context, constraints) {
              final column = Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                spacing: gap,
                children: children,
              );
              return constraints.hasBoundedHeight
                  ? _NeuronList(
                      followEnd: definition['followEnd'] == true,
                      child: column,
                    )
                  : column;
            },
          );
        }
        final horizontal =
            definition['mode'] == 'row' || definition['mode'] == 'split';
        return Flex(
          direction: horizontal ? Axis.horizontal : Axis.vertical,
          spacing: gap,
          children: [
            for (var i = 0; i < children.length; i++)
              if ((definition['extents'] as List?)?.elementAtOrNull(i)
                  case final num extent when extent > 0)
                SizedBox(
                  width: horizontal ? extent.toDouble() : null,
                  height: horizontal ? null : extent.toDouble(),
                  child: children[i],
                )
              else if (!horizontal &&
                  rawChildren[i] is Map &&
                  const {
                    'voiceinput',
                    'fileinput',
                  }.contains((rawChildren[i] as Map)['kind']))
                children[i]
              else
                Expanded(child: children[i]),
          ],
        );
      case 'text':
        return _markdown(definition['markdown'] as String? ?? '');
      case 'button':
        return TextButton(
          onPressed: enabled ? () => _button(definition) : null,
          child: Text(
            definition['label'] as String? ?? '',
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
          ),
        );
      case 'textfield':
        return _NeuronTextField(
          key: ValueKey(widget.name),
          name: widget.name,
          definition: definition,
          enabled: enabled && widget.onAction != null,
          dispatch: _dispatch,
        );
      case 'select':
        final options = (definition['options'] as List? ?? [])
            .whereType<Map>()
            .toList();
        return PopupMenuButton<String>(
          tooltip: definition['label'] as String? ?? '',
          enabled: enabled && widget.onAction != null,
          onSelected: (value) => unawaited(
            _dispatch({'kind': 'select', 'name': widget.name, 'value': value})
                .catchError((Object _) {}),
          ),
          itemBuilder: (_) => [
            for (final option in options)
              CheckedPopupMenuItem(
                value: '${option['id']}',
                checked: option['id'] == definition['selected'],
                enabled: option['enabled'] != false,
                child: Text('${option['label']}'),
              ),
          ],
          child: Padding(
            padding: const EdgeInsets.all(8),
            child: Text(
              '${options.where((option) => option['id'] == definition['selected']).firstOrNull?['label'] ?? definition['label'] ?? ''}',
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
          ),
        );
      case 'fileinput':
        final label = definition['label'] as String? ?? '';
        final capture = enabled && widget.onAction != null ? _file : null;
        return LayoutBuilder(
          builder: (context, constraints) => constraints.maxWidth < 120
              ? IconButton(
                  tooltip: label,
                  onPressed: capture,
                  icon: const Icon(Icons.attach_file),
                )
              : TextButton.icon(
                  onPressed: capture,
                  icon: const Icon(Icons.attach_file),
                  label: Text(label),
                ),
        );
      case 'webbrowser':
        final send = widget.onAction;
        return UiWebBrowser(
          part: UiWebBrowserPart.fromMetadata({
            ...definition,
            'name': widget.name,
          }),
          height: null,
          onConnect: send == null
              ? null
              : (port, sessionId) => send({
                  'kind': 'webbrowser',
                  'name': widget.name,
                  'action': 'connect',
                  'value': jsonEncode({'port': port, 'sessionId': sessionId}),
                }),
          onDisconnect: send == null
              ? null
              : (sessionId) => send({
                  'kind': 'webbrowser',
                  'name': widget.name,
                  'action': 'disconnect',
                  'value': jsonEncode({'sessionId': sessionId}),
                }),
        );
      case 'voiceinput':
        return widget.voiceBuilder?.call(widget.name, definition) ??
            UiVoiceInput(
              label: definition['label'] as String? ?? '',
              enabled: enabled && widget.onAction != null,
              onAudio: (audio, mimeType) => _dispatch({
                'kind': 'voiceinput',
                'name': widget.name,
                'value': base64Encode(audio),
                'mimeType': mimeType,
              }),
            );
      case 'tabs':
        final tabs = (definition['tabs'] as List? ?? [])
            .whereType<Map>()
            .toList();
        final selected = definition['selectedId'];
        final active = tabs.where((tab) => tab['id'] == selected).firstOrNull;
        return Column(
          children: [
            SizedBox(
              height: 42,
              child: ListView(
                scrollDirection: Axis.horizontal,
                children: [
                  for (final tab in tabs)
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 4),
                      child: ChoiceChip(
                        label: Text(tab['title'] as String? ?? ''),
                        selected: tab['id'] == selected,
                        onSelected: !enabled
                            ? null
                            : (_) => unawaited(
                                _dispatch({
                                  'kind': 'tabs',
                                  'name': widget.name,
                                  'value': tab['id'],
                                }).catchError((Object _) {}),
                              ),
                      ),
                    ),
                ],
              ),
            ),
            if (active?['child'] is Map)
              Expanded(child: _child(active!['child'] as Map)),
          ],
        );
      case 'card':
        return Padding(
          padding: const EdgeInsets.all(8),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              if ((definition['title'] as String? ?? '').isNotEmpty)
                Text(
                  definition['title'] as String,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
              if ((definition['body'] as String? ?? '').isNotEmpty)
                _markdown(definition['body'] as String),
              ...children,
            ],
          ),
        );
      case 'collection':
        final items = (definition['items'] as List? ?? [])
            .whereType<Map>()
            .map((item) => Map<String, dynamic>.from(item))
            .toList();
        return UiCollectionView(
          items: items,
          fileMode: definition['fileMode'] != false,
          selectionEnabled: definition['selectionEnabled'] != false,
          selectedId: selection ?? definition['selection'] as String?,
          emptyLabel: definition['emptyLabel'] as String?,
          error: definition['error'] as String?,
          onRetry: _reload,
          onSelect: !enabled
              ? null
              : (id) async {
                  setState(() => selection = id);
                  try {
                    await _dispatch({
                      'kind': 'collection',
                      'name': widget.name,
                      'action': 'select',
                      'value': id,
                      'revision': data['revision'],
                    });
                  } catch (_) {}
                },
          onActivate: !enabled
              ? null
              : (item) => widget.onActivate?.call({
                  ...item,
                  'collection': widget.name,
                  'revision': data['revision'],
                }),
        );
      case 'imagecanvas':
        return widget.imageBuilder?.call(definition) ??
            const Center(child: Text('Image canvas unavailable.'));
      case 'form':
        return _FormRenderer(
          part: UiFormPart.fromMetadata(definition),
          name: widget.name,
          revision: widget.revision,
          enabled: enabled,
          onAction: _dispatch,
          secretSaver: widget.secretSaver,
        );
      default:
        return _FallbackRenderer(kind: widget.kind);
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    _events.removeListener(_changed);
    if (widget.events == null) _events.dispose();
    super.dispose();
  }
}

/// Scroll behavior is declared by the layout, independent of its contents.
class _NeuronList extends StatefulWidget {
  const _NeuronList({required this.followEnd, required this.child});
  final bool followEnd;
  final Widget child;
  @override
  State<_NeuronList> createState() => _NeuronListState();
}

class _NeuronListState extends State<_NeuronList> {
  final _scroll = ScrollController();
  bool _following = true, _scheduled = false;
  void _follow() {
    if (_scheduled || !widget.followEnd || !_following) return;
    _scheduled = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _scheduled = false;
      if (!mounted || !_scroll.hasClients || !_following || !widget.followEnd) {
        return;
      }
      final end = _scroll.position.maxScrollExtent;
      if ((end - _scroll.offset).abs() > 1) _scroll.jumpTo(end);
    });
  }

  @override
  Widget build(BuildContext context) =>
      NotificationListener<ScrollMetricsNotification>(
        onNotification: (notification) {
          if (notification.depth == 0) _follow();
          return false;
        },
        child: NotificationListener<ScrollNotification>(
          onNotification: (notification) {
            if (notification.depth == 0 &&
                (notification is UserScrollNotification ||
                    notification is ScrollUpdateNotification &&
                        notification.dragDetails != null)) {
              _following = notification.metrics.extentAfter < 48;
            }
            return false;
          },
          child: SingleChildScrollView(
            controller: _scroll,
            child: widget.child,
          ),
        ),
      );
  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }
}

class _NeuronTextField extends StatefulWidget {
  const _NeuronTextField({
    super.key,
    required this.name,
    required this.definition,
    required this.enabled,
    required this.dispatch,
  });
  final String name;
  final Map<String, dynamic> definition;
  final bool enabled;
  final Future<void> Function(Map<String, dynamic>) dispatch;
  @override
  State<_NeuronTextField> createState() => _NeuronTextFieldState();
}

class _NeuronTextFieldState extends State<_NeuronTextField> {
  late final TextEditingController _controller = TextEditingController(
    text: widget.definition['value'] as String? ?? '',
  );
  final _focus = FocusNode();
  int _edit = 0, _pending = 0;
  bool _failedEdit = false;
  @override
  void didUpdateWidget(_NeuronTextField old) {
    super.didUpdateWidget(old);
    final value = widget.definition['value'] as String? ?? '';
    if (_pending == 0 && !_failedEdit && _controller.text != value) {
      _controller.value = TextEditingValue(
        text: value,
        selection: TextSelection.collapsed(offset: value.length),
      );
    }
  }

  Future<void> _change(String value) async {
    final edit = ++_edit;
    _pending++;
    try {
      await widget.dispatch({
        'kind': 'textfield',
        'name': widget.name,
        'value': value,
      });
      if (edit == _edit) _failedEdit = false;
    } catch (_) {
      if (edit == _edit) _failedEdit = true;
    } finally {
      _pending--;
      if (mounted && edit == _edit) setState(() {});
    }
  }

  void _submit(String target) {
    _focus.unfocus();
    unawaited(
      widget
          .dispatch({'kind': 'button', 'name': target})
          .catchError((Object _) {}),
    );
  }

  @override
  Widget build(BuildContext context) {
    final kind = '${widget.definition['kind'] ?? ''}'.toLowerCase();
    final multiline = kind == 'multiline';
    final target = widget.definition['submitButton'] as String?;
    final field = TextField(
      key: ValueKey('textfield:${widget.name}'),
      controller: _controller,
      focusNode: _focus,
      enabled: widget.enabled,
      obscureText: kind == 'secret' || kind == 'password',
      minLines: multiline ? 2 : 1,
      maxLines: multiline ? 5 : 1,
      textInputAction: target == null
          ? (multiline ? TextInputAction.newline : TextInputAction.done)
          : TextInputAction.send,
      decoration: InputDecoration(
        labelText: widget.definition['label'] as String? ?? '',
        isDense: true,
      ),
      onChanged: _change,
      onSubmitted: target == null ? null : (_) => _submit(target),
    );
    return Padding(
      padding: const EdgeInsets.all(4),
      child: target == null
          ? field
          : CallbackShortcuts(
              bindings: {
                const SingleActivator(LogicalKeyboardKey.enter): () =>
                    _submit(target),
              },
              child: field,
            ),
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    _focus.dispose();
    super.dispose();
  }
}

/// Renders the declared fallback for a UI kind that has no dedicated renderer: the kind is named
/// and explained, never blank.
class _FallbackRenderer extends StatelessWidget {
  const _FallbackRenderer({required this.kind});

  final String kind;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text('${RendererRegistry.fallbackLabel}: $kind'),
            const SizedBox(height: 4),
            const Text(
              RendererRegistry.fallbackExplanation,
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}

/// Renders each declared window state. Loading shows progress, failed offers a retry, and the
/// other states explain the situation in one line so no window is blank.
class WindowStateView extends StatelessWidget {
  const WindowStateView({super.key, required this.state, this.onRetry});

  final WindowState state;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    if (state.status == WindowStatus.loading) {
      return const Center(
        child: Padding(
          padding: EdgeInsets.all(16),
          child: CircularProgressIndicator(),
        ),
      );
    }
    final (icon, label, message) = switch (state.status) {
      WindowStatus.loading => (Icons.hourglass_empty, 'Loading', ''),
      WindowStatus.empty => (
        Icons.inbox_outlined,
        'Nothing here yet',
        'This window has no content yet.',
      ),
      WindowStatus.permissionDenied => (
        Icons.lock_outline,
        'Not available to you',
        'You do not have permission to view this window.',
      ),
      WindowStatus.expiredConnection => (
        Icons.link_off,
        'Connection expired',
        'The connection this window needs has expired. Reconnect to continue.',
      ),
      WindowStatus.failed => (
        Icons.error_outline,
        'Could not load',
        state.message ?? 'This component could not be loaded.',
      ),
    };
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 28),
            const SizedBox(height: 8),
            Text(label),
            if (message.isNotEmpty) ...[
              const SizedBox(height: 4),
              Text(message, textAlign: TextAlign.center),
            ],
            if (onRetry != null) ...[
              const SizedBox(height: 8),
              TextButton(onPressed: onRetry, child: const Text('Retry')),
            ],
          ],
        ),
      ),
    );
  }
}

/// Renders the first-run workspace state: the assistant plus starter prompts that match the
/// connected sources.
class FirstRunView extends StatelessWidget {
  const FirstRunView({super.key, required this.state, this.onPrompt});

  final FirstRunState state;
  final ValueChanged<StarterPrompt>? onPrompt;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            state.assistantTitle,
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 4),
          const Text('Start with one of these.'),
          const SizedBox(height: 12),
          for (final prompt in state.prompts)
            Card(
              child: ListTile(
                title: Text(prompt.label),
                subtitle: prompt.source == 'assistant'
                    ? null
                    : Text(prompt.source),
                onTap: onPrompt == null ? null : () => onPrompt!(prompt),
              ),
            ),
        ],
      ),
    );
  }
}

/// Renders a declarative form from one read: typed fields, a date picker and a masked secret
/// input. Field edits dispatch a `form` event; Save submits the whole form atomically.
class _FormRenderer extends StatefulWidget {
  const _FormRenderer({
    required this.part,
    required this.name,
    required this.revision,
    required this.enabled,
    required this.onAction,
    this.secretSaver,
  });

  final UiFormPart part;
  final String name;
  final int revision;
  final bool enabled;
  final Future<void> Function(Map<String, dynamic>)? onAction;
  final SecretSaver? secretSaver;

  @override
  State<_FormRenderer> createState() => _FormRendererState();
}

class _FormRendererState extends State<_FormRenderer> {
  late Map<String, String?> values = _initial();

  Map<String, String?> _initial() => {
    for (final field in widget.part.fields) field.name: field.value,
  };

  @override
  void didUpdateWidget(_FormRenderer old) {
    super.didUpdateWidget(old);
    if (old.revision != widget.revision) {
      values = _initial();
    }
  }

  Future<void> dispatch(Map<String, Object?> event) async {
    await widget.onAction?.call({
      'kind': 'form',
      'name': widget.name,
      'revision': widget.revision,
      ...event,
    });
  }

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (widget.part.title.isNotEmpty) ...[
            Text(
              widget.part.title,
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
          ],
          for (final field in widget.part.fields) _field(context, field),
          const SizedBox(height: 12),
          Row(
            children: [
              FilledButton(
                onPressed: widget.enabled
                    ? () => dispatch({'action': 'submit'})
                    : null,
                child: const Text('Save'),
              ),
              if (widget.part.submitted) ...[
                const SizedBox(width: 8),
                const Text('Saved'),
              ],
            ],
          ),
        ],
      ),
    );
  }

  Widget _field(BuildContext context, UiFormField field) {
    final label = field.required ? '${field.label} *' : field.label;
    switch (field.kind) {
      case 'Secret':
        // The raw secret is posted once to the vault; only the returned reference reaches the
        // form. Without a vault saver there is no way to set it, so the handle stays read-only.
        final saver = widget.secretSaver;
        if (saver == null) {
          return Padding(
            padding: const EdgeInsets.symmetric(vertical: 4),
            child: TextFormField(
              key: Key('form_secret_${field.name}'),
              obscureText: true,
              readOnly: true,
              enabled: widget.enabled,
              decoration: InputDecoration(
                labelText: label,
                isDense: true,
                helperText: field.secretSet ? '•••• set' : 'Not set',
              ),
            ),
          );
        }
        return _SecretInput(
          key: Key('form_secret_${field.name}'),
          label: label,
          enabled: widget.enabled,
          isSet: field.secretSet,
          save: (value) async {
            final reference = await saver(field.name, value);
            if (reference != null && reference.isNotEmpty) {
              await dispatch({
                'action': 'secret',
                'field': field.name,
                'value': reference,
              });
            }
            return reference;
          },
        );
      case 'Date':
        final value = values[field.name];
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: Row(
            children: [
              Expanded(
                child: InputDecorator(
                  decoration: InputDecoration(labelText: label, isDense: true),
                  child: Text(
                    value == null || value.isEmpty ? 'Pick a date' : value,
                  ),
                ),
              ),
              IconButton(
                key: Key('form_date_${field.name}'),
                icon: const Icon(Icons.calendar_today),
                tooltip: 'Pick ${field.label}',
                onPressed: widget.enabled ? () => _pickDate(field) : null,
              ),
            ],
          ),
        );
      case 'Boolean':
        return SwitchListTile(
          contentPadding: EdgeInsets.zero,
          value: values[field.name] == 'true',
          title: Text(label),
          onChanged: widget.enabled
              ? (on) => dispatch({
                  'field': field.name,
                  'value': on ? 'true' : 'false',
                })
              : null,
        );
      case 'Choice':
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: DropdownButtonFormField<String>(
            initialValue: field.choices.contains(values[field.name])
                ? values[field.name]
                : null,
            decoration: InputDecoration(labelText: label, isDense: true),
            items: [
              for (final choice in field.choices)
                DropdownMenuItem(value: choice, child: Text(choice)),
            ],
            onChanged: widget.enabled
                ? (value) => dispatch({'field': field.name, 'value': value})
                : null,
          ),
        );
      default:
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 4),
          child: TextFormField(
            key: Key('form_field_${field.name}'),
            initialValue: values[field.name] ?? '',
            enabled: widget.enabled,
            decoration: InputDecoration(
              labelText: label,
              isDense: true,
              helperText: field.supported ? null : 'Shown as plain text.',
            ),
            onFieldSubmitted: (value) =>
                dispatch({'field': field.name, 'value': value}),
          ),
        );
    }
  }

  Future<void> _pickDate(UiFormField field) async {
    final current = values[field.name];
    final parsed = current == null || current.isEmpty
        ? null
        : DateTime.tryParse(current);
    final picked = await showDatePicker(
      context: context,
      initialDate: parsed ?? DateTime.now(),
      firstDate: DateTime(1900),
      lastDate: DateTime(2200),
    );
    if (picked == null) {
      return;
    }
    final formatted = picked.toIso8601String().split('T').first;
    setState(() => values[field.name] = formatted);
    await dispatch({'field': field.name, 'value': formatted});
  }
}

/// A masked secret input that hands the raw value to the vault once and then drops it, keeping
/// only the returned reference. The field text is cleared on success so the raw value is not held.
class _SecretInput extends StatefulWidget {
  const _SecretInput({
    super.key,
    required this.label,
    required this.enabled,
    required this.isSet,
    required this.save,
  });

  final String label;
  final bool enabled;
  final bool isSet;
  final Future<String?> Function(String value) save;

  @override
  State<_SecretInput> createState() => _SecretInputState();
}

class _SecretInputState extends State<_SecretInput> {
  final controller = TextEditingController();
  bool set = false;
  bool saving = false;

  @override
  void initState() {
    super.initState();
    set = widget.isSet;
  }

  Future<void> submit() async {
    final value = controller.text;
    if (value.isEmpty || saving) return;
    setState(() => saving = true);
    try {
      final reference = await widget.save(value);
      if (!mounted) return;
      setState(() {
        set = reference != null && reference.isNotEmpty;
        saving = false;
        controller.clear();
      });
    } catch (_) {
      if (mounted) setState(() => saving = false);
      rethrow;
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 4),
    child: TextFormField(
      controller: controller,
      obscureText: true,
      enabled: widget.enabled && !saving,
      decoration: InputDecoration(
        labelText: widget.label,
        isDense: true,
        helperText: set ? '•••• set' : 'Not set',
        suffixIcon: IconButton(
          key: const Key('form_secret_save'),
          icon: const Icon(Icons.check),
          tooltip: 'Store secret',
          onPressed: widget.enabled && !saving ? submit : null,
        ),
      ),
      onFieldSubmitted: (_) => submit(),
    ),
  );

  @override
  void dispose() {
    controller.dispose();
    super.dispose();
  }
}
