import 'package:flutter/material.dart';

import '../brain_theme.dart';

/// Credential prompt shown when the kernel answers 401.
///
/// Deliberately minimal: one owner, no registration, no recovery. It exists to
/// keep the dev stand off the open internet until real auth replaces it.
final class LoginScreen extends StatefulWidget {
  const LoginScreen({
    super.key,
    required this.onSubmit,
    this.initialUsername = '',
    this.errorMessage,
  });

  /// Resolves to an error message to display, or null once signed in.
  final Future<String?> Function(String username, String password) onSubmit;
  final String initialUsername;
  final String? errorMessage;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

final class _LoginScreenState extends State<LoginScreen> {
  late final TextEditingController _username = TextEditingController(
    text: widget.initialUsername,
  );
  final TextEditingController _password = TextEditingController();
  final FocusNode _passwordFocus = FocusNode();

  late String? _error = widget.errorMessage;
  bool _busy = false;

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    _passwordFocus.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_busy) {
      return;
    }
    final username = _username.text.trim();
    final password = _password.text;
    if (username.isEmpty || password.isEmpty) {
      setState(() => _error = 'Enter a username and password.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    final failure = await widget.onSubmit(username, password);

    // The gate swaps this screen out on success, so only failure lands here.
    if (!mounted) {
      return;
    }
    setState(() {
      _busy = false;
      _error = failure;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: BrainPalette.surface,
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 360),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text('DigitalBrain', style: BrainType.title),
                const SizedBox(height: 8),
                Text(
                  'Sign in to reach the kernel.',
                  style: BrainType.bodyMuted,
                ),
                const SizedBox(height: 24),
                TextField(
                  key: const Key('login-username'),
                  controller: _username,
                  enabled: !_busy,
                  autofocus: true,
                  textInputAction: TextInputAction.next,
                  decoration: const InputDecoration(labelText: 'Username'),
                  onSubmitted: (_) => _passwordFocus.requestFocus(),
                ),
                const SizedBox(height: 12),
                TextField(
                  key: const Key('login-password'),
                  controller: _password,
                  focusNode: _passwordFocus,
                  enabled: !_busy,
                  obscureText: true,
                  textInputAction: TextInputAction.done,
                  decoration: const InputDecoration(labelText: 'Password'),
                  onSubmitted: (_) => _submit(),
                ),
                const SizedBox(height: 20),
                FilledButton(
                  key: const Key('login-submit'),
                  onPressed: _busy ? null : _submit,
                  child: _busy
                      ? const SizedBox(
                          height: 18,
                          width: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Text('Sign in'),
                ),
                if (_error case final error?) ...[
                  const SizedBox(height: 16),
                  Text(
                    error,
                    key: const Key('login-error'),
                    style: BrainType.body.copyWith(color: BrainPalette.signal),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
