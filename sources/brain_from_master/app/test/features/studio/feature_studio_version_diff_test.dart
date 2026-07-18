import 'package:digitalbrain_flutter/features/studio/feature_studio_models.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('Version diff reports exact changed, added, and removed files', () {
    final diff = buildFeatureStudioVersionDiff(
      currentVersion: _version(
        digest: 'c' * 64,
        files: const [
          FeatureStudioSourceFile(path: 'b.cs', content: 'new b'),
          FeatureStudioSourceFile(path: 'c.cs', content: 'new c'),
        ],
      ),
      previousVersion: _version(
        digest: 'd' * 64,
        files: const [
          FeatureStudioSourceFile(path: 'a.cs', content: 'old a'),
          FeatureStudioSourceFile(path: 'b.cs', content: 'old b'),
        ],
      ),
    );

    expect(diff.status, FeatureStudioVersionDiffStatus.compared);
    expect(diff.files.map((file) => (file.kind, file.path)).toList(), [
      (FeatureStudioVersionFileChangeKind.changed, 'b.cs'),
      (FeatureStudioVersionFileChangeKind.added, 'c.cs'),
      (FeatureStudioVersionFileChangeKind.removed, 'a.cs'),
    ]);
    expect(diff.files.first.previousContent, 'old b');
    expect(diff.files.first.currentContent, 'new b');
    expect(diff.files[1].previousContent, isNull);
    expect(diff.files[2].currentContent, isNull);
  });

  test('Version diff distinguishes no previous Version from missing source', () {
    final current = _version(
      digest: 'c' * 64,
      files: const [FeatureStudioSourceFile(path: 'a.cs', content: 'a')],
    );

    expect(
      buildFeatureStudioVersionDiff(
        currentVersion: current,
        previousVersion: null,
      ).status,
      FeatureStudioVersionDiffStatus.noPreviousVersion,
    );
    expect(
      buildFeatureStudioVersionDiff(
        currentVersion: current,
        previousVersion: FeatureStudioVersion(
          digest: 'd' * 64,
          sourceReference:
              'sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd',
          requestedCapabilityIds: const [],
          dependencies: const [],
          source: null,
        ),
      ).status,
      FeatureStudioVersionDiffStatus.sourceUnavailable,
    );
  });

  test('Version diff includes both project-coordinate changes', () {
    final diff = buildFeatureStudioVersionDiff(
      currentVersion: _version(
        digest: 'c' * 64,
        implementationProjectPath: 'Current/Feature.csproj',
        scenarioProjectPath: 'Current.Tests/Feature.Tests.csproj',
        files: const [],
      ),
      previousVersion: _version(
        digest: 'd' * 64,
        implementationProjectPath: 'Previous/Feature.csproj',
        scenarioProjectPath: 'Previous.Tests/Feature.Tests.csproj',
        files: const [],
      ),
    );

    expect(diff.coordinateChanges.map((change) => change.kind), [
      FeatureStudioVersionCoordinateKind.implementationProjectPath,
      FeatureStudioVersionCoordinateKind.scenarioProjectPath,
    ]);
    expect(
      diff.coordinateChanges.first.previousValue,
      'Previous/Feature.csproj',
    );
    expect(diff.coordinateChanges.first.currentValue, 'Current/Feature.csproj');
  });
}

FeatureStudioVersion _version({
  required String digest,
  required List<FeatureStudioSourceFile> files,
  String implementationProjectPath = 'Feature/Feature.csproj',
  String scenarioProjectPath = 'Feature.Tests/Feature.Tests.csproj',
}) => FeatureStudioVersion(
  digest: digest,
  sourceReference: 'sha256:$digest',
  requestedCapabilityIds: const [],
  dependencies: const [],
  source: FeatureStudioSource(
    implementationProjectPath: implementationProjectPath,
    scenarioProjectPath: scenarioProjectPath,
    files: files,
  ),
);
