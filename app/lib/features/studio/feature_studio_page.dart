import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/session/app_session_scope.dart';
import 'feature_studio_controller.dart';
import 'feature_studio_gateway.dart';
import 'widgets/behavior_canvas.dart';
import 'widgets/code_changes_disclosure.dart';
import 'widgets/origin_request_bar.dart';
import 'widgets/suggested_changes_panel.dart';
import 'widgets/test_results_panel.dart';

export 'widgets/origin_request_bar.dart'
    show featureStudioBackToChatButtonKey, featureStudioDraftIdKey;
export 'widgets/suggested_changes_panel.dart'
    show featureStudioSuggestionGuidanceKey, featureStudioSuggestionsPanelKey;
export 'widgets/test_results_panel.dart' show featureStudioVerifyButtonKey;

const Key featureStudioOpenSuggestionsKey = Key(
  'feature-studio-open-suggestions',
);
const Key featureStudioOpenCodeKey = Key('feature-studio-open-code');
const Key featureStudioLoadingKey = Key('feature-studio-loading');
const Key featureStudioLoadErrorKey = Key('feature-studio-load-error');
const Key featureStudioConflictKey = Key('feature-studio-conflict');
const Key featureStudioLeaveDialogKey = Key('feature-studio-leave-dialog');
const Key featureStudioStayButtonKey = Key('feature-studio-stay-button');
const Key featureStudioDiscardButtonKey = Key('feature-studio-discard-button');
const Key featureStudioNavigateBehaviorKey = Key(
  'feature-studio-navigate-behavior',
);
const Key featureStudioNavigateSuggestedChangesKey = Key(
  'feature-studio-navigate-suggested-changes',
);
const Key featureStudioNavigateCodeKey = Key('feature-studio-navigate-code');
const Key featureStudioNavigateTestResultsKey = Key(
  'feature-studio-navigate-test-results',
);
const Key featureStudioBehaviorSectionKey = Key(
  'feature-studio-behavior-section',
);
const Key featureStudioSuggestionsSectionKey = Key(
  'feature-studio-suggestions-section',
);
const Key featureStudioCodeSectionKey = Key('feature-studio-code-section');
const Key featureStudioTestResultsSectionKey = Key(
  'feature-studio-test-results-section',
);

class FeatureStudioExitCoordinator {
  Future<bool> Function(bool navigate)? _handler;
  Object? _registration;

  bool get isAttached => _handler != null;

  Future<bool> requestExit({bool navigate = true}) {
    final handler = _handler;
    return handler == null ? Future<bool>.value(false) : handler(navigate);
  }

  VoidCallback attach(Future<bool> Function(bool navigate) handler) {
    final registration = Object();
    _registration = registration;
    _handler = handler;
    return () {
      if (!identical(_registration, registration)) return;
      _registration = null;
      _handler = null;
    };
  }
}

class FeatureStudioPage extends StatefulWidget {
  const FeatureStudioPage({
    super.key,
    required this.draftId,
    required this.onBackToChat,
    this.controller,
    this.gateway,
    this.exitCoordinator,
  }) : assert(controller != null || gateway != null);

  final String draftId;
  final VoidCallback onBackToChat;
  final FeatureStudioController? controller;
  final FeatureStudioGateway? gateway;
  final FeatureStudioExitCoordinator? exitCoordinator;

  @override
  State<FeatureStudioPage> createState() => _FeatureStudioPageState();
}

