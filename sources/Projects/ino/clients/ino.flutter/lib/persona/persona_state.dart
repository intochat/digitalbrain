enum PersonaEmotion {
  sleeping,
  waking,
  idle,
  listening,
  thinking,
  acting,
  responding,
  celebrating,
  confused,
  evolving,
  searching,
  presenting,
}

class PersonaStateModel {
  const PersonaStateModel({
    this.emotion = PersonaEmotion.idle,
    this.energy = 0.5,
    this.confidence = 1.0,
    this.neuronCount = 0,
    this.synapseRate = 0.0,
    this.domainAffinity = const {},
    this.personaName = 'ino',
    this.personaSlug = 'ino',
    this.traits = const {},
    this.riveAssetUrl,
    this.signalPulse = 0.0,
    this.activeSkillCount = 0,
    this.currentAction,
  });

  final PersonaEmotion emotion;
  final double energy;
  final double confidence;
  final int neuronCount;
  final double synapseRate;
  final Map<String, double> domainAffinity;
  final String personaName;
  final String personaSlug;
  final Map<String, String> traits;
  final String? riveAssetUrl;
  final double signalPulse; // 0.0-1.0, spikes on synapse fire, decays over time
  final int activeSkillCount; // skills currently doing something
  final String? currentAction; // e.g. "Calling Uber API...", null = idle

  PersonaStateModel copyWith({
    PersonaEmotion? emotion,
    double? energy,
    double? confidence,
    int? neuronCount,
    double? synapseRate,
    Map<String, double>? domainAffinity,
    String? personaName,
    String? personaSlug,
    Map<String, String>? traits,
    String? riveAssetUrl,
    double? signalPulse,
    int? activeSkillCount,
    String? currentAction,
    bool clearCurrentAction = false,
  }) {
    return PersonaStateModel(
      emotion: emotion ?? this.emotion,
      energy: energy ?? this.energy,
      confidence: confidence ?? this.confidence,
      neuronCount: neuronCount ?? this.neuronCount,
      synapseRate: synapseRate ?? this.synapseRate,
      domainAffinity: domainAffinity ?? this.domainAffinity,
      personaName: personaName ?? this.personaName,
      personaSlug: personaSlug ?? this.personaSlug,
      traits: traits ?? this.traits,
      riveAssetUrl: riveAssetUrl ?? this.riveAssetUrl,
      signalPulse: signalPulse ?? this.signalPulse,
      activeSkillCount: activeSkillCount ?? this.activeSkillCount,
      currentAction: clearCurrentAction ? null : (currentAction ?? this.currentAction),
    );
  }
}
