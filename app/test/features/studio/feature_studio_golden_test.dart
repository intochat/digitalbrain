import 'dart:async';

import 'package:digitalbrain_flutter/digital_brain_ui/digital_brain_ui.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_controller.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_gateway.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_models.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_page.dart';
import 'package:digitalbrain_flutter/runtime/runtime_errors.dart';
import 'package:digitalbrain_flutter/shell/digitalbrain_shell.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

const Key _goldenBoundaryKey = Key('feature-studio-golden-boundary');

void main() {
  testWidgets('saved routed Studio at 1440 pixels', (tester) async {
    await _pumpGolden(tester, const Size(1440, 900));

    await _matchGolden(tester, 'feature_studio_saved_1440.png');
  });

  testWidgets('suggestion review routed Studio at 1440 pixels', (tester) async {
    final harness = await _pumpGolden(tester, const Size(1440, 900));
    await harness.controller.requestSuggestedChange(
      'Make the brief easier to scan',
    );
    await tester.pumpAndSettle();

    await _matchGolden(tester, 'feature_studio_suggestions_1440.png');
  });

  testWidgets('saving routed Studio at 1024 pixels', (tester) async {
    final harness = await _pumpGolden(tester, const Size(1024, 900));
    harness.gateway.pendingBehaviorSave = Completer<FeatureStudioDraft>();
    harness.controller.reviseBehavior(_editedBehavior());
    unawaited(harness.controller.saveNow());
    await tester.pump();
    expect(harness.controller.savePhase, FeatureStudioSavePhase.saving);

    await _matchGolden(tester, 'feature_studio_saving_1024.png');

    harness.gateway.pendingBehaviorSave!.complete(
      _copyDraft(
        harness.gateway.draft,
        revision: Int64(5),
        behavior: _editedBehavior(),
      ),
    );
    await tester.pump();
  });

  testWidgets('verified routed Studio at 1024 pixels', (tester) async {
    final harness = await _pumpGolden(tester, const Size(1024, 900));
    await harness.controller.verify();
    await tester.pumpAndSettle();

    await _matchGolden(tester, 'feature_studio_verified_1024.png');
  });

  testWidgets('conflicted routed Studio at 736 pixels', (tester) async {
    final harness = await _pumpGolden(tester, const Size(736, 900));
    harness.gateway.abortBehaviorSave = true;
    harness.controller.reviseBehavior(_editedBehavior());
    await harness.controller.saveNow();
    await tester.pumpAndSettle();

    await _matchGolden(tester, 'feature_studio_conflict_736.png');
  });

  testWidgets('compact suggestion review at 320 pixels', (tester) async {
    final harness = await _pumpGolden(tester, const Size(320, 900));
    await harness.controller.requestSuggestedChange(
      'Make the brief easier to scan',
    );
    await tester.pumpAndSettle();
    await _openCompactDisclosure(tester, featureStudioOpenSuggestionsKey);

    await _matchGolden(tester, 'feature_studio_suggestions_320.png');
  });

  testWidgets('compact Code review at 320 pixels', (tester) async {
    await _pumpGolden(tester, const Size(320, 900));
    await _openCompactDisclosure(tester, featureStudioOpenCodeKey);
    final sourceFile = find.text(
      'CompanyBrief/CompanyBriefFeature.cs',
      skipOffstage: false,
    );
    await tester.ensureVisible(sourceFile);
    await tester.tap(sourceFile);
    await tester.pumpAndSettle();

    await _matchGolden(tester, 'feature_studio_code_320.png');
  });
}

