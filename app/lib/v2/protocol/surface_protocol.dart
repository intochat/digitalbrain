import 'dart:convert';
import 'dart:typed_data';

const int digitalBrainUiProtocolVersion = 2;
const String digitalBrainSurfaceSchema = 'digitalbrain.surface';
const int digitalBrainSurfaceSchemaVersion = 2;
const int digitalBrainActionSchemaVersion = 1;
const int defaultMaximumSurfaceBytes = 1024 * 1024;

/// Capabilities advertised during the V2 feed handshake.
///
/// These values describe renderer support only. They are never authorization
/// claims and the server must not derive tenant, workspace, or principal
/// authority from them.
class V2ClientCapabilities {
  const V2ClientCapabilities({
    this.protocolVersions = const {digitalBrainUiProtocolVersion},
    this.payloadKinds = const {'widgetTree', 'rfw', 'native'},
    this.widgetVocabularyVersion = 2,
    this.maximumPayloadBytes = defaultMaximumSurfaceBytes,
    this.supportsBinaryRfw = true,
    this.nativeFeatures = const {'typed-actions', 'feed-reset', 'feed-ack'},
  });

  final Set<int> protocolVersions;
  final Set<String> payloadKinds;
  final int widgetVocabularyVersion;
  final int maximumPayloadBytes;
  final bool supportsBinaryRfw;
  final Set<String> nativeFeatures;

  Set<String> get names => {
    'ui.protocol.v$digitalBrainUiProtocolVersion',
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
    if (kind != 'principal' && kind != 'workspace' && kind != 'public') {
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
    required V2ClientCapabilities capabilities,
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
      'native' => NativeSurfacePayload.fromJson(json),
      _ => throw FormatException('Unsupported V2 surface payload "$kind".'),
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
    required this.tenantId,
    required this.workspaceId,
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
  final String tenantId;
  final String workspaceId;
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
    V2ClientCapabilities capabilities = const V2ClientCapabilities(),
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
      throw FormatException('Unsupported V2 surface schema "$schema".');
    }
    final schemaVersion = _positiveInt(json, 'surfaceSchemaVersion');
    if (schemaVersion != digitalBrainSurfaceSchemaVersion) {
      throw FormatException(
        'Unsupported V2 surface schema version $schemaVersion.',
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
      tenantId: _boundedString(json, 'tenantId', maxLength: 256),
      workspaceId: _boundedString(json, 'workspaceId', maxLength: 256),
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
    'tenantId': tenantId,
    'workspaceId': workspaceId,
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
    this.capabilities = const V2ClientCapabilities(),
  });

  final V2ClientCapabilities capabilities;

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
  'authorizationcode',
  'clientsecret',
  'codeverifier',
  'password',
  'refreshtoken',
  'secretvalue',
};

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
