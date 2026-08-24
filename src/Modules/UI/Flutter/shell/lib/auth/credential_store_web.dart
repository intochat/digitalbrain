import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:web/web.dart' as web;

// sessionStorage, not localStorage: the credential dies with the tab, so a
// shared machine does not hand the next visitor a signed-in shell.
const _usernameKey = 'digitalbrain.auth.username';
const _passwordKey = 'digitalbrain.auth.password';

BasicCredentials? readStoredCredentials() {
  final username = web.window.sessionStorage.getItem(_usernameKey);
  final password = web.window.sessionStorage.getItem(_passwordKey);
  if (username == null ||
      password == null ||
      username.isEmpty ||
      password.isEmpty) {
    return null;
  }
  return BasicCredentials(username: username, password: password);
}

void writeStoredCredentials(BasicCredentials credentials) {
  web.window.sessionStorage.setItem(_usernameKey, credentials.username);
  web.window.sessionStorage.setItem(_passwordKey, credentials.password);
}

void clearStoredCredentials() {
  web.window.sessionStorage.removeItem(_usernameKey);
  web.window.sessionStorage.removeItem(_passwordKey);
}
