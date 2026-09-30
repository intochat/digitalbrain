using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.TextField;

namespace DigitalBrain.CustomerResearcher;

// The researcher's edge neurons, composed by plain grain calls and safe to re-run.
public static class CustomerResearcherSurface
{
    public static string Key(string workspace) => workspace + "/applications/customer-researcher";

    public static async Task Compose(IGrainFactory grains, string key)
    {
        string Name(string part) => UiParts.NameOf(key, part);
        UiChildRef Ref(string kind, string part) => new(kind, Name(part));
        Task Bind(string part) => grains.GetGrain<IUiBinding>(Name(part)).Bind(grains.GetGrain<ICustomerResearcher>(key));

        await grains.GetGrain<ITextField>(Name("company")).Configure("Company name, location or website", "text", Name("research"));
        await Bind("company");
        await grains.GetGrain<IButton>(Name("research")).Set("Research", "research");
        await Bind("research");
        await grains.GetGrain<IButton>(Name("stop")).Set("Stop", "stop");
        await Bind("stop");
        await grains.GetGrain<IText>(Name("status")).Set("Waiting for browser…");
        var toolbar = grains.GetGrain<ILayout>(Name("toolbar"));
        await toolbar.Set(new("row",
            [Ref(UIVocabulary.TextFieldType, "company"), Ref(UIVocabulary.ButtonType, "research"), Ref(UIVocabulary.ButtonType, "stop"), Ref(UIVocabulary.TextType, "status")],
            Extents: [0, 110, 80, 280]), (await toolbar.Read()).Revision);

        await Bind("browser");
        var main = grains.GetGrain<ILayout>(Name("main"));
        await main.Set(new("column",
            [Ref(UIVocabulary.LayoutType, "toolbar"), Ref(UIVocabulary.WebBrowserType, "browser")],
            Extents: [64, 0]), (await main.Read()).Revision);

        var surface = grains.GetGrain<ISurface>(Name("surface"));
        await surface.Set(new("Customer Researcher", [Ref(UIVocabulary.LayoutType, "main")]), (await surface.Read()).Revision);
    }
}
