# Battle Arena — Item System Design

Status: Early design / living document  
Last updated: July 11, 2026

## Purpose

Items are the primary source of character growth, abilities, mechanical variety, and match-to-match stories in *Battle Arena*. This document defines the intended item philosophy, equipment structure, selection process, and unresolved progression systems.

The item system should be implemented as data-driven and extensible. Nearly any player statistic, action, ability, or rule may eventually be modified by an item.

## Core Philosophy

### Items Define the Build

The base Fighter has a deliberately limited kit. Items provide much of the player's complexity through stat changes, passive effects, toggled behavior, active abilities, movement changes, attacks, traps, and interactions with other items.

### Power Is Uneven

Items have rarities, and some items are intentionally stronger than others. The game is a social party game rather than a ranked competitive experience, so lucky and unlucky offerings are acceptable parts of the match story.

Stronger does not always mean better for the current player. An item can conflict with the existing build, while a lower-rarity item can complete a powerful synergy.

### Power Has a Price

Most items should contain at least one positive effect and one negative effect. The player chooses a package of tradeoffs rather than receiving an unconditional upgrade.

Good drawbacks create decisions and new build opportunities. A negative effect should sometimes be exploitable as an advantage. For example, an item that rewards missing health may synergize with another item that reduces maximum health or causes self-damage.

### Interactions Matter

Items should compound, synergize, and anti-synergize. The design target is not a collection of isolated bonuses; it is a system in which combinations produce distinct builds and surprising strategies.

## Equipment Layout

### Fixed Slots

The initial build has one of each fixed slot:

1. Weapon.
2. Helmet.
3. Chest armor.
4. Gloves.
5. Boots.
6. Amulet.

A fixed-slot item replaces the existing item in that slot. The replaced item is removed from the build and cannot be recovered under the base rules.

Fixed gear should visibly change the character model where practical. Visual customization is desirable, but clear mechanics, effects, and abilities take priority during development.

Equipment slots provide identity, replacement rules, and loose design themes; they do not grant innate mechanics. A chest-armor item does not automatically reduce damage, and a weapon does not automatically add a particular attack effect. Every gameplay behavior comes from the item's authored effects. Conventional patterns—such as plate armor commonly granting physical protection—are guidelines rather than hard rules.

### Flexible Random Slots

The default build has four flexible slots. The lobby host may configure this number.

Any item categorized for a flexible slot may occupy one of these positions. Duplicate flexible items can occupy multiple slots and their effects stack additively. Two identical items normally produce twice the effect while consuming two slots.

### Full Builds

With default settings, a full build contains ten items. Losing players continue receiving an item selection after the build is full; they can replace an existing fixed or flexible item to change direction or improve synergy.

## Effects and Modifiers

An item can modify almost any gameplay property, including:

- Maximum or current health.
- Damage, range, area, knockback, and attack behavior.
- Attack speed and recovery time.
- Movement, sprint, jump, roll, and aerial control.
- Healing, life steal, damage-over-time effects, and status conditions.
- Cooldowns, charges, ammunition, or resource costs.
- Selection-phase rules such as offering size and number of items chosen.
- Existing abilities or entirely new abilities.
- Traps, summons, projectiles, or persistent objects.
- Triggers such as taking damage, attacking, moving, dying, or respawning.
- Rules belonging to other equipped items.

Effects should be represented as explicit positive and negative modifications so the selection interface can clearly communicate the tradeoff.

### Authored Behavior with Optional Variation

Items are primarily authored designs rather than bundles of automatically generated stats. Designers should be able to specify exact values and behaviors while retaining the option to use ranges, probabilities, conditional branches, and randomized outcomes.

For example, a deliberately unreliable weapon such as a hypothetical "Sword of the Clover Leaf" might usually deal 5 damage but have a very small chance to deal 600 damage. This randomness belongs to that authored item's identity rather than being a universal random-stat system.

The effect architecture should support, without requiring special changes to the player controller for every new item:

- Immediate damage and healing.
- Damage or healing over time.
- Poison and other named status effects.
- Typed damage and resistance to particular damage types.
- Fixed values, value ranges, and probability rolls.
- Conditional effects based on game and player state.
- Periodic, event-triggered, toggled, and manually activated behavior.
- Multiple outcomes with item-specific selection rules.
- Custom combinations of positive and negative effects.

