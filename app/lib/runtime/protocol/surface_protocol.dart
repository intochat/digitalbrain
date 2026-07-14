import 'dart:convert';
import 'dart:typed_data';

const int digitalBrainUiProtocolVersion = 2;
const String digitalBrainSurfaceSchema = 'digitalbrain.surface';
const int digitalBrainSurfaceSchemaVersion = 2;
const int digitalBrainActionSchemaVersion = 1;
const int defaultMaximumSurfaceBytes = 1024 * 1024;

class ClientCapabilities {
  const ClientCapabilities({
    this.protocolVersions = const {digitalBrainUiProtocolVersion},
    this.payloadKinds = const {'widgetTree', 'rfw', 'native'},
    this.widgetVocabularyVersion = 2,
    this.maximumPayloadBytes = defaultMaximumSurfaceBytes,
    this.supportsBinaryRfw = false,
    this.nativeFeatures = const {
      'typed-actions',
      'feed-reset',
      'feed-ack',
      'ino-conversation',
      'feature-approval',
    },
  });

  final Set<int> protocolVersions;
  final Set<String> payloadKinds;
  final int widgetVocabularyVersion;
  final int maximumPayloadBytes;
  final bool supportsBinaryRfw;
  final Set<String> nativeFeatures;

  Set<String> get names => {
    for (final version in protocolVersions) 'ui.protocol.v$version',
    'ui.widget-vocabulary.v$widgetVocabularyVersion',
    for (final kind in payloadKinds) 'ui.payload.$kind',
    if (supportsBinaryRfw) 'ui.rfw.binary',
    ...nativeFeatures.map((feature) => 'ui.native.$feature'),
  };

  Map<String, Object?> toJson() => {
    'protocolVersions': protocolVersions.toList()..sort(),
    'payloadKinds': payloadKinds.toList()..sort(),
    'widgetVocabularyVersion': widgetVocabularyVersion,
    'maximumPayloadBytes': maximumPayloadBytes,
    'supportsBinaryRfw': supportsBinaryRfw,
    'nativeFeatures': nativeFeatures.toList()..sort(),
  };
}

class CauseRef {
  const CauseRef({required this.kind, required this.id});

  final String kind;
  final String id;

  factory CauseRef.fromJson(Object? value) {
    final json = _object(value, 'cause');
    final kind = _boundedString(json, 'kind', maxLength: 64);
    if (!_causeKinds.contains(kind)) {
      throw FormatException('Unsupported surface cause kind "$kind".');
    }
    return CauseRef(kind: kind, id: _boundedString(json, 'id', maxLength: 256));
  }

  Map<String, Object?> toJson() => {'kind': kind, 'id': id};
}

const Set<String> _causeKinds = {
  'conversation',
  'command',
  'event',
  'workflow-transition',
  'effect',
  'tool-invocation',
  'oauth-flow',
  'model-call',
  'surface',
};

class SurfaceAudience {
  const SurfaceAudience({required this.kind, required this.id});

  final String kind;
  final String id;

  bool get isPrivate => kind != 'public';

  factory SurfaceAudience.fromJson(Object? value) {
    final json = _object(value, 'audience');
    final kind = _boundedString(json, 'kind', maxLength: 32);
    if (kind != 'actor' && kind != 'owner' && kind != 'public') {
      throw FormatException('Unsupported surface audience kind "$kind".');
    }
    final id = _boundedString(
      json,
      'id',
      maxLength: 256,
      allowEmpty: kind == 'public',
    );
    if (kind != 'public' && id.isEmpty) {
      throw const FormatException('Private surface audience id is required.');
    }
    return SurfaceAudience(kind: kind, id: id);
  }

  Map<String, Object?> toJson() => {'kind': kind, 'id': id};
}

sealed class SurfacePayload {
  const SurfacePayload();

  String get kind;

  Map<String, Object?> toJson();

  factory SurfacePayload.fromJson(
    Object? value, {
    required ClientCapabilities capabilities,
    Uri? oauthStartOrigin,
  }) {
    final json = _object(value, 'payload');
    final kind = _boundedString(json, 'kind', maxLength: 32);
    if (!capabilities.payloadKinds.contains(kind)) {
      throw UnsupportedSurfaceCapability('ui.payload.$kind');
    }
    return switch (kind) {
      'widgetTree' => WidgetTreeSurfacePayload.fromJson(json),
      'rfw' => RfwSurfacePayload.fromJson(
        json,
        supportsBinary: capabilities.supportsBinaryRfw,
      ),
      'native' => _nativeSurfacePayloadFromJson(
        json,
        oauthStartOrigin: oauthStartOrigin,
      ),
      _ => throw FormatException('Unsupported surface payload "$kind".'),
    };
  }
}

class WidgetTreeSurfacePayload extends SurfacePayload {
  const WidgetTreeSurfacePayload({required this.tree, required this.data});

  @override
  String get kind => 'widgetTree';

  final Map<String, Object?> tree;
  final Map<String, Object?> data;

  factory WidgetTreeSurfacePayload.fromJson(Map<String, Object?> json) {
    final tree = _safeObject(json['tree'], 'payload.tree');
    final data = json['data'] == null
        ? const <String, Object?>{}
        : _safeObject(json['data'], 'payload.data');
    return WidgetTreeSurfacePayload(tree: tree, data: data);
  }

  @override
  Map<String, Object?> toJson() => {'kind': kind, 'tree': tree, 'data': data};
}

