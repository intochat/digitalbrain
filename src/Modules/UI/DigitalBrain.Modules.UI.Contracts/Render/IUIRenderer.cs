using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

// The one write path into UI entities. A renderer instance shares its name with the entity it
// writes -- uirenderer:{name} fills chart:{name} and surface:{name} -- so an entity-shaped
// fire target selects the writer instance by name.
[Alias("ui.renderer")]
public partial interface IUIRenderer :
    INeuron,
    IHandle<OpenSurface>,
    IHandle<ControlActivated>
{
    const string DefaultInstanceName = "default";
}
