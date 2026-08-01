import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/foundation.dart';

enum BehaviorStudioView {
  library,
  overview,
  scenarios,
  assistantChange,
  source,
  revisions,
}

final class BehaviorStudioController extends ChangeNotifier {
  BehaviorStudioController({this.client});

  final BehaviorClient? client;

  BehaviorStudioView view = BehaviorStudioView.library;
  List<BehaviorLibraryItem> library = const [];
  BehaviorDocument? selected;
  BehaviorChangeProposal? pendingProposal;
  String? statusMessage;
  bool loading = false;
  String? lastRunOutcome;

  bool get hasClient => client != null;

  Future<void> refreshLibrary() async {
    final edge = client;
    if (edge == null) {
      library = const [];
      notifyListeners();
      return;
    }

    loading = true;
    statusMessage = null;
    notifyListeners();
    try {
      final document = await edge.listBehaviors();
      library = document.items;
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> openBehavior(String behaviorId) async {
    final edge = client;
    if (edge == null) {
      return;
    }

    loading = true;
    statusMessage = null;
    notifyListeners();
    try {
      selected = await edge.readBehavior(behaviorId);
      view = BehaviorStudioView.overview;
      pendingProposal = null;
      lastRunOutcome = null;
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  void showView(BehaviorStudioView next) {
    if (view == next) {
      return;
    }
    view = next;
    notifyListeners();
  }

  void backToLibrary() {
    view = BehaviorStudioView.library;
    selected = null;
    pendingProposal = null;
    lastRunOutcome = null;
    notifyListeners();
  }

  Future<void> stopSelected() async {
    final edge = client;
    final current = selected;
    if (edge == null || current == null) {
      return;
    }

    loading = true;
    statusMessage =
        'Stop cancels active Tasks and closes the activation gate. The behavior revision is kept.';
    notifyListeners();
    try {
      selected = await edge.stop(current.behaviorId);
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> startSelected() async {
    final edge = client;
    final current = selected;
    if (edge == null || current == null) {
      return;
    }

    loading = true;
    statusMessage = null;
    notifyListeners();
    try {
      selected = await edge.start(current.behaviorId);
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> runOnceSelected({
    String triggerTypeName = 'EnrichTrigger',
    String triggerJson =
        '{"MessageId":"demo","AccountId":"demo","GmailAccount":"demo@example.com"}',
  }) async {
    final edge = client;
    final current = selected;
    if (edge == null || current == null) {
      return;
    }

    loading = true;
    statusMessage = null;
    notifyListeners();
    try {
      final result = await edge.runOnce(
        behaviorId: current.behaviorId,
        triggerTypeName: triggerTypeName,
        triggerJson: triggerJson,
      );
      selected = result.document;
      lastRunOutcome = result.outcome;
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> setBindingEnabled(String bindingId, bool enabled) async {
    final edge = client;
    final current = selected;
    if (edge == null || current == null) {
      return;
    }

    loading = true;
    notifyListeners();
    try {
      selected = await edge.setBindingEnabled(
        behaviorId: current.behaviorId,
        bindingId: bindingId,
        enabled: enabled,
      );
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> proposeChange(String requestText) async {
    final edge = client;
    final current = selected;
    if (edge == null || current == null || requestText.trim().isEmpty) {
      return;
    }

    loading = true;
    statusMessage = null;
    notifyListeners();
    try {
      pendingProposal = await edge.proposeChange(
        behaviorId: current.behaviorId,
        requestText: requestText.trim(),
      );
      view = BehaviorStudioView.assistantChange;
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> approvePendingScenario({required bool approved}) async {
    final edge = client;
    final current = selected;
    final proposal = pendingProposal;
    if (edge == null || current == null || proposal == null) {
      return;
    }

    loading = true;
    notifyListeners();
    try {
      final result = await edge.approveScenarioChange(
        behaviorId: current.behaviorId,
        proposalId: proposal.proposalId,
        approved: approved,
      );
      if (result is BehaviorDocument) {
        selected = result;
        pendingProposal = null;
        view = BehaviorStudioView.source;
      } else if (result is BehaviorChangeProposal) {
        pendingProposal = result;
      }
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> rollbackSelected() async {
    final edge = client;
    final current = selected;
    if (edge == null || current == null) {
      return;
    }

    loading = true;
    notifyListeners();
    try {
      selected = await edge.rollback(current.behaviorId);
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> proposeSource({
    required String programSource,
    required String featureText,
  }) async {
    final edge = client;
    final current = selected;
    if (edge == null || current == null) {
      return;
    }

    loading = true;
    notifyListeners();
    try {
      selected = await edge.propose(
        behaviorId: current.behaviorId,
        programSource: programSource,
        featureText: featureText,
        featureName: current.featureName,
        displayName: current.displayName,
        description: current.description,
      );
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }
}
