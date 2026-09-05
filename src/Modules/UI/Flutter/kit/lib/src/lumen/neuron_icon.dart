import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import 'lumen_palette.dart';

enum NeuronIconKind {
  assistant,
  conversation,
  execution,
  gmail,
  salesforce,
  aspire,
  document,
  search,
  repository,
  memory,
  clock,
  generic,
}

/// Curated marks from the approved Lumen study. Generic types use one coherent
/// icon vocabulary; caller labels identify the actual neuron/account instance.
final class NeuronIcon extends StatelessWidget {
  const NeuronIcon({
    super.key,
    required this.kind,
    this.size = 30,
    this.color,
    this.semanticLabel,
  });

  final NeuronIconKind kind;
  final double size;
  final Color? color;
  final String? semanticLabel;

  @override
  Widget build(BuildContext context) {
    final brand = _marks[kind];
    if (brand != null) {
      return SvgPicture.string(
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 34 34">$brand</svg>',
        width: size,
        height: size,
        semanticsLabel: semanticLabel,
      );
    }
    return Icon(
      switch (kind) {
        NeuronIconKind.assistant => Icons.auto_awesome_rounded,
        NeuronIconKind.conversation => Icons.chat_bubble_outline_rounded,
        NeuronIconKind.execution => Icons.bolt_rounded,
        NeuronIconKind.document => Icons.description_outlined,
        NeuronIconKind.search => Icons.search_rounded,
        NeuronIconKind.repository => Icons.code_rounded,
        NeuronIconKind.memory => Icons.inventory_2_outlined,
        NeuronIconKind.clock => Icons.schedule_rounded,
        _ => Icons.hub_outlined,
      },
      size: size,
      color: color ?? LumenPalette.accent,
      semanticLabel: semanticLabel,
    );
  }

  // Local vector interpretations retained from the approved prototype. Keep
  // branding isolated here for later replacement with audited provider assets.
  static const _marks = <NeuronIconKind, String>{
    NeuronIconKind.gmail: '''
      <path fill="#4285F4" d="M2 7h5v15H2z"/>
      <path fill="#34A853" d="M25 7h5v15h-5z"/>
      <path fill="#EA4335" d="M7 6 16 13l9-7v6l-9 7-9-7z"/>
      <path fill="#C5221F" d="M2 7q0-5 5-2v7L2 8z"/>
      <path fill="#FBBC04" d="M25 5q5-3 5 2v1l-5 4z"/>''',
    NeuronIconKind.salesforce: '''
      <path fill="#14A8E0" d="M7 10a7 7 0 0 1 12-4 8 8 0 0 1 12 7A7 7 0 0 1 29 27H8A9 9 0 0 1 7 10Z"/>
      <path fill="none" stroke="white" stroke-width="1.2" stroke-linecap="round" d="M9 16h3m-3 0v3h3v3H9m7 0v-8h3m-3 4h2m5-2h3m-3 0v6h3"/>''',
    NeuronIconKind.aspire: '''
      <path fill="#8B6CE8" d="M4 22 14 3h7L11 22Zm11 0L24 6l7 16h-7l-2-5-3 5Z"/>
      <path fill="#BDACF7" d="m4 26 3-5h23l3 5z"/>''',
  };
}
