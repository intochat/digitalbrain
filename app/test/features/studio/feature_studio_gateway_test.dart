import 'dart:convert';

import 'package:digitalbrain_flutter/core/session/digitalbrain_client.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_controller.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_gateway.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_models.dart';
import 'package:digitalbrain_flutter/grpc/ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/runtime/runtime_errors.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter_test/flutter_test.dart';

const _releaseDigest =
    'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
const _sourceReference =
    'sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';
const _previousDigest =
    'dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd';
const _previousSourceReference =
    'sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee';
const _artifactDigest =
    'sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc';

void main() {
  test(
    'load maps a complete generated draft into a validated Studio model',
    () async {
      final client = _RecordingFeatureAuthoringClient()
        ..getReply = wire.FeatureDraftReply(draft: _validWireDraft());
      final gateway = GrpcFeatureStudioGateway(client: client);

      final draft = await gateway.loadDraft('draft-a');

      expect(client.getRequest?.draftId, 'draft-a');
      expect(draft.draftId, 'draft-a');
      expect(draft.revision, Int64(4));
      expect(draft.originatingRequest.text, 'Research Acme');
      expect(draft.behavior.scenarios.single.name, 'Create a brief');
      expect(draft.source.files, hasLength(3));
      expect(draft.status, FeatureStudioDraftStatus.draft);
    },
  );

  test(
    'load accepts a revision-zero Draft after a protobuf round trip',
    () async {
      final serialized = wire.FeatureDraftReply(
        draft: _validWireDraft(revision: Int64.ZERO),
      );
      serialized.draft.clearRevision();
      final client = _RecordingFeatureAuthoringClient()
        ..getReply = wire.FeatureDraftReply.fromBuffer(
          serialized.writeToBuffer(),
        );
      final gateway = GrpcFeatureStudioGateway(client: client);

      expect(client.getReply.draft.hasRevision(), isFalse);
      final draft = await gateway.loadDraft('draft-a');

      expect(draft.revision, Int64.ZERO);
    },
  );

  test(
    'pending install reset forwards exact identity and maps a cleared update Draft',
    () async {
      final resetDraft = _validWireDraft(revision: Int64(5))
        ..installationId = 'installation-a';
      final client = _RecordingFeatureAuthoringClient()
        ..resetReply = wire.FeatureDraftReply(draft: resetDraft);
      final gateway = GrpcFeatureStudioGateway(client: client);

      final draft = await gateway.resetPendingInstall(
        draftId: 'draft-a',
        idempotencyId: 'reset-a',
      );

      expect(client.resetRequest?.draftId, 'draft-a');
      expect(client.resetRequest?.idempotencyId, 'reset-a');
      expect(draft.status, FeatureStudioDraftStatus.draft);
      expect(draft.installationId, 'installation-a');
      expect(draft.verification, isNull);
      expect(draft.installationRecovery, isNull);
      expect(draft.revision, Int64(5));
    },
  );

  test(
    'pending install reset rejects uncleared governed response state',
    () async {
      final withVerification = _validWireDraft(revision: Int64(5))
        ..verification = _wireVerificationSummary();
      final withRecovery = wire.FeatureDraftReply(
        draft: _validWireDraft(revision: Int64(5)),
        recovery: _wireRecoveryReply(installed: false).recovery,
      );
      final installed = _validWireDraft(revision: Int64(5))
        ..status = wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_INSTALLED
        ..installationId = 'installation-a';
      final zeroRevision = _validWireDraft(revision: Int64.ZERO);
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);

      for (final invalid in [
        wire.FeatureDraftReply(draft: withVerification),
        withRecovery,
        wire.FeatureDraftReply(draft: installed),
        wire.FeatureDraftReply(draft: zeroRevision),
      ]) {
        client.resetReply = invalid;
        await expectLater(
          gateway.resetPendingInstall(
            draftId: 'draft-a',
            idempotencyId: 'reset-a',
          ),
          throwsA(isA<ProtocolException>()),
        );
      }
    },
  );

  test(
    'pending install reset validates bounded canonical identities',
    () async {
      final gateway = GrpcFeatureStudioGateway(
        client: _RecordingFeatureAuthoringClient(),
      );

      await expectLater(
        gateway.resetPendingInstall(draftId: '', idempotencyId: 'reset-a'),
        throwsArgumentError,
      );
      await expectLater(
        gateway.resetPendingInstall(
          draftId: 'draft-a',
          idempotencyId: 'reset\u0000a',
        ),
        throwsArgumentError,
      );
    },
  );

  test(
    'load validates but does not invent exact evidence from a summary',
    () async {
      final summary = _wireVerificationSummary()
        ..sourceReference = _sourceReference;
      final summarized = _validWireDraft()..verification = summary;
      final client = _RecordingFeatureAuthoringClient()
        ..getReply = wire.FeatureDraftReply(draft: summarized);
      final gateway = GrpcFeatureStudioGateway(client: client);

      final draft = await gateway.loadDraft('draft-a');

      expect(draft.revision, Int64(4));
      expect(draft.verification, isNull);
    },
  );

  test(
    'load round trips reserved recovery with exact retry authority and zero artifacts',
    () async {
      final serialized = _wireRecoveryReply(installed: false);
      serialized.recovery.verification.artifacts.clear();
      final client = _RecordingFeatureAuthoringClient()
        ..getReply = wire.FeatureDraftReply.fromBuffer(
          serialized.writeToBuffer(),
        );
      final gateway = GrpcFeatureStudioGateway(client: client);

      expect(client.getReply.recovery.hasInstalled(), isFalse);
      expect(client.getReply.recovery.hasRollbackAvailable(), isFalse);
      expect(client.getReply.recovery.hasPaused(), isFalse);

      final draft = await gateway.loadDraft('draft-a');
      final recovery = draft.installationRecovery!;

      expect(recovery.installed, isFalse);
      expect(recovery.installationId, 'installation-a');
      expect(recovery.decisionId, 'decision-a');
      expect(recovery.idempotencyId, 'install-a');
      expect(recovery.verification.artifacts, isEmpty);
      expect(recovery.version.digest, _releaseDigest);
      expect(recovery.version.sourceReference, _sourceReference);
      expect(recovery.version.source?.files.last.content, contains('Feature'));
      expect(recovery.grants.single.capabilityId, 'capability.read');
      expect(recovery.subscriptions, ['manual']);
      expect(recovery.previousVersion, isNull);
      expect(recovery.rollbackAvailable, isFalse);
      expect(recovery.paused, isFalse);
      expect(client.sourceRequest, isNull);
    },
  );

  test(
    'load round trips installed recovery and hydrates its previous Version source',
    () async {
      final serialized = _wireRecoveryReply(
        installed: true,
        includePrevious: true,
      );
      final client = _RecordingFeatureAuthoringClient()
        ..getReply = wire.FeatureDraftReply.fromBuffer(
          serialized.writeToBuffer(),
        )
        ..sourceReply = wire.FeatureReleaseSourceReply.fromBuffer(
          _previousSourceReply().writeToBuffer(),
        );
      final gateway = GrpcFeatureStudioGateway(client: client);

      final draft = await gateway.loadDraft('draft-a');
      final recovery = draft.installationRecovery!;

      expect(draft.status, FeatureStudioDraftStatus.installed);
      expect(draft.installationId, 'installation-a');
      expect(recovery.installed, isTrue);
      expect(recovery.decisionId, isNull);
      expect(recovery.idempotencyId, isNull);
      expect(recovery.rollbackAvailable, isTrue);
      expect(recovery.verification.scenarios.single.scenarioId, 'brief');
      expect(recovery.verification.artifacts.single.digest, _artifactDigest);
      expect(recovery.previousVersion?.digest, _previousDigest);
      expect(
        recovery.previousVersion?.source?.files.last.content,
        contains('PreviousFeature'),
      );
      expect(client.sourceRequest?.featureId, 'draft-a');
      expect(client.sourceRequest?.installationId, 'installation-a');
      expect(client.sourceRequest?.releaseDigest, _previousDigest);
      expect(client.sourceRequest?.sourceReference, _previousSourceReference);
    },
  );

  test(
    'load preserves an installed historical Draft and hydrates the active recovery Version',
    () async {
      final serialized = _wireRecoveryReply(installed: true);
      serialized.draft
        ..verification = (_wireVerificationSummary()
          ..releaseDigest = _previousDigest
          ..sourceReference = _previousSourceReference)
        ..source = _wireRelease(
          digest: _previousDigest,
          sourceReference: _previousSourceReference,
          sourceContent: 'public sealed class HistoricalFeature {}',
        ).source;
      final client = _RecordingFeatureAuthoringClient()
        ..getReply = wire.FeatureDraftReply.fromBuffer(
          serialized.writeToBuffer(),
        )
        ..sourceReply = wire.FeatureReleaseSourceReply(
          featureId: 'draft-a',
          installationId: 'installation-a',
          releaseDigest: _releaseDigest,
          sourceReference: _sourceReference,
          source: _wireRelease().source,
        );
      final gateway = GrpcFeatureStudioGateway(client: client);

      final draft = await gateway.loadDraft('draft-a');
      final recovery = draft.installationRecovery!;

      expect(draft.verification?.releaseDigest, _previousDigest);
      expect(draft.verification?.sourceReference, _previousSourceReference);
      expect(draft.verification?.scenarios, isEmpty);
      expect(draft.source.files.last.content, contains('HistoricalFeature'));
      expect(recovery.verification.releaseDigest, _releaseDigest);
      expect(recovery.verification.sourceReference, _sourceReference);
      expect(recovery.version.digest, _releaseDigest);
      expect(recovery.version.source?.files.last.content, contains('Feature'));
      expect(client.sourceRequest?.featureId, 'draft-a');
      expect(client.sourceRequest?.installationId, 'installation-a');
      expect(client.sourceRequest?.releaseDigest, _releaseDigest);
      expect(client.sourceRequest?.sourceReference, _sourceReference);
    },
  );

  test('load requires exact reserved Draft verification coordinates', () async {
    final client = _RecordingFeatureAuthoringClient();
    final gateway = GrpcFeatureStudioGateway(client: client);

    for (final tamper in <void Function(wire.FeatureDraftReply)>[
      (reply) => reply.draft.verification.releaseDigest = _previousDigest,
      (reply) => reply.draft.verification.clearSourceReference(),
    ]) {
      final serialized = _wireRecoveryReply(installed: false);
      tamper(serialized);
      client.getReply = wire.FeatureDraftReply.fromBuffer(
        serialized.writeToBuffer(),
      );
      await expectLater(
        gateway.loadDraft('draft-a'),
        throwsA(isA<ProtocolException>()),
      );
    }
  });

  test(
    'load rejects missing or mismatched recovery state and installation identity',
    () async {
      final installedWithoutRecovery = _wireRecoveryReply(installed: true)
        ..clearRecovery();
      final reservedOnInstalled = _wireRecoveryReply(installed: true)
        ..recovery.clearInstalled();
      final installedOnMutable = _wireRecoveryReply(installed: false)
        ..recovery.installed = true
        ..recovery.clearDecisionId()
        ..recovery.clearIdempotencyId();
      final mismatchedInstallation = _wireRecoveryReply(installed: true)
        ..recovery.installationId = 'installation-b';
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);

      for (final invalid in [
        installedWithoutRecovery,
        reservedOnInstalled,
        installedOnMutable,
        mismatchedInstallation,
      ]) {
        client.getReply = wire.FeatureDraftReply.fromBuffer(
          invalid.writeToBuffer(),
        );
        await expectLater(
          gateway.loadDraft('draft-a'),
          throwsA(isA<ProtocolException>()),
        );
      }
    },
  );

  test(
    'load rejects tampered recovery verification Version and authority',
    () async {
      final tamperers = <void Function(wire.FeatureDraftReply)>[
        (reply) => reply.recovery.verification.releaseDigest = _previousDigest,
        (reply) => reply.recovery.verification.sourceReference =
            _previousSourceReference,
        (reply) => reply.recovery.release.digest = _previousDigest,
        (reply) => reply.recovery.release.source = _wireRelease(
          sourceContent: 'public sealed class TamperedFeature {}',
        ).source,
        (reply) => reply.recovery.release.requestedCapabilityIds[0] =
            'capability.write',
        (reply) => reply.recovery.grants[0].capabilityId = 'capability.write',
        (reply) =>
            reply.recovery.grants[0].constraintsJson = '{"unsupported":true}',
        (reply) => reply.recovery.subscriptions[0] = ' manual',
        (reply) =>
            reply.recovery.verification.artifacts[0].digest = 'not-a-digest',
      ];
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);

      for (final tamper in tamperers) {
        final invalid = _wireRecoveryReply(installed: false);
        tamper(invalid);
        client.getReply = wire.FeatureDraftReply.fromBuffer(
          invalid.writeToBuffer(),
        );
        await expectLater(
          gateway.loadDraft('draft-a'),
          throwsA(isA<ProtocolException>()),
        );
      }
    },
  );

  test(
    'load rejects contradictory recovery retry pause and rollback fields',
    () async {
      final reservedWithoutDecision = _wireRecoveryReply(installed: false)
        ..recovery.clearDecisionId();
      final reservedWithoutIdempotency = _wireRecoveryReply(installed: false)
        ..recovery.clearIdempotencyId();
      final installedWithRetry = _wireRecoveryReply(installed: true)
        ..recovery.decisionId = 'unexpected-decision';
      final reservedWithRollback = _wireRecoveryReply(installed: false)
        ..recovery.rollbackAvailable = true;
      final installedRollbackWithoutPrevious = _wireRecoveryReply(
        installed: true,
      )..recovery.rollbackAvailable = true;
      final installedPreviousWithoutRollback = _wireRecoveryReply(
        installed: true,
        includePrevious: true,
      )..recovery.clearRollbackAvailable();
      final pausedWithoutReason = _wireRecoveryReply(installed: true)
        ..recovery.paused = true;
      final reasonWithoutPause = _wireRecoveryReply(installed: true)
        ..recovery.pauseReason = 'Access revoked';
      final pausedWithPrevious =
          _wireRecoveryReply(installed: true, includePrevious: true)
            ..recovery.paused = true
            ..recovery.pauseReason = 'Access revoked';
      final client = _RecordingFeatureAuthoringClient()
        ..sourceReply = _previousSourceReply();
      final gateway = GrpcFeatureStudioGateway(client: client);

      for (final invalid in [
        reservedWithoutDecision,
        reservedWithoutIdempotency,
        installedWithRetry,
        reservedWithRollback,
        installedRollbackWithoutPrevious,
        installedPreviousWithoutRollback,
        pausedWithoutReason,
        reasonWithoutPause,
        pausedWithPrevious,
      ]) {
        client.getReply = wire.FeatureDraftReply.fromBuffer(
          invalid.writeToBuffer(),
        );
        await expectLater(
          gateway.loadDraft('draft-a'),
          throwsA(isA<ProtocolException>()),
        );
      }
    },
  );

  test(
    'load rejects tampered previous Version source coordinates and content',
    () async {
      final cases =
          <
            ({
              wire.FeatureDraftReply draft,
              wire.FeatureReleaseSourceReply source,
            })
          >[
            (
              draft: _wireRecoveryReply(installed: true, includePrevious: true),
              source: _previousSourceReply()..featureId = 'draft-b',
            ),
            (
              draft: _wireRecoveryReply(installed: true, includePrevious: true),
              source: _previousSourceReply()..installationId = 'installation-b',
            ),
            (
              draft: _wireRecoveryReply(installed: true, includePrevious: true),
              source: _previousSourceReply()..releaseDigest = _releaseDigest,
            ),
            (
              draft: _wireRecoveryReply(installed: true, includePrevious: true),
              source: _previousSourceReply()
                ..sourceReference = _sourceReference,
            ),
            (
              draft: _wireRecoveryReply(installed: true, includePrevious: true),
              source: _previousSourceReply()..clearSource(),
            ),
            (() {
              final draft = _wireRecoveryReply(
                installed: true,
                includePrevious: true,
              );
              draft.recovery.previousRelease.source = _wireRelease(
                digest: _previousDigest,
                sourceReference: _previousSourceReference,
                sourceContent: 'public sealed class TamperedPreviousFeature {}',
              ).source;
              return (draft: draft, source: _previousSourceReply());
            })(),
          ];

      for (final invalid in cases) {
        final client = _RecordingFeatureAuthoringClient()
          ..getReply = wire.FeatureDraftReply.fromBuffer(
            invalid.draft.writeToBuffer(),
          )
          ..sourceReply = wire.FeatureReleaseSourceReply.fromBuffer(
            invalid.source.writeToBuffer(),
          );
        final gateway = GrpcFeatureStudioGateway(client: client);

        await expectLater(
          gateway.loadDraft('draft-a'),
          throwsA(isA<ProtocolException>()),
        );
      }
    },
  );

  test('load preserves retryable previous Version source failures', () async {
    final client = _RecordingFeatureAuthoringClient()
      ..getReply = wire.FeatureDraftReply.fromBuffer(
        _wireRecoveryReply(
          installed: true,
          includePrevious: true,
        ).writeToBuffer(),
      )
      ..sourceError = const TransportException(
        TransportErrorCode.unavailable,
        'Previous Version source is temporarily unavailable.',
      );
    final gateway = GrpcFeatureStudioGateway(client: client);

    await expectLater(
      gateway.loadDraft('draft-a'),
      throwsA(
        isA<TransportException>().having(
          (error) => error.code,
          'code',
          TransportErrorCode.unavailable,
        ),
      ),
    );
  });

  test(
    'revision commands use exact oneofs, Int64 revisions, and identities',
    () async {
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);
      final behavior = _behaviorModel();
      final source = _sourceModel();
      final suggestion = FeatureStudioSuggestion(
        patchId: 'patch-a',
        draftId: 'draft-a',
        baseRevision: Int64(4),
        summary: 'Clarify the outcome',
        replacementBehavior: behavior,
        replacementSource: source,
      );

      await gateway.reviseBehavior(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'behavior-a',
        behavior: behavior,
        expectedSource: source,
      );
      await gateway.reviseSource(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'source-a',
        source: source,
        expectedBehavior: behavior,
      );
      await gateway.acceptSuggestedChange(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'accept-a',
        suggestion: suggestion,
      );
      await gateway.rejectSuggestedChange(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'reject-a',
        suggestion: suggestion,
        expectedBehavior: behavior,
        expectedSource: source,
        expectedVerification: null,
      );

      final behaviorRequest = client.reviseRequests[0];
      final sourceRequest = client.reviseRequests[1];
      final acceptRequest = client.reviseRequests[2];
      final rejectRequest = client.reviseRequests[3];
      expect(
        behaviorRequest.whichCommand(),
        wire.ReviseFeatureDraftRequest_Command.reviseBehavior,
      );
      expect(
        sourceRequest.whichCommand(),
        wire.ReviseFeatureDraftRequest_Command.reviseSource,
      );
      expect(
        acceptRequest.whichCommand(),
        wire.ReviseFeatureDraftRequest_Command.acceptSuggestedChange,
      );
      expect(
        rejectRequest.whichCommand(),
        wire.ReviseFeatureDraftRequest_Command.rejectSuggestedChange,
      );
      expect(behaviorRequest.expectedRevision, Int64(4));
      expect(behaviorRequest.idempotencyId, 'behavior-a');
      expect(sourceRequest.reviseSource.source.files, hasLength(3));
      final echoedPatch = acceptRequest.acceptSuggestedChange.patch;
      expect(echoedPatch.patchId, 'patch-a');
      expect(echoedPatch.baseRevision, Int64(4));
      expect(echoedPatch.replacementBehavior.scenarios, hasLength(1));
      expect(echoedPatch.replacementSource.files, hasLength(3));
      expect(rejectRequest.idempotencyId, 'reject-a');
      expect(rejectRequest.rejectSuggestedChange.patchId, 'patch-a');
      expect(rejectRequest.rejectSuggestedChange.baseRevision, Int64(4));
    },
  );

  test(
    'every revision reply rejects installation recovery envelopes',
    () async {
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);
      final behavior = _behaviorModel();
      final source = _sourceModel();
      final suggestion = FeatureStudioSuggestion(
        patchId: 'patch-a',
        draftId: 'draft-a',
        baseRevision: Int64(4),
        summary: 'Clarify the outcome',
        replacementBehavior: behavior,
        replacementSource: source,
      );
      wire.FeatureDraftReply reply(Int64 revision) => wire.FeatureDraftReply(
        draft: _validWireDraft(revision: revision),
        recovery: _wireRecoveryReply(installed: false).recovery,
      );
      final cases = <(String, Int64, Future<FeatureStudioDraft> Function())>[
        (
          'revise behavior',
          Int64(5),
          () => gateway.reviseBehavior(
            draftId: 'draft-a',
            expectedRevision: Int64(4),
            idempotencyId: 'behavior-a',
            behavior: behavior,
            expectedSource: source,
          ),
        ),
        (
          'revise source',
          Int64(5),
          () => gateway.reviseSource(
            draftId: 'draft-a',
            expectedRevision: Int64(4),
            idempotencyId: 'source-a',
            source: source,
            expectedBehavior: behavior,
          ),
        ),
        (
          'accept suggestion',
          Int64(5),
          () => gateway.acceptSuggestedChange(
            draftId: 'draft-a',
            expectedRevision: Int64(4),
            idempotencyId: 'accept-a',
            suggestion: suggestion,
          ),
        ),
        (
          'reject suggestion',
          Int64(4),
          () => gateway.rejectSuggestedChange(
            draftId: 'draft-a',
            expectedRevision: Int64(4),
            idempotencyId: 'reject-a',
            suggestion: suggestion,
            expectedBehavior: behavior,
            expectedSource: source,
            expectedVerification: null,
          ),
        ),
      ];

      for (final (name, revision, invoke) in cases) {
        client.reviseReply = reply(revision);
        client.rejectReply = reply(revision);

        await expectLater(
          invoke(),
          throwsA(isA<ProtocolException>()),
          reason: name,
        );
      }
    },
  );

  test(
    'controller and strict gateway serialize two dirty aggregates against confirmed state',
    () async {
      final client = _ApplyingFeatureAuthoringClient(_validWireDraft());
      final controller = FeatureStudioController(
        draftId: 'draft-a',
        gateway: GrpcFeatureStudioGateway(client: client),
        idFactory: _SequentialIds().call,
      );
      await controller.load();
      final behavior = FeatureStudioBehavior(
        scenarios: const [
          FeatureStudioScenario(
            scenarioId: 'brief',
            name: 'Changed brief',
            given: 'A company name',
            when: 'The Feature runs',
            then: 'A sourced brief is returned',
          ),
        ],
      );
      final source = FeatureStudioSource(
        implementationProjectPath: 'Feature/Feature.csproj',
        scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
        files: const [
          FeatureStudioSourceFile(
            path: 'Feature/Feature.csproj',
            content: '<Project Sdk="Microsoft.NET.Sdk" />',
          ),
          FeatureStudioSourceFile(
            path: 'Feature.Tests/Feature.Tests.csproj',
            content: '<Project Sdk="Microsoft.NET.Sdk" />',
          ),
          FeatureStudioSourceFile(
            path: 'Feature/Feature.cs',
            content: 'public sealed class ChangedFeature {}',
          ),
        ],
      );

      controller.reviseSource(source);
      controller.reviseBehavior(behavior);
      await controller.saveNow();

      expect(client.reviseRequests, hasLength(2));
      expect(client.reviseRequests.map((request) => request.whichCommand()), [
        wire.ReviseFeatureDraftRequest_Command.reviseBehavior,
        wire.ReviseFeatureDraftRequest_Command.reviseSource,
      ]);
      expect(controller.confirmedDraft?.revision, Int64(6));
      expect(controller.savePhase, FeatureStudioSavePhase.saved);
      expect(controller.isDirty, isFalse);
    },
  );

  test(
    'suggest requires a complete replacement at the exact base revision',
    () async {
      final client = _RecordingFeatureAuthoringClient()
        ..suggestReply = wire.FeatureDraftPatchReply(patch: _validWirePatch());
      final gateway = GrpcFeatureStudioGateway(client: client);

      final suggestion = await gateway.suggestChange(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        guidance: 'Clarify the expected outcome',
        suggestionId: 'suggestion-a',
      );

      expect(client.suggestRequest?.expectedRevision, Int64(4));
      expect(client.suggestRequest?.suggestionId, 'suggestion-a');
      expect(suggestion.patchId, 'patch-a');
      expect(suggestion.baseRevision, Int64(4));
      expect(suggestion.replacementBehavior.scenarios, hasLength(1));
      expect(suggestion.replacementSource.files, hasLength(3));
    },
  );

  test(
    'passing verification maps ordered evidence and the current Version',
    () async {
      final verifiedDraft = _validWireDraft(revision: Int64(5))
        ..updatedAtUnixMs = Int64(1_752_537_720_000)
        ..clearSource()
        ..verification = _wireVerificationSummary(
          verifiedAtUnixMs: Int64(1_752_537_720_000),
        );
      final release = _wireRelease()..clearSource();
      final evidence =
          _wireVerification(verifiedAtUnixMs: Int64(1_752_537_720_000))
            ..scenarios.single.durationMilliseconds = Int64.ZERO
            ..artifacts.single.sizeBytes = Int64.ZERO;
      evidence
        ..clearFailed()
        ..clearSkipped();
      evidence.scenarios.single.clearDurationMilliseconds();
      evidence.artifacts.single.clearSizeBytes();
      final serialized = wire.FeatureReleaseReviewReply(
        draft: verifiedDraft,
        release: release,
        verification: evidence,
      );
      final client = _RecordingFeatureAuthoringClient()
        ..verifyReply = wire.FeatureReleaseReviewReply.fromBuffer(
          serialized.writeToBuffer(),
        );
      final gateway = GrpcFeatureStudioGateway(client: client);

      final result = await gateway.verifyDraft(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'verify-a',
        expectedBehavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      );

      expect(client.verifyRequest?.expectedRevision, Int64(4));
      expect(client.verifyRequest?.idempotencyId, 'verify-a');
      expect(result.draft.revision, Int64(5));
      expect(result.verification.isPassing, isTrue);
      expect(
        result.verification.scenarios.map((scenario) => scenario.scenarioId),
        ['brief'],
      );
      expect(client.verifyReply.verification.hasFailed(), isFalse);
      expect(client.verifyReply.verification.hasSkipped(), isFalse);
      expect(
        client.verifyReply.verification.scenarios.single
            .hasDurationMilliseconds(),
        isFalse,
      );
      expect(
        client.verifyReply.verification.artifacts.single.hasSizeBytes(),
        isFalse,
      );
      expect(result.verification.scenarios.single.durationMilliseconds, 0);
      expect(result.verification.artifacts.single.sizeBytes, 0);
      expect(result.verification.artifacts.single.mediaType, 'text/plain');
      expect(result.verification.sourceReference, _sourceReference);
      expect(result.version?.digest, _releaseDigest);
      expect(result.version?.requestedCapabilityIds, ['capability.read']);
      expect(result.version?.source?.files.last.content, contains('Feature'));
    },
  );

  test('failed verification remains inspectable and has no Version', () async {
    final evidence = wire.FeatureVerification(
      total: 1,
      passed: 0,
      failed: 1,
      skipped: 0,
      verifiedAtUnixMs: Int64(1_752_537_720_000),
      sourceReference: _sourceReference,
      scenarios: [
        wire.FeatureVerificationScenario(
          scenarioId: 'citation',
          name: 'Include citations',
          outcome: wire.FeatureScenarioOutcome.FEATURE_SCENARIO_OUTCOME_FAILED,
          safeFailure: 'Expected a cited source.',
          durationMilliseconds: Int64.ZERO,
        ),
      ],
      artifacts: [
        wire.FeatureVerificationArtifact(
          name: 'test-results.txt',
          mediaType: 'text/plain',
          sizeBytes: Int64.ZERO,
          digest: _artifactDigest,
        ),
      ],
    );
    final serialized = wire.FeatureReleaseReviewReply(
      draft: _validWireDraft(),
      verification: evidence,
    );
    serialized.verification
      ..clearPassed()
      ..clearSkipped();
    serialized.verification.scenarios.single.clearDurationMilliseconds();
    serialized.verification.artifacts.single.clearSizeBytes();
    final client = _RecordingFeatureAuthoringClient()
      ..verifyReply = wire.FeatureReleaseReviewReply.fromBuffer(
        serialized.writeToBuffer(),
      );
    final gateway = GrpcFeatureStudioGateway(client: client);

    final result = await gateway.verifyDraft(
      draftId: 'draft-a',
      expectedRevision: Int64(4),
      idempotencyId: 'verify-failed-a',
      expectedBehavior: _behaviorModel(),
      expectedSource: _sourceModel(),
    );

    expect(result.draft.revision, Int64(4));
    expect(result.version, isNull);
    expect(result.verification.isPassing, isFalse);
    expect(client.verifyReply.verification.hasPassed(), isFalse);
    expect(client.verifyReply.verification.hasSkipped(), isFalse);
    expect(
      client.verifyReply.verification.scenarios.single
          .hasDurationMilliseconds(),
      isFalse,
    );
    expect(
      client.verifyReply.verification.artifacts.single.hasSizeBytes(),
      isFalse,
    );
    expect(result.verification.failed, 1);
    expect(result.verification.scenarios.single.durationMilliseconds, 0);
    expect(result.verification.artifacts.single.sizeBytes, 0);
    expect(result.draft.verification, isNull);
    expect(
      result.verification.verifiedAt.isAfter(result.draft.updatedAt),
      isTrue,
    );
    expect(
      result.verification.scenarios.last.safeFailure,
      'Expected a cited source.',
    );

    client.verifyReply.verification.verifiedAtUnixMs = Int64(1_752_537_599_999);
    await expectLater(
      gateway.verifyDraft(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'verify-failed-before-draft',
        expectedBehavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      ),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('verification evidence enforces the public response bounds', () async {
    final client = _RecordingFeatureAuthoringClient();
    final gateway = GrpcFeatureStudioGateway(client: client);
    final oversizedDuration = _wireVerification()
      ..scenarios.single.durationMilliseconds = Int64(70_001);
    final oversizedArtifact = _wireVerification()
      ..artifacts.single.sizeBytes = Int64(1_048_577);
    final duplicateArtifacts = _wireVerification()
      ..artifacts.add(_wireVerification().artifacts.single);

    for (final evidence in [
      oversizedDuration,
      oversizedArtifact,
      duplicateArtifacts,
    ]) {
      final draft = _validWireDraft(revision: Int64(5))
        ..verification = evidence;
      client.verifyReply = wire.FeatureReleaseReviewReply(
        draft: draft,
        release: _wireRelease(),
        verification: evidence,
      );

      await expectLater(
        gateway.verifyDraft(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'verify-bounds',
          expectedBehavior: _behaviorModel(),
          expectedSource: _sourceModel(),
        ),
        throwsA(isA<ProtocolException>()),
      );
    }
  });

  test(
    'access review binds the exact requested target and installed release',
    () async {
      final reviewedDraft = _validWireDraft(revision: Int64(5))
        ..clearSource()
        ..verification = _wireVerificationSummary();
      final release = _wireRelease()..clearSource();
      final previousRelease = _wireRelease(
        digest: _previousDigest,
        sourceReference: _previousSourceReference,
      )..clearSource();
      final client = _RecordingFeatureAuthoringClient()
        ..accessReviewReply = wire.FeatureAccessReviewReply(
          draft: reviewedDraft,
          release: release,
          installationId: 'installation-a',
          grants: [
            wire.FeatureGrant(
              capabilityId: 'capability.read',
              capabilityVersion: 1,
              provider: 'provider-a',
              connectionId: 'connection-a',
              constraintsJson: '{"allowedToolIds":["capability.read"]}',
            ),
          ],
          subscriptions: ['manual'],
          previousRelease: previousRelease,
        )
        ..sourceReply = wire.FeatureReleaseSourceReply(
          featureId: 'draft-a',
          installationId: 'installation-a',
          releaseDigest: _previousDigest,
          sourceReference: _previousSourceReference,
          source: _wireRelease(
            digest: _previousDigest,
            sourceReference: _previousSourceReference,
            sourceContent: 'public sealed class PreviousFeature {}',
          ).source,
        );
      final gateway = GrpcFeatureStudioGateway(client: client);

      final review = await gateway.reviewAccess(
        draftId: 'draft-a',
        expectedRevision: Int64(5),
        expectedDraft: _draftModel(
          revision: Int64(5),
          verification: _verificationModel(),
        ),
        installationId: 'installation-a',
        version: _versionModel(),
        expectedVerification: _verificationModel(),
        expectedBehavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      );

      final request = client.accessReviewRequest!;
      expect(request.installationId, 'installation-a');
      expect(request.releaseDigest, _releaseDigest);
      expect(request.subscriptions, isEmpty);
      expect(request.grants, isEmpty);
      expect(review.installationId, 'installation-a');
      expect(review.grants.single.provider, 'provider-a');
      expect(review.grants.single.connectionId, 'connection-a');
      expect(review.grants.single.constraintSummary, 'Only capability.read');
      expect(review.draft.verification?.scenarios, hasLength(1));
      expect(review.subscriptions, ['manual']);
      expect(client.sourceRequest?.featureId, 'draft-a');
      expect(client.sourceRequest?.installationId, 'installation-a');
      expect(client.sourceRequest?.releaseDigest, _previousDigest);
      expect(client.sourceRequest?.sourceReference, _previousSourceReference);
      expect(review.previousVersion?.digest, _previousDigest);
      expect(review.previousVersion?.sourceReference, _previousSourceReference);
      expect(
        review.previousVersion?.source?.files.last.content,
        'public sealed class PreviousFeature {}',
      );
    },
  );

  test(
    'access review rejects immutable metadata substitution with a summary verification',
    () async {
      final reviewedDraft = _validWireDraft(revision: Int64(5))
        ..clearSource()
        ..verification = _wireVerificationSummary();
      final changedGoal = reviewedDraft.deepCopy()..goal = 'A substituted goal';
      final changedOrigin = reviewedDraft.deepCopy()
        ..originatingRequest.text = 'A substituted request';
      final changedCreatedAt = reviewedDraft.deepCopy()
        ..createdAtUnixMs = Int64(1_752_537_600_001);
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);

      for (final tampered in [changedGoal, changedOrigin, changedCreatedAt]) {
        client.accessReviewReply = wire.FeatureAccessReviewReply(
          draft: tampered,
          release: _wireRelease()..clearSource(),
          installationId: 'installation-a',
          grants: [
            wire.FeatureGrant(
              capabilityId: 'capability.read',
              capabilityVersion: 1,
              constraintsJson: '{"allowedToolIds":["capability.read"]}',
            ),
          ],
          subscriptions: ['manual'],
        );

        await expectLater(
          gateway.reviewAccess(
            draftId: 'draft-a',
            expectedRevision: Int64(5),
            expectedDraft: _draftModel(
              revision: Int64(5),
              verification: _verificationModel(),
            ),
            installationId: 'installation-a',
            version: _versionModel(),
            expectedVerification: _verificationModel(),
            expectedBehavior: _behaviorModel(),
            expectedSource: _sourceModel(),
          ),
          throwsA(isA<ProtocolException>()),
        );
      }
    },
  );

  test(
    'access review discloses every supported constraint without collapsing payload rules',
    () async {
      const constraints =
          '{"payload":{"messageLimit":[25],"mailbox":["primary","archive"],"filter":{"unread":[true]}},"allowedToolIds":["capability.read"]}';
      final reviewedDraft = _validWireDraft(revision: Int64(5))
        ..verification = _wireVerification();
      final client = _RecordingFeatureAuthoringClient()
        ..accessReviewReply = wire.FeatureAccessReviewReply(
          draft: reviewedDraft,
          release: _wireRelease(),
          installationId: 'installation-a',
          grants: [
            wire.FeatureGrant(
              capabilityId: 'capability.read',
              capabilityVersion: 7,
              constraintsJson: constraints,
            ),
          ],
          subscriptions: ['manual'],
        );
      final gateway = GrpcFeatureStudioGateway(client: client);

      final review = await gateway.reviewAccess(
        draftId: 'draft-a',
        expectedRevision: Int64(5),
        expectedDraft: _draftModel(
          revision: Int64(5),
          verification: _verificationModel(),
        ),
        installationId: 'installation-a',
        version: _versionModel(),
        expectedVerification: _verificationModel(),
        expectedBehavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      );

      expect(review.grants.single.constraintsJson, constraints);
      expect(
        review.grants.single.constraintSummary,
        'Only capability.read; input filter.unread allows true; input mailbox allows "archive" or "primary"; input messageLimit allows 25',
      );
    },
  );

  test(
    'access review preserves and discloses external-effect tool authority',
    () async {
      const constraints =
          '{"allowedToolIds":["capability.send","GmailTools.Send"]}';
      final reviewedDraft = _validWireDraft(revision: Int64(5))
        ..verification = _wireVerification();
      final release = _wireRelease()
        ..requestedCapabilityIds.clear()
        ..requestedCapabilityIds.add('capability.send');
      final client = _RecordingFeatureAuthoringClient()
        ..accessReviewReply = wire.FeatureAccessReviewReply(
          draft: reviewedDraft,
          release: release,
          installationId: 'installation-a',
          grants: [
            wire.FeatureGrant(
              capabilityId: 'capability.send',
              capabilityVersion: 3,
              constraintsJson: constraints,
            ),
          ],
          subscriptions: ['manual'],
        );
      final gateway = GrpcFeatureStudioGateway(client: client);

      final review = await gateway.reviewAccess(
        draftId: 'draft-a',
        expectedRevision: Int64(5),
        expectedDraft: _draftModel(
          revision: Int64(5),
          verification: _verificationModel(),
        ),
        installationId: 'installation-a',
        version: _versionModel(
          requestedCapabilityIds: const ['capability.send'],
        ),
        expectedVerification: _verificationModel(),
        expectedBehavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      );

      expect(review.grants.single.constraintsJson, constraints);
      expect(
        review.grants.single.constraintSummary,
        'Allowed tools: GmailTools.Send, capability.send',
      );
    },
  );

  test(
    'access review rejects mismatched previous-source coordinates',
    () async {
      final reviewedDraft = _validWireDraft(revision: Int64(5))
        ..clearSource()
        ..verification = _wireVerification();
      final release = _wireRelease()..clearSource();
      final previousRelease = _wireRelease(
        digest: _previousDigest,
        sourceReference: _previousSourceReference,
      )..clearSource();
      final client = _RecordingFeatureAuthoringClient()
        ..accessReviewReply = wire.FeatureAccessReviewReply(
          draft: reviewedDraft,
          release: release,
          installationId: 'installation-a',
          grants: [
            wire.FeatureGrant(
              capabilityId: 'capability.read',
              capabilityVersion: 1,
              constraintsJson: '{"allowedToolIds":["capability.read"]}',
            ),
          ],
          subscriptions: ['manual'],
          previousRelease: previousRelease,
        )
        ..sourceReply = wire.FeatureReleaseSourceReply(
          featureId: 'another-draft',
          installationId: 'installation-a',
          releaseDigest: _previousDigest,
          sourceReference: _previousSourceReference,
          source: _wireRelease().source,
        );
      final gateway = GrpcFeatureStudioGateway(client: client);

      await expectLater(
        gateway.reviewAccess(
          draftId: 'draft-a',
          expectedRevision: Int64(5),
          expectedDraft: _draftModel(
            revision: Int64(5),
            verification: _verificationModel(),
          ),
          installationId: 'installation-a',
          version: _versionModel(),
          expectedVerification: _verificationModel(),
          expectedBehavior: _behaviorModel(),
          expectedSource: _sourceModel(),
        ),
        throwsA(isA<ProtocolException>()),
      );
    },
  );

  test('access review rejects undisclosed root constraint keys', () async {
    final reviewedDraft = _validWireDraft(revision: Int64(5))
      ..verification = _wireVerification();
    final client = _RecordingFeatureAuthoringClient()
      ..accessReviewReply = wire.FeatureAccessReviewReply(
        draft: reviewedDraft,
        release: _wireRelease(),
        installationId: 'installation-a',
        grants: [
          wire.FeatureGrant(
            capabilityId: 'capability.read',
            capabilityVersion: 1,
            constraintsJson:
                '{"allowedToolIds":["capability.read"],"mailbox":["primary"]}',
          ),
        ],
        subscriptions: ['manual'],
      );
    final gateway = GrpcFeatureStudioGateway(client: client);

    await expectLater(
      gateway.reviewAccess(
        draftId: 'draft-a',
        expectedRevision: Int64(5),
        expectedDraft: _draftModel(
          revision: Int64(5),
          verification: _verificationModel(),
        ),
        installationId: 'installation-a',
        version: _versionModel(),
        expectedVerification: _verificationModel(),
        expectedBehavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      ),
      throwsA(isA<ProtocolException>()),
    );
  });

  test(
    'access review enforces the bounded credential-safe constraint policy',
    () async {
      Object? excessiveDepth = true;
      for (var index = 0; index < 66; index++) {
        excessiveDepth = <String, Object?>{'level$index': excessiveDepth};
      }
      final credentialPayloads = [
        {'clientSecret': 'secret'},
        {'api_key': 'secret'},
        {'password': 'secret'},
        {'privateKey': 'secret'},
        {'token': 'secret'},
      ];
      final malformedConstraints = <String>[
        for (final payload in credentialPayloads)
          jsonEncode({
            'allowedToolIds': ['capability.read'],
            'payload': payload,
          }),
        jsonEncode({
          'allowedToolIds': ['capability.read'],
          'payload': {'text': 'x' * 65536},
        }),
        jsonEncode({
          'allowedToolIds': ['capability.read'],
          'payload': {'choice': List<int>.generate(257, (index) => index)},
        }),
        jsonEncode({
          'allowedToolIds': ['capability.read'],
          'payload': <String, Object?>{
            for (var index = 0; index < 129; index++) 'field$index': index,
          },
        }),
        jsonEncode({
          'allowedToolIds': ['capability.read'],
          'payload': excessiveDepth,
        }),
        jsonEncode({
          'allowedToolIds': [
            'capability.read',
            for (var index = 0; index < 256; index++) 'tool.$index',
          ],
        }),
        '{"allowedToolIds":["capability.read"],"payload":{" bad":true}}',
      ];
      final reviewedDraft = _validWireDraft(revision: Int64(5))
        ..verification = _wireVerification();
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);

      for (final constraintsJson in malformedConstraints) {
        client.accessReviewReply = wire.FeatureAccessReviewReply(
          draft: reviewedDraft,
          release: _wireRelease(),
          installationId: 'installation-a',
          grants: [
            wire.FeatureGrant(
              capabilityId: 'capability.read',
              capabilityVersion: 1,
              constraintsJson: constraintsJson,
            ),
          ],
          subscriptions: ['manual'],
        );
        await expectLater(
          gateway.reviewAccess(
            draftId: 'draft-a',
            expectedRevision: Int64(5),
            expectedDraft: _draftModel(
              revision: Int64(5),
              verification: _verificationModel(),
            ),
            installationId: 'installation-a',
            version: _versionModel(),
            expectedVerification: _verificationModel(),
            expectedBehavior: _behaviorModel(),
            expectedSource: _sourceModel(),
          ),
          throwsA(isA<ProtocolException>()),
          reason: constraintsJson.length.toString(),
        );
      }
    },
  );

  test('install rejects a forged constraint summary before sending', () async {
    final client = _RecordingFeatureAuthoringClient();
    final gateway = GrpcFeatureStudioGateway(client: client);
    final review = FeatureStudioAccessReview(
      draft: _draftModel(
        revision: Int64(5),
        verification: _verificationModel(),
      ),
      version: _versionModel(),
      installationId: 'installation-a',
      grants: const [
        FeatureStudioGrant(
          capabilityId: 'capability.read',
          capabilityVersion: 1,
          provider: null,
          connectionId: null,
          constraintsJson: '{"allowedToolIds":["capability.read"]}',
          constraintSummary: 'Unrestricted access',
        ),
      ],
      subscriptions: const ['manual'],
      previousVersion: null,
    );

    await expectLater(
      gateway.installVersion(
        review: review,
        expectedRevision: Int64(5),
        decisionId: 'decision-a',
        idempotencyId: 'install-a',
        expectedBehavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      ),
      throwsArgumentError,
    );
    expect(client.installRequest, isNull);
  });

  test('install sends the exact reviewed authority and maps success', () async {
    final reviewedDraft = _validWireDraft(revision: Int64(5))
      ..verification = _wireVerification();
    final installedDraft = _validWireDraft(revision: Int64(6))
      ..status = wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_INSTALLED
      ..installationId = 'installation-a'
      ..clearSource()
      ..verification = _wireVerificationSummary();
    final installedRelease = _wireRelease()..clearSource();
    final grant = const FeatureStudioGrant(
      capabilityId: 'capability.read',
      capabilityVersion: 1,
      provider: 'provider-a',
      connectionId: 'connection-a',
      constraintsJson: '{"allowedToolIds":["capability.read"]}',
      constraintSummary: 'Only capability.read',
    );
    final review = FeatureStudioAccessReview(
      draft: _draftModel(
        revision: Int64(5),
        verification: _verificationModel(),
      ),
      version: _versionModel(),
      installationId: 'installation-a',
      grants: [grant],
      subscriptions: const ['manual'],
      previousVersion: null,
    );
    final serializedInstall = wire.FeatureInstallReply(
      draft: installedDraft,
      release: installedRelease,
      installationId: 'installation-a',
      activeGrants: [
        wire.FeatureGrant(
          capabilityId: grant.capabilityId,
          capabilityVersion: grant.capabilityVersion,
          provider: grant.provider,
          connectionId: grant.connectionId,
          constraintsJson: grant.constraintsJson,
        ),
      ],
      subscriptions: ['manual'],
      rollbackAvailable: false,
      paused: false,
    );
    serializedInstall
      ..clearRollbackAvailable()
      ..clearPaused();
    final client = _RecordingFeatureAuthoringClient()
      ..installReply = wire.FeatureInstallReply.fromBuffer(
        serializedInstall.writeToBuffer(),
      );
    final gateway = GrpcFeatureStudioGateway(client: client);

    final success = await gateway.installVersion(
      review: review,
      expectedRevision: Int64(5),
      decisionId: 'decision-a',
      idempotencyId: 'install-a',
      expectedBehavior: _behaviorModel(),
      expectedSource: _sourceModel(),
    );

    final request = client.installRequest!;
    expect(request.decisionId, 'decision-a');
    expect(request.idempotencyId, 'install-a');
    expect(request.grants.single.provider, 'provider-a');
    expect(request.grants.single.connectionId, 'connection-a');
    expect(request.subscriptions, ['manual']);
    expect(success.draft.status, FeatureStudioDraftStatus.installed);
    expect(success.draft.installationId, 'installation-a');
    expect(success.draft.verification?.scenarios, hasLength(1));
    expect(success.installationId, 'installation-a');
    expect(client.installReply.hasRollbackAvailable(), isFalse);
    expect(client.installReply.hasPaused(), isFalse);
    expect(success.rollbackAvailable, isFalse);
    expect(success.originalRequest.text, 'Research Acme');
    expect(review.draft.revision, reviewedDraft.revision);

    final contradictoryPause = wire.FeatureInstallReply.fromBuffer(
      serializedInstall.writeToBuffer(),
    )..pauseReason = 'Paused by policy';
    client.installReply = wire.FeatureInstallReply.fromBuffer(
      contradictoryPause.writeToBuffer(),
    );
    expect(client.installReply.hasPauseReason(), isTrue);
    await expectLater(
      gateway.installVersion(
        review: review,
        expectedRevision: Int64(5),
        decisionId: 'decision-pause-mismatch',
        idempotencyId: 'install-pause-mismatch',
        expectedBehavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      ),
      throwsA(isA<ProtocolException>()),
    );
    client.installReply = wire.FeatureInstallReply.fromBuffer(
      serializedInstall.writeToBuffer(),
    );

    final missingDraftInstallation = installedDraft.deepCopy()
      ..clearInstallationId();
    final mismatchedDraftInstallation = installedDraft.deepCopy()
      ..installationId = 'installation-other';
    for (final malformedDraft in [
      missingDraftInstallation,
      mismatchedDraftInstallation,
    ]) {
      final malformed = wire.FeatureInstallReply.fromBuffer(
        serializedInstall.writeToBuffer(),
      )..draft = malformedDraft;
      client.installReply = wire.FeatureInstallReply.fromBuffer(
        malformed.writeToBuffer(),
      );
      await expectLater(
        gateway.installVersion(
          review: review,
          expectedRevision: Int64(5),
          decisionId: 'decision-draft-installation-mismatch',
          idempotencyId: 'install-draft-installation-mismatch',
          expectedBehavior: _behaviorModel(),
          expectedSource: _sourceModel(),
        ),
        throwsA(isA<ProtocolException>()),
      );
    }
    client.installReply = wire.FeatureInstallReply.fromBuffer(
      serializedInstall.writeToBuffer(),
    );

    client.installReply.rollbackAvailable = true;
    await expectLater(
      gateway.installVersion(
        review: review,
        expectedRevision: Int64(5),
        decisionId: 'decision-first-mismatch',
        idempotencyId: 'install-first-mismatch',
        expectedBehavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      ),
      throwsA(isA<ProtocolException>()),
    );
    client.installReply.rollbackAvailable = false;
    final updateReview = FeatureStudioAccessReview(
      draft: review.draft,
      version: review.version,
      installationId: review.installationId,
      grants: review.grants,
      subscriptions: review.subscriptions,
      previousVersion: FeatureStudioVersion(
        digest: _previousDigest,
        sourceReference: _previousSourceReference,
        requestedCapabilityIds: const ['capability.read'],
        dependencies: const [],
        source: _sourceModel(),
      ),
    );
    await expectLater(
      gateway.installVersion(
        review: updateReview,
        expectedRevision: Int64(5),
        decisionId: 'decision-update-mismatch',
        idempotencyId: 'install-update-mismatch',
        expectedBehavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      ),
      throwsA(isA<ProtocolException>()),
    );

    final changedGoal = installedDraft.deepCopy()..goal = 'A substituted goal';
    final changedOrigin = installedDraft.deepCopy()
      ..originatingRequest.text = 'A substituted request';
    for (final tampered in [changedGoal, changedOrigin]) {
      client.installReply.draft = tampered;
      await expectLater(
        gateway.installVersion(
          review: review,
          expectedRevision: Int64(5),
          decisionId: 'decision-a',
          idempotencyId: 'install-a',
          expectedBehavior: _behaviorModel(),
          expectedSource: _sourceModel(),
        ),
        throwsA(isA<ProtocolException>()),
      );
    }
  });

  test(
    'Draft mapping accepts update identity and rejects invalid installation state',
    () async {
      final updateDraft = _validWireDraft()..installationId = 'installation-a';
      final installedWithoutInstallation = _validWireDraft()
        ..status = wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_INSTALLED
        ..clearInstallationId();
      final draftWithInvalidInstallation = _validWireDraft()
        ..installationId = ' installation-a';
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);

      client.getReply = wire.FeatureDraftReply(draft: updateDraft);
      final mapped = await gateway.loadDraft('draft-a');
      expect(mapped.status, FeatureStudioDraftStatus.draft);
      expect(mapped.installationId, 'installation-a');

      for (final malformed in [
        installedWithoutInstallation,
        draftWithInvalidInstallation,
      ]) {
        client.getReply = wire.FeatureDraftReply(
          draft: wire.FeatureDraft.fromBuffer(malformed.writeToBuffer()),
        );
        await expectLater(
          gateway.loadDraft('draft-a'),
          throwsA(isA<ProtocolException>()),
        );
      }
    },
  );

  test(
    'invalid aggregates are rejected before any generated request is sent',
    () async {
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);
      final invalidBehavior = FeatureStudioBehavior(scenarios: const []);

      await expectLater(
        gateway.reviseBehavior(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'behavior-a',
          behavior: invalidBehavior,
          expectedSource: _sourceModel(),
        ),
        throwsArgumentError,
      );

      expect(client.reviseRequests, isEmpty);
    },
  );

  test('mutation replies must echo their exact aggregate contract', () async {
    final client = _RecordingFeatureAuthoringClient();
    final gateway = GrpcFeatureStudioGateway(client: client);
    final changedBehavior = FeatureStudioBehavior(
      scenarios: const [
        FeatureStudioScenario(
          scenarioId: 'brief',
          name: 'Changed brief',
          given: 'A company name',
          when: 'The Feature runs',
          then: 'A concise brief is returned',
        ),
      ],
    );
    final changedSource = FeatureStudioSource(
      implementationProjectPath: 'Feature/Feature.csproj',
      scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
      files: const [
        FeatureStudioSourceFile(
          path: 'Feature/Feature.csproj',
          content: '<Project Sdk="Microsoft.NET.Sdk" />',
        ),
        FeatureStudioSourceFile(
          path: 'Feature.Tests/Feature.Tests.csproj',
          content: '<Project Sdk="Microsoft.NET.Sdk" />',
        ),
        FeatureStudioSourceFile(
          path: 'Feature/Feature.cs',
          content: 'changed source',
        ),
      ],
    );

    await expectLater(
      gateway.reviseBehavior(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'behavior-a',
        behavior: changedBehavior,
        expectedSource: _sourceModel(),
      ),
      throwsA(isA<ProtocolException>()),
    );
    await expectLater(
      gateway.reviseSource(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'source-a',
        source: changedSource,
        expectedBehavior: _behaviorModel(),
      ),
      throwsA(isA<ProtocolException>()),
    );

    final suggestion = FeatureStudioSuggestion(
      patchId: 'patch-a',
      draftId: 'draft-a',
      baseRevision: Int64(4),
      summary: 'Change both aggregates',
      replacementBehavior: changedBehavior,
      replacementSource: changedSource,
    );
    await expectLater(
      gateway.acceptSuggestedChange(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'accept-a',
        suggestion: suggestion,
      ),
      throwsA(isA<ProtocolException>()),
    );
    await expectLater(
      gateway.rejectSuggestedChange(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'reject-a',
        suggestion: suggestion,
        expectedBehavior: changedBehavior,
        expectedSource: changedSource,
        expectedVerification: null,
      ),
      throwsA(isA<ProtocolException>()),
    );

    final verifiedDraft = _validWireDraft(revision: Int64(5))
      ..updatedAtUnixMs = Int64(1_752_537_720_000)
      ..verification = wire.FeatureVerification(
        releaseDigest: _releaseDigest,
        total: 1,
        passed: 1,
        failed: 0,
        skipped: 0,
        verifiedAtUnixMs: Int64(1_752_537_720_000),
      );
    client.verifyReply = wire.FeatureReleaseReviewReply(
      draft: verifiedDraft,
      release: wire.FeatureRelease(
        digest: _releaseDigest,
        sourceKind: wire.FeatureSourceKind.FEATURE_SOURCE_KIND_RUNTIME_AUTHORED,
      ),
    );
    await expectLater(
      gateway.verifyDraft(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'verify-a',
        expectedBehavior: changedBehavior,
        expectedSource: changedSource,
      ),
      throwsA(isA<ProtocolException>()),
    );
  });

  test(
    'mutation replies reject untouched aggregate, status, and verification drift',
    () async {
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);
      final behavior = _behaviorModel();
      final source = _sourceModel();
      final suggestion = FeatureStudioSuggestion(
        patchId: 'patch-a',
        draftId: 'draft-a',
        baseRevision: Int64(4),
        summary: 'Exact replacement',
        replacementBehavior: behavior,
        replacementSource: source,
      );

      client.reviseReply = wire.FeatureDraftReply(
        draft: _validWireDraft(revision: Int64(5))
          ..source.files.last.content = 'silently changed',
      );
      await expectLater(
        gateway.reviseBehavior(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'behavior-untouched',
          behavior: behavior,
          expectedSource: source,
        ),
        throwsA(isA<ProtocolException>()),
      );

      client.reviseReply = wire.FeatureDraftReply(
        draft: _validWireDraft(revision: Int64(5))
          ..behavior.scenarios.single.name = 'silently changed',
      );
      await expectLater(
        gateway.reviseSource(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'source-untouched',
          source: source,
          expectedBehavior: behavior,
        ),
        throwsA(isA<ProtocolException>()),
      );

      client.reviseReply = wire.FeatureDraftReply(
        draft: _validWireDraft(revision: Int64(5))
          ..status = wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_INSTALLED,
      );
      await expectLater(
        gateway.acceptSuggestedChange(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'accept-status',
          suggestion: suggestion,
        ),
        throwsA(isA<ProtocolException>()),
      );

      client.reviseReply = wire.FeatureDraftReply(
        draft: _validWireDraft(revision: Int64(5))
          ..verification = _wireVerification(),
      );
      await expectLater(
        gateway.reviseBehavior(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'behavior-verification',
          behavior: behavior,
          expectedSource: source,
        ),
        throwsA(isA<ProtocolException>()),
      );

      client.rejectReply = wire.FeatureDraftReply(
        draft: _validWireDraft()..verification = _wireVerification(),
      );
      await expectLater(
        gateway.rejectSuggestedChange(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'reject-verification',
          suggestion: suggestion,
          expectedBehavior: behavior,
          expectedSource: source,
          expectedVerification: null,
        ),
        throwsA(isA<ProtocolException>()),
      );

      final verifiedDraft = _validWireDraft(revision: Int64(5))
        ..status = wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_INSTALLED
        ..verification = _wireVerification();
      client.verifyReply = wire.FeatureReleaseReviewReply(
        draft: verifiedDraft,
        release: wire.FeatureRelease(
          digest: _releaseDigest,
          sourceKind:
              wire.FeatureSourceKind.FEATURE_SOURCE_KIND_RUNTIME_AUTHORED,
        ),
      );
      await expectLater(
        gateway.verifyDraft(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'verify-status',
          expectedBehavior: behavior,
          expectedSource: source,
        ),
        throwsA(isA<ProtocolException>()),
      );
    },
  );

  test('command text uses its protocol-specific canonical bounds', () async {
    final client = _RecordingFeatureAuthoringClient();
    final gateway = GrpcFeatureStudioGateway(client: client);

    for (final draftId in [
      ' draft-a',
      'draft\u0001a',
      'draft\u0085a',
      'd' * 129,
    ]) {
      await expectLater(gateway.loadDraft(draftId), throwsArgumentError);
    }
    await expectLater(
      gateway.reviseBehavior(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        idempotencyId: 'i' * 257,
        behavior: _behaviorModel(),
        expectedSource: _sourceModel(),
      ),
      throwsArgumentError,
    );
    for (final guidance in [
      ' guidance',
      'guide\u0001',
      'guide\u0085',
      'g' * 4097,
    ]) {
      await expectLater(
        gateway.suggestChange(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          guidance: guidance,
          suggestionId: 'suggestion-a',
        ),
        throwsArgumentError,
      );
    }
    await expectLater(
      gateway.suggestChange(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        guidance: 'Clarify the expected outcome',
        suggestionId: 's' * 257,
      ),
      throwsArgumentError,
    );

    expect(client.getRequest, isNull);
    expect(client.reviseRequests, isEmpty);
    expect(client.suggestRequest, isNull);
  });

  test('rejects malformed draft metadata and patch summaries', () async {
    final client = _RecordingFeatureAuthoringClient();
    final gateway = GrpcFeatureStudioGateway(client: client);

    final invalidDrafts = <wire.FeatureDraft>[
      _validWireDraft()..originatingRequest.operationId = '',
      _validWireDraft()..originatingRequest.text = ' request',
      _validWireDraft()..originatingRequest.text = 'bad\u0085request',
      _validWireDraft()..goal = 'g' * 4097,
      _validWireDraft()
        ..createdAtUnixMs = Int64(1_752_537_700_000)
        ..updatedAtUnixMs = Int64(1_752_537_600_000),
    ];
    for (final invalidDraft in invalidDrafts) {
      client.getReply = wire.FeatureDraftReply(draft: invalidDraft);
      await expectLater(
        gateway.loadDraft('draft-a'),
        throwsA(isA<ProtocolException>()),
      );
    }

    client.suggestReply = wire.FeatureDraftPatchReply(
      patch: _validWirePatch()..summary = 's' * 2049,
    );
    await expectLater(
      gateway.suggestChange(
        draftId: 'draft-a',
        expectedRevision: Int64(4),
        guidance: 'Clarify the expected outcome',
        suggestionId: 'suggestion-a',
      ),
      throwsA(isA<ProtocolException>()),
    );
  });

  test(
    'rejects missing payloads, revision regression, and unknown enums',
    () async {
      final client = _RecordingFeatureAuthoringClient();
      final gateway = GrpcFeatureStudioGateway(client: client);

      await expectLater(
        gateway.loadDraft('draft-a'),
        throwsA(isA<ProtocolException>()),
      );

      client.getReply = wire.FeatureDraftReply(
        draft: _validWireDraft()
          ..status = wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_UNSPECIFIED,
      );
      await expectLater(
        gateway.loadDraft('draft-a'),
        throwsA(isA<ProtocolException>()),
      );

      client.reviseReply = wire.FeatureDraftReply(
        draft: _validWireDraft(revision: Int64(3)),
      );
      await expectLater(
        gateway.reviseBehavior(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'behavior-a',
          behavior: _behaviorModel(),
          expectedSource: _sourceModel(),
        ),
        throwsA(isA<ProtocolException>()),
      );

      client.verifyReply = wire.FeatureReleaseReviewReply(
        draft: _validWireDraft(revision: Int64(5)),
        release: wire.FeatureRelease(
          digest: _releaseDigest,
          sourceKind: wire.FeatureSourceKind.FEATURE_SOURCE_KIND_UNSPECIFIED,
        ),
      );
      await expectLater(
        gateway.verifyDraft(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'verify-a',
          expectedBehavior: _behaviorModel(),
          expectedSource: _sourceModel(),
        ),
        throwsA(isA<ProtocolException>()),
      );
    },
  );
}

