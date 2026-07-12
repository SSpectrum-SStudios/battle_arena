# Battle Arena — Core Architecture Decisions

Status: Discussion / living document  
Last updated: July 11, 2026

## Purpose

This document records architectural decisions for `BattleArena.Core` before implementation begins. Each topic moves through three states:

- **Proposal**: a recommendation being discussed.
- **Confirmed**: accepted as the current design direction.
- **Revisit**: intentionally deferred or likely to change after testing.

Patterns and interfaces should be introduced only when they protect a real variation, invariant, or dependency boundary required by the game.

## Guiding Constraints

- The core domain must not depend on Godot.
- Game rules should be testable without scenes, nodes, timers, or multiplayer peers.
- The authoritative host must be able to resolve the same commands deterministically.
- JSON definitions need stable identities across machines and game versions.
- Equipped copies, timed effects, and world objects may need independent runtime identity.
- Values used in resolution should favor immutable snapshots over shared mutable state.
- The architecture must support item-owned rules and cross-item modification without one class knowing every concrete item.

## Decision 1: Entities and Value Objects

Status: Confirmed

### The Distinction

An **entity** is defined by continuity and identity. Two entities can contain identical data and still be different because the game must track their individual lifetimes.

A **value object** is defined entirely by its contents. Two values with the same contents are interchangeable. Value objects should normally be immutable.

The practical test is:

> If two instances contain identical data, may the game freely substitute one for the other?

If yes, use a value object. If no, the object needs identity.

Identity does not imply that a type must inherit from a universal `Entity` base class. Strongly typed identifiers and composition are preferred unless a shared base class eventually provides proven value.

### Proposed Identity Types

Use small strongly typed IDs instead of passing raw integers and strings throughout the domain. Conceptually:

```csharp
public readonly record struct CombatantId(long Value);
public readonly record struct ItemDefinitionId(string Value);
public readonly record struct ItemInstanceId(long Value);
public readonly record struct ActiveEffectId(long Value);
public readonly record struct SelectionOfferingId(long Value);
```

These prevent accidental comparisons such as using an item ID where a combatant ID is expected. The exact serialized representation and ID generator will be discussed separately.

Runtime IDs are compact match-scoped numeric IDs assigned by the authoritative match. Content-definition IDs are stable namespaced strings loaded from validated JSON and remain consistent across machines, such as `base:sword_of_greater_poison`.

These two identity domains remain deliberately separate:

- Runtime numeric IDs identify one combatant, equipped copy, active effect, offering, or world instance inside a particular match.
- Stable string IDs identify reusable authored definitions across matches, machines, saved content, and item-creator exports.

Clients never invent authoritative runtime IDs. They receive them in replicated commands or state. Numeric IDs may be reused only after the owning match has ended; avoiding reuse during a match prevents late messages from addressing a different entity.

### Proposed Entities

#### Combatant

`Combatant` is an entity identified by `CombatantId` for the duration of a match.

It owns or coordinates mutable match state such as:

- Current and maximum health.
- Lives and elimination state.
- Equipment.
- Derived statistics.
- Active effects attached to that combatant.
- Item activation state, shared cooldowns, and charges.

Two players with identical health, equipment, and effects are still different combatants.

Recommendation: use a match-scoped `CombatantId`, not a Steam ID, as the domain identity. A Godot/network adapter can map a Steam peer or connection to the match-scoped ID.

#### Equipped Item Instance

Each equipped copy is an entity identified by `ItemInstanceId`.

Two copies of the same item definition must remain distinguishable because they may:

- Occupy different flexible slots.
- Own separate modifiers or active abilities.
- Acquire different upgrades or forge results.
- Be removed or replaced independently.
- Track shared item cooldowns, charges, or other runtime state.

Both instances refer to the same immutable `ItemDefinition`, but they are not the same equipped item.

An equipped instance may accumulate irreversible match-local evolution, such as combination count, strengthened positive and negative effects, or a forge result. This state must never be written back into the shared catalog definition.

#### Active Effect Instance

Every application of a timed or persistent effect is an entity identified by `ActiveEffectId`.

This directly reflects the confirmed poison rule: two poison applications do not know about one another. Each has its own source, target, schedule, remaining lifetime, and runtime state.

#### Match

A match has identity and mutable lifecycle state: lobby configuration, participating combatants, round victories, current phase, and deterministic random state.

Recommendation: treat the match as an entity coordinated by application services. Avoid making it one enormous object responsible for every combat calculation and network concern.

#### Round

A round has a match-local identity or sequence number and state including lives, eliminations, selection order, and winner.

Whether `Round` deserves a standalone entity or is state owned entirely by the match coordinator can remain an implementation-level decision until round flow is built.

#### Selection Session

One player's sequence of offerings and picks may be a stateful entity. It must track:

- Which player is selecting.
- The current offering.
- Picks already consumed.
- Current extra-pick entitlements.
- Timer state when enabled.
- Public selection history.

This identity matters for multiplayer commands: a client must not submit a choice for an expired or previous offering.

#### Persistent World Effect Instance

A mine, summon, projectile, or other persistent world object needs runtime identity even if its behavior is driven by an immutable definition. The core owns its gameplay identity and state; Godot owns the corresponding scene node and presentation.

### Proposed Definition Objects

Definitions occupy a useful middle ground: they are immutable data, but stable content identity matters.

#### Item Definition

An `ItemDefinition` is immutable after validation and is addressed by `ItemDefinitionId`, such as `base:sword_of_greater_poison`.

Two definitions with identical fields but different IDs should not automatically be treated as the same catalog entry. For that reason, definition identity matters even though definitions otherwise behave like immutable values.

Recommendation: model definitions as immutable catalog objects with stable IDs. Keep them separate from stateful equipped instances.

#### Effect and Modifier Definitions

Effect and modifier definitions are normally immutable descriptions nested inside an item definition. Their equality can usually be value-based.

Some effects will need a stable local address so another modifier can target them, descriptions can reference them, or validation errors can identify them. That address does not necessarily make them independent runtime entities.

Recommendation: begin with immutable value semantics plus optional stable definition keys. Promote a definition to a separately identified catalog object only when cross-definition reuse or references require it.

### Proposed Value Objects

#### Damage Portion

An immutable amount paired with a damage type. Examples:

- 20 Physical damage.
- 8 Fire damage.

Two equal damage portions are interchangeable.

#### Damage Packet

An immutable snapshot containing one or more damage portions plus source and classification metadata. It represents the attack or effect being resolved at a particular moment.

A packet is not the attacking player and does not own ongoing state. If it changes during a resolution pipeline, each phase should produce a new value or a deliberately scoped mutable builder that produces an immutable result.

#### Damage Type

Initially an enum. If damage types later need data-driven behavior or mod-defined types, this may become a validated identifier instead.

#### Source Reference

An immutable value identifying the combatant, equipped item instance, active effect, or world instance responsible for an outcome. It supports attribution without retaining Godot node references.

#### Resistance and Modifier Values

Flat resistance, percentage resistance, duration multipliers, tick-rate changes, and similar calculations should be immutable values or immutable modifier specifications.

The installed modifier contribution may have an ownership handle, but the mathematical rule it applies can remain a value.

#### Health Change and Resolved Outcome

Results such as damage dealt, health gained, over-resistance healing, and triggered-event data should be immutable event snapshots. Historical results must not change when later stats or equipment change.

#### Slot Definition

Fixed slot keys such as Weapon, Helmet, and Chest Armor are values. A particular occupied slot belongs to a combatant's equipment state; the slot type itself has no runtime identity.

#### Lobby and Match Rules

