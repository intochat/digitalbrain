using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;

namespace DigitalBrain.MyData;

[Orleans.Metadata.DefaultGrainType("vault")]
[Alias("vault")]
public interface IVault : INeuron
{
    Task<VaultView> Read(CallerContext caller, CancellationToken cancellationToken = default);

    Task<VaultFieldView> SetField(CallerContext caller, string fieldPath, FieldKind kind, string value, CancellationToken cancellationToken = default);

    Task<SecretRef> SetSecret(CallerContext caller, string fieldPath, string label, string value, CancellationToken cancellationToken = default);

    Task<VaultExport> Export(CallerContext caller, CancellationToken cancellationToken = default);

    Task Erase(CallerContext caller, CancellationToken cancellationToken = default);

    Task<VaultAuditEntry[]> Audit(CallerContext caller, int limit, CancellationToken cancellationToken = default);

    Task<string> ResolveSecret(CallerContext caller, SecretRef secret, CancellationToken cancellationToken = default);
}