FeatureStudioBehavior _behaviorModel() => FeatureStudioBehavior(
  scenarios: const [
    FeatureStudioScenario(
      scenarioId: 'brief',
      name: 'Create a brief',
      given: 'A company name',
      when: 'The Feature runs',
      then: 'A concise brief is returned',
    ),
  ],
);

FeatureStudioSource _sourceModel() => FeatureStudioSource(
  implementationProjectPath: 'Feature/Feature.csproj',
  scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
  files: const [
    FeatureStudioSourceFile(
      path: 'Feature/Feature.csproj',
      content: '<Project Sdk="Microsoft.NET.Sdk" />',
    ),
    FeatureStudioSourceFile(
      path: 'Feature.Tests/Feature.Tests.csproj',
      content: '<Project Sdk="Microsoft.NET.Sdk" />',
    ),
    FeatureStudioSourceFile(
      path: 'Feature/Feature.cs',
      content: 'public sealed class Feature {}',
    ),
  ],
);

wire.FeatureDraftPatch _validWirePatch() => wire.FeatureDraftPatch(
  patchId: 'patch-a',
  draftId: 'draft-a',
  baseRevision: Int64(4),
  summary: 'Clarify the outcome',
  replacementBehavior: _validWireDraft().behavior,
  replacementSource: _validWireDraft().source,
);

