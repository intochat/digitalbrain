import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;
import 'package:ino_flutter/state/ino_bloc.dart';
import 'package:ino_flutter/voice/audio_transport.dart';
import 'package:ino_flutter/voice/audio_recorder.dart';
import 'package:mocktail/mocktail.dart';

class MockInoGrpcClient extends Mock implements InoGrpcClient {}

class MockAudioTransport extends Mock implements AudioTransport {}

class MockAudioRecorderService extends Mock implements AudioRecorderService {}

void main() {
  late MockInoGrpcClient mockClient;
  late MockAudioTransport mockTransport;
  late MockAudioRecorderService mockRecorder;

  setUp(() {
    mockClient = MockInoGrpcClient();
    mockTransport = MockAudioTransport();
    mockRecorder = MockAudioRecorderService();
  });

  InoBloc buildBloc() => InoBloc(
        client: mockClient,
        audioTransport: mockTransport,
        recorder: mockRecorder,
      );

  group('InoBloc', () {
    test('initial state has empty messages and is not loading or recording',
        () {
      final bloc = buildBloc();
      expect(bloc.state.messages, isEmpty);
      expect(bloc.state.isLoading, isFalse);
      expect(bloc.state.isRecording, isFalse);
      bloc.close();
    });

    blocTest<InoBloc, InoBlocState>(
      'SendMessage adds user message, calls chat, adds reply',
      setUp: () {
        when(() => mockClient.chat('hello', userId: any(named: 'userId')))
            .thenAnswer(
          (_) => Stream.value(pb.ChatResponse()..reply = 'hi there'),
        );
      },
      build: buildBloc,
      act: (bloc) => bloc.add(SendMessage('hello')),
      wait: const Duration(milliseconds: 100),
      expect: () => [
        isA<InoBlocState>()
            .having((s) => s.messages.length, 'messages.length', 1)
            .having((s) => s.messages.first.text, 'first.text', 'hello')
            .having((s) => s.messages.first.isUser, 'first.isUser', true)
            .having((s) => s.isLoading, 'isLoading', true),
        isA<InoBlocState>()
            .having((s) => s.messages.length, 'messages.length', 2)
            .having((s) => s.messages.last.text, 'last.text', 'hi there')
            .having((s) => s.messages.last.isUser, 'last.isUser', false)
            .having((s) => s.isLoading, 'isLoading', false),
      ],
    );

    blocTest<InoBloc, InoBlocState>(
      'SendMessage handles error gracefully',
      setUp: () {
        when(() => mockClient.chat(any(), userId: any(named: 'userId')))
            .thenThrow(Exception('network error'));
      },
      build: buildBloc,
      act: (bloc) => bloc.add(SendMessage('test')),
      wait: const Duration(milliseconds: 100),
      expect: () => [
        isA<InoBlocState>().having((s) => s.isLoading, 'isLoading', true),
        isA<InoBlocState>()
            .having((s) => s.messages.length, 'messages.length', 2)
            .having((s) => s.messages.last.isUser, 'last.isUser', false)
            .having((s) => s.isLoading, 'isLoading', false),
      ],
    );

    blocTest<InoBloc, InoBlocState>(
      'StartRecording denied when no permission',
      setUp: () {
        when(() => mockRecorder.hasPermission())
            .thenAnswer((_) async => false);
      },
      build: buildBloc,
      act: (bloc) => bloc.add(StartRecording()),
      wait: const Duration(milliseconds: 100),
      expect: () => [
        isA<InoBlocState>()
            .having((s) => s.isRecording, 'isRecording', false)
            .having((s) => s.messages.length, 'messages.length', 1)
            .having((s) => s.messages.first.text, 'text',
                contains('permission denied')),
      ],
    );

    blocTest<InoBloc, InoBlocState>(
      'StartRecording sets isRecording true when permitted',
      setUp: () {
        when(() => mockRecorder.hasPermission())
            .thenAnswer((_) async => true);
        when(() => mockRecorder.startRecording())
            .thenAnswer((_) async => const Stream.empty());
      },
      build: buildBloc,
      act: (bloc) => bloc.add(StartRecording()),
      wait: const Duration(milliseconds: 100),
      expect: () => [
        isA<InoBlocState>().having((s) => s.isRecording, 'isRecording', true),
      ],
    );
  });
}
