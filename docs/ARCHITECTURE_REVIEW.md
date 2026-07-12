# Battle Arena — Effect and Item Architecture Review

Status: Initial static review  
Reviewed: July 11, 2026

## Executive Verdict

The current architecture contains a promising central idea: immutable-style effect definitions create independent runtime effect nodes, while ordered modifiers transform attack payloads. That idea fits the game.

The current C# implementation does not provide a reliable foundation for the full design. It is incomplete, contains several immediate correctness failures, couples domain rules too tightly to Godot nodes and global signals, and uses a JSON reflection layer that cannot safely support an external item creator in its present form.

Recommendation: preserve the design lessons and selected concepts, but restart the C# gameplay core in a small test-first vertical slice. Do not translate the GDScript file-for-file and do not expand the current C# hierarchy before correcting its boundaries.

## Grades

| Area | Grade | Assessment |
|---|---:|---|
| Core concept | B | Separating reusable effect definitions from runtime effect instances is sound. Container effects and ordered modifiers demonstrate useful composition. |
| GDScript prototype | C | It proves basic composition, periodic effects, payload modification, and item-owned Resources, but has lifecycle, timer, signal, and extensibility weaknesses. |
| Current C# implementation | D | It is incomplete and contains blocking correctness defects. Several types cannot safely execute or serialize. |
| Fit for the full game design | D+ | It can express a basic hit and interval wrapper, but not yet the required resistance pipeline, mixed damage, precise triggers, independent ownership, active selection, deterministic networking, or robust data loading. |
| Direction of the C# migration | B- | C# interfaces and testability are useful here, but only if the rewrite uses plain domain types at the core and Godot adapters at the edges. |

Overall current grade: **D+** for production readiness, with a **B-quality architectural seed** worth carrying into a clean implementation.

## What Is Worth Keeping

### Definition Versus Runtime Instance

`IEffect` acts like a reusable definition and `IEffectNode` acts like a live applied instance. This is the strongest part of the design. Independent poison applications naturally map to separate runtime instances with separate timers and state.

Keep the separation, but rename it more explicitly—for example `EffectDefinition` and `ActiveEffect`—and avoid requiring every runtime effect to be a Godot `Node`.

### Effect Composition

`IntervalEffect` wrapping another effect is a useful composition model. It shows how timing can be separated from the damage performed on each tick.

Keep composable definitions, but represent common timing, targeting, triggering, and delivery concepts as explicit data schemas rather than relying on arbitrary nested reflection.

### Payload Modification

Building a fresh attack payload and applying ordered modifiers is directionally correct. It supports several equipped items contributing effects to one attack.

Keep an ordered resolution pipeline, but make its phases explicit and deterministic. A five-value priority enum is not sufficient once modifiers need to add damage, convert types, amplify tagged effects, apply resistance, and react to resolved outcomes.

### Item Factory Boundary

The idea that item data is separate from a scene-instantiation factory is useful. Visual scenes and domain item definitions should remain separate.

Keep this boundary, but do not require every logical item to instantiate a unique scene or subclass. Many items can share presentation scenes and differ entirely through data.

## Critical Correctness Problems

### Recursive C# Properties

Several property getters and setters reference the property itself instead of a backing field:

- `DamageContext.DamageAmount`, `DamageType`, and `AttackerID`.
- `HealthResource.MaxHealth` and `CurrentHealth`.
- `HitContext.HitEntityID` and `AttackPayload` getters.

These cause infinite recursion and stack overflow when accessed. This is a migration error rather than a reason to reject the conceptual model, but it means the current C# runtime cannot be trusted.

### Uninitialized Collections

`AttackPayload.attackingEffects` is never initialized. Cloning or adding an effect can immediately throw a null-reference exception.

Public mutable fields also allow callers to bypass invariants. Payloads should expose initialized collections through controlled construction.

### Inverted Hit Null Check

`HittableComponent.GetModifiedValue()` reports that the hit is null when `baseMostRecentHit` is not null, then returns null. This prevents the normal hit path from resolving.

### Timer Configuration Is Ignored

Both interval implementations store a requested interval but do not assign it to the Godot `Timer.WaitTime`. The authored tick interval therefore does not control the timer.

