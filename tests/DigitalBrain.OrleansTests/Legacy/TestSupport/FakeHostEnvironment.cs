using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.TestSupport;

public sealed class FakeHostEnvironment(string environmentName = "Development") : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "DigitalBrain.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
