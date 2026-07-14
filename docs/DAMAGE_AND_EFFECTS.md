# Battle Arena — Damage and Effect Model

Status: Early design / living document  
Last updated: July 11, 2026

## Purpose

This document defines the shared vocabulary for damage, resistance, status effects, and item-authored gameplay behavior. The system exists to support creative items rather than to impose conventional role-playing-game rules on them.

## Effects Are the Rules

Items and equipment categories have no automatic combat behavior. Every bonus, penalty, attack modification, resistance, and ability must come from an explicit authored effect.

Examples:

- Chest armor does not reduce damage merely because it is armor.
- Plate armor will often include protection effects because that fits its theme, but it does not have to.
- An amulet may grant physical armor.
- Boots may increase fire resistance or create a fire weakness.
- A weapon can heal, inflict a status, change movement, or do something unrelated to ordinary weapon expectations.

Slot themes help items feel coherent and make choices readable. They are not hard restrictions on item mechanics.

## Initial Damage Types

The initial working types are:

- Physical.
- Fire.
- Frost.
- Lightning.
- Poison.
- Arcane.

These will likely be represented by an enum in code and expanded as item designs require. The precise list is less important than ensuring effect and resistance systems accept a damage type consistently.

## Damage Instances

A damage event should be able to carry at least:

- Base amount.
- Damage type.
- Source player or world object.
- Source item or ability.
- Whether it is immediate or periodic.
- Any tags or flags required by triggered effects.

The architecture should allow an item to create unusual authored outcomes, including fixed damage, ranged damage, critical or probabilistic damage, damage over time, and conditional damage.

Authored or source-modified damage that falls below zero is normalized to zero. Negative damage is not used as an implicit representation of healing. Type-specific resistance above 100% produces a separate explicit healing outcome using only the excess resistance beyond 100%.

A single attack or effect may contain multiple damage types. As builds accumulate items, mixed damage is expected to become common. Each typed portion should be resolved against the target's applicable resistance so, for example, Physical damage can harm a target while Fire damage from the same attack heals them.

## Effects and Modifications

Effects and modifications are distinct concepts:

- An effect performs or represents gameplay behavior. Examples include an attached poison, a damage instance, a timed buff, a placed mine, or a triggered heal.
- A modification changes a value or rule used by an effect. Examples include increasing poison damage, extending its duration, shortening its tick interval, or adding another damage type to an attack.

Items can create effects and can also modify effects created by other equipped items. This cross-item modification is central to synergy and should be supported deliberately rather than handled as a collection of one-off item exceptions. Equipment changes occur only during the between-round selection phase; items cannot be swapped while combat is underway.

Modifiable effect properties may include:

- Magnitude.
- Duration.
- Tick or activation interval.
- Damage types.
- Probability.
- Area, range, speed, and quantity.
- Stack behavior.
- Trigger conditions.
- Target filters.
- Cooldowns, costs, and charges.

Modifications may apply broadly—such as all poison effects owned by the player—or narrowly to effects matching specific tags, sources, or conditions.

Active effects remain modifiable after application. An authored area ability may locate all poison-tagged active effects in a radius and change their damage, remaining duration, interval, or other exposed properties. The affected poison instances remain independent; the area ability owns the matching and modification behavior.

Spatial and allegiance filters are item-defined. An effect may target only the owner's poison, allies' poison, enemies' poison, or every poison instance in range. Godot performs or assists with spatial discovery, while the core applies deterministic effect-matching and modification rules using stable runtime IDs.

The authored modifier also defines its lifetime:

- Scoped modifiers are removed when the target stops matching, such as leaving a radius.
- Permanent modifiers remain attached to the active effect for the rest of that effect's lifetime.

Temporary modifiers should be represented as owned contributions rather than destructive changes to base state, allowing their removal to restore the correct effective values.

## Resistance and Armor

"Armor" is presentation language for effects that reduce damage. It is not an automatic property of an equipment slot.

