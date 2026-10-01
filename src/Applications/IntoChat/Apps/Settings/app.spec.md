# Settings

Workspace preferences live in durable fields the person edits in place: a display name and a theme.
Opening the app composes a settings surface; reading answers the preferences as JSON. A theme other
than "light" or "dark" answers as "system".

## Scenario: Opening settings composes the surface and answers the defaults

Invoking "open" on a fresh install answers a JSON payload naming the settings surface, whose title
is "Settings", with an empty display name and the "system" theme.

## Scenario: Typed values are the preferences

After setting the display name field to "Ada" and the theme field to "dark", invoking "read"
answers those preferences.

## Scenario: Preferences survive reopening

Invoking "open" again after values were set answers the same preferences, and the fields still
hold them.
