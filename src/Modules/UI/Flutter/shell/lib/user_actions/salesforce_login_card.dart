import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../chat/chat_contracts.dart';
import 'chat_login_action.dart';
import 'provider_login_card.dart';

/// Compatibility wrapper retaining the established Salesforce card API.
final class SalesforceLoginCard extends StatelessWidget {
  const SalesforceLoginCard({
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
    provider: 'salesforce',
    displayName: 'Salesforce',
    actionLabel: 'Log in to Salesforce',
    kernelBaseUri: kernelBaseUri,
    onOpenSignIn: onOpenSignIn,
    onCancelTurn: onCancelTurn,
    leading: SvgPicture.asset(
      'assets/salesforce.svg',
      width: 58,
      height: 40,
      excludeFromSemantics: true,
    ),
  );
}