wire.FeatureVerification _wireVerification({Int64? verifiedAtUnixMs}) =>
    wire.FeatureVerification(
      releaseDigest: _releaseDigest,
      total: 1,
      passed: 1,
      failed: 0,
      skipped: 0,
      verifiedAtUnixMs: verifiedAtUnixMs ?? Int64(1_752_537_660_000),
      sourceReference: _sourceReference,
      scenarios: [
        wire.FeatureVerificationScenario(
          scenarioId: 'brief',
          name: 'Create a brief',
          outcome: wire.FeatureScenarioOutcome.FEATURE_SCENARIO_OUTCOME_PASSED,
          durationMilliseconds: Int64(42),
        ),
      ],
      artifacts: [
        wire.FeatureVerificationArtifact(
          name: 'test-results.txt',
          mediaType: 'text/plain',
          sizeBytes: Int64(128),
          digest: _artifactDigest,
        ),
      ],
    );

wire.FeatureVerification _wireVerificationSummary({Int64? verifiedAtUnixMs}) {
  final summary = _wireVerification(verifiedAtUnixMs: verifiedAtUnixMs)
    ..clearSourceReference()
    ..scenarios.clear()
    ..artifacts.clear();
  return summary;
}

wire.FeatureRelease _wireRelease({
  String digest = _releaseDigest,
  String sourceReference = _sourceReference,
  String sourceContent = 'public sealed class Feature {}',
}) => wire.FeatureRelease(
  digest: digest,
  sourceKind: wire.FeatureSourceKind.FEATURE_SOURCE_KIND_RUNTIME_AUTHORED,
  requestedCapabilityIds: ['capability.read'],
  sourceReference: sourceReference,
  source: wire.FeatureSourceSnapshot(
    implementationProjectPath: 'Feature/Feature.csproj',
    scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
    files: [
      wire.FeatureSourceFile(
        path: 'Feature/Feature.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      wire.FeatureSourceFile(
        path: 'Feature.Tests/Feature.Tests.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      wire.FeatureSourceFile(
        path: 'Feature/Feature.cs',
        content: sourceContent,
      ),
    ],
  ),
);