class RfwSurfacePayload extends SurfacePayload {
  const RfwSurfacePayload({
    required this.rootWidget,
    required this.data,
    this.libraryBlob,
    this.libraryText,
  });

  @override
  String get kind => 'rfw';

  final String rootWidget;
  final Map<String, Object?> data;
  final Uint8List? libraryBlob;
  final String? libraryText;

  bool get isBinary => libraryBlob != null;

  factory RfwSurfacePayload.fromJson(
    Map<String, Object?> json, {
    required bool supportsBinary,
  }) {
    final rootWidget = _boundedString(json, 'rootWidget', maxLength: 128);
    final data = json['data'] == null
        ? const <String, Object?>{}
        : _safeObject(json['data'], 'payload.data', allowNull: false);
    final blob = json['libraryBlob'];
    final text = json['libraryText'];
    if ((blob == null) == (text == null)) {
      throw const FormatException(
        'RFW payload must contain exactly one of libraryBlob or libraryText.',
      );
    }
    if (blob != null) {
      if (!supportsBinary) {
        throw const UnsupportedSurfaceCapability('ui.rfw.binary');
      }
      final encoded = _stringValue(blob, 'payload.libraryBlob');
      try {
        final bytes = base64Decode(encoded);
        if (bytes.isEmpty) {
          throw const FormatException('RFW binary library cannot be empty.');
        }
        return RfwSurfacePayload(
          rootWidget: rootWidget,
          data: data,
          libraryBlob: Uint8List.fromList(bytes),
        );
      } on FormatException {
        rethrow;
      } catch (_) {
        throw const FormatException('RFW binary library is not valid base64.');
      }
    }
    final source = _stringValue(text, 'payload.libraryText');
    if (source.trim().isEmpty) {
      throw const FormatException('RFW text library cannot be empty.');
    }
    return RfwSurfacePayload(
      rootWidget: rootWidget,
      data: data,
      libraryText: source,
    );
  }

  @override
  Map<String, Object?> toJson() => {
    'kind': kind,
    'rootWidget': rootWidget,
    'data': data,
    if (libraryBlob case final bytes?) 'libraryBlob': base64Encode(bytes),
    if (libraryText != null) 'libraryText': libraryText,
  };
}

class NativeSurfacePayload extends SurfacePayload {
  const NativeSurfacePayload({required this.nativeKind, required this.data});

  @override
  String get kind => 'native';

  final String nativeKind;
  final Map<String, Object?> data;

  factory NativeSurfacePayload.fromJson(Map<String, Object?> json) =>
      NativeSurfacePayload(
        nativeKind: _boundedString(json, 'nativeKind', maxLength: 64),
        data: json['data'] == null
            ? const <String, Object?>{}
            : _safeObject(json['data'], 'payload.data'),
      );

  @override
  Map<String, Object?> toJson() => {
    'kind': kind,
    'nativeKind': nativeKind,
    'data': data,
  };
}

class FeatureApprovalCapabilityBinding {
  const FeatureApprovalCapabilityBinding({
    required this.capabilityId,
    required this.capabilityVersion,
    required this.constraints,
    this.provider,
    this.providerConnectionId,
  });

  final String capabilityId;
  final int capabilityVersion;
  final String? provider;
  final String? providerConnectionId;
  final Map<String, Object?> constraints;

  factory FeatureApprovalCapabilityBinding.fromJson(Object? value) {
    final json = _safeObject(value, 'payload.data.capabilityBindings[]');
    _demandOnlyKeys(json, const {
      'capabilityId',
      'capabilityVersion',
      'provider',
      'providerConnectionId',
      'constraints',
    }, 'payload.data.capabilityBindings[]');
    final version = json['capabilityVersion'];
    if (version is! int || version < 1) {
      throw const FormatException('Capability version must be positive.');
    }
    return FeatureApprovalCapabilityBinding(
      capabilityId: _boundedString(json, 'capabilityId', maxLength: 256),
      capabilityVersion: version,
      provider: _nullableBoundedString(json, 'provider', 64),
      providerConnectionId: _nullableBoundedString(
        json,
        'providerConnectionId',
        256,
      ),
      constraints: _safeObject(json['constraints'], 'constraints'),
    );
  }

  Map<String, Object?> toJson() => {
    'capabilityId': capabilityId,
    'capabilityVersion': capabilityVersion,
    'provider': provider,
    'providerConnectionId': providerConnectionId,
    'constraints': constraints,
  };
}

class FeatureApprovalSurfacePayload extends SurfacePayload {
  const FeatureApprovalSurfacePayload({
    required this.title,
    required this.installationId,
    required this.approvalId,
    required this.releaseDigest,
    required this.sourceReference,
    required this.sourceKind,
    required this.requestedCapabilities,
    required this.addedCapabilities,
    required this.removedCapabilities,
    required this.capabilityBindings,
    required this.revision,
  });

  static const String nativeKindName = 'featureApproval';

  @override
  String get kind => 'native';

  String get nativeKind => nativeKindName;
  final String title;
  final String installationId;
  final String approvalId;
  final String releaseDigest;
  final String sourceReference;
  final String sourceKind;
  final List<String> requestedCapabilities;
  final List<String> addedCapabilities;
  final List<String> removedCapabilities;
  final List<FeatureApprovalCapabilityBinding> capabilityBindings;
  final int revision;

