using System.Runtime.CompilerServices;
using DigitalBrain.Platform.Integrations;
using DigitalBrain.Platform.Integrations.Accounts;

[assembly: TypeForwardedTo(typeof(IIntegrationRegistration))]
[assembly: TypeForwardedTo(typeof(RegistrationStatus))]
[assembly: TypeForwardedTo(typeof(RegistrationSnapshot))]
[assembly: TypeForwardedTo(typeof(ConfigureRegistration))]
[assembly: TypeForwardedTo(typeof(ReleasedRegistration))]
[assembly: TypeForwardedTo(typeof(RegistrationChanged))]
[assembly: TypeForwardedTo(typeof(AccountProbeOutcome))]
[assembly: TypeForwardedTo(typeof(AccountProbeResult))]
[assembly: TypeForwardedTo(typeof(IAccountProbe))]
[assembly: TypeForwardedTo(typeof(AccountNotConfiguredException))]
