import 'package:http/http.dart' as http;

/// A surface composed entirely from generic declared primitives.
Map<String, dynamic>? applicationResponse(http.BaseRequest request) {
  final segments = request.url.pathSegments;
  if (segments.length < 2 || segments.first != 'workspaces') return null;
  final root = '${segments[1]}/applications/assistant';
  if (request.url.path.endsWith('/applications/assistant/start')) {
    return {
      'surface': {'kind': 'surface', 'name': '$root/surface'},
    };
  }
  if (!request.url.path.endsWith('/apps/node')) return null;
  final name = request.url.queryParameters['name'] ?? '';
  return switch (request.url.queryParameters['kind']) {
    'surface' => {
      'definition': {
        'mode': 'column',
        'children': [
          {'kind': 'layout', 'name': '$root/messages'},
          {'kind': 'layout', 'name': '$root/prompts'},
          {'kind': 'textfield', 'name': '$root/draft'},
          {'kind': 'layout', 'name': '$root/footer'},
        ],
        'extents': [-1, 50, 70, 48],
      },
    },
    'layout' =>
      name.endsWith('/messages')
          ? {'mode': 'list', 'children': <Object>[]}
          : name.endsWith('/prompts')
          ? {
              'mode': 'row',
              'children': [
                {'kind': 'button', 'name': '$root/prompt-one'},
                {'kind': 'button', 'name': '$root/prompt-two'},
              ],
            }
          : {
              'mode': 'row',
              'children': [
                {'kind': 'fileinput', 'name': '$root/file'},
                {'kind': 'voiceinput', 'name': '$root/voice'},
                {'kind': 'button', 'name': '$root/send'},
              ],
            },
    'textfield' => {'label': 'Message', 'kind': 'multiline', 'value': ''},
    'voiceinput' => {'label': 'Dictate'},
    'fileinput' => {'label': 'Attach'},
    'button' => {
      'label': name.endsWith('prompt-one')
          ? 'Ask the assistant'
          : name.endsWith('prompt-two')
          ? 'Work with Salesforce'
          : 'Send',
      'enabled': true,
      'action': 'click',
    },
    _ => null,
  };
}
