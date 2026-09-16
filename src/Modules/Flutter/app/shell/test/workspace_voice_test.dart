import 'dart:io';

import 'package:digitalbrain_flutter_shell/workspace/voice_file_io.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_voice.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'audio cleanup tolerates a recorder deleting the same capture',
    () async {
      final directory = Directory.systemTemp.createTempSync(
        'voice_cleanup_test',
      );
      addTearDown(() => directory.deleteSync(recursive: true));
      final path = '${directory.path}/capture.wav';
      for (var attempt = 0; attempt < 100; attempt++) {
        File(path).writeAsBytesSync([1, 2, 3]);
        await Future.wait([deleteVoiceFile(path), deleteVoiceFile(path)]);
        await deleteVoiceFile(path);
        expect(File(path).existsSync(), isFalse);
      }
      final invalidPath = Directory('${directory.path}/not-a-recording')
        ..createSync();
      File('${invalidPath.path}/keep').writeAsStringSync('preserve');
      await expectLater(
        deleteVoiceFile(invalidPath.path),
        throwsA(isA<FileSystemException>()),
      );
      expect(invalidPath.existsSync(), isTrue);
    },
  );
  testWidgets(
    'recording transcribes into a draft only after stopping and supports cancel',
    (tester) async {
      final directory = Directory.systemTemp.createTempSync(
        'workspace_voice_test',
      );
      addTearDown(() => directory.deleteSync(recursive: true));
      final messenger = tester.binding.defaultBinaryMessenger;
      const record = MethodChannel('com.llfbandit.record/messages');
      const paths = MethodChannel('plugins.flutter.io/path_provider');
      String? audioPath;
      var cancelled = 0;
      messenger.setMockMethodCallHandler(paths, (_) async => directory.path);
      messenger.setMockMethodCallHandler(record, (call) async {
        if (call.method == 'create') {
          final id = (call.arguments as Map)['recorderId'];
          messenger.setMockMethodCallHandler(
            MethodChannel('com.llfbandit.record/events/$id'),
            (_) async => null,
          );
        }
        if (call.method == 'hasPermission' ||
            call.method == 'isEncoderSupported') {
          return true;
        }
        if (call.method == 'start') {
          audioPath = (call.arguments as Map)['path'] as String;
          File(audioPath!).writeAsBytesSync([82, 73, 70, 70]);
        }
        if (call.method == 'stop') return audioPath;
        if (call.method == 'cancel') {
          cancelled++;
          if (audioPath != null && File(audioPath!).existsSync()) {
            File(audioPath!).deleteSync();
          }
          if (cancelled == 1) {
            // Force a genuine cleanup failure independently of the recorder.
            Directory(audioPath!).createSync();
            File('$audioPath/locked').writeAsStringSync('fixture');
          }
        }
        return null;
      });
      addTearDown(() {
        messenger.setMockMethodCallHandler(record, null);
        messenger.setMockMethodCallHandler(paths, null);
      });
      var transcriptions = 0;
      var failTranscription = false;
      var draft = '';
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: WorkspaceVoiceButton(
              onTranscribe: (audio, name) async {
                transcriptions++;
                if (failTranscription) {
                  throw StateError('Transcription offline');
                }
                return 'Editable transcript';
              },
              onDraft: (text) => draft = text,
            ),
          ),
        ),
      );
      await tester.pump();
      await tester.tap(find.byTooltip('Record a voice draft'));
      await tester.pumpAndSettle();
      expect(transcriptions, 0);
      expect(find.byTooltip('Cancel recording'), findsOneWidget);
      await tester.runAsync(() async {
        tester
            .widget<IconButton>(find.byKey(const Key('workspace_voice')))
            .onPressed!();
        await Future<void>.delayed(const Duration(milliseconds: 100));
      });
      await tester.pumpAndSettle();
      expect(transcriptions, 1);
      expect(draft, 'Editable transcript');
      expect(File(audioPath!).existsSync(), isFalse);
      failTranscription = true;
      await tester.tap(find.byTooltip('Record a voice draft'));
      await tester.pumpAndSettle();
      final failedPath = audioPath!;
      await tester.runAsync(() async {
        tester
            .widget<IconButton>(find.byKey(const Key('workspace_voice')))
            .onPressed!();
        await Future<void>.delayed(const Duration(milliseconds: 100));
      });
      await tester.pumpAndSettle();
      expect(File(failedPath).existsSync(), isFalse);
      expect(draft, 'Editable transcript');
      failTranscription = false;
      await tester.tap(find.byTooltip('Record a voice draft'));
      await tester.pumpAndSettle();
      await tester.runAsync(() async {
        tester
            .widget<IconButton>(
              find.byWidgetPredicate(
                (widget) =>
                    widget is IconButton &&
                    widget.tooltip == 'Cancel recording',
              ),
            )
            .onPressed!();
        await Future<void>.delayed(const Duration(milliseconds: 100));
      });
      await tester.pumpAndSettle();
      expect(cancelled, 1);
      expect(find.byTooltip('Record a voice draft'), findsOneWidget);
      expect(find.byTooltip('Cancel recording'), findsNothing);
      expect(tester.takeException(), isNull);
      expect(transcriptions, 2);
      await tester.tap(find.byTooltip('Record a voice draft'));
      await tester.pumpAndSettle();
      await tester.runAsync(() async {
        tester.binding.handleAppLifecycleStateChanged(
          AppLifecycleState.inactive,
        );
        await Future<void>.delayed(const Duration(milliseconds: 100));
      });
      await tester.pumpAndSettle();
      expect(cancelled, 2);
      expect(transcriptions, 2);
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pumpAndSettle();
    },
  );
}
