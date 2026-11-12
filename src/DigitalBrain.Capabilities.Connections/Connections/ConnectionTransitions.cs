namespace DigitalBrain.Capabilities.Connections;

using DigitalBrain.Capabilities.Secrets;

/// <summary>Pure transitions evolving connection state and producing domain facts.</summary>
public static class ConnectionTransitions
{
    /// <summary>Transitions to connected with a secret token value.</summary>
    public static (TState Next, Signal Fact) Connect<TState>(
        this TState state,
        string connectorId,
        string connectionId,
        string authMethodId,
        SealedSecret value,
        string? account = null,
        DateTimeOffset? probedAt = null)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authMethodId);
        ArgumentNullException.ThrowIfNull(value);

        var secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal)
        {
            [ConnectionState.ValueSecret] = value,
        };
        var next = state with
        {
            AuthMethodId = authMethodId,
            Status = ConnectionStatus.Connected,
            Secrets = secrets,
            Account = account ?? state.Account,
            ExpiresAt = null,
            Scopes = "",
            Detail = null,
            LastProbedAt = probedAt ?? state.LastProbedAt,
        };
        return (next, new ConnectionConnected(connectorId, connectionId, authMethodId, next.Account, probedAt));
    }

    /// <summary>Transitions to connected with OAuth credentials.</summary>
    public static (TState Next, Signal Fact) ConnectOAuth<TState>(
        this TState state,
        string connectorId,
        string connectionId,
        OAuthGrant grant,
        SealedSecret access,
        SealedSecret? refresh,
        string? account = null,
        DateTimeOffset? probedAt = null)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(access);

        var secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal)
        {
            [ConnectionState.AccessSecret] = access,
        };
        if (refresh is not null)
        {
            secrets[ConnectionState.RefreshSecret] = refresh;
        }

        var next = state with
        {
            AuthMethodId = grant.AuthMethodId,
            Status = ConnectionStatus.Connected,
            Secrets = secrets,
            Account = account ?? state.Account,
            ExpiresAt = grant.ExpiresAt,
            Scopes = grant.Scopes,
            Detail = null,
            LastProbedAt = probedAt ?? state.LastProbedAt,
        };
        return (next, new ConnectionConnected(connectorId, connectionId, grant.AuthMethodId, next.Account, probedAt));
    }

    /// <summary>Adopts connection metadata without credentials.</summary>
    public static (TState Next, Signal Fact) Adopt<TState>(
        this TState state,
        string connectorId,
        string connectionId,
        string authMethodId,
        string? account)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authMethodId);

        var next = state with
        {
            AuthMethodId = authMethodId,
            Account = account,
            Status = ConnectionStatus.Disconnected,
            Detail = "Shared into this brain; its credential is not sealed here yet.",
        };
        return (next, new ConnectionAdopted(connectorId, connectionId, authMethodId, account));
    }

    /// <summary>Transitions to disconnected, clearing credentials and account.</summary>
    public static (TState Next, Signal Fact) Disconnect<TState>(
        this TState state,
        string connectorId,
        string connectionId)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var next = state with
        {
            Status = ConnectionStatus.Disconnected,
            Secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal),
            ExpiresAt = null,
            Account = null,
            Detail = null,
        };
        return (next, new ConnectionDisconnected(connectorId, connectionId));
    }

    /// <summary>Transitions to expired.</summary>
    public static (TState Next, Signal Fact) Expire<TState>(
        this TState state,
        string connectorId,
        string connectionId,
        string? detail = null,
        DateTimeOffset? probedAt = null)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var next = state with
        {
            Status = ConnectionStatus.Expired,
            Detail = detail ?? state.Detail,
            LastProbedAt = probedAt ?? state.LastProbedAt,
        };
        return (next, new ConnectionExpired(connectorId, connectionId, probedAt));
    }

    /// <summary>Transitions to failing.</summary>
    public static (TState Next, Signal Fact) Fail<TState>(
        this TState state,
        string connectorId,
        string connectionId,
        string detail,
        DateTimeOffset? probedAt = null)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        var next = state with
        {
            Status = ConnectionStatus.Failing,
            Detail = detail,
            LastProbedAt = probedAt ?? state.LastProbedAt,
        };
        return (next, new ConnectionFailing(connectorId, connectionId, detail, probedAt));
    }

    /// <summary>Updates connection secrets (e.g. silent token refresh).</summary>
    public static (TState Next, Signal Fact) UpdateSecrets<TState>(
        this TState state,
        string connectorId,
        string connectionId,
        IReadOnlyDictionary<string, SealedSecret> secrets,
        DateTimeOffset? expiresAt = null)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(secrets);

        var next = state with
        {
            Secrets = new Dictionary<string, SealedSecret>(secrets, StringComparer.Ordinal),
            ExpiresAt = expiresAt ?? state.ExpiresAt,
        };
        return (next, new ConnectionUpdated(connectorId, connectionId));
    }

    /// <summary>Shares the connection with a recipient.</summary>
    public static (TState Next, Signal Fact) Share<TState>(
        this TState state,
        string connectorId,
        string connectionId,
        string recipient)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        if (state.SharedInto.Contains(recipient, StringComparer.Ordinal))
        {
            return (state, new ConnectionUpdated(connectorId, connectionId));
        }

        var next = state with
        {
            SharedInto = [.. state.SharedInto, recipient],
        };
        return (next, new ConnectionShared(connectorId, connectionId, recipient));
    }

    /// <summary>Unshares the connection from a recipient.</summary>
    public static (TState Next, Signal Fact) Unshare<TState>(
        this TState state,
        string connectorId,
        string connectionId,
        string recipient)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        if (!state.SharedInto.Contains(recipient, StringComparer.Ordinal))
        {
            return (state, new ConnectionUpdated(connectorId, connectionId));
        }

        var next = state with
        {
            SharedInto = [.. state.SharedInto.Where(s => s != recipient)],
        };
        return (next, new ConnectionUnshared(connectorId, connectionId, recipient));
    }

    /// <summary>Sets secret credentials on state.</summary>
    public static TState WithSecret<TState>(this TState state, string authMethodId, SealedSecret value)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(authMethodId);
        ArgumentNullException.ThrowIfNull(value);
        return state with
        {
            AuthMethodId = authMethodId,
            Secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal) { [ConnectionState.ValueSecret] = value },
            ExpiresAt = null,
            Scopes = "",
        };
    }

    /// <summary>Sets OAuth credentials on state.</summary>
    public static TState WithOAuth<TState>(this TState state, OAuthGrant grant, SealedSecret access, SealedSecret? refresh)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(access);

        var secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal)
        {
            [ConnectionState.AccessSecret] = access,
        };
        if (refresh is not null)
        {
            secrets[ConnectionState.RefreshSecret] = refresh;
        }

        return state with
        {
            AuthMethodId = grant.AuthMethodId,
            Secrets = secrets,
            ExpiresAt = grant.ExpiresAt,
            Scopes = grant.Scopes,
        };
    }

    /// <summary>Sets connection status and detail.</summary>
    public static TState In<TState>(this TState state, ConnectionStatus status, string? detail = null)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with { Status = status, Detail = detail };
    }

    /// <summary>Clears credentials and account on state.</summary>
    public static TState Cleared<TState>(this TState state)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with
        {
            Secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal),
            ExpiresAt = null,
            Account = null,
        };
    }

    /// <summary>Adds a shared recipient if not already present.</summary>
    public static TState WithSharing<TState>(this TState state, string recipient)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        return state.SharedInto.Contains(recipient, StringComparer.Ordinal)
            ? state
            : state with { SharedInto = [.. state.SharedInto, recipient] };
    }

    /// <summary>Removes a shared recipient if present.</summary>
    public static TState WithoutSharing<TState>(this TState state, string recipient)
        where TState : ConnectionState
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        return !state.SharedInto.Contains(recipient, StringComparer.Ordinal)
            ? state
            : state with { SharedInto = [.. state.SharedInto.Where(s => s != recipient)] };
    }
}