wire.FeatureDraftReply _wireRecoveryReply({
  required bool installed,
  bool includePrevious = false,
}) {
  final draft = _validWireDraft()
    ..verification = (_wireVerificationSummary()
      ..sourceReference = _sourceReference);
  if (installed) {
    draft
      ..status = wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_INSTALLED
      ..installationId = 'installation-a';
  }
  final previousRelease = _wireRelease(
    digest: _previousDigest,
    sourceReference: _previousSourceReference,
    sourceContent: 'public sealed class PreviousFeature {}',
  )..clearSource();
  final release = _wireRelease()..clearSource();
  final recovery = wire.FeatureInstallationRecovery(
    installed: installed,
    verification: _wireVerification(),
    release: release,
    installationId: 'installation-a',
    grants: [
      wire.FeatureGrant(
        capabilityId: 'capability.read',
        capabilityVersion: 1,
        constraintsJson: '{"allowedToolIds":["capability.read"]}',
      ),
    ],
    subscriptions: ['manual'],
    previousRelease: includePrevious ? previousRelease : null,
    decisionId: installed ? null : 'decision-a',
    idempotencyId: installed ? null : 'install-a',
    rollbackAvailable: installed && includePrevious,
    paused: false,
  );
  if (!installed) recovery.clearInstalled();
  if (!recovery.rollbackAvailable) recovery.clearRollbackAvailable();
  recovery.clearPaused();
  return wire.FeatureDraftReply(draft: draft, recovery: recovery);
}