  factory FeatureApprovalSurfacePayload.fromJson(Map<String, Object?> json) {
    if (_boundedString(json, 'nativeKind', maxLength: 64) != nativeKindName) {
      throw const FormatException('Unsupported Feature approval surface.');
    }
    final data = _safeObject(json['data'], 'payload.data');
    _demandOnlyKeys(data, const {
      'title',
      'installationId',
      'approvalId',
      'releaseDigest',
      'sourceReference',
      'sourceKind',
      'requestedCapabilities',
      'addedCapabilities',
      'removedCapabilities',
      'capabilityBindings',
      'revision',
    }, 'payload.data');
    final revision = data['revision'];
    final bindings = data['capabilityBindings'];
    if (revision is! int ||
        revision < 1 ||
        bindings is! List ||
        bindings.length > 32) {
      throw const FormatException('Feature approval data is not bounded.');
    }
    return FeatureApprovalSurfacePayload(
      title: _boundedString(data, 'title', maxLength: 128),
      installationId: _boundedString(data, 'installationId', maxLength: 256),
      approvalId: _boundedString(data, 'approvalId', maxLength: 256),
      releaseDigest: _boundedString(data, 'releaseDigest', maxLength: 128),
      sourceReference: _boundedString(data, 'sourceReference', maxLength: 256),
      sourceKind: _boundedString(data, 'sourceKind', maxLength: 64),
      requestedCapabilities: _boundedStringList(data, 'requestedCapabilities'),
      addedCapabilities: _boundedStringList(data, 'addedCapabilities'),
      removedCapabilities: _boundedStringList(data, 'removedCapabilities'),
      capabilityBindings: List.unmodifiable(
        bindings.map(FeatureApprovalCapabilityBinding.fromJson),
      ),
      revision: revision,
    );
  }

  @override
  Map<String, Object?> toJson() => {
    'kind': kind,
    'nativeKind': nativeKind,
    'data': {
      'title': title,
      'installationId': installationId,
      'approvalId': approvalId,
      'releaseDigest': releaseDigest,
      'sourceReference': sourceReference,
      'sourceKind': sourceKind,
      'requestedCapabilities': requestedCapabilities,
      'addedCapabilities': addedCapabilities,
      'removedCapabilities': removedCapabilities,
      'capabilityBindings': capabilityBindings
          .map((binding) => binding.toJson())
          .toList(),
      'revision': revision,
    },
  };
}

enum InoConversationRole {
  user,
  assistant;

  static InoConversationRole fromWire(String value) => switch (value) {
    'user' => user,
    'assistant' => assistant,
    _ => throw FormatException('Unsupported INO conversation role "$value".'),
  };
}

enum InoConversationTurnState {
  sending,
  queued,
  running,
  responding,
  awaitingApproval,
  awaitingAuthorization,
  retryScheduled,
  succeeded,
  failed,
  outcomeUnknown,
  cancelled;

  String get wire => switch (this) {
    sending => 'sending',
    queued => 'queued',
    running => 'running',
    responding => 'responding',
    awaitingApproval => 'awaiting-approval',
    awaitingAuthorization => 'awaiting-authorization',
    retryScheduled => 'retry-scheduled',
    succeeded => 'succeeded',
    failed => 'failed',
    outcomeUnknown => 'outcome-unknown',
    cancelled => 'cancelled',
  };

  static InoConversationTurnState fromWire(String value) => switch (value) {
    'sending' => sending,
    'queued' => queued,
    'running' => running,
    'responding' => responding,
    'awaiting-approval' => awaitingApproval,
    'awaiting-authorization' => awaitingAuthorization,
    'retry-scheduled' => retryScheduled,
    'succeeded' => succeeded,
    'failed' => failed,
    'outcome-unknown' => outcomeUnknown,
    'cancelled' => cancelled,
    _ => throw FormatException(
      'Unsupported INO conversation turn state "$value".',
    ),
  };
}

enum InoConversationOperationState {
  queued,
  running,
  responding,
  awaitingApproval,
  awaitingAuthorization,
  retryScheduled,
  succeeded,
  failed,
  outcomeUnknown,
  cancelled;

  String get wire => switch (this) {
    queued => 'queued',
    running => 'running',
    responding => 'responding',
    awaitingApproval => 'awaiting-approval',
    awaitingAuthorization => 'awaiting-authorization',
    retryScheduled => 'retry-scheduled',
    succeeded => 'succeeded',
    failed => 'failed',
    outcomeUnknown => 'outcome-unknown',
    cancelled => 'cancelled',
  };

  bool get isTerminal =>
      this == succeeded ||
      this == failed ||
      this == outcomeUnknown ||
      this == cancelled;

  static InoConversationOperationState fromWire(String value) =>
      switch (value) {
        'queued' => queued,
        'running' => running,
        'responding' => responding,
        'awaiting-approval' => awaitingApproval,
        'awaiting-authorization' => awaitingAuthorization,
        'retry-scheduled' => retryScheduled,
        'succeeded' => succeeded,
        'failed' => failed,
        'outcome-unknown' => outcomeUnknown,
        'cancelled' => cancelled,
        _ => throw FormatException(
          'Unsupported INO conversation operation state "$value".',
        ),
      };
}

enum InoConversationOperationPhase {
  accepted,
  queued,
  running,
  awaitingApproval,
  approved,
  applyingEffect,
  awaitingAuthorization,
  retryScheduled,
  succeeded,
  failed,
  outcomeUnknown,
  cancelled;

