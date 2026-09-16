import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;

import 'brain_session_gate.dart' show ShellBuilder;

/// Telegram proof is exchanged for an account session before any workspace API opens.
class TelegramSessionGate extends StatefulWidget {
  const TelegramSessionGate({
    super.key,
    required this.initData,
    required this.baseUri,
    required this.builder,
    this.httpClient,
  });
  final String initData;
  final Uri baseUri;
  final ShellBuilder builder;
  final http.Client? httpClient;

  @override
  State<TelegramSessionGate> createState() => _TelegramSessionGateState();
}

class _TelegramSessionGateState extends State<TelegramSessionGate> {
  late final http.Client _http = widget.httpClient ?? http.Client();
  final _code = TextEditingController();
  DigitalBrainUiClient? _client;
  bool _busy = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _authenticate();
  }

  Future<void> _authenticate({bool link = false}) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      if (widget.initData.isEmpty) {
        throw StateError('Open this app from the Telegram bot to sign in.');
      }
      final response = await _http.post(
        widget.baseUri.resolve(
          link ? '/identity/link/redeem/telegram' : '/identity/login/telegram',
        ),
        headers: {
          'content-type': 'application/json',
          'X-DigitalBrain-Session-Transport': 'bearer',
        },
        body: jsonEncode({
          'credential': widget.initData,
          if (link) 'code': _code.text.trim(),
        }),
      );
      if (response.statusCode != 200) {
        throw StateError(
          link
              ? 'Linking failed. Generate a new code in desktop Settings and try again.'
              : 'Link your Telegram account using a code from desktop Settings → Connections.',
        );
      }
      final session = jsonDecode(response.body) as Map<String, dynamic>;
      if (session['isOwner'] != true) {
        throw StateError(
          'This workspace currently requires its owner account.',
        );
      }
      final client = DigitalBrainUiClient(
        baseUri: widget.baseUri,
        bearerToken: session['token'] as String,
        accountId: session['userId'] as String,
      );
      if (!mounted) {
        client.close();
        return;
      }
      setState(() => _client = client);
    } catch (error) {
      if (mounted) {
        setState(
          () => _error = error is StateError
              ? error.message.toString()
              : 'Cannot connect. Please try again.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  void dispose() {
    _code.dispose();
    _client?.close();
    if (widget.httpClient == null) _http.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_client case final client?) return widget.builder(client, null);
    return MaterialApp(
      title: 'IntoChat',
      debugShowCheckedModeBanner: false,
      theme: UiTheme.dark(),
      home: Scaffold(
        body: SafeArea(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(24),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 440),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Text(
                      'Your projects in Telegram',
                      style: TextStyle(fontSize: 24),
                    ),
                    const SizedBox(height: 20),
                    if (_busy)
                      const CircularProgressIndicator()
                    else ...[
                      if (_error != null) Text(_error!),
                      const SizedBox(height: 20),
                      if (widget.initData.isNotEmpty) ...[
                        TextField(
                          controller: _code,
                          decoration: const InputDecoration(
                            labelText: 'One-use linking code',
                          ),
                        ),
                        const SizedBox(height: 12),
                        FilledButton(
                          onPressed: () => _authenticate(link: true),
                          child: const Text('Link and open projects'),
                        ),
                        TextButton(
                          onPressed: _authenticate,
                          child: const Text('Retry sign-in'),
                        ),
                      ],
                    ],
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
