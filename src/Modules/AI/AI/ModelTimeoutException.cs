namespace DigitalBrain.AI;

internal sealed class ModelTimeoutException(TimeoutException inner) : Exception(inner.Message, inner);