  String get wire => switch (this) {
    accepted => 'accepted',
    queued => 'queued',
    running => 'running',
    awaitingApproval => 'awaiting-approval',
    approved => 'approved',
    applyingEffect => 'applying-effect',
    awaitingAuthorization => 'awaiting-authorization',
    retryScheduled => 'retry-scheduled',
    succeeded => 'succeeded',
    failed => 'failed',
    outcomeUnknown => 'outcome-unknown',
    cancelled => 'cancelled',
  };

  static InoConversationOperationPhase fromWire(String value) =>
      switch (value) {
        'accepted' => accepted,
        'queued' => queued,
        'running' => running,
        'awaiting-approval' => awaitingApproval,
        'approved' => approved,
        'applying-effect' => applyingEffect,
        'awaiting-authorization' => awaitingAuthorization,
        'retry-scheduled' => retryScheduled,
        'succeeded' => succeeded,
        'failed' => failed,
        'outcome-unknown' => outcomeUnknown,
        'cancelled' => cancelled,
        _ => throw FormatException(
          'Unsupported INO conversation operation phase "$value".',
        ),
      };
}

class InoConversationMessage {
  const InoConversationMessage({
    required this.turnKey,
    required this.role,
    required this.text,
    required this.state,
  });

  final String turnKey;
  final InoConversationRole role;
  final String text;
  final InoConversationTurnState state;

  factory InoConversationMessage.fromJson(Object? value) {
    final json = _safeObject(value, 'payload.data.messages[]');
    _demandOnlyKeys(json, const {
      'turnKey',
      'role',
      'text',
      'state',
    }, 'payload.data.messages[]');
    final turnKey = _boundedString(json, 'turnKey', maxLength: 64);
    if (!RegExp(r'^turn-[a-z0-9-]{1,48}$').hasMatch(turnKey)) {
      throw const FormatException(
        'payload.data.messages[].turnKey has an invalid format.',
      );
    }
    return InoConversationMessage(
      turnKey: turnKey,
      role: InoConversationRole.fromWire(
        _boundedString(json, 'role', maxLength: 16),
      ),
      text: _boundedString(json, 'text', maxLength: 32 * 1024),
      state: InoConversationTurnState.fromWire(
        _boundedString(json, 'state', maxLength: 32),
      ),
    );
  }

  Map<String, Object?> toJson() => {
    'turnKey': turnKey,
    'role': role.name,
    'text': text,
    'state': state.wire,
  };
}

class InoConversationOperation {
  const InoConversationOperation({
    required this.state,
    required this.retryable,
    this.operationId = '',
    this.phase = InoConversationOperationPhase.queued,
    this.version = 0,
    this.safeReason,
    this.action,
    this.approvalId,
  });

  final String operationId;
  final InoConversationOperationPhase phase;
  final int version;
  final InoConversationOperationState state;
  final bool retryable;
  final String? safeReason;
  final InoConversationAction? action;
  final String? approvalId;

  factory InoConversationOperation.fromJson(
    Object? value, {
    Uri? oauthStartOrigin,
  }) {
    final json = _safeObject(value, 'payload.data.operation');
    _demandOnlyKeys(json, const {
      'operationId',
      'phase',
      'version',
      'state',
      'safeReason',
      'retryable',
      'action',
      'approvalId',
    }, 'payload.data.operation');
    const metadataKeys = {'operationId', 'phase', 'version'};
    final metadataCount = metadataKeys.where(json.containsKey).length;
    final isLegacy = metadataCount == 0;
    if (metadataCount != 0 && metadataCount != metadataKeys.length) {
      throw const FormatException(
        'payload.data.operation metadata must be complete.',
      );
    }
    final String operationId;
    final int version;
    if (isLegacy) {
      operationId = '';
      version = 0;
    } else {
      operationId = _boundedString(json, 'operationId', maxLength: 128);
      if (!RegExp(r'^[a-z][a-z0-9-]{2,127}$').hasMatch(operationId)) {
        throw const FormatException(
          'payload.data.operation.operationId has an invalid format.',
        );
      }
      final versionValue = json['version'];
      if (versionValue is! int ||
          versionValue < 1 ||
          versionValue > 9007199254740991) {
        throw const FormatException(
          'payload.data.operation.version must be a positive safe integer.',
        );
      }
      version = versionValue;
    }
    final retryable = json['retryable'];
    if (retryable is! bool) {
      throw const FormatException(
        'payload.data.operation.retryable must be a boolean.',
      );
    }
    final safeReason = json['safeReason'];
    final String? normalizedReason;
    if (safeReason == null) {
      normalizedReason = null;
    } else if (safeReason is String) {
      normalizedReason = safeReason.trim();
    } else {
      throw const FormatException(
        'payload.data.operation.safeReason must be a string.',
      );
    }
    if (normalizedReason != null &&
        (normalizedReason.isEmpty || normalizedReason.length > 512)) {
      throw const FormatException(
        'payload.data.operation.safeReason has an invalid length.',
      );
    }
    final state = InoConversationOperationState.fromWire(
      _boundedString(json, 'state', maxLength: 32),
    );
    if (isLegacy && state == InoConversationOperationState.awaitingApproval) {
      throw const FormatException(
        'Legacy operations cannot carry approval authority.',
      );
    }
    final approvalValue = json['approvalId'];
    final String? approvalId;
    if (approvalValue == null) {
      approvalId = null;
    } else if (approvalValue is String &&
        approvalValue.length >= 3 &&
        approvalValue.length <= 128 &&
        RegExp(r'^[a-z][a-z0-9-]{2,127}$').hasMatch(approvalValue)) {
      approvalId = approvalValue;
    } else {
      throw const FormatException(
        'payload.data.operation.approvalId has an invalid format.',
      );
    }
    if (state == InoConversationOperationState.awaitingApproval &&
        approvalId == null) {
      throw const FormatException(
        'payload.data.operation.approvalId is required while awaiting approval.',
      );
    }
    if (state != InoConversationOperationState.awaitingApproval &&
        approvalId != null) {
      throw const FormatException(
        'payload.data.operation.approvalId is only valid while awaiting approval.',
      );
    }
    final phase = isLegacy
        ? _legacyOperationPhase(state)
        : InoConversationOperationPhase.fromWire(
            _boundedString(json, 'phase', maxLength: 32),
          );
    if (phase == InoConversationOperationPhase.approved &&
        state != InoConversationOperationState.queued) {
      throw const FormatException(
        'payload.data.operation.approved must remain queued.',
      );
    }
    if (phase == InoConversationOperationPhase.applyingEffect &&
        state != InoConversationOperationState.running) {
      throw const FormatException(
        'payload.data.operation.applying-effect must remain running.',
      );
    }
    return InoConversationOperation(
      operationId: operationId,
      phase: phase,
      version: version,
      state: state,
      retryable: retryable,
      safeReason: normalizedReason,
      action: json['action'] == null
          ? null
          : InoConversationAction.fromJson(
              json['action'],
              oauthStartOrigin: oauthStartOrigin,
            ),
      approvalId: approvalId,
    );
  }

