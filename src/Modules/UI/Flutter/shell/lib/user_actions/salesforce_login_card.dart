import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import '../brain_theme.dart';
import '../chat/chat_contracts.dart';
import 'chat_login_action.dart';
import 'user_action_card.dart';

final class SalesforceLoginCard extends StatefulWidget {
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
  State<SalesforceLoginCard> createState() => _SalesforceLoginCardState();
}

final class _SalesforceLoginCardState extends State<SalesforceLoginCard> {
  Timer? _expiryTimer;
  bool _opening = false;
  bool _opened = false;
  bool _cancelling = false;
  bool _cancelRequested = false;
  String? _failure;

  bool get _expired =>
      !DateTime.now().toUtc().isBefore(widget.login.action.expiresAt);

  @override
  void initState() {
    super.initState();
    _scheduleExpiry();
  }

  @override
  void didUpdateWidget(covariant SalesforceLoginCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.login.action.expiresAt != widget.login.action.expiresAt ||
        oldWidget.login.status != widget.login.status) {
      _scheduleExpiry();
    }
  }

  void _scheduleExpiry() {
    _expiryTimer?.cancel();
    if (!widget.login.waiting || _expired) return;
    _expiryTimer = Timer(
      widget.login.action.expiresAt.difference(DateTime.now().toUtc()),
      () {
        if (mounted) setState(() {});
      },
    );
  }

  Future<void> _open() async {
    final uri = trustedSalesforceLoginUri(
      widget.login.action,
      widget.kernelBaseUri,
    );
    final open = widget.onOpenSignIn;
    if (uri == null ||
        open == null ||
        !widget.login.waiting ||
        _expired ||
        _opening ||
        _cancelling ||
        _cancelRequested) {
      return;
    }
    setState(() {
      _opening = true;
      _failure = null;
    });
    try {
      // Keep this directly in the click handler so browsers allow the popup.
      await open(uri);
      if (mounted) setState(() => _opened = true);
    } on Object {
      if (mounted) {
        setState(() => _failure = 'Could not open your browser. Try again.');
      }
    } finally {
      if (mounted) setState(() => _opening = false);
    }
  }

  Future<void> _cancel() async {
    final cancel = widget.onCancelTurn;
    final turnId = widget.login.turnId;
    if (cancel == null ||
        turnId == null ||
        turnId.isEmpty ||
        !widget.login.waiting ||
        _cancelling ||
        _cancelRequested) {
      return;
    }
    setState(() {
      _cancelling = true;
      _failure = null;
    });
    try {
      await cancel(commandId: widget.login.offer.commandId, turnId: turnId);
      if (mounted) setState(() => _cancelRequested = true);
    } on Object {
      if (mounted) {
        setState(() => _failure = 'Could not cancel the request. Try again.');
      }
    } finally {
      if (mounted) setState(() => _cancelling = false);
    }
  }

  String _statusText(bool trusted) {
    if (!widget.login.waiting) {
      return switch (widget.login.status) {
        LoginActionStatus.resuming => 'Continuing your request…',
        LoginActionStatus.completed => 'Request completed.',
        LoginActionStatus.cancelling => 'Cancelling request…',
        LoginActionStatus.cancelled => 'Request cancelled.',
        LoginActionStatus.failed => 'Request failed. Send your request again.',
        LoginActionStatus.superseded =>
          'A newer sign-in request replaced this one.',
        _ => 'This sign-in request is no longer available.',
      };
    }
    if (_cancelling || _cancelRequested) return 'Cancelling request…';
    if (_expired) {
      return 'Sign-in expired. Send your request again to reconnect.';
    }
    if (!trusted) return 'This sign-in link could not be verified.';
    if (_opening) return 'Opening your browser…';
    if (_opened) {
      return 'Finish signing in in your browser. Your request will continue here.';
    }
    return 'Sign in securely in your browser to continue this request.';
  }

  @override
  Widget build(BuildContext context) {
    final login = widget.login;
    final trusted =
        trustedSalesforceLoginUri(login.action, widget.kernelBaseUri) != null;
    final active = login.waiting && !_cancelling && !_cancelRequested;
    return ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: 440),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          UserActionCard(
            model: UserActionCardModel(
              moduleId: 'salesforce',
              displayName: 'Salesforce',
              displayText: login.action.message,
              actionUrl: login.action.loginUrl,
              taskId: login.offer.commandId,
              actionLabel: 'Log in to Salesforce',
              statusText: _statusText(trusted),
            ),
            leading: SvgPicture.asset(
              'assets/salesforce.svg',
              width: 58,
              height: 40,
              excludeFromSemantics: true,
            ),
            onAuthorize:
                active &&
                    !_expired &&
                    !_opening &&
                    trusted &&
                    widget.onOpenSignIn != null
                ? () => unawaited(_open())
                : null,
            showCancel: true,
            onCancel:
                active &&
                    !_opening &&
                    login.turnId != null &&
                    login.turnId!.isNotEmpty &&
                    widget.onCancelTurn != null
                ? () => unawaited(_cancel())
                : null,
          ),
          if (_failure != null && login.waiting)
            Padding(
              padding: const EdgeInsets.only(bottom: 10),
              child: Text(_failure!, style: BrainType.bodyMuted),
            ),
        ],
      ),
    );
  }

  @override
  void dispose() {
    _expiryTimer?.cancel();
    super.dispose();
  }
}
