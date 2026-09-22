using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Media;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests;

public sealed class SpeechTransportFacts
{
    [Fact]
    public async Task OpenAISpeechMapsModelVoiceTextAndReturnsMp3WithCompletionSignal()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        using var endpoint = new SpeechEndpoint();
        await using var brain = await UnitTest.Create().WithModule<AIModule>()
            .ConfigureSilo(silo =>
            {
                silo.Services.Configure<AIOptions>(options =>
                {
                    options.OpenAI.ApiKey = "test-only";
                    options.OpenAI.Endpoint = endpoint.Url;
                });
                silo.Services.AddOpenAISpeechSynthesis("fixture-tts");
            }).StartAsync(ct);
        var speech = brain.Get<ISpeechSynthesizer>("real-speech-transport");
        Assert.True((await speech.Describe()).Available);
        await using var completed = await brain.Observe<SpeechSynthesized>(speech, ct);
        byte[] mp3 = [0x49, 0x44, 0x33, 0x04, 0, 0];
        var requestTask = endpoint.Reply(mp3, ct);
        var response = await speech.Synthesize(new SpeechSynthesisRequest("Read this aloud.", "coral"), ct);
        var request = await requestTask;
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/audio/speech", request.Path);
        Assert.Equal("fixture-tts", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("coral", json.RootElement.GetProperty("voice").GetString());
        Assert.Equal("Read this aloud.", json.RootElement.GetProperty("input").GetString());
        Assert.Equal("mp3", json.RootElement.GetProperty("response_format").GetString());
        Assert.Equal(mp3, response.Audio.Content);
        Assert.Equal("audio/mpeg", response.Audio.MediaType);
        Assert.Equal("fixture-tts", response.Model);
        var signal = await completed.NextAsync(ct: ct);
        Assert.Equal(response.OperationId, signal.OperationId);
        Assert.Equal(response.Model, signal.Model);
    }

    private sealed class SpeechEndpoint : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Url { get; }
        public SpeechEndpoint()
        {
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            Url = $"http://localhost:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
        }
        public async Task<(string Method, string? Path, string Body)> Reply(byte[] audio, CancellationToken ct)
        {
            var context = await _listener.GetContextAsync().WaitAsync(ct);
            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = (context.Request.HttpMethod, context.Request.Url?.AbsolutePath, body);
            context.Response.ContentType = "audio/mpeg";
            context.Response.ContentLength64 = audio.Length;
            await context.Response.OutputStream.WriteAsync(audio, ct);
            context.Response.Close();
            return request;
        }
        public void Dispose() => _listener.Close();
    }
}