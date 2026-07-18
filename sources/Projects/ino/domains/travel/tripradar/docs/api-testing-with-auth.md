# API Testing with Authentication

## Quick Start: Generate a Dev Token

### Via Aspire Dashboard (fastest)

1. Open the Aspire dashboard
2. Find the **api** resource
3. Click the **Generate Dev Token** command button (key icon)
4. The response contains a JWT in the `Set-Cookie: accessToken=...` header
5. Copy the token value (everything between `accessToken=` and the first `;`)

### Via curl

```bash
# Get a dev token (development only)
curl -s -X POST http://localhost:5330/api/v1.0/tokens/dev \
  -H "Content-Type: application/json" \
  -d '{"telegramUserId": 12345}'

# Extract and use in one step:
TOKEN=$(curl -s -X POST http://localhost:5330/api/v1.0/tokens/dev \
  -H "Content-Type: application/json" \
  -d '{"telegramUserId": 12345}' \
  | python -c "import json,sys; print(json.load(sys.stdin)['token'])")

# Test with the token:
curl http://localhost:5330/api/v1.0/users/profile \
  -H "Authorization: Bearer $TOKEN"
```

## Authentication Schemes

The API uses two authentication methods:

| Scheme | Header | Usage |
|--------|--------|-------|
| **Bearer JWT** | `Authorization: Bearer <token>` | Primary auth for user-specific endpoints |
| **X-API-Key** | `X-API-Key: <key>` | Alternative auth for API consumers |

When a Bearer token is present, the API key check is skipped. Most endpoints accept either method.

## Scalar (built-in API docs)

**URL:** `http://localhost:5330/scalar/v1`

Scalar is the built-in API reference with a test client. It replaces Swagger UI.

### Setup

1. Open `http://localhost:5330/scalar/v1`
2. Click any endpoint, then **Test Request**
3. In the **Authentication** section, switch between **Bearer** and **X-API-Key** tabs
4. Paste your JWT token in the Bearer Token field
5. Send requests — the token persists across all endpoints in the session

### Auth flow

1. Navigate to **Token** > `POST /api/v1.0/tokens/dev`
2. Click **Test Request** and **Send** (body is pre-filled with `{"telegramUserId": 123456}`)
3. The response body contains `{"token": "eyJ...", "refreshToken": "..."}`
4. Copy the `token` value
5. Navigate to any protected endpoint
6. Click **Test Request**, paste the token in the **Bearer Token** field
7. All subsequent requests in the session use this token

### Notes

- API paths are resolved to `/api/v1.0/...` — no manual version input needed
- Server is set to `http://localhost:5330`
- Bearer and X-API-Key are both available as preferred auth schemes

## Search Endpoints

Authenticated search endpoints now live under a shared `/api/v1.0/search` namespace:

- `GET /api/v1.0/search/airports?query=bar&limit=10`
- `GET /api/v1.0/search/locations?query=bar&limit=10`

Use them for autocomplete and picker scenarios in the website, Mini App, and bot flows.

### Quick curl examples

```bash
curl "http://localhost:5330/api/v1.0/search/airports?query=bar&limit=5" \
  -H "Authorization: Bearer $TOKEN"

curl "http://localhost:5330/api/v1.0/search/locations?query=bar&limit=5" \
  -H "Authorization: Bearer $TOKEN"
```

`airports` returns airport suggestions. `locations` returns compact location suggestions with `locationId`, `name`, `canonicalName`, `countryCode`, `targetType`, and coordinates when available.

## Postman

### Setup

1. Import the OpenAPI spec: `http://localhost:5330/openapi/v1.json`
2. Create an **Environment** with variables:
   - `baseUrl`: `http://localhost:5330`
   - `token`: (leave empty, will be auto-filled)
   - `apiKey`: your API key value

### Auth flow

1. Create a **Collection-level** auth setting:
   - Type: **Bearer Token**
   - Token: `{{token}}`
2. To get a token, send `POST {{baseUrl}}/api/v1.0/tokens/dev` with body `{"telegramUserId": 12345}`
3. In the **Tests** tab of that request, add a script to auto-extract the token:
   ```javascript
   const body = pm.response.json();
   if (body.token) pm.environment.set("token", body.token);
   ```
4. All subsequent requests in the collection inherit the Bearer token automatically

### Alternative: API key auth

Set collection auth to **API Key**:
- Key: `X-API-Key`
- Value: `{{apiKey}}`
- Add to: **Header**

## Insomnia

### Setup

1. Import the OpenAPI spec: `http://localhost:5330/openapi/v1.json`
2. Create a **Base Environment** with:
   ```json
   {
     "baseUrl": "http://localhost:5330",
     "token": ""
   }
   ```

### Auth flow

1. Set **Folder-level** auth to **Bearer Token** with value `{{ _.token }}`
2. Create a request `POST {{ _.baseUrl }}/api/v1.0/tokens/dev` with body `{"telegramUserId": 12345}`
3. Send the request and copy the `token` value from the JSON response
4. Update the `token` environment variable with the JWT
5. All requests in the folder inherit the Bearer auth

### Alternative: API key auth

Set folder auth to **API Key**:
- Header name: `X-API-Key`
- Value: your API key

### Response extraction

Insomnia supports **Response > Body** chaining:
1. In any request's auth, use **Bearer Token**
2. Set the token to: **Response > Body** from the dev token request
3. Use JSONPath filter: `$.token`

## Token Details

The dev token endpoint (`POST /api/v1.0/tokens/dev`) is:
- **Development only** — returns 404 in production
- **No auth required** — the `[AllowAnonymous]` attribute skips all auth checks
- Creates a dev user with `telegramUserId` if one doesn't exist
- Returns the JWT directly in the response body: `{"token": "...", "refreshToken": "..."}`
- Token expires after 1000 minutes (~16 hours)
- User gets the "Basic" tier by default