An item may provide:

- Flat reduction against a specified damage type.
- Percentage reduction against a specified damage type.
- Flat or percentage reduction against multiple types.
- General reduction against all damage types.
- Negative resistance, presented as a weakness.

An individual item decides whether it uses a flat value, percentage, or a combination.

### Type-Specific Over-Resistance

Resistance to a particular damage type may exceed 100%. Only resistance beyond 100% becomes healing. For example, 120% Fire resistance converts 100 Fire damage into 20 health restored. This is an intentional build possibility: specializing heavily enough can cause an opponent's attack to become beneficial.

The interaction with flat reduction and mixed damage remains to be tested.

### General Resistance

General all-damage resistance should not normally turn damage into healing. Its initial rule clamps a successful damage event to a minimum of 1 damage. A future item may explicitly override this rule if its identity calls for it.

### Weakness

Negative resistance increases matching incoming damage. The selection interface should describe this plainly—for example, "Weakness to Fire"—rather than requiring players to interpret a negative resistance number without context.

## Order of Operations

The final calculation order is undecided and needs focused tests. A working pipeline to evaluate is:

1. Establish the authored base damage or rolled result.
2. Apply source-side additive and multiplicative modifiers.
3. Apply target weakness or percentage resistance.
4. Apply flat resistance.
5. Convert type-specific over-resistance into healing when applicable.
6. Apply the general-resistance minimum-damage rule.
7. Emit the resolved outcome for health changes and triggered effects.

This ordering is provisional. Changing the order can substantially alter stacking and should be treated as a design decision, not an implementation detail.

## Damage Over Time and Status Effects

Damage over time uses discrete ticks rather than applying a tiny amount every frame. A timed effect owns its schedule and emits a normal damage instance at each tick. Every emitted instance is independently processed through damage types, resistance, weakness, and triggered effects.

Poison uses both a status-like attached effect and typed damage:

- The poison effect attaches to the affected player for an authored duration.
- It emits a damage instance at an authored interval.
- Each tick may contain Poison damage and potentially other damage types.
- Resistance is evaluated separately for every tick.
- Removing the attached poison effect stops future ticks without changing the target's resistance.

Timing and damage are defined by the originating item. A hypothetical Greater Poison Sword might tick every 0.5 seconds for 10 seconds, while a Lesser Poison Sword might tick every 2 seconds for 10 seconds and use a different damage amount.

Other items can modify these poison effects. A chest-armor item might increase poison tick damage, extend poison duration, shorten the interval between ticks, or alter several of those properties. These modifications should apply through defined matching rules such as effect tags rather than requiring the armor to know about every individual poison weapon.

A spatial poison influence independently filters which combatants are affected and whose poison effects qualify. Authored relationship options support self, allies, enemies, or everyone for affected combatants, and owner-only, allied, enemy, or all sources for the poison's origin. Existing qualifying poison instances update on entry; new qualifying instances update while membership remains active; exit or toggle-off removes only that influence's contributions.

Effect tags, damage types, stable damage-portion IDs, first-tick policy, and completion-policy type are immutable structural properties. Runtime modifiers change exposed numeric values without changing the effect's nature.

Interval changes interrupt the current schedule immediately. The next due time is recalculated from the last tick, or from application time before the first tick. If that recalculated time has already passed, one tick becomes immediately due without creating multiple retroactive ticks.

Completion-value changes preserve progress. For tick-count completion, executed ticks remain executed and the modified total determines the new remainder. If two of five ticks have executed, adding three produces six remaining ticks; reducing the total to two expires the effect immediately. Duration completion similarly retains its original application time and moves its end time by modifying the authored duration value.

Temporary modifications are owned contributions. Removing their owner recompiles effective values from the immutable definition and all remaining contributions.

### Confirmed Physics-Aligned Time Representation

Effect timing uses integer authoritative simulation ticks rather than floating-point elapsed seconds. The Godot adapter advances the clock once for every authoritative physics step. The project initially uses 60 physics ticks per second.

