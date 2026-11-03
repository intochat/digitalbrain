namespace DigitalBrain.Errors;

// A refusal word: the system behind a neuron is down or not provisioned. HTTP surfaces map it to 503.
public class ServiceUnavailableException(string message) : Exception(message);
