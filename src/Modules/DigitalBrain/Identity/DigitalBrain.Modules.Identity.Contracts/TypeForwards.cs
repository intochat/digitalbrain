using System.Runtime.CompilerServices;
using DigitalBrain.Identity;

[assembly: TypeForwardedTo(typeof(Account))]
[assembly: TypeForwardedTo(typeof(Member))]
[assembly: TypeForwardedTo(typeof(Invitation))]
[assembly: TypeForwardedTo(typeof(MemberRole))]
[assembly: TypeForwardedTo(typeof(GrantMode))]
[assembly: TypeForwardedTo(typeof(Grant))]
[assembly: TypeForwardedTo(typeof(IIdentityDirectory))]
[assembly: TypeForwardedTo(typeof(IGrantStore))]
[assembly: TypeForwardedTo(typeof(IdentityGrains))]