wire.FeatureReleaseSourceReply _previousSourceReply() =>
    wire.FeatureReleaseSourceReply(
      featureId: 'draft-a',
      installationId: 'installation-a',
      releaseDigest: _previousDigest,
      sourceReference: _previousSourceReference,
      source: _wireRelease(
        digest: _previousDigest,
        sourceReference: _previousSourceReference,
        sourceContent: 'public sealed class PreviousFeature {}',
      ).source,
    );

FeatureStudioVerification _verificationModel() => FeatureStudioVerification(
  releaseDigest: _releaseDigest,
  sourceReference: _sourceReference,
  total: 1,
  passed: 1,
  failed: 0,
  skipped: 0,
  verifiedAt: DateTime.fromMillisecondsSinceEpoch(
    1_752_537_660_000,
    isUtc: true,
  ),
  scenarios: const [
    FeatureStudioVerificationScenario(
      scenarioId: 'brief',
      name: 'Create a brief',
      outcome: FeatureStudioScenarioOutcome.passed,
      safeFailure: null,
      durationMilliseconds: 42,
    ),
  ],
  artifacts: const [
    FeatureStudioVerificationArtifact(
      name: 'test-results.txt',
      mediaType: 'text/plain',
      sizeBytes: 128,
      digest: _artifactDigest,
    ),
  ],
);

