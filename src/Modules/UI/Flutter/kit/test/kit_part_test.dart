import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('KitButtonPart round-trips metadata for CustomMessage', () {
    const part = KitButtonPart(
      buttonId: 'show-time',
      label: 'Show current time',
      action: 'show-time',
      offerCommandId: 'cmd-1',
    );
    final parsed = KitPart.tryParse(part.toMetadata());
    expect(parsed, isA<KitButtonPart>());
    final button = parsed! as KitButtonPart;
    expect(button.buttonId, 'show-time');
    expect(button.offerCommandId, 'cmd-1');
  });

  test('KitChartPart round-trips points', () {
    const part = KitChartPart(
      title: 'Sales',
      points: [
        KitChartPoint(label: 'Mon', value: 1),
        KitChartPoint(label: 'Tue', value: 2),
      ],
    );
    final parsed = KitPart.tryParse(part.toMetadata()) as KitChartPart;
    expect(parsed.points, hasLength(2));
    expect(parsed.points.first.label, 'Mon');
  });

  test('KitMessageFactory emits CustomMessage for parts', () {
    final messages = KitMessageFactory.messagesForTurn(
      sequence: 2,
      fromUser: false,
      text: 'Tap the button',
      createdAt: DateTime.utc(2026, 8, 9),
      parts: const [
        KitButtonPart(
          buttonId: 'show-time',
          label: 'Show current time',
          action: 'show-time',
        ),
      ],
    );
    expect(messages, hasLength(2));
    expect(messages.first, isA<TextMessage>());
    expect(messages.last, isA<CustomMessage>());
    final custom = messages.last as CustomMessage;
    expect(custom.metadata?['kind'], 'button');
  });
}
