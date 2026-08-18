/**
 * Request/response shapes for `/api/identity/*`, typed directly off the real backend records —
 * never guessed (CLAUDE.md rule 2). Each interface below cites the exact backend file it mirrors.
 * Property casing is camelCase throughout because ASP.NET Core's minimal-API JSON output uses
 * `JsonSerializerDefaults.Web` (camelCase) unless a module opts out, and this module does not.
 */

// ---- Register (Features/Register/Command.cs, Response.cs) ----------------------------------

export interface RegisterRequest {
  readonly email: string;
  readonly password: string;
  readonly displayName: string;
}

export interface RegisterResponse {
  readonly message: string;
}

// ---- ConfirmEmail (Features/ConfirmEmail/Command.cs, Response.cs) --------------------------

export interface ConfirmEmailResponse {
  readonly message: string;
}

// ---- Login (Features/Login/Command.cs, Response.cs) ----------------------------------------

/** `deviceId`/`deviceName` are deliberately omitted here — this frontend has no per-device
 *  naming UX yet, so `LoginHandler` falls back to a generated `deviceId` and a `null`
 *  `deviceName` on its own (see `LoginCommand`'s doc comment); nothing is lost by not sending them. */
export interface LoginRequest {
  readonly email: string;
  readonly password: string;
}

/** Never a refresh token here — that arrives only as an httpOnly cookie the browser manages
 *  automatically (`RefreshTokenCookie.cs`); see `LoginResponse.cs`'s own doc comment. */
export interface LoginResponse {
  readonly accessToken: string;
  readonly accessTokenExpiresAtUtc: string;
}

// ---- ForgotPassword (Features/ForgotPassword/Command.cs, Response.cs) ----------------------

export interface ForgotPasswordResponse {
  readonly message: string;
}

// ---- ResetPassword (Features/ResetPassword/Command.cs, Response.cs) ------------------------

export interface ResetPasswordRequest {
  readonly token: string;
  readonly newPassword: string;
}

export interface ResetPasswordResponse {
  readonly message: string;
}

// ---- ListSessions (Features/ListSessions/Response.cs) --------------------------------------

export interface SessionSummary {
  readonly sessionId: string;
  readonly deviceName: string | null;
  readonly userAgent: string | null;
  readonly ipAddress: string | null;
  readonly createdAtUtc: string;
  readonly lastSeenAtUtc: string;
  readonly isActive: boolean;
  readonly revokedAtUtc: string | null;
  readonly isCurrentSession: boolean;
}

export interface ListSessionsResponse {
  readonly sessions: readonly SessionSummary[];
}

// ---- RevokeSession (Features/RevokeSession/Response.cs) ------------------------------------

export interface RevokeSessionResponse {
  readonly message: string;
  readonly wasCurrentSession: boolean;
}

// ---- RevokeOtherSessions (Features/RevokeOtherSessions/Response.cs) ------------------------

export interface RevokeOtherSessionsResponse {
  readonly message: string;
  readonly revokedCount: number;
}

// ---- RevokeAllSessions (Features/RevokeAllSessions/Response.cs) ----------------------------

export interface RevokeAllSessionsResponse {
  readonly message: string;
  readonly revokedCount: number;
}
