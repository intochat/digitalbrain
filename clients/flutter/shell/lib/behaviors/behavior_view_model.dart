import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/foundation.dart';

import 'behavior_demo_fixtures.dart';

enum BehaviorStudioView {
  library,
  overview,
  scenarios,
  assistantChange,
  source,
  revisions,
}

final class BehaviorStudioController extends ChangeNotifier {
  BehaviorStudioController({this.client}) {
    if (client == null) {
      library = BehaviorDemoFixtures.library;
      showingDemoFixtures = true;
      statusMessage =
          'Demo fixtures — offline. Seed live grains: dart run bin/seed_demo_behaviors.dart';
    }
  }

  final BehaviorClient? client;

  BehaviorStudioView view = BehaviorStudioView.library;
  List<BehaviorLibraryItem> library = const [];
  BehaviorDocument? selected;
  BehaviorChangeProposal? pendingProposal;
  String? statusMessage;
  bool loading = false;
  String? lastRunOutcome;
  bool showingDemoFixtures = false;

  bool get hasClient => client != null;

  Future<void> refreshLibrary() async {
    final edge = client;
    if (edge == null) {
      library = BehaviorDemoFixtures.library;
      showingDemoFixtures = true;
      statusMessage =
          'Demo fixtures — offline. Seed live grains: dart run bin/seed_demo_behaviors.dart';
      notifyListeners();
      return;
    }

    loading = true;
    statusMessage = null;
    notifyListeners();
    try {
      final document = await edge.listBehaviors();
      if (document.items.isEmpty) {
        library = BehaviorDemoFixtures.library;
        showingDemoFixtures = true;
        statusMessage =
            'Demo fixtures — edge has no behaviors yet. Seed: dart run bin/seed_demo_behaviors.dart';
      } else {
        library = document.items;
        showingDemoFixtures = false;
      }
    } on Object catch (error) {
      library = BehaviorDemoFixtures.library;
      showingDemoFixtures = true;
      statusMessage =
          'Demo fixtures — HTTP error ($error). Seed live when Kernel HTTP is up.';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> openBehavior(String behaviorId) async {
    final edge = client;
    final fixture = BehaviorDemoFixtures.documentFor(behaviorId);

    if (edge == null || showingDemoFixtures) {
      if (fixture == null) {
        statusMessage = 'Unknown demo behavior: $behaviorId';
        notifyListeners();
        return;
      }
      selected = fixture;
      view = BehaviorStudioView.overview;
      pendingProposal = null;
      lastRunOutcome = fixture.lastExecutionOutcome;
      statusMessage =
          'Demo document — mutations are local-only until seeded into DigitalBrain.';
      notifyListeners();
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
      if (fixture != null) {
        selected = fixture;
        view = BehaviorStudioView.overview;
        pendingProposal = null;
        lastRunOutcome = fixture.lastExecutionOutcome;
        statusMessage = 'Demo document — edge read failed: $error';
      } else {
        statusMessage = '$error';
      }
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
    if (current == null) {
      return;
    }
    if (edge == null || showingDemoFixtures) {
      statusMessage =
          'Demo fixture — Stop is live-only. Seed behaviors into DigitalBrain first.';
      notifyListeners();
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
    if (current == null) {
      return;
    }
    if (edge == null || showingDemoFixtures) {
      statusMessage =
          'Demo fixture — Start is live-only. Seed behaviors into DigitalBrain first.';
      notifyListeners();
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
    String triggerTypeName = 'EnrichFromLatestEmail',
    String triggerJson =
        '{"GmailAccount":"default","AccountId":"001DEMO000000000"}',
  }) async {
    final edge = client;
    final current = selected;
    if (current == null) {
      return;
    }
    if (edge == null || showingDemoFixtures) {
      lastRunOutcome =
          'demo-run-once: would execute $triggerTypeName with $triggerJson against the active revision';
      statusMessage =
          'Demo fixture — Run once is simulated. Seed for a real BehaviorHost attempt.';
      notifyListeners();
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

  Future<void> runTestsSelected() async {
    final edge = client;
    final current = selected;
    final artifactHash = current?.proposedArtifactHash;
    if (edge == null || current == null || artifactHash == null || artifactHash.isEmpty) {
      return;
    }

    loading = true;
    statusMessage = null;
    notifyListeners();
    try {
      selected = await edge.runTests(
        behaviorId: current.behaviorId,
        artifactHash: artifactHash,
      );
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> approveSelected() async {
    final edge = client;
    final current = selected;
    final artifactHash = current?.proposedArtifactHash;
    if (edge == null || current == null || artifactHash == null || artifactHash.isEmpty) {
      return;
    }

    loading = true;
    statusMessage = null;
    notifyListeners();
    try {
      selected = await edge.approve(
        behaviorId: current.behaviorId,
        artifactHash: artifactHash,
        approvalId: _newApprovalId(),
      );
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> activateSelected() async {
    final edge = client;
    final current = selected;
    final artifactHash = current?.proposedArtifactHash ?? current?.activeArtifactHash;
    if (edge == null || current == null || artifactHash == null || artifactHash.isEmpty) {
      return;
    }

    loading = true;
    statusMessage = null;
    notifyListeners();
    try {
      selected = await edge.activate(
        behaviorId: current.behaviorId,
        artifactHash: artifactHash,
      );
    } on Object catch (error) {
      statusMessage = '$error';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  static String _newApprovalId() {
    final now = DateTime.now().toUtc().microsecondsSinceEpoch.toRadixString(16).padLeft(12, '0');
    return '00000000-0000-4000-8000-${now.substring(now.length - 12)}';
  }
}
