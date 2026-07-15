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
    'verify requires a revised draft and runtime-authored release',
    () async {
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
      final client = _RecordingFeatureAuthoringClient()
        ..verifyReply = wire.FeatureReleaseReviewReply(
          draft: verifiedDraft,
          release: wire.FeatureRelease(
            digest: _releaseDigest,
            sourceKind:
                wire.FeatureSourceKind.FEATURE_SOURCE_KIND_RUNTIME_AUTHORED,
          ),
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
      expect(result.revision, Int64(5));
      expect(result.verification?.passed, 1);
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

wire.FeatureVerification _wireVerification() => wire.FeatureVerification(
  releaseDigest: _releaseDigest,
  total: 1,
  passed: 1,
  failed: 0,
  skipped: 0,
  verifiedAtUnixMs: Int64(1_752_537_660_000),
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
  final List<wire.ReviseFeatureDraftRequest> reviseRequests = [];
  wire.FeatureDraftReply reviseReply = wire.FeatureDraftReply(
    draft: _validWireDraft(revision: Int64(5)),
  );
  wire.FeatureDraftReply? rejectReply;
  wire.SuggestFeatureChangeRequest? suggestRequest;
  wire.FeatureDraftPatchReply suggestReply = wire.FeatureDraftPatchReply();
  wire.VerifyFeatureDraftRequest? verifyRequest;
  wire.FeatureReleaseReviewReply verifyReply = wire.FeatureReleaseReviewReply();

  @override
  Future<wire.FeatureDraftReply> getFeatureDraft(
    wire.GetFeatureDraftRequest request,
  ) async {
    getRequest = request;
    return getReply;
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
}

wire.FeatureDraft _copyDraft(wire.FeatureDraft draft) =>
    wire.FeatureDraft.fromBuffer(draft.writeToBuffer());
