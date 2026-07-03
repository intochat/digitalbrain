import 'package:flutter/material.dart';
import 'package:vector_math/vector_math_64.dart' as vm;

class DomainStyle {
  const DomainStyle({
    required this.color,
    required this.anchor,
    required this.shortLabel,
  });
  final Color color;
  final vm.Vector3 anchor;
  final String shortLabel;
}

const _kSphereRadius = 250.0;

final Map<String, DomainStyle> domainPalette = {
  'kernel':  DomainStyle(color: const Color(0xFFE6EDF7), anchor: vm.Vector3( 0.00,  0.20,  1.05) * _kSphereRadius, shortLabel: 'KERNEL'),
  'ai':      DomainStyle(color: const Color(0xFF7C8AFF), anchor: vm.Vector3( 0.95,  0.55,  0.10) * _kSphereRadius, shortLabel: 'AI'),
  'google':  DomainStyle(color: const Color(0xFFE8C56A), anchor: vm.Vector3(-0.85,  0.65,  0.40) * _kSphereRadius, shortLabel: 'GOOGLE'),
  'data':    DomainStyle(color: const Color(0xFF6EE7A8), anchor: vm.Vector3( 0.60, -0.65,  0.55) * _kSphereRadius, shortLabel: 'DATA'),
  'travel':  DomainStyle(color: const Color(0xFF9CB3FF), anchor: vm.Vector3(-0.55, -0.55,  0.70) * _kSphereRadius, shortLabel: 'TRAVEL'),
  'dynamic': DomainStyle(color: const Color(0xFFF4B8E4), anchor: vm.Vector3( 0.25, -0.95, -0.30) * _kSphereRadius, shortLabel: 'DYNAMIC'),
  'system':  DomainStyle(color: const Color(0xFFB8C5E0), anchor: vm.Vector3( 0.05,  0.95, -0.45) * _kSphereRadius, shortLabel: 'SYSTEM'),
};

DomainStyle styleForDomain(String? domain) =>
    domainPalette[domain ?? 'system'] ?? domainPalette['system']!;

// Family palette used by comets and static edges (Task 8 + Task 7 fallback).
const Color _periwinkle = Color(0xFF7C8AFF);
const Color _mint       = Color(0xFF6EE7A8);
const Color _gold       = Color(0xFFE8C56A);
const Color _softBlue   = Color(0xFF9CB3FF);
const Color _magenta    = Color(0xFFF4B8E4);
const Color _neutral    = Color(0xFFB6BEFF);
const Color _broadcastColor = Color(0xFFFF4081); // Neon Pink / Magenta

Color colorForSynapseType(String typeFullName) {
  final s = typeFullName;
  if (s.contains('.Signal.') ||
      s.contains('.Loaded') ||
      s.contains('.Created') ||
      s.contains('.Activated') ||
      s.toLowerCase().contains('created') ||
      s.toLowerCase().contains('activated') ||
      s.toLowerCase().contains('loaded') ||
      s.contains('Signal') ||
      s.contains('Hook')) {
    return _broadcastColor;
  }
  if (s.contains('.Llm') || s.contains('Classify') || s.contains('Voice2Text') || s.contains('InnoLang')) return _periwinkle;
  if (s.contains('Store') || s.contains('Sqlite') || s.contains('Database'))    return _mint;
  if (s.contains('Gmail') || s.contains('OAuth'))                                return _gold;
  if (s.contains('TripPlan') || s.contains('.Word'))                             return _softBlue;
  if (s.contains('CreateNeuron') || s.contains('NeuronCreated') || s.contains('.Diagram')) return _magenta;
  return _neutral;
}

// Short label derivation from a neuron's type full name:
//   "DigitalBrain.Domains.Ai.Llm.Gpt5MiniNeuron" -> "Gpt5Mini"
//   "gateway"                                -> "gateway"
String shortNeuronLabel(String typeFullName) {
  if (typeFullName.isEmpty) return '?';
  final dot = typeFullName.lastIndexOf('.');
  final tail = dot < 0 ? typeFullName : typeFullName.substring(dot + 1);
  return tail.endsWith('Neuron') ? tail.substring(0, tail.length - 'Neuron'.length) : tail;
}