class _FeatureStudioPageState extends State<FeatureStudioPage> {
  late final FeatureStudioController _controller;
  late final bool _ownsController;
  late final VoidCallback? _detachExit;
  final GlobalKey _behaviorSectionAnchor = GlobalKey();
  final GlobalKey _suggestionsSectionAnchor = GlobalKey();
  final GlobalKey _codeSectionAnchor = GlobalKey();
  final GlobalKey _testResultsSectionAnchor = GlobalKey();
  final FocusNode _behaviorSectionFocus = FocusNode(
    debugLabel: 'Behavior section',
  );
  final FocusNode _suggestionsSectionFocus = FocusNode(
    debugLabel: 'Suggested changes section',
  );
  final FocusNode _codeSectionFocus = FocusNode(
    debugLabel: 'Code and changes section',
  );
  final FocusNode _testResultsSectionFocus = FocusNode(
    debugLabel: 'Test results section',
  );
  final FocusNode _suggestionsLauncherFocus = FocusNode(
    debugLabel: 'Open Suggested changes',
  );
  final FocusNode _codeLauncherFocus = FocusNode(
    debugLabel: 'Open Code and changes',
  );
  bool _exitApproved = false;
  bool _exitInProgress = false;

  @override
  void initState() {
    super.initState();
    _ownsController = widget.controller == null;
    _controller =
        widget.controller ??
        FeatureStudioController(
          draftId: widget.draftId,
          gateway: widget.gateway!,
        );
    if (_controller.loadPhase == FeatureStudioLoadPhase.idle) {
      unawaited(_controller.load());
    }
    _detachExit = widget.exitCoordinator?.attach(_requestExit);
  }

  @override
  void dispose() {
    _detachExit?.call();
    _behaviorSectionFocus.dispose();
    _suggestionsSectionFocus.dispose();
    _codeSectionFocus.dispose();
    _testResultsSectionFocus.dispose();
    _suggestionsLauncherFocus.dispose();
    _codeLauncherFocus.dispose();
    if (_ownsController) _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: _exitApproved,
    onPopInvokedWithResult: (didPop, _) {
      if (!didPop) unawaited(_requestExit());
    },
    child: Shortcuts(
      shortcuts: const <ShortcutActivator, Intent>{
        SingleActivator(LogicalKeyboardKey.keyS, control: true): _SaveIntent(),
        SingleActivator(LogicalKeyboardKey.keyS, meta: true): _SaveIntent(),
        SingleActivator(LogicalKeyboardKey.enter, control: true):
            _VerifyIntent(),
        SingleActivator(LogicalKeyboardKey.enter, meta: true): _VerifyIntent(),
        SingleActivator(LogicalKeyboardKey.escape): _DismissIntent(),
      },
      child: Actions(
        actions: <Type, Action<Intent>>{
          _SaveIntent: CallbackAction<_SaveIntent>(
            onInvoke: (_) {
              unawaited(_controller.saveNow());
              return null;
            },
          ),
          _VerifyIntent: CallbackAction<_VerifyIntent>(
            onInvoke: (_) {
              if (_controller.canVerify) unawaited(_controller.verify());
              return null;
            },
          ),
          _DismissIntent: CallbackAction<_DismissIntent>(
            onInvoke: (_) {
              FocusManager.instance.primaryFocus?.unfocus();
              return null;
            },
          ),
        },
        child: FocusTraversalGroup(
          policy: OrderedTraversalPolicy(),
          child: AnimatedBuilder(
            animation: _controller,
            builder: (context, _) => _buildState(context),
          ),
        ),
      ),
    ),
  );

