namespace Ino.Core;

/// <summary>
/// Structured RFW (Remote Flutter Widgets) payload returned by neurons that
/// render rich cards. <see cref="DescriptionDsl"/> is the UTF-8 encoded RFW
/// text-library DSL (parsed by <c>parseLibraryFile</c> on the Dart side);
/// <see cref="DataPayload"/> is the UTF-8 JSON document that <c>DynamicContent</c>
/// binds against. <see cref="LibraryName"/> identifies which custom widget
/// library the bubble should mount (e.g. <c>"ino.travel.flights"</c>).
/// </summary>
/// <remarks>
/// Both byte arrays are concrete <see cref="byte"/>[] (not <c>IReadOnlyList&lt;byte&gt;</c>)
/// because Orleans's cross-silo deep-copy throws <c>CodecNotFoundException</c>
/// on the synthesised <c>&lt;&gt;z__ReadOnlyArray&lt;T&gt;</c> backing type — see
/// the known-traps section of <c>CLAUDE.md</c>.
/// </remarks>
[GenerateSerializer]
public sealed record RfwPayload(
    [property: Id(0)] string LibraryName,
    [property: Id(1)] byte[] DescriptionDsl,
    [property: Id(2)] byte[] DataPayload);
