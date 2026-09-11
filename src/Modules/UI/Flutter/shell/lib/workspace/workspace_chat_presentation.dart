part of 'workspace_chat.dart';

extension _WorkspaceChatPresentation on _WorkspaceChatState {
  Widget _message(Map<String, dynamic> entry) {
    final colors = Theme.of(context).colorScheme;
    if (entry['role'] == 'tool') return _toolMessage(entry);
    final user = entry['role'] == 'user';
    final text = entry['text'] as String? ?? '';
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Align(
        alignment: user ? Alignment.centerRight : Alignment.centerLeft,
        child: Container(
          constraints: const BoxConstraints(maxWidth: 720),
          padding: user
              ? const EdgeInsets.all(16)
              : const EdgeInsets.symmetric(vertical: 4),
          decoration: user
              ? BoxDecoration(
                  color: colors.surfaceContainerLow,
                  borderRadius: BorderRadius.circular(16),
                )
              : null,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              if (!user) ...[
                Text(
                  'IntoChat',
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w600,
                    color: colors.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 12),
              ],
              DefaultTextStyle.merge(
                style: TextStyle(
                  fontSize: 15,
                  height: 1.65,
                  color: colors.onSurface,
                ),
                child: SelectionArea(
                  child: GptMarkdown(text, onLinkTap: (url, _) => _open(url)),
                ),
              ),
              if (!user && text.isNotEmpty && !_running)
                Align(
                  alignment: Alignment.centerLeft,
                  child: IconButton(
                    tooltip: 'Copy response',
                    visualDensity: VisualDensity.compact,
                    icon: Icon(
                      Icons.copy_outlined,
                      size: 15,
                      color: colors.onSurfaceVariant,
                    ),
                    onPressed: () =>
                        Clipboard.setData(ClipboardData(text: text)),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _toolMessage(Map<String, dynamic> entry) {
    final colors = Theme.of(context).colorScheme;
    final result = entry['result'];
    final map = result is Map ? result : null;
    final kind = map?['kind'];
    if (kind == 'connection' && map?['service'] == 'salesforce') {
      final needsLogin = map?['status'] == 'authentication_required';
      final loginUrl = map?['loginUrl'] as String?;
      return Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        child: Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: colors.surfaceContainerLow,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: colors.outlineVariant),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(Icons.cloud_outlined, color: colors.primary),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Text(
                      needsLogin
                          ? 'Connect Salesforce'
                          : 'Salesforce connected',
                      style: const TextStyle(fontWeight: FontWeight.w600),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              Text(
                needsLogin
                    ? 'Sign in securely in your browser. Your request will continue automatically.'
                    : (map?['instanceUrl'] as String? ??
                          'Your account is ready.'),
              ),
              if (needsLogin) ...[
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    if (loginUrl != null)
                      FilledButton.icon(
                        onPressed: () => _open(loginUrl),
                        icon: const Icon(Icons.open_in_new, size: 16),
                        label: const Text('Sign in to Salesforce'),
                      ),
                    TextButton(
                      onPressed: _running
                          ? null
                          : () {
                              _composer.text = 'Continue my Salesforce setup review using the connected org and show its structure in the workspace.';
                              _send();
                            },
                      child: const Text('Continue after sign-in'),
                    ),
                  ],
                ),
              ],
            ],
          ),
        ),
      );
    }
    if (['table', 'diagram', 'brain', 'image', 'document'].contains(kind)) {
      return Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        child: Material(
          color: colors.surfaceContainerLow,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(10),
            side: BorderSide(color: colors.outlineVariant),
          ),
          clipBehavior: Clip.antiAlias,
          child: ListTile(
            leading: Icon(
              switch (kind) {
                'table' => Icons.table_chart_outlined,
                'image' => Icons.image_outlined,
                'brain' => Icons.hub_outlined,
                'diagram' => Icons.draw_outlined,
                _ => Icons.description_outlined,
              },
              size: 21,
              color: colors.primary,
            ),
            title: Text(
              map?['title'] as String? ?? 'Saved artifact',
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w500),
            ),
            subtitle: const Text(
              'Open in workspace',
              style: TextStyle(fontSize: 12),
            ),
            trailing: const Icon(Icons.arrow_outward, size: 17),
            onTap: () => widget.onArtifact(Map<String, dynamic>.from(map!)),
          ),
        ),
      );
    }
    final name = entry['name'] as String? ?? 'Tool';
    final label = switch (name) {
      'search_web' => 'Web search',
      'read_artifact' => 'Reading project work',
      'list_artifacts' => 'Looking through project work',
      'read_table' => 'Reading table',
      _ => name.replaceAll('_', ' '),
    };
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      child: ExpansionTile(
        key: PageStorageKey(entry['id']),
        dense: true,
        shape: const Border(),
        collapsedShape: const Border(),
        leading: Icon(
          entry['complete'] == true ? Icons.check : Icons.more_horiz,
          size: 16,
          color: colors.onSurfaceVariant,
        ),
        title: Text(
          label,
          style: TextStyle(fontSize: 12, color: colors.onSurfaceVariant),
        ),
        children: [
          if (map?['results'] is List)
            for (final source in (map!['results'] as List).whereType<Map>())
              ListTile(
                title: Text(source['title'] as String? ?? 'Source'),
                subtitle: Text(
                  source['url'] as String? ?? '',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                onTap: () => _open(source['url'] as String? ?? ''),
              )
          else
            Padding(
              padding: const EdgeInsets.all(16),
              child: SelectableText(
                result == null
                    ? 'Working…'
                    : result is String
                    ? result
                    : const JsonEncoder.withIndent('  ').convert(result),
                style: TextStyle(fontSize: 12, color: colors.onSurfaceVariant),
              ),
            ),
        ],
      ),
    );
  }

  Widget _chatSurface(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final selected = [uiCoordinator, ...uiSpecialists].firstWhere(
      (s) => s.id == widget.conversation.selectedAgentId,
      orElse: () => uiCoordinator,
    );
    return Column(
      children: [
        Expanded(
          child: UiChat(
            workspaceTheme: true,
            chatController: _controller,
            currentUserId: 'user',
            resolveUser: (id) async =>
                User(id: id, name: id == 'user' ? 'You' : 'IntoChat'),
            builders: Builders(
              composerBuilder: (_) => const SizedBox.shrink(),
              emptyChatListBuilder: (_) => Center(
                child: SingleChildScrollView(
                  padding: const EdgeInsets.all(28),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      UiSpecialistIcon(specialist: selected, size: 44),
                      const SizedBox(height: 24),
                      Text(
                        'What are we working on?',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          fontSize: 24,
                          fontWeight: FontWeight.w400,
                          letterSpacing: -.7,
                          color: colors.onSurface,
                        ),
                      ),
                      const SizedBox(height: 12),
                      Text(
                        'Think it through, find an answer,\nor create something together.',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          fontSize: 14,
                          height: 1.6,
                          color: colors.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              customMessageBuilder: (
                context,
                message,
                index, {
                required isSentByMe,
                groupStatus,
              }) => _message(message.metadata ?? {}),
            ),
          ),
        ),
        if (_running)
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 6),
            child: Align(
              alignment: Alignment.centerLeft,
              child: Text(
                'Working…',
                style: TextStyle(fontSize: 12, color: colors.onSurfaceVariant),
              ),
            ),
          ),
        if (_notice != null)
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 8),
            child: Text(
              _notice!,
              style: TextStyle(fontSize: 12, color: colors.error),
            ),
          ),
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
          child: Container(
            key: const Key('chat-composer'),
            decoration: BoxDecoration(
              color: colors.surfaceContainerLow,
              border: Border.all(color: colors.outlineVariant),
              borderRadius: BorderRadius.circular(18),
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (widget.conversation.attachedArtifactIds.isNotEmpty)
                  SingleChildScrollView(
                    scrollDirection: Axis.horizontal,
                    padding: const EdgeInsets.fromLTRB(12, 10, 12, 0),
                    child: Row(
                      children: [
                        for (final id
                            in widget.conversation.attachedArtifactIds)
                          Padding(
                            padding: const EdgeInsets.only(right: 6),
                            child: InputChip(
                              label: ConstrainedBox(
                                constraints: const BoxConstraints(
                                  maxWidth: 160,
                                ),
                                child: Text(
                                  _project.artifacts
                                          .where((a) => a.id == id)
                                          .firstOrNull
                                          ?.title ??
                                      id,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                              onPressed: () => widget.store.openArtifact(id),
                              onDeleted: () => widget.store.detachArtifact(id),
                            ),
                          ),
                      ],
                    ),
                  ),
                CallbackShortcuts(
                  bindings: {
                    const SingleActivator(
                      LogicalKeyboardKey.enter,
                      shift: true,
                    ): () {
                      final value = _composer.value;
                      if (value.composing.isValid &&
                          !value.composing.isCollapsed) {
                        return;
                      }
                      final selection = value.selection;
                      final start = selection.isValid
                          ? selection.start
                          : value.text.length;
                      final end = selection.isValid
                          ? selection.end
                          : value.text.length;
                      _composer.value = TextEditingValue(
                        text: value.text.replaceRange(start, end, '\n'),
                        selection: TextSelection.collapsed(offset: start + 1),
                      );
                    },
                    const SingleActivator(LogicalKeyboardKey.enter): () {
                      if (_composer.value.composing.isValid &&
                          !_composer.value.composing.isCollapsed) {
                        return;
                      }
                      _send();
                    },
                  },
                  child: TextField(
                    key: const Key('chat-message-input'),
                    controller: _composer,
                    focusNode: _composerFocus,
                    minLines: 2,
                    maxLines: 6,
                    keyboardType: TextInputType.multiline,
                    style: const TextStyle(fontSize: 15, height: 1.5),
                    decoration: InputDecoration(
                      hintText: widget.onRun == null
                          ? 'Connect to start a conversation'
                          : 'Message ${selected.name}…',
                      hintStyle: TextStyle(
                        color: colors.onSurfaceVariant,
                        fontSize: 14,
                      ),
                      filled: false,
                      contentPadding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
                      border: InputBorder.none,
                      enabledBorder: InputBorder.none,
                      focusedBorder: InputBorder.none,
                    ),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(6, 0, 8, 6),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      Expanded(
                        child: Wrap(
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: [
                            IconButton(
                              tooltip: 'Attach project work',
                              onPressed: widget.onAttach,
                              icon: const Icon(Icons.add, size: 21),
                            ),
                            PopupMenuButton<String>(
                              tooltip: 'Choose specialist',
                              onSelected: widget.store.setAgent,
                              itemBuilder: (_) => [
                                for (final s in [
                                  uiCoordinator,
                                  ...uiSpecialists,
                                ])
                                  PopupMenuItem(
                                    value: s.id,
                                    child: Row(
                                      children: [
                                        UiSpecialistIcon(
                                          specialist: s,
                                          size: 28,
                                        ),
                                        const SizedBox(width: 12),
                                        Expanded(child: Text(s.name)),
                                      ],
                                    ),
                                  ),
                              ],
                              child: Padding(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 6,
                                  vertical: 12,
                                ),
                                child: Row(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    ConstrainedBox(
                                      constraints: const BoxConstraints(
                                        maxWidth: 120,
                                      ),
                                      child: Text(
                                        selected.name,
                                        maxLines: 1,
                                        overflow: TextOverflow.ellipsis,
                                        style: TextStyle(
                                          fontSize: 12,
                                          color: colors.onSurfaceVariant,
                                        ),
                                      ),
                                    ),
                                    const SizedBox(width: 4),
                                    const Icon(
                                      Icons.keyboard_arrow_down,
                                      size: 15,
                                    ),
                                  ],
                                ),
                              ),
                            ),
                            WorkspaceVoiceButton(
                              onTranscribe: widget.onTranscribe,
                              onDraft: (draft) {
                                _composer.text = draft;
                                _composerFocus.requestFocus();
                              },
                              enabled: !_running && widget.active,
                            ),
                          ],
                        ),
                      ),
                      ValueListenableBuilder<TextEditingValue>(
                        valueListenable: _composer,
                        builder: (context, value, _) => IconButton.filled(
                          tooltip: _running ? 'Stop response' : 'Send message',
                          style: IconButton.styleFrom(
                            backgroundColor: colors.onSurface,
                            foregroundColor: colors.surface,
                          ),
                          onPressed: _running
                              ? () => _finish('Response stopped.')
                              : widget.onRun == null ||
                                    value.text.trim().isEmpty
                              ? null
                              : _send,
                          icon: Icon(
                            _running ? Icons.stop_rounded : Icons.arrow_upward,
                            size: 20,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}