Validated configuration such as lives per round, victory target, flexible-slot count, and optional timer duration should be an immutable value snapshot used to start a match.

### State That Should Not Be Mistaken for Definitions

The following pairs must remain separate:

| Immutable definition/value | Stateful runtime entity/state |
|---|---|
| `ItemDefinition` | `EquippedItemInstance` |
| `EffectDefinition` | `ActiveEffectInstance` |
| `WorldEffectDefinition` | Mine, summon, or projectile instance |
| `AbilityDefinition` | Item-level cooldown, charge, and activation state |
| `MatchRules` | Current match and round state |
| `DamagePacket` | Combatant health and active effects after resolution |

This separation prevents loaded JSON definitions from being mutated when one player attacks, upgrades an item, or starts a cooldown.

### Equipment Mutation Boundary

Equipment can be added, replaced, combined, or forged only during the between-round selection phase. It cannot change while arena combat is active.

This invariant removes an entire category of ambiguous combat behavior. A weapon cannot disappear while one of its poison effects is ticking because item replacement and active combat do not overlap. Round-phase rules—not defensive checks scattered through effects—should enforce this boundary.

Disconnect cleanup and match termination remain exceptional lifecycle operations rather than ordinary equipment changes.

### Upgrade and Forge Representation Tradeoffs

Two viable representations were considered.

#### Option A: Evolve the Equipped Instance

The instance continues referencing its original immutable definition and stores a controlled state overlay describing its evolution.

Example overlay data:

- Combination count.
- Effect-strength multipliers.
- Added, removed, or replaced effect specifications.
- Forge ancestry or input definition IDs.
- Generated display-name and description data.

Advantages:

- Repeated combining naturally updates the same owned item.
- The global definition catalog remains small and immutable.
- Runtime identity, slot ownership, and ability bindings remain stable.
- Network messages can describe a change to an existing item instance.
- Saving a match requires the base definition ID plus the instance overlay.

Costs:

- Effective behavior and descriptions must be calculated from definition plus overlay.
- Validation must ensure an evolved instance remains internally consistent.
- Code must not accidentally read only the base definition and ignore instance evolution.
- The overlay becomes a versioned format if matches can be saved or reconnected.

#### Option B: Generate a New Definition

Combining or forging produces a new immutable generated definition, and the equipped item references that result.

Advantages:

- Runtime systems consume one fully materialized definition.
- The generated item is easy to inspect as a complete snapshot.
- No definition-plus-overlay calculation is needed during normal use.

Costs:

- Generated definitions need unique IDs, serialization, validation, and replication.
- The catalog can fill with match-specific definitions that are not reusable content.
- Provenance and repeated combinations become harder to track cleanly.
- External definitions and transient forged results need separate concepts anyway.
- Every peer must receive the complete generated definition before referring to it.

#### Confirmed Decision

Use Option A: preserve `ItemInstanceId`, retain the immutable base `ItemDefinition`, and replace the instance's evolution state with a new validated immutable snapshot whenever it is combined or forged.

This is an entity changing state, not mutation of the shared definition. Triple-combining and later irreversible transformations fit naturally. The transition should occur through a controlled method or domain service rather than public setters.

The compiled hybrid is also confirmed: after every equipment, combination, forge, or other evolution change, compile the base definition plus evolution state into a complete immutable effective-item snapshot. Combat and selection systems read that compiled snapshot, while persistence and provenance retain the original definition ID and evolution state.

Compilation happens when item state changes, not on every attack or effect tick. This avoids repeatedly rebuilding the same effective item during combat and ensures all consumers observe one validated representation.

### Damage-Over-Time Evaluation

Every damage-over-time tick creates and resolves a fresh damage packet. Poison, fire, and other periodic effects do not cache their final post-modifier damage when first applied.

Each tick therefore uses the current applicable source modifiers, target resistances, weaknesses, and triggered rules. Historical tick results remain immutable, but later ticks can differ when combat state changes.

The schedule itself—duration and next-tick timing—is separate from damage resolution, but it is also dynamically modifiable. External effects may change an active effect's remaining duration, tick interval, magnitude, or other supported properties while it is running.

For example, a chest-armor ability may create a radius that magnifies every active poison effect inside it. The individual poison instances remain independent and do not search for or communicate with one another. The radius ability queries the active-effect system for instances matching its authored spatial and tag criteria, then applies owned modifications to those instances.

Spatial discovery belongs at the Godot/application boundary because the pure domain core should not inspect scene-tree nodes or physics areas. The adapter supplies identified matching targets or spatial-query results; the domain validates and applies the authored modifications.

Target filters must be data-driven. An area modifier may affect the owner's effects, allied effects, enemy effects, or every matching effect regardless of source, depending on the item definition.

Modification lifetime is also authored:

- A scoped modification exists only while its matching condition remains true, such as while a poison stays inside a radius. Removing the influence removes the modifier and restores the values calculated without it.
- A permanent modification changes the active effect for the remainder of its lifetime, even after it leaves the radius.

Both use explicit ownership and source IDs so cleanup and attribution remain deterministic.

### Variable Item Abilities

An item definition may contain a variable number of active ability definitions. Pressing that item's activation input attempts to activate every active ability on the item; the player does not select one ability from the item.

This bundled activation is itself part of item balance. It can create a powerful combination, force several abilities to be used together, consume multiple resources, or trigger an undesirable effect alongside a beneficial one.

The equipped item instance owns one universal cooldown shared by all active abilities it contains. While that cooldown is active, none of the item's abilities can be activated. When the item activates, its entire ability bundle is invoked and the shared cooldown begins.

Contained abilities may still have authored conditions, costs, or toggled outcomes, but they do not have separate cooldown clocks under the base model. Item-level charges, if used, are likewise part of the shared activation state unless an authored mechanic explicitly introduces another resource.

A single ability may also perform several effects together, such as healing the user and emitting an area effect. Both composition levels are supported:

- One item activation invokes every active ability on that item.
- One active ability may execute multiple authored effects.

### Equality Rules

Proposed rules:

- Entities compare by their strongly typed identity.
- Value objects compare structurally by their contents.
- Definition lookups and references use stable definition IDs.
- Immutable definitions should not be used to store per-match or per-player state.
- Runtime entities should never use a Godot instance ID, scene-tree path, or node reference as their domain identity.

### Aggregate Boundary Proposal

An aggregate is a consistency boundary, not merely a folder or object hierarchy.

Initial proposal:

- `Combatant` is the primary aggregate for health, equipment, installed contributions, and attached active effects.
- `MatchState` coordinates combatants, rounds, wins, and phases but delegates calculations to focused domain services.
- Persistent world instances are managed by a match-scoped world-effect collection because they may outlive a particular combatant life.
- Catalog definitions live outside match aggregates and are immutable.

We should avoid a single object graph in which every combatant directly references every source item, target, node, and match service. IDs and immutable event data keep boundaries explicit.

### Open Questions for Decision 1

1. If one contained ability cannot satisfy an authored condition or cost, do the other abilities still execute and start the shared cooldown, or does the entire item activation fail atomically?
2. The precise numeric width and overflow policy for match-scoped runtime IDs will be chosen during implementation.

Selection offerings should receive match-scoped runtime IDs under the confirmed identity policy so the authority can reject a late choice from an expired offering.

## Decision 2: Effects, Modifications, and Active Instances

Status: Confirmed

### Design Goal

The effect system must support authored JSON content, independent runtime effects, cross-item synergy, live modification, deterministic multiplayer resolution, and useful validation errors without turning every item into a custom class.

The word "effect" should not refer simultaneously to JSON data, an activation attempt, and a ticking runtime object. Those concepts require separate types and lifecycles.

### Proposed Vocabulary

