import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/persona/persona_state.dart';
import 'package:ino_flutter/state/persona_bloc.dart';
import 'package:mocktail/mocktail.dart';

class MockInoGrpcClient extends Mock implements InoGrpcClient {}

void main() {
  late MockInoGrpcClient mockClient;

  setUp(() {
    mockClient = MockInoGrpcClient();
  });

  group('PersonaBloc', () {
    test('initial state is sleeping', () {
      final bloc = PersonaBloc(client: mockClient);
      expect(bloc.state.emotion, PersonaEmotion.sleeping);
      expect(bloc.state.energy, 0.5);
      expect(bloc.state.confidence, 1.0);
      bloc.close();
    });

    blocTest<PersonaBloc, PersonaStateModel>(
      'PersonaEmotionChanged updates emotion',
      setUp: () {
        when(() => mockClient.streamPersonaState(
              userId: any(named: 'userId'),
            )).thenAnswer((_) => const Stream.empty());
      },
      build: () => PersonaBloc(client: mockClient),
      act: (bloc) => bloc.add(PersonaEmotionChanged(PersonaEmotion.thinking)),
      expect: () => [
        isA<PersonaStateModel>()
            .having((s) => s.emotion, 'emotion', PersonaEmotion.thinking),
      ],
    );

    blocTest<PersonaBloc, PersonaStateModel>(
      'PersonaStarted transitions sleeping -> waking -> idle',
      setUp: () {
        when(() => mockClient.streamPersonaState(
              userId: any(named: 'userId'),
            )).thenAnswer((_) => const Stream.empty());
      },
      build: () => PersonaBloc(client: mockClient),
      act: (bloc) => bloc.add(PersonaStarted()),
      wait: const Duration(seconds: 1),
      expect: () => [
        isA<PersonaStateModel>()
            .having((s) => s.emotion, 'emotion', PersonaEmotion.waking),
        isA<PersonaStateModel>()
            .having((s) => s.emotion, 'emotion', PersonaEmotion.idle),
      ],
    );
  });
}
