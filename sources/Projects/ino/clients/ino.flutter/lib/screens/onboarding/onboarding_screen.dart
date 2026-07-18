import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:ino_flutter/persona/persona_state.dart';
import 'package:ino_flutter/persona/persona_widget.dart';
import 'package:ino_flutter/state/persona_bloc.dart';

class OnboardingScreen extends StatefulWidget {
  const OnboardingScreen({super.key});

  @override
  State<OnboardingScreen> createState() => _OnboardingScreenState();
}

class _OnboardingScreenState extends State<OnboardingScreen> {
  int _step = 0;
  final _nameController = TextEditingController();

  @override
  void initState() {
    super.initState();
    context.read<PersonaBloc>().add(PersonaStarted());
  }

  @override
  void dispose() {
    _nameController.dispose();
    super.dispose();
  }

  void _nextStep() {
    if (_step == 1 && _nameController.text.trim().isNotEmpty) {
      context
          .read<PersonaBloc>()
          .add(PersonaEmotionChanged(PersonaEmotion.celebrating));
    }
    setState(() {
      _step++;
    });
    if (_step == 2) {
      Future.delayed(const Duration(seconds: 2), () {
        if (mounted) context.go('/brain');
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 32),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                const PersonaWidget(size: 250),
                const SizedBox(height: 48),
                AnimatedSwitcher(
                  duration: const Duration(milliseconds: 400),
                  child: _buildStep(),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildStep() {
    return switch (_step) {
      0 => _GreetingStep(key: const ValueKey(0), onContinue: _nextStep),
      1 => _NameStep(
          key: const ValueKey(1),
          controller: _nameController,
          onContinue: _nextStep,
        ),
      _ => _ReadyStep(
          key: const ValueKey(2),
          name: _nameController.text.trim(),
        ),
    };
  }
}

class _GreetingStep extends StatelessWidget {
  const _GreetingStep({super.key, required this.onContinue});
  final VoidCallback onContinue;

  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          "I'm ino \u2014 your personal intelligence.",
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                color: Colors.white,
                fontWeight: FontWeight.w300,
              ),
        ),
        const SizedBox(height: 32),
        FilledButton(
          onPressed: onContinue,
          child: const Text('Continue'),
        ),
      ],
    );
  }
}

class _NameStep extends StatelessWidget {
  const _NameStep({
    super.key,
    required this.controller,
    required this.onContinue,
  });
  final TextEditingController controller;
  final VoidCallback onContinue;

  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          'What should I call you?',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                color: Colors.white,
                fontWeight: FontWeight.w300,
              ),
        ),
        const SizedBox(height: 24),
        SizedBox(
          width: 280,
          child: TextField(
            controller: controller,
            autofocus: true,
            textAlign: TextAlign.center,
            style: const TextStyle(color: Colors.white, fontSize: 20),
            decoration: InputDecoration(
              hintText: 'Your name',
              hintStyle: TextStyle(color: Colors.white.withAlpha(100)),
              enabledBorder: UnderlineInputBorder(
                borderSide: BorderSide(color: Colors.white.withAlpha(60)),
              ),
              focusedBorder: const UnderlineInputBorder(
                borderSide: BorderSide(color: Color(0xFF6C63FF)),
              ),
            ),
            onSubmitted: (_) => onContinue(),
          ),
        ),
        const SizedBox(height: 32),
        FilledButton(
          onPressed: onContinue,
          child: const Text('Continue'),
        ),
      ],
    );
  }
}

class _ReadyStep extends StatelessWidget {
  const _ReadyStep({super.key, required this.name});
  final String name;

  @override
  Widget build(BuildContext context) {
    final displayName = name.isEmpty ? 'you' : name;
    return Text(
      "Nice to meet you, $displayName. Let's go.",
      textAlign: TextAlign.center,
      style: Theme.of(context).textTheme.headlineSmall?.copyWith(
            color: Colors.white,
            fontWeight: FontWeight.w300,
          ),
    );
  }
}
