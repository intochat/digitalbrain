import 'dart:async';

import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';

import 'api.dart';

class TelegramApp extends StatelessWidget {
  const TelegramApp({super.key, required this.initData, this.api});
  final String initData;
  final TelegramApi? api;

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'DigitalBrain reminders',
    theme: ThemeData(colorSchemeSeed: const Color(0xff287bb8)),
    home: initData.isEmpty
        ? const Scaffold(
            body: Center(child: Text('Open this app from your Telegram bot.')),
          )
        : InboxPage(api: api ?? TelegramApi(initData)),
  );
}

class InboxPage extends StatefulWidget {
  const InboxPage({super.key, required this.api});
  final TelegramApi api;
  @override
  State<InboxPage> createState() => _InboxPageState();
}

class _InboxPageState extends State<InboxPage> {
  Map<String, dynamic>? _state;
  String? _error;
  bool _loading = false;
  final Set<String> _pending = {};
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    unawaited(_refresh());
    _poll = Timer.periodic(
      const Duration(seconds: 10),
      (_) => unawaited(_refresh()),
    );
  }

  @override
  void dispose() {
    _poll?.cancel();
    widget.api.close();
    super.dispose();
  }

  Future<void> _refresh() async {
    if (_loading) return;
    setState(() => _loading = true);
    try {
      final value = await widget.api.state();
      if (mounted) {
        setState(() {
          _state = value;
          _error = null;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(
          () => _error = error.toString().replaceFirst('Bad state: ', ''),
        );
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _act(String key, Future<void> Function() action) async {
    setState(() => _pending.add(key));
    try {
      await action();
      // Commands are durably accepted before the inbox projection catches up.
      await Future<void>.delayed(const Duration(milliseconds: 350));
      if (mounted) await _refresh();
    } catch (error) {
      if (mounted) {
        setState(
          () => _error = error.toString().replaceFirst('Bad state: ', ''),
        );
      }
    } finally {
      if (mounted) setState(() => _pending.remove(key));
    }
  }

  @override
  Widget build(BuildContext context) {
    final notifications = (_state?['notifications'] as List? ?? [])
        .cast<Map<String, dynamic>>()
        .where((item) => item['dismissed'] != true)
        .toList();
    final reminders = (_state?['reminders'] as List? ?? [])
        .cast<Map<String, dynamic>>()
        .where((item) => item['status'].toString().toLowerCase() == 'scheduled')
        .toList();
    final automation = _state?['automation'] as Map<String, dynamic>?;
    final status = automation?['status']?.toString().toLowerCase();
    return Scaffold(
      appBar: AppBar(
        title: const Text('Inbox & reminders'),
        actions: [
          IconButton(
            onPressed: _loading ? null : _refresh,
            tooltip: 'Refresh',
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: _refresh,
          child: ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            children: [
              if (_loading) const LinearProgressIndicator(),
              if (_error != null)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 12),
                  child: Semantics(liveRegion: true, child: Text(_error!)),
                ),
              if (automation?['hasErrors'] == true)
                const Card(
                  child: Padding(
                    padding: EdgeInsets.all(12),
                    child: Text(
                      'Reminder processing needs attention. Your message is saved; check the automation in DigitalBrain.',
                    ),
                  ),
                ),
              if (status == 'stopped')
                const Card(
                  child: Padding(
                    padding: EdgeInsets.all(12),
                    child: Text(
                      'Automation stopped. New messages will not schedule reminders until it is started in DigitalBrain.',
                    ),
                  ),
                ),
              Text(
                'Notifications',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              if (_state != null && notifications.isEmpty)
                const Padding(
                  padding: EdgeInsets.all(16),
                  child: Text(
                    'Your inbox is clear. Send a message to the bot to get started.',
                  ),
                ),
              for (final item in notifications)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 6),
                  child: UiNotificationCard(
                    eventId: item['eventId'] as String,
                    title: item['title'] as String,
                    message: item['message'] as String,
                    kind: item['kind'] as String,
                    createdUnixSeconds: item['createdUnixSeconds'] as int,
                    dismissing: _pending.contains('n:${item['eventId']}'),
                    onDismiss: () => _act(
                      'n:${item['eventId']}',
                      () => widget.api.dismiss(item['eventId'] as String),
                    ),
                  ),
                ),
              const SizedBox(height: 24),
              Text(
                'Scheduled reminders',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              if (_state != null && reminders.isEmpty)
                const Padding(
                  padding: EdgeInsets.all(16),
                  child: Text('No scheduled reminders.'),
                ),
              for (final item in reminders)
                Card(
                  child: ListTile(
                    title: Text(item['text'] as String),
                    subtitle: Text(
                      DateTime.fromMillisecondsSinceEpoch(
                        (item['dueUnixSeconds'] as int) * 1000,
                      ).toLocal().toString(),
                    ),
                    trailing: TextButton(
                      onPressed: _pending.contains('r:${item['reminderId']}')
                          ? null
                          : () => _act(
                              'r:${item['reminderId']}',
                              () => widget.api.cancel(
                                item['reminderId'] as String,
                              ),
                            ),
                      child: const Text('Cancel'),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
