import 'package:digitalbrain_corev2/digitalbrain_corev2.dart';
import 'package:flutter/material.dart';

void main() {
  final productBase = DigitalBrainHostEnvironment.requireProductBase();
  runApp(DigitalBrainShell(productBase: productBase));
}

class DigitalBrainShell extends StatelessWidget {
  const DigitalBrainShell({required this.productBase, super.key});

  final Uri productBase;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'DigitalBrain CoreV2',
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xff4f46e5),
          brightness: Brightness.dark,
        ),
        useMaterial3: true,
      ),
      home: Scaffold(
        appBar: AppBar(title: const Text('DigitalBrain CoreV2')),
        body: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.hub_outlined, size: 72),
              const SizedBox(height: 20),
              const Text('ProductHost connected through Aspire'),
              const SizedBox(height: 8),
              Text(productBase.origin),
            ],
          ),
        ),
      ),
    );
  }
}
