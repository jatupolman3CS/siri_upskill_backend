namespace Siri.Modules.Identity.Features.Login;

/// <summary>
/// Request payload for POST /api/identity/login. Binds from the JSON request body (default
/// minimal-API inference for a non-primitive parameter type) — deliberately carries only what a
/// client legitimately supplies about itself: credentials, plus an optional self-reported device id/
/// name for the session row (e.g. so a future "devices" UI, P0-18, can show something more useful
/// than a raw GUID). <see cref="DeviceId"/>/<see cref="DeviceName"/> are the client's own claim about
/// itself, no different in trust level from a User-Agent string — never anything privileged.
/// <para>
/// User-Agent and IP address are <b>not</b> here on purpose: those must come from the server's own
/// view of the request (<c>HttpContext</c>), never from client-supplied JSON, or a caller could simply
/// lie about them in the body (task instruction: "User-Agent and IP read server-side from the request,
/// never trusted from the body"). <see cref="LoginEndpoint"/> reads them separately and passes them
/// into <see cref="LoginHandler.HandleAsync"/> as their own parameters, not as part of this command.
/// </para>
/// </summary>
public sealed record LoginCommand(string Email, string Password, string? DeviceId, string? DeviceName);
