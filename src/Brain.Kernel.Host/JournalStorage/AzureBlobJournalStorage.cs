using System.Buffers;
using System.Globalization;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Orleans.Journaling;

namespace Brain.Kernel.Host.JournalStorage;

public sealed class AzureBlobJournalStorage : IJournalStorage
{
    private const string FormatMetadataKey = "orjformat";
    private const string SegmentCountMetadataKey = "orjsegcount";

    private readonly BlobClient _blob;
    private readonly AzureBlobJournalStorageOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ETag? _etag;
    private int _segmentCount;
    private bool _exists;
    private string? _format;
    private Dictionary<string, string> _properties = new(StringComparer.Ordinal);

    public AzureBlobJournalStorage(BlobClient blob, AzureBlobJournalStorageOptions options)
    {
        _blob = blob ?? throw new ArgumentNullException(nameof(blob));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _format = options.JournalFormatKey;
    }

    public bool IsCompactionRequested => _segmentCount > _options.CompactionSegmentThreshold;

    public async ValueTask<bool> CreateIfNotExistsAsync(
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var properties = CopyCallerProperties(metadata);
            var blobMetadata = ToBlobMetadata(properties, _options.JournalFormatKey, segmentCount: 0);

            try
            {
                var response = await _blob.UploadAsync(
                    BinaryData.FromBytes(ReadOnlyMemory<byte>.Empty),
                    new BlobUploadOptions
                    {
                        Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                        Metadata = blobMetadata,
                    },
                    cancellationToken).ConfigureAwait(false);

                ApplyLocalState(
                    exists: true,
                    etag: response.Value.ETag,
                    format: _options.JournalFormatKey,
                    segmentCount: 0,
                    properties: properties);
                return true;
            }
            catch (RequestFailedException ex) when (
                ex.Status == 409
                || ex.ErrorCode == BlobErrorCode.BlobAlreadyExists
                || IsPreconditionFailure(ex))
            {
                await RefreshFromServerAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask AppendAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            byte[] existing = [];
            if (_exists)
            {
                existing = await DownloadBytesAsync(cancellationToken).ConfigureAwait(false);
            }

            var appended = new byte[checked(existing.Length + value.Length)];
            if (existing.Length > 0)
            {
                Buffer.BlockCopy(existing, 0, appended, 0, existing.Length);
            }

            value.CopyTo(appended.AsSpan(existing.Length));

            var nextSegmentCount = _exists ? _segmentCount + 1 : 1;
            var format = _options.JournalFormatKey;
            var blobMetadata = ToBlobMetadata(_properties, format, nextSegmentCount);
            var conditions = CreateWriteConditions(requireExisting: _exists);

            try
            {
                var response = await _blob.UploadAsync(
                    BinaryData.FromBytes(appended),
                    new BlobUploadOptions
                    {
                        Conditions = conditions,
                        Metadata = blobMetadata,
                    },
                    cancellationToken).ConfigureAwait(false);

                ApplyLocalState(
                    exists: true,
                    etag: response.Value.ETag,
                    format: format,
                    segmentCount: nextSegmentCount,
                    properties: _properties);
            }
            catch (RequestFailedException ex) when (IsPreconditionFailure(ex))
            {
                throw CreateStaleWriterException(ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ReplaceAsync(ReadOnlySequence<byte> value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var replacement = value.ToArray();
            var format = _options.JournalFormatKey;
            var blobMetadata = ToBlobMetadata(_properties, format, segmentCount: 1);
            var conditions = CreateWriteConditions(requireExisting: _exists);

            try
            {
                var response = await _blob.UploadAsync(
                    BinaryData.FromBytes(replacement),
                    new BlobUploadOptions
                    {
                        Conditions = conditions,
                        Metadata = blobMetadata,
                    },
                    cancellationToken).ConfigureAwait(false);

                ApplyLocalState(
                    exists: true,
                    etag: response.Value.ETag,
                    format: format,
                    segmentCount: 1,
                    properties: _properties);
            }
            catch (RequestFailedException ex) when (IsPreconditionFailure(ex))
            {
                throw CreateStaleWriterException(ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ReadAsync(IJournalStorageConsumer consumer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await _blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                consumer.Read(
                    new ReadOnlySequence<byte>(ReadOnlyMemory<byte>.Empty),
                    JournalMetadata.Empty,
                    complete: true);
                return;
            }

            var download = await _blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            var properties = await _blob.GetPropertiesAsync(conditions: null, cancellationToken).ConfigureAwait(false);
            var state = FromBlobProperties(properties.Value);
            ApplyLocalState(true, properties.Value.ETag, state.Format, state.SegmentCount, state.Properties);

            consumer.Read(
                new ReadOnlySequence<byte>(download.Value.Content.ToMemory()),
                new JournalMetadata(state.Format, properties.Value.ETag.ToString(), state.Properties),
                complete: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var conditions = _etag is { } etag
                ? new BlobRequestConditions { IfMatch = etag }
                : null;

            try
            {
                await _blob.DeleteIfExistsAsync(
                    DeleteSnapshotsOption.IncludeSnapshots,
                    conditions,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (IsPreconditionFailure(ex))
            {
                throw CreateStaleWriterException(ex);
            }

            ApplyLocalState(exists: false, etag: null, format: _options.JournalFormatKey, segmentCount: 0, properties: new Dictionary<string, string>(StringComparer.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IJournalMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                ApplyLocalState(false, null, _options.JournalFormatKey, 0, new Dictionary<string, string>(StringComparer.Ordinal));
                return null;
            }

            var properties = await _blob.GetPropertiesAsync(conditions: null, cancellationToken).ConfigureAwait(false);
            var state = FromBlobProperties(properties.Value);
            ApplyLocalState(true, properties.Value.ETag, state.Format, state.SegmentCount, state.Properties);
            return new JournalMetadata(state.Format, properties.Value.ETag.ToString(), state.Properties);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IJournalMetadata?> UpdateMetadataAsync(
        IReadOnlyDictionary<string, string>? set = null,
        IEnumerable<string>? remove = null,
        string? expectedETag = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (!_exists)
            {
                return null;
            }

            if (expectedETag is not null && !ETagEquals(expectedETag, _etag))
            {
                return null;
            }

            var next = new Dictionary<string, string>(_properties, StringComparer.Ordinal);
            var removeSet = new HashSet<string>(StringComparer.Ordinal);
            if (remove is not null)
            {
                foreach (var key in remove)
                {
                    ValidateCallerPropertyName(key);
                    removeSet.Add(key);
                }
            }

            if (set is not null)
            {
                foreach (var (key, value) in set)
                {
                    ValidateCallerProperty(key, value);
                    if (removeSet.Contains(key))
                    {
                        throw new ArgumentException($"Journal metadata property '{key}' cannot be both set and removed.", nameof(remove));
                    }
                }
            }

            foreach (var key in removeSet)
            {
                next.Remove(key);
            }

            if (set is not null)
            {
                foreach (var (key, value) in set)
                {
                    next[key] = value;
                }
            }

            var blobMetadata = ToBlobMetadata(next, _format ?? _options.JournalFormatKey, _segmentCount);
            var conditions = new BlobRequestConditions
            {
                IfMatch = expectedETag is null ? _etag : new ETag(expectedETag),
            };

            try
            {
                var response = await _blob.SetMetadataAsync(blobMetadata, conditions, cancellationToken).ConfigureAwait(false);
                ApplyLocalState(true, response.Value.ETag, _format ?? _options.JournalFormatKey, _segmentCount, next);
                return new JournalMetadata(_format, response.Value.ETag.ToString(), next);
            }
            catch (RequestFailedException ex) when (IsPreconditionFailure(ex))
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_etag is not null || _exists)
        {
            return;
        }

        await RefreshFromServerAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshFromServerAsync(CancellationToken cancellationToken)
    {
        if (!await _blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            ApplyLocalState(false, null, _options.JournalFormatKey, 0, new Dictionary<string, string>(StringComparer.Ordinal));
            return;
        }

        var properties = await _blob.GetPropertiesAsync(conditions: null, cancellationToken).ConfigureAwait(false);
        var state = FromBlobProperties(properties.Value);
        ApplyLocalState(true, properties.Value.ETag, state.Format, state.SegmentCount, state.Properties);
    }

    private async Task<byte[]> DownloadBytesAsync(CancellationToken cancellationToken)
    {
        var conditions = _etag is { } etag
            ? new BlobRequestConditions { IfMatch = etag }
            : null;

        try
        {
            var download = await _blob.DownloadContentAsync(
                new BlobDownloadOptions { Conditions = conditions },
                cancellationToken).ConfigureAwait(false);
            return download.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (IsPreconditionFailure(ex))
        {
            throw CreateStaleWriterException(ex);
        }
    }

    private BlobRequestConditions? CreateWriteConditions(bool requireExisting)
    {
        if (_etag is { } etag)
        {
            return new BlobRequestConditions { IfMatch = etag };
        }

        if (!requireExisting)
        {
            return new BlobRequestConditions { IfNoneMatch = ETag.All };
        }

        return null;
    }

    private void ApplyLocalState(
        bool exists,
        ETag? etag,
        string? format,
        int segmentCount,
        Dictionary<string, string> properties)
    {
        _exists = exists;
        _etag = etag;
        _format = format;
        _segmentCount = segmentCount;
        _properties = new Dictionary<string, string>(properties, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ToBlobMetadata(
        IReadOnlyDictionary<string, string> properties,
        string? format,
        int segmentCount)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FormatMetadataKey] = format ?? string.Empty,
            [SegmentCountMetadataKey] = segmentCount.ToString(CultureInfo.InvariantCulture),
        };

        foreach (var (key, value) in properties)
        {
            metadata[EncodePropertyKey(key)] = value;
        }

        return metadata;
    }

    private (string? Format, int SegmentCount, Dictionary<string, string> Properties) FromBlobProperties(BlobProperties properties)
    {
        var userProperties = new Dictionary<string, string>(StringComparer.Ordinal);
        string? format = _options.JournalFormatKey;
        var segmentCount = 0;

        foreach (var (key, value) in properties.Metadata)
        {
            if (string.Equals(key, FormatMetadataKey, StringComparison.OrdinalIgnoreCase))
            {
                format = string.IsNullOrEmpty(value) ? null : value;
                continue;
            }

            if (string.Equals(key, SegmentCountMetadataKey, StringComparison.OrdinalIgnoreCase))
            {
                _ = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out segmentCount);
                continue;
            }

            userProperties[DecodePropertyKey(key)] = value;
        }

        return (format, segmentCount, userProperties);
    }

    private static Dictionary<string, string> CopyCallerProperties(IReadOnlyDictionary<string, string>? metadata)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata is null)
        {
            return properties;
        }

        foreach (var (key, value) in metadata)
        {
            ValidateCallerProperty(key, value);
            properties.Add(key, value);
        }

        return properties;
    }

    private static void ValidateCallerProperty(string propertyName, string value)
    {
        ValidateCallerPropertyName(propertyName);
        ArgumentNullException.ThrowIfNull(value);
    }

    private static void ValidateCallerPropertyName(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (propertyName.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Journal metadata property names must not contain null characters.", nameof(propertyName));
        }

        if (propertyName.StartsWith('$'))
        {
            throw new ArgumentException(
                $"Journal metadata property '{propertyName}' is provider-owned. Caller updates must not set or remove provider-owned properties.",
                nameof(propertyName));
        }
    }

    private static string EncodePropertyKey(string key)
    {
        return "p" + Convert.ToHexString(Encoding.UTF8.GetBytes(key)).ToLowerInvariant();
    }

    private static string DecodePropertyKey(string encoded)
    {
        if (encoded.Length < 2 || (encoded[0] != 'p' && encoded[0] != 'P'))
        {
            return encoded;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromHexString(encoded.AsSpan(1)));
        }
        catch (FormatException)
        {
            return encoded;
        }
    }

    private static bool IsPreconditionFailure(RequestFailedException ex)
    {
        return ex.Status == 412
            || string.Equals(ex.ErrorCode, BlobErrorCode.ConditionNotMet.ToString(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(ex.ErrorCode, "ConditionNotMet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ETagEquals(string expectedETag, ETag? actual)
    {
        if (actual is null)
        {
            return false;
        }

        return string.Equals(expectedETag, actual.Value.ToString(), StringComparison.Ordinal)
            || string.Equals(expectedETag.Trim('"'), actual.Value.ToString("H"), StringComparison.Ordinal);
    }

    private static InvalidOperationException CreateStaleWriterException(RequestFailedException ex)
    {
        return new InvalidOperationException("The journal storage handle is stale relative to Azure Blob storage.", ex);
    }
}
