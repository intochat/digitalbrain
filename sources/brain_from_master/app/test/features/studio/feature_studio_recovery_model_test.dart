import 'package:digitalbrain_flutter/features/studio/feature_studio_models.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('model accepts a governed update reset Draft identity', () {
    expect(
      () => FeatureStudioDraft(
        draftId: 'draft-a',
        originatingRequest: const FeatureStudioOriginatingRequest(
          operationId: 'operation-a',
          conversationId: 'conversation-a',
          text: 'Research Acme',
        ),
        goal: 'Create a concise company brief',
        status: FeatureStudioDraftStatus.draft,
        installationId: 'installation-a',
        behavior: _behavior(),
        source: _source(),
        verification: null,
        revision: Int64(6),
        createdAt: DateTime.utc(2026, 7, 15, 10),
        updatedAt: DateTime.utc(2026, 7, 15, 10, 2),
      ),
      returnsNormally,
    );
  });

  test('recovery model accepts coherent reserved and installed states', () {
    final reserved = _recovery(
      installed: false,
      retryIds: true,
      previous: true,
    );
    final installed = _recovery(
      installed: true,
      retryIds: false,
      previous: true,
      rollbackAvailable: true,
    );
    final paused = _recovery(
      installed: true,
      retryIds: false,
      paused: true,
      pauseReason: 'Paused by policy',
    );

    expect(_draft(recovery: reserved).installationRecovery, same(reserved));
    expect(
      _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-a',
        recovery: installed,
      ).installationRecovery,
      same(installed),
    );
    expect(
      _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-a',
        recovery: paused,
      ).installationRecovery,
      same(paused),
    );
  });

  test(
    'installed recovery preserves a historical Draft verification and source',
    () {
      final recovery = _recovery(installed: true, retryIds: false);
      final historicalVerification = _verification(
        releaseDigest: 'c' * 64,
        sourceReference: 'sha256:${'d' * 64}',
      );

      final draft = _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-a',
        recovery: recovery,
        verification: historicalVerification,
        source: _source(content: 'historical source'),
      );

      expect(draft.verification, same(historicalVerification));
      expect(draft.source.files.last.content, 'historical source');
      expect(
        draft.installationRecovery?.verification,
        same(recovery.verification),
      );
      expect(
        draft.installationRecovery?.version.source?.files.last.content,
        'source',
      );
    },
  );

  test('reserved recovery requires exact Draft verification authority', () {
    final recovery = _recovery(installed: false, retryIds: true);

    expect(
      () => _draft(
        recovery: recovery,
        verification: _verification(releaseDigest: 'c' * 64),
      ),
      throwsArgumentError,
    );
  });

  test('installed recovery keeps source equality for the same release', () {
    final recovery = _recovery(installed: true, retryIds: false);

    expect(
      () => _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-a',
        recovery: recovery,
        source: _source(content: 'tampered source'),
      ),
      throwsArgumentError,
    );
  });

  test('recovery model matches pause and evidence wire boundaries', () {
    expect(
      () => _recovery(
        installed: true,
        retryIds: false,
        paused: true,
        pauseReason: 'x' * 4096,
        zeroArtifacts: true,
      ),
      returnsNormally,
    );
    expect(
      () => _recovery(
        installed: true,
        retryIds: false,
        paused: true,
        pauseReason: 'x' * 4097,
      ),
      throwsArgumentError,
    );
    expect(
      () => _recovery(installed: false, retryIds: true, zeroArtifacts: true),
      returnsNormally,
    );
  });

  test('recovery model rejects incoherent authority and lifecycle states', () {
    final validReserved = _recovery(installed: false, retryIds: true);
    final validInstalled = _recovery(installed: true, retryIds: false);
    final invalid = <FeatureStudioDraft Function()>[
      () => _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-a',
        recovery: validReserved,
      ),
      () => _draft(recovery: validInstalled),
      () => _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-other',
        recovery: validInstalled,
      ),
      () => _draft(recovery: _recovery(installed: false, retryIds: false)),
      () => _draft(recovery: _recovery(installed: true, retryIds: true)),
      () => _draft(
        recovery: _recovery(
          installed: false,
          retryIds: true,
          rollbackAvailable: true,
          previous: true,
        ),
      ),
      () => _draft(
        recovery: _recovery(
          installed: false,
          retryIds: true,
          paused: true,
          pauseReason: 'Paused by policy',
        ),
      ),
      () => _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-a',
        recovery: _recovery(
          installed: true,
          retryIds: false,
          rollbackAvailable: true,
        ),
      ),
      () => _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-a',
        recovery: _recovery(installed: true, retryIds: false, previous: true),
      ),
      () => _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-a',
        recovery: _recovery(installed: true, retryIds: false, paused: true),
      ),
      () => _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-a',
        recovery: _recovery(
          installed: true,
          retryIds: false,
          pauseReason: 'Unexpected reason',
        ),
      ),
      () => _draft(
        status: FeatureStudioDraftStatus.installed,
        installationId: 'installation-a',
        recovery: _recovery(
          installed: true,
          retryIds: false,
          paused: true,
          pauseReason: 'Paused by policy',
          previous: true,
          rollbackAvailable: true,
        ),
      ),
      () => _draft(
        recovery: _recovery(
          installed: false,
          retryIds: true,
          verificationDigest: 'b' * 64,
        ),
      ),
      () => _draft(
        recovery: _recovery(
          installed: false,
          retryIds: true,
          grantCapabilityId: 'capability.write',
        ),
      ),
      () => _draft(
        recovery: _recovery(
          installed: false,
          retryIds: true,
          emptySubscriptions: true,
        ),
      ),
      () => _draft(
        recovery: _recovery(
          installed: false,
          retryIds: true,
          requestedCapabilityIds: const ['capability.read', 'capability.read'],
        ),
      ),
      () => _draft(
        recovery: _recovery(
          installed: false,
          retryIds: true,
          requestedCapabilityIds: [
            for (var index = 0; index < 33; index++) 'capability.$index',
          ],
        ),
      ),
    ];

    for (final create in invalid) {
      expect(create, throwsArgumentError);
    }
  });
}