The implementation also applies child effects before consistently placing their runtime nodes in the scene tree. One-shot effects may queue themselves for deletion before they have a valid lifecycle.

### Global Signal Wiring Is Broken or Fragile

`GlobalSignals.Instance` is declared but never assigned in the reviewed class. `HittableComponent` attempts to emit signal names belonging to `GlobalSignals` on itself. The GDScript version depends heavily on an autoload event bus.

A global bus makes ownership, cleanup, prediction, testing, and multiplayer authority difficult. Life steal should subscribe to scoped combat events for its owning entity or be evaluated in the authoritative combat pipeline.

### C# Item Runtime Is Not Godot-Ready

`Weapon` implements `IItem` but is not a `GodotObject` or `Node`. It contains animations that are never populated in its constructor or through exported properties, and `Reset.ResourceName` is accessed without ensuring `Reset` exists.

`Slot` requires constructor injection despite Godot normally instantiating scene nodes through parameterless construction. This can work only when every slot is created manually and is awkward for editor-authored scenes.

### Modifier Compatibility Is Not Enforced

Modifiers expose `ModifiableIsCompatible`, but `AddModifier` does not validate it. Incorrect modifiers can enter lists and fail later.

Modifiers are stored by object reference, so reliable removal, save/load, replication, and item replacement require ownership handles that do not currently exist.

### Deep Copy Semantics Are Unclear

The design depends on copying payloads and nested effects before mutation. GDScript uses `duplicate(true)` and C# mixes `Duplicate(true)` with manual copy methods. Interface-typed properties and non-exported C# properties are not guaranteed to duplicate as intended.

Mutable shared `DamageContext` instances can therefore leak attacker IDs or augmented damage between attacks or players.

## JSON and External Item Creator Assessment

### The Good Direction

A stable item format loaded at startup is the right choice for user-created content. An external creator can validate fields, preview descriptions, and export item packages without requiring users to write code.

### Why the Current Converter Should Be Replaced

The reflection-based `EffectConverter` has major problems:

- It discovers runtime types from class names, coupling saved data to C# implementation names.
- It uses `Activator.CreateInstance`, but several effect classes lack a parameterless constructor.
- It sets Godot properties through dynamic `GodotObject.Set`, while the reviewed C# properties are not consistently exported or Godot-bound.
- Nested interface values and container effects do not have a reliable recursive schema.
- The writer does not close the `params` and effect JSON objects.
- Unsupported values silently fall back to an empty string.
- There is no schema version, stable content ID, migration strategy, structured validation, or useful per-field error reporting.
- Runtime reflection registration makes compatibility and safe content loading harder to reason about.

### Recommended Data Pipeline

Use versioned JSON as the canonical distributable format, with plain C# data-transfer objects and an explicit registry of stable behavior IDs.

Example conceptual shape:

```json
{
  "schemaVersion": 1,
  "id": "base:sword_of_greater_poison",
  "name": "Sword of Greater Poison",
  "slot": "weapon",
  "rarity": "rare",
  "effects": [
    {
      "kind": "on_hit_apply_timed_effect",
      "effectId": "greater_poison",
      "durationSeconds": 10.0,
      "tickIntervalSeconds": 0.5,
      "tickEffects": [
        { "kind": "damage", "amount": 8.0, "damageTypes": ["poison"] }
      ]
    }
  ]
}
```

Each `kind` maps to a deliberately registered parser/factory. Do not persist CLR class names. Validate the complete file before creating runtime objects.

Godot `Resource` files can still be useful as editor-authored source assets or generated caches, but they should not be the public mod format if an external application must create content.

### Important Limitation

Data-driven content cannot perform literally arbitrary new behavior without executable mod code. It can create any combination supported by the game's effect vocabulary, conditions, selectors, triggers, and operations.

The goal should be a rich composable language that covers surprising combinations—not an unsafe promise that JSON can invent algorithms the game does not implement. Adding a new primitive behavior still requires game code; combining existing primitives should require only data.

## Recommended Target Architecture

### 1. Plain C# Domain Core

Use ordinary C# records/classes for:

