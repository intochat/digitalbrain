namespace DigitalBrain.Behaviors.Tests;

using System;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Behaviors.Runtime.Artifacts;
using Xunit;

public sealed partial class CanonicalArtifacts
{
    private static BehaviorArtifactEnvelope CreateEnvelope(
        bool reverseOrder,
        string requestSchema = "{\"type\":\"object\",\"properties\":{\"scene\":{\"type\":\"string\"}}}",
        string resultSchema = "{\"type\":\"object\",\"properties\":{\"opened\":{\"type\":\"boolean\"}}}")
    {
        var normalizedRequest = requestSchema == "{\"type\":\"object\",\"properties\":{\"scene\":{\"type\":\"string\"}}}"
            ? "{\"properties\":{\"scene\":{\"type\":\"string\"}},\"type\":\"object\"}"
            : requestSchema;
        var normalizedResult = resultSchema == "{\"type\":\"object\",\"properties\":{\"opened\":{\"type\":\"boolean\"}}}"
            ? "{\"properties\":{\"opened\":{\"type\":\"boolean\"}},\"type\":\"object\"}"
            : resultSchema;

        var oneOf = reverseOrder
            ? "{\"oneOf\":[{\"properties\":{\"caseId\":{\"const\":\"case.scene\"},\"payload\":" + normalizedRequest + "},\"type\":\"object\"}]}"
            : "{\"oneOf\":[{\"properties\":{\"caseId\":{\"const\":\"case.scene\"},\"payload\":" + requestSchema + "},\"type\":\"object\"}]}";

        if (reverseOrder && requestSchema == "{\"type\":\"object\",\"properties\":{\"scene\":{\"type\":\"string\"}}}")
        {
            oneOf = "{\"oneOf\":[{\"properties\":{\"caseId\":{\"const\":\"case.scene\"},\"payload\":" + normalizedRequest + "},\"type\":\"object\"}]}";
        }

        var contract = reverseOrder
            ? new BehaviorContractManifest(
                "com.digitalbrain.start-ui",
                1,
                oneOf,
                [new BehaviorContractCaseManifest("case.scene", 1, "scene", normalizedRequest)],
                normalizedResult)
            : new BehaviorContractManifest(
                "com.digitalbrain.start-ui",
                1,
                oneOf,
                [new BehaviorContractCaseManifest("case.scene", 1, "scene", requestSchema)],
                resultSchema);

        IReadOnlyList<BehaviorScenarioManifest> scenarios = reverseOrder
            ?
            [
                new BehaviorScenarioManifest("scenario.zulu", "Zulu path", "bind.zulu"),
                new BehaviorScenarioManifest("scenario.alpha", "Alpha path", "bind.alpha"),
            ]
            :
            [
                new BehaviorScenarioManifest("scenario.alpha", "Alpha path", "bind.alpha"),
                new BehaviorScenarioManifest("scenario.zulu", "Zulu path", "bind.zulu"),
            ];

        return new BehaviorArtifactEnvelope(
            new BehaviorDefinitionManifest(
                new BehaviorId("com.digitalbrain.start-ui"),
                "Start UI",
                "Open the first scene.",
                reverseOrder
                    ? new BehaviorEntryPoints(["db.ready", "db.activated"], contract)
                    : new BehaviorEntryPoints(["db.activated", "db.ready"], contract),
                scenarios,
                "Start UI opens the first scene for alpha and zulu paths.",
                reverseOrder
                    ? new BehaviorCompilerPolicy("11.0.100-preview.6", "5.6.0", "Preview", "contract-only-v1")
                    : new BehaviorCompilerPolicy("11.0.100-preview.6", "5.6.0", "Preview", "contract-only-v1"),
                reverseOrder
                    ? [
                        new BehaviorCapabilityGrant("db.time", "time.schedule-request", 1, "time.schedule-result", 1, "named", "clock"),
                        new BehaviorCapabilityGrant("db.shell", "shell.open-request", 1, "shell.open-result", 1, "named", "desk"),
                    ]
                    : [
                        new BehaviorCapabilityGrant("db.shell", "shell.open-request", 1, "shell.open-result", 1, "named", "desk"),
                        new BehaviorCapabilityGrant("db.time", "time.schedule-request", 1, "time.schedule-result", 1, "named", "clock"),
                    ],
                new BehaviorResourceLimits(1_000, 64 * 1024 * 1024, 30_000)),
            "public sealed class StartUi { }\n",
            "Feature: start-ui\n  Scenario: alpha path\n    Then alpha succeeds\n  Scenario: zulu path\n    Then zulu succeeds\n",
            reverseOrder ? "{\"version\":1,\"libraries\":{}}" : "{\"libraries\":{},\"version\":1}",
            new byte[] { 0, 1, 2, 3 },
            "{\"runtimeTarget\":{\"name\":\"net10.0\"}}",
            reverseOrder
                ? "{\"diagnostics\":[],\"languageVersion\":\"Preview\",\"policy\":\"contract-only-v1\",\"roslyn\":\"5.6.0\",\"sdk\":\"11.0.100-preview.6\",\"succeeded\":true}"
                : "{\"diagnostics\":[],\"languageVersion\":\"Preview\",\"policy\":\"contract-only-v1\",\"roslyn\":\"5.6.0\",\"sdk\":\"11.0.100-preview.6\",\"succeeded\":true}",
            "{\"policy\":\"v1\",\"result\":\"accepted\"}",
            "{\"passed\":true,\"scenarios\":2}");
    }