Damage modifiers may use flat values or percentages on an item-by-item basis. Items may grant protection against one damage type, several types, or all damage. They may also create negative resistance described to players as a weakness.

Type-specific resistance may exceed 100%. This can turn incoming damage of that type into healing, enabling intentionally extreme specialization and counterplay. General all-damage resistance should not convert damage into healing; its initial rule should clamp a successful hit to at least 1 damage unless a specific item explicitly overrides that behavior.

Flexibility is a primary technical requirement. The implementation can begin with a small tested subset, but its boundaries should not assume that all future items are simple stat modifiers.

Effects and modifications should remain distinct. Effects perform behavior; modifications change effect properties or rules. An item may modify effects created by other items—for example, chest armor can enhance the damage, duration, or tick frequency of poison effects created by an equipped weapon. Cross-item modification is a primary mechanism for synergy.

## Active and Passive Abilities

An item may provide:

- Passive modifiers that are always active.
- Conditional passive effects triggered by an event.
- A toggled state.
- An actively triggered ability.
- A combination of these forms.

Each of the six fixed equipment slots has a corresponding ability input. If the equipped item has an active or toggled ability, that input activates it. If the item is entirely passive, the input has no action.

Flexible-slot items may also grant active abilities, but they do not each receive a dedicated input. The player cycles through their flexible items to select one, then uses a separate input to activate the currently selected item. This input model is one of the tradeoffs of flexible slots and continues to work when the host changes their number.

The interface must clearly show the selected flexible item, whether it has an active action, its cooldown or charges, and enough neighboring context that cycling does not become confusing during combat.

## Item-Owned Rules

Each item defines the limits and lifecycle of the mechanic it creates. Limits should not be generalized more than necessary.

For example:

- A powerful mine item might permit only two active mines.
- A weaker mine item might permit ten active mines.
- Equipping both retains two separately tracked mine families with their respective limits.
- One summoned object might expire on death, while another persists until destroyed.

The engine may still enforce technical safety ceilings, but these should be high-level safeguards rather than balance rules shared by every item.

## Selection Offering

Every player who loses a round receives exactly one item selection.

Without item modifiers, the base offering contains five item cards generated independently for that player, and the player chooses exactly one:

- If the player has any empty slot, at least one choice is guaranteed to fit an empty slot.
- The other four choices are unrestricted random results.
- If no slots are empty, all choices are unrestricted and the player may need to replace an equipped item.
- Previously seen and currently equipped items may appear again.
- Rarity affects potential strength and will likely affect appearance frequency, though exact weights are undecided.

All players see the complete offering. They may discuss, recommend, or tease before the active player makes the final decision.

### Selection-Phase Modifiers

Equipped items may modify the selection system itself. Selection is another effect target alongside combat, movement, health, and abilities.

Examples include:

- `+1 card offered`: the player sees six cards instead of five.
- `+1 item selected`: the player chooses two cards from the offering instead of one.
- Future authored effects may influence rerolls, rarity weights, guaranteed categories, selection order, or other selection rules.

These effects are item-owned and must be stated explicitly in the item description. They may include tradeoffs; for example, an item might offer more cards but reduce the chance of high-rarity results.

The selection system calculates each player's offering and pick count from base lobby rules plus equipped modifiers. The user interface, item generator, and network messages must not assume that five cards and one pick are permanent constants.

Each allowed pick is a separate selection cycle:

1. Generate a new offering using the player's current modified card count.
2. Display the complete offering publicly.
3. The player selects exactly one card.
4. Equip or replace the selected item and discard the rest of that offering.
5. Re-evaluate whether the player still has another pick.
6. If another pick remains, generate an entirely new randomized offering and repeat.

An extra-pick entitlement exists only while its source item remains equipped. If the first pick replaces the item granting a second pick, the unspent second pick is immediately removed and no new offering is generated for it.

If a newly selected item grants an additional pick, the new entitlement applies immediately. After equipping it, the system re-evaluates the player's allowance and generates another offering if a pick remains. Successive selections may therefore chain into further picks when the chosen items support that interaction.

### Optional Selection Timer

Selection time is unlimited by default to support conversation and the relaxed party-game experience. The lobby host may enable a timer and configure its duration when a faster match is desired.

If time expires, the game randomly selects one card from the current offering. The item is equipped using the normal rules and may replace an existing item. Automatic selection does not prefer an empty slot, even when another displayed card was guaranteed to fit an empty slot. The game then re-evaluates extra-pick entitlements and, if another pick remains, generates a fresh offering for the next selection cycle.

