namespace DigitalBrain.AI;

// Root of every model marker. Markers are the identity of a model: they key the
// DI registration, they name the model in configuration, and their kind
// interface constrains which catalog a model may join. Nothing is ever resolved
// by parsing a marker's name.
public interface IAiMarker;
