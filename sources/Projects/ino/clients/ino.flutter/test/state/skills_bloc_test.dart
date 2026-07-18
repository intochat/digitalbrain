import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;
import 'package:ino_flutter/state/skills_bloc.dart';
import 'package:mocktail/mocktail.dart';

class MockInoGrpcClient extends Mock implements InoGrpcClient {}

void main() {
  late MockInoGrpcClient mockClient;

  setUp(() {
    mockClient = MockInoGrpcClient();
  });

  group('SkillsBloc', () {
    test('initial state is empty', () {
      final bloc = SkillsBloc(client: mockClient);
      expect(bloc.state.skills, isEmpty);
      expect(bloc.state.isLoading, isFalse);
      expect(bloc.state.installingId, isNull);
      expect(bloc.state.error, isNull);
      bloc.close();
    });

    blocTest<SkillsBloc, SkillsBlocState>(
      'LoadSkills fetches and emits skills',
      setUp: () {
        when(() => mockClient.listSkills(
              domain: any(named: 'domain'),
              query: any(named: 'query'),
            )).thenAnswer(
          (_) async => pb.ListSkillsResponse()
            ..skills.addAll([
              pb.SkillInfo()
                ..id = 'trip-planner'
                ..name = 'Trip Planner'
                ..domain = 'travel'
                ..description = 'Plan trips'
                ..installed = false,
              pb.SkillInfo()
                ..id = 'code-review'
                ..name = 'Code Review'
                ..domain = 'coding'
                ..description = 'Review code'
                ..installed = true,
            ]),
        );
      },
      build: () => SkillsBloc(client: mockClient),
      act: (bloc) => bloc.add(LoadSkills()),
      wait: const Duration(milliseconds: 100),
      expect: () => [
        isA<SkillsBlocState>()
            .having((s) => s.isLoading, 'isLoading', true),
        isA<SkillsBlocState>()
            .having((s) => s.skills.length, 'skills.length', 2)
            .having((s) => s.skills.first.id, 'first.id', 'trip-planner')
            .having((s) => s.skills.last.domain, 'last.domain', 'coding')
            .having((s) => s.isLoading, 'isLoading', false),
      ],
    );

    blocTest<SkillsBloc, SkillsBlocState>(
      'LoadSkills with domain filter passes domain to client',
      setUp: () {
        when(() => mockClient.listSkills(
              domain: 'travel',
              query: any(named: 'query'),
            )).thenAnswer(
          (_) async => pb.ListSkillsResponse()
            ..skills.add(
              pb.SkillInfo()
                ..id = 'trip-planner'
                ..name = 'Trip Planner'
                ..domain = 'travel'
                ..description = 'Plan trips'
                ..installed = false,
            ),
        );
      },
      build: () => SkillsBloc(client: mockClient),
      act: (bloc) => bloc.add(LoadSkills(domain: 'travel')),
      wait: const Duration(milliseconds: 100),
      expect: () => [
        isA<SkillsBlocState>()
            .having((s) => s.isLoading, 'isLoading', true),
        isA<SkillsBlocState>()
            .having((s) => s.skills.length, 'skills.length', 1)
            .having((s) => s.skills.first.domain, 'first.domain', 'travel')
            .having((s) => s.isLoading, 'isLoading', false),
      ],
      verify: (_) {
        verify(() => mockClient.listSkills(
              domain: 'travel',
              query: '',
            )).called(1);
      },
    );

    blocTest<SkillsBloc, SkillsBlocState>(
      'LoadSkills handles error',
      setUp: () {
        when(() => mockClient.listSkills(
              domain: any(named: 'domain'),
              query: any(named: 'query'),
            )).thenThrow(Exception('network error'));
      },
      build: () => SkillsBloc(client: mockClient),
      act: (bloc) => bloc.add(LoadSkills()),
      wait: const Duration(milliseconds: 100),
      expect: () => [
        isA<SkillsBlocState>()
            .having((s) => s.isLoading, 'isLoading', true),
        isA<SkillsBlocState>()
            .having((s) => s.isLoading, 'isLoading', false)
            .having((s) => s.error, 'error', isNotNull),
      ],
    );

    blocTest<SkillsBloc, SkillsBlocState>(
      'InstallSkillRequested sets installingId then reloads',
      setUp: () {
        when(() => mockClient.installSkill('trip-planner')).thenAnswer(
          (_) async => pb.InstallSkillResponse()
            ..ok = true
            ..neuronId = 'neuron-1',
        );
        when(() => mockClient.listSkills(
              domain: any(named: 'domain'),
              query: any(named: 'query'),
            )).thenAnswer(
          (_) async => pb.ListSkillsResponse()
            ..skills.add(
              pb.SkillInfo()
                ..id = 'trip-planner'
                ..name = 'Trip Planner'
                ..domain = 'travel'
                ..description = 'Plan trips'
                ..installed = true,
            ),
        );
      },
      build: () => SkillsBloc(client: mockClient),
      act: (bloc) => bloc.add(InstallSkillRequested('trip-planner')),
      wait: const Duration(milliseconds: 200),
      expect: () => [
        isA<SkillsBlocState>()
            .having((s) => s.installingId, 'installingId', 'trip-planner'),
        isA<SkillsBlocState>()
            .having((s) => s.isLoading, 'isLoading', true),
        isA<SkillsBlocState>()
            .having((s) => s.skills.length, 'skills.length', 1)
            .having((s) => s.skills.first.installed, 'first.installed', true)
            .having((s) => s.installingId, 'installingId', isNull)
            .having((s) => s.isLoading, 'isLoading', false),
      ],
    );
  });
}
