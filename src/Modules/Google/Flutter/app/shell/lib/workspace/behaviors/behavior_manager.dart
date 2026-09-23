import 'dart:async';

import 'package:flutter/material.dart';
import 'package:uuid/uuid.dart';

import 'behavior_detail.dart';
import 'behavior_forms.dart';

typedef BehaviorRequest = Future<Map<String, dynamic>> Function(
  String path, {
  Map<String, Object?>? body,
});
typedef BehaviorAsk = void Function(String id, String prompt);
Map<String, dynamic> behaviorMap(dynamic value) =>
    value is Map ? Map<String, dynamic>.from(value) : {};
List<Map<String, dynamic>> behaviorMaps(dynamic value) =>
    value is List ? value.whereType<Map>().map(behaviorMap).toList() : [];

String behaviorState(Map<String, dynamic> program) {
  final state = program['state'];
  final name = state is int
      ? [
          'Stopped',
          'Starting',
          'Running',
          'Stopping',
          'Completed',
          'Failed',
          'Interrupted',
        ][state.clamp(0, 6)]
      : '$state';
  return name == 'Running' && program['ready'] != true
      ? 'Waiting for readiness'
      : name;
}

String checkState(Map<String, dynamic> check) {
  final state = check['status'];
  return state is int
      ? [
          'Queued',
          'Building',
          'Testing',
          'Passed',
          'Failed',
          'Cancelled',
          'Interrupted',
        ][state.clamp(0, 6)]
      : state?.toString() ?? 'Not checked';
}

class BehaviorManager extends StatefulWidget {
  const BehaviorManager({
    super.key,
    required this.request,
    required this.onAsk,
    required this.onSelected,
    this.initialId,
    this.active = true,
  });
  final BehaviorRequest request;
  final BehaviorAsk onAsk;
  final ValueChanged<String?> onSelected;
  final String? initialId;
  final bool active;
  @override
  State<BehaviorManager> createState() => _BehaviorManagerState();
}

class _BehaviorManagerState extends State<BehaviorManager> {
  List<Map<String, dynamic>> items = [];
  Map<String, dynamic>? detail;
  String? selected, failure, notice;
  String query = '', filter = 'All';
  bool reading = false, busy = false, loaded = false, stale = true;
  int generation = 0;
  Timer? timer;
  @override
  void initState() {
    super.initState();
    selected = widget.initialId;
    unawaited(load());
    timer = Timer.periodic(const Duration(seconds: 4), (_) {
      if (widget.active) unawaited(load());
    });
  }

  @override
  void didUpdateWidget(BehaviorManager oldWidget) {
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
    timer?.cancel();
    super.dispose();
  }

