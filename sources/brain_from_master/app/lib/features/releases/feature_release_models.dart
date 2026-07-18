import 'dart:convert';

import 'package:fixnum/fixnum.dart';

import '../shared/feature_grant_constraint_policy.dart';

export '../shared/feature_grant_constraint_policy.dart';

enum FeatureReleaseSourceKind { repository, runtimeAuthored }

class FeatureReleaseOriginatingRequest {
  const FeatureReleaseOriginatingRequest({
    required this.operationId,
    required this.conversationId,
    required this.text,
  });

  final String operationId;
  final String? conversationId;
  final String text;
}

class FeatureReleaseSourceFile {
  const FeatureReleaseSourceFile({required this.path, required this.content});

  final String path;
  final String content;
}

class FeatureReleaseSourceSnapshot {
  FeatureReleaseSourceSnapshot({
    required this.implementationProjectPath,
    required this.scenarioProjectPath,
    required List<FeatureReleaseSourceFile> files,
  }) : files = List.unmodifiable(files) {
    _requirePath(implementationProjectPath, 'implementationProjectPath');
    _requirePath(scenarioProjectPath, 'scenarioProjectPath');
    if (!implementationProjectPath.endsWith('.csproj') ||
        !scenarioProjectPath.endsWith('.csproj') ||
        files.isEmpty ||
        files.length > 64) {
      throw ArgumentError('Invalid Feature source snapshot.');
    }
    final paths = <String>{};
    var totalBytes = 0;
    for (final file in files) {
      _requirePath(file.path, 'sourcePath');
      if (!paths.add(file.path.toLowerCase())) {
        throw ArgumentError('Duplicate Feature source path.');
      }
      final byteCount = utf8.encode(file.content).length;
      if (byteCount > 1048576 || file.content.contains('\u0000')) {
        throw ArgumentError('Invalid Feature source file.');
      }
      totalBytes += byteCount;
    }
    if (totalBytes > 4194304 ||
        !paths.contains(implementationProjectPath.toLowerCase()) ||
        !paths.contains(scenarioProjectPath.toLowerCase())) {
      throw ArgumentError('Invalid Feature source snapshot.');
    }
  }

  final String implementationProjectPath;
  final String scenarioProjectPath;
  final List<FeatureReleaseSourceFile> files;

  bool exactlyMatches(FeatureReleaseSourceSnapshot other) =>
      implementationProjectPath == other.implementationProjectPath &&
      scenarioProjectPath == other.scenarioProjectPath &&
      _sameSourceFiles(files, other.files);
}

class FeatureReleaseVersion {
  FeatureReleaseVersion({
    required this.digest,
    required this.sourceReference,
    required this.sourceKind,
    required List<String> requestedCapabilityIds,
    required List<String> dependencies,
    required this.source,
  }) : requestedCapabilityIds = List.unmodifiable(requestedCapabilityIds),
       dependencies = List.unmodifiable(dependencies) {
    if (!_digestPattern.hasMatch(digest) ||
        !_sourceReferencePattern.hasMatch(sourceReference)) {
      throw ArgumentError('Invalid Feature Version identity.');
    }
    _requireUniqueIdentities(
      requestedCapabilityIds,
      'requestedCapabilityIds',
      maximumCount: 32,
    );
    _requireUniqueIdentities(dependencies, 'dependencies', maximumCount: 64);
  }

  final String digest;
  final String sourceReference;
  final FeatureReleaseSourceKind sourceKind;
  final List<String> requestedCapabilityIds;
  final List<String> dependencies;
  final FeatureReleaseSourceSnapshot source;

  String get sourceKindLabel => switch (sourceKind) {
    FeatureReleaseSourceKind.repository => 'Repository source',
    FeatureReleaseSourceKind.runtimeAuthored => 'Runtime-authored source',
  };

  bool exactlyMatches(FeatureReleaseVersion other) =>
      digest == other.digest &&
      sourceReference == other.sourceReference &&
      sourceKind == other.sourceKind &&
      _sameStrings(requestedCapabilityIds, other.requestedCapabilityIds) &&
      _sameStrings(dependencies, other.dependencies) &&
      source.exactlyMatches(other.source);
}

class FeatureReleaseGrant {
  const FeatureReleaseGrant({
    required this.capabilityId,
    required this.capabilityVersion,
    required this.provider,
    required this.connectionId,
    required this.constraintsJson,
    required this.constraintSummary,
  });

  final String capabilityId;
  final int capabilityVersion;
  final String? provider;
  final String? connectionId;
  final String constraintsJson;
  final String constraintSummary;
}

