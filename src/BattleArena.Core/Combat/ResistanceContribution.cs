using BattleArena.Core.Common;

namespace BattleArena.Core.Combat;

public sealed record ResistanceContribution(
    ContributionId Id,
    InstallationSequence InstallationSequence,
    ResistanceStatKey Stat,
    ValueOperation Operation);
