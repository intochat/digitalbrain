import 'dart:async';

import 'package:flutter/material.dart';

import 'csharp_detail.dart';
import 'csharp_forms.dart';

typedef CSharpRequest = Future<Map<String, dynamic>> Function(
  String method,
  String path, {
  Map<String, Object?>? body,
});
typedef CSharpAsk = void Function(String id, String prompt);

Map<String, dynamic> csharpMap(dynamic value) =>
    value is Map ? Map<String, dynamic>.from(value) : {};
List<Map<String, dynamic>> csharpMaps(dynamic value) =>
    value is List ? value.whereType<Map>().map(csharpMap).toList() : [];

// CSharpFileStatus is a C# enum; minimal APIs serialize it as its number.
const csharpStatusNames = ['Stopped', 'Running', 'Restarting', 'Exited'];

String csharpStatus(Map<String, dynamic> file) {
  final status = file['status'];
  if (status is int) {
    return status >= 0 && status < csharpStatusNames.length
        ? csharpStatusNames[status]
        : 'Unknown';
  }
  return status is String && status.isNotEmpty ? status : 'Stopped';
}

bool csharpIsRunning(Map<String, dynamic> file) =>
    const {'Running', 'Restarting'}.contains(csharpStatus(file));

String csharpStatusLabel(Map<String, dynamic> file) {
  final status = csharpStatus(file);
  final exitCode = file['exitCode'];
  return status == 'Exited' && exitCode != null
      ? 'Exited with code $exitCode'
      : status;
}

class CSharpManager extends StatefulWidget {
  const CSharpManager({
    super.key,
    required this.request,
    required this.onAsk,
    required this.onSelected,
    this.initialId,
    this.active = true,
  });
  final CSharpRequest request;
  final CSharpAsk onAsk;
  final ValueChanged<String?> onSelected;
  final String? initialId;
  final bool active;
  @override
  State<CSharpManager> createState() => _CSharpManagerState();
}

class _CSharpManagerState extends State<CSharpManager> {
  List<Map<String, dynamic>> items = [];
  Map<String, dynamic>? detail;
  String? selected, failure, notice;
  String query = '';
  bool canRun = false;
  bool reading = false, busy = false, loaded = false, stale = true;
  int generation = 0;
  Timer? poll;

  @override
  void initState() {
    super.initState();
    selected = widget.initialId;
    unawaited(load());
    poll = Timer.periodic(const Duration(seconds: 4), (_) {
      if (widget.active) unawaited(load());
    });
  }

