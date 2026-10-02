import 'app_document.dart';

class SpecToken {
  const SpecToken({
    required this.name,
    required this.kind,
    this.qualifiedName = '',
    this.module = '',
    this.description,
  });
  final String name, kind, qualifiedName, module;
  final String? description;
  factory SpecToken.fromJson(Map<String, dynamic> j) => SpecToken(
    name: '${j['name'] ?? ''}',
    kind: '${j['kind'] ?? ''}',
    qualifiedName: '${j['qualifiedName'] ?? ''}',
    module: '${j['module'] ?? ''}',
    description: j['description'] as String?,
  );
  static List<SpecToken> read(dynamic json) => (json as List? ?? [])
      .map((x) => SpecToken.fromJson(appMap(x)))
      .where((x) => x.name.isNotEmpty)
      .toList();
}
