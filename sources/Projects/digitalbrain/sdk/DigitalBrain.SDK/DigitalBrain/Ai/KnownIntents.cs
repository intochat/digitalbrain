namespace DigitalBrain.SDK.DigitalBrain.Ai;

// Enumerated set of intents the classifier knows about today. Unknown is the
// terminal fallback -- M6 routes Unknown through the Creator to draft a new
// neuron rather than dropping the request.
public enum KnownIntent
{
    Unknown = 0,
    GetLastNGmailSenders = 1,
    ExplainQuery = 2,
    LifePlanning = 3,
    FindVideo = 4,
    OpenCanvas = 5,
    CreateFolder = 6,
    NemoChat = 7,
    SetClock = 8,
    RemindMe = 9,
    ShowFlight = 10,
}
