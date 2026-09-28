import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:uuid/uuid.dart';

import 'workspace_store.dart';

/// A disposable client projection. The server partitions this transport snapshot
/// into scoped neurons; no application state is written to device preferences.
final class NeuronWorkspacePersistence
    implements WorkspacePersistence, WorkspaceImportRecovery {
  NeuronWorkspacePersistence({
    required this.client,
    this.legacy,
    this.legacyKey = '',
  });

  final DigitalBrainUiClient client;
  final WorkspacePersistence? legacy;
  final String legacyKey;
  int? _revision;
  bool _conflicted = false;
  Map<String, dynamic>? _pending;
  bool _skipLegacy = false;

  @override
  void useServerVersion() {
    _skipLegacy = true;
  }

  Future<Map<String, dynamic>> _request(
    String method,
    String path, [
    Map<String, dynamic>? command,
  ]) async => Map<String, dynamic>.from(
    await client.jsonRequest(method, path, command) as Map,
  );

  @override
  Future<String?> read() async {
    // Do not silently rebase unsaved writes onto a newer server revision.
    _revision = null;
    var state = await _request('GET', '/shell/state');
    final raw = _skipLegacy ? null : await legacy?.read();
    if (raw != null) {
      final snapshot = jsonDecode(raw);
      if (snapshot is! Map ||
          snapshot['version'] != 1 ||
          snapshot['projects'] is! List) {
        throw const FormatException(
          'Legacy workspace cannot be imported. Its source has been retained.',
        );
      }
      final imported = await _request('POST', '/shell/import', {
        'expectedRevision': state['revision'],
        'operationId': const Uuid().v5(Namespace.url.value, '$legacyKey:$raw'),
        'snapshot': snapshot,
      });
      state = await _request('GET', '/shell/state');
      if (state['revision'] != imported['revision'] ||
          _canonical(state['snapshot']) != _canonical(imported['snapshot'])) {
        throw StateError(
          'Imported workspace could not be verified. Retry loading; the original remains available.',
        );
      }
    }
    _revision = state['revision'] as int;
    _conflicted = false;
    _pending = null;
    return state['snapshot'] == null ? null : jsonEncode(state['snapshot']);
  }

  @override
  Future<void> write(String value) async {
    if (_conflicted) throw const WorkspaceSaveConflict();
    if (_revision == null) throw StateError('Load saved work before saving.');
    // A network failure may arrive after the server committed. Replay exactly
    // that operation before issuing the latest snapshot with a new revision.
    if (_pending != null) {
      final previous = _canonical(_pending!['snapshot']);
      await _commit();
      if (previous == _canonical(jsonDecode(value))) return;
    }
    _pending = {
      'expectedRevision': _revision,
      'operationId': const Uuid().v4(),
      'snapshot': jsonDecode(value),
    };
    await _commit();
  }

  Future<void> _commit() async {
    try {
      final state = await _request('PUT', '/shell/state', _pending);
      if (state['revision'] != (_pending!['expectedRevision'] as int) + 1) {
        _conflicted = true;
        throw const WorkspaceSaveConflict();
      }
      _revision = state['revision'] as int;
      _pending = null;
    } on UiRequestException catch (error) {
      if (error.statusCode == 409) {
        _conflicted = true;
        throw const WorkspaceSaveConflict();
      }
      rethrow;
    }
  }

  static String _canonical(Object? value) {
    Object? sorted(Object? value) => switch (value) {
      Map map => {
        for (final key in map.keys.cast<String>().toList()..sort())
          key: sorted(map[key]),
      },
      List list => list.map(sorted).toList(),
      _ => value,
    };
    return jsonEncode(sorted(value));
  }
}
