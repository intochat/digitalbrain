import 'dart:convert';

abstract final class FeatureGrantConstraintPolicy {
  static const int _maximumBytes = 65536;
  static const int _maximumAllowedValues = 256;
  static const int _maximumObjectProperties = 128;
  static const int _maximumDepth = 64;
  static const Set<String> _credentialPropertyNames = {
    'password',
    'accesstoken',
    'refreshtoken',
    'authorization',
    'apikey',
    'privatekey',
    'credential',
    'credentials',
    'token',
    'clientsecret',
    'secret',
    'secretvalue',
    'actiontoken',
    'authtoken',
    'bearertoken',
    'idtoken',
    'sessiontoken',
    'secretkey',
    'connectionstring',
    'passphrase',
    'authorizationcode',
    'codeverifier',
    'secretaccesskey',
    'privatekeypem',
    'sastoken',
    'sessionid',
  };

  static String? summarize({
    required String constraintsJson,
    required String capabilityId,
  }) {
    if (utf8.encode(constraintsJson).length > _maximumBytes ||
        !_isCanonicalText(capabilityId, 256)) {
      return null;
    }
    Object? decoded;
    try {
      decoded = jsonDecode(constraintsJson);
    } on FormatException {
      return null;
    }
    if (decoded is! Map<String, dynamic> ||
        decoded.keys.any(
          (key) => key != 'allowedToolIds' && key != 'payload',
        )) {
      return null;
    }
    final allowedValue = decoded['allowedToolIds'];
    if (allowedValue is! List<dynamic> ||
        allowedValue.isEmpty ||
        allowedValue.length > _maximumAllowedValues) {
      return null;
    }
    final allowed = <String>[];
    for (final value in allowedValue) {
      if (value is! String || !_isCanonicalText(value, 256)) return null;
      allowed.add(value);
    }
    if (allowed.toSet().length != allowed.length ||
        !allowed.contains(capabilityId)) {
      return null;
    }
    final sortedTools = allowed.toList(growable: false)..sort();
    final parts = <String>[
      sortedTools.length == 1
          ? 'Only ${sortedTools.single}'
          : 'Allowed tools: ${sortedTools.join(', ')}',
    ];
    if (!decoded.containsKey('payload')) return parts.single;
    final payload = decoded['payload'];
    if (payload is! Map<String, dynamic> ||
        !_validExpression(payload, depth: 0)) {
      return null;
    }
    if (payload.isEmpty) {
      parts.add('inputs require an object with no field restrictions');
    } else {
      final keys = payload.keys.toList(growable: false)..sort();
      for (final key in keys) {
        _appendSummary(parts, key, payload[key]);
      }
    }
    return parts.join('; ');
  }

  static bool _validExpression(Object? value, {required int depth}) {
    if (depth > _maximumDepth) return false;
    if (value is Map<String, dynamic>) {
      if (value.length > _maximumObjectProperties ||
          value.keys.any(
            (key) =>
                !_isCanonicalText(key, 256) ||
                _credentialPropertyNames.contains(_normalize(key)),
          )) {
        return false;
      }
      return value.values.every(
        (nested) => _validExpression(nested, depth: depth + 1),
      );
    }
    if (value is List<dynamic>) {
      return value.isNotEmpty &&
          value.length <= _maximumAllowedValues &&
          value.every((nested) => _validExpression(nested, depth: depth + 1));
    }
    return value == null || value is bool || value is num || value is String;
  }

  static void _appendSummary(
    List<String> parts,
    String path,
    Object? constraint,
  ) {
    if (constraint is Map<String, dynamic>) {
      if (constraint.isEmpty) {
        parts.add('input $path requires an object');
        return;
      }
      final keys = constraint.keys.toList(growable: false)..sort();
      for (final key in keys) {
        _appendSummary(parts, '$path.$key', constraint[key]);
      }
      return;
    }
    if (constraint is List<dynamic>) {
      final values = constraint.map(_renderValue).toList(growable: false)
        ..sort();
      parts.add('input $path allows ${values.join(' or ')}');
      return;
    }
    parts.add('input $path must equal ${_renderValue(constraint)}');
  }

  static String _renderValue(Object? value) => jsonEncode(_canonicalize(value));

  static Object? _canonicalize(Object? value) {
    if (value is Map<String, dynamic>) {
      final keys = value.keys.toList(growable: false)..sort();
      return <String, Object?>{
        for (final key in keys) key: _canonicalize(value[key]),
      };
    }
    if (value is List<dynamic>) {
      final values = value.map(_canonicalize).toList(growable: false)
        ..sort((left, right) => jsonEncode(left).compareTo(jsonEncode(right)));
      return values;
    }
    return value;
  }

  static String _normalize(String value) =>
      value.toLowerCase().replaceAll(RegExp('[^a-z0-9]'), '');

  static bool _isCanonicalText(String value, int maximumLength) =>
      value.isNotEmpty &&
      value.length <= maximumLength &&
      value.trim() == value &&
      !value.runes.any(
        (character) => character < 32 || (character >= 127 && character <= 159),
      );
}