Authored JSON may use readable seconds. Content compilation converts seconds into an integer tick count using the match's fixed simulation rate and an explicit rounding policy. The item creator displays both values when rounding occurs.

At 60 ticks per second:

```text
0.5 seconds = 30 ticks
1 second    = 60 ticks
2 seconds   = 120 ticks
```

The minimum schedulable interval is one authoritative simulation tick. Values below one tick are rejected or explicitly clamped during content validation; runtime effects never schedule work between physics steps.

The authoritative simulation clock advances only while the active arena simulation is running. Paused gameplay and between-round selection do not consume effect intervals, durations, cooldowns, or other simulation-time schedules.

The simulation rate is fixed for a match and included in authoritative configuration/snapshots. It must not silently change after item timing has been compiled.

### First-Tick Policy

Periodic definitions explicitly choose one of two first-tick policies:

- Tick immediately when applied.
- Wait one full effective interval before the first tick.

Waiting one interval is the default, but both behaviors are supported and clearly described by the item.

### Periodic Completion Policies

Periodic effects have two useful completion models.

#### Tick-Count Completion

The effect owns an interval and a number of ticks remaining.

```text
Interval: 2 seconds
Tick count: 5
First tick: after one interval
Tick times: 2, 4, 6, 8, 10
```

Advantages:

- The exact number of executions is unambiguous.
- No interval-versus-expiration boundary decision is required.
- Total nominal lifetime is easily derived for a fixed interval.
- Deterministic scheduling and testing are straightforward.

Tradeoffs:

- Changing interval changes total elapsed lifetime when remaining tick count stays fixed.
- Faster ticks front-load the same remaining executions instead of creating more executions.
- "Increase duration" must mean adding ticks, changing interval, or another explicit operation.
- Immediate-first-tick effects have a different elapsed lifetime: five ticks at 0, 2, 4, 6, and 8 seconds.

#### Duration Completion

The effect owns a duration/end time and ticks whenever its interval becomes due during that lifetime.

Advantages:

- The effect exists for an exact authored amount of simulation time.
- Speeding up its interval naturally produces more ticks in the same duration.
- Temporary areas, buffs, and debuffs map naturally to duration.

Tradeoffs:

- A boundary policy is required when a tick is due exactly at expiration.
- Interval changes can alter the eventual number of ticks.
- Item descriptions may not promise an exact tick count.

#### Confirmed Completion-Policy Model

Definitions explicitly select their completion policy:

- `AfterTickCount` for behavior promising an exact number of executions.
- `AfterDuration` for behavior promising an exact lifetime.
- Future persistent effects may use an authored external removal condition instead.

A periodic effect uses one authoritative completion policy, not both tick count and duration as competing termination conditions. User-facing descriptions may display a derived expected duration or expected tick count.

The selected completion-policy type is immutable for the active effect. Modifiers may change total tick count for `AfterTickCount` or total duration for `AfterDuration`, but cannot convert between the policies. An authored replacement effect is required to change that structural behavior.

For the first implementation, scheduling tests prove both policies. Poison will become the first gameplay effect using `AfterTickCount`, while a temporary poison-amplification radius can later prove `AfterDuration` in an authored item.

## Healing Events

The system must distinguish between an attempted healing effect and health actually restored:

- `HealingApplied` represents a healing effect being processed, even if resistance, full health, or another rule changes its final result.
- `HealthGained` represents the resolved increase in current health.

Item descriptions and trigger definitions should say which event they use. "When a healing effect is applied, gain attack speed" can trigger without actual health gain, while "after gaining X health within one second, gain attack speed" depends on resolved health changes.

Healing created by over-resistance counts as actual health gained and can contribute to `HealthGained` triggers. Whether it also counts as a healing effect for `HealingApplied` triggers should be explicitly chosen by the authored effect or event tags rather than assumed globally.

