import 'dart:convert';

/// The dev-stand owner credential, carried as HTTP Basic on every kernel call.
///
/// Token-free by design: the kernel compares the same `user:pass` pair it was
/// configured with, so there is nothing to refresh or revoke. Replaced wholesale
/// when real multi-user auth lands.
final class BasicCredentials {
  const BasicCredentials({required this.username, required this.password});

  final String username;
  final String password;

  /// The `Authorization` header value for these credentials.
  String get authorizationHeader =>
      'Basic ${base64.encode(utf8.encode('$username:$password'))}';

  @override
  bool operator ==(Object other) =>
      other is BasicCredentials &&
      other.username == username &&
      other.password == password;

  @override
  int get hashCode => Object.hash(username, password);
}