  Map<String, Object?> toJson() => {
    if (version > 0) ...{
      'operationId': operationId,
      'phase': phase.wire,
      'version': version,
    },
    'state': state.wire,
    'retryable': retryable,
    'safeReason': ?safeReason,
    'action': action?.toJson(),
    'approvalId': ?approvalId,
  };
}

InoConversationOperationPhase _legacyOperationPhase(
  InoConversationOperationState state,
) => switch (state) {
  InoConversationOperationState.queued =>
    InoConversationOperationPhase.accepted,
  InoConversationOperationState.responding =>
    InoConversationOperationPhase.running,
  InoConversationOperationState.running =>
    InoConversationOperationPhase.running,
  InoConversationOperationState.awaitingApproval => throw const FormatException(
    'Legacy operations cannot carry approval authority.',
  ),
  InoConversationOperationState.awaitingAuthorization =>
    InoConversationOperationPhase.awaitingAuthorization,
  InoConversationOperationState.retryScheduled =>
    InoConversationOperationPhase.retryScheduled,
  InoConversationOperationState.succeeded =>
    InoConversationOperationPhase.succeeded,
  InoConversationOperationState.failed => InoConversationOperationPhase.failed,
  InoConversationOperationState.outcomeUnknown =>
    InoConversationOperationPhase.outcomeUnknown,
  InoConversationOperationState.cancelled =>
    InoConversationOperationPhase.cancelled,
};

class InoConversationAction {
  const InoConversationAction({
    required this.kind,
    required this.label,
    required this.target,
  });

  final String kind;
  final String label;
  final Uri target;

  factory InoConversationAction.fromJson(
    Object? value, {
    Uri? oauthStartOrigin,
  }) {
    final json = _safeObject(value, 'payload.data.operation.action');
    _demandOnlyKeys(json, const {
      'kind',
      'label',
      'target',
    }, 'payload.data.operation.action');
    final kind = _boundedString(json, 'kind', maxLength: 32);
    final label = _boundedString(json, 'label', maxLength: 64);
    final targetText = _boundedString(json, 'target', maxLength: 4096);
    final target = _resolveConnectionTarget(targetText, oauthStartOrigin);
    if (kind != 'openUrl' || target == null) {
      throw const FormatException(
        'payload.data.operation.action is not an allowed connection action.',
      );
    }
    return InoConversationAction(kind: kind, label: label, target: target);
  }

  Map<String, Object?> toJson() => {
    'kind': kind,
    'label': label,
    'target': target.toString(),
  };
}

Uri? _resolveConnectionTarget(String targetText, Uri? oauthStartOrigin) {
  if (oauthStartOrigin == null || !_isTrustedRuntimeOrigin(oauthStartOrigin)) {
    return null;
  }
  String path;
  String flowReference;
  const googlePrefix = '/oauth/start/google?f=';
  const salesforcePrefix = '/oauth/start/salesforce?f=';
  if (targetText.startsWith(googlePrefix)) {
    path = '/oauth/start/google';
    flowReference = targetText.substring(googlePrefix.length);
  } else if (targetText.startsWith(salesforcePrefix)) {
    path = '/oauth/start/salesforce';
    flowReference = targetText.substring(salesforcePrefix.length);
  } else {
    return null;
  }
  if (!_isBoundedFlowReference(flowReference)) return null;
  return oauthStartOrigin.replace(path: path, query: 'f=$flowReference');
}

bool _isTrustedRuntimeOrigin(Uri origin) =>
    origin.isAbsolute &&
    origin.scheme == 'https' &&
    origin.host.isNotEmpty &&
    origin.userInfo.isEmpty &&
    !origin.hasQuery &&
    !origin.hasFragment &&
    (origin.path.isEmpty || origin.path == '/');

