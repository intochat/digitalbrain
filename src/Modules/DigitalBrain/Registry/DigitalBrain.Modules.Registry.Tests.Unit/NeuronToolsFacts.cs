using DigitalBrain.Contracts;
using DigitalBrain.Registry;
using DigitalBrain.Registry.Agents;

namespace DigitalBrain.Modules.Registry.Tests.Unit;

public sealed class NeuronToolsFacts
{
    [Fact]
    public void SchemasCanBeCreatedConcurrentlyAfterInvocationOptionsAreUsed()
    {
        var method = new NeuronMethod(new NeuronContract("test.shelf", typeof(IShelf), "module"), typeof(IShelf).GetMethod(nameof(IShelf.Put))!);
        System.Text.Json.JsonSerializer.Serialize(new ShelfItem("book", 1), NeuronInvoker.Json);

        Parallel.For(0, 64, iteration =>
        {
            var schema = NeuronTools.Schema(method);
            Assert.True(schema.GetProperty("properties").GetProperty("item").GetProperty("properties").TryGetProperty("title", out _));
        });
    }

    [Fact]
    public void SchemaRequiresTheNeuronIdAndEveryArgumentWithoutADefault()
    {
        var method = new NeuronMethod(new NeuronContract("test.shelf", typeof(IShelf), "module"), typeof(IShelf).GetMethod(nameof(IShelf.Put))!);

        var schema = NeuronTools.Schema(method);

        var properties = schema.GetProperty("properties");
        Assert.Equal("string", properties.GetProperty("neuronId").GetProperty("type").GetString());
        Assert.True(properties.GetProperty("item").GetProperty("properties").TryGetProperty("title", out _));
        Assert.False(properties.TryGetProperty("cancellationToken", out _));
        Assert.Equal(["neuronId", "item"], schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
    }

    [GenerateSerializer]
    public sealed record ShelfItem([property: Id(0)] string Title, [property: Id(1)] int Count);

    [Alias("test.shelf")]
    public interface IShelf : INeuron
    {
        Task Put(ShelfItem item, string? note = null, CancellationToken cancellationToken = default);
    }

}