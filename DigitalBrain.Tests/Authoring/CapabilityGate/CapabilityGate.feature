Feature: CapabilityGate accept/reject rules
  As the platform
  I want the pack sandbox's own rules expressed as specs
  So that security invariants are provable the same way pack behavior is

@packspec @security
Scenario: A pack using System.Net.Http.HttpClient is rejected
  Given a pack source that calls "System.Net.Http.HttpClient"
  When the pack is compiled
  Then compilation is rejected with violation "System.Net."

@packspec @security
Scenario: A pack using only collections and LINQ is accepted
  Given a pack source that only uses collections and LINQ
  When the pack is compiled
  Then compilation is accepted