  Widget _buildState(BuildContext context) {
    if (_controller.loadPhase == FeatureStudioLoadPhase.idle ||
        _controller.loadPhase == FeatureStudioLoadPhase.loading) {
      return const Material(
        child: Center(
          key: featureStudioLoadingKey,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              CircularProgressIndicator(),
              SizedBox(height: 16),
              Text('Opening Draft…'),
            ],
          ),
        ),
      );
    }
    if (_controller.loadPhase == FeatureStudioLoadPhase.notFound) {
      return _LoadFailure(
        title: 'Draft not found',
        message: 'This Draft is no longer available.',
        onBackToChat: _requestExit,
      );
    }
    if (_controller.loadPhase ==
        FeatureStudioLoadPhase.authenticationRequired) {
      return _LoadFailure(
        title: 'Sign-in required',
        message: 'Return to Chat to renew your DigitalBrain session.',
        onBackToChat: _requestExit,
      );
    }
    if (_controller.loadPhase == FeatureStudioLoadPhase.retryableFailure) {
      return _LoadFailure(
        title: 'Draft could not be opened',
        message: 'Check your connection and try again.',
        onBackToChat: _requestExit,
        onRetry: _controller.load,
      );
    }
    if (_controller.loadPhase == FeatureStudioLoadPhase.terminalFailure ||
        _controller.confirmedDraft == null ||
        _controller.behavior == null ||
        _controller.source == null) {
      return _LoadFailure(
        title: 'Draft could not be opened',
        message: 'This Draft cannot be opened safely.',
        onBackToChat: _requestExit,
      );
    }
    final draft = _controller.confirmedDraft!;
    return Material(
      color: Theme.of(context).scaffoldBackgroundColor,
      child: SafeArea(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            OriginRequestBar(
              draft: draft,
              savePhase: _controller.savePhase,
              onBackToChat: _requestExit,
            ),
            if (_controller.hasConflict)
              _ConflictBanner(controller: _controller)
            else if (_controller.savePhase ==
                FeatureStudioSavePhase.retryableFailure)
              _RetrySaveBanner(controller: _controller),
            Expanded(
              child: LayoutBuilder(
                builder: (context, constraints) {
                  if (constraints.maxWidth < 720) {
                    return _buildCompact(context);
                  }
                  if (constraints.maxWidth < 1180) {
                    return _buildMedium(context);
                  }
                  return _buildWide(context);
                },
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildCompact(BuildContext context) => ListView(
    padding: const EdgeInsets.all(12),
    children: [
      BehaviorCanvas(
        behavior: _controller.behavior!,
        errors: _controller.behaviorErrors,
        onChanged: _controller.reviseBehavior,
        enabled: _editingEnabled,
      ),
      const SizedBox(height: 12),
      OutlinedButton.icon(
        key: featureStudioOpenSuggestionsKey,
        focusNode: _suggestionsLauncherFocus,
        onPressed: () => _showSuggestions(context),
        icon: const Icon(Icons.auto_awesome_outlined),
        label: const Text('Suggested changes'),
      ),
      const SizedBox(height: 8),
      OutlinedButton.icon(
        key: featureStudioOpenCodeKey,
        focusNode: _codeLauncherFocus,
        onPressed: () => _showCode(context),
        icon: const Icon(Icons.code),
        label: const Text('Code & changes'),
      ),
      const SizedBox(height: 12),
      TestResultsPanel(controller: _controller),
      const SizedBox(height: 24),
    ],
  );

  Widget _buildMedium(BuildContext context) => Row(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Expanded(
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            BehaviorCanvas(
              behavior: _controller.behavior!,
              errors: _controller.behaviorErrors,
              onChanged: _controller.reviseBehavior,
              enabled: _editingEnabled,
            ),
            const SizedBox(height: 16),
            CodeChangesDisclosure(
              source: _controller.source!,
              errors: _controller.sourceErrors,
              onChanged: _controller.reviseSource,
              enabled: _editingEnabled,
            ),
            const SizedBox(height: 16),
            TestResultsPanel(controller: _controller),
            const SizedBox(height: 24),
          ],
        ),
      ),
      SizedBox(
        width: 340,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(0, 16, 16, 16),
          child: SuggestedChangesPanel(controller: _controller),
        ),
      ),
    ],
  );

  Widget _buildWide(BuildContext context) => Row(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      SizedBox(
        width: 188,
        child: _SectionNavigation(
          onBehavior: () =>
              _activateSection(_behaviorSectionAnchor, _behaviorSectionFocus),
          onSuggestedChanges: () => _activateSection(
            _suggestionsSectionAnchor,
            _suggestionsSectionFocus,
          ),
          onCode: () => _activateSection(_codeSectionAnchor, _codeSectionFocus),
          onTestResults: () => _activateSection(
            _testResultsSectionAnchor,
            _testResultsSectionFocus,
          ),
        ),
      ),
      Expanded(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _SectionTarget(
                anchorKey: _behaviorSectionAnchor,
                sectionKey: featureStudioBehaviorSectionKey,
                focusNode: _behaviorSectionFocus,
                label: 'Behavior',
                child: BehaviorCanvas(
                  behavior: _controller.behavior!,
                  errors: _controller.behaviorErrors,
                  onChanged: _controller.reviseBehavior,
                  enabled: _editingEnabled,
                ),
              ),
              const SizedBox(height: 20),
              _SectionTarget(
                anchorKey: _codeSectionAnchor,
                sectionKey: featureStudioCodeSectionKey,
                focusNode: _codeSectionFocus,
                label: 'Code & changes',
                child: CodeChangesDisclosure(
                  source: _controller.source!,
                  errors: _controller.sourceErrors,
                  onChanged: _controller.reviseSource,
                  enabled: _editingEnabled,
                ),
              ),
              const SizedBox(height: 20),
              _SectionTarget(
                anchorKey: _testResultsSectionAnchor,
                sectionKey: featureStudioTestResultsSectionKey,
                focusNode: _testResultsSectionFocus,
                label: 'Test results',
                child: TestResultsPanel(controller: _controller),
              ),
              const SizedBox(height: 32),
            ],
          ),
        ),
      ),
      SizedBox(
        width: 360,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(0, 20, 20, 20),
          child: _SectionTarget(
            anchorKey: _suggestionsSectionAnchor,
            sectionKey: featureStudioSuggestionsSectionKey,
            focusNode: _suggestionsSectionFocus,
            label: 'Suggested changes',
            child: SuggestedChangesPanel(controller: _controller),
          ),
        ),
      ),
    ],
  );

  Future<void> _activateSection(
    GlobalKey anchorKey,
    FocusNode focusNode,
  ) async {
    final targetContext = anchorKey.currentContext;
    if (targetContext == null) return;
    await Scrollable.ensureVisible(
      targetContext,
      alignment: 0.05,
      duration: const Duration(milliseconds: 220),
      curve: Curves.easeOutCubic,
    );
    if (mounted) focusNode.requestFocus();
  }

  Future<void> _showSuggestions(BuildContext context) async {
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      useRootNavigator: false,
      builder: (sheetContext) => CallbackShortcuts(
        bindings: {
          const SingleActivator(LogicalKeyboardKey.escape): () =>
              Navigator.of(sheetContext).pop(),
        },
        child: FractionallySizedBox(
          heightFactor: 0.9,
          child: Padding(
            padding: const EdgeInsets.all(8),
            child: AnimatedBuilder(
              animation: _controller,
              builder: (context, _) =>
                  SuggestedChangesPanel(controller: _controller),
            ),
          ),
        ),
      ),
    );
    if (mounted) _suggestionsLauncherFocus.requestFocus();
  }

  Future<void> _showCode(BuildContext context) async {
    await showDialog<void>(
      context: context,
      useSafeArea: false,
      useRootNavigator: false,
      builder: (dialogContext) => CallbackShortcuts(
        bindings: {
          const SingleActivator(LogicalKeyboardKey.escape): () =>
              Navigator.of(dialogContext).pop(),
        },
        child: Dialog.fullscreen(
          child: Scaffold(
            appBar: AppBar(
              title: const Text('Code & changes'),
              leading: IconButton(
                tooltip: 'Close Code & changes',
                onPressed: () => Navigator.of(dialogContext).pop(),
                icon: const Icon(Icons.close),
              ),
            ),
            body: SingleChildScrollView(
              padding: const EdgeInsets.all(12),
              child: AnimatedBuilder(
                animation: _controller,
                builder: (context, _) => CodeChangesDisclosure(
                  source: _controller.source!,
                  errors: _controller.sourceErrors,
                  onChanged: _controller.reviseSource,
                  enabled: _editingEnabled,
                ),
              ),
            ),
          ),
        ),
      ),
    );
    if (mounted) _codeLauncherFocus.requestFocus();
  }

  Future<bool> _requestExit([bool navigate = true]) async {
    if (_exitApproved) return true;
    if (_exitInProgress) return false;
    _exitInProgress = true;
    try {
      if ((_controller.isDirty || _controller.hasUnresolvedMutation) &&
          !_controller.hasConflict &&
          _controller.behaviorErrors.isEmpty &&
          _controller.sourceErrors.isEmpty) {
        await _controller.saveNow();
      }
      if (!_controller.isDirty &&
          !_controller.hasConflict &&
          !_controller.hasUnresolvedMutation) {
        return _approveExit(navigate);
      }
      if (!_authenticatedSubtreeAvailable) return false;
      if (!mounted) return false;
      final discard = await showDialog<bool>(
        context: context,
        barrierDismissible: false,
        useRootNavigator: false,
        builder: (context) => AlertDialog(
          key: featureStudioLeaveDialogKey,
          title: const Text('Leave Feature Studio?'),
          content: const Text(
            'This Draft has changes that cannot be saved safely. Stay to review them or discard them and leave.',
          ),
          actions: [
            TextButton(
              key: featureStudioStayButtonKey,
              autofocus: true,
              onPressed: () => Navigator.of(context).pop(false),
              child: const Text('Stay'),
            ),
            TextButton(
              key: featureStudioDiscardButtonKey,
              onPressed: () => Navigator.of(context).pop(true),
              child: const Text('Discard changes'),
            ),
          ],
        ),
      );
      if (discard == true) return _approveExit(navigate);
      return false;
    } finally {
      _exitInProgress = false;
    }
  }

  bool get _authenticatedSubtreeAvailable {
    if (!mounted || !TickerMode.valuesOf(context).enabled) return false;
    final scope = context.getInheritedWidgetOfExactType<AppSessionScope>();
    return scope == null ||
        scope.notifier?.controller?.session.isAuthenticated == true;
  }

  Future<bool> _approveExit(bool navigate) async {
    if (!mounted) return false;
    setState(() => _exitApproved = true);
    await WidgetsBinding.instance.endOfFrame;
    if (!mounted) return false;
    if (navigate) widget.onBackToChat();
    return true;
  }

  bool get _editingEnabled =>
      _controller.isMutableDraft && !_controller.conflictRecoveryInFlight;
}

