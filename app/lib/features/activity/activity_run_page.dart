import 'dart:async';

import 'package:flutter/material.dart';

import 'activity_gateway.dart';
import 'activity_models.dart';
import 'activity_run_detail.dart';

const activityRunPageRetryKey = ValueKey('activity-run-page-retry');

class ActivityRunPage extends StatefulWidget {
  const ActivityRunPage({
    super.key,
    required this.runId,
    required this.gateway,
    required this.onBackToActivity,
    this.sessionIdentity,
    this.onOpenFeature,
    this.onOpenConversation,
    this.onOpenRequest,
    this.onOpenConversationContext,
    this.onOpenAutomation,
    this.onOpenResultSurface,
  });

  final String runId;
  final ActivityRunGateway gateway;
  final VoidCallback onBackToActivity;
  final Object? sessionIdentity;
  final ValueChanged<String>? onOpenFeature;
  final ValueChanged<String>? onOpenConversation;
  final ValueChanged<String>? onOpenRequest;
  final void Function(String conversationId, String requestId)?
  onOpenConversationContext;
  final ActivityAutomationReferenceCallback? onOpenAutomation;
  final ValueChanged<String>? onOpenResultSurface;

  @override
  State<ActivityRunPage> createState() => _ActivityRunPageState();
}

class _ActivityRunPageState extends State<ActivityRunPage> {
  ActivityRun? _run;
  var _loading = true;
  var _failed = false;
  var _requestFence = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void didUpdateWidget(ActivityRunPage oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.runId != widget.runId ||
        !identical(
          oldWidget.sessionIdentity ?? oldWidget.gateway,
          widget.sessionIdentity ?? widget.gateway,
        )) {
      unawaited(_load());
    }
  }

  Future<void> _load() async {
    final request = ++_requestFence;
    setState(() {
      _loading = true;
      _failed = false;
    });
    try {
      final run = await widget.gateway.loadRun(widget.runId);
      if (!mounted || request != _requestFence) return;
      setState(() {
        _run = run;
        _loading = false;
      });
    } on Object {
      if (!mounted || request != _requestFence) return;
      setState(() {
        _run = null;
        _loading = false;
        _failed = true;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return _ActivityRunStateFrame(
        onBackToActivity: widget.onBackToActivity,
        child: Center(
          child: Semantics(
            label: 'Loading Run details',
            liveRegion: true,
            child: CircularProgressIndicator(),
          ),
        ),
      );
    }
    if (_failed || _run == null) {
      return _ActivityRunStateFrame(
        onBackToActivity: widget.onBackToActivity,
        child: Center(
          child: Semantics(
            container: true,
            liveRegion: true,
            label: 'Run details could not be loaded',
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.error_outline, size: 40),
                  const SizedBox(height: 12),
                  Text(
                    "We couldn't load this Run.",
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  const SizedBox(height: 16),
                  FilledButton.icon(
                    key: activityRunPageRetryKey,
                    onPressed: _load,
                    icon: const Icon(Icons.refresh),
                    label: const Text('Try again'),
                  ),
                ],
              ),
            ),
          ),
        ),
      );
    }
    return Material(
      child: SafeArea(
        child: ActivityRunDetailView(
          run: _run!,
          onBackToActivity: widget.onBackToActivity,
          onOpenFeature: widget.onOpenFeature,
          onOpenConversation: _openConversation(_run!),
          onOpenRequest: _openRequest(_run!),
          onOpenAutomation: widget.onOpenAutomation,
          onOpenResultSurface: widget.onOpenResultSurface,
        ),
      ),
    );
  }

  ValueChanged<String>? _openConversation(ActivityRun run) {
    final openContext = widget.onOpenConversationContext;
    final requestId = run.requestId;
    if (openContext == null || requestId == null) {
      return widget.onOpenConversation;
    }
    return (conversationId) => openContext(conversationId, requestId);
  }

  ValueChanged<String>? _openRequest(ActivityRun run) {
    final openContext = widget.onOpenConversationContext;
    final conversationId = run.conversationId;
    if (openContext == null || conversationId == null) {
      return widget.onOpenRequest;
    }
    return (requestId) => openContext(conversationId, requestId);
  }
}

class _ActivityRunStateFrame extends StatelessWidget {
  const _ActivityRunStateFrame({
    required this.onBackToActivity,
    required this.child,
  });

  final VoidCallback onBackToActivity;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Material(
      child: SafeArea(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Align(
              alignment: Alignment.centerLeft,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
                child: _BackToActivityButton(onPressed: onBackToActivity),
              ),
            ),
            Expanded(child: child),
          ],
        ),
      ),
    );
  }
}

class _BackToActivityButton extends StatelessWidget {
  const _BackToActivityButton({required this.onPressed});

  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    return TextButton.icon(
      key: activityBackToActivityButtonKey,
      onPressed: onPressed,
      icon: const Icon(Icons.arrow_back),
      label: const Text('Back to Activity'),
    );
  }
}