FeatureStudioDraft _draft({
  FeatureStudioDraftStatus status = FeatureStudioDraftStatus.draft,
  String? installationId,
  required FeatureStudioInstallationRecovery recovery,
  FeatureStudioVerification? verification,
  FeatureStudioSource? source,
}) => FeatureStudioDraft(
  draftId: 'draft-a',
  originatingRequest: const FeatureStudioOriginatingRequest(
    operationId: 'operation-a',
    conversationId: 'conversation-a',
    text: 'Research Acme',
  ),
  goal: 'Create a concise company brief',
  status: status,
  installationId: installationId,
  behavior: _behavior(),
  source: source ?? _source(),
  verification: verification ?? recovery.verification,
  revision: Int64(5),
  createdAt: DateTime.utc(2026, 7, 15, 10),
  updatedAt: DateTime.utc(2026, 7, 15, 10, 2),
  installationRecovery: recovery,
);

FeatureStudioInstallationRecovery _recovery({
  required bool installed,
  required bool retryIds,
  bool previous = false,
  bool rollbackAvailable = false,
  bool paused = false,
  String? pauseReason,
  String? verificationDigest,
  String grantCapabilityId = 'capability.read',
  bool emptySubscriptions = false,
  bool zeroArtifacts = false,
  List<String> requestedCapabilityIds = const ['capability.read'],
}) => FeatureStudioInstallationRecovery(
  installed: installed,
  verification: _verification(
    releaseDigest: verificationDigest ?? 'a' * 64,
    zeroArtifacts: zeroArtifacts,
  ),
  version: _version(requestedCapabilityIds: requestedCapabilityIds),
  installationId: 'installation-a',
  grants: [
    for (final capabilityId
        in grantCapabilityId == 'capability.read'
            ? requestedCapabilityIds
            : [grantCapabilityId])
      FeatureStudioGrant(
        capabilityId: capabilityId,
        capabilityVersion: 1,
        provider: null,
        connectionId: null,
        constraintsJson: '{"allowedToolIds":["$capabilityId"]}',
        constraintSummary: 'Only $capabilityId',
      ),
  ],
  subscriptions: emptySubscriptions ? const [] : const ['manual'],
  previousVersion: previous
      ? _version(digest: 'c' * 64, sourceReference: 'sha256:${'d' * 64}')
      : null,
  decisionId: retryIds ? 'decision-a' : null,
  idempotencyId: retryIds ? 'install-a' : null,
  rollbackAvailable: rollbackAvailable,
  paused: paused,
  pauseReason: pauseReason,
);

FeatureStudioVerification _verification({
  required String releaseDigest,
  String? sourceReference,
  bool zeroArtifacts = false,
}) => FeatureStudioVerification(
  releaseDigest: releaseDigest,
  sourceReference: sourceReference ?? 'sha256:${'b' * 64}',
  total: 1,
  passed: 1,
  failed: 0,
  skipped: 0,
  verifiedAt: DateTime.utc(2026, 7, 15, 10, 1),
  scenarios: const [
    FeatureStudioVerificationScenario(
      scenarioId: 'brief',
      name: 'Create a brief',
      outcome: FeatureStudioScenarioOutcome.passed,
      safeFailure: null,
      durationMilliseconds: 20,
    ),
  ],
  artifacts: zeroArtifacts
      ? const []
      : [
          FeatureStudioVerificationArtifact(
            name: 'verification.json',
            mediaType: 'application/json',
            sizeBytes: 128,
            digest: 'e' * 64,
          ),
        ],
);

FeatureStudioVersion _version({
  String? digest,
  String? sourceReference,
  List<String> requestedCapabilityIds = const ['capability.read'],
}) => FeatureStudioVersion(
  digest: digest ?? 'a' * 64,
  sourceReference: sourceReference ?? 'sha256:${'b' * 64}',
  requestedCapabilityIds: requestedCapabilityIds,
  dependencies: const [],
  source: _source(),
);

FeatureStudioBehavior _behavior() => FeatureStudioBehavior(
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

FeatureStudioSource _source({String content = 'source'}) => FeatureStudioSource(
  implementationProjectPath: 'Feature/Feature.csproj',
  scenarioProjectPath: 'Feature.Tests/Feature.Tests.csproj',
  files: [
    const FeatureStudioSourceFile(
      path: 'Feature/Feature.csproj',
      content: '<Project Sdk="Microsoft.NET.Sdk" />',
    ),
    const FeatureStudioSourceFile(
      path: 'Feature.Tests/Feature.Tests.csproj',
      content: '<Project Sdk="Microsoft.NET.Sdk" />',
    ),
    FeatureStudioSourceFile(path: 'Feature/Feature.cs', content: content),
  ],
);
