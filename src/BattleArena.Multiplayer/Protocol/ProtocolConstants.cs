namespace BattleArena.Multiplayer.Protocol;

public static class ProtocolConstants
{
    public const uint CurrentVersion = 8;
    public const int MaxPacketBytes = 64 * 1024;
    public const int MaxPredictionUnreliablePacketBytes = 1200;
    public const int MaxPredictionControlPacketBytes = 4 * 1024;
    public const int MaxInputFramesPerBatch = 8;
    public const int MaxAcceptedMovementCommandsPerBatch = MaxCombatants * 3;
    public const int MaxCombatants = 256;
    public const int MaxActiveEffects = 4096;
    public const int MaxWorldObjects = 4096;
    public const int MaxEventsPerBatch = 256;
    public const int MaxSessionPeers = 8;
    public const int MaxPredictionCommandsPerBundle = 3;
    public const int MaxPredictionRouteDescriptorBytes = 1024;
    public const int MaxMovementConfigurationUpdatesPerBatch = MaxCombatants;
    public const uint MaxAuthoredMovementCount = 16;
    public const float MaxPredictionCoordinateMagnitude = 100_000f;
    public const float MaxPredictionVelocityMagnitude = 1_000f;
    public const float MaxPredictionRollBoostDistance = 10_000f;
    public const ulong MaxPredictionTimerTicks = 216_000;
    public const int MaxDisplayNameCharacters = 32;
    public const int MaxDefinitionIdCharacters = 128;
    public const int ClientNonceBytes = 16;
    public const int ReconnectTokenBytes = 32;
    public const int PredictionRouteCredentialBytes = 32;
    public const int PredictionHandshakeNonceBytes = 32;
    public const int PredictionHandshakeProofBytes = 32;
    public const uint EquipmentSlotCount = 6;
    public const uint KnownInputButtonMask = 0x3ff;
    public const uint KnownPredictionMovementButtonMask = 0x7;
    public const uint KnownOwnerHeldMovementMask = 0x7;
    public const uint KnownOwnerHeldCombatMask = 0x1ff;
    public const int MaxOwnerCommandsPerBatch =
        OwnerPrediction.OwnerInputSendWindowLimits.MaximumCommandsPerBatch;
    public const int MaxOwnerJournalEntriesPerBatch =
        OwnerPrediction.OwnerInputSendWindowLimits.MaximumJournalEntriesPerBatch;
    public const int MaxOwnerTransitionReferencesPerCommand =
        BattleArena.Core.Movement.Simulation.OwnerSimulationLimits
            .MaximumTransitionReferences;
    public const int MaxOwnerActionReferencesPerCommand =
        BattleArena.Core.Movement.Simulation.OwnerSimulationLimits
            .MaximumActionReferences;
    public const int MaxOwnerRecentInputDispositions = 64;
    public const ulong MaxOwnerIntentValidityTicks =
        (ulong)OwnerPrediction.MovementTransitionJournalLimits.MaximumValidityTicks;
    public const uint MaxOwnerPredictionLeadFrames =
        OwnerPrediction.PredictionLeadLimits.MaximumSupportedFrames;
    public const ulong MaxOwnerBootstrapNeutralPrerollFrames =
        OwnerPrediction.OwnerPredictionBootstrapLimits.MaximumNeutralPrerollFrames;
    public const ulong MaxOwnerBootstrapEnableNoticeFrames =
        OwnerPrediction.OwnerPredictionBootstrapLimits.MaximumEnableNoticeFrames;
    public const int OwnerCollisionContentHashBytes = 32;
    public const int OwnerAxisQ15Magnitude = 32_767;
    public const uint OwnerViewYawU16Maximum = ushort.MaxValue;
    public const int OwnerViewPitchI16Magnitude = 32_767;
    // Conservative Steam/IPv6 owner-command body allowance from P01/P03 sizing:
    // 1,200-byte ceiling - 41 future-envelope bytes - 112 authentication,
    // Steam framing, and IPv6/UDP bytes.
    public const int MaxOwnerCommandDraftBodyBytes = 1_047;
}
