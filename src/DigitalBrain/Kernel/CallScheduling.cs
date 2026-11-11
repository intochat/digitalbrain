using DigitalBrain.Neurons;

namespace DigitalBrain.Kernel;

/// <summary>How a contract call is scheduled by the neuron host.</summary>
internal enum CallScheduling
{
    /// <summary>The call takes a turn and runs exclusively with state mutation.</summary>
    Turn = 0,

    /// <summary>The call may interleave while another turn is suspended.</summary>
    Interleaved = 1,

    /// <summary>The class's <see cref="InterleavesWhenAttribute"/> predicate decides per call.</summary>
    Conditional = 2,
}