  @override
  void didUpdateWidget(CSharpManager oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.initialId != oldWidget.initialId &&
        widget.initialId != selected) {
      choose(widget.initialId);
    }
    if (widget.active && !oldWidget.active) unawaited(load());
  }

  @override
  void dispose() {
    generation++;
    poll?.cancel();
    super.dispose();
  }

  String filePath(String id, [String action = '']) =>
      '${Uri.encodeComponent(id)}${action.isEmpty ? '' : '/$action'}';

  Future<void> load() async {
    if (reading || busy || !mounted) return;
    reading = true;
    final ticket = generation;
    final id = selected;
    try {
      final list = await widget.request('GET', '');
      final next = id == null
          ? null
          : await widget.request('GET', filePath(id));
      if (!mounted || ticket != generation) return;
      setState(() {
        items = csharpMaps(list['items']);
        canRun = list['canRun'] == true;
        detail = next;
        loaded = true;
        stale = false;
        failure = null;
      });
    } catch (error) {
      if (mounted && ticket == generation) {
        setState(() {
          loaded = true;
          stale = true;
          failure = 'Connection lost. Displayed state may be outdated. $error';
        });
      }
    } finally {
      reading = false;
      if (mounted && ticket != generation) unawaited(load());
    }
  }

  void choose(String? id) {
    setState(() {
      selected = id;
      detail = null;
      generation++;
      notice = null;
    });
    widget.onSelected(id);
    unawaited(load());
  }

  Future<void> mutate(
    String method,
    String action, {
    Map<String, Object?>? body,
    required String success,
    bool deselect = false,
  }) async {
    if (busy || stale || selected == null) return;
    final id = selected!;
    setState(() {
      generation++;
      busy = true;
      notice = null;
    });
    try {
      final view = await widget.request(
        method,
        filePath(id, action),
        body: body,
      );
      if (!mounted || selected != id) return;
      setState(() {
        notice = success;
        if (!deselect && view.isNotEmpty) detail = view;
      });
      if (deselect) choose(null);
    } catch (error) {
      if (mounted && selected == id) {
        setState(() => notice = 'Could not apply change. $error');
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
        await load();
      }
    }
  }

  Future<void> share(String packageName) async {
    if (busy || stale || selected == null) return;
    final id = selected!;
    setState(() {
      busy = true;
      notice = null;
    });
    try {
      final package = await widget.request(
        'POST',
        filePath(id, 'share'),
        body: {'name': packageName},
      );
      final shared = csharpMap(package['id']);
      if (mounted && selected == id) {
        setState(
          () => notice =
              'Shared as ${shared['owner']}/${shared['name']}. Anyone can install it from Packages.',
        );
      }
    } catch (error) {
      if (mounted && selected == id) {
        setState(() => notice = 'Could not share this C# file. $error');
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> create() async {
    final draft = await showDialog<CSharpNewFile>(
      context: context,
      builder: (_) => const CSharpNewFileDialog(),
    );
    if (draft == null || !mounted) return;
    setState(() {
      busy = true;
      notice = null;
    });
    try {
      await widget.request(
        'PUT',
        filePath(draft.id),
        body: {
          'source': csharpStarterSource,
          'name': draft.name,
          'purpose': draft.purpose,
        },
      );
    } catch (error) {
      if (mounted) setState(() => notice = 'Could not create C# file. $error');
      return;
    } finally {
      if (mounted) setState(() => busy = false);
    }
    if (mounted) choose(draft.id);
  }

  Widget list() {
    final needle = query.toLowerCase();
    final visible = items.where((item) {
      final description = csharpMap(item['description']);
      return '${description['name']} ${description['purpose']} ${description['id']}'
          .toLowerCase()
          .contains(needle);
    }).toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.all(12),
          child: TextField(
            decoration: const InputDecoration(
              prefixIcon: Icon(Icons.search),
              hintText: 'Find a C# file',
              isDense: true,
            ),
            onChanged: (value) => setState(() => query = value),
          ),
        ),
        const Divider(height: 1),
        Expanded(
          child: !loaded
              ? const Center(child: CircularProgressIndicator())
              : visible.isEmpty
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(20),
                    child: Text(
                      items.isEmpty
                          ? 'No C# files yet.\nCreate one here or ask the assistant to write a C# app.'
                          : 'No matching C# files.',
                      textAlign: TextAlign.center,
                    ),
                  ),
                )
              : ListView.builder(
                  itemCount: visible.length,
                  itemBuilder: (context, index) {
                    final item = visible[index];
                    final description = csharpMap(item['description']);
                    final file = csharpMap(item['file']);
                    final id = description['id'] as String;
                    return ListTile(
                      selected: selected == id,
                      onTap: () => choose(id),
                      leading: Icon(
                        csharpStatus(file) == 'Exited' &&
                                (file['exitCode'] ?? 0) != 0
                            ? Icons.error_outline
                            : Icons.code,
                        size: 20,
                      ),
                      title: Text(
                        '${description['name'] ?? id}',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                      subtitle: Text(
                        '${stale ? 'State unavailable' : csharpStatusLabel(file)}\n${description['purpose'] ?? ''}',
                        maxLines: 3,
                        overflow: TextOverflow.ellipsis,
                      ),
                      isThreeLine: true,
                    );
                  },
                ),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Padding(
        padding: const EdgeInsets.fromLTRB(16, 10, 8, 10),
        child: Row(
          children: [
            const Expanded(
              child: Text(
                'C# files',
                style: TextStyle(fontSize: 20, fontWeight: FontWeight.w600),
              ),
            ),
            IconButton(
              tooltip: 'Refresh C# files',
              onPressed: load,
              icon: const Icon(Icons.refresh),
            ),
            if (MediaQuery.sizeOf(context).width >= 700)
              FilledButton.icon(
                onPressed: busy || stale ? null : create,
                icon: const Icon(Icons.add, size: 18),
                label: const Text('New C# file'),
              )
            else
              IconButton(
                tooltip: 'New C# file',
                onPressed: busy || stale ? null : create,
                icon: const Icon(Icons.add, size: 18),
              ),
          ],
        ),
      ),
      if (failure != null)
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Text(
            failure!,
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
        ),
      if (notice != null)
        Padding(
          padding: const EdgeInsets.all(8),
          child: Text(notice!, maxLines: 3, overflow: TextOverflow.ellipsis),
        ),
      const Divider(height: 1),
      Expanded(
        child: LayoutBuilder(
          builder: (context, box) {
            final narrow = box.maxWidth < 700;
            final content = selected == null
                ? const Center(child: Text('Select a C# file to edit or run.'))
                : detail == null
                ? Center(
                    child: stale && failure != null
                        ? const Text(
                            'Could not load this C# file. Refresh to retry.',
                          )
                        : const CircularProgressIndicator(),
                  )
                : CSharpDetailView(
                    key: ValueKey(selected),
                    view: detail!,
                    canRun: canRun,
                    enabled: !busy && !stale,
                    stale: stale,
                    onSave: (body) =>
                        mutate('PUT', '', body: body, success: 'Saved.'),
                    onStart: () => mutate(
                      'POST',
                      'start',
                      success: 'Started. Refresh logs to see its output.',
                    ),
                    onStop: () => mutate('POST', 'stop', success: 'Stopped.'),
                    onDelete: () => mutate(
                      'DELETE',
                      '',
                      success: 'Deleted.',
                      deselect: true,
                    ),
                    onShare: share,
                    onRefresh: load,
                    onAsk: (prompt) => widget.onAsk(selected!, prompt),
                  );
            if (narrow) {
              return selected == null
                  ? list()
                  : Column(
                      children: [
                        Align(
                          alignment: Alignment.centerLeft,
                          child: IconButton(
                            tooltip: 'All C# files',
                            onPressed: () => choose(null),
                            icon: const Icon(Icons.arrow_back),
                          ),
                        ),
                        Expanded(child: content),
                      ],
                    );
            }
            return Row(
              children: [
                SizedBox(width: 250, child: list()),
                const VerticalDivider(width: 1),
                Expanded(child: content),
              ],
            );
          },
        ),
      ),
    ],
  );
}