class _SectionNavigation extends StatelessWidget {
  const _SectionNavigation({
    required this.onBehavior,
    required this.onSuggestedChanges,
    required this.onCode,
    required this.onTestResults,
  });

  final VoidCallback onBehavior;
  final VoidCallback onSuggestedChanges;
  final VoidCallback onCode;
  final VoidCallback onTestResults;

  @override
  Widget build(BuildContext context) => Material(
    color: Theme.of(context).colorScheme.surfaceContainerLowest,
    child: ListView(
      padding: const EdgeInsets.fromLTRB(12, 20, 12, 12),
      children: [
        _SectionControl(
          key: featureStudioNavigateBehaviorKey,
          icon: Icons.rule_outlined,
          label: 'Behavior',
          onPressed: onBehavior,
        ),
        _SectionControl(
          key: featureStudioNavigateSuggestedChangesKey,
          icon: Icons.auto_awesome_outlined,
          label: 'Suggested changes',
          onPressed: onSuggestedChanges,
        ),
        _SectionControl(
          key: featureStudioNavigateCodeKey,
          icon: Icons.code,
          label: 'Code & changes',
          onPressed: onCode,
        ),
        _SectionControl(
          key: featureStudioNavigateTestResultsKey,
          icon: Icons.fact_check_outlined,
          label: 'Test results',
          onPressed: onTestResults,
        ),
      ],
    ),
  );
}