    private static void ReadWithExtraEntry(string name)
    {
        var bytes = CreateZip((archive, _) => WriteText(archive.CreateEntry(name, CompressionLevel.NoCompression), "unsafe"));
        _ = CanonicalArtifactReader.Read(bytes);
    }

    private static byte[] CreateZip(Action<ZipArchive, byte[]> mutation)
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        using var stream = new MemoryStream();
        stream.Write(artifact.Bytes);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            mutation(archive, artifact.Bytes);
        }

        return stream.ToArray();
    }

    private static byte[] CreateRequiredZip(string? skip = null)
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        using var source = new ZipArchive(new MemoryStream(artifact.Bytes), ZipArchiveMode.Read);
        using var stream = new MemoryStream();
        using (var destination = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries.Where(entry => entry.FullName != skip))
            {
                var replacement = destination.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                replacement.LastWriteTime = entry.LastWriteTime;
                using var input = entry.Open();
                using var output = replacement.Open();
                input.CopyTo(output);
            }
        }

        return stream.ToArray();
    }

    private static void WriteText(ZipArchiveEntry entry, string value)
        => WriteBytes(entry, Encoding.UTF8.GetBytes(value));

    private static void WriteBytes(ZipArchiveEntry entry, byte[] value)
    {
        using var stream = entry.Open();
        stream.Write(value);
    }

    private static byte[] ReplaceStoredEntryText(byte[] artifact, string name, string original, string replacement)
    {
        Assert.Equal(original.Length, replacement.Length);
        var text = Encoding.UTF8.GetString(GetStoredEntryBytes(artifact, name));
        var index = text.IndexOf(original, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Could not find '{original}' in {name}.");
        text = string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + replacement.Length));
        return MutateStoredEntryBytes(artifact, name, bytes => Encoding.UTF8.GetBytes(text).CopyTo(bytes, 0));
    }

    private static byte[] MutateStoredEntryBytes(byte[] artifact, string name, Action<byte[]> mutate)
    {
        var bytes = artifact.ToArray();
        var local = FindEntryLocalHeader(bytes, name);
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(local + 26, 2));
        var dataOffset = local + 30 + nameLength;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(local + 22, 4));
        var content = bytes.AsSpan(dataOffset, checked((int)length)).ToArray();
        mutate(content);
        content.CopyTo(bytes, dataOffset);

        var crc = ComputeCrc32(content);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(local + 14, 4), crc);
        var central = FindCentralEntry(bytes, name);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 16, 4), crc);
        return bytes;
    }

    private static byte[] GetStoredEntryBytes(byte[] artifact, string name)
    {
        var local = FindEntryLocalHeader(artifact, name);
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(local + 26, 2));
        var dataOffset = local + 30 + nameLength;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(local + 22, 4));
        return artifact.AsSpan(dataOffset, checked((int)length)).ToArray();
    }

    private static int FindEntryLocalHeader(byte[] bytes, string name)
    {
        for (var offset = 0; offset < bytes.Length;)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) != 0x04034B50u)
            {
                break;
            }

            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 26, 2));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 22, 4));
            if (Encoding.UTF8.GetString(bytes, offset + 30, nameLength) == name)
            {
                return offset;
            }

            offset += checked(30 + nameLength + (int)length);
        }

        throw new InvalidOperationException($"ZIP entry '{name}' was not found.");
    }

    private static int FindCentralEntry(byte[] bytes, string name)
    {
        for (var offset = FindSignature(bytes, 0x02014B50u); offset >= 0;)
        {
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28, 2));
            if (Encoding.UTF8.GetString(bytes, offset + 46, nameLength) == name)
            {
                return offset;
            }

            offset = FindSignature(bytes, 0x02014B50u, offset + 1);
        }

        throw new InvalidOperationException($"Central ZIP entry '{name}' was not found.");
    }

    private static uint ComputeCrc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
            }
        }

        return ~crc;
    }

    private static int FindSignature(byte[] bytes, uint signature, int start = 0)
    {
        for (var index = start; index <= bytes.Length - 4; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index, 4)) == signature)
            {
                return index;
            }
        }

        throw new InvalidOperationException("ZIP signature was not found.");
    }
}
