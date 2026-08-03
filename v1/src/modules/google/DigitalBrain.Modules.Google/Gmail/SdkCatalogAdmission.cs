using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Google.Apis.Gmail.v1;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Google;

internal static class SdkCatalogAdmission
{
    internal const string MessagesList = "gmail_messages_list";
    internal const string MessagesGet = "gmail_messages_get";
    internal const string ThreadsList = "gmail_threads_list";
    internal const string ThreadsGet = "gmail_threads_get";
    internal const string LabelsList = "gmail_labels_list";
    internal const int MaxResultsCap = 10;

    private static readonly string[] MutatingVerbs =
    [
        "Send", "Delete", "Trash", "Untrash", "Modify", "Insert", "Import",
        "Patch", "Update", "Create", "Stop", "Watch", "BatchDelete", "BatchModify",
    ];

    private static readonly AllowlistEntry[] Allowlist =
    [
        new(MessagesList, "Users.Messages.List", "UsersResource.MessagesResource.List",
            "Lists the messages in the user's mailbox matching optional query filters."),
        new(MessagesGet, "Users.Messages.Get", "UsersResource.MessagesResource.Get",
            "Gets the specified message by id."),
        new(ThreadsList, "Users.Threads.List", "UsersResource.ThreadsResource.List",
            "Lists the threads in the user's mailbox matching optional query filters."),
        new(ThreadsGet, "Users.Threads.Get", "UsersResource.ThreadsResource.Get",
            "Gets the specified thread by id."),
        new(LabelsList, "Users.Labels.List", "UsersResource.LabelsResource.List",
            "Lists all labels in the user's mailbox."),
    ];

    internal static IReadOnlyList<string> AllowedToolNames { get; } =
        new ReadOnlyCollection<string>(Allowlist.Select(static entry => entry.ToolName).ToArray());

    internal static IReadOnlyList<string> AllowedSdkMembers { get; } =
        new ReadOnlyCollection<string>(Allowlist.Select(static entry => entry.SdkMember).ToArray());

    internal static IReadOnlyList<AIFunction> Build(GmailService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        var docs = LoadSdkDocumentation();
        var tools = new AIFunction[Allowlist.Length];
        for (var i = 0; i < Allowlist.Length; i++)
        {
            var entry = Allowlist[i];
            var description = ResolveDescription(docs, entry);
            tools[i] = entry.ToolName switch
            {
                MessagesList => CreateMessagesList(service, description),
                MessagesGet => CreateMessagesGet(service, description),
                ThreadsList => CreateThreadsList(service, description),
                ThreadsGet => CreateThreadsGet(service, description),
                LabelsList => CreateLabelsList(service, description),
                _ => throw new InvalidOperationException($"Allowlist tool '{entry.ToolName}' has no binder."),
            };
        }

        return tools;
    }

    internal static IReadOnlyList<string> EnumerateSdkResourceMethods()
    {
        var methods = new List<string>();
        CollectResourceMethods(typeof(UsersResource), "Users", methods);
        return methods;
    }

    internal static bool IsMutatingVerb(string methodName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        return MutatingVerbs.Any(verb =>
            string.Equals(methodName, verb, StringComparison.Ordinal)
            || methodName.StartsWith(verb, StringComparison.Ordinal));
    }

    private static void CollectResourceMethods(Type type, string path, List<string> methods)
    {
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            methods.Add($"{path}.{method.Name}");
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
        {
            if (!nested.Name.EndsWith("Resource", StringComparison.Ordinal))
            {
                continue;
            }

            var segment = nested.Name.EndsWith("Resource", StringComparison.Ordinal)
                ? nested.Name[..^"Resource".Length]
                : nested.Name;
            CollectResourceMethods(nested, $"{path}.{segment}", methods);
        }
    }

    private static AIFunction CreateMessagesList(GmailService service, string description)
    {
        [Description("Gmail search-box query (from:, is:unread, newer_than:…).")]
        async Task<string> Invoke(
            string? q = null,
            int? maxResults = null,
            string? pageToken = null,
            string[]? labelIds = null,
            CancellationToken cancellationToken = default)
        {
            var request = service.Users.Messages.List("me");
            if (!string.IsNullOrWhiteSpace(q))
            {
                request.Q = q;
            }

            request.MaxResults = BoundMaxResults(maxResults);
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                request.PageToken = pageToken;
            }

            if (labelIds is { Length: > 0 })
            {
                request.LabelIds = labelIds;
            }

            var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var count = response.Messages?.Count ?? 0;
            return $"listed {count} message id(s); nextPageToken present={ !string.IsNullOrEmpty(response.NextPageToken) }";
        }

