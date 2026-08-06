namespace DigitalBrain;

internal interface IEnvelopeCarrier
{
    void Write(DeliveryEnvelope envelope);

    DeliveryEnvelope? Consume();
}
