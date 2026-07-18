namespace DigitalBrain.Kernel.Gateway;

using System;
using System.Linq;
using System.Threading.Tasks;
using DigitalBrain.Runtime.Brain;
using DigitalBrain.Runtime.Grpc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Orleans;
using Microsoft.Extensions.Logging;

public sealed class BrainRegistryService(
    IGrainFactory grains,
    ILogger<BrainRegistryService> logger) : BrainRegistry.BrainRegistryBase
{
    public override async Task<BrainsResult> ListBrains(DigitalBrain.Runtime.Grpc.ListBrainsRequest request, ServerCallContext context)
    {
        try
        {
            var registry = grains.GetGrain<IBrainRegistry>(Guid.Empty);
            var demoBrains = await registry.ListBrainsAsync();

            var result = new BrainsResult();
            result.Brains.AddRange(demoBrains.Select(b => new BrainSummary
            {
                Id = b.BrainId,
                Name = b.Name,
                Version = "1.0.0",
                BrandColor = "#1E88E5",
                NeuronCount = 0,
                LastActivityAt = Timestamp.FromDateTimeOffset(b.CreatedAt.ToUniversalTime()),
                InstalledBundleCount = 0,
                CapabilityTags = { new List<string>() }
            }));

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in ListBrains gRPC service.");
            throw new RpcException(new Status(StatusCode.Internal, $"Internal error: {ex.Message}"));
        }
    }

    public override async Task WatchActivity(
        WatchActivityRequest request,
        IServerStreamWriter<BrainActivityDelta> responseStream,
        ServerCallContext context)
    {
        logger.LogInformation("BrainRegistry.WatchActivity subscriber attached.");
        
        try
        {
            var registry = grains.GetGrain<IBrainRegistry>(Guid.Empty);
            
            while (!context.CancellationToken.IsCancellationRequested)
            {
                var brains = await registry.ListBrainsAsync();
                if (brains.Count > 0)
                {
                    var random = new Random();
                    var target = brains[random.Next(brains.Count)];

                    var delta = new BrainActivityDelta
                    {
                        BrainId = target.BrainId,
                        SynapsesPerSecond = random.NextDouble() * 5.0,
                        NeuronCountDelta = 0,
                        VersionBump = ""
                    };

                    await responseStream.WriteAsync(delta, context.CancellationToken);
                }
                await Task.Delay(5000, context.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("BrainRegistry.WatchActivity subscriber disconnected.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in WatchActivity gRPC service stream.");
        }
    }

    public override async Task<BrainCreated> CreateBrain(CreateBrainCommand request, ServerCallContext context)
    {
        try
        {
            var registry = grains.GetGrain<IBrainRegistry>(Guid.Empty);
            var brain = await registry.CreateBrainAsync(request.Name, request.SeedTemplate);

            return new BrainCreated
            {
                BrainId = brain.Value,
                Success = true,
                ErrorMessage = ""
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in CreateBrain gRPC service.");
            return new BrainCreated
            {
                BrainId = "",
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public override async Task<BrainRenamed> RenameBrain(RenameBrainCommand request, ServerCallContext context)
    {
        try
        {
            var registry = grains.GetGrain<IBrainRegistry>(Guid.Empty);
            await registry.RenameBrainAsync(request.BrainId, request.NewName);

            return new BrainRenamed
            {
                BrainId = request.BrainId,
                Success = true,
                ErrorMessage = ""
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in RenameBrain gRPC service.");
            return new BrainRenamed
            {
                BrainId = request.BrainId,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public override async Task<BrainDeleted> DeleteBrain(DeleteBrainCommand request, ServerCallContext context)
    {
        try
        {
            var registry = grains.GetGrain<IBrainRegistry>(Guid.Empty);
            await registry.DeleteBrainAsync(request.BrainId);

            return new BrainDeleted
            {
                BrainId = request.BrainId,
                Success = true,
                ErrorMessage = ""
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in DeleteBrain gRPC service.");
            return new BrainDeleted
            {
                BrainId = request.BrainId,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
