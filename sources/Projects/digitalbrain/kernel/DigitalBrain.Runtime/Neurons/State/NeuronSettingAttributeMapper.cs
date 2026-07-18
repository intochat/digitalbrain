using System.Reflection;

namespace DigitalBrain.Runtime.Neurons.State;

public sealed class NeuronSettingAttributeMapper(IConfiguration? configuration = null) : IAttributeToFactoryMapper<NeuronSettingAttribute>
{
    public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, NeuronSettingAttribute metadata)
    {
        if (parameter.ParameterType != typeof(string))
        {
            throw new InvalidOperationException($"[NeuronSetting] can only be applied to string parameters (type {parameter.ParameterType.FullName} is not supported).");
        }

        return context =>
        {
            var config = configuration ?? context.ActivationServices.GetService<IConfiguration>();
            return NeuronSettingResolver.Resolve(config, metadata);
        };
    }
}