Sliding-window triggers such as "gain X health within the last second" should aggregate timestamped resolved health changes. Healing-reduction effects naturally influence these triggers because they change the amount of health actually gained.

## Atomic Net Health Resolution

All damage and healing portions belonging to one resolved packet combine into one atomic net health change:

```text
Net health change = total resolved healing - total resolved damage
```

The combatant's health does not move through intermediate damage and healing states while that packet commits. For example, a combatant at 10 health receiving 20 damage and 30 healing ends at 20 health, not 30:

```text
10 + (30 - 20) = 20
```

Raw resolution facts remain available independently:

- Total damage resolved.
- Total healing resolved.
- Net health change.
- Actual health gained after the atomic commit and maximum-health clamp.
- Actual health lost after the atomic commit and minimum-health handling.

This allows precise triggers. `DamageResolved` and `HealingApplied` can occur even when they cancel one another, while `HealthGained` and `HealthLost` reflect only the actual atomic change in stored health.

Death or elimination is evaluated after the entire packet's net health change is calculated. No intermediate portion can kill, revive, or cross a health threshold independently within the same packet.

After applying the atomic net change, stored current health clamps to a minimum of zero. The immutable result separately records overkill—the amount by which the unclamped result fell below zero—so authored triggers, abilities, statistics, and presentation can use it.

For example:

```text
Current health:       20
Atomic net damage:    75
Unclamped result:    -55
Stored health:         0
Recorded overkill:    55
```

Ordinary healing does not revive an eliminated combatant. Resurrection, if introduced, must be an explicit authored mechanic rather than a side effect of applying normal healing to zero health.

## Maximum-Health Changes During Combat

Items are equipped or replaced only between rounds, but active and passive effects may increase or decrease effective maximum health during combat.

When effective maximum health changes, preserve the combatant's current-health percentage:

```text
Old state:  50 / 100 = 50%
New maximum:    200
New state: 100 / 200 = 50%
```

This proportional rescaling is a distinct health-state transition. It is not ordinary damage or healing and does not emit `DamageResolved` or `HealingApplied`. Future triggers may explicitly react to maximum-health or proportional-health rescaling facts.

A combatant at zero health remains at zero when maximum health changes. Maximum-health modification cannot revive an eliminated combatant.

Effective maximum health has a minimum valid value of 1 unless a future explicit mechanic introduces another rule. Ordered contributions are evaluated first, then the final compiled maximum is clamped to that minimum.

### Layered Maximum-Health State

A combatant needs several distinct maximum-health values:

- **Base maximum health:** the immutable class or character baseline before items and abilities.
- **Equipment maximum health:** the compiled result after ordered equipped-item contributions.
- **Effective maximum health:** the current result after runtime passive, triggered, active, area, and other ability/effect contributions.

These are derived checkpoints, not three independently mutable sources of truth:

```text
Base maximum health
    -> ordered equipment contributions
    = equipment maximum health
    -> ordered runtime ability/effect contributions
    = effective maximum health
```

The authoritative combatant owns the base value, installed contributions, compilation revisions, derived checkpoints, current health, and trigger state. Godot and network clients receive immutable health snapshots rather than separately calculating or mutating maximum health.

A snapshot should expose at least:

- Base maximum health.
- Equipment maximum health.
- Effective maximum health.
- Current health.
- Health/effect revision.
- Elimination state.

This supports player-facing stat breakdowns and prevents clients from disagreeing about which layer a threshold references.

### Explicit Health References

Conditions and modifiers must state which health reference they use. Examples include:

- Current health at or below 10% of **base maximum health**.
- Current health at or below 10% of **equipment maximum health**.
- Current health at or below 10% of **effective maximum health**.
- Missing health as a percentage of effective maximum health.
- Effective maximum health greater than a fixed value.

The denominator is part of the authored definition and item description. The engine must not assume that every phrase such as "10% health" means effective maximum health.

The earlier 10% example uses the immutable, unmodified class baseline. This is only one available authored reference; other effects may explicitly use equipment or effective maximum health.

