import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:web/web.dart' as web;

// Basic credentials are transient. Reload authentication uses the session cookie.
const _usernameKey = 'digitalbrain.auth.username';
const _passwordKey = 'digitalbrain.auth.password';
BasicCredentials? _credentials;

BasicCredentials? readStoredCredentials() {
  // Remove secrets left by older clients without ever reading them.
  _clearLegacy();
  return _credentials;
}

void writeStoredCredentials(BasicCredentials credentials) {
  _clearLegacy();
  _credentials = credentials;
}

void clearStoredCredentials() {
  _credentials = null;
  _clearLegacy();
}

void _clearLegacy() {
  try {
    web.window.sessionStorage.removeItem(_usernameKey);
    web.window.sessionStorage.removeItem(_passwordKey);
  } catch (_) {
    // Browsers may disable storage. Authentication must still work via cookies.
  }
}
