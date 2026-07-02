Feature: Cross-replica broadcast
  As a pack author
  I want to prove a broadcast reaches subscribers regardless of which silo hosts them
  So that cluster/HA behavior is a reusable, provable spec capability, not a manual aspire-run check

@packspec @cluster
Scenario: A broadcast synapse reaches a subscriber on another silo
  Given the cluster has 3 replicas
  And a pack "DriverProbePack" version "1.0" with source from "DriverProbePack.cs"
  And pack "DriverProbePack" is installed
  When a broadcast synapse "DemoMessageSynapse" with text "cross-silo-probe" is fired
  Then pack "DriverProbePack" observes the broadcast on another silo