bool _isBoundedFlowReference(String value) {
  if (value.length < 32 || value.length > 1024) return false;
  for (final codeUnit in value.codeUnits) {
    final allowed =
        (codeUnit >= 0x30 && codeUnit <= 0x39) ||
        (codeUnit >= 0x41 && codeUnit <= 0x5a) ||
        (codeUnit >= 0x61 && codeUnit <= 0x7a) ||
        codeUnit == 0x2d ||
        codeUnit == 0x5f;
    if (!allowed) return false;
  }
  return true;
}

class InoConversationSurfacePayload extends SurfacePayload {
  const InoConversationSurfacePayload({
    required this.intro,
    required this.messages,
    this.operation,
  });

  static const String nativeKindName = 'inoConversation';

  @override
  String get kind => 'native';

  String get nativeKind => nativeKindName;

  final String intro;
  final List<InoConversationMessage> messages;
  final InoConversationOperation? operation;

  factory InoConversationSurfacePayload.fromJson(
    Map<String, Object?> json, {
    Uri? oauthStartOrigin,
  }) {
    final nativeKind = _boundedString(json, 'nativeKind', maxLength: 64);
    if (nativeKind != nativeKindName) {
      throw FormatException('Unsupported INO native surface "$nativeKind".');
    }
    final data = _safeObject(json['data'], 'payload.data');
    _demandOnlyKeys(data, const {
      'intro',
      'messages',
      'operation',
    }, 'payload.data');
    final messages = data['messages'];
    if (messages is! List || messages.length > 200) {
      throw const FormatException(
        'payload.data.messages must be a bounded JSON array.',
      );
    }
    return InoConversationSurfacePayload(
      intro: _boundedString(data, 'intro', maxLength: 512),
      messages: List.unmodifiable(
        messages.map(InoConversationMessage.fromJson),
      ),
      operation: data['operation'] == null
          ? null
          : InoConversationOperation.fromJson(
              data['operation'],
              oauthStartOrigin: oauthStartOrigin,
            ),
    );
  }

  @override
  Map<String, Object?> toJson() => {
    'kind': kind,
    'nativeKind': nativeKind,
    'data': {
      'intro': intro,
      'messages': messages.map((message) => message.toJson()).toList(),
      'operation': operation?.toJson(),
    },
  };
}

SurfacePayload _nativeSurfacePayloadFromJson(
  Map<String, Object?> json, {
  Uri? oauthStartOrigin,
}) {
  final nativeKind = _boundedString(json, 'nativeKind', maxLength: 64);
  if (nativeKind == InoConversationSurfacePayload.nativeKindName) {
    return InoConversationSurfacePayload.fromJson(
      json,
      oauthStartOrigin: oauthStartOrigin,
    );
  }
  if (nativeKind == FeatureApprovalSurfacePayload.nativeKindName) {
    return FeatureApprovalSurfacePayload.fromJson(json);
  }
  return NativeSurfacePayload.fromJson(json);
}

class UiActionRef {
  const UiActionRef({
    required this.actionSchemaVersion,
    required this.bindingId,
    required this.actionType,
    required this.actionToken,
    required this.surfaceId,
    required this.surfaceRevision,
    required this.expiresAt,
  });

  final int actionSchemaVersion;
  final String bindingId;
  final String actionType;
  final String actionToken;
  final String surfaceId;
  final int surfaceRevision;
  final DateTime expiresAt;

  bool isExpired(DateTime now) => !expiresAt.isAfter(now.toUtc());

  factory UiActionRef.fromJson(Object? value) {
    final json = _object(value, 'action');
    final version = _positiveInt(json, 'actionSchemaVersion');
    if (version != digitalBrainActionSchemaVersion) {
      throw FormatException('Unsupported UI action schema version $version.');
    }
    return UiActionRef(
      actionSchemaVersion: version,
      bindingId: _boundedString(json, 'bindingId', maxLength: 256),
      actionType: _boundedString(json, 'actionType', maxLength: 128),
      actionToken: _boundedString(json, 'actionToken', maxLength: 4096),
      surfaceId: _boundedString(json, 'surfaceId', maxLength: 256),
      surfaceRevision: _positiveInt(json, 'surfaceRevision'),
      expiresAt: _dateTime(json, 'expiresAt'),
    );
  }

  Map<String, Object?> toJson() => {
    'actionSchemaVersion': actionSchemaVersion,
    'bindingId': bindingId,
    'actionType': actionType,
    'actionToken': actionToken,
    'surfaceId': surfaceId,
    'surfaceRevision': surfaceRevision,
    'expiresAt': expiresAt.toUtc().toIso8601String(),
  };
}

class SurfaceEnvelope {
  SurfaceEnvelope({
    required this.protocolVersion,
    required this.surfaceSchema,
    required this.surfaceSchemaVersion,
    required this.surfaceId,
    required this.revision,
    required this.ownerId,
    required this.actorId,
    required this.audience,
    required this.feedSequence,
    required this.createdAt,
    required this.expiresAt,
    required this.correlationId,
    required this.cause,
    required Set<String> requiredClientCapabilities,
    required this.contentHash,
    required this.payload,
    required List<UiActionRef> actions,
  }) : requiredClientCapabilities = Set.unmodifiable(
         requiredClientCapabilities,
       ),
       actions = List.unmodifiable(actions) {
    final bindingIds = <String>{};
    for (final action in actions) {
      if (action.surfaceId != surfaceId || action.surfaceRevision != revision) {
        throw FormatException(
          'Action ${action.bindingId} is bound to the wrong surface revision.',
        );
      }
      if (!bindingIds.add(action.bindingId)) {
        throw FormatException(
          'Duplicate action binding "${action.bindingId}".',
        );
      }
    }
  }