  Future<void> load() async {
    if (reading || busy || !mounted) return;
    reading = true;
    final ticket = generation;
    final id = selected;
    try {
      final list = await widget.request('');
      final next = id == null
          ? null
          : await widget.request('${Uri.encodeComponent(id)}/detail');
      if (!mounted || ticket != generation) return;
      setState(() {
        items = behaviorMaps(list['items']);
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

  Future<void> command(String path, Map<String, Object?> body) async {
    if (busy || stale || selected == null) return;
    final id = selected!;
    setState(() {
      generation++;
      stale = true;
      busy = true;
      notice = null;
    });
    try {
      await widget.request('${Uri.encodeComponent(id)}/$path', body: body);
      if (mounted && selected == id) {
        setState(
          () => notice = 'Request accepted. Waiting for authoritative state…',
        );
      }
    } catch (error) {
      if (mounted && selected == id) {
        setState(
          () =>
              notice = 'Could not apply change. Refresh and try again. $error',
        );
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
        await load();
      }
    }
  }

  Future<void> editDescription({bool create = false}) async {
    final current = create ? null : behaviorMap(detail?['description']);
    final result = await showDialog<Map<String, dynamic>>(
      context: context,
      builder: (_) => BehaviorDescriptionDialog(current: current),
    );
    if (result == null || !mounted) return;
    final id = create
        ? (result.remove('id') as String? ?? 'behavior-${const Uuid().v4()}')
        : selected!;
    setState(() {
      busy = true;
      notice = null;
    });
    try {
      await widget.request(
        '${Uri.encodeComponent(id)}/description',
        body: result,
      );
      if (!mounted) return;
      choose(id);
      if (create) {
        widget.onAsk(
          id,
          'Create automation "$id" named "${result['name']}". ${result['purpose']}\nInspect installed contracts, save source and meaningful tests, and validate. Leave it as a draft for me to deploy from Automations.',
        );
      }
    } catch (error) {
      if (mounted) {
        setState(() => notice = 'Could not save automation details. $error');
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
        await load();
      }
    }
  }

  Future<void> findExisting() async {
    final id = await showDialog<String>(
      context: context,
      builder: (_) => const BehaviorFindDialog(),
    );
    if (id == null || !mounted) return;
    setState(() => busy = true);
    try {
      try {
        await widget.request('${Uri.encodeComponent(id)}/detail');
      } catch (_) {
        // Registers a known logical identity; it cannot recover hashed identities by guessing scope.
        final draft = await widget.request('${Uri.encodeComponent(id)}/draft');
        if (draft['revision'] == 0) {
          throw StateError('No saved draft with that ID in this workspace.');
        }
        await widget.request(
          '${Uri.encodeComponent(id)}/description',
          body: {
            'expectedRevision': 0,
            'name': id,
            'purpose': '',
            'triggers': <String>[],
            'effects': <String>[],
          },
        );
      }
      if (mounted) choose(id);
    } catch (error) {
      if (mounted) setState(() => notice = '$error');
    } finally {
      if (mounted) {
        setState(() => busy = false);
        await load();
      }
    }
  }

  Widget list() {
    final visible = items.where((item) {
      final description = behaviorMap(item['description']);
      final program = behaviorMap(item['program']);
      final state = behaviorState(program);
      final check = checkState(behaviorMap(item['check']));
      final text =
          '${description['name']} ${description['purpose']} ${description['id']}'
              .toLowerCase();
      final deployments = behaviorMaps(program['deployments']);
      return text.contains(query.toLowerCase()) &&
          switch (filter) {
            'Running' => state == 'Running',
            'Needs attention' =>
              ['Failed', 'Interrupted'].contains(state) ||
                  ['Failed', 'Interrupted'].contains(check),
            'Drafts' => deployments.isEmpty,
            _ => true,
          };
    }).toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.all(12),
          child: TextField(
            decoration: const InputDecoration(
              prefixIcon: Icon(Icons.search),
              hintText: 'Find an automation',
              isDense: true,
            ),
            onChanged: (value) => setState(() => query = value),
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12),
          child: DropdownButtonFormField<String>(
            isExpanded: true,
            initialValue: filter,
            decoration: const InputDecoration(
              isDense: true,
              border: InputBorder.none,
            ),
            items: [
              'All',
              'Running',
              'Needs attention',
              'Drafts',
            ].map((f) => DropdownMenuItem(value: f, child: Text(f))).toList(),
            onChanged: (value) => setState(() => filter = value ?? 'All'),
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
                          ? 'Make something happen.\nCreate your first automation with the assistant.'
                          : 'No matching automations.',
                      textAlign: TextAlign.center,
                    ),
                  ),
                )
              : ListView.builder(
                  itemCount: visible.length,
                  itemBuilder: (context, index) {
                    final item = visible[index];
                    final d = behaviorMap(item['description']);
                    final p = behaviorMap(item['program']);
                    final state = (p['deployments'] as List? ?? []).isEmpty
                        ? 'Draft'
                        : behaviorState(p);
                    return ListTile(
                      selected: selected == d['id'],
                      onTap: () => choose(d['id'] as String),
                      leading: Icon(
                        state == 'Failed'
                            ? Icons.error_outline
                            : Icons.account_tree_outlined,
                        size: 20,
                      ),
                      title: Text(
                        d['name'] as String,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                      subtitle: Text(
                        '${stale ? 'State unavailable' : state}\n${d['purpose'] ?? ''}',
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
                'Automations',
                style: TextStyle(fontSize: 20, fontWeight: FontWeight.w600),
              ),
            ),
            IconButton(
              tooltip: 'Find existing automation by ID',
              onPressed: busy ? null : findExisting,
              icon: const Icon(Icons.link),
            ),
            IconButton(
              tooltip: 'Refresh automations',
              onPressed: load,
              icon: const Icon(Icons.refresh),
            ),
            if (MediaQuery.sizeOf(context).width >= 700)
              FilledButton.icon(
                onPressed: busy || stale
                    ? null
                    : () => editDescription(create: true),
                icon: const Icon(Icons.add, size: 18),
                label: const Text('New automation'),
              )
            else
              IconButton(
                tooltip: 'New automation',
                onPressed: busy || stale
                    ? null
                    : () => editDescription(create: true),
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
                ? const Center(
                    child: Text('Select an automation to see what it does.'),
                  )
                : detail == null
                ? Center(
                    child: stale && failure != null
                        ? const Text(
                            'Could not load this automation. Refresh to retry.',
                          )
                        : const CircularProgressIndicator(),
                  )
                : BehaviorDetailView(
                    key: ValueKey(selected),
                    detail: detail!,
                    enabled: !busy && !stale,
                    stale: stale,
                    onCommand: command,
                    onDescribe: editDescription,
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
                            tooltip: 'All automations',
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
