# SDD Spec — Device-Bound Sessions

Replaces stateless refresh JWTs with a persisted, per-device `UserSession`. Access,
Invitation, and PasswordReset token kinds are unaffected.

## Requirements

### R1 — Opaque, hashed refresh tokens
- The system MUST generate `Refresh`-kind tokens as 256-bit cryptographically random
  values (`RandomNumberGenerator`), base64url-encoded — never a JWT.
- The system MUST persist only the SHA-256 hex hash of the token; the raw value MUST NOT
  be stored or logged anywhere.
- `TokenPairResponse(AccessToken, RefreshToken)` wire shape MUST remain unchanged.

### R2 — Persisted per-device session (upsert)
- `LoginAsync` and `SignupAsync` MUST create exactly one active `UserSession` per
  `(UserId, DeviceId)`.
- If an active session already exists for that `(UserId, DeviceId)`, it MUST be revoked
  before the new row is inserted.

### R3 — Single-use rotation
- Each successful `/auth/refresh` MUST mint a new opaque token, move the presented
  `CurrentTokenHash` into `PreviousTokenHash`, and set the new hash as `CurrentTokenHash`.
- `LastUsedAt` MUST be updated on every successful refresh.

### R4 — Grace window
- A token matching `PreviousTokenHash` MUST be accepted as a valid refresh if
  `now <= PreviousTokenExpiresAt` (config `RefreshGraceWindowSeconds`, default 30s),
  treated identically to a normal rotation.
- After the grace deadline, `PreviousTokenHash` MUST NOT be accepted.

### R5 — Reuse detection and device mismatch
- A presented token matching neither `CurrentTokenHash` nor an in-window
  `PreviousTokenHash` MUST cause the entire session to be revoked
  (`RevokedReason=ReuseDetected`) and return 401.
- A presented `deviceId` not equal to `session.DeviceId` MUST be treated identically
  (revoke + 401).
- Revocation MUST invalidate both `Current` and `Previous` tokens immediately.

### R6 — Sliding / absolute TTL
- `ExpiresAt` MUST be recomputed on every successful refresh as
  `min(now + RefreshSlidingExpiryDays, AbsoluteExpiresAt)`.
- `AbsoluteExpiresAt` MUST be set once at creation (`CreatedDate + RefreshAbsoluteExpiryDays`)
  and MUST NOT be extended by any refresh.
- A refresh presented when `now >= ExpiresAt` MUST be rejected with 401.

### R7 — Self-service revocation endpoints
- `POST /auth/logout`, `GET /auth/sessions`, `DELETE /auth/sessions/{id}`, and
  `POST /auth/sessions/revoke-all` MUST require authentication and be scoped to the
  caller's own sessions only.
- A foreign or nonexistent session `id` MUST return 404 Not Found, never 403 Forbidden.

### R8 — Password reset bulk revocation
- `ResetPasswordAsync` MUST immediately revoke all of the user's active `UserSession`
  rows (`RevokedReason=PasswordReset`), in addition to the unchanged `prv` bump for
  `TokenKind.PasswordReset`.

### R9 — Session list DTO safety
- `GET /auth/sessions` responses MUST expose only `Id, DeviceId, DeviceName, Platform,
  LastUsedAt, CreatedDate`.
- No response DTO MUST ever include `CurrentTokenHash`, `PreviousTokenHash`, or any raw
  token value.

### R10 — Request DTO additions
- Login, refresh, and signup request DTOs MUST include `deviceId` (required),
  `deviceName` (optional), and `platform` (required `DevicePlatform` enum).
- A request missing `deviceId` MUST fail validation with 400.

## Scenarios

