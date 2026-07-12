using BattleArena.Core.Common;

namespace BattleArena.Core.Combat;

public sealed record MaximumHealthContribution(
    ContributionId Id,
    InstallationSequence InstallationSequence,
    ValueOperation Operation);