#### Effect Definition

An `EffectDefinition` is an immutable authored recipe contained in an item, ability, active effect, or other content definition.

Examples:

- Deal 20 Physical damage.
- Apply a poison for 10 seconds that ticks every 0.5 seconds.
- Heal the user for 15 health.
- Create a radius that modifies poison effects.
- Place a mine with a maximum active count of three.
- Add one card to a selection offering.

Definitions contain no current timer, target, remaining duration, scene node, or mutable combat state. They are validated when content loads and compiled as part of the effective item snapshot.

Definitions should use stable explicit `kind` identifiers in JSON rather than CLR class names. Each supported kind has a dedicated schema, validator, and runtime handler or factory.

#### Effect Execution

An `EffectExecution` is a momentary attempt to perform an effect definition in a specific context.

Its context may include:

- Source combatant and item instance.
- Intended target or targets.
- Current match and round phase.
- Triggering event.
- Authority-owned random source.
- Spatial-query results supplied by an adapter.

An execution can resolve immediately, create one or more active-effect instances, create persistent world instances, or fail validation. It is not itself a long-lived entity.

Examples:

- Immediate damage resolves and produces immutable outcomes without remaining active.
- Applying poison creates a new `ActiveEffectInstance`.
- Placing a mine creates a persistent world-effect instance.

#### Active Effect Instance

An `ActiveEffectInstance` is a match-scoped runtime entity created when behavior persists over time.

It contains state such as:

- `ActiveEffectId`.
- Source and target references.
- Definition or compiled specification.
- Tags and matching metadata.
- Start time and remaining lifetime.
- Next scheduled tick.
- Base runtime properties.
- Installed modifier contributions.
- Stack-independent local state.

Two applications of the same poison definition create two unrelated instances. Neither poison searches for, merges with, refreshes, or otherwise knows about the other unless a separate authored effect explicitly queries matching active effects.

#### Modification Definition

A `ModificationDefinition` is an immutable authored rule describing how matching values or runtime objects should change.

It must answer explicitly:

- What does it target?
- Which candidates match?
- Which property or resolution phase changes?
- Which operation is performed?
- What is the magnitude or authored formula?
- What is its lifetime?
- What is its ordering behavior?

Examples:

- Multiply damage of poison-tagged effects owned by this combatant by 1.5.
- Reduce tick interval of all poison effects in a radius by 25%.
- Add 20% Fire resistance to the wearer.
- Add one card to this player's selection offering.
- Convert 10 Physical damage into Fire damage before defenses resolve.

#### Modifier Contribution

A `ModifierContribution` is the installed runtime representation of a modification definition. It retains ownership and source information so it can be removed precisely.

Conceptually it includes:

```text
Contribution ID
Source item/effect ID
Target ID or target scope
Matching specification
Operation
Magnitude
Phase
Lifetime policy
```

Scoped modifications create contributions that are removed when their source, range, condition, item, or round lifecycle ends. Permanent modifications create contributions or state changes that remain for the target effect's lifetime.

Recommendation: even a permanent active-effect modification should retain source and history metadata. "Permanent" should mean it has no automatic removal condition, not that it becomes impossible to inspect or reproduce.

### Why Definitions and Runtime Instances Must Differ

Consider one poison definition applied twice:

```text
PoisonDefinition
    ├── ActiveEffect 501
    │     Target: Player B
    │     Next tick: 12.5
    │     Remaining: 7.0
    │
    └── ActiveEffect 544
          Target: Player B
          Next tick: 13.1
          Remaining: 9.5
```

The definition is shared immutable data. Each active instance owns its schedule and installed modifications.

If a poison-amplifying radius affects both instances, it installs distinct modifier contributions on both. Removing the radius removes only the contributions it owns.

### Effect Composition

Definitions may be composed. Composition should remain explicit rather than hidden inside arbitrary object inheritance.

Examples:

- An active ability executes Heal, ApplyStatus, and SpawnArea definitions together.
- A timed-effect definition contains one or more definitions executed at every tick.
- An on-hit trigger executes Damage and ApplyPoison definitions.
- A mine executes Damage, Knockback, and SpawnVisualEvent definitions when triggered.

This resembles the Composite pattern, but not every leaf and container must expose every possible operation. The JSON schema should make valid child relationships explicit and reject impossible combinations during loading.

### Effect Categories

Effect definitions can share broad lifecycle categories without forcing one inheritance tree:

- Immediate operations.
- Active timed effects.
- Triggered effects.
- Persistent world effects.
- Stat or rule contributions.
- Selection-phase effects.
- Presentation requests emitted by resolved gameplay.

Categories assist validation and execution routing. Concrete capabilities and schemas matter more than a deep hierarchy such as `Effect -> TimedEffect -> DamagingTimedEffect -> PoisonEffect`.

### Modification Targets

One universal `IModifier<T>` is not recommended. It makes incompatible targets appear interchangeable and hides domain-specific ordering.

Instead, use explicit modification families or compiled target descriptors, such as:

- Damage construction modifiers.
- Damage-resolution modifiers.
- Active-effect property modifiers.
- Combatant-stat modifiers.
- Item-compilation modifiers.
- Selection-rule modifiers.
- Ability-activation modifiers.

They may share small supporting abstractions for ownership, matching, and ordering, but the compiler should prevent a selection-card modifier from being installed into a damage packet.

### Matching and Specifications

Modifiers locate targets through composable authored criteria. Likely criteria include:

- Tags such as `poison`, `fire`, `melee`, or `healing`.
- Damage type.
- Source item, slot, or combatant.
- Target combatant or allegiance relationship.
- Effect kind.
- Spatial membership supplied by a Godot adapter.
- Trigger and event metadata.

This is a suitable use of the Specification pattern. Specifications answer whether a candidate matches; they do not apply the modification themselves.

Tags should be stable validated identifiers rather than unrestricted user-facing strings. Display text remains separate and localizable.

### Ordering and Resolution Phases

A single numeric priority is insufficient for all modifications. First select a named domain phase, then apply deterministic ordering within that phase.

Example damage phases:

1. Construct base damage portions.
2. Add or remove portions.
3. Convert damage types.
4. Apply source flat modifiers.
5. Apply source multipliers.
6. Apply target weakness and percentage resistance.
7. Apply target flat resistance.
8. Resolve damage or over-resistance healing.
9. Change health.
10. Emit immutable resolved events.

Within a phase, order by explicit priority and then a stable source identifier. This ensures the authority and clients never depend on dictionary order, scene-tree order, or subscription timing.

Other subsystems, such as selection and effect scheduling, define their own named phases rather than reusing damage phases artificially.

### Authored and Acquisition Ordering

Effect arrays execute in their authored listed order by default. Item contributions also receive a stable match-local installation sequence when equipped so later items can operate on the result produced by earlier items.

Conceptually, each operation transforms the current value:

```text
value = operation1(value)
value = operation2(value)
value = operation3(value)
```

For a base maximum health of 100:

```text
Base:                  100
First item:  ×1.5  ->  150
Second item: ×1.5  ->  225
```

The result is 225% of base health, or a 125% increase over base. Item descriptions should prefer unambiguous language such as `Maximum Health ×1.5` or `+50% Maximum Health`. The phrase `+150% health` conventionally means adding 150% of base and would produce 250 from a base of 100.

Multiplication by itself is commutative, but mixing additive, multiplicative, replacement, conversion, minimum, and maximum operations makes ordering observable. The current proposal is:

1. The subsystem's named resolution phases establish broad semantic order.
2. Within a compatible phase, equipped items apply by stable installation sequence.
3. Multiple effects or operations belonging to one compiled item apply in authored list order.
4. Stable contribution IDs break any remaining ties.

