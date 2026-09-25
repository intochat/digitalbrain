import 'package:digitalbrain_ui/src/lumen/neuron_icon.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('allowlisted grain types resolve provider icons', () {
    expect(
      NeuronIconKind.forGrainId('supabase-table/customer'),
      NeuronIconKind.supabase,
    );
    expect(NeuronIconKind.forGrainId('gmail/inbox'), NeuronIconKind.gmail);
    expect(
      NeuronIconKind.forGrainId('supabase-unknown/customer'),
      NeuronIconKind.generic,
    );
    expect(
      NeuronIconKind.fromKey('../assets/brands/gmail.svg'),
      NeuronIconKind.generic,
    );
  });
}
