import 'dart:io' show Platform;

String? getEnv(String key) => Platform.environment[key];