  final int protocolVersion;
  final String surfaceSchema;
  final int surfaceSchemaVersion;
  final String surfaceId;
  final int revision;
  final String ownerId;
  final String actorId;
  final SurfaceAudience audience;
  final int feedSequence;
  final DateTime createdAt;
  final DateTime? expiresAt;
  final String correlationId;
  final CauseRef cause;
  final Set<String> requiredClientCapabilities;
  final String contentHash;
  final SurfacePayload payload;
  final List<UiActionRef> actions;

  bool isExpired(DateTime now) =>
      expiresAt != null && !expiresAt!.isAfter(now.toUtc());

  UiActionRef? actionByBindingId(String bindingId) {
    for (final action in actions) {
      if (action.bindingId == bindingId) return action;
    }
    return null;
  }

  UiActionRef? actionByType(String actionType) {
    UiActionRef? found;
    for (final action in actions) {
      if (action.actionType != actionType) continue;
      if (found != null) return null;
      found = action;
    }
    return found;
  }

  factory SurfaceEnvelope.fromJson(
    Map<String, Object?> json, {
    ClientCapabilities capabilities = const ClientCapabilities(),
    Uri? oauthStartOrigin,
  }) {
    final protocolVersion = _positiveInt(json, 'protocolVersion');
    if (protocolVersion != digitalBrainUiProtocolVersion ||
        !capabilities.protocolVersions.contains(protocolVersion)) {
      throw FormatException(
        'Unsupported DigitalBrain UI protocol version $protocolVersion.',
      );
    }
    final schema = _boundedString(json, 'surfaceSchema', maxLength: 128);
    if (schema != digitalBrainSurfaceSchema) {
      throw FormatException('Unsupported surface schema "$schema".');
    }
    final schemaVersion = _positiveInt(json, 'surfaceSchemaVersion');
    if (schemaVersion != digitalBrainSurfaceSchemaVersion) {
      throw FormatException(
        'Unsupported surface schema version $schemaVersion.',
      );
    }

    final requiredCapabilities = _stringSet(
      json['requiredClientCapabilities'],
      'requiredClientCapabilities',
    );
    for (final required in requiredCapabilities) {
      if (!capabilities.names.contains(required)) {
        throw UnsupportedSurfaceCapability(required);
      }
    }

    final surfaceId = _boundedString(json, 'surfaceId', maxLength: 256);
    final revision = _positiveInt(json, 'revision');
    final actionsRaw = json['actions'];
    if (actionsRaw is! List<Object?>) {
      throw const FormatException('Surface actions must be a JSON array.');
    }

    final hash = _boundedString(json, 'contentHash', maxLength: 80);
    if (!RegExp(r'^(?:sha256:)?[a-f0-9]{64}$').hasMatch(hash)) {
      throw const FormatException(
        'Surface contentHash must be a SHA-256 hex value.',
      );
    }

    final expiresAt = json['expiresAt'] == null
        ? null
        : _dateTime(json, 'expiresAt');
    final envelope = SurfaceEnvelope(
      protocolVersion: protocolVersion,
      surfaceSchema: schema,
      surfaceSchemaVersion: schemaVersion,
      surfaceId: surfaceId,
      revision: revision,
      ownerId: _boundedString(json, 'ownerId', maxLength: 256),
      actorId: _boundedString(json, 'actorId', maxLength: 256),
      audience: SurfaceAudience.fromJson(json['audience']),
      feedSequence: _positiveInt(json, 'feedSequence'),
      createdAt: _dateTime(json, 'createdAt'),
      expiresAt: expiresAt,
      correlationId: _boundedString(json, 'correlationId', maxLength: 256),
      cause: CauseRef.fromJson(json['cause']),
      requiredClientCapabilities: requiredCapabilities,
      contentHash: hash,
      payload: SurfacePayload.fromJson(
        json['payload'],
        capabilities: capabilities,
        oauthStartOrigin: oauthStartOrigin,
      ),
      actions: actionsRaw.map(UiActionRef.fromJson).toList(),
    );
    return envelope;
  }

  Map<String, Object?> toJson() => {
    'protocolVersion': protocolVersion,
    'surfaceSchema': surfaceSchema,
    'surfaceSchemaVersion': surfaceSchemaVersion,
    'surfaceId': surfaceId,
    'revision': revision,
    'ownerId': ownerId,
    'actorId': actorId,
    'audience': audience.toJson(),
    'feedSequence': feedSequence,
    'createdAt': createdAt.toUtc().toIso8601String(),
    'expiresAt': expiresAt?.toUtc().toIso8601String(),
    'correlationId': correlationId,
    'cause': cause.toJson(),
    'requiredClientCapabilities': requiredClientCapabilities.toList()..sort(),
    'contentHash': contentHash,
    'payload': payload.toJson(),
    'actions': actions.map((action) => action.toJson()).toList(),
  };
}

class SurfaceEnvelopeDecoder {
  const SurfaceEnvelopeDecoder({
    this.capabilities = const ClientCapabilities(),
    this.oauthStartOrigin,
  });

  final ClientCapabilities capabilities;
  final Uri? oauthStartOrigin;

