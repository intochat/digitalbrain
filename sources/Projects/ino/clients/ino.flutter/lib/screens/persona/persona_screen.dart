import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:ino_flutter/persona/persona_widget.dart';

class PersonaScreen extends StatelessWidget {
  const PersonaScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: SafeArea(
        child: Stack(
          children: [
            const Center(child: PersonaWidget(size: 360)),
            Positioned(
              top: 12,
              left: 12,
              child: IconButton(
                tooltip: 'Back to brain',
                icon: const Icon(Icons.arrow_back, color: Colors.white70),
                onPressed: () => context.go('/brain'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
