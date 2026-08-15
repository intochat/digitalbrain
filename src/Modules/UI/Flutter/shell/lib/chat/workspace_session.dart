import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/foundation.dart';

import 'chat_contracts.dart';

/// Stream subscriptions and projected state for [BrainWorkspace].
///
/// Keeps durable chat/topology session out of the widget tree so the shell
/// can rebuild chrome without re-owning SSE bookkeeping.
final class WorkspaceSession extends ChangeNotifier {
  WorkspaceSession({
    required this.chatName,
    Stream<ChatTurnEvent>? turns,
    Stream<AuthorizationEvent>? authorizations,
    Stream<GraphChangeEvent>? graphChanges,
    this.onLoadTopology,
  }) {
    listenTurns(turns);
    listenAuthorizations(authorizations);
    listenGraphChanges(graphChanges);
    unawaited(refreshTopology());
  }

  String chatName;
  LoadTopology? onLoadTopology;

  final _turns = <ChatTurnEvent>[];
  final _seen = <int>{};
  final _authorizationEvents = <AuthorizationEvent>[];
  final _seenAuthorizations = <int>{};

  List<ChatTurnEvent> projectedTurns = const [];
  List<SignInCardProjection> signInCards = const [];
  GraphChangeEvent? graphChange;
  BrainTopologySnapshot? topology;
  String? turnFailure;
  String? topologyFailure;

  StreamSubscription<ChatTurnEvent>? _turnSubscription;
  StreamSubscription<AuthorizationEvent>? _authorizationSubscription;
  StreamSubscription<GraphChangeEvent>? _graphSubscription;
  int _topologyLoadEpoch = 0;

  void updateChatName(String name) {
    if (chatName == name) {
      return;
    }
    chatName = name;
    _turns.clear();
    _seen.clear();
    projectedTurns = const [];
    notifyListeners();
    unawaited(refreshTopology());
  }

  void listenTurns(Stream<ChatTurnEvent>? turns) {
    unawaited(_turnSubscription?.cancel());
    turnFailure = null;
    _turnSubscription = turns?.listen(
      (turn) {
        if (!_seen.add(turn.sequence)) {
          return;
        }
        _turns.add(turn);
        _turns.sort((a, b) => a.sequence.compareTo(b.sequence));
        projectedTurns = List<ChatTurnEvent>.unmodifiable(_turns);
        notifyListeners();
        unawaited(refreshTopology());
      },
      onError: (Object error) {
        turnFailure = '$error';
        notifyListeners();
      },
    );
  }

  void listenAuthorizations(Stream<AuthorizationEvent>? authorizations) {
    unawaited(_authorizationSubscription?.cancel());
    _authorizationSubscription = authorizations?.listen(
      (event) {
        if (!_seenAuthorizations.add(event.sequence)) {
          return;
        }
        _authorizationEvents.add(event);
        signInCards = SignInCardProjection.project(_authorizationEvents);
        notifyListeners();
      },
      onError: (Object error) {
        turnFailure = '$error';
        notifyListeners();
      },
    );
  }

  void listenGraphChanges(Stream<GraphChangeEvent>? graphChanges) {
    unawaited(_graphSubscription?.cancel());
    _graphSubscription = graphChanges?.listen(
      (change) {
        graphChange = change;
        notifyListeners();
        unawaited(refreshTopology());
      },
      onError: (Object error) {
        topologyFailure = '$error';
        notifyListeners();
      },
    );
  }

  Future<void> refreshTopology() async {
    final load = onLoadTopology;
    if (load == null) {
      return;
    }

    final epoch = ++_topologyLoadEpoch;
    try {
      final snapshot = await load();
      if (epoch != _topologyLoadEpoch) {
        return;
      }
      topology = snapshot;
      topologyFailure = null;
      notifyListeners();
    } on Object catch (error) {
      if (epoch != _topologyLoadEpoch) {
        return;
      }
      topology = null;
      topologyFailure = '$error';
      notifyListeners();
    }
  }

  String? statusMessage(String? external) =>
      external ?? turnFailure ?? topologyFailure;

  @override
  void dispose() {
    unawaited(_turnSubscription?.cancel());
    unawaited(_authorizationSubscription?.cancel());
    unawaited(_graphSubscription?.cancel());
    super.dispose();
  }
}
