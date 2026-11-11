namespace DigitalBrain.Limits;

/// <summary>A policy rule was proposed to a person for confirmation.</summary>
/// <param name="RuleId">The proposed rule id.</param>
/// <param name="PlanId">The confirmation plan id.</param>
/// <param name="Text">The rendered sentence put to the person.</param>
[Signal("policy.proposed")]
public sealed record PolicyProposed(string RuleId, string PlanId, string Text) : Signal;

/// <summary>A proposed policy rule was approved by a person and adopted.</summary>
/// <param name="RuleId">The adopted rule id.</param>
/// <param name="Action">The action constrained by the rule.</param>
/// <param name="Text">The rendered sentence of the rule.</param>
[Signal("policy.adopted")]
public sealed record PolicyAdopted(string RuleId, string Action, string Text) : Signal;

/// <summary>An action was refused by an adopted policy rule.</summary>
/// <param name="Action">The refused action.</param>
/// <param name="Amount">The requested amount.</param>
/// <param name="RuleId">The rule that refused the action.</param>
[Signal("policy.refused")]
public sealed record PolicyRefused(string Action, decimal Amount, string RuleId) : Signal;

/// <summary>An adopted policy rule was retired.</summary>
/// <param name="RuleId">The retired rule id.</param>
[Signal("policy.retired")]
public sealed record PolicyRetired(string RuleId) : Signal;
