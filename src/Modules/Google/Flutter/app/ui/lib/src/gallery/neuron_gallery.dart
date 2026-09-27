import 'dart:convert';

import 'package:flutter/material.dart';

import '../composition/neuron_view.dart';
import '../composition/renderer_registry.dart';

/// Fixtures use the same kind/name references and definitions as NeuronView.
const neuronDescriptions = <String, String>{
  'surface': 'An app root that brings named child neurons into one window.',
  'layout': 'Arrange child neurons in a column, row, split, or stack.',
  'collection':
      'Browse items, select a record, and open its connected content.',
  'imagecanvas':
      'Edit an image through the host-provided image canvas and asset loader.',
  'button': 'A labeled action connected to its named neuron.',
  'text': 'Display the text stored in a neuron’s markdown field.',
  'textfield': 'Collect a value and send it when the user submits the field.',
  'card': 'Group a title, body, and optional child neurons.',
  'tabs': 'Switch between named child views within an app.',
  'form': 'Collect typed fields and submit their values together.',
  'toggle': 'An on/off value for a setting or option.',
  'slider': 'A numeric value selected along a range.',
  'progress': 'Progress or activity feedback for ongoing work.',
  'infobar': 'An informational notice, warning, or status message.',
  'image': 'An image with contextual content.',
  'video': 'Video content and playback state.',
  'webbrowser': 'A web page or browser surface.',
  'chart': 'A visual summary of numeric data.',
  'graph': 'Connected nodes and relationships.',
  'table': 'Structured records arranged in columns and rows.',
  'calendar': 'Dates and scheduled items.',
  'clock': 'Time or a timer display.',
  'map': 'Locations in a geographic view.',
  'person': 'A person’s identity or profile.',
  'rating': 'A score or rating control.',
  'color': 'A color value or selection.',
  'expander': 'Content that can be expanded or collapsed.',
  'tree': 'Items organized into a parent and child hierarchy.',
  'sheet': 'Spreadsheet-style cells and tabular content.',
};

String compositionGuidance(String kind) => switch (kind) {
  'surface' || 'layout' =>
    '\n\nReading app recipe: surface → layout → text + card + button. '
        'The surface references layout:reading-layout; that layout references '
        'text:welcome, card:reading-card, and button:add-book. Define each named '
        'child separately using the examples in this catalog. Children use kind '
        'and name, so the same neuron can be reused across views.',
  'tabs' =>
    '\n\nBrowse-and-detail recipe: tabs → collection + card. The Books tab '
        'references collection:books; Summary references card:summary. Each tab '
        'has an id, title, and child reference; selectedId chooses the active view.',
  'form' =>
    '\n\nData-entry recipe: surface → form. Fields belong to one form neuron; '
        'they are not separate neurons. Submission sends the field values together.',
  _ => '',
};

Map<String, dynamic> neuronExample(String kind) => switch (kind) {
  'surface' => {
    'title': 'Reading app',
    'children': [
      {'kind': 'layout', 'name': 'reading-layout'},
    ],
  },
  'layout' => {
    'mode': 'column',
    'gap': 12,
    'children': [
      {'kind': 'text', 'name': 'welcome'},
      {'kind': 'card', 'name': 'reading-card'},
      {'kind': 'button', 'name': 'add-book'},
    ],
  },
  'text' => {'markdown': 'Your reading space'},
  'button' => {'label': 'Add a book', 'action': 'add-book', 'enabled': true},
  'textfield' => {'label': 'Book title', 'value': 'A Field Guide'},
  'card' => {
    'title': 'Currently reading',
    'body': 'A Field Guide · 42 pages read',
  },
  'collection' => {
    'fileMode': false,
    'items': [
      {
        'id': 'field-guide',
        'label': 'A Field Guide',
        'kind': 'book',
        'canActivate': true,
      },
      {
        'id': 'creative-act',
        'label': 'The Creative Act',
        'kind': 'book',
        'canActivate': true,
      },
    ],
  },
  'tabs' => {
    'selectedId': 'books',
    'tabs': [
      {
        'id': 'books',
        'title': 'Books',
        'child': {'kind': 'collection', 'name': 'books'},
      },
      {
        'id': 'summary',
        'title': 'Summary',
        'child': {'kind': 'card', 'name': 'summary'},
      },
    ],
  },
  'form' => {
    'title': 'Add to reading list',
    'fields': [
      {
        'name': 'title',
        'label': 'Title',
        'kind': 'PlainText',
        'value': 'A Field Guide',
      },
      {
        'name': 'format',
        'label': 'Format',
        'kind': 'Choice',
        'choices': ['Print', 'Digital'],
        'value': 'Print',
      },
    ],
  },
  'imagecanvas' => {
    'assetId': 'local-image',
    'width': 640,
    'height': 480,
    'recipe': {'strokes': []},
    'documentRevision': 1,
  },
  _ => {},
};

String neuronExampleJson(String kind) => const JsonEncoder.withIndent('  ')
    .convert({
      'kind': kind,
      'name': 'example-$kind',
      'definition': neuronExample(kind),
    });

class NeuronGalleryPreview extends StatefulWidget {
  const NeuronGalleryPreview({super.key, required this.kind});
  final String kind;
  @override
  State<NeuronGalleryPreview> createState() => _NeuronGalleryPreviewState();
}

class _NeuronGalleryPreviewState extends State<NeuronGalleryPreview> {
  String? selectedTab;
  String lastAction = 'Actions stay in this preview.';
  int revision = 0;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      SizedBox(
        height: widget.kind == 'form' ? 360 : 280,
        child: NeuronView(
          kind: widget.kind,
          name: 'example-${widget.kind}',
          revision: revision,
          load: (kind, name) async => {
            'revision': revision,
            'definition': {
              ...neuronExample(kind),
              if (kind == 'tabs' && selectedTab != null)
                'selectedId': selectedTab,
            },
          },
          onAction: (action) async => setState(() {
            lastAction =
                'Local ${action['kind']} action: ${action['action'] ?? action['value'] ?? 'submit'}';
            if (action['kind'] == 'tabs') {
              selectedTab = action['value'] as String;
            }
            revision++;
          }),
          onActivate: (item) =>
              setState(() => lastAction = 'Opened locally: ${item['label']}'),
          imageBuilder: (_) => const Center(
            child: Text(
              'Image canvas requires a host-loaded image.\nAsset: local-image · 640 × 480',
              textAlign: TextAlign.center,
            ),
          ),
        ),
      ),
      if (!RendererRegistry.uiEntry(widget.kind).isFallback) ...[
        const SizedBox(height: 12),
        Text(lastAction),
      ],
    ],
  );
}
