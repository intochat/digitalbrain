using System.Reflection;

namespace DigitalBrain.Runtime.Neurons.State;

public sealed class NeuronStateAttributeMapper : IAttributeToFactoryMapper<NeuronStateAttribute>
{
    public Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, NeuronStateAttribute metadata)
    {
        var stateType = parameter.ParameterType;

        return context =>
        {
            var constructors = stateType.GetConstructors();
            if (constructors.Length == 0)
            {
                throw new InvalidOperationException($"State type {stateType.FullName} has no public constructors.");
            }

            // We grab the first constructor
            var ctor = constructors[0];
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var paramType = param.ParameterType;

                // Resolve as a keyed service using the parameter name as the key.
                var key = param.Name ?? paramType.Name;
                args[i] = context.ActivationServices.GetKeyedService(paramType, key)
                    ?? throw new InvalidOperationException($"Unable to resolve keyed service of type {paramType.FullName} with key '{key}' for state type {stateType.FullName}.");
            }

            return ctor.Invoke(args);
        };
    }
}
