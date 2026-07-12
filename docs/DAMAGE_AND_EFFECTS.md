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

The system must specify how duration and interval changes affect an effect already attached to a player, including whether the next scheduled tick moves, how duration extensions are applied, and whether temporary modifications revert when their source stops affecting the poison.

## Healing Events

The system must distinguish between an attempted healing effect and health actually restored:

- `HealingApplied` represents a healing effect being processed, even if resistance, full health, or another rule changes its final result.
- `HealthGained` represents the resolved increase in current health.

Item descriptions and trigger definitions should say which event they use. "When a healing effect is applied, gain attack speed" can trigger without actual health gain, while "after gaining X health within one second, gain attack speed" depends on resolved health changes.

Healing created by over-resistance counts as actual health gained and can contribute to `HealthGained` triggers. Whether it also counts as a healing effect for `HealingApplied` triggers should be explicitly chosen by the authored effect or event tags rather than assumed globally.

Sliding-window triggers such as "gain X health within the last second" should aggregate timestamped resolved health changes. Healing-reduction effects naturally influence these triggers because they change the amount of health actually gained.

## Respawn Interaction

Player-bound temporary effects, including poison-like timed conditions, clear when the affected player respawns. Persistent world objects remain according to their source item's rules.

For example, death clears poison currently affecting the player, but it does not remove land mines already placed in the arena.

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
6. How should temporary in-round modifications affect an already-running timed effect: snapshot its values at application or evaluate them again on later ticks?

## Confirmed Effect Independence

Every application of a timed effect creates an independent runtime instance with its own schedule, duration, source, and state. Applying poison twice does not require either poison instance to know that the other exists. Both tick and expire independently unless a separate authored effect explicitly searches for or modifies matching active effects.