class FeatureReleaseDetails {
  FeatureReleaseDetails({
    required this.featureId,
    required this.installationId,
    required this.revision,
    required this.originatingRequest,
    required this.activeVersion,
    required this.previousVersion,
    required List<FeatureReleaseGrant> activeGrants,
    required List<String> subscriptions,
    required this.paused,
    required this.pauseReason,
  }) : activeGrants = List.unmodifiable(activeGrants),
       subscriptions = List.unmodifiable(subscriptions) {
    _requireIdentity(featureId, 'featureId', 128);
    _requireIdentity(installationId, 'installationId', 256);
    if (revision <= Int64.ZERO) {
      throw ArgumentError.value(revision, 'revision', 'Invalid revision.');
    }
    _requireIdentity(originatingRequest.operationId, 'operationId', 256);
    if (originatingRequest.conversationId case final conversationId?) {
      _requireIdentity(conversationId, 'conversationId', 256);
    }
    _requireText(originatingRequest.text, 'originatingRequest', 4096);
    if (previousVersion?.digest == activeVersion.digest ||
        (paused && previousVersion != null) ||
        paused != (pauseReason != null)) {
      throw ArgumentError('Invalid installed Feature state.');
    }
    if (pauseReason case final reason?) {
      _requireText(reason, 'pauseReason', 4096);
    }
    final grantIds = <String>{};
    for (final grant in activeGrants) {
      _requireIdentity(grant.capabilityId, 'capabilityId', 256);
      final expectedSummary = FeatureGrantConstraintPolicy.summarize(
        constraintsJson: grant.constraintsJson,
        capabilityId: grant.capabilityId,
      );
      if (grant.capabilityVersion <= 0 ||
          !grantIds.add(grant.capabilityId) ||
          expectedSummary == null ||
          grant.constraintSummary != expectedSummary ||
          (grant.provider == null) != (grant.connectionId == null)) {
        throw ArgumentError('Invalid Feature grant.');
      }
      if (grant.provider case final provider?) {
        _requireIdentity(provider, 'provider', 64);
      }
      if (grant.connectionId case final connectionId?) {
        _requireIdentity(connectionId, 'connectionId', 256);
      }
    }
    if (!_sameSets(grantIds, activeVersion.requestedCapabilityIds.toSet())) {
      throw ArgumentError('Feature grants do not match the active Version.');
    }
    if (subscriptions.isEmpty) {
      throw ArgumentError('Installed Feature subscriptions are required.');
    }
    _requireUniqueIdentities(subscriptions, 'subscriptions', maximumCount: 64);
  }

  final String featureId;
  final String installationId;
  final Int64 revision;
  final FeatureReleaseOriginatingRequest originatingRequest;
  final FeatureReleaseVersion activeVersion;
  final FeatureReleaseVersion? previousVersion;
  final List<FeatureReleaseGrant> activeGrants;
  final List<String> subscriptions;
  final bool paused;
  final String? pauseReason;

  bool get rollbackAvailable => previousVersion != null;
}

final RegExp _digestPattern = RegExp(r'^[0-9a-f]{64}$');
final RegExp _sourceReferencePattern = RegExp(r'^sha256:[0-9a-f]{64}$');

bool isCanonicalFeatureReleaseDigest(String value) =>
    _digestPattern.hasMatch(value);

void _requireIdentity(String value, String name, int maximumLength) {
  if (value.isEmpty ||
      value.length > maximumLength ||
      value.trim() != value ||
      value.runes.any(
        (character) => character < 32 || (character >= 127 && character <= 159),
      )) {
    throw ArgumentError.value(value, name, 'Invalid identity.');
  }
}

void _requireText(String value, String name, int maximumLength) {
  if (value.isEmpty ||
      value.length > maximumLength ||
      value.trim() != value ||
      value.runes.any(
        (character) => character < 32 || (character >= 127 && character <= 159),
      )) {
    throw ArgumentError.value(value, name, 'Invalid text.');
  }
}

void _requirePath(String value, String name) {
  if (value.isEmpty ||
      value.length > 240 ||
      value.trim() != value ||
      value.startsWith('/') ||
      value.contains('\\') ||
      value
          .split('/')
          .any(
            (segment) =>
                segment.isEmpty ||
                segment == '.' ||
                segment == '..' ||
                segment.trim() != segment,
          )) {
    throw ArgumentError.value(value, name, 'Invalid relative path.');
  }
}

void _requireUniqueIdentities(
  List<String> values,
  String name, {
  required int maximumCount,
}) {
  if (values.length > maximumCount) {
    throw ArgumentError.value(values, name, 'Too many values.');
  }
  final seen = <String>{};
  for (final value in values) {
    _requireIdentity(value, name, 256);
    if (!seen.add(value)) {
      throw ArgumentError.value(values, name, 'Duplicate value.');
    }
  }
}

bool _sameStrings(List<String> first, List<String> second) {
  if (first.length != second.length) return false;
  for (var index = 0; index < first.length; index++) {
    if (first[index] != second[index]) return false;
  }
  return true;
}

bool _sameSourceFiles(
  List<FeatureReleaseSourceFile> first,
  List<FeatureReleaseSourceFile> second,
) {
  if (first.length != second.length) return false;
  for (var index = 0; index < first.length; index++) {
    if (first[index].path != second[index].path ||
        first[index].content != second[index].content) {
      return false;
    }
  }
  return true;
}

bool _sameSets(Set<String> first, Set<String> second) =>
    first.length == second.length && first.containsAll(second);
