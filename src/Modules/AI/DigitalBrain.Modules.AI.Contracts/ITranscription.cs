namespace DigitalBrain.AI;

// Marker root for speech-to-text model markers. One root for both locally hosted
// and cloud models: which one serves a marker is a matter of AiProvider, not of
// which catalog it lives in.
public interface ITranscription : IAiMarker;