```
SCENARIO: login creates a device session (R2, R10)
  GIVEN a valid login request with deviceId "device-A", platform "iOS"
  WHEN POST /auth/login succeeds
  THEN a UserSession exists for (UserId, DeviceId="device-A") with CurrentTokenHash set
  AND TokenPairResponse is returned unchanged in shape

SCENARIO: refresh rotates the token (R1, R3)
  GIVEN an active session with CurrentTokenHash = hash(T1)
  WHEN POST /auth/refresh { refreshToken: T1, deviceId: "device-A" }
  THEN 200 is returned with new token T2, T2 != T1
  AND session.PreviousTokenHash = hash(T1), session.CurrentTokenHash = hash(T2)

SCENARIO: reuse after grace window revokes the session (R5)
  GIVEN T1 was rotated to T2 more than RefreshGraceWindowSeconds ago
  WHEN POST /auth/refresh { refreshToken: T1, deviceId: "device-A" }
  THEN 401 is returned and the session is revoked (RevokedReason=ReuseDetected)
  AND a subsequent refresh with T2 also returns 401

SCENARIO: grace-window retry is accepted (R4)
  GIVEN T1 was rotated to T2 less than RefreshGraceWindowSeconds ago
  WHEN POST /auth/refresh { refreshToken: T1, deviceId: "device-A" }
  THEN 200 is returned with a new valid token pair

SCENARIO: grace-window boundary rejects late retry (R4)
  GIVEN T1 was rotated to T2 one second past the grace deadline
  WHEN POST /auth/refresh { refreshToken: T1, deviceId: "device-A" }
  THEN 401 is returned

SCENARIO: deviceId mismatch revokes the session (R5)
  GIVEN an active session for deviceId "device-A" with CurrentTokenHash = hash(T1)
  WHEN POST /auth/refresh { refreshToken: T1, deviceId: "device-B" }
  THEN 401 is returned and the session is revoked

SCENARIO: logout revokes only the caller's device (R7)
  GIVEN the caller has active sessions for "device-A" and "device-B"
  WHEN POST /auth/logout { deviceId: "device-A" }
  THEN 204 is returned; device-A's session is revoked, device-B's is unaffected

SCENARIO: revoke-all revokes every device (R7)
  GIVEN the caller has 2 active sessions
  WHEN POST /auth/sessions/revoke-all
  THEN 204 is returned and both sessions are revoked; both fail on next refresh

SCENARIO: DELETE /auth/sessions/{id} revokes only that device (R7)
  GIVEN the caller has session idA (device-A) and idB (device-B)
  WHEN DELETE /auth/sessions/{idA}
  THEN 204 is returned; device-A refresh now fails, device-B refresh still succeeds

SCENARIO: GET /auth/sessions is self-scoped and hash-free (R7, R9)
  GIVEN two users each have active sessions
  WHEN caller A calls GET /auth/sessions
  THEN 200 is returned with only caller A's sessions
  AND no CurrentTokenHash/PreviousTokenHash field is present in the response

SCENARIO: password reset kills all sessions (R8)
  GIVEN the caller has active sessions on 2 devices
  WHEN POST /auth/password/reset succeeds
  THEN all of the user's UserSession rows are revoked (RevokedReason=PasswordReset)
  AND a subsequent refresh from either device returns 401

SCENARIO: sliding TTL extends on use (R6)
  GIVEN a session with ExpiresAt = now + 60d
  WHEN a successful refresh occurs 30 days later
  THEN ExpiresAt is recomputed to min(now + 60d, AbsoluteExpiresAt)

SCENARIO: absolute cap is not extendable (R6)
  GIVEN a session created 179 days ago (AbsoluteExpiresAt = CreatedDate + 180d)
  WHEN a successful refresh occurs
  THEN ExpiresAt does not exceed AbsoluteExpiresAt
  AND once now >= AbsoluteExpiresAt, refresh returns 401

SCENARIO: access token or garbage rejected by refresh (R1, R5)
  GIVEN a valid access token (or any arbitrary string) matching no UserSession hash
  WHEN POST /auth/refresh presents it as refreshToken
  THEN 401 is returned
```

## API surface

```
POST   /auth/login                → 200 TokenPairResponse
POST   /auth/signup               → 201 TokenPairResponse
POST   /auth/refresh              → 200 TokenPairResponse | 401
POST   /auth/logout               → 204
GET    /auth/sessions             → 200 UserSessionResponse[]
DELETE /auth/sessions/{id}        → 204 | 404
POST   /auth/sessions/revoke-all  → 204
POST   /auth/password/reset       → 200 (unchanged shape; now also bulk-revokes sessions)
```

## DTOs

```csharp
record LoginRequest(
    string Email, string Password,
    string DeviceId, string? DeviceName, DevicePlatform Platform);

record SignupRequest(
    /* existing fields... */
    string DeviceId, string? DeviceName, DevicePlatform Platform);

record RefreshRequest(string RefreshToken, string DeviceId);

record LogoutRequest(string DeviceId);

record TokenPairResponse(string AccessToken, string RefreshToken); // unchanged

record UserSessionResponse(
    Guid Id, string DeviceId, string? DeviceName,
    DevicePlatform Platform, DateTime LastUsedAt, DateTime CreatedDate);

enum DevicePlatform { Android, iOS, Web }

enum RevokedReason { UserLogout, UserRevoked, ReuseDetected, PasswordReset, ExpiredAbsolute }
```

## Validation rules

| Field | Rule |
|---|---|
| `deviceId` | NotEmpty (login, signup, refresh, logout) |
| `deviceName` | Optional, MaxLength 200 |
| `platform` | Must be a valid `DevicePlatform` value |
| `refreshToken` | NotEmpty; opaque string, no structural validation |
| `id` (`DELETE /auth/sessions/{id}`) | Must be a valid `Guid` route parameter |
