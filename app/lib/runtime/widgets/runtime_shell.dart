import 'package:flutter/material.dart';

import '../../core/session/app_session_scope.dart';
import '../runtime.dart';
import '../runtime_session_owner.dart';
import 'chat_page.dart';

export 'chat_page.dart'
    show
        runtimeLoadingKey,
        runtimeSignOutButtonKey,
        runtimeSurfaceKey,
        runtimeTerminalErrorKey;

const Key runtimeSignInKey = Key('v2-runtime-sign-in');
const String developmentUsername = 'admin';
const String developmentPassword = 'admin';
const Key runtimeUsernameFieldKey = Key('v2-runtime-username-field');
const Key runtimePasswordFieldKey = Key('v2-runtime-password-field');
const Key runtimeSignInButtonKey = Key('v2-runtime-sign-in-button');

class RuntimeShell extends StatefulWidget {
  const RuntimeShell({super.key, required this.child});

  final Widget child;

  @override
  State<RuntimeShell> createState() => _RuntimeShellState();
}

class _RuntimeShellState extends State<RuntimeShell> {
  final TextEditingController _username = TextEditingController(
    text: developmentUsername,
  );
  final TextEditingController _password = TextEditingController(
    text: developmentPassword,
  );

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final session = AppSessionScope.of(context);
    if (session.initializationError != null) {
      return _errorScaffold(
        'DigitalBrain could not start. Please try again.',
        key: runtimeTerminalErrorKey,
      );
    }
    final controller = session.controller;
    if (controller == null) {
      return _buildLoading('Preparing your workspace…');
    }
    if (!controller.session.isAuthenticated) {
      if (controller.status == RuntimeStatus.awaitingSignIn) {
        return _buildSignIn(session, controller);
      }
      return _buildLoading(
        controller.status == RuntimeStatus.authenticating
            ? 'Signing you in…'
            : 'Preparing your workspace…',
      );
    }
    return widget.child;
  }

  Widget _buildLoading(String message) => Scaffold(
    body: Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const CircularProgressIndicator(key: runtimeLoadingKey),
          const SizedBox(height: 16),
          Text(message),
        ],
      ),
    ),
  );

  Widget _buildSignIn(
    RuntimeSessionOwner session,
    RuntimeController controller,
  ) {
    final externalIdentity = session.hasExternalIdentity;
    return Scaffold(
      key: runtimeSignInKey,
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Card(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'Sign in to DigitalBrain',
                    style: Theme.of(context).textTheme.headlineSmall,
                  ),
                  const SizedBox(height: 12),
                  Text(
                    externalIdentity
                        ? 'Continue with your organization identity.'
                        : 'Use your Development username and password.',
                  ),
                  const SizedBox(height: 16),
                  if (!externalIdentity) ...[
                    TextField(
                      key: runtimeUsernameFieldKey,
                      controller: _username,
                      textInputAction: TextInputAction.next,
                      onSubmitted: (_) => _authenticate(session),
                      decoration: const InputDecoration(labelText: 'Username'),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      key: runtimePasswordFieldKey,
                      controller: _password,
                      obscureText: true,
                      enableSuggestions: false,
                      autocorrect: false,
                      onSubmitted: (_) => _authenticate(session),
                      decoration: const InputDecoration(labelText: 'Password'),
                    ),
                    const SizedBox(height: 16),
                  ],
                  FilledButton(
                    key: runtimeSignInButtonKey,
                    onPressed: controller.status == RuntimeStatus.authenticating
                        ? null
                        : () => _authenticate(session),
                    child: Text(externalIdentity ? 'Continue' : 'Sign in'),
                  ),
                  if (controller.transientError != null) ...[
                    const SizedBox(height: 12),
                    Text(
                      'Sign-in was not accepted. Please try again.',
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  void _authenticate(RuntimeSessionOwner session) {
    if (session.hasExternalIdentity) {
      session.authenticateWithExternalIdentity();
      return;
    }
    final username = _username.text;
    final password = _password.text;
    _resetDevelopmentCredentials();
    session.authenticateWithPassword(username: username, password: password);
  }

  void _resetDevelopmentCredentials() {
    _username.text = developmentUsername;
    _password.text = developmentPassword;
  }

  Widget _errorScaffold(String message, {required Key key}) => Scaffold(
    body: Center(
      child: Padding(
        key: key,
        padding: const EdgeInsets.all(24),
        child: Text(message, textAlign: TextAlign.center),
      ),
    ),
  );
}
