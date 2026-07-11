using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public interface IMcpIntegrationToolGateway
{
    Task<GmailReadResult> ReadIncomingAtOffsetAsync(
        string ownerScope,
        GmailReadRequest request,
        CancellationToken cancellationToken = default);

    Task<SalesforceReadResult> ReadSalesforceAsync(
        string ownerScope,
        string toolId,
        CancellationToken cancellationToken = default);

    Task<GmailMessageListResult> ReadGmailMessagesAsync(
        string ownerScope,
        GmailMessageListRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GmailMessageListResult(
            GmailReadStatus.Unavailable,
            [],
            new GmailResultCoverage(0, 0, 0, 0, 0, false, false)));

    Task<GmailMailboxOverviewResult> ReadGmailMailboxOverviewAsync(
        string ownerScope,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GmailMailboxOverviewResult(GmailReadStatus.Unavailable));

    Task<GmailThreadListResult> ReadGmailThreadsAsync(
        string ownerScope,
        GmailThreadListRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GmailThreadListResult(
            GmailReadStatus.Unavailable,
            [],
            new GmailResultCoverage(0, 0, 0, 0, 0, false, false)));

    Task<SalesforceReadResult> DiscoverSalesforceObjectsAsync(
        string ownerScope,
        SalesforceDiscoveryRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SalesforceReadResult(SalesforceReadStatus.Unavailable));

    Task<SalesforceReadResult> ReadSalesforceRecordsAsync(
        string ownerScope,
        SalesforceRecordReadRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SalesforceReadResult(SalesforceReadStatus.Unavailable));

    Task<SalesforceReadResult> SearchSalesforceRecordsAsync(
        string ownerScope,
        SalesforceSearchRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SalesforceReadResult(SalesforceReadStatus.Unavailable));

    Task<SalesforceReadResult> AggregateSalesforceRecordsAsync(
        string ownerScope,
        SalesforceAggregateRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SalesforceReadResult(SalesforceReadStatus.Unavailable));

    Task<SalesforceReadResult> ContinueSalesforceRecordsAsync(
        string ownerScope,
        SalesforceContinuationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SalesforceReadResult(SalesforceReadStatus.Unavailable));
}

public sealed class McpIntegrationToolGateway(IClusterClient cluster) : IMcpIntegrationToolGateway
{
    public async Task<GmailReadResult> ReadIncomingAtOffsetAsync(
        string ownerScope,
        GmailReadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.provider.gmail", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", GmailTools.ReadIncomingAtOffset);
        var result = await cluster.GetGrain<IGmailReadToolGrain>(ownerScope)
            .ReadIncomingAtOffsetAsync(request, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        return result;
    }

    public async Task<SalesforceReadResult> ReadSalesforceAsync(
        string ownerScope,
        string toolId,
        CancellationToken cancellationToken = default)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.provider.salesforce", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", toolId);
        var grain = cluster.GetGrain<ISalesforceReadToolGrain>(ownerScope);
        var result = await (toolId switch
        {
            SalesforceTools.ReadLatestAccount => grain.ReadLatestAccountAsync(cancellationToken),
            SalesforceTools.ReadCurrentProfile => grain.ReadCurrentProfileAsync(cancellationToken),
            SalesforceTools.ReadRecentAccounts => grain.ReadRecentAccountsAsync(cancellationToken),
            SalesforceTools.ReadRecentContacts => grain.ReadRecentContactsAsync(cancellationToken),
            SalesforceTools.ReadCrmSchema => grain.ReadCrmSchemaAsync(cancellationToken),
            _ => Task.FromResult(new SalesforceReadResult(SalesforceReadStatus.Unavailable))
        }).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        return result;
    }

    public async Task<GmailMessageListResult> ReadGmailMessagesAsync(
        string ownerScope,
        GmailMessageListRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.provider.gmail", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", GmailTools.ReadMessages);
        var result = await cluster.GetGrain<IGmailMetadataToolGrain>(ownerScope)
            .ReadMessagesAsync(request, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        activity?.SetTag("db.ino.result_count", result.Messages.Length);
        return result;
    }

    public async Task<GmailMailboxOverviewResult> ReadGmailMailboxOverviewAsync(
        string ownerScope,
        CancellationToken cancellationToken = default)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.provider.gmail", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", GmailTools.ReadMailboxOverview);
        var result = await cluster.GetGrain<IGmailMetadataToolGrain>(ownerScope)
            .ReadMailboxOverviewAsync(cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        return result;
    }

    public async Task<GmailThreadListResult> ReadGmailThreadsAsync(
        string ownerScope,
        GmailThreadListRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.provider.gmail", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", GmailTools.ReadThreads);
        var result = await cluster.GetGrain<IGmailMetadataToolGrain>(ownerScope)
            .ReadThreadsAsync(request, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        activity?.SetTag("db.ino.result_count", result.Threads.Length);
        return result;
    }

    public Task<SalesforceReadResult> DiscoverSalesforceObjectsAsync(
        string ownerScope,
        SalesforceDiscoveryRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeSalesforceAsync(ownerScope, SalesforceTools.DiscoverObjects,
            grain => grain.DiscoverObjectsAsync(request, cancellationToken));

    public Task<SalesforceReadResult> ReadSalesforceRecordsAsync(
        string ownerScope,
        SalesforceRecordReadRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeSalesforceAsync(ownerScope, SalesforceTools.ReadRecords,
            grain => grain.ReadRecordsAsync(request, cancellationToken));

    public Task<SalesforceReadResult> SearchSalesforceRecordsAsync(
        string ownerScope,
        SalesforceSearchRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeSalesforceAsync(ownerScope, SalesforceTools.SearchRecords,
            grain => grain.SearchRecordsAsync(request, cancellationToken));

    public Task<SalesforceReadResult> AggregateSalesforceRecordsAsync(
        string ownerScope,
        SalesforceAggregateRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeSalesforceAsync(ownerScope, SalesforceTools.AggregateRecords,
            grain => grain.AggregateRecordsAsync(request, cancellationToken));

    public Task<SalesforceReadResult> ContinueSalesforceRecordsAsync(
        string ownerScope,
        SalesforceContinuationRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeSalesforceAsync(ownerScope, SalesforceTools.ContinueRecords,
            grain => grain.ContinueRecordsAsync(request, cancellationToken));

    private async Task<SalesforceReadResult> InvokeSalesforceAsync(
        string ownerScope,
        string toolId,
        Func<ISalesforceReadToolGrain, Task<SalesforceReadResult>> invoke)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.provider.salesforce", ActivityKind.Client);
        activity?.SetTag("db.ino.tool_id", toolId);
        var result = await invoke(cluster.GetGrain<ISalesforceReadToolGrain>(ownerScope)).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider_outcome", result.Status.ToString());
        activity?.SetTag("db.ino.result_count", result.ReturnedCount);
        return result;
    }
}