### Triggered Maximum-Health Passives

A triggered passive may install, remove, or permanently disable a runtime maximum-health contribution.

Example: grow when critically wounded:

```text
Condition: Current health <= 10% of base maximum health
Re-entry: Once for the authored reset scope
Action: Install a runtime maximum-health contribution
Result: Recompile effective maximum health and preserve current-health percentage
```

Because the trigger is latched for its reset scope, proportional rescaling does not immediately undo the passive merely because current health rises above the original threshold. The player can subsequently heal toward the larger effective maximum.

Opposite example: lose giant form when critically wounded:

```text
Initial state: Runtime maximum-health contribution installed
Condition: Current health <= authored threshold
Re-entry: Once for the authored reset scope
Action: Remove or permanently disable that contribution
Result: Recompile effective maximum health and preserve current-health percentage
```

Runtime effects, temporary contributions, and trigger latches reset per life by default. An authored definition may explicitly choose another supported lifetime, such as once per round, until the source item is removed, or another future scope.

Equipment-passive contributions remain installed across lives because the item remains equipped. They compile into equipment maximum health before life-scoped runtime contributions are applied.

Maximum-health rescaling may itself cause other health conditions to become true. The rescaling emits a normal immutable effect fact into the explicit chain and activation-budget system; reactions do not execute through recursive property setters.

### Life Generations and Persistent World Objects

Spawning at round start and respawning after death both begin a new life generation. By default, life-scoped state resets cleanly:

- Attached runtime effects.
- Trigger latches and per-life activation counts.
- Cooldowns, charges, and item-owned deployment quotas.
- Other authored per-life resources.

Persistent world objects may outlive the life generation that created them when their definition allows it. Their old existence does not consume the new life's reset deployment quota.

Example: an item permits two active mines per life. A player places two mines, dies, and respawns. The original mines remain, while the new life receives a fresh quota of two and may place two additional mines. Every mine records the source combatant, item instance, and life-generation ID for ownership and diagnostics.

Round cleanup, item replacement, or another authored rule may remove persistent world objects separately. Per-life quota reset does not itself destroy them.

## Respawn Interaction

Elimination immediately ends the current life and clears player-bound per-life effects, including poison-like timed conditions. The later respawn begins a new life generation at full effective health. Cleanup does not wait through the respawn delay.

For example, death clears poison currently affecting the player, but it does not remove land mines already placed in the arena.

An active effect already attached to another player may continue after its source dies. Damage attribution retains the original source combatant and source life generation. Source-benefit reactions such as lifesteal require that recorded source life to remain active by default, preventing an old poison from healing a dead source or that source's later respawned life.

## Testing Requirements

Every implemented effect and damage rule should have focused automated tests. Important cases include:

- Flat resistance.
- Percentage resistance.
- Weakness through negative resistance.
- Resistance exactly at and above 100%.
- Type-specific damage converting into healing.
- General resistance clamping damage without healing.
- Interactions between flat and percentage modifiers.
- Damage-over-time ticks and expiration.
- Effects clearing on respawn.
- Persistent item-owned objects surviving another player's respawn.
- Extreme modifier values and host-configured match lengths.

## Open Questions

1. What gameplay identities distinguish Fire, Frost, Lightning, Poison, and Arcane beyond their resistance categories, if any?
2. What is the exact order for flat reduction, percentage reduction, weakness, and over-resistance healing?
3. Does over-resistance healing count as `HealingApplied`, or only as `HealthGained`, by default?
4. Should the minimum 1 damage rule apply before or after shields and other defensive layers?
5. Which effect tags are needed initially for item matching and triggers without creating an overly rigid taxonomy?

## Confirmed Effect Independence

Every application of a timed effect creates an independent runtime instance with its own schedule, duration, source, and state. Applying poison twice does not require either poison instance to know that the other exists. Both tick and expire independently unless a separate authored effect explicitly searches for or modifies matching active effects.
