@cluster
Feature: Driver probe pack
  As a pack author
  I want a minimal pack proven by the shared Reqnroll vocabulary
  So that the vocabulary itself is validated end to end

@packspec
Scenario: A minimal pack echoes its input
  Given a pack "DriverProbePack" version "1.0" with source from "DriverProbePack.cs"
  And pack "DriverProbePack" is installed
  When I fire synapse "ExperienceUsed" at pack "DriverProbePack" with pack "DriverProbePack" action "probe"
  Then pack "DriverProbePack" emits "driver-echo:probe"