FeatureStudioVersion _versionModel({
  List<String> requestedCapabilityIds = const ['capability.read'],
}) => FeatureStudioVersion(
  digest: _releaseDigest,
  sourceReference: _sourceReference,
  requestedCapabilityIds: requestedCapabilityIds,
  dependencies: const [],
  source: _sourceModel(),
);

FeatureStudioDraft _draftModel({
  required Int64 revision,
  FeatureStudioVerification? verification,
}) => FeatureStudioDraft(
  draftId: 'draft-a',
  originatingRequest: const FeatureStudioOriginatingRequest(
    operationId: 'operation-a',
    conversationId: 'conversation-a',
    text: 'Research Acme',
  ),
  goal: 'Create a concise company brief',
  status: FeatureStudioDraftStatus.draft,
  installationId: null,
  behavior: _behaviorModel(),
  source: _sourceModel(),
  verification: verification,
  revision: revision,
  createdAt: DateTime.fromMillisecondsSinceEpoch(
    1_752_537_600_000,
    isUtc: true,
  ),
  updatedAt: DateTime.fromMillisecondsSinceEpoch(
    1_752_537_660_000,
    isUtc: true,
  ),
);

wire.FeatureDraft _validWireDraft({Int64? revision}) => wire.FeatureDraft(
  draftId: 'draft-a',
  originatingRequest: wire.OriginatingRequest(
    operationId: 'operation-a',
    conversationId: 'conversation-a',
    text: 'Research Acme',
  ),
  goal: 'Create a concise company brief',
  status: wire.FeatureDraftStatus.FEATURE_DRAFT_STATUS_DRAFT,
  behavior: wire.FeatureBehavior(
    scenarios: [
      wire.FeatureScenario(
        scenarioId: 'brief',
        name: 'Create a brief',
        given: 'A company name',
        when: 'The Feature runs',
        then: 'A concise brief is returned',
      ),
    ],
  ),
  source: wire.FeatureSourceSnapshot(
    implementationProjectPath: 'Feature/Feature.csproj',
    scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
    files: [
      wire.FeatureSourceFile(
        path: 'Feature/Feature.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      wire.FeatureSourceFile(
        path: 'Feature.Tests/Feature.Tests.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      wire.FeatureSourceFile(
        path: 'Feature/Feature.cs',
        content: 'public sealed class Feature {}',
      ),
    ],
  ),
  revision: revision ?? Int64(4),
  createdAtUnixMs: Int64(1_752_537_600_000),
  updatedAtUnixMs: Int64(1_752_537_660_000),
);