Replacing an item removes its contributions. The replacement receives a new installation sequence because it is newly equipped. Combining or forging the same item instance retains its installation sequence while replacing its compiled operations.

Item acquisition history is confirmed as part of a build's behavior. Flat, multiplicative, replacement, conversion, minimum, maximum, and other compatible operations transform the running value in stable installation order.

The user interface must make this order inspectable. A derived-stat breakdown should be able to show the base value and every ordered transformation so players can understand why two builds containing similar items produce different results.

Removing or replacing an earlier item requires recalculating the complete ordered chain from the base value. Derived values should therefore be compiled or cached with revision tracking rather than mutated incrementally in a way that cannot reconstruct the correct result.

### Dynamic Active-Effect Properties

An active effect separates base state from effective state:

```text
Base tick interval
    + installed interval contributions
    = effective tick interval
```

The base runtime values come from the compiled definition when the effect is applied. Modifier contributions are evaluated or compiled into effective values when relevant contributions change.

Each tick builds a fresh damage packet using the active effect's current effective properties. Target defenses and other resolution modifiers are applied at that tick.

When an active effect's interval changes, the scheduler interrupts and recalculates the current cycle immediately:

1. Read the authoritative time of the effect's previous tick.
2. Add the new effective interval to calculate the new due time.
3. If the new due time is still in the future, reschedule the next tick for that time.
4. If the new due time is now or in the past, queue one immediate tick.
5. Do not generate multiple retroactive ticks for time that would have elapsed under the new interval.

Example: a poison last ticked 1.5 seconds ago and changes from a 2-second interval to a 1-second interval. Its newly calculated due time has passed, so it ticks immediately. If only 0.5 seconds had elapsed, it would tick after another 0.5 seconds.

The interrupt and any immediate tick are processed through the authoritative effect queue. This prevents reentrant calls and ensures identical ordering for multiplayer resolution. A small positive minimum interval and chain-operation ceiling protect against zero-time loops.

### Effect-Specific Property Schemas

Each effect kind declares exactly which properties it exposes for modification, including their types, valid ranges, and supported operations.

Examples:

- Poison may expose tick damage, damage types, interval, remaining duration, and radius-related matching tags.
- A mine may expose trigger radius, damage, arm time, active limit, and lifetime.
- An immediate heal has no remaining duration or tick interval to modify.

There is no artificial universal property set that every active effect must pretend to support. Shared property descriptors may be reused by several kinds, but compatibility is validated against the concrete effect schema when content loads.

An item that attempts to modify an unsupported property is invalid content and should produce a precise item-creator or loading error rather than silently doing nothing.

### Tag Catalog and Namespaced Extensions

Tags use a hybrid model:

- The base game publishes a known catalog such as `base:poison`, `base:fire`, `base:healing`, and `base:melee`.
- Item packs may introduce validated namespaced tags such as `family_pack:volatile_poison`.
- The item creator offers known tags through searchable selection while still allowing a pack to declare its own tags.
- Custom tags can participate in data-defined matching without requiring engine code.
- Validation warns when a custom tag is never produced, never consumed, or appears to be misspelled, but an otherwise valid custom tag is not rejected merely because the base game does not recognize it.

Namespacing prevents unrelated packs from accidentally assigning the same meaning to a generic tag name.

### Item Activation Success Rule

Item activation uses partial success:

1. The shared item cooldown must be ready.
2. Every contained active ability evaluates its own authored conditions and costs.
3. Each ability that can execute does so; unavailable abilities are skipped.
4. If at least one ability executes, the item consumes applicable shared activation resources and starts its universal cooldown.
5. If none can execute, activation fails and the cooldown does not start.

Item-level conditions may be used when the entire bundle must be gated atomically. This avoids adding a separate activation policy while still allowing an authored item to require a shared prerequisite.

### Ownership and Cleanup

Every installed runtime contribution must be traceable to an owner. Removal occurs by ownership, not by comparing modifier values or concrete object references.

Examples:

- Replacing an item removes all contributions owned by its `ItemInstanceId`.
- Leaving a poison radius removes contributions owned by that active radius and applied to the departing poison.
- Respawning removes active effects attached to the player and contributions owned by those effects.
- Ending a round clears combat-scoped active effects but retains equipped item instances.

Cleanup operations must be idempotent: removing an already removed contribution should not corrupt state or remove another source's identical modifier.

### Authored Reapplication and Stacking

When the same source repeatedly applies a permanent or scoped modification to the same target, the modification definition controls the behavior. Supported policies should include:

- Apply once per source.
- Refresh or replace the source's existing contribution.
- Stack on every successful application.
- Stack up to an authored maximum.

This policy belongs to the item data and is validated with the target property's schema. There is no universal poison-radius stacking rule.

### Trigger Processing and Safety

Effects may trigger other effects, creating chains. The authoritative resolver should use an explicit queue rather than recursive method calls.

Each resolution chain should carry:

- A chain or correlation ID.
- Current depth or processed-operation count.
- Source history sufficient for diagnostics.
- A high technical safety ceiling.

Each trigger definition also declares or inherits a per-chain activation budget. Examples include:

- Thorns may activate once in one resolution chain.
- Bouncing lightning may activate up to eight times in one chain.
- A deliberately recursive item may permit a larger authored number.

The resolver tracks activations by the relevant trigger instance and chain ID. Once that trigger's budget is exhausted, further attempts from that trigger in the same chain are skipped while unrelated triggers may continue.

Every independent periodic tick starts a new resolution chain with fresh per-chain budgets. This supports rules analogous to "X operations per tick" without allowing one poison tick's counter to affect later ticks.

The global chain ceiling prevents accidental infinite loops such as `on heal -> deal damage -> life steal -> heal`, including loops spread across several different triggers whose individual budgets are high. Reaching a trigger budget or the global ceiling produces a clear diagnostic and deterministic termination, not a frozen match.

Per-trigger budgets are authored gameplay rules. The much higher global chain ceiling is a technical safeguard, not a normal balancing restriction.

### Godot Boundary

The core owns gameplay definitions, active-effect state, modifier contributions, schedules, matching, and outcomes.

Godot adapters own:

- Physics overlap and raycast queries.
- Node and scene lifecycles.
- Animation, particles, sound, and interface feedback.
- Mapping world objects to runtime IDs.
- Collecting input and sending authoritative commands.

Godot may report that identified effects or combatants are inside a radius. It should not independently decide the magnitude, duration, ownership, or stacking of the resulting gameplay modification.

### Proposed SOLID Boundaries

- **Single Responsibility:** definitions describe; factories compile; instances hold state; resolvers execute; schedulers determine due work; adapters integrate Godot.
- **Open/Closed:** new registered effect kinds can be added without changing every item, while existing schemas remain stable.
- **Liskov Substitution:** avoid broad effect interfaces whose implementations cannot honor the same contract.
- **Interface Segregation:** use focused capabilities and handlers rather than forcing instant damage, timed poison, mines, and selection modifiers through one large interface.
- **Dependency Inversion:** domain logic depends on abstractions for clocks, randomness, spatial-query results, and event output—not on Godot nodes or static globals.

### Confirmed Summary for Decision 2

- Immutable definitions, momentary executions, active instances, and modifier contributions are distinct concepts.
- Persistent applications receive independent runtime identities and state.
- Effect kinds expose their own validated modifiable-property schemas.
- Effect composition is explicit and data-driven.
- Modifiers use domain-specific targets, named phases, deterministic ordering, specifications, and ownership.
- Temporary and permanent modification lifetimes are both supported.
- Reapplication and stacking policies are item-authored.
- Dynamic interval changes interrupt and deterministically reschedule the current cycle.
- Known base tags and validated namespaced custom tags are supported.
- Item activation uses partial success with one shared item cooldown.
- Trigger chains use authored per-trigger activation budgets and a high global technical ceiling.
- Every periodic tick begins a new resolution chain with fresh budgets.

