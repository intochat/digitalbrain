import 'package:flutter/material.dart';

import '../chat/chat_contracts.dart';
import 'chat_login_action.dart';
import 'provider_login_card.dart';

final class GmailLoginCard extends StatelessWidget {
  const GmailLoginCard({
    super.key,
    required this.login,
    this.kernelBaseUri,
    this.onOpenSignIn,
    this.onCancelTurn,
  });

  final ChatLoginAction login;
  final Uri? kernelBaseUri;
  final OpenUrl? onOpenSignIn;
  final CancelChatTurn? onCancelTurn;

  @override
  Widget build(BuildContext context) => ProviderLoginCard(
    login: login,
    provider: 'gmail',
    displayName: 'Gmail',
    actionLabel: 'Sign in with Google',
    kernelBaseUri: kernelBaseUri,
    onOpenSignIn: onOpenSignIn,
    onCancelTurn: onCancelTurn,
    authorizeButtonBuilder: (context, onPressed) => Semantics(
      button: true,
      label: 'Sign in with Google',
      child: Opacity(
        opacity: onPressed == null ? 0.45 : 1,
        child: Material(
          color: Colors.transparent,
          child: InkWell(
            key: const Key('user_action_authorize_gmail'),
            onTap: onPressed,
            borderRadius: BorderRadius.circular(20),
            child: Image.asset(
              'assets/google-signin-light-2x.png',
              width: 180,
              height: 40,
              fit: BoxFit.contain,
              excludeFromSemantics: true,
            ),
          ),
        ),
      ),
    ),
  );
}
