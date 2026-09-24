using DigitalBrain.Coding;

namespace IntoChat.Packages;

internal sealed class PackageCheckFailedException(CodeCheckSnapshot check)
    : Exception($"The package did not pass its check ({check.Status}); nothing was committed.")
{
    public CodeCheckSnapshot Check { get; } = check;
}
