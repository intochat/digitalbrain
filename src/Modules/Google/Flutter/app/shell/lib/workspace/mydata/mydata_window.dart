import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

typedef MyDataRequest = Future<Map<String, dynamic>> Function(
  String path, {
  Map<String, Object?>? body,
});

/// Minimal My Data window: lists the owner's field handles (values masked by the vault), the
/// grants apps hold over them with a one-click Revoke, and writes a typed value through the same
/// module the assistant stores `me.birthDate` in.
class MyDataWindow extends StatefulWidget {
  const MyDataWindow({
    super.key,
    required this.request,
    this.loadGrants,
    this.revokeGrant,
  });

  final MyDataRequest request;
  final Future<List<GrantSummary>> Function()? loadGrants;
  final Future<void> Function(GrantSummary grant)? revokeGrant;

  @override
  State<MyDataWindow> createState() => _MyDataWindowState();
}

class _MyDataWindowState extends State<MyDataWindow> {
  List<Map<String, dynamic>> fields = [];
  List<GrantSummary> grants = [];
  String? error;
  String? grantsError;
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
      List<GrantSummary> loadedGrants = const [];
      String? grantFailure;
      if (widget.loadGrants != null) {
        try {
          loadedGrants = await widget.loadGrants!();
        } catch (failure) {
          grantFailure = 'Grants are unavailable. $failure';
        }
      }
      if (!mounted || ticket != generation) return;
      setState(() {
        fields = [
          for (final field in (result['fields'] as List? ?? const []))
            Map<String, dynamic>.from(field as Map),
        ];
        grants = loadedGrants;
        grantsError = grantFailure;
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

  Future<void> revoke(GrantSummary grant) async {
    final revoke = widget.revokeGrant;
    if (revoke == null) return;
    try {
      await revoke(grant);
      if (!mounted) return;
      setState(() => grants = [...grants]..remove(grant));
    } catch (failure) {
      if (mounted) setState(() => grantsError = 'Could not revoke. $failure');
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
            : _body(context),
      ),
    ],
  );

  Widget _body(BuildContext context) {
    final theme = Theme.of(context);
    final rows = <Widget>[
      if (fields.isEmpty)
        const Padding(
          padding: EdgeInsets.all(16),
          child: Text(
            'Nothing stored yet. Ask the assistant to remember a detail.',
          ),
        )
      else ...[
        _SectionHeader('Stored fields'),
        for (final field in fields)
          ListTile(
            leading: Icon(
              (field['isSecret'] as bool? ?? false)
                  ? Icons.key_outlined
                  : Icons.shield_outlined,
            ),
            title: Text('${field['fieldPath']}'),
            subtitle: Text('${field['kind']} · ${field['display']}'),
          ),
      ],
      _SectionHeader('Grants'),
      if (grantsError != null)
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Text(
            grantsError!,
            style: TextStyle(color: theme.colorScheme.error),
          ),
        ),
      if (grants.isEmpty)
        const Padding(
          padding: EdgeInsets.fromLTRB(16, 0, 16, 16),
          child: Text('No app can read your data.'),
        )
      else
        for (final grant in grants)
          ListTile(
            leading: const Icon(Icons.vpn_key_outlined),
            title: Text(grant.semanticTypeId),
            subtitle: Text(
              '${grant.appId} · ${grant.mode} · granted ${_date(grant.grantedAt)}',
            ),
            trailing: widget.revokeGrant == null
                ? null
                : TextButton(
                    onPressed: () => unawaited(revoke(grant)),
                    child: const Text('Revoke'),
                  ),
          ),
    ];
    return ListView(children: rows);
  }

  static String _date(DateTime value) => value.year <= 1
      ? 'unknown'
      : '${value.year}-${value.month.toString().padLeft(2, '0')}-${value.day.toString().padLeft(2, '0')}';
}

class _SectionHeader extends StatelessWidget {
  const _SectionHeader(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(16, 14, 16, 6),
    child: Text(
      text,
      style: Theme.of(
        context,
      ).textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w600),
    ),
  );
}