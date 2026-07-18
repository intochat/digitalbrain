import 'package:digitalbrain_flutter/features/studio/feature_studio_models.dart';
import 'package:digitalbrain_flutter/features/studio/feature_studio_validation.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('Feature Studio validation', () {
    test(
      'Behavior enforces count, canonical text, distinct references, and UTF-8',
      () {
        expect(validateFeatureStudioBehavior(_behavior()), isEmpty);
        expect(
          validateFeatureStudioBehavior(
            FeatureStudioBehavior(scenarios: const []),
          ),
          isNotEmpty,
        );
        expect(
          validateFeatureStudioBehavior(
            FeatureStudioBehavior(
              scenarios: List.generate(33, (index) => _scenario('$index')),
            ),
          ),
          isNotEmpty,
        );
        expect(
          validateFeatureStudioBehavior(
            FeatureStudioBehavior(
              scenarios: [_scenario('brief', name: 'Bad\u0085name')],
            ),
          ),
          isNotEmpty,
        );
        expect(
          validateFeatureStudioBehavior(
            FeatureStudioBehavior(scenarios: [_scenario('Bad\u0085reference')]),
          ),
          ['A Scenario reference is invalid.'],
        );
        expect(
          validateFeatureStudioBehavior(
            FeatureStudioBehavior(
              scenarios: const [
                FeatureStudioScenario(
                  scenarioId: 'same',
                  name: 'First',
                  given: 'Input exists',
                  when: 'The Feature runs',
                  then: 'A result appears',
                ),
                FeatureStudioScenario(
                  scenarioId: 'same',
                  name: 'Second',
                  given: 'Input exists',
                  when: 'The Feature runs',
                  then: 'A result appears',
                ),
              ],
            ),
          ),
          ['Scenarios must be distinct.'],
        );
        expect(
          validateFeatureStudioBehavior(
            FeatureStudioBehavior(
              scenarios: [_scenario('Brief'), _scenario('brief')],
            ),
          ),
          isEmpty,
        );
        expect(
          validateFeatureStudioBehavior(
            FeatureStudioBehavior(
              scenarios: List.generate(
                6,
                (index) => FeatureStudioScenario(
                  scenarioId: '$index',
                  name: 'Scenario $index',
                  given: _filled('é', 4096),
                  when: _filled('é', 4096),
                  then: _filled('é', 4096),
                ),
              ),
            ),
          ),
          isNotEmpty,
        );
      },
    );

    test(
      'Source enforces portable unique paths, projects, and byte limits',
      () {
        expect(validateFeatureStudioSource(_source()), isEmpty);
        expect(
          validateFeatureStudioSource(
            _source(
              extraFiles: const [
                FeatureStudioSourceFile(
                  path: 'feature/feature.csproj',
                  content: 'duplicate',
                ),
              ],
            ),
          ),
          isNotEmpty,
        );
        for (final reserved in [
          'COM¹',
          'COM²',
          'COM³',
          'LPT¹',
          'LPT²',
          'LPT³',
        ]) {
          expect(
            validateFeatureStudioSource(
              _source(
                extraFiles: [
                  FeatureStudioSourceFile(
                    path: 'Feature/$reserved.cs',
                    content: 'unsafe',
                  ),
                ],
              ),
            ),
            isNotEmpty,
          );
        }
        expect(
          validateFeatureStudioSource(
            _source(
              extraFiles: const [
                FeatureStudioSourceFile(path: 'Feature/σ.cs', content: 'first'),
                FeatureStudioSourceFile(
                  path: 'Feature/ς.cs',
                  content: 'second',
                ),
              ],
            ),
          ),
          isNotEmpty,
        );
        expect(
          validateFeatureStudioSource(
            _source(
              extraFiles: const [
                FeatureStudioSourceFile(
                  path: 'Feature/Bad\u0085Path.cs',
                  content: 'unsafe',
                ),
              ],
            ),
          ),
          isNotEmpty,
        );
        expect(
          validateFeatureStudioSource(
            _source(implementationProjectPath: 'Missing/Missing.csproj'),
          ),
          isNotEmpty,
        );
        expect(
          validateFeatureStudioSource(
            _source(
              extraFiles: const [
                FeatureStudioSourceFile(
                  path: '../outside.cs',
                  content: 'unsafe',
                ),
              ],
            ),
          ),
          isNotEmpty,
        );
        expect(
          validateFeatureStudioSource(
            _source(
              extraFiles: [
                FeatureStudioSourceFile(
                  path: 'Feature/Large.cs',
                  content: _filled('x', 1024 * 1024 + 1),
                ),
              ],
            ),
          ),
          isNotEmpty,
        );
        expect(
          validateFeatureStudioSource(
            _source(
              extraFiles: List.generate(
                5,
                (index) => FeatureStudioSourceFile(
                  path: 'Feature/Large$index.cs',
                  content: _filled('x', 1024 * 1024),
                ),
              ),
            ),
          ),
          isNotEmpty,
        );
        expect(
          validateFeatureStudioSource(
            _source(
              extraFiles: const [
                FeatureStudioSourceFile(
                  path: 'Feature/Null.cs',
                  content: 'bad\u0000content',
                ),
              ],
            ),
          ),
          isNotEmpty,
        );
      },
    );
  });
}

FeatureStudioBehavior _behavior() =>
    FeatureStudioBehavior(scenarios: [_scenario('brief')]);

String _filled(String value, int count) => List.filled(count, value).join();

FeatureStudioScenario _scenario(String id, {String name = 'Create a brief'}) =>
    FeatureStudioScenario(
      scenarioId: id,
      name: name,
      given: 'A company name',
      when: 'The Feature runs',
      then: 'A concise brief is returned',
    );

FeatureStudioSource _source({
  String implementationProjectPath = 'Feature/Feature.csproj',
  Iterable<FeatureStudioSourceFile> extraFiles = const [],
}) => FeatureStudioSource(
  implementationProjectPath: implementationProjectPath,
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
    const FeatureStudioSourceFile(
      path: 'Feature/Feature.cs',
      content: 'public sealed class Feature {\n}',
    ),
    ...extraFiles,
  ],
);
