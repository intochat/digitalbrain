using Orleans.Services;

namespace DigitalBrain.Testing;

internal interface ITestReminderDeliveryServiceClient :
    IGrainServiceClient<ITestReminderDeliveryService>,
    ITestReminderDeliveryService;
