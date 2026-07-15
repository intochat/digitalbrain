import 'dart:convert';

import 'feature_studio_models.dart';

const int maximumFeatureScenarios = 32;
const int maximumFeatureBehaviorUtf8Bytes = 65_536;
const int maximumFeatureSourceFiles = 64;
const int maximumFeatureSourceFileUtf8Bytes = 1_048_576;
const int maximumFeatureSourceUtf8Bytes = 4_194_304;

List<String> validateFeatureStudioBehavior(FeatureStudioBehavior behavior) {
  final errors = <String>[];
  final scenarios = behavior.scenarios;
  if (scenarios.isEmpty || scenarios.length > maximumFeatureScenarios) {
    errors.add('Behavior must contain between 1 and 32 Scenarios.');
  }
  final ids = <String>{};
  var aggregateBytes = 0;
  for (final scenario in scenarios) {
    final fields = <(String, String, int)>[
      ('A Scenario reference', scenario.scenarioId, 128),
      ('Scenario name', scenario.name, 256),
      ('Given', scenario.given, 4096),
      ('When', scenario.when, 4096),
      ('Then', scenario.then, 4096),
    ];
    for (final (label, value, maximumLength) in fields) {
      if (!_isCanonicalText(value, maximumLength)) {
        errors.add('$label is invalid.');
      }
      aggregateBytes += utf8.encode(value).length;
    }
    if (!ids.add(scenario.scenarioId)) {
      errors.add('Scenarios must be distinct.');
    }
  }
  if (aggregateBytes > maximumFeatureBehaviorUtf8Bytes) {
    errors.add('Behavior is too large.');
  }
  return List.unmodifiable(errors);
}

List<String> validateFeatureStudioSource(FeatureStudioSource source) {
  final errors = <String>[];
  final implementationPathValid = _isCanonicalSourcePath(
    source.implementationProjectPath,
  );
  final scenarioPathValid = _isCanonicalSourcePath(source.scenarioProjectPath);
  if (!implementationPathValid ||
      !_ordinalIgnoreCaseKey(
        source.implementationProjectPath,
      ).endsWith('.CSPROJ')) {
    errors.add('The implementation project path is invalid.');
  }
  if (!scenarioPathValid ||
      !_ordinalIgnoreCaseKey(source.scenarioProjectPath).endsWith('.CSPROJ')) {
    errors.add('The Scenario project path is invalid.');
  }
  if (source.files.isEmpty || source.files.length > maximumFeatureSourceFiles) {
    errors.add('Code must contain between 1 and 64 files.');
  }
  final collisionPaths = <String>{};
  final exactPaths = <String>{};
  var aggregateBytes = 0;
  for (final file in source.files) {
    if (!_isCanonicalSourcePath(file.path)) {
      errors.add('A Code file path is invalid.');
    }
    if (!collisionPaths.add(_ordinalIgnoreCaseKey(file.path))) {
      errors.add('Code file paths must be unique.');
    }
    exactPaths.add(file.path);
    if (file.content.contains('\u0000')) {
      errors.add('Code files cannot contain null characters.');
    }
    final contentBytes = utf8.encode(file.content).length;
    aggregateBytes += contentBytes;
    if (contentBytes > maximumFeatureSourceFileUtf8Bytes) {
      errors.add('A Code file is too large.');
    }
  }
  if (aggregateBytes > maximumFeatureSourceUtf8Bytes) {
    errors.add('Code is too large.');
  }
  if (!exactPaths.contains(source.implementationProjectPath) ||
      !exactPaths.contains(source.scenarioProjectPath)) {
    errors.add('Both declared project files must be present.');
  }
  return List.unmodifiable(errors);
}

bool _isCanonicalText(String value, int maximumLength) =>
    value.isNotEmpty &&
    value.length <= maximumLength &&
    value.trim() == value &&
    !value.runes.any(_isControl);

bool _isCanonicalSourcePath(String value) {
  if (value.isEmpty ||
      value.length > 240 ||
      value.contains('\\') ||
      value.startsWith('/') ||
      RegExp(r'^[A-Za-z]:').hasMatch(value)) {
    return false;
  }
  return value.split('/').every(_isPortablePathSegment);
}

bool _isPortablePathSegment(String segment) {
  if (segment.isEmpty ||
      segment == '.' ||
      segment == '..' ||
      segment.trim() != segment ||
      segment.endsWith('.') ||
      segment.runes.any(_isControl) ||
      RegExp(r'''[<>:"|?*]''').hasMatch(segment)) {
    return false;
  }
  const reserved = {
    'CON',
    'PRN',
    'AUX',
    'NUL',
    'COM1',
    'COM2',
    'COM3',
    'COM¹',
    'COM²',
    'COM³',
    'COM4',
    'COM5',
    'COM6',
    'COM7',
    'COM8',
    'COM9',
    'LPT1',
    'LPT2',
    'LPT3',
    'LPT¹',
    'LPT²',
    'LPT³',
    'LPT4',
    'LPT5',
    'LPT6',
    'LPT7',
    'LPT8',
    'LPT9',
  };
  return !reserved.contains(segment.split('.').first.toUpperCase());
}

bool _isControl(int rune) => rune < 32 || (rune >= 127 && rune <= 159);

String _ordinalIgnoreCaseKey(String value) => value.toUpperCase();
