import 'dart:async';

import 'package:flutter/material.dart';

typedef MyDataRequest = Future<Map<String, dynamic>> Function(
  String path, {
  Map<String, Object?>? body,
});

/// Minimal My Data window: lists the owner's field handles (values masked by the vault) and
/// writes a typed value through the same module the assistant stores `me.birthDate` in.
class MyDataWindow extends StatefulWidget {
  const MyDataWindow({super.key, required this.request});

  final MyDataRequest request;

  @override
  State<MyDataWindow> createState() => _MyDataWindowState();
}

class _MyDataWindowState extends State<MyDataWindow> {
  List<Map<String, dynamic>> fields = [];
  String? error;
  bool loading = false;
  bool loaded = false;
  int generation = 0;

  @override
  void initState() {
    super.initState();
    unawaited(load());
  }

  Future<void> load() async {
    final ticket = ++generation;
    setState(() => loading = true);
    try {
      final result = await widget.request('');
      if (!mounted || ticket != generation) return;
      setState(() {
        fields = [
          for (final field in (result['fields'] as List? ?? const []))
            Map<String, dynamic>.from(field as Map),
        ];
        loaded = true;
        error = null;
      });
    } catch (failure) {
      if (mounted && ticket == generation) {
        setState(() => error = 'My Data is unavailable. $failure');
      }
    } finally {
      if (mounted && ticket == generation) setState(() => loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Padding(
        padding: const EdgeInsets.fromLTRB(16, 10, 8, 10),
        child: Row(
          children: [
            const Expanded(
              child: Text(
                'My Data',
                style: TextStyle(fontSize: 20, fontWeight: FontWeight.w600),
              ),
            ),
            IconButton(
              tooltip: 'Refresh My Data',
              onPressed: load,
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
      ),
      if (error != null)
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Text(
            error!,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
        ),
      if (loading) const LinearProgressIndicator(minHeight: 2),
      const Divider(height: 1),
      Expanded(
        child: !loaded
            ? (error != null
                  ? const SizedBox.shrink()
                  : const Center(child: CircularProgressIndicator()))
            : fields.isEmpty
            ? const Center(
                child: Text('Nothing stored yet. Ask the assistant to remember a detail.'),
              )
            : ListView(
                children: [
                  for (final field in fields)
                    ListTile(
                      leading: Icon(
                        (field['isSecret'] as bool? ?? false)
                            ? Icons.key_outlined
                            : Icons.shield_outlined,
                      ),
                      title: Text('${field['fieldPath']}'),
                      subtitle: Text(
                        '${field['kind']} · ${field['display']}',
                      ),
                    ),
                ],
              ),
      ),
    ],
  );
}