## Decision 3: Domain Objects and Services

Status: Confirmed

### Design Goal

Place behavior where its invariants and required knowledge naturally live. The model should avoid both:

- An anemic domain in which entities are public data bags and enormous manager classes perform every operation.
- Overloaded entities that depend on Godot, networking, clocks, random generators, catalogs, other aggregates, and every game subsystem.

The proposed rule is:

> An entity protects state and invariants it owns. A domain service coordinates a rule that does not naturally belong to one entity. An application service coordinates a complete use case and its external ports.

### Three Behavioral Layers

#### Domain Objects

Entities and value objects contain deterministic behavior using data they already own or values explicitly passed to them.

They should:

- Protect constructors and state transitions.
- Reject invalid local state.
- Expose intention-revealing methods rather than public setters.
- Return immutable results or domain events describing what changed.
- Remain independent of Godot, JSON parsing, network peers, wall-clock APIs, and global singletons.

Examples:

```text
combatant.ApplyHealthChange(resolvedChange)
equipment.Replace(slot, newItem, expectedOldItemId)
item.BeginActivation(authoritativeTime)
activeEffect.Install(contribution)
activeEffect.Reschedule(newInterval, authoritativeTime)
selectionSession.AcceptChoice(offeringId, cardId)
```

These methods protect local invariants such as health bounds, slot occupancy, stale offering rejection, cooldown readiness, contribution ownership, and effect lifetime.

They should not independently locate other combatants, query physics, roll rarity, parse item files, or send RPCs.

#### Domain Services

Domain services contain stateless or explicitly scoped domain policies that require several objects or do not belong naturally to one entity.

Examples:

- Resolve an attack between a source and target.
- Compile an effective item from a definition and evolution state.
- Generate an offering from selection rules and a supplied random source.
- Match active effects against a modification specification.
- Determine selection order from round eliminations.
- Process an effect-execution chain.

A domain service may call behavior-rich entity methods. It should not bypass their invariants by mutating internal collections.

Domain services should not become generic `Manager` classes. Each service needs a narrow policy-oriented responsibility, explicit inputs, and explicit outputs.

#### Application Services

Application services execute complete game use cases. They coordinate domain objects, domain services, and interfaces to the outside world.

Examples:

- Handle an authoritative `ActivateItem` command.
- Handle a hit reported by a Godot hitbox adapter.
- Advance the authoritative simulation clock and process due effects.
- Start and end rounds.
- Generate and publish the next player's item offering.
- Equip a selected item and continue or end the selection session.
- Build a reconnect snapshot.

Application services own transaction boundaries and sequencing. They may depend on interfaces for persistence, time, randomness, spatial queries, and output publication, but not on concrete Godot nodes.

### Infrastructure and Godot Adapters

Adapters implement the external capabilities requested by application services:

- Godot physics supplies hit and overlap results.
- Godot input creates commands.
- Godot scenes represent combatants, equipment visuals, projectiles, and mines.
- Steam networking transports commands and snapshots.
- The filesystem loads JSON content.
- A deterministic authoritative random implementation supplies rolls.
- A simulation clock supplies match time.

Adapters translate at the boundary. Domain objects never store `Node`, `Resource`, `PackedScene`, `Timer`, peer, or RPC references.

### Proposed Dependency Direction

```text
Godot / Steam / Filesystem
            ↓ implements ports used by
Application use cases
            ↓ coordinates
Domain services
            ↓ operate on
Entities and value objects
```

Dependencies point inward. The core defines any interface it genuinely needs from an external system; the Godot project implements that interface.

The domain must not define interfaces merely to place an `I` before every class. Interfaces are justified at variation or infrastructure boundaries, not automatically for internal deterministic classes.

### Proposed Entity Responsibilities

#### Combatant

Owns:

- Identity.
- Health and life-local state.
- Equipment aggregate.
- Attached active-effect collection or an owned effect container.
- Installed stat contributions.
- Elimination state.

Protects:

- Health cannot exceed its effective maximum unless an explicit mechanic permits it.
- Health cannot fall below its allowed minimum.
- A dead or eliminated combatant cannot perform prohibited actions.
- Respawn restores and clears exactly the state defined by round rules.
- Owned contributions cannot be removed through another owner's handle.

Maximum-health state is layered. The combatant owns one immutable base maximum, ordered equipment contributions, ordered runtime ability/effect contributions, derived equipment and effective maximum checkpoints, current health, and a revisioned immutable health snapshot. Derived checkpoints are never independently mutated by adapters.

Equipment-passive contributions compile first. Runtime active, triggered, area, and other effect contributions compile afterward. Runtime state resets per life by default unless its authored lifetime explicitly overrides that policy.

Does not:

- Calculate an entire attack pipeline.
- Search for nearby enemies or effects.
- Load item definitions.
- Decide match victory.
- Send multiplayer messages.

#### Equipment

Owns:

- Fixed and flexible slots.
- Equipped item instances.
- Slot compatibility and capacity.
- Atomic replacement operations.

Protects:

- One item occupies no more than one slot unless an explicit mechanic says otherwise.
- Fixed-slot compatibility.
- Flexible-slot capacity.
- Expected-item checks for stale commands.
- Replacement removes the intended instance only.

Does not:

- Decide whether the match is currently in selection phase.
- Randomly generate items.
- Compile JSON definitions.
- Directly modify combatant stats when its collections change.

An equipment application use case coordinates replacement, compilation, and owned contribution installation as one transaction.

#### Equipped Item Instance

Owns:

- Runtime identity.
- Base definition reference.
- Evolution-state snapshot.
- Compiled effective-item snapshot.
- Shared cooldown and item-level charges.

Protects:

- Evolution state and compiled snapshot change together.
- Cooldown cannot begin twice from one activation.
- Activation cannot begin before the shared cooldown is ready.
- The compiled snapshot corresponds to the current evolution revision.

Does not execute arbitrary effects itself. It exposes the compiled ability and contribution descriptions used by the activation use case and effect resolver.

#### Active Effect Instance

Owns:

- Runtime identity and source/target attribution.
- Base runtime properties.
- Installed modifier contributions.
- Effective property cache or revision.
- Schedule and remaining lifetime.
- Per-instance local state.

Protects:

- Contribution ownership and idempotent removal.
- Supported-property schema.
- Valid rescheduling.
- Expired effects cannot tick.
- Runtime revisions remain internally consistent.

It does not query Godot physics or independently advance wall-clock time. The scheduler/application use case tells it the authoritative time and asks for due transitions.

#### Selection Session

Owns:

- Current selector and offering ID.
- Displayed card identities.
- Picks consumed and current entitlement state.
- Optional timer deadline.
- Public choice history.

Protects:

- Only the current selector may choose.
- A card must belong to the current offering.
- A stale offering cannot be selected.
- Each offering accepts at most one choice.
- Timer expiry accepts exactly one authoritative random choice.

It does not generate rarity rolls itself. An offering-generation service supplies a validated offering.

### Proposed Domain Services

#### Item Compiler

Input:

- Immutable item definition.
- Immutable evolution state.
- Effect and property schema registry.

Output:

- Validated immutable effective-item snapshot or structured errors.

The compiler resolves combinations and forge changes before combat. It does not equip the item or alter a combatant.

#### Combat Resolver

Input:

- Attack/effect intent.
- Immutable source and target resolution snapshots.
- Applicable contributions.
- Chain context.

