import 'dart:io' show Platform;

/// Native: read from process environment variables.
String? getEnv(String key) => Platform.environment[key];