class _RecordingFeatureAuthoringClient implements FeatureAuthoringClient {
  wire.GetFeatureDraftRequest? getRequest;
  wire.FeatureDraftReply getReply = wire.FeatureDraftReply();
  wire.ResetFeatureDraftInstallationRequest? resetRequest;
  wire.FeatureDraftReply resetReply = wire.FeatureDraftReply();
  final List<wire.ReviseFeatureDraftRequest> reviseRequests = [];
  wire.FeatureDraftReply reviseReply = wire.FeatureDraftReply(
    draft: _validWireDraft(revision: Int64(5)),
  );
  wire.FeatureDraftReply? rejectReply;
  wire.SuggestFeatureChangeRequest? suggestRequest;
  wire.FeatureDraftPatchReply suggestReply = wire.FeatureDraftPatchReply();
  wire.VerifyFeatureDraftRequest? verifyRequest;
  wire.FeatureReleaseReviewReply verifyReply = wire.FeatureReleaseReviewReply();
  wire.ReviewFeatureAccessRequest? accessReviewRequest;
  wire.FeatureAccessReviewReply accessReviewReply =
      wire.FeatureAccessReviewReply();
  wire.InstallFeatureVersionRequest? installRequest;
  wire.FeatureInstallReply installReply = wire.FeatureInstallReply();
  wire.GetFeatureReleaseSourceRequest? sourceRequest;
  wire.FeatureReleaseSourceReply sourceReply = wire.FeatureReleaseSourceReply();
  Object? sourceError;