  SurfaceEnvelope decode(String source) {
    if (utf8.encode(source).length > capabilities.maximumPayloadBytes) {
      throw const FormatException(
        'Surface envelope exceeds client size limit.',
      );
    }
    final Object? value;
    try {
      value = jsonDecode(source);
    } on FormatException {
      rethrow;
    }
    return SurfaceEnvelope.fromJson(
      _object(value, 'surface envelope'),
      capabilities: capabilities,
      oauthStartOrigin: oauthStartOrigin,
    );
  }
}

class UnsupportedSurfaceCapability implements Exception {
  const UnsupportedSurfaceCapability(this.capability);

  final String capability;

  @override
  String toString() => 'Unsupported surface capability "$capability".';
}

const Set<String> _forbiddenPayloadKeys = {
  'accesstoken',
  'actiontoken',
  'authorization',
  'authorizationcode',
  'clientid',
  'clientsecret',
  'codeverifier',
  'grants',
  'password',
  'actor',
  'actorid',
  'refreshtoken',
  'secret',
  'secretvalue',
  'sessionid',
  'ownerid',
  'userid',
};

void _demandOnlyKeys(
  Map<String, Object?> value,
  Set<String> allowed,
  String path,
) {
  for (final key in value.keys) {
    if (!allowed.contains(key)) {
      throw FormatException('$path contains unsupported field "$key".');
    }
  }
}

String? _nullableBoundedString(
  Map<String, Object?> json,
  String key,
  int maximumLength,
) {
  final value = json[key];
  if (value == null) return null;
  if (value is! String || value.isEmpty || value.length > maximumLength) {
    throw FormatException('$key must be a bounded string.');
  }
  return value;
}

List<String> _boundedStringList(Map<String, Object?> json, String key) {
  final value = json[key];
  if (value is! List || value.length > 64) {
    throw FormatException('$key must be a bounded array.');
  }
  return List.unmodifiable(
    value.map((item) {
      if (item is! String || item.isEmpty || item.length > 256) {
        throw FormatException('$key contains an invalid identifier.');
      }
      return item;
    }),
  );
}

Map<String, Object?> _safeObject(
  Object? value,
  String name, {
  bool allowNull = true,
}) {
  final raw = _object(value, name);
  final safe = _deepCopyJson(raw, path: name, depth: 0, allowNull: allowNull);
  return (safe as Map<Object?, Object?>).cast<String, Object?>();
}

Object? _deepCopyJson(
  Object? value, {
  required String path,
  required int depth,
  required bool allowNull,
}) {
  if (depth > 64) {
    throw FormatException('$path exceeds the maximum nesting depth.');
  }
  if (value == null) {
    if (!allowNull) {
      throw FormatException('$path contains null, which RFW cannot decode.');
    }
    return null;
  }
  if (value is String || value is bool || value is int || value is double) {
    return value;
  }
  if (value is List) {
    return List<Object?>.unmodifiable(
      value.asMap().entries.map(
        (entry) => _deepCopyJson(
          entry.value,
          path: '$path[${entry.key}]',
          depth: depth + 1,
          allowNull: allowNull,
        ),
      ),
    );
  }
  if (value is Map) {
    final output = <String, Object?>{};
    for (final entry in value.entries) {
      if (entry.key is! String) {
        throw FormatException('$path contains a non-string JSON object key.');
      }
      final key = entry.key as String;
      final normalized = key
          .replaceAll(RegExp(r'[^A-Za-z0-9]'), '')
          .toLowerCase();
      if (_forbiddenPayloadKeys.contains(normalized)) {
        throw FormatException(
          '$path contains forbidden sensitive field "$key".',
        );
      }
      output[key] = _deepCopyJson(
        entry.value,
        path: '$path.$key',
        depth: depth + 1,
        allowNull: allowNull,
      );
    }
    return Map<String, Object?>.unmodifiable(output);
  }
  throw FormatException(
    '$path contains unsupported value ${value.runtimeType}.',
  );
}

Map<String, Object?> _object(Object? value, String name) {
  if (value is! Map) throw FormatException('$name must be a JSON object.');
  return value.map((key, item) {
    if (key is! String) {
      throw FormatException('$name contains a non-string key.');
    }
    return MapEntry(key, item);
  });
}

String _boundedString(
  Map<String, Object?> json,
  String key, {
  required int maxLength,
  bool allowEmpty = false,
}) {
  final value = _stringValue(json[key], key).trim();
  if ((!allowEmpty && value.isEmpty) || value.length > maxLength) {
    throw FormatException('$key has an invalid length.');
  }
  return value;
}

String _stringValue(Object? value, String name) {
  if (value is! String) throw FormatException('$name must be a string.');
  return value;
}

int _positiveInt(Map<String, Object?> json, String key) {
  final value = json[key];
  if (value is! int || value <= 0) {
    throw FormatException('$key must be a positive integer.');
  }
  return value;
}

DateTime _dateTime(Map<String, Object?> json, String key) {
  final source = _stringValue(json[key], key);
  final value = DateTime.tryParse(source);
  if (value == null) {
    throw FormatException('$key must be an ISO-8601 timestamp.');
  }
  return value.toUtc();
}

Set<String> _stringSet(Object? value, String name) {
  if (value is! List) throw FormatException('$name must be a JSON array.');
  final result = <String>{};
  for (final item in value) {
    if (item is! String || item.trim().isEmpty || item.length > 128) {
      throw FormatException('$name contains an invalid capability.');
    }
    if (!result.add(item)) {
      throw FormatException('$name contains duplicate capability "$item".');
    }
  }
  return Set.unmodifiable(result);
}