- Item definitions and loaded content.
- Damage packets and typed damage portions.
- Effect specifications.
- Modifier specifications.
- Active-effect state.
- Combat events and resolved outcomes.

Keep these independent of `Node`, `Resource`, scene paths, and global autoloads wherever possible. This makes unit tests fast and deterministic.

### 2. Explicit Runtime Services

Introduce focused services:

- `ItemCatalog`: loads and indexes validated definitions by stable string ID.
- `ItemLoader`: parses versioned JSON and returns validation errors.
- `EffectFactory`: builds supported runtime effects from stable effect kinds.
- `EffectController`: owns active effects for one entity, ticks them, removes them on respawn, and provides ownership queries.
- `CombatResolver`: resolves attacks, typed damage, resistances, healing, and ordered triggers on the authority.
- `StatController`: calculates derived stats from owned modifier handles.
- `EquipmentController`: owns slots, equips/replaces items between rounds, and attaches/removes all item contributions atomically.
- `AbilityController`: routes fixed-slot inputs and selected flexible-slot activation.
- `SelectionService`: resolves card count, pick count, guarantees, rarity weighting, timer behavior, and public selection state from lobby rules plus equipped modifiers.

### 3. Godot Adapters

Use Godot nodes for:

- Physics detection and hitbox/hurtbox signals.
- Animation, audio, particles, and presentation.
- Input collection.
- Multiplayer RPC and synchronized snapshots.
- Scene instantiation for visible equipment and placed world objects.

These nodes call the domain services rather than owning the combat rules themselves.

### 4. Handles and Ownership

Every installed contribution should have an ownership handle containing at least the player, equipped item instance, effect source, and unique runtime instance ID.

When a chest plate is replaced between rounds, `EquipmentController` removes every modifier, trigger, ability, and visual contribution owned by that item. Existing enemy effects need not inspect other poison instances. Whether an already-applied effect retains a snapshot of source values should be defined by that effect's authored policy.

### 5. Deterministic Authority

The server or lobby host should authoritatively:

- Roll item offerings.
- Validate selections and equipment replacement.
- Resolve attack outcomes and random item effects.
- Assign effect instance IDs and tick schedules.
- Apply health changes and deaths.

Clients receive results and presentation data. Random effects require authority-owned seeded random streams or replicated outcomes; `System.Random` calls scattered across item nodes will not be sufficient.

### 6. Explicit Resolution Phases

Replace broad priority sorting with named phases, for example:

1. Build attack intent.
2. Add and transform damage portions.
3. Apply source amplifiers.
4. Apply target defenses and weaknesses.
5. Resolve damage or over-resistance healing.
6. Change health.
7. emit resolved events.
8. Queue triggered effects using recursion/loop guards.

Within a phase, deterministic ordering can still use priority and stable source IDs.

## Suggested Rewrite Sequence

1. Write plain C# tests for a damage packet containing Physical and Fire portions.
2. Implement flat, percentage, weakness, and over-100% type resistance resolution.
3. Define versioned item JSON DTOs and validation using two small authored items.
4. Equip and replace an item through ownership handles; prove all contributions are removed.
5. Implement independent poison instances with discrete schedules in a testable clock/tick service.
6. Add cross-item modification by tags, such as increased poison damage and tick frequency.
7. Add Godot hitbox and health adapters around the tested core.
8. Add authoritative multiplayer messages and deterministic random outcomes.
9. Add fixed and flexible active-item input routing.
10. Build the external item creator against the same schema and validation library.

## Final Recommendation

Do not delete the GDScript immediately. Treat it as a behavioral reference while building a small, tested C# replacement alongside it. Once each vertical slice works in C#, detach the corresponding GDScript scene and remove the superseded files.

Do restart the current C# gameplay architecture rather than repairing it in place. Some individual concepts and names can be reused, but trying to preserve the existing class graph will cost more than establishing clean domain, runtime, Godot-adapter, and content-schema boundaries now.

The project is early enough that this restart is inexpensive. The proposed game is unusually demanding of effect composition, ownership, deterministic ordering, validation, and networking; those foundations are worth getting right before producing a large item catalog.