        return AIFunctionFactory.Create(Invoke, MessagesList, description);
    }

    private static AIFunction CreateMessagesGet(GmailService service, string description)
    {
        [Description("Gmail message id.")]
        async Task<GmailMessage> Invoke(
            string id,
            string format = "FULL",
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var request = service.Users.Messages.Get("me", id);
            request.Format = ParseMessageFormat(format);
            var message = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return GmailMessageMapper.ToMessage(message, requestedId: id);
        }

        return AIFunctionFactory.Create(Invoke, MessagesGet, description);
    }

    private static AIFunction CreateThreadsList(GmailService service, string description)
    {
        async Task<string> Invoke(
            string? q = null,
            int? maxResults = null,
            string? pageToken = null,
            string[]? labelIds = null,
            CancellationToken cancellationToken = default)
        {
            var request = service.Users.Threads.List("me");
            if (!string.IsNullOrWhiteSpace(q))
            {
                request.Q = q;
            }

            request.MaxResults = BoundMaxResults(maxResults);
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                request.PageToken = pageToken;
            }

            if (labelIds is { Length: > 0 })
            {
                request.LabelIds = labelIds;
            }

            var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var count = response.Threads?.Count ?? 0;
            return $"listed {count} thread id(s); nextPageToken present={ !string.IsNullOrEmpty(response.NextPageToken) }";
        }

        return AIFunctionFactory.Create(Invoke, ThreadsList, description);
    }

    private static AIFunction CreateThreadsGet(GmailService service, string description)
    {
        async Task<string> Invoke(
            string id,
            string format = "METADATA",
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var request = service.Users.Threads.Get("me", id);
            request.Format = ParseThreadFormat(format);
            var thread = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var messages = thread.Messages?.Count ?? 0;
            return $"thread id={thread.Id}; messages={messages}";
        }

        return AIFunctionFactory.Create(Invoke, ThreadsGet, description);
    }

    private static AIFunction CreateLabelsList(GmailService service, string description)
    {
        async Task<string> Invoke(CancellationToken cancellationToken = default)
        {
            var response = await service.Users.Labels.List("me").ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var count = response.Labels?.Count ?? 0;
            return $"listed {count} label(s)";
        }

        return AIFunctionFactory.Create(Invoke, LabelsList, description);
    }

    internal static long BoundMaxResults(int? maxResults)
    {
        if (maxResults is null || maxResults <= 0)
        {
            return MaxResultsCap;
        }

        return Math.Min(maxResults.Value, MaxResultsCap);
    }

    internal static UsersResource.MessagesResource.GetRequest.FormatEnum ParseMessageFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)
            || string.Equals(format, "FULL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "FULL_CONTENT", StringComparison.OrdinalIgnoreCase))
        {
            return UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        }

        if (string.Equals(format, "METADATA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "METADATA_ONLY", StringComparison.OrdinalIgnoreCase))
        {
            return UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        }

        throw new InvalidOperationException(
            $"Gmail message format '{format}' is not allowed. Use METADATA or FULL.");
    }

    private static UsersResource.ThreadsResource.GetRequest.FormatEnum ParseThreadFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)
            || string.Equals(format, "METADATA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "METADATA_ONLY", StringComparison.OrdinalIgnoreCase))
        {
            return UsersResource.ThreadsResource.GetRequest.FormatEnum.Metadata;
        }

        if (string.Equals(format, "FULL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "FULL_CONTENT", StringComparison.OrdinalIgnoreCase))
        {
            return UsersResource.ThreadsResource.GetRequest.FormatEnum.Full;
        }

        throw new InvalidOperationException(
            $"Gmail thread format '{format}' is not allowed. Use METADATA or FULL.");
    }

    private static string ResolveDescription(Dictionary<string, string> docs, AllowlistEntry entry)
    {
        foreach (var (key, value) in docs)
        {
            if (key.Contains(entry.XmlMemberHint, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return entry.FallbackDescription;
    }

    private static Dictionary<string, string> LoadSdkDocumentation()
    {
        var location = typeof(GmailService).Assembly.Location;
        if (string.IsNullOrWhiteSpace(location))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var xmlPath = Path.ChangeExtension(location, ".xml");
        if (!File.Exists(xmlPath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var document = XDocument.Load(xmlPath);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in document.Descendants("member"))
            {
                var name = member.Attribute("name")?.Value;
                var summary = member.Element("summary")?.Value;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(summary))
                {
                    continue;
                }

                map[name] = CollapseWhitespace(summary);
            }

            return map;
        }
        catch (IOException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (System.Xml.XmlException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    private readonly record struct AllowlistEntry(
        string ToolName,
        string SdkMember,
        string XmlMemberHint,
        string FallbackDescription);
}