class _SectionControl extends StatelessWidget {
  const _SectionControl({
    super.key,
    required this.icon,
    required this.label,
    required this.onPressed,
  });

  final IconData icon;
  final String label;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) => TextButton.icon(
    onPressed: onPressed,
    icon: Icon(icon, size: 19),
    label: Align(alignment: Alignment.centerLeft, child: Text(label)),
    style: TextButton.styleFrom(
      alignment: Alignment.centerLeft,
      padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 8),
    ),
  );
}

class _SectionTarget extends StatelessWidget {
  const _SectionTarget({
    required this.anchorKey,
    required this.sectionKey,
    required this.focusNode,
    required this.label,
    required this.child,
  });

  final GlobalKey anchorKey;
  final Key sectionKey;
  final FocusNode focusNode;
  final String label;
  final Widget child;

  @override
  Widget build(BuildContext context) => Focus(
    key: anchorKey,
    focusNode: focusNode,
    child: Semantics(
      key: sectionKey,
      container: true,
      label: '$label section',
      child: child,
    ),
  );
}

class _ConflictBanner extends StatelessWidget {
  const _ConflictBanner({required this.controller});

  final FeatureStudioController controller;

  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    container: true,
    label: 'This Draft changed elsewhere. Choose how to continue.',
    child: Material(
      key: featureStudioConflictKey,
      color: Theme.of(context).colorScheme.errorContainer,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
        child: Wrap(
          spacing: 10,
          runSpacing: 8,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            const Text('This Draft changed elsewhere. Choose how to continue.'),
            TextButton(
              onPressed: controller.conflictRecoveryInFlight
                  ? null
                  : controller.resolveConflictUsingServerDraft,
              child: const Text('Use server version'),
            ),
            OutlinedButton(
              onPressed: controller.conflictRecoveryInFlight
                  ? null
                  : controller.resolveConflictKeepingLocalChanges,
              child: const Text('Retry my changes'),
            ),
          ],
        ),
      ),
    ),
  );
}

