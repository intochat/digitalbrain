# DigitalBrain.Runtime.Http

The HTTP edge seam for DigitalBrain modules on ASP.NET Core (09-03). It references the ASP.NET Core shared framework and
`DigitalBrain.Capabilities.Identity` only: no Orleans.

- `IModuleHttpSurface.Map(brainGroup)`: a module's routes. Register with `services.AddModuleHttpSurface<T>()`; the
  host maps every registered surface with one `endpoints.MapModuleHttpSurfaces()` call. Every route needs an
  authenticated caller (401 otherwise) unless it calls `.AllowAnonymous()` explicitly.
- `BrainRouteGroups.Brain(endpoints)`: `/brains/{brainId}` with the brain id check (400) and the membership check
  through identity@1 `IIdentity.Membership` (403 when there is no identity@1, no principal claim or no membership).
  `BrainRouteGroups.MemberOf(http)` returns the checked member. `BrainRouteGroups.Session(endpoints)` checks the brain
  id only.
- `ISessionIssuer`: `IssueAsync(HttpContext, SessionRequest)` and `EndAsync(HttpContext)` write and clear the session
  cookie for identity@1 `ISessions`. `SessionPrincipal.Create` builds the cookie principal: the `principal`,
  `account`, `brain` and `role` claims come from the member only, and a request naming one of them is rejected.

It replaces the os `IAuthorityHttpExtension` and `BrainRoutes` pair; the os keeps its own path until 09-06.
