using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Abstractions;

[SuppressMessage(
    "Design",
    "CA1040:Avoid empty interfaces",
    Justification = "IEmit<TSynapse> is the declaration of what a neuron produces. It carries no members by design: the source-generated dispatch manifest reads it to prove wiring at build time.")]
public interface IEmit<TSynapse>
    where TSynapse : Synapse;