Future<_GoldenHarness> _pumpGolden(WidgetTester tester, Size size) async {
  tester.view.devicePixelRatio = 1;
  tester.view.physicalSize = size;
  addTearDown(tester.view.resetDevicePixelRatio);
  addTearDown(tester.view.resetPhysicalSize);
  final gateway = _GoldenGateway();
  final controller = FeatureStudioController(
    draftId: 'draft-golden',
    gateway: gateway,
    delay: (_) => Future<void>.value(),
    idFactory: _GoldenIds().call,
  );
  addTearDown(controller.dispose);
  await controller.load();

  await tester.pumpWidget(
    MaterialApp(
      debugShowCheckedModeBanner: false,
      theme: buildDigitalBrainTheme(useGoogleFonts: false),
      darkTheme: buildDigitalBrainTheme(useGoogleFonts: false),
      themeMode: ThemeMode.dark,
      builder: (context, child) => RepaintBoundary(
        key: _goldenBoundaryKey,
        child: WindowSizeScope(child: child ?? const SizedBox.shrink()),
      ),
      home: DigitalBrainShell(
        location: Uri.parse('/features/proposals/draft-golden'),
        onDestinationSelected: (_) {},
        onSignOut: () {},
        child: FeatureStudioPage(
          draftId: 'draft-golden',
          controller: controller,
          onBackToChat: () {},
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
  return _GoldenHarness(controller: controller, gateway: gateway);
}

Future<void> _openCompactDisclosure(WidgetTester tester, Key key) async {
  final mainList = find.byType(ListView).last;
  for (
    var attempt = 0;
    attempt < 8 && find.byKey(key).evaluate().isEmpty;
    attempt++
  ) {
    await tester.drag(mainList, const Offset(0, -300));
    await tester.pumpAndSettle();
  }
  expect(find.byKey(key), findsOneWidget);
  await tester.tap(find.byKey(key));
  await tester.pumpAndSettle();
}

Future<void> _matchGolden(WidgetTester tester, String name) => expectLater(
  find.byKey(_goldenBoundaryKey),
  matchesGoldenFile('../../goldens/$name'),
);

class _GoldenHarness {
  const _GoldenHarness({required this.controller, required this.gateway});

  final FeatureStudioController controller;
  final _GoldenGateway gateway;
}

class _GoldenIds {
  int _next = 0;

  String call() => 'golden-id-${++_next}';
}

class _GoldenGateway implements FeatureStudioGateway {
  FeatureStudioDraft draft = _draft();
  Completer<FeatureStudioDraft>? pendingBehaviorSave;
  bool abortBehaviorSave = false;

  @override
  Future<FeatureStudioDraft> loadDraft(String draftId) async => draft;

  @override
  Future<FeatureStudioDraft> reviseBehavior({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior behavior,
    required FeatureStudioSource expectedSource,
  }) async {
    if (abortBehaviorSave) {
      throw const TransportException(
        TransportErrorCode.aborted,
        'Draft changed.',
      );
    }
    if (pendingBehaviorSave case final pending?) return pending.future;
    draft = _copyDraft(
      draft,
      revision: expectedRevision + Int64.ONE,
      behavior: behavior,
      verification: null,
    );
    return draft;
  }

  @override
  Future<FeatureStudioDraft> reviseSource({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSource source,
    required FeatureStudioBehavior expectedBehavior,
  }) async {
    draft = _copyDraft(
      draft,
      revision: expectedRevision + Int64.ONE,
      source: source,
      verification: null,
    );
    return draft;
  }

  @override
  Future<FeatureStudioDraft> acceptSuggestedChange({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSuggestion suggestion,
  }) async {
    draft = _copyDraft(
      draft,
      revision: expectedRevision + Int64.ONE,
      behavior: suggestion.replacementBehavior,
      source: suggestion.replacementSource,
      verification: null,
    );
    return draft;
  }

  @override
  Future<FeatureStudioDraft> rejectSuggestedChange({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioSuggestion suggestion,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
    required FeatureStudioVerification? expectedVerification,
  }) async => draft;

  @override
  Future<FeatureStudioSuggestion> suggestChange({
    required String draftId,
    required Int64 expectedRevision,
    required String guidance,
    required String suggestionId,
  }) async => FeatureStudioSuggestion(
    patchId: 'patch-golden',
    draftId: draftId,
    baseRevision: expectedRevision,
    summary: 'Add evidence and sharpen the expected outcome',
    replacementBehavior: FeatureStudioBehavior(
      scenarios: const [
        FeatureStudioScenario(
          scenarioId: 'company-brief',
          name: 'Create an evidence-backed company brief',
          given: 'A company name and research focus',
          when: 'The Feature runs',
          then: 'A concise sourced brief and ranked key risks are returned',
        ),
        FeatureStudioScenario(
          scenarioId: 'missing-evidence',
          name: 'Explain missing evidence',
          given: 'Reliable evidence cannot be found',
          when: 'The brief is prepared',
          then: 'The evidence gap is called out clearly',
        ),
      ],
    ),
    replacementSource: FeatureStudioSource(
      implementationProjectPath: draft.source.implementationProjectPath,
      scenarioProjectPath: draft.source.scenarioProjectPath,
      files: [
        ...draft.source.files.take(2),
        const FeatureStudioSourceFile(
          path: 'CompanyBrief/CompanyBriefFeature.cs',
          content:
              'public sealed class CompanyBriefFeature { public string Summarize() => "Evidence-backed brief"; }',
        ),
        const FeatureStudioSourceFile(
          path: 'CompanyBrief/EvidenceRanker.cs',
          content: 'public sealed class EvidenceRanker { }',
        ),
      ],
    ),
  );

  @override
  Future<FeatureStudioDraft> verifyDraft({
    required String draftId,
    required Int64 expectedRevision,
    required String idempotencyId,
    required FeatureStudioBehavior expectedBehavior,
    required FeatureStudioSource expectedSource,
  }) async {
    draft = _copyDraft(
      draft,
      revision: expectedRevision + Int64.ONE,
      verification: FeatureStudioVerification(
        releaseDigest: 'a' * 64,
        total: 12,
        passed: 12,
        failed: 0,
        skipped: 0,
        verifiedAt: DateTime.utc(2026, 7, 15, 10, 2),
      ),
    );
    return draft;
  }
}

FeatureStudioBehavior _editedBehavior() => FeatureStudioBehavior(
  scenarios: const [
    FeatureStudioScenario(
      scenarioId: 'company-brief',
      name: 'Create an updated company brief',
      given: 'A company name and research focus',
      when: 'The Feature runs',
      then: 'A concise sourced brief and key risks are returned',
    ),
  ],
);

FeatureStudioDraft _draft() => FeatureStudioDraft(
  draftId: 'draft-golden',
  originatingRequest: const FeatureStudioOriginatingRequest(
    operationId: 'operation-golden',
    conversationId: 'conversation-golden',
    text: 'Research Acme before tomorrow’s planning session',
  ),
  goal: 'Create a concise company brief with sources and key risks',
  status: FeatureStudioDraftStatus.draft,
  behavior: FeatureStudioBehavior(
    scenarios: const [
      FeatureStudioScenario(
        scenarioId: 'company-brief',
        name: 'Create a company brief',
        given: 'A company name and research focus',
        when: 'The Feature runs',
        then: 'A concise sourced brief and key risks are returned',
      ),
    ],
  ),
  source: FeatureStudioSource(
    implementationProjectPath: 'CompanyBrief/CompanyBrief.csproj',
    scenarioProjectPath: 'CompanyBrief.Tests/CompanyBrief.Tests.csproj',
    files: const [
      FeatureStudioSourceFile(
        path: 'CompanyBrief/CompanyBrief.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      FeatureStudioSourceFile(
        path: 'CompanyBrief.Tests/CompanyBrief.Tests.csproj',
        content: '<Project Sdk="Microsoft.NET.Sdk" />',
      ),
      FeatureStudioSourceFile(
        path: 'CompanyBrief/CompanyBriefFeature.cs',
        content: 'public sealed class CompanyBriefFeature { }',
      ),
    ],
  ),
  verification: null,
  revision: Int64(4),
  createdAt: DateTime.utc(2026, 7, 15, 10),
  updatedAt: DateTime.utc(2026, 7, 15, 10, 1),
);

FeatureStudioDraft _copyDraft(
  FeatureStudioDraft value, {
  Int64? revision,
  FeatureStudioBehavior? behavior,
  FeatureStudioSource? source,
  FeatureStudioVerification? verification,
}) => FeatureStudioDraft(
  draftId: value.draftId,
  originatingRequest: value.originatingRequest,
  goal: value.goal,
  status: value.status,
  behavior: behavior ?? value.behavior,
  source: source ?? value.source,
  verification: verification,
  revision: revision ?? value.revision,
  createdAt: value.createdAt,
  updatedAt: value.updatedAt.add(const Duration(minutes: 1)),
);