Output:

- Resolved health changes.
- Contributions or active effects to install/remove.
- Triggered operations queued next.
- Immutable presentation facts.

The resolver calculates; the application use case commits valid results to entities in deterministic order.

#### Effect Resolver

Executes effect definitions in a supplied context, produces immediate outcomes, and creates active-effect or world-effect requests. It uses registered handlers for supported effect kinds.

It does not own the master clock or scene nodes.

#### Effect Scheduler

Maintains or calculates authoritative due ordering for active effects. Given time advancement, it identifies due work in deterministic order.

Recommendation: the scheduler indexes timing, while each active effect owns and validates its schedule state. This prevents the scheduler from becoming the true owner of effect behavior and prevents each effect from creating its own Godot timer.

#### Selection Rule Resolver

Combines lobby base rules with compiled equipped-item contributions to calculate card count, pick entitlement, guarantees, weights, and timer rules.

It produces immutable rules used by offering generation and the selection session.

#### Offering Generator

Uses resolved selection rules, the immutable item catalog, player equipment snapshot, and an injected authoritative random source to produce cards. It does not mutate selection or equipment state.

#### Specification Evaluator

Evaluates validated compiled matching specifications against candidates. This may be a set of focused specification objects rather than one centralized service; the important boundary is that matching remains separate from applying modifications.

### Commands, Events, and Results

Commands express requested intent:

- Activate item.
- Submit selection choice.
- Report authoritative hit candidate.
- Request respawn.

Commands can be rejected and should contain expected IDs or revisions where stale state matters.

Domain events and resolved facts state what happened:

- Damage resolved.
- Health changed.
- Active effect installed.
- Item activated.
- Combatant eliminated.
- Selection choice accepted.

Events are immutable data. They should not use a static global event bus. Application services process them through an explicit local queue and publish the subset needed by presentation or networking adapters.

This keeps effect-chain ordering visible and testable.

### Transaction Boundaries

Some operations must succeed or fail as a unit.

Examples:

- Replacing an item removes the old item's contributions, installs the new item's contributions, updates equipment, and publishes one consistent result.
- Forging validates evolution, compiles the effective snapshot, and replaces instance state only if compilation succeeds.
- Choosing a card validates the offering, equips the item, recalculates pick entitlement, and advances the selection session atomically.
- Resolving one queued combat operation commits its ordered outcomes before processing triggers it generated.

Application services coordinate these transactions. Entities expose reversible preparation or validated state-transition operations as needed, but the design should prefer validating all inputs before mutation rather than implementing broad rollback machinery.

### Snapshot Strategy

Resolvers should consume immutable snapshots rather than hold live references to mutable aggregates during a calculation.

Benefits:

- Deterministic tests.
- Clear before-and-after state.
- Easier replication and diagnostics.
- No modifier changing a collection while it is being iterated.
- Resolved events accurately record the values used.

Entities remain behavior-rich because they create snapshots, validate commands, and commit transitions. Snapshots do not turn them into public mutable data bags.

### Time, Randomness, and Spatial Queries

The domain core must never call system time or create uncoordinated random generators.

Application services supply:

- Authoritative simulation timestamps.
- Deterministic random results or an injected random port.
- Identified spatial-query results from Godot.

This enables replayable tests and prevents authority/client divergence.

Recommendation: use explicit time values in most domain methods. Introduce a clock interface only at the application boundary rather than injecting clocks into every entity.

### Failure Model

Expected invalid commands return structured domain results rather than throwing exceptions:

- Cooldown active.
- Stale offering.
- Invalid card.
- Wrong phase.
- Slot incompatible.
- Combatant eliminated.

Exceptions remain appropriate for programmer errors, corrupt internal state, or violated assumptions that validation should have prevented.

Content loading and compilation return collections of structured validation errors so the item-creation application can display every problem in one pass.

### Testing Boundaries

- Value-object tests cover validation and calculations.
- Entity tests cover local invariants and state transitions.
- Domain-service tests cover policies and resolution combinations.
- Application-service tests use fake ports to cover complete use cases and transaction ordering.
- Godot integration tests cover adapter translation, physics, scene lifecycle, and replication wiring.

The majority of item, effect, combat, and selection behavior should be testable without launching Godot.

### SOLID Implications

- **SRP:** responsibilities follow ownership and use-case boundaries rather than files named `Manager`.
- **OCP:** handlers, schemas, and compiled definitions extend supported content without editing combatants.
- **LSP:** narrow contracts avoid substituting objects that cannot uphold the expected behavior.
- **ISP:** external ports are capability-specific; a spatial query adapter does not also load content or send RPCs.
- **DIP:** orchestration depends on core-defined ports and immutable data, with Godot providing implementations.

### Confirmed Summary for Decision 3

- `Combatant` contains a dedicated owned `ActiveEffectContainer` that protects effect installation, lookup, modification, and cleanup.
- `Equipment` is an owned child of `Combatant`, not an independently persisted aggregate.
- `CombatResolver` calculates and returns a complete immutable resolution result before an application use case commits valid state changes.
- Each active effect owns and validates its schedule state; one centralized scheduler indexes effects by authoritative due time.
- Domain and application namespaces remain in `BattleArena.Core` initially. A separate application assembly will be extracted only if its size or dependency needs justify it.
- The first implementation uses an in-memory authoritative match.
- Reconnect-friendly immutable snapshots are considered in state design, but durable match persistence is deferred until after the combat vertical slice.
- Entities protect local invariants, domain services coordinate multi-object policies, and application services execute complete transactional use cases.
- Godot, Steam, filesystem, clock, randomness, and spatial behavior remain behind application-boundary adapters or explicit inputs.
- Expected invalid commands return structured failures; exceptions represent programmer errors or corrupted internal assumptions.
- Domain events and resolved facts use explicit queues and immutable data rather than a static global event bus.

## Decision 4: Design Patterns and Interface Boundaries

Status: Confirmed

### Design Goal

Use patterns to protect known variation, ordering, ownership, validation, and engine boundaries. Do not introduce patterns solely because their names are familiar or because every concrete class could theoretically have an interface.

The governing questions are:

1. What is expected to vary independently?
2. Which invariant or dependency does the abstraction protect?
3. Can an invalid combination be rejected during compilation rather than during combat?
4. Does the pattern make execution and ownership easier to trace?
5. Is a direct concrete dependency currently simpler and equally testable?

### Pattern Map

| Pattern | Proposed use | Why it fits |
|---|---|---|
| Adapter | Godot, Steam, filesystem, clocks, and spatial-query boundaries | Keeps engine concerns outside the domain |
| Factory + explicit registry | Compile stable JSON `kind` values into supported definitions and handlers | Extensible without CLR-name reflection |
| Strategy | Interchangeable effect handlers, formulas, targeting, stacking, and random policies | Behavior varies independently of item identity |
| Composite | Abilities and timed effects containing child effect definitions | Items execute trees/lists of authored behavior |
| Specification | Tag, source, allegiance, state, and spatial matching | Matching composes independently of modification |
| Pipeline / Chain of Responsibility | Named combat and effect-resolution phases | Deterministic ordered transformation |
| Command | Player and network intent entering authoritative application use cases | Validation, stale-state checks, and replayable tests |
| State machine | Lobby, combat, respawn, selection, and match-end phases | Makes legal transitions and commands explicit |
| Result | Expected command failures and validation errors | Avoids exceptions for normal rejected actions |
| Snapshot | Resolution inputs, effective items, reconnect state, and published outcomes | Determinism and stable before/after state |
| Ownership handle | Installed modifiers, effects, abilities, and presentation contributions | Exact idempotent cleanup |

