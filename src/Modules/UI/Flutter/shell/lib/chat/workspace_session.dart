import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/foundation.dart';

/// Stream subscriptions and projected state for [BrainWorkspace].
///
/// Keeps durable chat session state out of the widget tree so the shell
/// can rebuild chrome without re-owning SSE bookkeeping.
final class WorkspaceSession extends ChangeNotifier {
  WorkspaceSession({required this.chatName, Stream<ChatStreamEvent>? turns}) {
    listenTurns(turns);
  }

  String chatName;

  final _turns = <ChatTurnEvent>[];
  final _seen = <int>{};
  List<ChatTurnEvent> projectedTurns = const [];
  String? turnFailure;

  StreamSubscription<ChatStreamEvent>? _turnSubscription;

  void updateChatName(String name) {
    if (chatName == name) {
      return;
    }
    chatName = name;
    _turns.clear();
    _seen.clear();
    projectedTurns = const [];
    notifyListeners();
  }

  void listenTurns(Stream<ChatStreamEvent>? turns) {
    unawaited(_turnSubscription?.cancel());
    turnFailure = null;
    _turnSubscription = turns?.listen(
      (event) {
        switch (event) {
          case ChatTurnObserved(:final turn):
            if (!_seen.add(turn.sequence)) return;
            _turns.add(turn);
            _turns.sort((a, b) => a.sequence.compareTo(b.sequence));
          case ChatJournalReset(:final turns):
            _turns
              ..clear()
              ..addAll(turns);
            _seen
              ..clear()
              ..addAll(turns.map((turn) => turn.sequence));
        }
        projectedTurns = List<ChatTurnEvent>.unmodifiable(_turns);
        notifyListeners();
      },
      onError: (Object error) {
        turnFailure = '$error';
        notifyListeners();
      },
    );
  }

  String? statusMessage(String? external) => external ?? turnFailure;

  @override
  void dispose() {
    unawaited(_turnSubscription?.cancel());
    super.dispose();
  }
}
