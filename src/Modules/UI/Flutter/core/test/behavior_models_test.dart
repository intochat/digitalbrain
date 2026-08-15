import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

void main() {
  test('BehaviorDocument reads studio projection fields from OS UI JSON', () {
    final document = BehaviorDocument.fromJson({
      'behaviorId': 'com.digitalbrain.account-enrichment',
      'status': 'Active',
      'runState': 'Running',
      'activationGateOpen': true,
      'proposedArtifactHash': 'p1',
      'activeArtifactHash': 'a1',
      'priorArtifactHash': 'r1',
      'lastCompileFailure': null,
      'testsPassed': true,
      'isApproved': true,
      'lastExecutionOutcome': 'ok',
      'programSource': 'class Program {}',
      'featureName': 'account-enrichment',
      'featureText': 'Feature: account\n  Scenario: enrich\n',
      'displayName': 'Account enrichment',
      'description': 'Enrich accounts',
      'overview': 'Account enrichment: enrich',
      'activeSignatureHex': 'ABCD',
      'activeTaskCount': 1,
      'scenarios': [
        {
          'scenarioId': 'scenario.enrich',
          'title': 'enrich',
          'bindingKey': 'bind.enrich',
          'passed': true,
          'detail': null,
        },
      ],
      'bindings': [
        {
          'bindingId': 'task__case.Enrich',
          'sourceModule': 'task',
          'sourceSynapse': 'ActivateBoundBehavior',
          'targetCase': 'case.Enrich',
          'contractVersion': '1',
          'enabled': true,
          'configurationHint': 'opaque',
        },
      ],
      'revisions': [
        {
          'role': 'active',
          'artifactHash': 'a1',
          'signatureHex': 'ABCD',
          'status': 'Active',
          'isActive': true,
        },
      ],
    });

    expect(document.behaviorId, 'com.digitalbrain.account-enrichment');
    expect(document.isRunning, isTrue);
    expect(document.canStop, isTrue);
    expect(document.scenarios.single.title, 'enrich');
    expect(document.bindings.single.configurationHint, 'opaque');
    expect(document.revisions.single.isActive, isTrue);
  });

  test('BehaviorChangeProposal marks awaiting scenario approval', () {
    final proposal = BehaviorChangeProposal.fromJson({
      'proposalId': 'p',
      'behaviorId': 'b',
      'requestText': 'add phone',
      'proposedFeatureText': 'Feature:\n  Scenario: add phone\n',
      'proposedFeatureName': 'install',
      'status': 'awaiting-scenario-approval',
      'diffSummary': 'Add scenario',
    });

    expect(proposal.awaitsScenarioApproval, isTrue);
    expect(proposal.proposedFeatureText, contains('Scenario:'));
  });

  test('BehaviorLibraryItem classifies draft/running/stopped health', () {
    final draft = BehaviorLibraryItem.fromJson({
      'behaviorId': 'b',
      'displayName': 'B',
      'description': 'd',
      'status': 'Empty',
      'runState': 'Idle',
      'activationGateOpen': false,
      'overview': 'o',
      'scenarioTitles': <String>['s'],
      'health': 'draft',
    });
    expect(draft.isDraft, isTrue);
    expect(draft.isRunning, isFalse);
  });
}