The configured timer restarts at its full duration for every newly generated offering. It does not cover the player's entire multi-pick sequence with one shared countdown.

## Selection Order

Losing players choose sequentially from the strongest-performing loser to the weakest-performing loser:

1. The last player eliminated chooses first.
2. Selection proceeds backward through the elimination order.
3. The first player eliminated chooses last.

This deliberately gives the weakest-performing player the strongest counter-pick opportunity. They see every other loser's offering and final selection before choosing their own item. A randomized selection order may be offered as an optional lobby rule.

## Rarity

Items use the familiar working rarity tiers Common, Uncommon, Rare, Epic, and Legendary. The names are intentionally conventional and immediately understandable, though they can be renamed later if the game develops a stronger thematic vocabulary. Exact probabilities and power expectations are not yet defined.

Rarity guidelines:

- Higher rarity can mean greater raw power, more unusual mechanics, or more complex combinations of effects.
- A high-rarity item is allowed to be strictly stronger in isolation.
- Downsides may scale alongside benefits.
- A lower-rarity item can remain the correct choice because of synergy or because it avoids a harmful interaction.
- Rarity should influence excitement without making the player stop evaluating the actual effects.

## Synergy and Anti-Synergy Examples

These examples demonstrate the philosophy and are not confirmed content:

- High attack speed plus life steal creates rapid sustain.
- Bonus power at low health turns reduced maximum health into a potential advantage.
- Increased jump height plus an aerial slam creates a dive build.
- A weapon that deals extreme damage but attacks slowly conflicts with effects triggered by frequent hits.
- Self-damage can activate effects that trigger when hurt.
- Many weak mines and a few powerful mines can create a layered trap build.
- Reduced movement speed may benefit an effect that scales while standing still.

## Long-Match Progression

Host-configurable match rules may produce matches lasting twenty or more rounds. A default ten-slot build can fill before such a match ends, so replacement may eventually need a supplemental progression system.

### Duplicate Upgrade Concept

Selecting another copy of an equipped item could combine it into the existing item and increase the magnitude of its effects. An initial example is 1.5 times the original strength per combination rather than doubling it. Both positive and negative effects become stronger, preserving and intensifying the item's tradeoff.

Questions include whether downsides also become stronger, whether upgrades can repeat indefinitely, and how upgraded items are represented visually.

### Risky Forge Concept

A forge could combine two items belonging to the same fixed slot type into a randomized hybrid. The result could inherit a mixture of positive and negative effects from both inputs, producing either an exceptional item or a poor combination.

The forge is intended to be risky, social, and capable of creating memorable overpowered results. It is not part of the first implementation.

## Initial Implementation Boundary

The first implementation should prove:

- Data-defined items and rarities.
- Six fixed slots and four flexible slots.
- Positive and negative stat modifiers.
- Passive effects and at least one active item ability.
- Flexible-item cycling and activation.
- Duplicate stacking in flexible slots.
- Five-card generation, exactly one selection, and the empty-slot guarantee.
- Selection modifiers that change the base offering size and pick count.
- Public sequential selection and replacement of equipped items.
- At least three deliberate two-item synergies or anti-synergies.
- Automated tests for each implemented effect type and its important interactions.

Forging, duplicate upgrades, visible equipment for every slot, and exhaustive item variety can follow after the base loop works.

The order in which individual effect types are implemented is less important than maintaining an architecture that anticipates varied authored behavior. Each added capability should be proven with focused tests before more complex combinations depend on it.

## Open Questions

1. What are the selection probabilities for Common, Uncommon, Rare, Epic, and Legendary items?
2. Should poison be modeled as a damage type, a timed status that deals poison-type damage, or both?
3. Which other status effects are separate named systems rather than instances of generic damage over time or stat modification?
4. Should passive-only fixed items leave their ability button unused or provide a standard slot action?
5. Do fixed-slot duplicates have any purpose before an upgrade or forge system exists?
6. Can upgraded items be replaced normally, and is the investment entirely lost when replaced?
7. Is randomized selection order worth including as a lobby option?
8. Can players review all revealed opponent builds during combat or only between rounds?
9. Which initial items will prove that the architecture supports genuinely unusual effects?
10. What high technical safety ceiling should stop a pathological or intentionally constructed infinite extra-pick chain?