Some of these are ordinary design techniques rather than classes named after patterns. The code should express the idea clearly without forcing pattern terminology into every type name.

### Adapter Pattern

Adapters translate between the pure core and external systems.

Examples:

- `GodotHitAdapter` turns an authoritative hitbox overlap into a core command containing runtime IDs and hit metadata.
- `GodotSpatialQueryAdapter` converts scene-world overlap results into identified candidate sets.
- `SteamCommandTransport` turns network messages into authenticated application commands.
- `JsonItemDefinitionSource` reads external item files and supplies raw definitions to core parsing and validation.
- Presentation adapters turn immutable combat facts into animations, particles, audio, floating numbers, and user-interface updates.

Adapters must not duplicate game rules. A Godot area reports what overlaps; the core decides whether those overlaps match a poison-amplification specification.

### Factory and Explicit Registry

The item system needs factories, but not unrestricted reflection.

An explicit registry maps stable content identifiers to known compilers and runtime handlers:

```text
"base:damage"               -> Damage definition compiler/handler
"base:apply_timed_effect"   -> Timed-effect compiler/handler
"base:modify_active_effect" -> Active-effect modifier compiler/handler
"base:spawn_world_effect"   -> World-effect compiler/handler
```

The registry is assembled in one composition root when the application starts. Duplicate keys fail startup. Unknown kinds produce structured content-validation errors.

Advantages:

- JSON does not depend on C# type names.
- Supported executable behavior is allow-listed.
- Renaming a class does not invalidate item files.
- Tests can assemble a small registry containing only relevant handlers.
- Schema versions and migrations remain explicit.

User content may compose registered behaviors but may not load arbitrary assemblies or name arbitrary CLR types. Executable code modding would be a separate future security and compatibility decision.

### Strategy Pattern

Use Strategy where a domain operation has several interchangeable policies behind the same narrow contract.

Likely strategies include:

- Value operations: add, multiply, replace, clamp, min, max, convert.
- Reapplication: once per source, refresh, replace, stack, capped stack.
- Target selection: self, hit target, radius candidates, all combatants, active effects matching a specification.
- Random selection: weighted choice, uniform choice, range roll, probability branch.
- Timing behavior: periodic ticks, delayed execution, duration-bound area.
- Potential item-compiler transformations for combine and forge operations.

Avoid a single universal `IStrategy` or `Execute(object context)`. Each strategy family needs typed inputs and outputs that express its invariant.

Data-only strategies should generally be compiled into discriminated values or sealed policy types. Interfaces are most useful where runtime behavior truly has multiple implementations and substitutability can be tested.

### Composite Pattern

An ability may contain several effects; a timed effect may execute several child effects per tick; a triggered effect may execute another authored bundle.

```text
Chest ability
├── Heal owner
├── Create poison-amplification radius
└── Apply self weakness
```

Composition should preserve ordering explicitly. JSON arrays execute in validated authored order unless a definition declares another policy.

Not every definition can contain every other definition. For example, a display-only description node should not be accepted where an executable child effect is required. Compilation validates child capabilities.

Avoid relying on a deep base-class tree to represent composition. Prefer immutable definitions containing typed child definitions or compiled executable nodes.

### Specification Pattern

Specifications describe whether a candidate matches. They do not perform the resulting action.

Example:

```text
All of:
├── Has tag base:poison
├── Is inside supplied radius-result set
└── Source allegiance is Any
```

Supported composition may include `All`, `Any`, and `Not`, plus typed leaves such as tag, source, target, damage type, slot, health state, and runtime-ID membership.

Compiled specifications should be immutable and validate candidate compatibility. A specification that requires `DamageType` cannot silently run against a selection card.

Spatial leaves consume identified results supplied by an adapter; specifications never query Godot directly.

### Resolution Pipeline

Combat and effect processing use explicit named phases. This resembles Chain of Responsibility, but handlers transform a well-defined context rather than deciding opaque control flow.

Each pipeline declares:

- Allowed input and output types.
- Ordered named phases.
- Compatible contribution kinds per phase.
- Stable ordering inside a phase.
- Whether contributions may append queued operations.
- Validation and chain-budget behavior.

Do not build one global pipeline for combat, selection, compilation, and scheduling. Each subsystem owns phases matching its vocabulary.

Pipeline handlers should normally be small concrete classes or compiled operations. An interface is justified when independently registered implementations participate in the same phase contract.

### Command Pattern

Commands are immutable requested intentions entering the authority:

```text
ActivateItemCommand
SubmitSelectionChoiceCommand
ProcessHitCandidateCommand
RequestRespawnCommand
```

Commands include runtime IDs, expected offering/revision IDs, and client sequence metadata where needed. They contain no Godot nodes.

A command handler is an application use case. It authenticates authority, validates match phase and stale state, coordinates domain services and entities, commits results, and returns immutable outcomes.

We are using commands as a clean boundary, not committing to a full CQRS framework, message broker, or event-sourced architecture.

### Match State Machine

The match has explicit phases such as:

```text
Lobby
→ RoundStarting
→ Combat
→ RoundResolving
→ ItemSelection
→ RoundStarting or MatchEnded
```

The state machine owns legal transitions and phase-level command permissions. Equipment replacement is permitted in `ItemSelection` and rejected in `Combat`.

Initial recommendation: use an explicit `MatchPhase` value plus a transition table/policy and focused phase methods. Do not begin with one polymorphic class per state unless phase-specific behavior becomes large enough to justify the full State pattern.

This keeps transitions inspectable and serializable for reconnect snapshots.

### Result Pattern

Expected failures return typed results:

```text
Success(value, events)
Failure(code, message, details)
```

Failure codes are stable machine-readable values; messages are developer diagnostics or localization keys rather than the only source of meaning.

Do not return `null` for domain failure and do not throw for normal invalid commands. Do throw for corrupted invariants, impossible internal states, or programmer misuse.

Avoid an excessively generic result carrying untyped dictionaries. Each use case can return a focused result type while sharing small success/failure primitives.

### Snapshot Pattern

Immutable snapshots are used at calculation and transport boundaries:

- Effective item snapshot.
- Combatant resolution snapshot.
- Active-effect property snapshot.
- Match reconnect snapshot.
- Offering snapshot.
- Resolved outcome facts.

Snapshots should be purpose-specific. Do not create one enormous `GameStateSnapshot` and pass it through every method.

Snapshots may omit private implementation details while carrying version/revision IDs for stale-state detection.

### Ownership Handle Pattern

Ownership handles are strongly typed value objects identifying installed contributions.

They support:

- Removing exactly the modifiers installed by one item.
- Removing radius contributions when a target exits.
- Preventing one identical contribution from deleting another source's contribution.
- Idempotent cleanup.
- Diagnostics and forge provenance.

An ownership handle is not a disposable Godot node or raw object reference. It remains meaningful in tests, snapshots, and network diagnostics.

### Domain Events Without a Global Observer Bus

The Observer pattern is useful at the application boundary for publishing completed facts to presentation and networking. It is dangerous as an invisible internal control-flow system.

Recommendation:

- Domain operations return immutable facts and queued operations explicitly.
- The authoritative application service processes effect chains through a visible local queue.
- After state commits, an output port publishes selected facts to adapters.
- Subscribers cannot mutate authoritative domain state during publication.

Do not use a static `GlobalSignals`, global event aggregator, or service locator to drive combat rules. Hidden subscription order would undermine deterministic resolution and cleanup.

### Dependency Injection and Composition Root

Use constructor injection for required dependencies in ordinary C# application services and adapters.

Assemble the object graph in one Godot-side composition root during startup:

