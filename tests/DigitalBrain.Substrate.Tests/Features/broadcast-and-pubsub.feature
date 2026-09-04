Feature: Broadcast versus named-instance pub/sub
  IAW had three channels (IReceiver, streams, observers). DigitalBrain collapses
  neuron-to-neuron traffic to two verbs on the membrane: Send (directed fire) and
  Broadcast (fan-out from THIS neuron). Orleans streams, BroadcastChannel, and
  Azure queues are hosting adapters, not script vocabulary.

  Broadcast receiver set (today):
    1. Learned synapses on the source for that signal type (instance pub/sub)
    2. The "default" instance of every grain type that declares IHandle<T>
       (innate, type-level — NOT every named instance)
    3. Never the emitter itself

  Named accounts therefore do not share an audience. Account "elon" and account
  "vlad" are two instances of the same neuron type. Subscribing to elon means a
  NewPost synapse stored ON elon, targeting the subscriber. Vlad's posts follow
  vlad's synapses. IHandle<NewPost> on Timeline is the capability to receive;
  it does not subscribe alice to every account.

  Azure Service Bus / Orleans persistent streams are for offline or huge fan-out
  that must not live in elon's synapse dictionary. They are not how alice
  subscribes to elon.

  Rule: Who may broadcast

    Scenario: Any neuron may broadcast; a signal with no IHandle and no synapses reaches nobody
      Given a running brain
      When account "elon" broadcasts Secret "lost in space"
      Then the broadcast reaches 0 receivers
      And account "elon" has no synapses

    Scenario: Broadcast reaches every grain type that declares IHandle, at the default instance
      Given a running brain
      And timeline "default" can handle NewPost
      When account "elon" broadcasts NewPost "to all defaults"
      Then the broadcast reaches at least 1 receiver
      And timeline "default" incoming journal contains NewPost "to all defaults"

    Scenario: The emitter is never a receiver of its own broadcast
      Given a running brain
      And timeline "default" can handle NewPost
      When account "elon" broadcasts NewPost "no echo"
      Then account "elon" has no NewPost synapse to itself

  Rule: Subscribe to elon is not subscribe to vlad

    Scenario: Alice follows elon; bob follows vlad; broadcasts do not cross
      Given a running brain
      And timeline "alice" can handle NewPost
      And timeline "bob" can handle NewPost
      And account "elon" has introduced NewPost to timeline "alice"
      And account "vlad" has introduced NewPost to timeline "bob"
      When account "elon" broadcasts NewPost "starship"
      And account "vlad" broadcasts NewPost "ukraine"
      Then timeline "alice" incoming journal contains NewPost "starship"
      And timeline "alice" incoming journal does not contain NewPost "ukraine"
      And timeline "bob" incoming journal contains NewPost "ukraine"
      And timeline "bob" incoming journal does not contain NewPost "starship"
      And account "elon" has a learned NewPost synapse to timeline "alice"
      And account "elon" has no NewPost synapse to timeline "bob"

    Scenario: A later broadcast follows the learned synapse without a new directed fire
      Given a running brain
      And timeline "alice" can handle NewPost
      And account "elon" has introduced NewPost to timeline "alice"
      When account "elon" broadcasts NewPost "second post"
      Then timeline "alice" incoming journal contains NewPost "second post"
      And the NewPost synapse from account "elon" to timeline "alice" has fire count 2