  @override
  Future<wire.FeatureDraftReply> getFeatureDraft(
    wire.GetFeatureDraftRequest request,
  ) async {
    getRequest = request;
    return getReply;
  }

  @override
  Future<wire.FeatureDraftReply> resetFeatureDraftInstallation(
    wire.ResetFeatureDraftInstallationRequest request,
  ) async {
    resetRequest = request;
    return resetReply;
  }

  @override
  Future<wire.FeatureDraftReply> reviseFeatureDraft(
    wire.ReviseFeatureDraftRequest request,
  ) async {
    reviseRequests.add(request);
    if (request.whichCommand() ==
        wire.ReviseFeatureDraftRequest_Command.rejectSuggestedChange) {
      return rejectReply ?? wire.FeatureDraftReply(draft: _validWireDraft());
    }
    return reviseReply;
  }

  @override
  Future<wire.FeatureDraftPatchReply> suggestFeatureChange(
    wire.SuggestFeatureChangeRequest request,
  ) async {
    suggestRequest = request;
    return suggestReply;
  }

  @override
  Future<wire.FeatureReleaseReviewReply> verifyFeatureDraft(
    wire.VerifyFeatureDraftRequest request,
  ) async {
    verifyRequest = request;
    return verifyReply;
  }

  @override
  Future<wire.FeatureAccessReviewReply> reviewFeatureAccess(
    wire.ReviewFeatureAccessRequest request,
  ) async {
    accessReviewRequest = request;
    return accessReviewReply;
  }

  @override
  Future<wire.FeatureInstallReply> installFeatureVersion(
    wire.InstallFeatureVersionRequest request,
  ) async {
    installRequest = request;
    return installReply;
  }

  @override
  Future<wire.FeatureReply> getFeature(wire.GetFeatureRequest request) =>
      throw UnimplementedError();

  @override
  Future<wire.FeatureReleaseSourceReply> getFeatureReleaseSource(
    wire.GetFeatureReleaseSourceRequest request,
  ) async {
    sourceRequest = request;
    final error = sourceError;
    if (error != null) throw error;
    return sourceReply;
  }

  @override
  Future<wire.FeatureReply> rollbackFeatureVersion(
    wire.RollbackFeatureVersionRequest request,
  ) => throw UnimplementedError();
}

class _SequentialIds {
  int _next = 0;

  String call() => 'strict-save-${++_next}';
}

class _ApplyingFeatureAuthoringClient implements FeatureAuthoringClient {
  _ApplyingFeatureAuthoringClient(wire.FeatureDraft initial)
    : _draft = _copyDraft(initial);

  wire.FeatureDraft _draft;
  final List<wire.ReviseFeatureDraftRequest> reviseRequests = [];

  @override
  Future<wire.FeatureDraftReply> getFeatureDraft(
    wire.GetFeatureDraftRequest request,
  ) async => wire.FeatureDraftReply(draft: _copyDraft(_draft));

  @override
  Future<wire.FeatureDraftReply> resetFeatureDraftInstallation(
    wire.ResetFeatureDraftInstallationRequest request,
  ) => throw UnimplementedError();

  @override
  Future<wire.FeatureDraftReply> reviseFeatureDraft(
    wire.ReviseFeatureDraftRequest request,
  ) async {
    reviseRequests.add(request.deepCopy());
    if (request.expectedRevision != _draft.revision) {
      throw StateError('stale test revision');
    }
    final next = _copyDraft(_draft)
      ..revision = _draft.revision + Int64.ONE
      ..updatedAtUnixMs = _draft.updatedAtUnixMs + Int64.ONE;
    switch (request.whichCommand()) {
      case wire.ReviseFeatureDraftRequest_Command.reviseBehavior:
        next.behavior = request.reviseBehavior.behavior.deepCopy();
      case wire.ReviseFeatureDraftRequest_Command.reviseSource:
        next.source = request.reviseSource.source.deepCopy();
      default:
        throw StateError('unexpected test command');
    }
    next.clearVerification();
    _draft = next;
    return wire.FeatureDraftReply(draft: _copyDraft(_draft));
  }

  @override
  Future<wire.FeatureDraftPatchReply> suggestFeatureChange(
    wire.SuggestFeatureChangeRequest request,
  ) => throw UnimplementedError();

  @override
  Future<wire.FeatureReleaseReviewReply> verifyFeatureDraft(
    wire.VerifyFeatureDraftRequest request,
  ) => throw UnimplementedError();

  @override
  Future<wire.FeatureAccessReviewReply> reviewFeatureAccess(
    wire.ReviewFeatureAccessRequest request,
  ) => throw UnimplementedError();

  @override
  Future<wire.FeatureInstallReply> installFeatureVersion(
    wire.InstallFeatureVersionRequest request,
  ) => throw UnimplementedError();

  @override
  Future<wire.FeatureReply> getFeature(wire.GetFeatureRequest request) =>
      throw UnimplementedError();

  @override
  Future<wire.FeatureReleaseSourceReply> getFeatureReleaseSource(
    wire.GetFeatureReleaseSourceRequest request,
  ) => throw UnimplementedError();

  @override
  Future<wire.FeatureReply> rollbackFeatureVersion(
    wire.RollbackFeatureVersionRequest request,
  ) => throw UnimplementedError();
}

wire.FeatureDraft _copyDraft(wire.FeatureDraft draft) =>
    wire.FeatureDraft.fromBuffer(draft.writeToBuffer());
