import 'package:flutter/foundation.dart';

@immutable
class Pack {
  final String name;
  final String version;
  final String description;
  final String owner;
  final bool installed;
  final List<String> tags;

  const Pack({
    required this.name,
    required this.version,
    required this.description,
    required this.owner,
    required this.installed,
    required this.tags,
  });
}
