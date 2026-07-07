# Google OAuth Setup for DigitalBrain

To use Gmail integration, you need to create OAuth 2.0 credentials in Google Cloud Console.

1. Go to https://console.cloud.google.com/apis/credentials/create (use the link in the credential form if shown).

2. Create a new project if needed.

3. Enable the Gmail API.

4. Create OAuth client ID (Web application type).

5. Add authorized redirect URI: http://localhost:51014/google-callback (or your production one).

6. Note the Client ID and Client Secret.

7. In DigitalBrain, when the form appears, enter them. The system will store per-user and use merged scopes.

Scopes used: https://www.googleapis.com/auth/gmail.readonly (readonly access to Gmail).

The flow uses offline access and consent prompt to get refresh token.

For production, configure the redirect in Aspire or config under DigitalBrain:Google:RedirectUri.

See also the gap analysis for context.