Feature: Broadcast versus named-instance pub/sub
  Broadcast fires only along synapses on the source. IHandle<T> is the capability
  to receive T; it does not subscribe every instance of a type. SubscribeTo writes
  a Bound synapse (does not decay). A handled directed Send writes a Learned synapse.

  Broadcast receiver set:
    1. Synapses on the source for that signal type
    2. Never the emitter itself
    3. Never "all default IHandle types" in the silo

  Named accounts do not share an audience. Account "elon" and account "vlad"
  are two instances. Subscribing to elon means a NewPost synapse stored ON elon.

  Rule: Who may broadcast

    Scenario: Any neuron may broadcast; a signal with no IHandle and no synapses reaches nobody
      Given a running brain
      When account "elon" broadcasts Secret "lost in space"
      Then the broadcast reaches 0 receivers
      And account "elon" has no synapses

    Scenario: Broadcast without a synapse reaches nobody even when a type IHandle<T>
      Given a running brain
      And timeline "default" can handle NewPost
      When account "elon" broadcasts NewPost "to all defaults"
      Then the broadcast reaches 0 receivers
      And timeline "default" incoming journal does not contain NewPost "to all defaults"

    Scenario: Subscribe then broadcast reaches only that instance
      Given a running brain
      And timeline "alice" can handle NewPost
      And timeline "alice" subscribes to account "elon" for NewPost
      When account "elon" broadcasts NewPost "starship"
      Then the broadcast reaches 1 receivers
      And timeline "alice" incoming journal contains NewPost "starship"
      And account "elon" has a bound NewPost synapse to timeline "alice"

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
