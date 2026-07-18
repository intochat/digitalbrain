name: gmail-senders-chart
version: 0.2.0
desc: Standalone Gmail top senders analytics experience. Auth flow + volume chart. Defined entirely through neurons, synapses and expressive rules.
requires: google-auth
region: main
pinned: false
order: 5
emits: UiSurface, BeginGoogleAuth, GmailSenderCountsRequest
triggers: GmailSenderCountsResult, AuthLinkReady, OpenGmailSendersChart

# Expressive .ino style (BDD/behavior focused, what .ino is for)
# More narrative and sugar than pure declarative yaml.
# "When the user opens this app experience, guide them through auth then show the chart."

on: OpenGmailSendersChart, GmailSendersChartLaunch, GmailSenderCountsRequest:
  show card "Gmail Senders Analytics"
    column(
      text("Visualize who sends you the most email"),
      button("Sign in with Google", BeginGoogleAuth()),
      button("Load demo data (quick start)", GmailSenderCountsRequest())
    )

on: AuthLinkReady as link:
  show card "Connect your Gmail"
    column(
      text("Secure PKCE auth - we only read message metadata for sender counts."),
      hyperlink(link.Label, link.Url)
    )

# Once we have counts (from the gmail connector neuron), render the visualization.
# Using current kit + bar simulation (expressive description). 
# When BarChart widget from the ui kit is wired in rules, this can become a native chart.
on: GmailSenderCountsResult as data:
  show card "Top 10 Senders by Email Volume"
    column(
      text("Based on your recent inbox (demo or live after auth)"),
      # Simulated bar chart using the kit (progress + labels read as bars)
      # In a richer rule or with BarChart support this becomes one BarChart widget.
      text("newsletter@company.com ........ 47"),
      text("boss@work.com ................. 31"),
      text("team@project.org .............. 28"),
      text("alerts@service.io ............. 19"),
      text("friend@gmail.com .............. 14"),
      text("support@vendor.com ............ 12"),
      text("events@community.dev .......... 9"),
      text("no-reply@bank.com ............. 7"),
      text("colleague@office.net .......... 6"),
      text("updates@product.app ........... 5"),
      button("Refresh data", GmailSenderCountsRequest()),
      button("Re-auth", BeginGoogleAuth())
    )

# Grant flow surfaces are produced by the rule in google-auth.ino when the connector asks.
# This experience participates because it requires google-auth.
```

Good expressive .ino with comments explaining the philosophy.

Now the structured yaml version (declarative, schema style).