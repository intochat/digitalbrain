import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'slow saves keep the in-flight snapshot and coalesce pending edits',
    () async {
      final persistence = _SlowPersistence();
      final store = WorkspaceStore(persistence: persistence);
      store.currentConversation.draft = 'First';
      var firstCompleted = false;
      final first = store.save().then((_) => firstCompleted = true);
      await Future<void>.delayed(Duration.zero);
      expect(persistence.drafts, ['First']);

      store.currentConversation.draft = 'Second';
      final second = store.save();
      store.currentConversation.draft = 'Latest';
      final latest = store.save();
      var flushed = false;
      final flush = store.flush().then((_) => flushed = true);
      expect(persistence.drafts, ['First']);
      expect(flushed, isFalse);

      persistence.completions[0].complete();
      await Future<void>.delayed(Duration.zero);
      expect(persistence.drafts, ['First', 'Latest']);
      expect(firstCompleted, isTrue);
      expect(flushed, isFalse);
      persistence.completions[1].complete();
      await Future.wait([first, second, latest, flush]);
      expect(flushed, isTrue);
      expect(persistence.drafts, ['First', 'Latest']);
      expect(store.persistenceError, isNull);
      store.dispose();
    },
  );

  test('a failed write retains the latest pending draft and later saves still work', () async {
    final persistence = _SlowPersistence();
    final store = WorkspaceStore(persistence: persistence);
    store.currentConversation.draft = 'First';
    final first = store.save();
    await Future<void>.delayed(Duration.zero);
    store.currentConversation.draft = 'Latest';
    final latest = store.save();
    persistence.completions[0].completeError(StateError('Server unavailable'));
    await Future<void>.delayed(Duration.zero);
    expect(store.persistenceError, isNotNull);
    expect(persistence.drafts, ['First', 'Latest']);
    expect(store.currentConversation.draft, 'Latest');
    persistence.completions[1].complete();
    await Future.wait([first, latest, store.flush()]);
    expect(store.persistenceError, isNull);

    store.currentConversation.draft = 'After flush';
    final after = store.save();
    await Future<void>.delayed(Duration.zero);
    expect(persistence.drafts, ['First', 'Latest', 'After flush']);
    persistence.completions[2].complete();
    await after;
    store.dispose();
  });
}

class _SlowPersistence implements WorkspacePersistence {
  final drafts = <String>[];
  final completions = <Completer<void>>[];
  @override
  Future<String?> read() async => null;
  @override
  Future<void> write(String value) {
    final snapshot = jsonDecode(value);
    drafts.add(snapshot['projects'][0]['conversations'][0]['draft'] as String);
    final completion = Completer<void>();
    completions.add(completion);
    return completion.future;
  }
}
