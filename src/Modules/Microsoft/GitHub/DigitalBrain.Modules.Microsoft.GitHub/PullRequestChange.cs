namespace DigitalBrain.Microsoft.GitHub;

[Flags]
internal enum PullRequestChange { Opened = 1, Updated = 2, Closed = 4, Checks = 8 }