- Register effect kinds and schemas.
- Construct domain services.
- Construct application services with external ports.
- Attach Godot and Steam adapters.
- Validate that required registrations exist.

Use manual composition rather than a dependency-injection container for the initial implementation. The object graph should remain small enough to understand directly. Introduce a container only if composition becomes genuinely repetitive or conditional.

A dependency-injection container is an object-construction library. Instead of writing constructors directly in the composition root:

```text
resolver = new CombatResolver(...)
handler = new ActivateItemHandler(resolver, ...)
facade = new GameApplicationFacade(handler, ...)
```

the application registers mappings and lifetimes:

```text
Register CombatResolver
Register ActivateItemHandler
Register ISpatialQueryPort -> GodotSpatialQueryAdapter
Resolve GameApplicationFacade
```

The container examines constructor dependencies and builds the graph. Containers are useful when an application has many services, conditional registrations, and scoped lifetimes. Their costs include indirection, registration errors appearing at startup, harder navigation, and lifecycle behavior that may be unnecessary in a small game core.

Manual constructor injection still follows Dependency Inversion. "Dependency injection" means dependencies are supplied from outside; it does not require a container.

Future migration to a container should remain mechanical because:

- Required dependencies use constructor parameters.
- Objects do not construct their own service dependencies.
- Gameplay services do not use global mutable singletons or a service locator.
- All startup wiring is centralized in one composition root.
- Lifetimes and ownership remain explicit rather than depending on ambient resolution.
- External adapters implement narrow core-defined ports.

A later migration would largely replace explicit constructor calls in the composition root with registrations. Domain objects and services should not need redesign merely because construction becomes automated.

Godot scene nodes may receive application-service references from the composition root or resolve one narrow application facade attached to the running match. They should not pull arbitrary services from a global locator.

### Application Facade

A narrow facade can provide the Godot layer with stable use-case entry points:

```text
Submit(command)
AdvanceSimulation(time)
CreateSnapshot(request)
```

This is not a god object containing business logic. It delegates to focused application handlers and exposes only the operations adapters need.

The facade can simplify scene wiring and tests while keeping concrete domain services private from Godot nodes.

### Patterns to Avoid Initially

#### Interface for Every Class

Concrete deterministic domain services do not need interfaces merely for mocking. Test them directly. Add an interface when multiple implementations, an external boundary, or independent registration requires one.

#### Service Locator and Global Singleton

Avoid `GetService<T>()`, static mutable registries after startup, and global gameplay signals. Dependencies and ownership should remain visible.

The compiled effect registry can be immutable after composition and passed explicitly where required.

#### Deep Inheritance Hierarchies

Avoid trees such as:

```text
Effect
└── DamagingEffect
    └── TimedDamagingEffect
        └── ElementalTimedDamagingEffect
            └── PoisonEffect
```

Composition and capabilities handle mixed behavior more effectively. Inheritance remains acceptable for genuinely shared implementation where substitutability is clear, but it should be shallow and rare.

#### Universal Generic Modifier

Avoid `IModifier<T>` as the central domain abstraction. It permits nonsensical combinations, hides phases, and moves validation into runtime casts.

Use typed modification families, property schemas, and compiled operations.

#### Reflection-Based Content Execution

Do not instantiate arbitrary types named by JSON. Explicit allow-listed kind registration is safer, versionable, and easier to validate.

#### Full CQRS, Event Sourcing, or Message Broker

Commands and immutable events are useful without adopting infrastructure intended for distributed business systems. The authoritative in-memory match remains the source of truth.

#### Generic Repository and Unit of Work

There is no database-backed aggregate persistence yet. Do not add repository abstractions until a real storage boundary exists. `ItemCatalog` is a domain-specific read-only catalog, not `IRepository<Item>`.

#### ECS Rewrite

Godot already supplies scene composition, while the pure core uses aggregates and focused services. An entity-component-system architecture would add major complexity without a proven need at the expected player count.

### Interface Qualification Rules

An interface is justified when at least one is true:

1. It is an external port implemented outside Core.
2. Multiple implementations must be selected at runtime or registration time.
3. It is a plugin-style handler contract for allow-listed effect kinds.
4. It isolates nondeterminism such as randomness or external time.
5. A focused capability must be consumed without exposing a larger implementation.

An interface is not justified merely because:

- A class is public.
- A unit test might mock it instead of constructing it.
- SOLID is interpreted as requiring every dependency to be abstract.
- Another language used duck typing for the same operation.

Interfaces should be small, cohesive, and named after capabilities or ports. Implementations should be substitutable under contract tests where multiple implementations exist.

### Proposed Initial Interfaces

The exact names may change, but the initial interface surface should remain small.

External/application ports:

- `IAuthoritativeRandom`: deterministic authoritative random operations.
- `ISimulationClock`: authoritative current simulation time at the application boundary.
- `ISpatialQueryPort`: identified candidate queries implemented by Godot.
- `IContentTextSource`: raw item-file discovery and reading implemented by filesystem/workshop adapters.
- `IGameplayOutputPort`: publishes committed immutable facts to Godot/network presentation.
- `IRuntimeIdAllocator`: authority-owned match-scoped ID allocation, if not owned directly by match state.

Plugin-style core contracts:

- Effect-definition compiler/handler contract keyed by stable effect kind.
- Property-schema provider contract for registered modifiable effect kinds.
- Optional pipeline-contribution handler contracts separated by domain family.

Types that should initially remain concrete:

- `CombatResolver`.
- `ItemCompiler` coordinator.
- `OfferingGenerator`.
- `SelectionRuleResolver`.
- `EffectScheduler`.
- Entity and value-object types.
- Match phase-transition policy.

Concrete services can still receive interface ports and registered handler collections through constructors.

### Strategy Preference

Strategy is a particularly good fit for this project because operation, targeting, reapplication, timing, and random behavior must vary independently while item definitions remain data-driven.

We should favor focused strategy families with strongly typed contexts. We should not convert every small arithmetic enum into a class automatically; simple compiled operations can remain immutable discriminated values when that is clearer and faster to validate.

### Testing the Patterns

- Contract tests verify every external-port fake and production adapter obeys important semantics.
- Registry tests reject duplicate and unknown kinds.
- Handler tests verify supported schemas and execution output.
- Specification tests cover composition and incompatible candidate types.
- Pipeline tests prove phase ordering and stable tie-breaking.
- Command-handler tests prove phase validation, stale IDs, atomic commit, and output publication.
- State-machine tests cover every legal and illegal transition.
- Ownership tests prove idempotent removal and isolation between identical contributions.

### Confirmed Summary for Decision 4

- Manual constructor injection and one explicit composition root are used initially; a DI container may be introduced later without changing domain design.
- Match phases begin as an enum plus explicit transition policy, promoting to State objects only if behavior becomes unwieldy.
- Unknown executable effect kinds reject an item file; unknown valid namespaced tags remain allowed with warnings.
- Authored effect arrays execute in listed order by default.
- Godot nodes use one narrow application facade and act primarily as adapters for physics, collisions, spatial information, input, and presentation.
- Focused Strategy families are encouraged where behavior genuinely varies.
- Mixed compatible operations apply in stable item installation order, making acquisition history mechanically meaningful.
- Adapter, explicit registry/factory, Composite, Specification, Pipeline, Command, Result, Snapshot, and ownership-handle patterns are used at the boundaries identified above.
- Interfaces are introduced for external ports, registered interchangeable handlers, nondeterminism, or focused capabilities—not automatically for every public class.
- Global gameplay buses, service locators, deep effect inheritance, universal generic modifiers, reflection-based execution, premature persistence patterns, and a full ECS rewrite are avoided.