class _RetrySaveBanner extends StatelessWidget {
  const _RetrySaveBanner({required this.controller});

  final FeatureStudioController controller;

  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    container: true,
    label: 'Changes are waiting to be saved.',
    child: Material(
      color: Theme.of(context).colorScheme.surfaceContainerHigh,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 8),
        child: Row(
          children: [
            const Expanded(child: Text('Changes are waiting to be saved.')),
            OutlinedButton(
              onPressed: controller.retrySave,
              child: const Text('Try again'),
            ),
          ],
        ),
      ),
    ),
  );
}

class _LoadFailure extends StatelessWidget {
  const _LoadFailure({
    required this.title,
    required this.message,
    required this.onBackToChat,
    this.onRetry,
  });

  final String title;
  final String message;
  final Future<void> Function() onBackToChat;
  final Future<void> Function()? onRetry;

  @override
  Widget build(BuildContext context) => Material(
    color: Theme.of(context).scaffoldBackgroundColor,
    child: Center(
      child: ConstrainedBox(
        key: featureStudioLoadErrorKey,
        constraints: const BoxConstraints(maxWidth: 440),
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Icon(
                Icons.edit_note_outlined,
                size: 42,
                color: Theme.of(context).colorScheme.primary,
              ),
              const SizedBox(height: 16),
              Text(title, style: Theme.of(context).textTheme.headlineSmall),
              const SizedBox(height: 8),
              Text(message),
              const SizedBox(height: 20),
              if (onRetry != null) ...[
                OutlinedButton(
                  onPressed: () => unawaited(onRetry!()),
                  child: const Text('Try again'),
                ),
                const SizedBox(height: 8),
              ],
              TextButton(
                key: featureStudioBackToChatButtonKey,
                onPressed: onBackToChat,
                child: const Text('Back to Chat'),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

class _SaveIntent extends Intent {
  const _SaveIntent();
}

class _VerifyIntent extends Intent {
  const _VerifyIntent();
}

class _DismissIntent extends Intent {
  const _DismissIntent();
}
