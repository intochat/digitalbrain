using System.Text.Json.Serialization;
using DigitalBrain.UI;

namespace APP_NAMESPACE;

// Every operation input, state, signal and private contract argument/result needs an explicit root.
// View includes the polymorphic framework node hierarchy used by the presenter.
[JsonSerializable(typeof(Opened))]
[JsonSerializable(typeof(CounterState))]
[JsonSerializable(typeof(Counted))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(View))]
[JsonSerializable(typeof(Toast))]
public partial class AppJsonContext : JsonSerializerContext;
