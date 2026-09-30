using DigitalBrain.Contracts.Enforcement;
using System.Reflection;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Supabase.Tests.Unit;

public sealed class TableHttpFacts
{
    private const string TableId = "sbtable-1";

    [Fact]
    public async Task ARevisionConflictOnATableReadIsAnswered409()
    {
        var status = await GetTable(memberOfBrain: true, tableRead: () => throw new SupabaseTableRevisionConflictException("stale", 3));

        Assert.Equal(StatusCodes.Status409Conflict, status);
    }

    [Fact]
    public async Task ATableReadByANonMemberIsDeniedBeforeAnyGrainIsTouched()
    {
        var grainsTouched = false;
        var status = await GetTable(memberOfBrain: false, tableRead: () => throw new InvalidOperationException("unreachable"), onGrainTouched: () => grainsTouched = true);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(grainsTouched);
    }

    private static async Task<int> GetTable(bool memberOfBrain, Func<SupabaseTableSnapshot?> tableRead, Action? onGrainTouched = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IBrainAccess>(new FixedMembership(memberOfBrain));
        builder.Services.AddSingleton<IDigitalBrain>(new FakeBrain(tableRead, onGrainTouched));
        IEndpointRouteBuilder app = builder.Build();
        new SupabaseModule().Configure(app);
        var endpoint = app.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/brains/{brainId}/tables/{tableId}");

        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "caller",
            AccountId = "caller",
            BrainId = "brain-a",
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        var http = new DefaultHttpContext { RequestServices = ((IApplicationBuilder)app).ApplicationServices };
        http.Request.RouteValues["brainId"] = "brain-a";
        http.Request.RouteValues["tableId"] = TableId;
        http.Request.Headers.Authorization = "Basic abc";
        http.Response.Body = new MemoryStream();
        http.SetEndpoint(endpoint);

        await endpoint.RequestDelegate!(http);
        return http.Response.StatusCode;
    }

    private sealed class FixedMembership(bool isMember) : IBrainAccess
    {
        public ValueTask<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(isMember);
    }

    private sealed class FakeBrain(Func<SupabaseTableSnapshot?> tableRead, Action? onGrainTouched) : IDigitalBrain
    {
        public T Get<T>(string id) where T : class, IGrainWithStringKey
        {
            onGrainTouched?.Invoke();
            var proxy = DispatchProxy.Create<T, GrainProxy>();
            ((GrainProxy)(object)proxy).Answer = method => method.Name switch
            {
                nameof(IWorkspace.Read) when typeof(T) == typeof(IWorkspace)
                    => Task.FromResult(new WorkspaceState(1, [new("window-1", "Table", WindowReference.Table(TableId), true)])),
                nameof(ISupabaseTable.Read) when typeof(T) == typeof(ISupabaseTable) => Task.FromResult(tableRead()),
                _ => throw new NotSupportedException(method.Name),
            };
            return proxy;
        }

        public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
            => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public class GrainProxy : DispatchProxy
    {
        public Func<MethodInfo, object?> Answer { get; set; } = _ => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Answer(targetMethod!);
    }
}
