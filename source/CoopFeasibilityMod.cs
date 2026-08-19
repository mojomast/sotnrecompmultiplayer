using System;
using ImGuiNET;
using Recompiled;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Silk.NET.Input;
using Sotn;
using GameButton = Sotn.Button;

namespace CoopFeasibilityMod;

public sealed class CoopFeasibility : IMod
{
    private const string Version = "0.4.0";
    private const uint ExpectedCollisionFunction = 0x800EF45C;
    private const uint AssignAttackerIdSlot = 0x8003C804;
    private const uint ExpectedAssignAttackerId = 0x80118894;
    private const uint ExpectedDealDamage = 0x800FF128;
    private const uint ExpectedEnemyDefinitions = 0x800A8900;
    private const uint ExpectedGetEquipProperties = 0x800FE728;
    private const uint ExpectedCalcAttack = 0x800F4D38;
    private const uint EquipmentDefinitionsAddress = 0x800A4B04;
    private const uint EntityNullAddress = 0x8011A4C8;
    private const uint AttackMarker = 0x50324B43; // "CK2P"; intentionally lives only in our owned transient.
    private const int AttackSlotStart = 17;
    private const int AttackSlotEnd = 48;
    private const ushort AttackEntityId = 0x003E;

    private const uint GameStepAddress = 0x80073060;
    private const uint EngineStepAddress = 0x8003C9A4;
    private const uint CutsceneControlAddress = 0x8003C704;
    private const uint SpecialTransitionAddress = 0x80097C98;
    private const uint RoomLeftAddress = 0x800730B0;
    private const uint RoomTopAddress = 0x800730B4;
    private const uint RoomRightAddress = 0x800730B8;
    private const uint RoomBottomAddress = 0x800730BC;
    private const uint TilemapAddress = 0x80073084;
    private const uint TileDefinitionsAddress = 0x80073088;
    private const uint ScrollXAddress = 0x8007308C;
    private const uint ScrollYAddress = 0x80073090;
    private const uint PlayerWorldXAddress = 0x800973F0;
    private const uint PlayerWorldYAddress = 0x800973F4;

    private const uint CurrentBufferPointer = 0x8006C37C;
    private const uint GpuBuffersAddress = 0x8003CB08;
    private const uint GpuBufferStride = 0x177F4;
    private const uint BackbufferXAddress = 0x8006C39C;
    private const uint BackbufferYAddress = 0x8006C3A0;
    private const uint TimerAddress = 0x8003C998;
    private const uint GpuUsageGt4Address = 0x80097930;
    private const uint OrderingTableOffset = 0x474;
    private const uint GpuGt4Offset = 0x3C74;
    private const uint GpuGt4Stride = 0x34;
    private const uint MaxGpuGt4 = 0x300;
    private const uint AlucardFrameTableAddress = 0x800CF324;
    private const uint AlucardDescriptorBase = 0x800CF740;
    private const uint AlucardSpriteTableAddress = 0x8013C020;
    private const uint PlayerDrawAddress = 0x80097D1C;
    private const uint AlucardClutAddress = 0x8003C304;
    private const int AlucardFrameCount = 263;
    private const int AlucardSpriteCount = 218;
    private const uint AlucardDescriptorEnd = 0x800CFE40;
    private const int IndependentPoseCount = 43;

    private const uint PlayerEntityAddress = 0x800733D8;
    private const uint EntityHitboxOffsetX = 0x10;
    private const uint EntityHitboxOffsetY = 0x12;
    private const uint EntityFacingOffset = 0x14;
    private const uint EntityDrawFlagsOffset = 0x19;
    private const uint EntityIdOffset = 0x26;
    private const uint EntityUpdateOffset = 0x28;
    private const uint EntityFlagsOffset = 0x34;
    private const uint EntityEnemyIdOffset = 0x3A;
    private const uint EntityHitboxStateOffset = 0x3C;
    private const uint EntityAttackOffset = 0x40;
    private const uint EntityAttackElementOffset = 0x42;
    private const uint EntityHitboxWidthOffset = 0x46;
    private const uint EntityHitboxHeightOffset = 0x47;
    private const uint EntityAnimSetOffset = 0x54;
    private const uint EntityAnimFrameOffset = 0x56;
    private const uint EntityPlayerDrawOffset = 0x5A;
    private const byte EntityBlink = 0x80;
    private const uint EntityDead = 0x100;
    private const int ContactSlotStart = 64;
    private const int ContactSlotCount = 128;

    private const uint ProtectedPlayerRuntimeAddress = 0x80072BD0;
    private const int ProtectedPlayerRuntimeLength = 0x03CE;
    private const uint ProtectedStatusAddress = 0x80097964;
    private const int ProtectedStatusLength = 0x0334;
    private const uint ProtectedCastleFlagsAddress = 0x8003BDEC;
    private const int ProtectedCastleFlagsLength = 0x0300;
    private const uint ProtectedCastleMapAddress = 0x8006BB74;
    private const int ProtectedCastleMapLength = 0x0800;
    private const uint ProtectedSaveWorkspaceAddress = 0x801EA000;
    private const int ProtectedSaveWorkspaceLength = 0x11CC;

    private const int FixedOne = 0x10000;
    private const int RunSpeed = 0x18000;
    private const int Gravity = 0x04000;
    private const int JumpSpeed = -0x48000;
    private const int MaxFallSpeed = 0x40000;
    private const int HalfWidth = 7;
    private const int StandingHeadOffset = -22;
    private const int CrouchedHeadOffset = 0;
    private const int FootOffset = 25;
    private const int ColliderSize = 0x24;
    private const int CollisionStackDepth = 0xD0;
    private const int ReconstructionStableUpdates = 3;
    private const int MinimumPlayerSeparation = 24;
    private const int ProjectileLifetimeUpdates = 40;
    private const int ProjectileSpeed = 4 * FixedOne;
    private const int ProjectileMaximumRange = 160;
    private const int AwarenessHysteresisSquared = 64;
    private const int ManagedMaxHp = ManagedHealthMachine.MaximumHp;
    private const int DamageInvulnerabilityUpdates = ManagedHealthMachine.DamageInvulnerabilityUpdates;
    private const int HurtLockUpdates = ManagedHealthMachine.HurtLockUpdates;
    private const int ReviveUpdates = ManagedHealthMachine.ReviveUpdates;
    private const uint EffectSolid = 0x0001;
    private const uint EffectSolidFromAbove = 0x0040;
    private const uint EffectSolidFromBelow = 0x0080;
    private const uint EffectSlopeMask = 0xF800;

    private const ushort RequiredHostButtons =
        Controller.Up | Controller.Right | Controller.Down | Controller.Left |
        Controller.Cross | Controller.Circle | Controller.Start;
    private const ushort RequiredGameButtons =
        (ushort)(GameButton.Up | GameButton.Right | GameButton.Down | GameButton.Left |
                 GameButton.Cross | GameButton.Circle | GameButton.Start);

    private static CoopFeasibility? _instance;

    private bool _enabled = true;
    private bool _virtualKeyboard = true;
    private bool _physicalControllerTest;
    private bool _visualConfirmed;
    private bool _fatal;
    private string _firstError = "none";
    private bool _safeFrame;
    private string _safeCode = "WAIT";
    private string _safeReason = "Waiting for gameplay";
    private string _operationStatus = "Waiting for gameplay";
    private int _diagnosticGeneration;
    private readonly string _diagnosticSessionId = Guid.NewGuid().ToString("N");
    private long _diagnosticFrame;
    private int _proxyResetRequests => _movementSession.State.ManualResetRequests;
    private int _proxyResetCompletions => _movementSession.State.ManualResetCompletions;

    private long _vsyncCalls;
    private long _mainEngineCalls;
    private long _updateCalls;
    private long _renderCalls;
    private long _pad2Reads;

    private ushort _hostState = 0xFFFF;
    private ushort _padState = 0xFFFF;
    private ushort _gamePressed;
    private ushort _gameTapped;
    private ushort _hostSeen;
    private ushort _padSeen;
    private ushort _gameSeen;
    private ushort _tapSeen;
    private bool _neutralSeen;
    private bool _previousConnected;
    private int _connectionChanges;
    private readonly Pad2SourceAvailability _pad2Source = new();
    private byte _leftXMin = 0xFF;
    private byte _leftXMax;
    private byte _leftYMin = 0xFF;
    private byte _leftYMax;
    private byte _rightXMin = 0xFF;
    private byte _rightXMax;
    private byte _rightYMin = 0xFF;
    private byte _rightYMax;
    private ushort _virtualPressed;
    private ushort _virtualPreviousHeld;
    private ushort _virtualRawHeld;
    private ushort _virtualRawSeen;
    private ushort _virtualDownSeen;
    private ushort _virtualUpSeen;
    private bool _virtualNeutralSeen;
    private bool _virtualNeutralObserved;
    private int _virtualSuppressionFrames;
    private int _virtualForcedReleases;
    private AutoTestState _autoTestState;
    private int _autoTestFrame;
    private int _autoTestRuns;
    private string _autoTestStatus = "idle";

    private bool _proxyInitialized => _movementSession.State.ProxyInitialized;
    private int _proxyX;
    private int _proxyY;
    private int _velocityX;
    private int _velocityY;
    private bool _grounded;
    private bool _facingLeft;
    private long _leftDistanceRaw;
    private long _rightDistanceRaw;
    private bool _jumpObserved;
    private bool _jumpPending;
    private int _jumpStartY;
    private bool _collisionThisFrame;
    private bool _reinitializeRequested => _movementSession.State.ManualResetPending;
    private readonly ManagedLocomotionReducer _locomotionState = new();
    private ManagedLocomotion _locomotion => _locomotionState.State.Locomotion;
    private ManagedAnimation _animation => _locomotionState.State.Animation;
    private int _animationFrame => _locomotionState.State.Frame;
    private int _animationTick => _locomotionState.State.Tick;
    private int _animationTransitions => _locomotionState.State.Transitions;
    private long _animationAdvances => _locomotionState.State.Advances;
    private int _animationStatesSeen => _locomotionState.State.StatesSeen;
    private int _animationAdvanceStatesSeen => _locomotionState.State.AdvanceStatesSeen;
    private bool _animationStateValid => _locomotionState.State.Valid;
    private long _ownedHurtboxSamples;
    private int _ownedHurtboxStatesSeen;
    private ulong _visualPosesSeen;
    private ulong _hurtboxPosesSeen;
    private long _independentVisualEligible;
    private long _independentVisualSubmitted;
    private int _independentRestoreChecks;
    private int _independentRestoreFailures;
    private int _independentVisualFailures;
    private bool _independentVisualDisabled;
    private bool _poseTableValidated;
    private ushort _independentNativeFrame;
    private string _independentVisualStatus = "WAIT";
    private readonly JumpForgivenessReducer _jumpForgiveness = new();
    private int _coyoteUpdates => _jumpForgiveness.CoyoteUpdates;
    private int _jumpBufferUpdates => _jumpForgiveness.BufferUpdates;
    private int _normalJumps;
    private int _coyoteJumps;
    private int _bufferedJumps;
    private readonly ManagedStanceReducer _stance = new();
    private readonly ReconstructionPolicyReducer _reconstructionPolicy = new();
    private bool _crouched => _stance.Crouched;
    private bool _standBlocked => _stance.StandBlocked;
    private bool _landedThisUpdate;
    private bool _horizontalCommandThisUpdate;
    private int _reconstructionSafeFrames => _movementSession.State.SafeUpdates;
    private int _reconstructionAttempts => _movementSession.State.ReconstructionAttempts;
    private int _reconstructionSuccesses => _movementSession.State.ReconstructionSuccesses;
    private int _reconstructionFailures => _movementSession.State.ReconstructionFailures;
    private int _tetherRecoveries => _movementSession.State.TetherRecoveries;
    private bool _reconstructionHardFailure => _movementSession.State.ReconstructionHardFailure;
    private string _reconstructionStatus = "WAIT";

    private int _attackTimer;
    private bool _attackSpawnPending;
    private bool _outgoingAttackDisabled;
    private bool _attackHardFailure;
    private AttackLeaseState _attackLease = AttackLeaseMachine.Initial();
    private AttackPublicationState _attackPublication = AttackPublicationPolicy.Initial();
    private readonly NativeAttackPublicationAdapter _attackPublicationAdapter;
    private ref int _ownedAttackSlot => ref _attackLease.OwnedSlot;
    private ref uint _ownedAttackGeneration => ref _attackLease.OwnedGeneration;
    private ref uint _ownedAttackRoomHash => ref _attackLease.OwnedRoomHash;
    private ref int _attackQuarantineSlot => ref _attackLease.QuarantineSlot;
    private ref uint _attackQuarantineGeneration => ref _attackLease.QuarantineGeneration;
    private ref uint _attackQuarantineRoomHash => ref _attackLease.QuarantineRoomHash;
    private long _attackAllocations;
    private long _attackNormalEngineWindows;
    private long _attackCleanups;
    private long _attackLifecycleCancellations;
    private long _attackFailures;
    private int _attackLastAttackerId;
    private bool _attackAttackerIdValid;
    private int _attackHitFlagObservations;
    private int _attackCooldownObservations;
    private int _attackTargetHpChanges;
    private readonly AttackTargetCaptureState _attackTargets = new();
    private int _attackTargetCount => _attackTargets.Count;
    private bool _attackWindowObserved;
    private ref bool _attackCleanupPending => ref _attackLease.CleanupPending;
    private ref bool _attackQuarantineMutationStopped => ref _attackLease.MutationStopped;
    private long _attackArmMainGeneration;
    private long _attackArmUpdateGeneration;
    private long _attackObservedMainGeneration;
    private int _attackTimingFailures;
    private int _attackCausalResults;
    private int _attackPhaseCompletionMask => _locomotionState.State.AttackPhaseCompletionMask;
    private string _attackStatus = "IDLE";
    private AttackProfile _latchedAttackProfile;
    private bool _attackProfileLatched;
    private AttackKind _pendingAttackKind;
    private int _projectileX;
    private int _projectileY;
    private int _projectileOriginX;
    private int _projectileLifetime;
    private long _projectileWindows;
    private int _profileExtractions;
    private int _profileExtractionFailures;
    private int _equipmentRestoreChecks;
    private int _equipmentRestoreFailures;

    private long _enemyDiagnosticScans;
    private long _enemyNativeCandidateSamples;
    private long _enemyCompatibleCandidateSamples;
    private int _nearestTargetSlot = -1;
    private ushort _nearestTargetEntityId;
    private ushort _nearestTargetEnemyId;
    private short _nearestTargetHp;
    private int _nearestTargetP1Distance;
    private int _nearestTargetP2Distance;
    private bool _nearestTargetCompatible;
    private int _nativeTargetHits;
    private int _defeatedTargets;
    private int _compatibleZeroHpHits;
    private string _enemyDiagnosticStatus = "WAIT";

    private bool _awarenessDisabled;
    private long _awarenessCalls;
    private long _awarenessOverrides;
    private int _awarenessChosenSlot = -1;
    private string _awarenessStatus = "WAIT";

    private long _renderEligible;
    private long _renderSubmitted;
    private long _drawOtCalls;
    private bool _nativeCapturePending;
    private bool _nativeSpriteDrawnThisFrame;
    private uint _nativeCaptureBuffer;
    private uint _nativeCaptureGt4;
    private int _nativeCaptureAnchorX;
    private int _nativeCaptureAnchorY;
    private bool _nativeCaptureFacingLeft;
    private long _nativeSpriteEligible;
    private long _nativeSpriteCaptured;
    private long _nativeSpriteSubmitted;
    private long _nativeSpriteFlipped;
    private long _nativeSpriteFallbacks;
    private int _nativeSpriteStreak;
    private bool _nativeSpriteFlipSeenInStreak;
    private string _nativeSpriteStatus = "WAIT";
    private long _hudEligible;
    private long _hudSubmitted;

    private readonly ContactObservation[] _contactObservations = new ContactObservation[ContactSlotCount];
    private readonly ContactOpportunityMachine _contactOpportunities = new();
    private bool _contactDisabled;
    private bool _contactVisualConfirmed;
    private int _contactGuardChecks;
    private int _contactGuardFailures;
    private int _contactLastSlot = -1;
    private ushort _contactLastEntityId;
    private int _contactOffsetX;
    private int _contactOffsetY;
    private int _contactHalfWidth;
    private int _contactHalfHeight;
    private int _contactShapeCenterX;
    private string _contactGuardRegion = "none";
    private string _contactStatus = "WAIT";
    private ManagedHealthState _health = ManagedHealthMachine.Reset();
    private ref int _managedHp => ref _health.Hp;
    private ref int _damageInvulnerability => ref _health.Invulnerability;
    private ref int _hurtLock => ref _health.HurtLock;
    private ref bool _downed => ref _health.Downed;
    private ref int _damageEvents => ref _health.DamageEvents;
    private ref int _damageConsumed => ref _health.DamageConsumed;
    private ref int _damageSuppressedInvul => ref _health.SuppressedInvulnerability;
    private ref int _damageSuppressedHitInvul => ref _health.SuppressedHitInvulnerability;
    private ref bool _hitInvulnerabilityActive => ref _health.HitInvulnerabilityActive;
    private ref int _downedCount => ref _health.DownedCount;
    private ref int _reviveStarts => ref _health.ReviveStarts;
    private ref int _reviveCancels => ref _health.ReviveCancels;
    private ref int _reviveRecoveries => ref _health.ReviveRecoveries;
    private ref int _healthInvariantFailures => ref _health.InvariantFailures;
    private ref bool _compactHurt => ref _health.CompactHurt;
    private ref int _lastDamage => ref _health.LastDamage;
    private ref int _lastDamageSlot => ref _health.LastDamageSlot;
    private ref ushort _lastDamageElement => ref _health.LastDamageElement;
    private ref int _reviveProgress => ref _health.ReviveProgress;
    private ref int _revives => ref _health.Revives;
    private long _collisionCalls;
    private int _collisionRestoreFailures;
    private int _invalidCorrections;
    private int _lastRejectedCorrection;
    private int _groundContacts;
    private int _wallCorrections;
    private int _ceilingCorrections;
    private int _oneWayContacts;
    private bool _sawSolid;
    private bool _sawEmpty;
    private uint _collisionFunction;
    private bool _collisionDisabled;
    private bool _collisionQueryFailed;
    private int _unsupportedTerrainSuspensions;
    private string _collisionFailureReason = "none";

    private RoomIdentity _room;
    private bool _roomKnown => _movementSession.State.RoomKnown;
    private int _roomStableFrames => _movementSession.State.RoomStableUpdates;
    private bool _transitionPending => _movementSession.State.TransitionPending;
    private int _roomLayerEvents => _movementSession.State.RoomLayerEvents;
    private int _completedTransitions => _movementSession.State.CompletedTransitions;
    private int _passedTransitions => _movementSession.State.PassedTransitions;
    private readonly RoomEpochTracker _roomEpochTracker = new();
    private readonly ManagedMovementSessionReducer _movementSession;
    private long _postTransitionCommandedRaw => _movementSession.State.PostTransitionCommandedRaw;
    private bool _awaitingPostTransitionMovement => _movementSession.State.AwaitingPostTransitionMovement;
    private bool _postTransitionMoved => _movementSession.State.PostTransitionMoved;
    private ManagedInputFrame _lastManagedInput;
    private ManagedProxySnapshot _lastManagedSnapshot;
    private ulong _lastManagedStateHash;
    private long _managedSnapshotCount;
    private long _managedUpdateId;

    public CoopFeasibility()
    {
        _movementSession = new ManagedMovementSessionReducer(_roomEpochTracker);
        _attackPublicationAdapter = new NativeAttackPublicationAdapter(this);
    }

    // This is the only bridge used by the attack publication policy. The policy itself has no
    // dependency on guest memory or dispatch and this adapter is reused to keep windows allocation-free.
    private sealed class NativeAttackPublicationAdapter : IAttackPublicationAdapter, IAttackTargetReadAdapter
    {
        private readonly CoopFeasibility _mod;
        private IMemory _memory = null!;
        private CpuContext _context = null!;
        private AttackProfile _profile;
        private uint _entity;

        public NativeAttackPublicationAdapter(CoopFeasibility mod) => _mod = mod;

        public void Bind(IMemory memory, uint entity, AttackProfile profile, CpuContext? context = null)
        {
            _memory = memory;
            _entity = entity;
            _profile = profile;
            if (context != null) _context = context;
        }

        public AttackSlotObservation Probe(in AttackPublicationTuple tuple)
        {
            uint marker = _memory.ReadU32(_entity + 0x7C);
            uint generation = _memory.ReadU32(_entity + 0x80);
            uint roomHash = _memory.ReadU32(_entity + 0x84);
            ushort id = _memory.ReadU16(_entity + EntityIdOffset);
            uint update = _memory.ReadU32(_entity + EntityUpdateOffset);
            ushort hitState = _memory.ReadU16(_entity + EntityHitboxStateOffset);
            bool exact = marker == AttackMarker && generation == tuple.Generation && roomHash == tuple.RoomHash;
            if (exact) return AttackSlotObservation.Exact;
            bool free = id == 0 && update == 0 && hitState == 0;
            return free ? AttackSlotObservation.Free : AttackSlotObservation.Reused;
        }

        public void ClearReservedSlot(in AttackPublicationTuple tuple)
        {
            for (uint offset = 0; offset < Entity.Stride; offset += 4) _memory.WriteU32(_entity + offset, 0);
        }

        public void WriteOwnershipField(in AttackPublicationTuple tuple, int field)
        {
            if (field == 0) _memory.WriteU32(_entity + 0x7C, AttackMarker);
            else if (field == 1) _memory.WriteU32(_entity + 0x80, tuple.Generation);
            else if (field == 2) _memory.WriteU32(_entity + 0x84, tuple.RoomHash);
            else throw new ArgumentOutOfRangeException(nameof(field));
        }

        public void WritePayload(in AttackPublicationTuple tuple)
        {
            int scrollX = unchecked((int)_memory.ReadU32(ScrollXAddress)) >> 16;
            int scrollY = unchecked((int)_memory.ReadU32(ScrollYAddress)) >> 16;
            int worldX = _profile.Kind == AttackKind.Projectile ? _mod._projectileX : _mod._proxyX;
            int worldY = _profile.Kind == AttackKind.Projectile ? _mod._projectileY : _mod._proxyY;
            _memory.WriteU32(_entity, unchecked((uint)(((worldX >> 16) - scrollX) << 16)));
            _memory.WriteU32(_entity + 0x04, unchecked((uint)(((worldY >> 16) - scrollY) << 16)));
            _memory.WriteU16(_entity + EntityHitboxOffsetX, unchecked((ushort)(_profile.Kind == AttackKind.Projectile ? 0 : 14)));
            _memory.WriteU16(_entity + EntityHitboxOffsetY, unchecked((ushort)(_profile.Kind == AttackKind.Projectile ? 0 : -8)));
            _memory.WriteU16(_entity + EntityFacingOffset, (ushort)(_mod._facingLeft ? 1 : 0));
            _memory.WriteU16(_entity + 0x2C, 1);
            _memory.WriteU16(_entity + 0x32, _profile.Source);
            _memory.WriteU32(_entity + EntityFlagsOffset, 0x00020000);
            _memory.WriteU16(_entity + EntityAttackOffset, unchecked((ushort)_profile.Attack));
            _memory.WriteU16(_entity + EntityAttackElementOffset, _profile.Element);
            _memory.WriteU8(_entity + EntityHitboxWidthOffset, _profile.HalfWidth);
            _memory.WriteU8(_entity + EntityHitboxHeightOffset, _profile.HalfHeight);
            _memory.WriteU8(_entity + 0x49, _profile.InvincibilityFrames);
            _memory.WriteU16(_entity + 0x58, _profile.StunFrames);
            _memory.WriteU16(_entity + 0x6A, _profile.HitEffect);
        }

        public void WriteLiveField(in AttackPublicationTuple tuple, int field)
        {
            if (field == 0) _memory.WriteU16(_entity + EntityIdOffset, AttackEntityId);
            else if (field == 1) _memory.WriteU32(_entity + EntityUpdateOffset, EntityNullAddress);
            else if (field == 2) _memory.WriteU16(_entity + EntityHitboxStateOffset, _profile.HitState);
            else throw new ArgumentOutOfRangeException(nameof(field));
        }

        public void CallGuest(in AttackPublicationTuple tuple)
        {
            uint function = _memory.ReadU32(AssignAttackerIdSlot);
            _ = CpuContextGuardedDirectCall.Invoke(_context, _memory, function, _entity, 0, 0, 0);
        }

        public void ReadPostCall(in AttackPublicationTuple tuple)
        {
            _mod._attackLastAttackerId = _memory.ReadU16(_entity + EntityEnemyIdOffset);
            _mod._attackAttackerIdValid = _mod._attackLastAttackerId is >= 0 and < 11;
            if (!_mod._attackAttackerIdValid)
                throw new InvalidOperationException($"attacker ID {_mod._attackLastAttackerId} is outside native cooldown bounds");
            _mod.CaptureAttackTargets(this);
        }

        public void ObserveNative(in AttackPublicationTuple tuple) => _mod.ObserveAttackResult(this);

        public void DeactivateLiveField(in AttackPublicationTuple tuple, int field)
        {
            if (field == 0) _memory.WriteU32(_entity + EntityUpdateOffset, 0);
            else if (field == 1) _memory.WriteU16(_entity + EntityHitboxStateOffset, 0);
            else if (field == 2) _memory.WriteU16(_entity + EntityIdOffset, 0);
            else throw new ArgumentOutOfRangeException(nameof(field));
        }

        public void MutateProjectile(in AttackPublicationTuple tuple)
        {
            int scrollX = unchecked((int)_memory.ReadU32(ScrollXAddress)) >> 16;
            int scrollY = unchecked((int)_memory.ReadU32(ScrollYAddress)) >> 16;
            _memory.WriteU32(_entity, unchecked((uint)(((_mod._projectileX >> 16) - scrollX) << 16)));
            _memory.WriteU32(_entity + 0x04, unchecked((uint)(((_mod._projectileY >> 16) - scrollY) << 16)));
            _memory.WriteU8(_entity + 0x48, 0);
            _memory.WriteU16(_entity + 0x44, 0);
            _mod.CaptureAttackTargets(this);
        }

        public void ClearOwnedSlot(in AttackPublicationTuple tuple)
        {
            for (uint offset = 0; offset < Entity.Stride; offset += 4) _memory.WriteU32(_entity + offset, 0);
        }

        public int ReadScrollX() => unchecked((int)_memory.ReadU32(ScrollXAddress)) >> 16;
        public int ReadScrollY() => unchecked((int)_memory.ReadU32(ScrollYAddress)) >> 16;
        public uint TargetAddress(int slot) => Game.EntitiesAddr + (uint)(slot * Entity.Stride);
        public byte ReadAttackU8(uint offset) => _memory.ReadU8(_entity + offset);
        public byte ReadTargetU8(uint address, uint offset) => _memory.ReadU8(address + offset);
        public ushort ReadTargetU16(uint address, uint offset) => _memory.ReadU16(address + offset);
        public uint ReadTargetU32(uint address, uint offset) => _memory.ReadU32(address + offset);
    }

    private int _slotSamples;
    private int _freePlayerCurrent;
    private int _freeAttackCurrent;
    private int _freeStageCurrent;
    private int _freeTailCurrent;
    private int _longestAttackCurrent;
    private int _minimumFreeAttack = int.MaxValue;
    private int _minimumLongestAttack = int.MaxValue;

    public void OnLoad()
    {
        _pad2Source.Reset();
        _movementSession.Load();
        _instance = this;
        Event.AddListener<VSyncEvent>(OnVSync);
        Event.AddListener<PadReadEvent>(OnPadRead);
        Event.AddListener<PlayerLoadedEvent>(OnPlayerLoaded);
        Event.AddListener<RoomLayerLoadEvent>(OnRoomLayerLoaded);
        ResetManagedHealth();
        Console.WriteLine($"[CoopProbe] Loaded v{Version}; target SymphonyRecomp v0.4.3b");
    }

    public void OnUnload()
    {
        try
        {
            UnloadOwnedAttack();
            ClearLatchedAttackProfile();
        }
        finally
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
            _pad2Source.Reset();
            _virtualPressed = 0;
            Event.RemoveListener<VSyncEvent>(OnVSync);
            Event.RemoveListener<PadReadEvent>(OnPadRead);
            Event.RemoveListener<PlayerLoadedEvent>(OnPlayerLoaded);
            Event.RemoveListener<RoomLayerLoadEvent>(OnRoomLayerLoaded);
            _movementSession.Unload();
            _locomotionState.Invalidate();
            DisarmAwareness("UNLOAD");
            ClearJumpForgiveness();
            SuspendContactScan("UNLOAD");
        }
    }

    private void UnloadOwnedAttack()
    {
        AttackLeaseCommand request = AttackLeaseMachine.RequestOwnedCleanup(_attackLease);
        if (request.Kind == AttackLeaseCommandKind.None) return;
        if (RecompOne.Runtime.Runtime.Mem is not IMemory memory)
        {
            _ = AttackPublicationPolicy.Unload(ref _attackPublication, null, false);
            StopResidualAttackMutation(request, "UNLOAD-RESIDUAL:NO-MEMORY");
            return;
        }

        uint entity = Game.EntitiesAddr + (uint)(request.Lease.Slot * Entity.Stride);
        _attackPublicationAdapter.Bind(memory, entity, _latchedAttackProfile);
        AttackUnloadResult result = AttackPublicationPolicy.Unload(ref _attackPublication,
            _attackPublicationAdapter, true);
        if (result == AttackUnloadResult.Cleaned)
        {
            AttackLeaseCommand authorization = AttackLeaseMachine.OwnedExact(_attackLease, request);
            _attackCleanups++;
            _attackLifecycleCancellations++;
            _attackStatus = "CLEAN:UNLOAD";
            ClearOwnedAttackMetadata(authorization);
            return;
        }

        string evidence = result == AttackUnloadResult.OwnershipMismatch
            ? "UNLOAD-RESIDUAL:MISMATCH"
            : "UNLOAD-RESIDUAL:FAULT";
        if (result == AttackUnloadResult.OwnershipMismatch)
            StopQuarantinedMutation(request, evidence);
        else
            StopResidualAttackMutation(request, evidence);
    }

    public string CaptureAutomationDiagnostics(long automationFrame)
    {
        P2D4Report report = P2D4Report.Parse(BuildReport());
        return P2D4DiagnosticsEnvelope.Serialize(report, _diagnosticSessionId, _diagnosticGeneration,
            _diagnosticFrame, automationFrame);
    }

    public bool TryResetAutomationDiagnostics(string sessionId, int expectedGeneration)
    {
        if (!string.Equals(sessionId, _diagnosticSessionId, StringComparison.Ordinal) ||
            expectedGeneration != _diagnosticGeneration)
            return false;
        return TryResetDiagnostic();
    }

    public void DrawSettings()
    {
        if (_autoTestState == AutoTestState.Running)
            CancelAutomaticTest("settings reopened");
        if (_virtualKeyboard)
        {
            if (_virtualPressed != 0) _virtualForcedReleases++;
            _virtualPressed = 0;
            _virtualNeutralSeen = false;
            _virtualSuppressionFrames = 2;
        }

        ImGui.TextDisabled($"Co-op Feasibility Probe v{Version} | target v0.4.3b");

        bool enabled = _enabled;
        if (ImGui.Checkbox("Enable proxy and collision tests", ref enabled))
        {
            _enabled = enabled;
            _neutralSeen = false;
            if (!enabled)
            {
                SuspendContactScan("DISABLED");
                CancelOwnedAttack("DISABLED");
            }
        }

        bool virtualKeyboard = _virtualKeyboard;
        if (ImGui.Checkbox("Use virtual Player 2 keyboard", ref virtualKeyboard))
            SetVirtualKeyboard(virtualKeyboard);

        bool visible = _visualConfirmed;
        if (ImGui.Checkbox("I can see the Player 2 avatar", ref visible)) _visualConfirmed = visible;

        bool contactVisible = _contactVisualConfirmed;
        if (ImGui.Checkbox("I saw the Player 2 contact tint", ref contactVisible))
            _contactVisualConfirmed = contactVisible;

        bool physicalController = _physicalControllerTest;
        if (ImGui.Checkbox("Require analog test (physical controller 2)", ref physicalController))
            _physicalControllerTest = physicalController;

        if (ImGui.Button("Reset diagnostic") && !TryResetDiagnostic())
            Console.Error.WriteLine("[CoopProbe] Diagnostic reset refused: attack cleanup remains unresolved.");
        ImGui.SameLine();
        if (ImGui.Button("Reset proxy to Player 1")) QueueProxyReset();
        ImGui.SameLine();
        if (ImGui.Button("Release virtual keys")) ReleaseVirtualKeys();
        if (ImGui.Button("Run automatic P2 movement test")) QueueAutomaticTest();
        ImGui.SameLine();
        if (ImGui.Button("Cancel automatic test")) CancelAutomaticTest("cancelled by user");
        if (ImGui.Button("Print report to console")) Console.WriteLine($"[CoopProbe] {BuildReport()}");
        ImGui.SameLine();
        if (ImGui.Button("Copy report")) ImGui.SetClipboardText(BuildReport());

        ImGui.Separator();
        ImGui.TextWrapped(_virtualKeyboard
            ? "Virtual Pad 2: I/J/K/L = Up/Left/Down/Right, U = Cross/jump, O = Circle, P = Start. Close the Mods window before using these keys."
            : "Physical Pad 2: release all buttons, then press and release Up, Right, Down, Left, Cross, Circle, and Start. Move both analog sticks.");
        ImGui.Text($"Safe state: {(_safeFrame ? "yes" : "no")} | {_safeReason}");
        ImGui.Text($"Operation: {_operationStatus}");
        ImGui.Text($"P2 source: {(_virtualKeyboard ? "virtual keyboard" : "configured Pad 2")} | runtime connection: {Controller.Connected2} | changes: {_connectionChanges}");
        ImGui.Text($"Raw host: 0x{_hostState:X4} | pad event: 0x{_padState:X4}");
        ImGui.Text($"Virtual output/raw/seen: 0x{_virtualPressed:X4}/0x{_virtualRawHeld:X4}/0x{_virtualRawSeen:X4} | down/up: 0x{_virtualDownSeen:X4}/0x{_virtualUpSeen:X4}");
        ImGui.Text($"Automatic test: {_autoTestStatus} | frame {_autoTestFrame} | completed runs {_autoTestRuns} | forced releases {_virtualForcedReleases}");
        ImGui.Text($"Game pressed: 0x{_gamePressed:X4} | tapped: 0x{_gameTapped:X4}");
        if (_virtualKeyboard)
        {
            ImGui.Text($"Required input seen key-down/key-up/pad/game/tap: {CountSeen(_virtualDownSeen, RequiredGameButtons)}/7, {CountSeen(_virtualUpSeen, RequiredGameButtons)}/7, {CountSeen(_padSeen, RequiredGameButtons)}/7, {CountSeen(_gameSeen, RequiredGameButtons)}/7, {CountSeen(_tapSeen, RequiredGameButtons)}/7");
            ImGui.TextWrapped($"Missing virtual key-down: {MissingGameButtons(_virtualDownSeen)} | missing game taps: {MissingGameButtons(_tapSeen)}");
        }
        else
        {
            ImGui.Text($"Required input seen host/pad/game/tap: {CountSeen(_hostSeen, RequiredHostButtons)}/7, {CountSeen(_padSeen, RequiredGameButtons)}/7, {CountSeen(_gameSeen, RequiredGameButtons)}/7, {CountSeen(_tapSeen, RequiredGameButtons)}/7");
            ImGui.TextWrapped($"Missing host: {MissingHostButtons()} | missing game taps: {MissingGameButtons(_tapSeen)}");
        }
        ImGui.Text($"Left stick X {_leftXMin}-{_leftXMax}, Y {_leftYMin}-{_leftYMax} | Right X {_rightXMin}-{_rightXMax}, Y {_rightYMin}-{_rightYMax}");

        ImGui.Separator();
        ImGui.Text($"Proxy: {(_proxyInitialized ? "active" : "waiting")} | world {_proxyX >> 16}, {_proxyY >> 16} | velocity {_velocityX / 65536f:F2}, {_velocityY / 65536f:F2}");
        ImGui.Text($"Grounded: {_grounded} | collision this frame: {_collisionThisFrame} | moved L/R: {_leftDistanceRaw >> 16}/{_rightDistanceRaw >> 16} | jumped: {_jumpObserved}");
        ImGui.Text($"P2 stance/lifecycle: {(_crouched ? "crouched" : "standing")}/{(_downed ? "downed" : "alive")} | {_locomotion}/{_animation} frame {_animationFrame}, tick {_animationTick} | stand blocked {_standBlocked}");
        ImGui.Text($"Animation state/advance/hurtbox masks 0x{_animationStatesSeen:X}/0x{_animationAdvanceStatesSeen:X}/0x{_ownedHurtboxStatesSeen:X}");
        ImGui.Text($"Independent visual frame 0x{_independentNativeFrame:X2} | poses 0x{_visualPosesSeen:X16}/0x{_hurtboxPosesSeen:X16} | {_independentVisualStatus}");
        ImGui.Text($"Independent eligible/submitted/restore checks/failures: {_independentVisualEligible}/{_independentVisualSubmitted}/{_independentRestoreChecks}/{_independentRestoreFailures}");
        ImGui.Text($"Jump coyote/buffer remaining: {_coyoteUpdates}/{_jumpBufferUpdates} | normal/coyote/buffered: {_normalJumps}/{_coyoteJumps}/{_bufferedJumps}");
        ImGui.Text($"Collision API: 0x{_collisionFunction:X8} | calls: {_collisionCalls} | restore failures: {_collisionRestoreFailures} | rejected corrections/unsupported suspensions: {_invalidCorrections}/{_unsupportedTerrainSuspensions}");
        ImGui.Text($"Avatar frames/eligible/DrawOTag: {_renderSubmitted}/{_renderEligible}/{_drawOtCalls} | HLE active/ready: {GpuHle.Active}/{GpuHle.Backend?.Ready == true}");
        ImGui.Text($"Native source confirmed/captured/eligible/opposite-facing/fallback: {_nativeSpriteSubmitted}/{_nativeSpriteCaptured}/{_nativeSpriteEligible}/{_nativeSpriteFlipped}/{_nativeSpriteFallbacks} | streak {_nativeSpriteStreak}, opposite {Bool(_nativeSpriteFlipSeenInStreak)} | {_nativeSpriteStatus}");
        ImGui.Text($"Contact shadow current/peak/enter/stay/exit: {_contactOpportunities.Current}/{_contactOpportunities.Peak}/{_contactOpportunities.Entries}/{_contactOpportunities.StaySamples}/{_contactOpportunities.Exits} | {_contactStatus}");
        ImGui.Text($"Owned hurtbox offset {_contactOffsetX},{_contactOffsetY} half-size {_contactHalfWidth},{_contactHalfHeight} | last slot/id {_contactLastSlot}/{_contactLastEntityId}");
        ImGui.Text($"Contact scans/slots/eligible/overlap/damaging: {_contactOpportunities.ScanFrames}/{_contactOpportunities.SlotsScanned}/{_contactOpportunities.EligibleSamples}/{_contactOpportunities.OverlapSamples}/{_contactOpportunities.DamagingSamples} | resume grace {_contactOpportunities.ResumeGraceScans}, budget {_contactOpportunities.ResumeGraceBudget}");
        ImGui.Text($"HP {_managedHp}/{ManagedMaxHp} | invuln/hurt {_damageInvulnerability}/{_hurtLock} | damage {_damageEvents}, hit-suppressed {_damageSuppressedHitInvul} last {_lastDamage}@{_lastDamageSlot} elem 0x{_lastDamageElement:X} | revive {_reviveProgress}/{ReviveUpdates} ({_revives})");
        ImGui.Text($"Attack {_animation}/{_attackTimer} | slot/quarantine {_ownedAttackSlot}/{_attackQuarantineSlot} | alloc/normal-window/clean/fail {_attackAllocations}/{_attackNormalEngineWindows}/{_attackCleanups}/{_attackFailures} | id {_attackLastAttackerId} causal/hit/cooldown/hp {_attackCausalResults}/{_attackHitFlagObservations}/{_attackCooldownObservations}/{_attackTargetHpChanges} | {_attackStatus}");
        ImGui.Text($"Profile {(_attackProfileLatched ? _latchedAttackProfile.Kind : AttackKind.Contact)} item/attack/element/state {(_attackProfileLatched ? _latchedAttackProfile.Item : 0)}/{(_attackProfileLatched ? _latchedAttackProfile.Attack : 0)}/{(_attackProfileLatched ? _latchedAttackProfile.Element : 0):X}/{(_attackProfileLatched ? _latchedAttackProfile.HitState : 0):X} | extract/failed/restore {_profileExtractions}/{_profileExtractionFailures}/{_equipmentRestoreChecks},{_equipmentRestoreFailures} | projectile windows {_projectileWindows}");
        ImGui.Text($"Enemies scans/native/compatible {_enemyDiagnosticScans}/{_enemyNativeCandidateSamples}/{_enemyCompatibleCandidateSamples} | nearest slot/id/enemy/hp/dist {_nearestTargetSlot}/{_nearestTargetEntityId}/{_nearestTargetEnemyId}/{_nearestTargetHp}/{_nearestTargetP1Distance},{_nearestTargetP2Distance} | hits/defeated/zero {_nativeTargetHits}/{_defeatedTargets}/{_compatibleZeroHpHits} | {_enemyDiagnosticStatus}");
        ImGui.Text($"CEN awareness calls/overrides/chosen {_awarenessCalls}/{_awarenessOverrides}/{_awarenessChosenSlot} | {_awarenessStatus} | HUD eligible/submitted {_hudEligible}/{_hudSubmitted}");
        ImGui.Text($"Reconstruction {_reconstructionStatus} stable {_reconstructionSafeFrames}/{ReconstructionStableUpdates} attempts/success/fail/tether {_reconstructionAttempts}/{_reconstructionSuccesses}/{_reconstructionFailures}/{_tetherRecoveries}");
        ImGui.Text($"Replay epoch/update/snapshots/hash: {_roomEpochTracker.Epoch}/{_lastManagedInput.UpdateId}/{_managedSnapshotCount}/{_lastManagedStateHash:X16}");
        ImGui.Text($"Read-only guard checks/failures/region: {_contactGuardChecks}/{_contactGuardFailures}/{_contactGuardRegion}");
        ImGui.Text($"Collision contacts ground/wall/ceiling/one-way: {_groundContacts}/{_wallCorrections}/{_ceilingCorrections}/{_oneWayContacts}");
        ImGui.Text($"Transitions passed/completed/layer events/post-move: {_passedTransitions}/{_completedTransitions}/{_roomLayerEvents}/{_postTransitionMoved}");
        ImGui.Text($"Free slots player/attack/stage/tail: {_freePlayerCurrent}/{_freeAttackCurrent}/{_freeStageCurrent}/{_freeTailCurrent}");
        ImGui.Text($"Attack-pool minimum free/run: {DisplayMinimum(_minimumFreeAttack)}/{DisplayMinimum(_minimumLongestAttack)} over {_slotSamples} samples");

        ImGui.Separator();
        string report = BuildReport();
        ImGui.TextWrapped(report);
        if (_fatal) ImGui.TextWrapped($"First error: {_firstError}");
        else if (_collisionDisabled) ImGui.TextWrapped($"Collision disabled: {_collisionFailureReason}");
        else if (_contactDisabled) ImGui.TextWrapped($"Contact disabled: {_contactStatus} ({_contactGuardRegion})");
        ImGui.TextDisabled("P2 state and incoming damage remain managed-only. Outgoing profiles use one exact-owned entity and normal collision windows; target HP/death/rewards are never written directly.");
    }

    private void OnVSync(VSyncEvent e)
    {
        try
        {
            _diagnosticFrame = e.Frame;
            _vsyncCalls++;
            if (!_virtualKeyboard && Controller.Connected2 != _previousConnected)
            {
                _previousConnected = Controller.Connected2;
                _connectionChanges++;
                ClearInputObservations();
            }

            if (_virtualKeyboard) ReconcileVirtualKeys();
            if (_virtualSuppressionFrames > 0) _virtualSuppressionFrames--;
            if (_virtualKeyboard) AdvanceAutomaticTest(e.Memory);

            _hostState = Controller.State2;
            ushort pressed = (ushort)~_hostState;
            _hostSeen |= pressed;
            if (_hostState == 0xFFFF) _neutralSeen = true;

            TrackAxis(Controller.LeftX2, ref _leftXMin, ref _leftXMax);
            TrackAxis(Controller.LeftY2, ref _leftYMin, ref _leftYMax);
            TrackAxis(Controller.RightX2, ref _rightXMin, ref _rightXMax);
            TrackAxis(Controller.RightY2, ref _rightYMin, ref _rightYMax);
        }
        catch (Exception ex)
        {
            Fail("VSync event", ex);
        }
    }

    private void OnPadRead(PadReadEvent e)
    {
        if (e.Port != 1) return;
        try
        {
            _pad2Reads++;
            if (_virtualKeyboard)
            {
                if (VirtualInputAllowed(e.Memory)) e.Buttons = (ushort)~_virtualPressed;
                else
                {
                    if (_virtualPressed != 0) _virtualForcedReleases++;
                    _virtualPressed = 0;
                    _virtualNeutralSeen = false;
                    e.Buttons = 0xFFFF;
                }
            }
            _padState = e.Buttons;
            _pad2Source.ObserveProcessed(_virtualKeyboard, e.Buttons);
            _padSeen |= (ushort)~e.Buttons;
            if (_virtualKeyboard && _virtualPressed == 0 && e.Buttons == 0xFFFF && ReadVirtualKeysDown() == 0)
            {
                _virtualNeutralSeen = true;
                _virtualNeutralObserved = true;
            }
        }
        catch (Exception ex)
        {
            Fail("PadRead event", ex);
        }
    }

    private void OnPlayerLoaded(PlayerLoadedEvent e)
    {
        try
        {
            CancelAutomaticTest("player reloaded");
            ReleaseVirtualKeys();
            _pad2Source.Reset();
            _movementSession.PlayerReloaded();
            _locomotionState.Invalidate();
            DisarmAwareness("PLAYER");
            ClearJumpForgiveness();
            ResetNativeSpriteFrame();
            _visualConfirmed = false;
            _contactVisualConfirmed = false;
            SuspendContactScan("PLAYER");
            CancelOwnedAttack("PLAYER");
            ClearLatchedAttackProfile();
            ResetManagedHealth();
            _safeReason = e.Character == PlayableCharacter.Alucard
                ? "Player loaded; waiting for a stable room"
                : "Unsupported character; use Alucard";
        }
        catch (Exception ex)
        {
            Fail("PlayerLoaded event", ex);
        }
    }

    private void OnRoomLayerLoaded(RoomLayerLoadEvent e)
    {
        try
        {
            CancelAutomaticTest("room layer changed");
            ReleaseVirtualKeys();
            BeginTransition();
            _movementSession.RoomLayerLoaded();
            _locomotionState.Invalidate();
            DisarmAwareness("TRANS");
            ClearJumpForgiveness();
            ResetNativeSpriteFrame();
            _contactVisualConfirmed = false;
            SuspendContactScan("TRANS");
            CancelOwnedAttack("TRANS");
            ClearLatchedAttackProfile();
            _visualConfirmed = false;
            _safeReason = $"Room layer event: stage 0x{e.StageId:X2}, layer {e.LayerIndex}";
        }
        catch (Exception ex)
        {
            Fail("RoomLayer event", ex);
        }
    }

    [PostHook("dra", "RunMainEngine")]
    private static void AfterMainEngine(CpuContext context, IMemory memory)
    {
        CoopFeasibility? mod = _instance;
        if (mod == null) return;
        try
        {
            mod._mainEngineCalls++;
            if (!Game.Available || !Game.InGame)
            {
                mod.SuspendContactScan("WAIT");
                return;
            }
            mod._gamePressed = Game.Pressed2;
            mod._gameTapped = Game.Tapped2;
            mod._gameSeen |= mod._gamePressed;
            mod._tapSeen |= mod._gameTapped;
            mod.ObserveOwnedAttackWindow(memory);
            mod.UpdateContactShadow(memory);
        }
        catch (Exception ex)
        {
            mod.Fail("RunMainEngine hook", ex);
        }
    }

    [PostHook("dra", "UpdatePlayerEntities")]
    private static void AfterPlayerEntities(CpuContext context, IMemory memory)
    {
        CoopFeasibility? mod = _instance;
        if (mod == null) return;
        try
        {
            mod._updateCalls++;
            mod.CleanupOwnedAttack(memory, mod.AutomaticInputSafe(memory) ? "WINDOW" : "UNSAFE");
            if (mod._fatal) return;
            mod.UpdateProxy(context, memory);
            mod.TrySpawnOwnedAttack(context, memory);
        }
        catch (Exception ex)
        {
            mod.Fail("UpdatePlayerEntities hook", ex);
        }
    }

    [PostHook("cen", "GetDistanceToPlayerX_cen")]
    private static void AfterCenterCubeDistanceX(CpuContext context, IMemory memory) =>
        ApplyCenterCubeAwareness(context, memory, AwarenessHelper.DistanceX);

    [PostHook("cen", "GetDistanceToPlayerY_cen")]
    private static void AfterCenterCubeDistanceY(CpuContext context, IMemory memory) =>
        ApplyCenterCubeAwareness(context, memory, AwarenessHelper.DistanceY);

    [PostHook("cen", "GetSideToPlayer_cen")]
    private static void AfterCenterCubeSide(CpuContext context, IMemory memory) =>
        ApplyCenterCubeAwareness(context, memory, AwarenessHelper.Side);

    private static void ApplyCenterCubeAwareness(CpuContext context, IMemory memory, AwarenessHelper helper)
    {
        CoopFeasibility? mod = _instance;
        if (mod == null) return;
        uint original = context.V0;
        try
        {
            mod._awarenessCalls++;
            if (!mod.TryGetAwarenessTarget(memory, out int slot, out int entityX, out int entityY,
                out int p2X, out int p2Y))
                return;
            int dy = p2Y - entityY;
            context.V0 = helper switch
            {
                AwarenessHelper.DistanceX => NativeDistanceX(entityX, p2X),
                AwarenessHelper.DistanceY => unchecked((uint)Math.Abs(dy)),
                AwarenessHelper.Side => (uint)((p2X < entityX ? 1 : 0) | (p2Y < entityY ? 2 : 0)),
                _ => original,
            };
            mod._awarenessOverrides++;
            mod._awarenessChosenSlot = slot;
            mod._awarenessStatus = $"P2:{slot}";
        }
        catch (Exception ex)
        {
            context.V0 = original;
            mod._awarenessDisabled = true;
            mod._awarenessStatus = $"FAIL:{ex.GetType().Name}";
        }
    }

    private static uint NativeDistanceX(int entityX, int targetX)
    {
        int difference = unchecked((short)(entityX - targetX));
        int absolute = difference == short.MinValue ? short.MinValue : Math.Abs(difference);
        return unchecked((uint)absolute);
    }

    [PreHook("dra", "RenderEntities")]
    private static void BeforeRenderEntities(CpuContext context, IMemory memory)
    {
        CoopFeasibility? mod = _instance;
        if (mod == null) return;
        try
        {
            mod.PrepareNativeSpriteCapture(memory);
        }
        catch (Exception ex)
        {
            mod.Fail("RenderEntities pre-hook", ex);
        }
    }

    [PostHook("dra", "RenderEntities")]
    private static void AfterRenderEntities(CpuContext context, IMemory memory)
    {
        CoopFeasibility? mod = _instance;
        if (mod == null) return;
        try
        {
            mod._renderCalls++;
            if (mod._fatal) return;
            mod.SubmitNativeSprite(memory);
        }
        catch (Exception ex)
        {
            mod.Fail("RenderEntities hook", ex);
        }
    }

    [PostHook("main", "DrawOTag")]
    private static void AfterDrawOrderingTable(CpuContext context, IMemory memory)
    {
        CoopFeasibility? mod = _instance;
        if (mod == null) return;
        try
        {
            mod._drawOtCalls++;
            if (mod._fatal) return;
            mod.DrawProxyGp0(context, memory);
        }
        catch (Exception ex)
        {
            mod.Fail("DrawOTag hook", ex);
        }
    }

    private void UpdateProxy(CpuContext context, IMemory memory)
    {
        if (Game.Available && Game.InGame) SampleEntitySlots(memory);
        _collisionQueryFailed = false;

        _safeFrame = TryGetSafeState(memory, out _safeReason);
        if (!_safeFrame || memory.ReadU8(Game.StageIdAddr) != (byte)Stage.CenterCube || _downed)
            DisarmAwareness("WAIT");
        if (_collisionDisabled)
        {
            _safeFrame = false;
            if (_collisionFunction == ExpectedCollisionFunction) _safeCode = "COL";
            _safeReason = _collisionFailureReason;
        }
        if (!_enabled || !_safeFrame)
        {
            _movementSession.ObserveUnsafe();
            ClearJumpForgiveness();
            if (_reinitializeRequested)
                _operationStatus = $"Proxy reset blocked: {_safeReason}";
            return;
        }

        RoomIdentity observedRoom = ReadRoomIdentity(memory);
        bool roomChanged = _roomKnown && !_room.Equals(observedRoom);
        if (roomChanged) BeginTransition();
        _room = observedRoom;
        ManagedMovementSessionTransition movement = _movementSession.ObserveSafeRoom(observedRoom.ManagedKey());
        if (roomChanged)
        {
            _locomotionState.Invalidate();
            _reconstructionStatus = "ROOM";
        }
        if (movement.ReconstructionRequested)
        {
            bool requested = _reinitializeRequested;
            ReconstructionRunResult reconstruction = TryReconstructProxy(context, memory,
                requested ? "RESET" : "ROOM", movement.Reconstruction);
            if (reconstruction != ReconstructionRunResult.Selected)
                _movementSession.CompleteReconstruction(movement.Reconstruction,
                    reconstruction == ReconstructionRunResult.NoSafeCandidate
                        ? ManagedMovementReconstructionResult.NoSafeCandidate
                        : ManagedMovementReconstructionResult.AdapterFault);
            if (reconstruction != ReconstructionRunResult.Selected)
            {
                if (_collisionQueryFailed || _collisionDisabled) AbortCollisionFrame(memory);
                return;
            }
            if (requested)
            {
                _operationStatus = "Proxy reset completed beside Player 1";
            }
            else _operationStatus = "Proxy initialized beside Player 1";
        }
        else if (!_proxyInitialized)
        {
            _reconstructionStatus = _reconstructionHardFailure ? "NO_SAFE_CANDIDATE" : "STABILIZE";
            return;
        }
        if (_transitionPending)
        {
            _reconstructionStatus = "TRANSITION_READY";
            return;
        }

        _collisionThisFrame = false;
        if (!TryCollision(context, memory, _proxyX >> 16, _proxyY >> 16, out _))
        {
            AbortCollisionFrame(memory);
            return;
        }

        bool decrementInvulnerability = _damageInvulnerability > 0;
        bool decrementHurtLock = _hurtLock > 0;

        _gamePressed = Game.Pressed2;
        _gameTapped = Game.Tapped2;
        ushort pressed = _gamePressed;
        ushort tapped = _gameTapped;
        bool sourceAvailable = _pad2Source.IsAvailable(_virtualKeyboard, Controller.Connected2);
        bool sourceNeutral = _virtualKeyboard ? _virtualNeutralSeen : _neutralSeen;
        bool canControl = sourceAvailable && sourceNeutral;
        _managedUpdateId = checked(_managedUpdateId + 1);
        _lastManagedInput = new ManagedInputFrame(_managedUpdateId, _roomEpochTracker.Epoch,
            pressed, tapped, canControl);
        int beforeX = _proxyX;
        bool commandedLeft = false;
        bool commandedRight = false;
        bool wasGrounded = _grounded;
        JumpForgivenessContinuation jumpContinuation;
        _landedThisUpdate = false;
        _horizontalCommandThisUpdate = false;

        UpdateRevive(memory, pressed, canControl);

        bool canAct = canControl && !_downed && _hurtLock == 0 && _attackTimer == 0 && _ownedAttackSlot < 0;
        ManagedStanceTransition stance = _stance.Observe(canAct, _grounded,
            (pressed & (ushort)GameButton.Down) != 0);
        if (stance.Command.Kind == ManagedStanceCommandKind.ProbeStandingHull)
        {
            if (!TryStandingHullClear(context, memory, _proxyX >> 16, _proxyY >> 16, out bool clear))
            {
                AbortCollisionFrame(memory);
                return;
            }
            _stance.CompleteStandingProbe(stance.Command, clear);
        }

        if (canAct)
        {
            bool left = (pressed & (ushort)GameButton.Left) != 0;
            bool right = (pressed & (ushort)GameButton.Right) != 0;
            commandedLeft = !_crouched && left && !right;
            commandedRight = !_crouched && right && !left;
            _horizontalCommandThisUpdate = commandedLeft || commandedRight;
            _velocityX = _crouched || left == right ? 0 : left ? -RunSpeed : RunSpeed;
            if (_velocityX < 0) _facingLeft = true;
            else if (_velocityX > 0) _facingLeft = false;

            JumpForgivenessTransition jump = _jumpForgiveness.BeginUpdate(
                (tapped & (ushort)GameButton.Cross) != 0, _grounded, _crouched);
            jumpContinuation = jump.Continuation;
            if (jump.Request != JumpForgivenessRequest.None)
            {
                BeginProxyJump(jump.Request);
            }
            if (!_crouched && (tapped & (ushort)GameButton.Circle) != 0)
            {
                _pendingAttackKind = (pressed & (ushort)GameButton.Up) != 0
                    ? AttackKind.Projectile : AttackKind.Contact;
                _attackProfileLatched = false;
                _attackTimer = ManagedLocomotionCatalog.AttackTotalUpdates;
                _attackStatus = "STARTUP";
                _velocityX = 0;
            }
        }
        else
        {
            if (_hurtLock > 0 || _downed)
                _velocityX = ApproachZero(_velocityX, 0x04000);
            else _velocityX = 0;
            ClearJumpForgiveness();
            // The legacy path cleared before physics but still ran walk-off initialization after
            // grounded refresh, so advance an empty pre-physics phase to preserve that ordering.
            jumpContinuation = _jumpForgiveness.BeginUpdate(false, _grounded, _crouched).Continuation;
        }

        if (!_grounded) _velocityY = Math.Min(MaxFallSpeed, _velocityY + Gravity);
        else if (_velocityY > 0) _velocityY = 0;

        if (!MoveHorizontal(context, memory) || !MoveVertical(context, memory) ||
            !RefreshGrounded(context, memory))
        {
            AbortCollisionFrame(memory);
            return;
        }
        _landedThisUpdate = !wasGrounded && _grounded && _velocityY == 0;
        JumpForgivenessTransition completedJump = _jumpForgiveness.CompleteUpdate(
            jumpContinuation, _grounded);
        if (completedJump.Request != JumpForgivenessRequest.None)
        {
            BeginProxyJump(completedJump.Request);
        }
        _locomotionState.Update(new ManagedLocomotionObservation(_downed, _hurtLock > 0, _compactHurt,
            _attackTimer, _crouched, _grounded, _horizontalCommandThisUpdate, _velocityX, _velocityY,
            _landedThisUpdate));

        _health = ManagedHealthMachine.AdvanceTimers(_health, decrementInvulnerability, decrementHurtLock);

        if (_attackTimer > 0)
        {
            ManagedAttackAdvance attackAdvance = _locomotionState.AdvanceAttackCountdown(_attackTimer);
            _attackTimer = attackAdvance.Timer;
            if (attackAdvance.EnteredActive)
            {
                _attackSpawnPending = true;
                _attackStatus = "ACTIVE";
            }
            else if (attackAdvance.EnteredRecovery)
            {
                _attackStatus = "RECOVERY";
            }
            else if (attackAdvance.Completed)
            {
                if (_ownedAttackSlot < 0) _attackStatus = "IDLE";
            }
        }

        int deltaX = _proxyX - beforeX;
        if (commandedLeft && deltaX < 0) _leftDistanceRaw += -(long)deltaX;
        else if (commandedRight && deltaX > 0) _rightDistanceRaw += deltaX;
        if (_awaitingPostTransitionMovement &&
            ((commandedLeft && deltaX < 0) || (commandedRight && deltaX > 0)))
            _movementSession.ObservePostTransitionMovement(Math.Abs((long)deltaX));

        if (_jumpPending && _proxyY <= _jumpStartY - 4 * FixedOne)
        {
            _jumpObserved = true;
            _jumpPending = false;
        }

        int playerX = unchecked((int)memory.ReadU32(PlayerWorldXAddress));
        int playerY = unchecked((int)memory.ReadU32(PlayerWorldYAddress));
        ValidateManagedHealth();
        if (Math.Abs((_proxyX >> 16) - playerX) > 256 || Math.Abs((_proxyY >> 16) - playerY) > 192)
        {
            BeginReconstruction("TETHER");
            return;
        }

        if (!_movementSession.SnapshotEligible) return;
        _lastManagedSnapshot = new ManagedProxySnapshot(_managedUpdateId, _roomEpochTracker.Epoch,
            _movementSession.State.Phase, _room.ManagedKey(), _proxyX, _proxyY, _velocityX, _velocityY, _proxyInitialized,
            _grounded, _facingLeft, _crouched, _standBlocked, _coyoteUpdates, _jumpBufferUpdates,
            (byte)_locomotion, (byte)_animation, _animationFrame, _animationTick);
        _lastManagedStateHash = ManagedStateCodec.Hash(_lastManagedInput, _lastManagedSnapshot);
        _managedSnapshotCount++;

    }

    private void AbortCollisionFrame(IMemory memory)
    {
        _safeFrame = false;
        _safeCode = _collisionDisabled ? "COL" : "SHAPE";
        _safeReason = _collisionFailureReason;
        _attackTimer = 0;
        _attackSpawnPending = false;
        CleanupOwnedAttack(memory, "COLLISION");
        BeginReconstruction(_collisionDisabled ? "COLLISION" : "TERRAIN");
    }

    private void BeginProxyJump(JumpForgivenessRequest request)
    {
        _velocityY = JumpSpeed;
        _grounded = false;
        _jumpStartY = _proxyY;
        _jumpPending = true;
        if (request == JumpForgivenessRequest.Normal) _normalJumps++;
        else if (request == JumpForgivenessRequest.Coyote) _coyoteJumps++;
        else _bufferedJumps++;
    }

    private void UpdateRevive(IMemory memory, ushort player2Pressed, bool canControl)
    {
        if (!_downed)
        {
            _health = ManagedHealthMachine.ApplyRevive(_health, false);
            return;
        }
        int playerX = unchecked((int)memory.ReadU32(PlayerWorldXAddress));
        int playerY = unchecked((int)memory.ReadU32(PlayerWorldYAddress));
        bool playerAlive = memory.ReadU32(PlayerEntityAddress + EntityUpdateOffset) != 0 &&
            (memory.ReadU32(PlayerEntityAddress + EntityFlagsOffset) & EntityDead) == 0;
        bool playerCompatible = Player.IsAlucard && Player.HasControl &&
            !Player.HasStatus(PlayerStatus.Transform | PlayerStatus.Dead);
        var observation = new ManagedReviveObservation(canControl,
            (Game.Pressed & (ushort)GameButton.Down) != 0,
            (player2Pressed & (ushort)GameButton.Circle) != 0,
            (_proxyX >> 16) - playerX, (_proxyY >> 16) - playerY,
            playerAlive, playerCompatible, _roomKnown && ReadRoomIdentity(memory).Equals(_room));
        bool wasDowned = _downed;
        _health = ManagedHealthMachine.ApplyRevive(_health, observation);
        if (wasDowned && !_downed) _locomotionState.Invalidate();
    }

    private void ResetManagedHealth()
    {
        _health = ManagedHealthMachine.Reset();
    }

    private void ValidateManagedHealth()
    {
        _health = ManagedHealthMachine.Validate(_health);
    }

    private static int ApproachZero(int value, int amount) => value > 0
        ? Math.Max(0, value - amount)
        : Math.Min(0, value + amount);

    private void ClearJumpForgiveness()
    {
        _jumpForgiveness.Clear();
    }

    private bool MoveHorizontal(CpuContext context, IMemory memory)
    {
        int remaining = _velocityX;
        while (remaining != 0)
        {
            int step = Math.Clamp(remaining, -FixedOne, FixedOne);
            int substepStart = _proxyX;
            _proxyX += step;
            remaining -= step;

            bool movingRight = step > 0;
            int queryX = (_proxyX >> 16) + (movingRight ? HalfWidth : -HalfWidth);
            int correction = 0;
            bool blocked = false;
            ReadOnlySpan<int> offsets = _crouched
                ? [24, 17, 9, 5, 1]
                : [24, 17, 9, 1, -7, -14, -21];
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!TryCollision(context, memory, queryX, (_proxyY >> 16) + offsets[i], out CollisionResult hit))
                {
                    _proxyX = substepStart;
                    return false;
                }
                if (!BlocksSide(hit.Effects)) continue;
                int value = movingRight ? hit.RightCorrection : hit.LeftCorrection;
                correction = !blocked ? value : movingRight ? Math.Min(correction, value) : Math.Max(correction, value);
                blocked = true;
            }

            if (!blocked) continue;
            if (!TryApplyCorrection(ref _proxyX, correction))
            {
                _proxyX = substepStart;
                return false;
            }
            _velocityX = 0;
            _wallCorrections++;
            _collisionThisFrame = true;
            return true;
        }
        return true;
    }

    private bool MoveVertical(CpuContext context, IMemory memory)
    {
        int remaining = _velocityY;
        while (remaining != 0)
        {
            int step = Math.Clamp(remaining, -FixedOne, FixedOne);
            int substepStart = _proxyY;
            _proxyY += step;
            remaining -= step;

            bool falling = step > 0;
            int queryY = (_proxyY >> 16) + (falling ? FootOffset : CurrentHeadOffset);
            int correction = 0;
            bool blocked = false;
            ReadOnlySpan<int> offsets = [-HalfWidth + 1, 0, HalfWidth - 1];
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!TryCollision(context, memory, (_proxyX >> 16) + offsets[i], queryY, out CollisionResult hit))
                {
                    _proxyY = substepStart;
                    return false;
                }
                if (falling ? !BlocksFloor(hit.Effects) : !BlocksCeiling(hit.Effects)) continue;
                int value = falling ? hit.FloorCorrection : hit.CeilingCorrection;
                correction = !blocked ? value : falling ? Math.Min(correction, value) : Math.Max(correction, value);
                blocked = true;
            }

            if (!blocked) continue;
            if (!TryApplyCorrection(ref _proxyY, correction))
            {
                _proxyY = substepStart;
                return false;
            }
            _velocityY = 0;
            _grounded = falling;
            if (falling) _groundContacts++;
            else _ceilingCorrections++;
            _collisionThisFrame = true;
            return true;
        }
        return true;
    }

    private bool RefreshGrounded(CpuContext context, IMemory memory)
    {
        if (_velocityY < 0)
        {
            _grounded = false;
            return true;
        }

        bool floor = false;
        int queryY = (_proxyY >> 16) + FootOffset + 1;
        ReadOnlySpan<int> offsets = [-HalfWidth + 1, 0, HalfWidth - 1];
        for (int i = 0; i < offsets.Length; i++)
        {
            if (!TryCollision(context, memory, (_proxyX >> 16) + offsets[i], queryY, out CollisionResult hit)) return false;
            if (BlocksFloor(hit.Effects) && hit.FloorCorrection is >= -4 and <= 0)
            {
                floor = true;
                _groundContacts++;
                if ((hit.Effects & EffectSolidFromAbove) != 0) _oneWayContacts++;
            }
        }
        _grounded = floor;
        return true;
    }

    private bool TryCollision(CpuContext context, IMemory memory, int worldX, int worldY, out CollisionResult result)
    {
        result = default;
        _collisionFunction = memory.ReadU32(GameApi.CheckCollisionAddr);
        if (_collisionFunction != ExpectedCollisionFunction)
        {
            _collisionDisabled = true;
            _collisionFailureReason = $"Collision API mismatch: expected 0x{ExpectedCollisionFunction:X8}, got 0x{_collisionFunction:X8}";
            return false;
        }

        int scrollX = unchecked((int)memory.ReadU32(ScrollXAddress)) >> 16;
        int scrollY = unchecked((int)memory.ReadU32(ScrollYAddress)) >> 16;
        int screenX = worldX - scrollX;
        int screenY = worldY - scrollY;

        uint savedSp = context.SP;
        if (savedSp < 0x80010000 || savedSp >= 0x80200000)
            throw new InvalidOperationException($"Guest stack is outside RAM: 0x{savedSp:X8}");
        ulong temporarySpValue = ((ulong)savedSp - 0x80u) & ~7UL;
        ulong scratchStartValue = temporarySpValue - CollisionStackDepth;
        ulong scratchEndValue = temporarySpValue + 0x10u + ColliderSize;
        if (scratchStartValue < 0x80010000UL || scratchEndValue > 0x80200000UL)
            throw new InvalidOperationException($"Collision scratch is outside RAM: 0x{savedSp:X8}");
        uint temporarySp = checked((uint)temporarySpValue);
        uint output = temporarySp + 0x10u;
        uint scratchStart = checked((uint)scratchStartValue);
        int scratchLength = checked((int)(scratchEndValue - scratchStartValue));

        Span<uint> savedContext = stackalloc uint[CpuContextRegisterGuard.StateWordCount];
        var contextGuard = new CpuContextRegisterGuard(context, savedContext);
        Span<byte> saved = stackalloc byte[scratchLength];
        int savedCount = 0;
        try
        {
            for (int i = 0; i < scratchLength; i++)
            {
                saved[i] = memory.ReadU8(scratchStart + (uint)i);
                savedCount++;
                memory.WriteU8(scratchStart + (uint)i, 0);
            }

            context.SP = temporarySp;
            _ = CpuContextDirectCall.Invoke(context, memory, _collisionFunction,
                unchecked((uint)screenX), unchecked((uint)screenY), output, 0);
            result = new CollisionResult(
                memory.ReadU32(output),
                unchecked((int)memory.ReadU32(output + 0x04)),
                unchecked((int)memory.ReadU32(output + 0x0C)),
                unchecked((int)memory.ReadU32(output + 0x18)),
                unchecked((int)memory.ReadU32(output + 0x20)));
        }
        finally
        {
            try
            {
                GuestScratchRestore.RestoreAll(memory, scratchStart, saved[..savedCount]);
            }
            catch
            {
                _collisionRestoreFailures++;
                throw;
            }
            finally
            {
                contextGuard.Restore();
            }
        }

        _collisionCalls++;
        if ((result.Effects & EffectSlopeMask) != 0)
        {
            _collisionQueryFailed = true;
            _unsupportedTerrainSuspensions++;
            _collisionFailureReason = $"Unsupported shaped/slope terrain at {worldX},{worldY}: 0x{result.Effects:X4}";
            return false;
        }
        if ((result.Effects & EffectSolid) != 0) _sawSolid = true;
        else _sawEmpty = true;
        return true;
    }

    private int CurrentHeadOffset => _crouched ? CrouchedHeadOffset : StandingHeadOffset;

    private bool TryStandingHullClear(CpuContext context, IMemory memory, int x, int y, out bool clear)
    {
        clear = false;
        ReadOnlySpan<int> vertical = [-22, -14, -7, 1, 9, 17, 24];
        ReadOnlySpan<int> horizontal = [-HalfWidth + 1, 0, HalfWidth - 1];
        for (int i = 0; i < vertical.Length; i++)
            for (int j = 0; j < horizontal.Length; j++)
            {
                if (!TryCollision(context, memory, x + horizontal[j], y + vertical[i], out CollisionResult hit))
                    return false;
                if ((hit.Effects & EffectSolid) != 0 && (hit.Effects & EffectSolidFromAbove) == 0)
                    return true;
            }
        clear = true;
        return true;
    }

    private bool TryCrouchedHullClear(CpuContext context, IMemory memory, int x, int y, out bool clear)
    {
        clear = false;
        ReadOnlySpan<int> vertical = [1, 5, 9, 17, 24];
        ReadOnlySpan<int> horizontal = [-HalfWidth + 1, 0, HalfWidth - 1];
        for (int i = 0; i < vertical.Length; i++)
            for (int j = 0; j < horizontal.Length; j++)
            {
                if (!TryCollision(context, memory, x + horizontal[j], y + vertical[i], out CollisionResult hit))
                    return false;
                if ((hit.Effects & EffectSolid) != 0 && (hit.Effects & EffectSolidFromAbove) == 0)
                    return true;
            }
        clear = true;
        return true;
    }

    private bool TryApplyCorrection(ref int position, int correction)
    {
        if (correction is < -64 or > 64)
        {
            _invalidCorrections++;
            _lastRejectedCorrection = correction;
            _collisionDisabled = true;
            _collisionFailureReason = $"Rejected collision correction {correction}";
            return false;
        }
        position = ((position >> 16) + correction) << 16;
        return true;
    }

    private void DrawProxyGp0(CpuContext context, IMemory memory)
    {
        uint currentBuffer = memory.ReadU32(CurrentBufferPointer);
        if (!IsGpuBuffer(currentBuffer))
        {
            _nativeSpriteDrawnThisFrame = false;
            return;
        }
        uint expectedOt = (currentBuffer + OrderingTableOffset) & 0x1FFFFC;
        if ((context.A0 & 0x1FFFFC) != expectedOt) return;
        bool nativeBodyConfirmed = _nativeSpriteDrawnThisFrame;
        _nativeSpriteDrawnThisFrame = false;

        if (!_enabled || !_safeFrame || !_proxyInitialized || !_animationStateValid || _transitionPending ||
            !Game.Available || !Game.InGame || Game.IsLoading || Game.MenuOpen || Game.MapOpen || !DisplayModeHooks.IsStage)
            return;
        _renderEligible++;

        var gpu = RecompOne.Runtime.Runtime.Gpu;
        if (gpu == null) return;

        bool avatarSubmitted = nativeBodyConfirmed && TryDrawIndependentSprite(gpu, memory, currentBuffer);
        if (!avatarSubmitted)
        {
            int scrollX = unchecked((int)memory.ReadU32(ScrollXAddress)) >> 16;
            int scrollY = unchecked((int)memory.ReadU32(ScrollYAddress)) >> 16;
            int x = unchecked((int)memory.ReadU32(BackbufferXAddress)) + (_proxyX >> 16) - scrollX;
            int y = unchecked((int)memory.ReadU32(BackbufferYAddress)) + (_proxyY >> 16) - scrollY;
            if (x >= -32 && x <= 288 && y >= -48 && y <= 288 &&
                WriteStageDrawEnvironment(gpu, memory, currentBuffer))
            {
                int headOffset = CurrentHeadOffset;
                DrawGpuTile(gpu, x - HalfWidth - 1, y + headOffset - 1, HalfWidth * 2 + 2,
                    FootOffset - headOffset + 2, 0, 0, 0);

                byte r = _contactOpportunities.Current != 0 ? (byte)255 : _collisionThisFrame ? (byte)255 : (byte)32;
                byte g = _contactOpportunities.Current != 0 ? (byte)48 : _grounded ? (byte)255 : (byte)192;
                byte b = _contactOpportunities.Current != 0 ? (byte)160 : (byte)255;
                DrawGpuTile(gpu, x - HalfWidth, y + headOffset, HalfWidth * 2,
                    FootOffset - headOffset, r, g, b);
                DrawGpuTile(gpu, x + (_facingLeft ? -5 : 2), y - 6, 3, 3, 255, 232, 32);
                _nativeSpriteFallbacks++;
                _nativeSpriteStreak = 0;
                _nativeSpriteFlipSeenInStreak = false;
                avatarSubmitted = true;
            }
        }
        if (avatarSubmitted) _renderSubmitted++;
        DrawProjectileMarker(gpu, memory, currentBuffer);
        DrawCombatHud(gpu, memory, currentBuffer);
    }

    private void DrawProjectileMarker(RecompOne.Runtime.Gpu gpu, IMemory memory, uint currentBuffer)
    {
        if (_ownedAttackSlot < 0 || !_attackProfileLatched ||
            _latchedAttackProfile.Kind != AttackKind.Projectile) return;
        int scrollX = unchecked((int)memory.ReadU32(ScrollXAddress)) >> 16;
        int scrollY = unchecked((int)memory.ReadU32(ScrollYAddress)) >> 16;
        int x = unchecked((int)memory.ReadU32(BackbufferXAddress)) + (_projectileX >> 16) - scrollX;
        int y = unchecked((int)memory.ReadU32(BackbufferYAddress)) + (_projectileY >> 16) - scrollY;
        if (x is < -8 or > 264 || y is < -8 or > 248 ||
            !WriteStageDrawEnvironment(gpu, memory, currentBuffer)) return;
        DrawGpuTile(gpu, x - 3, y - 3, 7, 7, 0, 0, 0);
        DrawGpuTile(gpu, x - 2, y - 2, 5, 5, 64, 224, 255);
        DrawGpuTile(gpu, x, y, 2, 2, 255, 255, 255);
    }

    private void DrawCombatHud(RecompOne.Runtime.Gpu gpu, IMemory memory, uint currentBuffer)
    {
        _hudEligible++;
        if (!WriteStageDrawEnvironment(gpu, memory, currentBuffer)) return;
        int x = unchecked((int)memory.ReadU32(BackbufferXAddress)) + 8;
        int y = unchecked((int)memory.ReadU32(BackbufferYAddress)) + 34;
        const int barWidth = 72;
        DrawGpuTile(gpu, x, y, 82, 16, 0, 0, 0);
        DrawGpuTile(gpu, x + 2, y + 2, 6, 12, 48, 192, 255);
        DrawGpuTile(gpu, x + 10, y + 3, barWidth, 10, 32, 32, 40);
        int hpWidth = Math.Clamp(_managedHp * barWidth / ManagedMaxHp, 0, barWidth);
        byte hpR = _downed ? (byte)96 : (byte)48;
        byte hpG = _downed ? (byte)32 : (byte)208;
        if (hpWidth > 0) DrawGpuTile(gpu, x + 10, y + 3, hpWidth, 10, hpR, hpG, 255);
        if (_downed)
        {
            int reviveWidth = Math.Clamp(_reviveProgress * barWidth / ReviveUpdates, 0, barWidth);
            if (reviveWidth > 0) DrawGpuTile(gpu, x + 10, y + 11, reviveWidth, 2, 255, 224, 64);
        }
        byte profileR = _attackProfileLatched && _latchedAttackProfile.Kind == AttackKind.Projectile
            ? (byte)255 : (byte)96;
        byte profileG = _ownedAttackSlot >= 0 ? (byte)255 : (byte)96;
        DrawGpuTile(gpu, x + 3, y + 5, 4, 4, profileR, profileG, 64);
        _hudSubmitted++;
    }

    private bool TryDrawIndependentSprite(RecompOne.Runtime.Gpu gpu, IMemory memory, uint currentBuffer)
    {
        if (_independentVisualDisabled) return false;
        _independentVisualEligible++;
        if (!TryGetProxyPose(_animation, _animationFrame, out ProxyPose pose) ||
            !TryResolveSprite(memory, pose.NativeFrame, out SpriteFrame proxySprite))
        {
            _independentVisualFailures++;
            _independentVisualDisabled = true;
            _independentVisualStatus = "TABLE";
            return false;
        }
        if (memory.ReadU16(PlayerDrawAddress) != 513 || memory.ReadU16(PlayerDrawAddress + 2) != 257 ||
            memory.ReadU8(PlayerDrawAddress + 0x1F) != 0x18)
        {
            _independentVisualFailures++;
            _independentVisualDisabled = true;
            _independentVisualStatus = "PDRAW";
            return false;
        }

        ushort clut = memory.ReadU16(AlucardClutAddress);
        if (clut == 0)
        {
            _independentVisualFailures++;
            _independentVisualDisabled = true;
            _independentVisualStatus = "CLUT";
            return false;
        }

        int scrollX = unchecked((int)memory.ReadU32(ScrollXAddress)) >> 16;
        int scrollY = unchecked((int)memory.ReadU32(ScrollYAddress)) >> 16;
        int targetX = unchecked((int)memory.ReadU32(BackbufferXAddress)) + (_proxyX >> 16) - scrollX;
        int targetY = unchecked((int)memory.ReadU32(BackbufferYAddress)) + (_proxyY >> 16) - scrollY;
        int polyX = targetX + proxySprite.PivotX;
        int x0 = _facingLeft ? polyX - proxySprite.Width + 1 : polyX;
        int x1 = _facingLeft ? polyX + 1 : polyX + proxySprite.Width;
        int y0 = targetY + proxySprite.PivotY;
        int y1 = y0 + proxySprite.Height;
        if (x0 is < -1024 or > 1023 || x1 is < -1024 or > 1023 ||
            y0 is < -1024 or > 1023 || y1 is < -1024 or > 1023)
        {
            _independentVisualStatus = "POS";
            return false;
        }

        if (!WriteStageDrawEnvironment(gpu, memory, currentBuffer)) return false;
        gpu.WriteGp0(0xE2000000u);
        int textureHalfwords = checked((proxySprite.Width / 4) * proxySprite.Height);
        Span<ushort> savedTexture = stackalloc ushort[textureHalfwords];
        ReadTextureRect(gpu, proxySprite.Width / 4, proxySprite.Height, savedTexture);
        try
        {
            UploadSprite(gpu, memory, proxySprite);

            byte r = _contactOpportunities.Current != 0 ? (byte)255 : (byte)96;
            byte g = _contactOpportunities.Current != 0 ? (byte)48 : (byte)176;
            byte b = _contactOpportunities.Current != 0 ? (byte)160 : (byte)255;
            uint color = 0x3C000000u | ((uint)b << 16) | ((uint)g << 8) | r;
            uint nextColor = ((uint)b << 16) | ((uint)g << 8) | r;
            byte u0 = _facingLeft ? checked((byte)(4 + proxySprite.Width - 1)) : (byte)4;
            byte u1 = _facingLeft ? (byte)3 : checked((byte)(4 + proxySprite.Width));
            byte v0 = 1;
            byte v1 = checked((byte)(v0 + proxySprite.Height));
            gpu.WriteGp0(color);
            gpu.WriteGp0(PackPosition(x0, y0));
            gpu.WriteGp0(((uint)clut << 16) | ((uint)v0 << 8) | u0);
            gpu.WriteGp0(nextColor);
            gpu.WriteGp0(PackPosition(x1, y0));
            gpu.WriteGp0(0x00180000u | ((uint)v0 << 8) | u1);
            gpu.WriteGp0(nextColor);
            gpu.WriteGp0(PackPosition(x0, y1));
            gpu.WriteGp0(((uint)v1 << 8) | u0);
            gpu.WriteGp0(nextColor);
            gpu.WriteGp0(PackPosition(x1, y1));
            gpu.WriteGp0(((uint)v1 << 8) | u1);
        }
        catch
        {
            // Discard a partial image or polygon command before attempting restoration.
            gpu.WriteGp1(0x01000000u);
            throw;
        }
        finally
        {
            // The restore upload flushes the direct quad before replacing the exact saved texture rectangle.
            UploadTextureRect(gpu, proxySprite.Width / 4, proxySprite.Height, savedTexture);
        }
        _independentVisualSubmitted++;
        _independentNativeFrame = pose.NativeFrame;
        _visualPosesSeen |= 1UL << pose.Index;
        _independentVisualStatus = "OK";

        if ((_independentVisualSubmitted % 60) == 0)
        {
            _independentRestoreChecks++;
            if (!VerifyTextureRect(gpu, proxySprite.Width / 4, proxySprite.Height, savedTexture))
            {
                _independentRestoreFailures++;
                _independentVisualDisabled = true;
                _independentVisualStatus = "RESTORE";
                throw new InvalidOperationException("Player 1 texture restore verification failed");
            }
        }
        return true;
    }

    private static void UploadSprite(RecompOne.Runtime.Gpu gpu, IMemory memory, SpriteFrame sprite)
    {
        int widthWords = sprite.Width / 4;
        int halfwords = checked(widthWords * sprite.Height);
        gpu.WriteGp0(0xA0000000u);
        gpu.WriteGp0((257u << 16) | 513u);
        gpu.WriteGp0(((uint)sprite.Height << 16) | (uint)widthWords);
        for (int i = 0; i < halfwords; i += 2)
        {
            uint address = sprite.Pixels + (uint)(i * 2);
            uint word = memory.ReadU16(address);
            if (i + 1 < halfwords) word |= (uint)memory.ReadU16(address + 2) << 16;
            gpu.WriteGp0(word);
        }
    }

    private static void UploadTextureRect(RecompOne.Runtime.Gpu gpu, int widthWords, int height, ReadOnlySpan<ushort> pixels)
    {
        int halfwords = checked(widthWords * height);
        gpu.WriteGp0(0xA0000000u);
        gpu.WriteGp0((257u << 16) | 513u);
        gpu.WriteGp0(((uint)height << 16) | (uint)widthWords);
        for (int i = 0; i < halfwords; i += 2)
        {
            uint word = pixels[i];
            if (i + 1 < halfwords) word |= (uint)pixels[i + 1] << 16;
            gpu.WriteGp0(word);
        }
    }

    private static void ReadTextureRect(RecompOne.Runtime.Gpu gpu, int widthWords, int height, Span<ushort> pixels)
    {
        int halfwords = checked(widthWords * height);
        gpu.WriteGp0(0xC0000000u);
        gpu.WriteGp0((257u << 16) | 513u);
        gpu.WriteGp0(((uint)height << 16) | (uint)widthWords);
        for (int i = 0; i < halfwords; i += 2)
        {
            uint word = gpu.ReadData();
            pixels[i] = (ushort)word;
            if (i + 1 < halfwords) pixels[i + 1] = (ushort)(word >> 16);
        }
    }

    private static bool VerifyTextureRect(RecompOne.Runtime.Gpu gpu, int widthWords, int height, ReadOnlySpan<ushort> expected)
    {
        int halfwords = checked(widthWords * height);
        bool matches = true;
        gpu.WriteGp0(0xC0000000u);
        gpu.WriteGp0((257u << 16) | 513u);
        gpu.WriteGp0(((uint)height << 16) | (uint)widthWords);
        for (int i = 0; i < halfwords; i += 2)
        {
            uint actual = gpu.ReadData();
            if ((ushort)actual != expected[i]) matches = false;
            if (i + 1 < halfwords && (ushort)(actual >> 16) != expected[i + 1]) matches = false;
        }
        return matches;
    }

    private static uint PackPosition(int x, int y) => ((uint)(ushort)y << 16) | (ushort)x;

    private void PrepareNativeSpriteCapture(IMemory memory)
    {
        _nativeCapturePending = false;
        _nativeSpriteDrawnThisFrame = false;
        if (!_enabled || _fatal || !_safeFrame || !_proxyInitialized || !Game.Available || !Game.InGame ||
            Game.IsLoading || Game.MenuOpen || Game.MapOpen || !DisplayModeHooks.IsStage || _transitionPending)
        {
            _nativeSpriteStatus = "WAIT";
            ResetNativeSpriteStreak();
            return;
        }

        short animSet = unchecked((short)memory.ReadU16(PlayerEntityAddress + EntityAnimSetOffset));
        ushort animFrame = memory.ReadU16(PlayerEntityAddress + EntityAnimFrameOffset);
        if (animSet != 1)
        {
            _nativeSpriteStatus = "ANIM";
            ResetNativeSpriteStreak();
            return;
        }
        if ((animFrame & 0x7FFF) == 0)
        {
            _nativeSpriteStatus = "FRAME";
            ResetNativeSpriteStreak();
            return;
        }
        if (memory.ReadU16(PlayerEntityAddress + EntityPlayerDrawOffset) != 0)
        {
            _nativeSpriteStatus = "PDRAW";
            ResetNativeSpriteStreak();
            return;
        }

        byte drawFlags = memory.ReadU8(PlayerEntityAddress + EntityDrawFlagsOffset);
        if ((drawFlags & EntityBlink) != 0 && (memory.ReadU32(TimerAddress) & 1) != 0)
        {
            _nativeSpriteStatus = "BLINK";
            ResetNativeSpriteStreak();
            return;
        }

        int backbufferX = unchecked((int)memory.ReadU32(BackbufferXAddress));
        int backbufferY = unchecked((int)memory.ReadU32(BackbufferYAddress));
        int anchorX = unchecked((short)memory.ReadU16(PlayerEntityAddress + 0x02)) + backbufferX;
        int anchorY = unchecked((short)memory.ReadU16(PlayerEntityAddress + 0x06)) + backbufferY;
        if (anchorX is < -512 or > 512 || anchorY is < -512 or > 512)
        {
            _nativeSpriteStatus = "OFF";
            ResetNativeSpriteStreak();
            return;
        }

        uint buffer = memory.ReadU32(CurrentBufferPointer);
        uint gt4 = memory.ReadU32(GpuUsageGt4Address);
        if (!IsGpuBuffer(buffer))
        {
            _nativeSpriteStatus = "BUFFER";
            ResetNativeSpriteStreak();
            return;
        }
        if (gt4 >= MaxGpuGt4)
        {
            _nativeSpriteStatus = "POOL";
            ResetNativeSpriteStreak();
            return;
        }

        _nativeCaptureBuffer = buffer;
        _nativeCaptureGt4 = gt4;
        _nativeCaptureAnchorX = anchorX;
        _nativeCaptureAnchorY = anchorY;
        _nativeCaptureFacingLeft = memory.ReadU16(PlayerEntityAddress + EntityFacingOffset) != 0;
        _nativeCapturePending = true;
        _nativeSpriteEligible++;
        _nativeSpriteStatus = "READY";
    }

    private void SubmitNativeSprite(IMemory memory)
    {
        if (!_nativeCapturePending) return;
        _nativeCapturePending = false;

        uint buffer = memory.ReadU32(CurrentBufferPointer);
        uint used = memory.ReadU32(GpuUsageGt4Address);
        if (buffer != _nativeCaptureBuffer || used <= _nativeCaptureGt4)
        {
            _nativeSpriteStatus = "POST";
            ResetNativeSpriteStreak();
            return;
        }
        if (used >= MaxGpuGt4)
        {
            _nativeSpriteStatus = "POOL";
            ResetNativeSpriteStreak();
            return;
        }

        uint source = buffer + GpuGt4Offset + _nativeCaptureGt4 * GpuGt4Stride;
        uint sourceTag = memory.ReadU32(source);
        byte command = memory.ReadU8(source + 0x07);
        if ((sourceTag >> 24) != 12)
        {
            _nativeSpriteStatus = "TAG";
            ResetNativeSpriteStreak();
            return;
        }
        if ((command & 0xFC) != 0x3C)
        {
            _nativeSpriteStatus = "CMD";
            ResetNativeSpriteStreak();
            return;
        }
        ReadOnlySpan<uint> coordinateOffsets = [0x08, 0x14, 0x20, 0x2C];
        for (int i = 0; i < coordinateOffsets.Length; i++)
        {
            int x = unchecked((short)memory.ReadU16(source + coordinateOffsets[i]));
            int y = unchecked((short)memory.ReadU16(source + coordinateOffsets[i] + 2));
            if (Math.Abs(x - _nativeCaptureAnchorX) <= 128 && Math.Abs(y - _nativeCaptureAnchorY) <= 128) continue;
            _nativeSpriteStatus = "OWNER";
            ResetNativeSpriteStreak();
            return;
        }

        _nativeSpriteCaptured++;
        _nativeSpriteDrawnThisFrame = true;
        _nativeSpriteSubmitted++;
        bool oppositeFacing = _nativeCaptureFacingLeft != _facingLeft;
        if (oppositeFacing) _nativeSpriteFlipped++;
        _nativeSpriteStreak++;
        if (oppositeFacing) _nativeSpriteFlipSeenInStreak = true;
        _nativeSpriteStatus = "OK";
    }

    private void ResetNativeSpriteFrame()
    {
        _nativeCapturePending = false;
        _nativeSpriteDrawnThisFrame = false;
        ResetNativeSpriteStreak();
        _nativeSpriteStatus = "WAIT";
        if (!_independentVisualDisabled) _independentVisualStatus = "WAIT";
    }

    private void ResetNativeSpriteStreak()
    {
        _nativeSpriteStreak = 0;
        _nativeSpriteFlipSeenInStreak = false;
    }

    private void UpdateContactShadow(IMemory memory)
    {
        if (_contactDisabled)
        {
            _contactStatus = _contactGuardFailures > 0 ? "GUARD" : "MEM";
            return;
        }
        if (!ContactScanAllowed(memory))
        {
            SuspendContactScan("WAIT");
            return;
        }
        if (memory is not PSMemory psMemory)
        {
            DisableContactScan("MEM", "memory");
            return;
        }

        ReadOnlySpan<byte> ram = psMemory.Ram;
        if (!TryBuildContactShape(ram, out ContactShape shape))
        {
            SuspendContactScan("SHAPE");
            return;
        }

        ContactGuardSnapshot before = ContactGuardSnapshot.Capture(ram);
        ContactScanResult result = ScanContactCandidates(ram, shape);
        ContactGuardSnapshot after = ContactGuardSnapshot.Capture(ram);
        _contactGuardChecks++;
        if (!before.Matches(after))
        {
            _contactGuardFailures++;
            DisableContactScan("GUARD", before.FirstDifference(after));
            return;
        }

        CommitContactScan(result);
    }

    private bool ContactScanAllowed(IMemory memory) =>
        !_fatal && !_contactDisabled && _safeFrame && _animationStateValid && AutomaticInputSafe(memory) &&
        _roomKnown && ReadRoomIdentity(memory).Equals(_room);

    private bool TryBuildContactShape(ReadOnlySpan<byte> ram, out ContactShape shape)
    {
        shape = default;
        if (!_animationStateValid || !TryGetProxyPose(_animation, _animationFrame, out ProxyPose pose)) return false;
        ProxyHurtbox hurtbox = pose.Hurtbox;
        int offsetX = hurtbox.OffsetX;
        int offsetY = hurtbox.OffsetY;
        int halfWidth = hurtbox.HalfWidth;
        int halfHeight = hurtbox.HalfHeight;

        int scrollX = ReadRamS32(ram, ScrollXAddress) >> 16;
        int scrollY = ReadRamS32(ram, ScrollYAddress) >> 16;
        int playerX = ReadRamS16(ram, PlayerEntityAddress + 0x02);
        bool shiftEnabled = !RecompOne.Runtime.Runtime.View.GetBool("WidescreenOriginalAspect", true) &&
            RecompOne.Runtime.Hle.Display.WideMargin(256) != 0;
        int widescreenShift = !shiftEnabled ? 0 :
            playerX > 256 ? 256 - playerX : playerX < 0 ? -playerX : 0;
        int centerX = (_proxyX >> 16) - scrollX + (_facingLeft ? -offsetX : offsetX) + widescreenShift;
        int centerY = (_proxyY >> 16) - scrollY + offsetY;
        if (centerX is < -32 or > 288 || centerY is < -32 or > 256) return false;

        _contactOffsetX = offsetX;
        _contactOffsetY = offsetY;
        _contactHalfWidth = halfWidth;
        _contactHalfHeight = halfHeight;
        _contactShapeCenterX = centerX;
        _ownedHurtboxSamples++;
        _ownedHurtboxStatesSeen |= 1 << (int)_animation;
        _hurtboxPosesSeen |= 1UL << pose.Index;
        shape = new ContactShape(centerX, centerY, halfWidth, halfHeight, widescreenShift);
        return true;
    }

    private ContactScanResult ScanContactCandidates(ReadOnlySpan<byte> ram, ContactShape shape)
    {
        Array.Clear(_contactObservations);
        int eligible = 0;
        int overlaps = 0;
        int damaging = 0;
        int current = 0;
        int lastSlot = -1;
        ushort lastEntityId = 0;
        long nearestSquared = long.MaxValue;
        int nearestSlot = -1;
        ushort nearestEntityId = 0;
        ushort nearestEnemyId = 0;
        short nearestHp = 0;
        int nearestP1Distance = 0;
        int nearestP2Distance = 0;
        bool nearestCompatible = false;
        int p1X = ReadRamS16(ram, PlayerEntityAddress + 0x02) + shape.WidescreenShift;
        int p1Y = ReadRamS16(ram, PlayerEntityAddress + 0x06);
        ushort compatibleState = _attackProfileLatched ? _latchedAttackProfile.HitState : (ushort)2;
        _enemyDiagnosticScans++;

        for (int i = 0; i < ContactSlotCount; i++)
        {
            int slot = ContactSlotStart + i;
            uint entity = Game.EntitiesAddr + (uint)(slot * Entity.Stride);
            ushort state = ReadRamU16(ram, entity + EntityHitboxStateOffset);
            int halfWidth = ReadRamU8(ram, entity + EntityHitboxWidthOffset);
            int halfHeight = ReadRamU8(ram, entity + EntityHitboxHeightOffset);
            if (halfWidth == 0 || halfHeight == 0) continue;
            if ((ReadRamU32(ram, entity + EntityFlagsOffset) & EntityDead) != 0) continue;

            ushort entityId = ReadRamU16(ram, entity + EntityIdOffset);
            uint update = ReadRamU32(ram, entity + EntityUpdateOffset);
            if (entityId == 0 || update == 0) continue;

            int offsetX = ReadRamS16(ram, entity + EntityHitboxOffsetX);
            int offsetY = ReadRamS16(ram, entity + EntityHitboxOffsetY);
            bool facingLeft = ReadRamU16(ram, entity + EntityFacingOffset) != 0;
            int centerX = ReadRamS16(ram, entity + 0x02) + (facingLeft ? -offsetX : offsetX) + shape.WidescreenShift;
            int centerY = ReadRamS16(ram, entity + 0x06) + offsetY;
            if (centerX is <= -32 or >= 288 || centerY is <= -32 or >= 256) continue;

            if (IsNativeTargetBody(state))
            {
                _enemyNativeCandidateSamples++;
                if ((state & compatibleState) != 0) _enemyCompatibleCandidateSamples++;
                long p2Dx = centerX - shape.CenterX;
                long p2Dy = centerY - shape.CenterY;
                long p2Squared = p2Dx * p2Dx + p2Dy * p2Dy;
                if (p2Squared < nearestSquared)
                {
                    long p1Dx = centerX - p1X;
                    long p1Dy = centerY - p1Y;
                    nearestSquared = p2Squared;
                    nearestSlot = slot;
                    nearestEntityId = entityId;
                    nearestEnemyId = ReadRamU16(ram, entity + EntityEnemyIdOffset);
                    nearestHp = ReadRamS16(ram, entity + 0x3E);
                    nearestP1Distance = (int)Math.Sqrt(p1Dx * p1Dx + p1Dy * p1Dy);
                    nearestP2Distance = (int)Math.Sqrt(p2Squared);
                    nearestCompatible = (state & compatibleState) != 0;
                }
            }
            if ((state & 1) == 0) continue;
            _contactObservations[i] = new ContactObservation(true, false, entityId, 0, update,
                state, 0, 0, 0, 0);
            eligible++;

            if (Math.Abs(centerX - shape.CenterX) >= halfWidth + shape.HalfWidth ||
                Math.Abs(centerY - shape.CenterY) >= halfHeight + shape.HalfHeight)
                continue;

            ushort enemyId = ReadRamU16(ram, entity + EntityEnemyIdOffset);
            overlaps++;
            current++;
            short attack = ReadRamS16(ram, entity + EntityAttackOffset);
            if (attack > 0) damaging++;
            ushort element = ReadRamU16(ram, entity + EntityAttackElementOffset);
            _contactObservations[i] = new ContactObservation(true, true, entityId, enemyId, update,
                state, attack, element, checked((short)centerX), checked((short)centerY));
            lastSlot = slot;
            lastEntityId = entityId;
        }

        _nearestTargetSlot = nearestSlot;
        _nearestTargetEntityId = nearestEntityId;
        _nearestTargetEnemyId = nearestEnemyId;
        _nearestTargetHp = nearestHp;
        _nearestTargetP1Distance = nearestP1Distance;
        _nearestTargetP2Distance = nearestP2Distance;
        _nearestTargetCompatible = nearestCompatible;
        _enemyDiagnosticStatus = nearestSlot >= 0 ? "TARGET" : "EMPTY";

        return new ContactScanResult(eligible, overlaps, damaging, current, lastSlot, lastEntityId);
    }

    private void CommitContactScan(ContactScanResult result)
    {
        if (result.LastSlot >= 0)
        {
            _contactLastSlot = result.LastSlot;
            _contactLastEntityId = result.LastEntityId;
        }
        ContactOpportunityTransition transition = _contactOpportunities.Advance(_contactObservations);
        for (int index = 0; index < transition.OpportunityCount; index++)
            _health = ManagedHealthMachine.ConsumeOpportunity(_health);
        if (transition.HasWinner) ApplyManagedDamage(transition.Winner);
        _contactStatus = _contactOpportunities.Current != 0 ? "CONTACT" : "OK";
    }

    private void ApplyManagedDamage(ContactDamageOpportunity opportunity)
    {
        ManagedDamageTransition transition = ManagedHealthMachine.ApplyIncomingHit(_health, opportunity.Damage,
            ContactSlotStart + opportunity.Index, opportunity.Element, _crouched);
        _health = transition.State;
        if (!transition.Applied) return;
        _velocityX = opportunity.CenterX < _contactShapeCenterX ? 0x28000 : -0x28000;
        _velocityY = -0x38000;
        _grounded = false;
        _attackTimer = 0;
        _attackSpawnPending = false;
        CancelOwnedAttack("HURT");
        _attackStatus = "CANCEL:HURT";
        if (transition.Lethal)
        {
            _stance.ApplyLethalDamage();
        }
    }

    private void SuspendContactScan(string status)
    {
        _contactOpportunities.Suspend();
        Array.Clear(_contactObservations);
        _nearestTargetSlot = -1;
        _nearestTargetEntityId = _nearestTargetEnemyId = 0;
        _nearestTargetHp = 0;
        _nearestTargetP1Distance = _nearestTargetP2Distance = 0;
        _nearestTargetCompatible = false;
        _enemyDiagnosticStatus = status == "WAIT" ? "WAIT" : status;
        _contactStatus = status;
    }

    private void DisableContactScan(string status, string region)
    {
        SuspendContactScan(status);
        _contactDisabled = true;
        _contactGuardRegion = region;
        _contactStatus = status;
    }

    private static byte ReadRamU8(ReadOnlySpan<byte> ram, uint address) =>
        ram[RamOffset(ram, address, 1)];

    private static ushort ReadRamU16(ReadOnlySpan<byte> ram, uint address)
    {
        int offset = RamOffset(ram, address, 2);
        return (ushort)(ram[offset] | (ram[offset + 1] << 8));
    }

    private static uint ReadRamU32(ReadOnlySpan<byte> ram, uint address)
    {
        int offset = RamOffset(ram, address, 4);
        return (uint)(ram[offset] | (ram[offset + 1] << 8) |
            (ram[offset + 2] << 16) | (ram[offset + 3] << 24));
    }

    private static short ReadRamS16(ReadOnlySpan<byte> ram, uint address) =>
        unchecked((short)ReadRamU16(ram, address));

    private static int ReadRamS32(ReadOnlySpan<byte> ram, uint address) =>
        unchecked((int)ReadRamU32(ram, address));

    private static int RamOffset(ReadOnlySpan<byte> ram, uint address, int length)
    {
        int offset = checked((int)(address & 0x1FFFFFFF));
        if (offset < 0 || length < 0 || offset > ram.Length - length)
            throw new InvalidOperationException($"Protected RAM range is invalid: 0x{address:X8}+0x{length:X}");
        return offset;
    }

    private static ulong HashRamRange(ReadOnlySpan<byte> ram, uint address, int length)
    {
        int offset = RamOffset(ram, address, length);
        ulong hash = 14695981039346656037UL;
        hash = (hash ^ address) * 1099511628211UL;
        hash = (hash ^ (uint)length) * 1099511628211UL;
        for (int i = 0; i < length; i++) hash = (hash ^ ram[offset + i]) * 1099511628211UL;
        return hash;
    }

    private static bool WriteStageDrawEnvironment(RecompOne.Runtime.Gpu gpu, IMemory memory, uint buffer)
    {
        int clipX = (short)memory.ReadU16(buffer + 0x04);
        int clipY = (short)memory.ReadU16(buffer + 0x06);
        int clipW = (short)memory.ReadU16(buffer + 0x08);
        int clipH = (short)memory.ReadU16(buffer + 0x0A);
        int offsetX = (short)memory.ReadU16(buffer + 0x0C);
        int offsetY = (short)memory.ReadU16(buffer + 0x0E);
        if (clipW <= 0 || clipH <= 0) return false;

        gpu.WriteGp0(0xE3000000u |
            (((uint)clipY & 0x3FF) << 10) | ((uint)clipX & 0x3FF));
        gpu.WriteGp0(0xE4000000u |
            (((uint)(clipY + clipH - 1) & 0x3FF) << 10) |
            ((uint)(clipX + clipW - 1) & 0x3FF));
        gpu.WriteGp0(0xE5000000u |
            (((uint)offsetY & 0x7FF) << 11) | ((uint)offsetX & 0x7FF));
        gpu.WriteGp0(0xE6000000u);
        return true;
    }

    private static void DrawGpuTile(RecompOne.Runtime.Gpu gpu, int x, int y, int width, int height,
        byte r, byte g, byte b)
    {
        gpu.WriteGp0(0x60000000u | ((uint)b << 16) | ((uint)g << 8) | r);
        gpu.WriteGp0((((uint)y & 0x7FF) << 16) | ((uint)x & 0x7FF));
        gpu.WriteGp0(((uint)height << 16) | (uint)width);
    }

    private bool TryGetSafeState(IMemory memory, out string reason)
    {
        _safeCode = "OK";
        reason = "Ready";
        if (!Game.Available || !Game.InGame) return Unsafe("GAME", "Not in gameplay", out reason);
        if (!Game.InAlucardMode()) return Unsafe("CHAR", "Unsupported character or prologue", out reason);
        if (Game.IsLoading) return Unsafe("LOAD", "Game is loading", out reason);
        if (memory.ReadU32(GameStepAddress) != (uint)PlayStep.Default) return Unsafe("STEP", "Play step is not normal", out reason);
        if (memory.ReadU32(EngineStepAddress) != 1) return Unsafe("ENG", "Engine step is not normal", out reason);
        if (Game.MenuOpen || Game.MapOpen) return Unsafe("MENU", "Menu or map is open", out reason);
        if (!DisplayModeHooks.IsStage) return Unsafe("DISP", "Display is not in stage mode", out reason);
        if (memory.ReadU32(CutsceneControlAddress) != 0) return Unsafe("CUT", "Cutscene owns player control", out reason);
        if (IsSpecialTransition(memory.ReadU32(SpecialTransitionAddress))) return Unsafe("TRANS", "Special transition is active", out reason);
        uint foreground = memory.ReadU32(TilemapAddress);
        uint tileDefinitions = memory.ReadU32(TileDefinitionsAddress);
        if (!IsGuestPointer(foreground) || !IsGuestPointer(tileDefinitions)) return Unsafe("TILE", "Tilemap pointers are invalid", out reason);
        uint collisionTable = memory.ReadU32(tileDefinitions + 0x0C);
        if (!IsGuestPointer(collisionTable)) return Unsafe("COLPTR", "Tile collision table is invalid", out reason);
        uint horizontalSize = memory.ReadU32(TilemapAddress + 0x20);
        uint verticalSize = memory.ReadU32(TilemapAddress + 0x24);
        if (horizontalSize is 0 or > 0x100 || verticalSize is 0 or > 0x100) return Unsafe("DIM", "Tilemap dimensions are invalid", out reason);

        _collisionFunction = memory.ReadU32(GameApi.CheckCollisionAddr);
        if (_collisionFunction != ExpectedCollisionFunction)
        {
            _collisionDisabled = true;
            _collisionFailureReason = $"Collision API mismatch: expected 0x{ExpectedCollisionFunction:X8}, got 0x{_collisionFunction:X8}";
            return Unsafe("API", $"Collision API mismatch: 0x{_collisionFunction:X8}", out reason);
        }
        return true;
    }

    private bool Unsafe(string code, string value, out string reason)
    {
        _safeCode = code;
        reason = value;
        return false;
    }

    private bool TryGetAwarenessTarget(IMemory memory, out int slot, out int entityX,
        out int entityY, out int p2X, out int p2Y)
    {
        slot = -1;
        entityX = entityY = p2X = p2Y = 0;
        if (_awarenessDisabled)
        {
            _awarenessStatus = "DISABLED";
            return false;
        }
        if (memory.ReadU8(Game.StageIdAddr) != (byte)Stage.CenterCube || !_enabled || _fatal ||
            !_safeFrame || !_proxyInitialized || !_animationStateValid || _downed ||
            _transitionPending || _roomStableFrames < ReconstructionStableUpdates ||
            !_roomKnown || !ReadRoomIdentity(memory).Equals(_room) || !Game.Available || !Game.InGame ||
            !Game.InAlucardMode() || Game.IsLoading || Game.MenuOpen || Game.MapOpen ||
            !DisplayModeHooks.IsStage || memory.ReadU32(GameStepAddress) != (uint)PlayStep.Default ||
            memory.ReadU32(EngineStepAddress) != 1 || memory.ReadU32(CutsceneControlAddress) != 0 ||
            IsSpecialTransition(memory.ReadU32(SpecialTransitionAddress)))
        {
            DisarmAwareness("WAIT");
            return false;
        }

        uint current = memory.ReadU32(Game.CurrentEntityAddr);
        uint first = Game.EntitiesAddr + ContactSlotStart * (uint)Entity.Stride;
        uint end = first + ContactSlotCount * (uint)Entity.Stride;
        if (current < first || current >= end || (current - Game.EntitiesAddr) % Entity.Stride != 0)
        {
            DisarmAwareness("ENTITY");
            return false;
        }
        slot = checked((int)((current - Game.EntitiesAddr) / Entity.Stride));
        ushort state = memory.ReadU16(current + EntityHitboxStateOffset);
        if (memory.ReadU16(current + EntityIdOffset) == 0 ||
            memory.ReadU32(current + EntityUpdateOffset) == 0 || !IsNativeTargetBody(state) ||
            memory.ReadU8(current + EntityHitboxWidthOffset) == 0 ||
            memory.ReadU8(current + EntityHitboxHeightOffset) == 0 ||
            (memory.ReadU32(current + EntityFlagsOffset) & EntityDead) != 0)
        {
            DisarmAwareness("BODY");
            return false;
        }

        entityX = unchecked((short)memory.ReadU16(current + 0x02));
        entityY = unchecked((short)memory.ReadU16(current + 0x06));
        int p1X = unchecked((short)memory.ReadU16(PlayerEntityAddress + 0x02));
        int p1Y = unchecked((short)memory.ReadU16(PlayerEntityAddress + 0x06));
        int scrollX = unchecked((int)memory.ReadU32(ScrollXAddress)) >> 16;
        int scrollY = unchecked((int)memory.ReadU32(ScrollYAddress)) >> 16;
        p2X = (_proxyX >> 16) - scrollX;
        p2Y = (_proxyY >> 16) - scrollY;
        if (p2X is < -32 or > 288 || p2Y is < -32 or > 256)
        {
            DisarmAwareness("OFF");
            return false;
        }
        long p1Dx = p1X - entityX;
        long p1Dy = p1Y - entityY;
        long p2Dx = p2X - entityX;
        long p2Dy = p2Y - entityY;
        long p1Squared = p1Dx * p1Dx + p1Dy * p1Dy;
        long p2Squared = p2Dx * p2Dx + p2Dy * p2Dy;
        if (p2Squared + AwarenessHysteresisSquared >= p1Squared)
        {
            DisarmAwareness("P1");
            return false;
        }
        return true;
    }

    private void DisarmAwareness(string status)
    {
        _awarenessChosenSlot = -1;
        if (!_awarenessDisabled) _awarenessStatus = status;
    }

    private static bool IsNativeTargetBody(ushort state) => (state & 0x3E) != 0;

    private static RoomIdentity ReadRoomIdentity(IMemory memory) => new(
        memory.ReadU8(Game.StageIdAddr),
        memory.ReadU8(Game.RoomAddr),
        memory.ReadU8(Game.AreaAddr),
        unchecked((int)memory.ReadU32(RoomLeftAddress)),
        unchecked((int)memory.ReadU32(RoomTopAddress)),
        unchecked((int)memory.ReadU32(RoomRightAddress)),
        unchecked((int)memory.ReadU32(RoomBottomAddress)));

    private ReconstructionRunResult TryReconstructProxy(CpuContext context, IMemory memory, string reason,
        ManagedMovementReconstructionContinuation continuation)
    {
        SuspendContactScan("RECON");
        int playerX = unchecked((int)memory.ReadU32(PlayerWorldXAddress));
        int playerY = unchecked((int)memory.ReadU32(PlayerWorldYAddress));
        var adapter = new LiveReconstructionAdapter(this, context, memory, reason, continuation);
        return ReconstructionPolicyOrchestration.Run(_reconstructionPolicy, playerX, playerY, ref adapter);
    }

    private struct LiveReconstructionAdapter : IReconstructionPolicyAdapter
    {
        private readonly CoopFeasibility _mod;
        private readonly CpuContext _context;
        private readonly IMemory _memory;
        private readonly string _reason;
        private readonly ManagedMovementReconstructionContinuation _continuation;
        private int _preparedX;
        private int _preparedY;
        private bool _preparedCrouched;
        private ReconstructionCandidate _preparedCandidate;
        private bool _prepared;

        public LiveReconstructionAdapter(CoopFeasibility mod, CpuContext context, IMemory memory, string reason,
            ManagedMovementReconstructionContinuation continuation)
        {
            _mod = mod;
            _context = context;
            _memory = memory;
            _reason = reason;
            _continuation = continuation;
            _preparedX = _preparedY = 0;
            _preparedCrouched = false;
            _preparedCandidate = default;
            _prepared = false;
        }

        public ReconstructionObservation ProbeCandidate(int worldX, int worldY, bool crouched)
        {
            if (_mod.ValidateReconstructionCandidate(_context, _memory, worldX, worldY, crouched))
                return ReconstructionObservation.Valid;
            return _mod._collisionQueryFailed || _mod._collisionDisabled
                ? ReconstructionObservation.AdapterFault
                : ReconstructionObservation.Blocked;
        }

        public void PrepareInitialization(int worldX, int worldY, bool crouched)
        {
            _preparedX = worldX;
            _preparedY = worldY;
            _preparedCrouched = crouched;
            _prepared = true;
        }

        public void PreparePoseProjection()
        {
            if (!_prepared) throw new InvalidOperationException("Reconstruction initialization was not prepared.");
        }

        public void PrepareHealthProjection()
        {
            if (!_prepared) throw new InvalidOperationException("Reconstruction initialization was not prepared.");
        }

        public void PrepareSuccessDiagnostics(ReconstructionCandidate candidate)
        {
            if (!_prepared) throw new InvalidOperationException("Reconstruction was not prepared.");
            _preparedCandidate = candidate;
        }

        public void CommitPreparedSuccess()
        {
            if (!_prepared) throw new InvalidOperationException("Reconstruction was not prepared.");
            var commit = new LiveReconstructionCommitAdapter(_mod, _memory, _reason,
                _preparedX, _preparedY, _preparedCrouched, _preparedCandidate, _continuation);
            if (ManagedReconstructionCommitOrchestration.Run(ref commit) != ReconstructionRunResult.Selected)
                throw new InvalidOperationException("Reconstruction commit preparation failed.");
        }

        public void CommitCollisionFault()
        {
            _mod._reconstructionStatus = $"{_reason}:COLLISION_SUSPEND";
        }

        public void CommitNoSafeCandidate()
        {
            _mod._reconstructionStatus = $"{_reason}:NO_SAFE_CANDIDATE";
            _mod._operationStatus = "Reconstruction suspended: no validated position beside Player 1";
        }
    }

    private struct LiveReconstructionCommitAdapter : IManagedReconstructionCommitAdapter
    {
        private readonly CoopFeasibility _mod;
        private readonly IMemory _memory;
        private readonly string _reason;
        private readonly int _worldX;
        private readonly int _worldY;
        private readonly bool _crouched;
        private readonly ReconstructionCandidate _candidate;
        private readonly ManagedMovementReconstructionContinuation _continuation;
        private int _proxyX;
        private int _proxyY;
        private ManagedStanceInitialization _stance;
        private JumpForgivenessClearPreparation _jump;
        private ManagedLocomotionInitialization _locomotion;
        private ProxyPoseValidationProjection _pose;
        private ManagedHealthState _health;
        private ManagedMovementReconstructionCompletion _session;
        private string? _status;

        public LiveReconstructionCommitAdapter(CoopFeasibility mod, IMemory memory, string reason,
            int worldX, int worldY, bool crouched, ReconstructionCandidate candidate,
            ManagedMovementReconstructionContinuation continuation)
        {
            _mod = mod;
            _memory = memory;
            _reason = reason;
            _worldX = worldX;
            _worldY = worldY;
            _crouched = crouched;
            _candidate = candidate;
            _continuation = continuation;
            _proxyX = _proxyY = 0;
            _stance = default;
            _jump = default;
            _locomotion = default;
            _pose = default;
            _health = mod._health;
            _session = default;
            _status = null;
        }

        public void PrepareScalars()
        {
            _proxyX = checked(_worldX * FixedOne);
            _proxyY = checked(_worldY * FixedOne);
        }

        public void PrepareStance() => _stance = _mod._stance.PrepareInitialization(_crouched);
        public void PrepareJump() => _jump = _mod._jumpForgiveness.PrepareClear();
        public void PrepareLocomotion() => _locomotion = _mod._locomotionState.PrepareInitialization();
        public void ValidatePoseProjection() => _pose = _mod.PrepareAllProxyPoses(_memory);
        public void PrepareHealthProjection() => _health = ManagedHealthMachine.Reconstructed(_mod._health);
        public void PrepareSessionCompletion() => _session = _mod._movementSession
            .PrepareReconstructionCompletion(_continuation, ManagedMovementReconstructionResult.Selected);
        public void PrepareDiagnostics() => _status =
            $"{_reason}:{(_candidate.OffsetX < 0 ? 'L' : 'R')}{Math.Abs(_candidate.OffsetX)}/Y{_candidate.OffsetY}/{(_candidate.Crouched ? 'C' : 'S')}";

        public bool CanCommit() => _status != null &&
            _mod._stance.CanCommitInitialization(_stance) &&
            _mod._jumpForgiveness.CanCommitClear(_jump) &&
            _mod._locomotionState.CanCommitInitialization(_locomotion) &&
            _mod._movementSession.CanCommitReconstructionCompletion(_session);

        public bool CommitPrepared()
        {
            // CanCommit immediately precedes this synchronous publication. These reducer commits
            // contain no fallible work and cannot fail without an intervening mutation.
            _mod._attackTimer = 0;
            _mod._attackSpawnPending = false;
            _mod._proxyX = _proxyX;
            _mod._proxyY = _proxyY;
            _mod._velocityX = 0;
            _mod._velocityY = 0;
            _mod._grounded = true;
            _mod._stance.CommitPreparedInitialization(_stance);
            _mod._jumpPending = false;
            _mod._jumpForgiveness.CommitPreparedClear(_jump);
            _mod._locomotionState.CommitPreparedInitialization(_locomotion);
            _mod.ApplyProxyPoseProjection(_pose);
            _mod._health = _health;
            _mod._reconstructionStatus = _status!;
            _mod._movementSession.CommitPreparedReconstructionCompletion(_session);
            return true;
        }
    }

    private readonly struct ProxyPoseValidationProjection
    {
        public readonly bool TableValidated;
        public readonly bool Disabled;
        public readonly int Failures;
        public readonly string Status;
        public ProxyPoseValidationProjection(bool tableValidated, bool disabled, int failures,
            string status)
        {
            TableValidated = tableValidated;
            Disabled = disabled;
            Failures = failures;
            Status = status;
        }
    }

    private bool ValidateReconstructionCandidate(CpuContext context, IMemory memory, int x, int y, bool crouched)
    {
        bool clear;
        bool queried = crouched
            ? TryCrouchedHullClear(context, memory, x, y, out clear)
            : TryStandingHullClear(context, memory, x, y, out clear);
        if (!queried || !clear) return false;
        ReadOnlySpan<int> floorX = [-HalfWidth + 1, 0, HalfWidth - 1];
        bool supported = false;
        for (int i = 0; i < floorX.Length; i++)
        {
            if (!TryCollision(context, memory, x + floorX[i], y + FootOffset + 1, out CollisionResult hit))
                return false;
            if (BlocksFloor(hit.Effects) && hit.FloorCorrection is >= -4 and <= 0) supported = true;
        }
        return supported;
    }

    private void BeginReconstruction(string reason)
    {
        CancelOwnedAttack($"RECON:{reason}");
        ClearLatchedAttackProfile();
        DisarmAwareness("RECON");
        SuspendContactScan("RECON");
        _movementSession.BeginRecovery(reason == "TETHER"
            ? ManagedMovementRecoveryKind.Tether : ManagedMovementRecoveryKind.Collision);
        _locomotionState.Invalidate();
        _attackTimer = 0;
        _attackSpawnPending = false;
        _velocityX = _velocityY = 0;
        _reconstructionStatus = reason;
        ClearJumpForgiveness();
    }

    private static int AnimationFrameCount(ManagedAnimation animation) =>
        ManagedLocomotionCatalog.FrameCount(animation);

    private static bool TryGetProxyPose(ManagedAnimation animation, int frame, out ProxyPose pose)
    {
        pose = default;
        ushort nativeFrame;
        ProxyHurtbox hurtbox;
        int index;
        switch (animation)
        {
            case ManagedAnimation.Idle when frame is >= 0 and < 4:
                nativeFrame = (ushort)((frame & 1) == 0 ? 0x7A : 0x7B);
                hurtbox = new ProxyHurtbox(0, 1, 4, 20);
                index = frame;
                break;
            case ManagedAnimation.Walk when frame is >= 0 and < 8:
                ReadOnlySpan<ushort> walkFrames = [0x19, 0x1B, 0x1D, 0x1F, 0x21, 0x23, 0x25, 0x27];
                nativeFrame = walkFrames[frame];
                hurtbox = frame switch
                {
                    < 2 => new ProxyHurtbox(0, 1, 4, 20),
                    2 or 6 or 7 => new ProxyHurtbox(2, 3, 5, 13),
                    _ => new ProxyHurtbox(5, -1, 8, 9),
                };
                index = 4 + frame;
                break;
            case ManagedAnimation.JumpRise when frame is >= 0 and < 2:
                nativeFrame = (ushort)(0x65 + frame);
                hurtbox = new ProxyHurtbox(5, -5, 6, 12);
                index = 12 + frame;
                break;
            case ManagedAnimation.Fall when frame is >= 0 and < 2:
                nativeFrame = (ushort)(0x6E + frame);
                hurtbox = new ProxyHurtbox(0, -3, 4, 16);
                index = 14 + frame;
                break;
            case ManagedAnimation.Landing when frame is >= 0 and < 5:
                ReadOnlySpan<ushort> landingFrames = [0x74, 0x75, 0x76, 0x77, 0x78];
                ReadOnlySpan<int> landingBoxes = [4, 4, 2, 1, 1];
                nativeFrame = landingFrames[frame];
                hurtbox = Hurtbox(landingBoxes[frame]);
                index = 16 + frame;
                break;
            case ManagedAnimation.CrouchEnter when frame is >= 0 and < 13:
                nativeFrame = (ushort)(frame == 0 ? 0x02 : 0x02 + frame);
                hurtbox = Hurtbox(frame == 0 ? 2 : 3);
                index = 21 + frame;
                break;
            case ManagedAnimation.CrouchHold when frame == 0:
                nativeFrame = 0x0F;
                hurtbox = Hurtbox(3);
                index = 34;
                break;
            case ManagedAnimation.CrouchExit when frame is >= 0 and < 2:
                nativeFrame = (ushort)(0x11 + frame);
                hurtbox = Hurtbox(frame == 0 ? 2 : 1);
                index = 35 + frame;
                break;
            case ManagedAnimation.Hurt when frame == 0:
                nativeFrame = 0x9F;
                hurtbox = Hurtbox(7);
                index = 37;
                break;
            case ManagedAnimation.CompactHurt when frame == 0:
                nativeFrame = 0x9F;
                hurtbox = Hurtbox(3);
                index = 42;
                break;
            case ManagedAnimation.AttackStartup when frame == 0:
                nativeFrame = 0x7A;
                hurtbox = Hurtbox(1);
                index = 38;
                break;
            case ManagedAnimation.AttackActive when frame == 0:
                nativeFrame = 0x7A;
                hurtbox = Hurtbox(1);
                index = 39;
                break;
            case ManagedAnimation.AttackRecovery when frame == 0:
                nativeFrame = 0x7A;
                hurtbox = Hurtbox(1);
                index = 40;
                break;
            case ManagedAnimation.Downed when frame == 0:
                nativeFrame = 0x9F;
                hurtbox = Hurtbox(7);
                index = 41;
                break;
            default:
                return false;
        }
        pose = new ProxyPose(index, nativeFrame, hurtbox);
        return true;
    }

    private static ProxyHurtbox Hurtbox(int index) => index switch
    {
        1 => new ProxyHurtbox(0, 1, 4, 20),
        2 => new ProxyHurtbox(0, 7, 4, 16),
        3 => new ProxyHurtbox(0, 13, 4, 9),
        4 => new ProxyHurtbox(2, 3, 5, 13),
        7 => new ProxyHurtbox(0, -3, 4, 16),
        _ => new ProxyHurtbox(0, 1, 4, 20),
    };

    private static bool TryResolveSprite(IMemory memory, ushort nativeFrame, out SpriteFrame sprite)
    {
        sprite = default;
        if (nativeFrame >= AlucardFrameCount) return false;
        uint expectedDescriptor = AlucardDescriptorBase + nativeFrame * 8u;
        uint descriptor = memory.ReadU32(AlucardFrameTableAddress + nativeFrame * 4u);
        if (descriptor != expectedDescriptor) return false;
        if (descriptor < AlucardDescriptorBase + 8 || descriptor > AlucardDescriptorEnd || (descriptor & 7) != 0)
            return false;
        ushort packedSheet = memory.ReadU16(descriptor);
        int sheet = packedSheet & 0x7FFF;
        if ((packedSheet & 0x8000) == 0 || sheet >= AlucardSpriteCount || memory.ReadU16(descriptor + 6) != 0)
            return false;
        uint source = memory.ReadU32(AlucardSpriteTableAddress + (uint)(sheet * 4));
        if (!IsGuestPointer(source)) return false;
        int width = memory.ReadU8(source);
        int height = memory.ReadU8(source + 1);
        int payloadBytes = checked(width * height / 2);
        if (width is < 4 or > 128 || (width & 3) != 0 || height is < 1 or > 128 ||
            source + 4u + (uint)payloadBytes > 0x80200000u)
            return false;
        int pivotX = unchecked((short)memory.ReadU16(descriptor + 2)) + memory.ReadU8(source + 2);
        int pivotY = unchecked((short)memory.ReadU16(descriptor + 4)) + memory.ReadU8(source + 3);
        if (pivotX is < -128 or > 128 || pivotY is < -128 or > 128) return false;
        sprite = new SpriteFrame(source + 4, width, height, pivotX, pivotY);
        return true;
    }

    private ProxyPoseValidationProjection PrepareAllProxyPoses(IMemory memory)
    {
        if (_poseTableValidated || _independentVisualDisabled)
            return new ProxyPoseValidationProjection(_poseTableValidated, _independentVisualDisabled,
                _independentVisualFailures, _independentVisualStatus);
        for (int state = 0; state <= (int)ManagedAnimation.Downed; state++)
        {
            ManagedAnimation animation = (ManagedAnimation)state;
            int count = AnimationFrameCount(animation);
            for (int frame = 0; frame < count; frame++)
            {
                if (TryGetProxyPose(animation, frame, out ProxyPose pose) &&
                    TryResolveSprite(memory, pose.NativeFrame, out _)) continue;
                return new ProxyPoseValidationProjection(false, true,
                    checked(_independentVisualFailures + 1), $"MAP:{state}/{frame}");
            }
        }
        return new ProxyPoseValidationProjection(true, false, _independentVisualFailures, "VALID");
    }

    private void ApplyProxyPoseProjection(ProxyPoseValidationProjection projection)
    {
        _poseTableValidated = projection.TableValidated;
        _independentVisualDisabled = projection.Disabled;
        _independentVisualFailures = projection.Failures;
        _independentVisualStatus = projection.Status;
    }

    private void SampleEntitySlots(IMemory memory)
    {
        _freePlayerCurrent = CountFree(memory, 0, 16, out _);
        _freeAttackCurrent = CountFree(memory, AttackSlotStart, AttackSlotEnd, out _longestAttackCurrent);
        _freeStageCurrent = CountFree(memory, 64, 208, out _);
        _freeTailCurrent = CountFree(memory, 208, 256, out _);
        _minimumFreeAttack = Math.Min(_minimumFreeAttack, _freeAttackCurrent);
        _minimumLongestAttack = Math.Min(_minimumLongestAttack, _longestAttackCurrent);
        _slotSamples++;
    }

    private bool TryExtractAttackProfile(CpuContext context, IMemory memory, AttackKind kind,
        out AttackProfile profile)
    {
        profile = default;
        if (memory.ReadU32(GameApi.GetEquipPropertiesAddr) != ExpectedGetEquipProperties)
        {
            _attackStatus = "PROFILE:API";
            return false;
        }

        uint left = memory.ReadU32(Game.StatusAddr + 0x29C);
        uint right = memory.ReadU32(Game.StatusAddr + 0x2A0);
        int hand = left != 0 ? 0 : 1;
        uint item = hand == 0 ? left : right;
        uint oppositeItem = hand == 0 ? right : left;
        // Item 0x8D makes CalcAttack use GTE state, which the register guard does not cover.
        if (item >= Inventory.HandItemCount || oppositeItem >= Inventory.HandItemCount || item == 0x8D)
            return false;

        uint definition = EquipmentDefinitionsAddress + item * 0x34u;
        short attack = unchecked((short)memory.ReadU16(definition + 0x08));
        ushort element = memory.ReadU16(definition + 0x0C);
        byte type = memory.ReadU8(definition + 0x0E);
        byte invincibility = memory.ReadU8(definition + 0x1A);
        ushort stun = memory.ReadU16(definition + 0x26);
        ushort hitState = memory.ReadU16(definition + 0x28);
        ushort hitEffect = memory.ReadU16(definition + 0x2A);
        // Keep the deterministic template value instead of consuming native RNG for +0x30.
        ushort source = memory.ReadU16(definition + 0x30);

        uint savedSp = context.SP;
        if (savedSp < 0x80010000 || savedSp >= 0x80200000) return false;
        uint temporarySp = (savedSp - 0x80u) & ~7u;
        uint scratchStart = temporarySp - 0x80u;
        uint scratchEnd = temporarySp + 0x10u;
        if (scratchStart < 0x80010000 || scratchEnd > 0x80200000) return false;
        int scratchLength = checked((int)(scratchEnd - scratchStart));
        Span<byte> saved = stackalloc byte[scratchLength];
        bool callOk = true;
        int savedCount = 0;
        Exception? restoreFailure = null;
        if (type is not (6 or 10))
        {
            callOk = CpuContextScratchDirectCall.TryInvoke(context, memory, ExpectedCalcAttack,
                item, oppositeItem, temporarySp, scratchStart, saved,
                out uint calculatedAttack, out savedCount, out restoreFailure);
            if (callOk)
            {
                try
                {
                    attack = unchecked((short)calculatedAttack);
                    if ((memory.ReadU32(0x80072F2C) & 0x4000) != 0) attack = (short)(attack >> 1);
                }
                catch
                {
                    callOk = false;
                }
            }
        }
        else
        {
            Span<uint> savedContext = stackalloc uint[CpuContextRegisterGuard.StateWordCount];
            var contextGuard = new CpuContextRegisterGuard(context, savedContext);
            try
            {
                // Preserve the existing scratch clear/restore safety even when CalcAttack is skipped.
                for (int i = 0; i < scratchLength; i++)
                {
                    saved[i] = memory.ReadU8(scratchStart + (uint)i);
                    savedCount++;
                    memory.WriteU8(scratchStart + (uint)i, 0);
                }
                context.SP = temporarySp;
            }
            catch
            {
                callOk = false;
            }
            finally
            {
                try
                {
                    GuestScratchRestore.RestoreAll(memory, scratchStart, saved[..savedCount]);
                }
                catch (Exception ex)
                {
                    restoreFailure = ex;
                }
                finally
                {
                    contextGuard.Restore();
                }
            }
        }

        if (savedCount != 0) _equipmentRestoreChecks++;
        if (restoreFailure is not null)
        {
            _equipmentRestoreFailures++;
            throw restoreFailure;
        }
        uint stableLeft = memory.ReadU32(Game.StatusAddr + 0x29C);
        uint stableRight = memory.ReadU32(Game.StatusAddr + 0x2A0);
        if (!callOk || stableRight != right || stableLeft != left || attack <= 0 ||
            stun > 0x7FFF || source > 0xFF || !IsNativeTargetBody(hitState) ||
            (hitState & ~0x3E) != 0)
            return false;
        profile = new AttackProfile(kind, checked((ushort)item), attack, element,
            invincibility, stun, hitState, hitEffect,
            kind == AttackKind.Projectile ? (byte)5 : (byte)12,
            kind == AttackKind.Projectile ? (byte)5 : (byte)10,
            _facingLeft ? -1 : 1, source);
        return true;
    }

    private void TrySpawnOwnedAttack(CpuContext context, IMemory memory)
    {
        if (!_attackSpawnPending) return;
        _attackSpawnPending = false;
        if (_outgoingAttackDisabled)
        {
            _attackStatus = "DISABLED";
            return;
        }
        if (_ownedAttackSlot >= 0 || !_safeFrame || !_proxyInitialized || _downed || _transitionPending)
        {
            _attackStatus = "SPAWN:STATE";
            return;
        }
        if (!TryExtractAttackProfile(context, memory, _pendingAttackKind, out AttackProfile profile))
        {
            _profileExtractionFailures++;
            if (_equipmentRestoreFailures != 0) _outgoingAttackDisabled = true;
            _attackStatus = "PROFILE:INVALID";
            _attackTimer = 0;
            return;
        }
        _latchedAttackProfile = profile;
        _attackProfileLatched = true;
        _profileExtractions++;
        if (memory.ReadU32(AssignAttackerIdSlot) != ExpectedAssignAttackerId ||
            memory.ReadU32(GameApi.DealDamageAddr) != ExpectedDealDamage ||
            memory.ReadU32(GameApi.EnemyDefsAddr) != ExpectedEnemyDefinitions)
        {
            MarkAttackHardFailure("API");
            return;
        }

        int slot = -1;
        for (int candidate = AttackSlotStart; candidate < AttackSlotEnd; candidate++)
        {
            uint address = Game.EntitiesAddr + (uint)(candidate * Entity.Stride);
            if (memory.ReadU16(address + EntityIdOffset) == 0 && memory.ReadU32(address + EntityUpdateOffset) == 0)
            {
                slot = candidate;
                break;
            }
        }
        if (slot < 0)
        {
            _attackStatus = "SPAWN:POOL";
            return;
        }

        uint entity = Game.EntitiesAddr + (uint)(slot * Entity.Stride);
        uint roomHash = _room.StableHash();
        _attackLease = AttackLeaseMachine.Reserve(_attackLease, slot, roomHash);
        uint generation = _ownedAttackGeneration;
        _projectileX = _projectileOriginX = _proxyX;
        _projectileY = _proxyY - 8 * FixedOne;
        _projectileLifetime = 0;
        _attackPublicationAdapter.Bind(memory, entity, profile, context);
        var publicationTuple = new AttackPublicationTuple(slot, generation, roomHash);
        bool completed = AttackPublicationPolicy.Publish(ref _attackPublication, _attackPublicationAdapter,
            publicationTuple, _mainEngineCalls, _updateCalls);
        if (completed)
        {
            _attackWindowObserved = false;
            _attackArmMainGeneration = _mainEngineCalls;
            _attackArmUpdateGeneration = _updateCalls;
            _attackObservedMainGeneration = -1;
            _attackAllocations++;
            _attackStatus = "ARMED";
        }
        else
        {
            AttackLeaseCommand failed = AttackLeaseMachine.RequestOwnedCleanup(_attackLease);
            if (_attackPublication.Phase == AttackPublicationPhase.RolledBack)
            {
                AttackLeaseCommand authorization = AttackLeaseMachine.OwnedExact(_attackLease, failed);
                ClearOwnedAttackMetadata(authorization);
                MarkAttackHardFailure("PUBLICATION-ROLLED-BACK");
            }
            else if (_attackPublication.Phase == AttackPublicationPhase.RetryableQuarantine)
                RetainAttackCleanupPending(failed, "PUBLICATION");
            else if (_attackPublication.Phase == AttackPublicationPhase.MutationStopped)
                StopQuarantinedMutation(failed, "PUBLICATION");
            else
                StopResidualAttackMutation(failed, "PUBLICATION");
            _attackStatus = $"FAIL:{_attackStatus}";
        }
    }

    private bool CleanupOwnedAttack(IMemory memory, string reason)
    {
        AttackLeaseCommand request = AttackLeaseMachine.RequestOwnedCleanup(_attackLease);
        if (request.Kind == AttackLeaseCommandKind.None) return true;
        if (_attackPublication.Phase == AttackPublicationPhase.RetryableQuarantine)
            return TryCleanupQuarantinedAttack(memory, reason);
        if (_attackPublication.Phase is AttackPublicationPhase.MutationStopped or
            AttackPublicationPhase.ResidualStopped) return false;
        int slot = request.Lease.Slot;
        uint generation = request.Lease.Generation;
        uint roomHash = request.Lease.RoomHash;
        uint entity = Game.EntitiesAddr + (uint)(slot * Entity.Stride);
        bool owned;
        try
        {
            owned = memory.ReadU32(entity + 0x7C) == AttackMarker &&
                memory.ReadU32(entity + 0x80) == generation &&
                memory.ReadU32(entity + 0x84) == roomHash;
        }
        catch (Exception ex)
        {
            RetainAttackCleanupPending(request, $"VERIFY:{ex.GetType().Name}");
            return false;
        }
        if (!owned)
        {
            StopQuarantinedMutation(request, "OWNERSHIP-MISMATCH");
            return false;
        }
        AttackLeaseCommand authorization = AttackLeaseMachine.OwnedExact(_attackLease, request);
        _attackPublicationAdapter.Bind(memory, entity, _latchedAttackProfile);

        try
        {
            if (!_attackWindowObserved)
            {
                try { ObserveAttackResult(_attackPublicationAdapter); }
                catch (Exception ex) { MarkAttackHardFailure($"OBSERVE:{ex.GetType().Name}"); }
            }
        }
        catch (Exception ex)
        {
            RetainAttackCleanupPending(request, $"DEACTIVATE:{ex.GetType().Name}");
            return false;
        }

        bool timingValid = true;
        if (reason == "WINDOW")
        {
            timingValid = _updateCalls == _attackArmUpdateGeneration + 1 &&
                _attackWindowObserved && _attackObservedMainGeneration == _attackArmMainGeneration + 1;
            if (timingValid) _attackNormalEngineWindows++;
            else
            {
                _attackTimingFailures++;
                MarkAttackHardFailure("NORMAL-WINDOW-TIMING");
            }
        }
        bool nativeHit = memory.ReadU8(entity + 0x48) != 0;
        if (reason == "WINDOW" && timingValid && !_attackHardFailure &&
            _attackProfileLatched && _latchedAttackProfile.Kind == AttackKind.Projectile && !nativeHit)
        {
            if (TryAdvanceProjectileWindow(memory, entity))
            {
                _attackLease = AttackLeaseMachine.RetainOwned(_attackLease, authorization);
                return true;
            }
            if (_ownedAttackSlot < 0 || _attackPublication.Phase is
                AttackPublicationPhase.RetryableQuarantine or AttackPublicationPhase.ResidualStopped or
                AttackPublicationPhase.MutationStopped)
                return false;
        }

        if (!AttackPublicationPolicy.Cleanup(ref _attackPublication, _attackPublicationAdapter))
        {
            ReconcileRejectedAttackOperation(request, "DEACTIVATE-REJECTED");
            return false;
        }
        if (reason != "WINDOW" && reason != "BUILD_FAIL") _attackLifecycleCancellations++;
        _attackCleanups++;
        _attackStatus = CleanupStatus(reason);
        ClearOwnedAttackMetadata(authorization);
        return true;
    }

    private bool TryAdvanceProjectileWindow(IMemory memory, uint entity)
    {
        if (!_safeFrame || !_proxyInitialized || _downed || _transitionPending || !_roomKnown ||
            !ReadRoomIdentity(memory).Equals(_room) || !AutomaticInputSafe(memory))
            return false;
        AttackProfile profile = _latchedAttackProfile;
        _projectileLifetime++;
        _projectileX += profile.Direction * ProjectileSpeed;
        if (_projectileLifetime >= ProjectileLifetimeUpdates ||
            Math.Abs((_projectileX - _projectileOriginX) >> 16) > ProjectileMaximumRange)
            return false;
        int scrollX = unchecked((int)memory.ReadU32(ScrollXAddress)) >> 16;
        int scrollY = unchecked((int)memory.ReadU32(ScrollYAddress)) >> 16;
        int screenX = (_projectileX >> 16) - scrollX;
        int screenY = (_projectileY >> 16) - scrollY;
        if (screenX is < -16 or > 272 || screenY is < -16 or > 240) return false;

        _attackPublicationAdapter.Bind(memory, entity, profile);
        if (!AttackPublicationPolicy.RepublishProjectile(ref _attackPublication,
            _attackPublicationAdapter, _mainEngineCalls, _updateCalls))
        {
            AttackLeaseCommand probe = AttackLeaseMachine.RequestOwnedCleanup(_attackLease);
            ReconcileRejectedAttackOperation(probe, "REPUBLISH-REJECTED");
            return false;
        }
        _attackWindowObserved = false;
        _attackArmMainGeneration = _mainEngineCalls;
        _attackArmUpdateGeneration = _updateCalls;
        _attackObservedMainGeneration = -1;
        _projectileWindows++;
        _attackStatus = "PROJECTILE";
        return true;
    }

    private void CancelOwnedAttack(string reason)
    {
        _attackTimer = 0;
        _attackSpawnPending = false;
        if (RecompOne.Runtime.Runtime.Mem is IMemory memory)
        {
            try
            {
                CleanupOwnedAttack(memory, reason);
                TryCleanupQuarantinedAttack(memory, reason);
            }
            catch (Exception ex)
            {
                MarkAttackHardFailure($"LEASE:{ex.GetType().Name}");
            }
        }
    }

    private void ClearLatchedAttackProfile()
    {
        _attackProfileLatched = false;
        _latchedAttackProfile = default;
        _pendingAttackKind = AttackKind.Contact;
        _projectileX = _projectileY = _projectileOriginX = _projectileLifetime = 0;
    }

    private static string CleanupStatus(string reason) => reason switch
    {
        "WINDOW" => "CLEAN:WINDOW",
        "UNSAFE" => "CLEAN:UNSAFE",
        "COLLISION" => "CLEAN:COLLISION",
        "OBSERVE_FAIL" => "CLEAN:OBSERVE",
        "HURT" => "CLEAN:HURT",
        "TRANS" or "TRANSITION" => "CLEAN:TRANSITION",
        "PLAYER" => "CLEAN:PLAYER",
        "RESET" or "MANUAL_RESET" => "CLEAN:RESET",
        "FATAL" => "CLEAN:FATAL",
        "DISABLED" => "CLEAN:DISABLED",
        _ => "CLEAN:LIFECYCLE"
    };

    private void MarkAttackHardFailure(string status)
    {
        _attackFailures++;
        _attackHardFailure = true;
        _outgoingAttackDisabled = true;
        _attackSpawnPending = false;
        _attackTimer = 0;
        _attackStatus = status;
    }

    private void RetainAttackCleanupPending(AttackLeaseCommand probe, string status)
    {
        _attackLease = AttackLeaseMachine.ProbeFault(_attackLease, probe);
        MarkAttackHardFailure($"CLEANUP-PENDING:{status}:{probe.Lease.Slot}");
    }

    private void StopQuarantinedMutation(AttackLeaseCommand probe, string status)
    {
        _attackLease = AttackLeaseMachine.ProbeReused(_attackLease, probe);
        MarkAttackHardFailure($"QUARANTINE-REUSED:{status}:{probe.Lease.Slot}");
    }

    private void StopResidualAttackMutation(AttackLeaseCommand probe, string status)
    {
        _attackLease = AttackLeaseMachine.TerminalFault(_attackLease, probe);
        MarkAttackHardFailure($"RESIDUAL-STOP:{status}:{probe.Lease.Slot}");
    }

    private void ReconcileRejectedAttackOperation(AttackLeaseCommand probe, string status)
    {
        if (_attackPublication.Phase == AttackPublicationPhase.RolledBack)
        {
            AttackLeaseCommand authorization = AttackLeaseMachine.OwnedExact(_attackLease, probe);
            ClearOwnedAttackMetadata(authorization);
            MarkAttackHardFailure(status);
        }
        else if (_attackPublication.Phase == AttackPublicationPhase.RetryableQuarantine)
            RetainAttackCleanupPending(probe, status);
        else if (_attackPublication.Phase == AttackPublicationPhase.MutationStopped)
            StopQuarantinedMutation(probe, status);
        else
            StopResidualAttackMutation(probe, status);
    }

    private void ClearOwnedAttackMetadata(AttackLeaseCommand authorization)
    {
        _attackLease = AttackLeaseMachine.ClearSucceeded(_attackLease, authorization);
        _attackPublication = AttackPublicationPolicy.Initial();
        _attackTargets.Clear();
        _attackWindowObserved = false;
    }

    private bool TryCleanupQuarantinedAttack(IMemory memory, string reason)
    {
        bool retainedOwned = _attackLease.Phase == AttackLeasePhase.Owned;
        AttackLeaseCommand request = retainedOwned
            ? AttackLeaseMachine.RequestOwnedCleanup(_attackLease)
            : AttackLeaseMachine.RequestQuarantineRetry(_attackLease);
        if (request.Kind == AttackLeaseCommandKind.None)
            return !_attackQuarantineMutationStopped;
        uint entity = Game.EntitiesAddr + (uint)(request.Lease.Slot * Entity.Stride);
        _attackPublicationAdapter.Bind(memory, entity, _latchedAttackProfile);
        if (AttackPublicationPolicy.RetryQuarantine(ref _attackPublication, _attackPublicationAdapter))
        {
            _attackCleanups++;
            _attackStatus = "QUARANTINE-CLEAN";
            if (retainedOwned)
            {
                AttackLeaseCommand authorization = AttackLeaseMachine.OwnedExact(_attackLease, request);
                _attackLease = AttackLeaseMachine.ClearSucceeded(_attackLease, authorization);
            }
            else _attackLease = AttackLeaseMachine.RetryFree(_attackLease, request);
            _attackTargets.Clear();
            _attackWindowObserved = false;
            return true;
        }
        if (_attackPublication.Phase == AttackPublicationPhase.RetryableQuarantine)
        {
            MarkAttackHardFailure("CLEANUP-PENDING:RETRY");
            return false;
        }
        if (_attackPublication.Phase == AttackPublicationPhase.MutationStopped)
            StopQuarantinedMutation(request, "RETRY-REUSED");
        else
            StopResidualAttackMutation(request, "RETRY-FAULT");
        return false;
    }

    private void CaptureAttackTargets(IAttackTargetReadAdapter adapter)
    {
        _attackTargets.Clear();
        if (!_attackProfileLatched) return;
        AttackProfile profile = _latchedAttackProfile;
        int worldX = profile.Kind == AttackKind.Projectile ? _projectileX : _proxyX;
        int worldY = profile.Kind == AttackKind.Projectile ? _projectileY : _proxyY;
        var input = new AttackTargetCaptureInput(worldX, worldY,
            profile.Kind == AttackKind.Projectile, _facingLeft, profile.HalfWidth,
            profile.HalfHeight, profile.HitState, _attackLastAttackerId);
        AttackTargetObservationPolicy.Capture(_attackTargets, adapter, input);
    }

    private void ObserveAttackResult(IAttackTargetReadAdapter adapter)
    {
        _attackWindowObserved = true;
        _attackObservedMainGeneration = _mainEngineCalls;
        AttackTargetObservation result = AttackTargetObservationPolicy.Observe(_attackTargets,
            adapter, _attackLastAttackerId);
        if (result.HitFlag) _attackHitFlagObservations++;
        _attackTargetHpChanges += result.HpChanges;
        _attackCooldownObservations += result.CooldownChanges;
        _nativeTargetHits += result.NativeHits;
        _defeatedTargets += result.Defeated;
        _compatibleZeroHpHits += result.CompatibleZeroHpHits;
        _attackCausalResults += result.CausalResults;
    }

    private void ObserveOwnedAttackWindow(IMemory memory)
    {
        if (_ownedAttackSlot < 0 || _attackWindowObserved) return;
        int slot = _ownedAttackSlot;
        AttackLeaseCommand probe = AttackLeaseMachine.RequestOwnedCleanup(_attackLease);
        uint entity = Game.EntitiesAddr + (uint)(slot * Entity.Stride);
        try
        {
            _attackPublicationAdapter.Bind(memory, entity, _latchedAttackProfile);
            if (!AttackPublicationPolicy.Observe(ref _attackPublication, _attackPublicationAdapter,
                _mainEngineCalls))
            {
                ReconcileRejectedAttackOperation(probe, "OBSERVE-REJECTED");
                return;
            }
        }
        catch (Exception ex)
        {
            MarkAttackHardFailure($"OBSERVE:{ex.GetType().Name}");
            // Observation failure must not abandon a still-owned live entity.
            CleanupOwnedAttack(memory, "OBSERVE_FAIL");
            TryCleanupQuarantinedAttack(memory, "OBSERVE_FAIL");
        }
    }

    private static int CountFree(IMemory memory, int start, int end, out int longestRun)
    {
        int free = 0;
        int run = 0;
        longestRun = 0;
        for (int slot = start; slot < end; slot++)
        {
            uint entity = Game.EntitiesAddr + (uint)(slot * Entity.Stride);
            bool available = memory.ReadU16(entity + EntityIdOffset) == 0 &&
                memory.ReadU32(entity + EntityUpdateOffset) == 0;
            if (available)
            {
                free++;
                run++;
                longestRun = Math.Max(longestRun, run);
            }
            else run = 0;
        }
        return free;
    }

    private bool TryResetDiagnostic()
    {
        // Every fallible/nonwrapping transition is prepared before native memory is touched.
        if (!DiagnosticResetPreparationPolicy.TryPrepare(_diagnosticGeneration, _movementSession,
            _attackLease, _attackPublication, _jumpForgiveness, _stance, _locomotionState,
            _reconstructionPolicy, out DiagnosticResetPreparation preparation)) return false;
        bool preparedConnected;
        ushort preparedVirtualHeld;
        try
        {
            preparedConnected = Controller.Connected2;
            preparedVirtualHeld = ReadVirtualKeysDown();
        }
        catch { return false; }
        AttackResetPreflightOutcome attackPreflight = PreflightAttackReset(preparation.Publication);
        if (!AttackResetPreflight.AllowsReset(attackPreflight))
            return false;
        if (!DiagnosticResetPreparationPolicy.CommitPreparedReducers(preparation, attackPreflight,
            ref _attackLease, _movementSession, _jumpForgiveness, _stance, _locomotionState,
            _reconstructionPolicy)) return false;
        bool cleanupQuarantined = _attackQuarantineSlot >= 0;
        _diagnosticGeneration = preparation.NextDiagnosticGeneration;
        _fatal = false;
        _firstError = "none";
        _collisionDisabled = false;
        _collisionFailureReason = "none";
        _enabled = true;
        _safeFrame = false;
        _safeCode = "WAIT";
        _safeReason = "Diagnostic reset; waiting for gameplay";
        _operationStatus = "Diagnostic reset; initialization awaiting a safe player update";
        _vsyncCalls = _mainEngineCalls = _updateCalls = _renderCalls = _pad2Reads = 0;
        _connectionChanges = 0;
        _previousConnected = preparedConnected;
        _hostSeen = _padSeen = _gameSeen = _tapSeen = 0;
        _hostState = _padState = 0xFFFF;
        _gamePressed = _gameTapped = 0;
        _neutralSeen = false;
        _pad2Source.Reset();
        _virtualPressed = 0;
        _virtualPreviousHeld = preparedVirtualHeld;
        _virtualRawHeld = _virtualPreviousHeld;
        _virtualRawSeen = 0;
        _virtualDownSeen = _virtualUpSeen = 0;
        _virtualNeutralSeen = false;
        _virtualNeutralObserved = false;
        _virtualSuppressionFrames = 2;
        _virtualForcedReleases = 0;
        _autoTestState = AutoTestState.Idle;
        _autoTestFrame = 0;
        _autoTestRuns = 0;
        _autoTestStatus = "idle";
        _visualConfirmed = false;
        _contactVisualConfirmed = false;
        _leftXMin = _leftYMin = _rightXMin = _rightYMin = 0xFF;
        _leftXMax = _leftYMax = _rightXMax = _rightYMax = 0;
        _leftDistanceRaw = _rightDistanceRaw = 0;
        _jumpObserved = false;
        _jumpPending = false;
        _ownedHurtboxStatesSeen = 0;
        _ownedHurtboxSamples = 0;
        _visualPosesSeen = _hurtboxPosesSeen = 0;
        _independentVisualEligible = _independentVisualSubmitted = 0;
        _independentRestoreChecks = _independentRestoreFailures = _independentVisualFailures = 0;
        _independentVisualDisabled = false;
        _poseTableValidated = false;
        _independentNativeFrame = 0;
        _independentVisualStatus = "WAIT";
        _normalJumps = _coyoteJumps = _bufferedJumps = 0;
        _landedThisUpdate = _horizontalCommandThisUpdate = false;
        _reconstructionStatus = "WAIT";
        _collisionCalls = 0;
        _collisionRestoreFailures = 0;
        _invalidCorrections = 0;
        _lastRejectedCorrection = 0;
        _groundContacts = _wallCorrections = _ceilingCorrections = _oneWayContacts = 0;
        _collisionFunction = 0;
        _collisionQueryFailed = false;
        _unsupportedTerrainSuspensions = 0;
        _sawSolid = _sawEmpty = false;
        _renderEligible = _renderSubmitted = _drawOtCalls = 0;
        _nativeCapturePending = false;
        _nativeSpriteDrawnThisFrame = false;
        _nativeSpriteEligible = _nativeSpriteCaptured = _nativeSpriteSubmitted = 0;
        _nativeSpriteFlipped = _nativeSpriteFallbacks = 0;
        _nativeSpriteStreak = 0;
        _nativeSpriteFlipSeenInStreak = false;
        _nativeSpriteStatus = "WAIT";
        _hudEligible = _hudSubmitted = 0;
        Array.Clear(_contactObservations);
        _contactOpportunities.Reset();
        _contactDisabled = false;
        _contactGuardChecks = _contactGuardFailures = 0;
        _contactLastSlot = -1;
        _contactLastEntityId = 0;
        _contactOffsetX = _contactOffsetY = _contactHalfWidth = _contactHalfHeight = 0;
        _contactGuardRegion = "none";
        _contactStatus = "WAIT";
        ResetManagedHealth();
        _attackTimer = 0;
        _attackSpawnPending = false;
        if (!cleanupQuarantined) _outgoingAttackDisabled = _attackHardFailure = false;
        _attackAllocations = _attackNormalEngineWindows = _attackCleanups = 0;
        _attackLifecycleCancellations = 0;
        _attackFailures = cleanupQuarantined ? 1 : 0;
        _attackLastAttackerId = _attackHitFlagObservations = _attackCooldownObservations = 0;
        _attackTargetHpChanges = 0;
        _attackTargets.Clear();
        _attackAttackerIdValid = false;
        _attackWindowObserved = false;
        _attackArmMainGeneration = _attackArmUpdateGeneration = 0;
        _attackObservedMainGeneration = -1;
        _attackTimingFailures = _attackCausalResults = 0;
        _attackProfileLatched = false;
        _pendingAttackKind = AttackKind.Contact;
        _latchedAttackProfile = default;
        _projectileX = _projectileY = _projectileOriginX = _projectileLifetime = 0;
        _projectileWindows = 0;
        _profileExtractions = _profileExtractionFailures = 0;
        _equipmentRestoreChecks = _equipmentRestoreFailures = 0;
        _enemyDiagnosticScans = _enemyNativeCandidateSamples = _enemyCompatibleCandidateSamples = 0;
        _nearestTargetSlot = -1;
        _nearestTargetEntityId = _nearestTargetEnemyId = 0;
        _nearestTargetHp = 0;
        _nearestTargetP1Distance = _nearestTargetP2Distance = 0;
        _nearestTargetCompatible = false;
        _nativeTargetHits = _defeatedTargets = _compatibleZeroHpHits = 0;
        _enemyDiagnosticStatus = "WAIT";
        _awarenessDisabled = false;
        _awarenessCalls = _awarenessOverrides = 0;
        _awarenessChosenSlot = -1;
        _awarenessStatus = "WAIT";
        if (!cleanupQuarantined) _attackStatus = "IDLE";
        _lastManagedInput = default;
        _lastManagedSnapshot = default;
        _lastManagedStateHash = 0;
        _managedSnapshotCount = 0;
        _slotSamples = 0;
        _freePlayerCurrent = _freeAttackCurrent = _freeStageCurrent = _freeTailCurrent = 0;
        _longestAttackCurrent = 0;
        _minimumFreeAttack = _minimumLongestAttack = int.MaxValue;
        return true;
    }

    private AttackResetPreflightOutcome PreflightAttackReset(
        in AttackPublicationResetCommand publication)
    {
        bool memoryAvailable = RecompOne.Runtime.Runtime.Mem is IMemory;
        if (memoryAvailable && _attackPublication.Phase is not
            (AttackPublicationPhase.Empty or AttackPublicationPhase.RolledBack or
             AttackPublicationPhase.MutationStopped or AttackPublicationPhase.ResidualStopped))
        {
            IMemory memory = (IMemory)RecompOne.Runtime.Runtime.Mem!;
            uint entity = Game.EntitiesAddr + (uint)(_attackPublication.Tuple.Slot * Entity.Stride);
            _attackPublicationAdapter.Bind(memory, entity, _latchedAttackProfile);
            return AttackResetPreflight.RunPrepared(ref _attackPublication,
                _attackPublicationAdapter, true, publication);
        }
        return AttackResetPreflight.RunPrepared(ref _attackPublication, null,
            memoryAvailable, publication);
    }

    private void Fail(string subsystem, Exception ex)
    {
        if (_fatal) return;
        _firstError = $"{subsystem}: {ex.GetType().Name}: {ex.Message}";
        _fatal = true;
        _enabled = false;
        _safeFrame = false;
        _movementSession.Fatal();
        _locomotionState.Invalidate();
        DisarmAwareness("FATAL");
        _attackSpawnPending = false;
        _attackTimer = 0;
        CancelAutomaticTest("diagnostic circuit breaker");
        ClearJumpForgiveness();
        SuspendContactScan("FATAL");
        // Cleanup is deliberately non-throwing and cannot replace the original latched error.
        CancelOwnedAttack("FATAL");
        ClearLatchedAttackProfile();
        Console.Error.WriteLine($"[CoopProbe] Circuit breaker: {_firstError}");
    }

    private string BuildReport()
    {
        char hooks = _fatal ? 'F' :
            _vsyncCalls >= 60 && _mainEngineCalls >= 60 && _updateCalls >= 60 && _renderCalls >= 60 && _pad2Reads >= 60 ? 'P' : 'W';
        int hostCount = CountSeen(_hostSeen, RequiredHostButtons);
        int padCount = CountSeen(_padSeen, RequiredGameButtons);
        int gameCount = CountSeen(_gameSeen, RequiredGameButtons);
        int tapCount = CountSeen(_tapSeen, RequiredGameButtons);
        int axisCount = ActiveAxisCount();
        int virtualDownCount = CountSeen(_virtualDownSeen, RequiredGameButtons);
        int virtualUpCount = CountSeen(_virtualUpSeen, RequiredGameButtons);
        char input = _virtualKeyboard
            ? virtualDownCount == 7 && virtualUpCount == 7 && padCount == 7 && gameCount == 7 && tapCount == 7 &&
              _virtualNeutralObserved && _virtualPressed == 0 ? 'P' : 'W'
            : Controller.Connected2 && hostCount == 7 && padCount == 7 && gameCount == 7 && tapCount == 7 &&
              (!_physicalControllerTest || axisCount == 4) ? 'P' : 'W';
        char movement = (_leftDistanceRaw >> 16) >= 8 && (_rightDistanceRaw >> 16) >= 8 && _jumpObserved ? 'P' : 'W';
        int animationMask = (1 << ((int)ManagedAnimation.Downed + 1)) - 1;
        int advancingAnimationMask = (1 << (int)ManagedAnimation.Idle) |
            (1 << (int)ManagedAnimation.Walk) | (1 << (int)ManagedAnimation.JumpRise) |
            (1 << (int)ManagedAnimation.Fall) | (1 << (int)ManagedAnimation.Landing) |
            (1 << (int)ManagedAnimation.CrouchEnter) | (1 << (int)ManagedAnimation.CrouchExit);
        ulong poseMask = IndependentPoseCount >= 64 ? ulong.MaxValue : (1UL << IndependentPoseCount) - 1UL;
        char animation = _fatal ? 'F' : (_animationStatesSeen & animationMask) == animationMask &&
            (_animationAdvanceStatesSeen & advancingAnimationMask) == advancingAnimationMask &&
            (_ownedHurtboxStatesSeen & animationMask) == animationMask && _hurtboxPosesSeen == poseMask && _animationTransitions >= 4 &&
            _ownedHurtboxSamples >= 120 && _attackPhaseCompletionMask == 7 ? 'P' : 'W';
        char independentVisual = _fatal || _independentVisualDisabled || _independentRestoreFailures != 0 ? 'F' :
            _visualConfirmed && _visualPosesSeen == poseMask && _independentVisualSubmitted >= 120 &&
            _independentRestoreChecks > 0 && _independentVisualStatus == "OK" ? 'P' : 'W';
        char jumpForgiveness = _coyoteJumps > 0 && _bufferedJumps > 0 ? 'P' : 'W';
        char render = _visualConfirmed && _renderSubmitted >= 60 ? 'P' : 'W';
        char nativeSprite = _visualConfirmed && _nativeSpriteSubmitted >= 60 &&
            _nativeSpriteStreak >= 60 && _nativeSpriteFlipSeenInStreak && _nativeSpriteStatus == "OK" ? 'P' : 'W';
        char contact = _contactDisabled || _contactGuardFailures != 0 ? 'F' :
            _contactOpportunities.ScanFrames >= 120 &&
            _contactOpportunities.SlotsScanned == _contactOpportunities.ScanFrames * ContactSlotCount &&
            _contactGuardChecks == _contactOpportunities.ScanFrames && _contactOpportunities.Entries > 0 &&
            _contactOpportunities.StaySamples > 0 && _contactOpportunities.Exits > 0 &&
            _contactOpportunities.DamagingSamples > 0 && _contactVisualConfirmed ? 'P' : 'W';
        char collision = _fatal || _collisionDisabled || _collisionRestoreFailures != 0 ? 'F' :
            _unsupportedTerrainSuspensions == 0 && _collisionCalls >= 120 && _sawSolid && _sawEmpty && _groundContacts > 0 &&
            _wallCorrections > 0 && _ceilingCorrections > 0 ? 'P' : 'W';
        char transition = _fatal || _collisionDisabled || _reconstructionHardFailure ? 'F' :
            _completedTransitions > 0 && _passedTransitions == _completedTransitions &&
            !_transitionPending && !_awaitingPostTransitionMovement ? 'P' : 'W';
        char slots = _slotSamples < 5 ? 'W' : _minimumFreeAttack == 0 ? 'F' :
            _minimumFreeAttack >= 4 && _minimumLongestAttack >= 2 ? 'P' : 'W';
        bool attackCountMismatch = _attackCleanups > _attackAllocations ||
            _attackLifecycleCancellations > _attackCleanups;
        char outgoingAttack = _fatal || _attackHardFailure || _equipmentRestoreFailures != 0 ||
            attackCountMismatch || _attackQuarantineSlot >= 0 ? 'F' :
            _attackAllocations > 0 && _attackNormalEngineWindows > 0 && _attackCleanups == _attackAllocations &&
            _attackAttackerIdValid && _attackCausalResults > 0 ? 'P' : 'W';
        char health = _fatal || _healthInvariantFailures > 0 ? 'F' :
            _damageEvents > 0 && _damageSuppressedHitInvul > 0 && _downedCount > 0 &&
            _reviveStarts > 0 && _reviveCancels > 0 && _revives > 0 &&
            _reviveRecoveries == _revives && !_downed && _managedHp == 50 ? 'P' : 'W';
        char enemies = _fatal ? 'F' : _nearestTargetSlot >= 0 && _nearestTargetCompatible ? 'P' : 'W';
        char awareness = _awarenessDisabled ? 'F' : _awarenessOverrides > 0 ? 'P' : 'W';
        char hud = _fatal ? 'F' : _hudSubmitted >= 60 && _hudSubmitted == _hudEligible ? 'P' : 'W';

        string inputReport = _virtualKeyboard
            ? $"I={input}:K:-/{padCount}/{gameCount}/{tapCount}/A- K={virtualDownCount}/{virtualUpCount}/H{_virtualPressed:X4}/R{_virtualRawHeld:X4}/U{_virtualRawSeen:X4}/N{Bool(_virtualNeutralObserved)}/S{_virtualSuppressionFrames}"
            : $"I={input}:C:{hostCount}/{padCount}/{gameCount}/{tapCount}/A{axisCount} K=-";

        string report = $"P2D4 VER={Version} H={hooks}:{_vsyncCalls}/{_mainEngineCalls}/{_updateCalls}/{_renderCalls}/{_pad2Reads} {inputReport} " +
               $"M={movement}:{_leftDistanceRaw >> 16}/{_rightDistanceRaw >> 16}/{Bool(_jumpObserved)} " +
               $"R={render}:{_renderSubmitted}/{_renderEligible}/{Bool(_visualConfirmed)}/D{_drawOtCalls}/H{Bool(GpuHle.Active)}{Bool(GpuHle.Backend?.Ready == true)} " +
               $"N={nativeSprite}:{_nativeSpriteSubmitted}/{_nativeSpriteCaptured}/{_nativeSpriteEligible}/{_nativeSpriteFlipped}/{_nativeSpriteFallbacks}/S{_nativeSpriteStreak}/F{Bool(_nativeSpriteFlipSeenInStreak)}/L{_nativeSpriteStatus} " +
               $"B={contact}:F{_contactOpportunities.ScanFrames}/S{_contactOpportunities.SlotsScanned}/E{_contactOpportunities.EligibleSamples}/O{_contactOpportunities.OverlapSamples}/C{_contactOpportunities.Current}/P{_contactOpportunities.Peak}/D{_contactOpportunities.DamagingSamples}/I{_contactOpportunities.Entries}/T{_contactOpportunities.StaySamples}/X{_contactOpportunities.Exits}/R{_contactOpportunities.Resets}/U{_contactOpportunities.ResumeGraceScans},{_contactOpportunities.ResumeGraceBudget},{Bool(_contactOpportunities.Suspended)}/G{_contactGuardChecks},{_contactGuardFailures}/V{Bool(_contactVisualConfirmed)}/H{_contactOffsetX},{_contactOffsetY},{_contactHalfWidth},{_contactHalfHeight}/Q{_contactGuardRegion}/L{_contactStatus} " +
               $"C={collision}:{_collisionCalls}/{_collisionRestoreFailures}/{_invalidCorrections}/{_unsupportedTerrainSuspensions}/{_groundContacts}/{_wallCorrections}/{_ceilingCorrections}/B{Bool(_sawSolid)}{Bool(_sawEmpty)} " +
               $"T={transition}:{_passedTransitions}/{_completedTransitions}/{_roomLayerEvents}/R{_reconstructionSafeFrames},{_reconstructionAttempts},{_reconstructionSuccesses},{_reconstructionFailures},H{_tetherRecoveries}/L{_reconstructionStatus} " +
               $"S={slots}:{DisplayMinimum(_minimumFreeAttack)}/{DisplayMinimum(_minimumLongestAttack)}/{_slotSamples} " +
               $"G={_safeCode}:E{Bool(_enabled)}S{Bool(_safeFrame)}P{Bool(_proxyInitialized)} Q={_diagnosticGeneration}/{_proxyResetRequests}/{_proxyResetCompletions}/{Bool(_reinitializeRequested)} " +
               $"A={AutoTestCode()}:{_autoTestFrame}/{_autoTestRuns} E={ErrorCode()} " +
               $"D={animation}:{(int)_locomotion}/{(int)_animation}/{_animationFrame}/{_animationTick}/S{_animationStatesSeen:X}/F{_animationAdvanceStatesSeen:X}/Q{_attackPhaseCompletionMask:X}/P{_ownedHurtboxStatesSeen:X}/T{_animationTransitions}/A{_animationAdvances}/H{_ownedHurtboxSamples},{_contactOffsetX},{_contactOffsetY},{_contactHalfWidth},{_contactHalfHeight}/C{Bool(_crouched)}{Bool(_standBlocked)} " +
               $"VIS={independentVisual}:F{_independentNativeFrame:X2}/P{_visualPosesSeen:X16}/H{_hurtboxPosesSeen:X16}/E{_independentVisualEligible}/S{_independentVisualSubmitted}/R{_independentRestoreChecks},{_independentRestoreFailures}/X{_independentVisualFailures}/L{_independentVisualStatus} " +
               $"J={jumpForgiveness}:N{_normalJumps}/C{_coyoteJumps}/B{_bufferedJumps}/R{_coyoteUpdates},{_jumpBufferUpdates} " +
               $"X={outgoingAttack}:{_attackStatus}/T{_attackTimer}/O{_ownedAttackSlot},{Bool(_attackCleanupPending)}/Q{_attackQuarantineSlot},{_attackQuarantineGeneration:X},{_attackQuarantineRoomHash:X},{Bool(_attackQuarantineMutationStopped)}/A{_attackAllocations}/W{_attackNormalEngineWindows}/C{_attackCleanups},{_attackLifecycleCancellations}/F{_attackFailures},{_attackTimingFailures}/I{_attackLastAttackerId},{Bool(_attackAttackerIdValid)}/G{_attackArmMainGeneration},{_attackObservedMainGeneration},{_attackArmUpdateGeneration}/R{_attackCausalResults},{_attackHitFlagObservations},{_attackCooldownObservations},{_attackTargetHpChanges}/P{(_attackProfileLatched ? (int)_latchedAttackProfile.Kind : -1)},{(_attackProfileLatched ? _latchedAttackProfile.Item : 0)},{(_attackProfileLatched ? _latchedAttackProfile.Attack : 0)},{(_attackProfileLatched ? _latchedAttackProfile.Element : 0):X},{(_attackProfileLatched ? _latchedAttackProfile.HitState : 0):X}/E{_profileExtractions},{_profileExtractionFailures},{_equipmentRestoreChecks},{_equipmentRestoreFailures}/J{_projectileWindows},{_projectileLifetime} " +
               $"EN={enemies}:S{_enemyDiagnosticScans}/N{_enemyNativeCandidateSamples}/C{_enemyCompatibleCandidateSamples}/T{_nearestTargetSlot},{_nearestTargetEntityId},{_nearestTargetEnemyId},{_nearestTargetHp},{_nearestTargetP1Distance},{_nearestTargetP2Distance},{Bool(_nearestTargetCompatible)}/H{_nativeTargetHits},{_defeatedTargets},{_compatibleZeroHpHits}/L{_enemyDiagnosticStatus} " +
               $"AW={awareness}:C{_awarenessCalls}/O{_awarenessOverrides}/S{_awarenessChosenSlot}/L{_awarenessStatus} HU={hud}:E{_hudEligible}/S{_hudSubmitted} " +
               $"HP={health}:{_managedHp}/{ManagedMaxHp}/I{_damageInvulnerability}/K{_hurtLock}/D{_damageEvents},{_damageConsumed},{_damageSuppressedInvul},{_damageSuppressedHitInvul},{_lastDamage},{_lastDamageSlot},{_lastDamageElement:X}/N{Bool(_downed)},{_downedCount}/R{_reviveProgress},{_reviveStarts},{_reviveCancels},{_revives},{_reviveRecoveries}/F{_healthInvariantFailures}";
        return P2D4Report.Parse(report).CanonicalLine;
    }

    private string MissingHostButtons()
    {
        string value = "";
        AppendMissing(ref value, _hostSeen, Controller.Up, "Up");
        AppendMissing(ref value, _hostSeen, Controller.Right, "Right");
        AppendMissing(ref value, _hostSeen, Controller.Down, "Down");
        AppendMissing(ref value, _hostSeen, Controller.Left, "Left");
        AppendMissing(ref value, _hostSeen, Controller.Cross, "Cross");
        AppendMissing(ref value, _hostSeen, Controller.Circle, "Circle");
        AppendMissing(ref value, _hostSeen, Controller.Start, "Start");
        return value.Length == 0 ? "none" : value;
    }

    private static string MissingGameButtons(ushort seen)
    {
        string value = "";
        AppendMissing(ref value, seen, (ushort)GameButton.Up, "Up");
        AppendMissing(ref value, seen, (ushort)GameButton.Right, "Right");
        AppendMissing(ref value, seen, (ushort)GameButton.Down, "Down");
        AppendMissing(ref value, seen, (ushort)GameButton.Left, "Left");
        AppendMissing(ref value, seen, (ushort)GameButton.Cross, "Cross");
        AppendMissing(ref value, seen, (ushort)GameButton.Circle, "Circle");
        AppendMissing(ref value, seen, (ushort)GameButton.Start, "Start");
        return value.Length == 0 ? "none" : value;
    }

    private static void AppendMissing(ref string value, ushort seen, ushort mask, string name)
    {
        if ((seen & mask) != 0) return;
        if (value.Length != 0) value += ", ";
        value += name;
    }

    private static int CountSeen(ushort seen, ushort required)
    {
        int count = 0;
        ushort value = (ushort)(seen & required);
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }

    private static ushort ReadVirtualKeysDown()
    {
        ushort held = 0;
        if (HostWindow.IsKeyDown(Key.I)) held |= (ushort)GameButton.Up;
        if (HostWindow.IsKeyDown(Key.L)) held |= (ushort)GameButton.Right;
        if (HostWindow.IsKeyDown(Key.K)) held |= (ushort)GameButton.Down;
        if (HostWindow.IsKeyDown(Key.J)) held |= (ushort)GameButton.Left;
        if (HostWindow.IsKeyDown(Key.U)) held |= (ushort)GameButton.Cross;
        if (HostWindow.IsKeyDown(Key.O)) held |= (ushort)GameButton.Circle;
        if (HostWindow.IsKeyDown(Key.P)) held |= (ushort)GameButton.Start;
        return held;
    }

    private void ReconcileVirtualKeys()
    {
        ushort held = ReadVirtualKeysDown();
        _virtualRawHeld = held;
        _virtualRawSeen |= held;
        if (_virtualSuppressionFrames > 0 || !_enabled || _fatal)
        {
            if (_virtualPressed != 0) _virtualForcedReleases++;
            _virtualPressed = 0;
            _virtualNeutralSeen = held == 0;
            _virtualPreviousHeld = held;
            return;
        }

        if (!_virtualNeutralSeen)
        {
            _virtualPressed = 0;
            if (held == 0) _virtualNeutralSeen = true;
            _virtualPreviousHeld = held;
            return;
        }

        _virtualDownSeen |= (ushort)(held & ~_virtualPreviousHeld);
        _virtualUpSeen |= (ushort)(_virtualPreviousHeld & ~held);
        _virtualPressed = held;
        _virtualPreviousHeld = held;
    }

    private bool VirtualInputAllowed(IMemory memory) =>
        _enabled && !_fatal && _virtualSuppressionFrames == 0 &&
        Game.Available && Game.InAlucardMode() && !Game.IsLoading &&
        !Game.MenuOpen && !Game.MapOpen && DisplayModeHooks.IsStage &&
        memory.ReadU32(CutsceneControlAddress) == 0 &&
        !IsSpecialTransition(memory.ReadU32(SpecialTransitionAddress));

    private void SetVirtualKeyboard(bool enabled)
    {
        CancelAutomaticTest("input mode changed");
        ReleaseVirtualKeys();
        _virtualKeyboard = enabled;
        _physicalControllerTest = false;
        _previousConnected = Controller.Connected2;
        ClearInputObservations();
        _operationStatus = enabled
            ? "Virtual Player 2 keyboard enabled; release I/J/K/L/U/O/P once"
            : "Configured Pad 2 selected; release its buttons once";
    }

    private void ReleaseVirtualKeys()
    {
        if (_virtualPressed != 0) _virtualForcedReleases++;
        _virtualPressed = 0;
        _virtualPreviousHeld = ReadVirtualKeysDown();
        _virtualNeutralSeen = false;
    }

    private void QueueProxyReset()
    {
        CancelOwnedAttack("MANUAL_RESET");
        _movementSession.RequestManualReset();
        _operationStatus = "Proxy reset queued";
    }

    private void QueueAutomaticTest()
    {
        if (!_virtualKeyboard)
        {
            _autoTestStatus = "virtual keyboard mode is required";
            return;
        }
        ReleaseVirtualKeys();
        _autoTestState = AutoTestState.Queued;
        _autoTestFrame = 0;
        _autoTestStatus = "queued; close the Mods window";
    }

    private void AdvanceAutomaticTest(IMemory memory)
    {
        if (_autoTestState == AutoTestState.Queued)
        {
            _virtualPressed = 0;
            if (_virtualSuppressionFrames != 0 || !AutomaticInputSafe(memory) || !_grounded)
                return;
            _autoTestState = AutoTestState.Running;
            _autoTestFrame = 0;
            _autoTestStatus = "running";
        }

        if (_autoTestState != AutoTestState.Running) return;
        if (!AutomaticInputSafe(memory))
        {
            CancelAutomaticTest("gameplay became unsafe");
            return;
        }

        ushort mask;
        if (_autoTestFrame < 12) mask = (ushort)GameButton.Right;
        else if (_autoTestFrame < 16) mask = 0;
        else if (_autoTestFrame < 28) mask = (ushort)GameButton.Left;
        else if (_autoTestFrame < 32) mask = 0;
        else if (_autoTestFrame < 33)
        {
            if (!_grounded)
            {
                CancelAutomaticTest("jump phase was not grounded");
                return;
            }
            mask = (ushort)GameButton.Cross;
        }
        else if (_autoTestFrame < 64) mask = 0;
        else
        {
            _virtualPressed = 0;
            _autoTestState = AutoTestState.Completed;
            _autoTestRuns++;
            _autoTestStatus = "completed; input returned to neutral";
            return;
        }

        _virtualPressed = mask;
        _autoTestFrame++;
    }

    private bool AutomaticInputSafe(IMemory memory)
    {
        if (!_enabled || _fatal || _collisionDisabled || !_proxyInitialized || _transitionPending) return false;
        if (!Game.Available || !Game.InAlucardMode() || Game.IsLoading || Game.MenuOpen || Game.MapOpen) return false;
        if (!DisplayModeHooks.IsStage || memory.ReadU32(GameStepAddress) != (uint)PlayStep.Default ||
            memory.ReadU32(EngineStepAddress) != 1 || memory.ReadU32(CutsceneControlAddress) != 0 ||
            IsSpecialTransition(memory.ReadU32(SpecialTransitionAddress)))
            return false;

        uint foreground = memory.ReadU32(TilemapAddress);
        uint tileDefinitions = memory.ReadU32(TileDefinitionsAddress);
        if (!IsGuestPointer(foreground) || !IsGuestPointer(tileDefinitions) ||
            !IsGuestPointer(memory.ReadU32(tileDefinitions + 0x0C)))
            return false;
        uint horizontalSize = memory.ReadU32(TilemapAddress + 0x20);
        uint verticalSize = memory.ReadU32(TilemapAddress + 0x24);
        return horizontalSize is > 0 and <= 0x100 && verticalSize is > 0 and <= 0x100 &&
               memory.ReadU32(GameApi.CheckCollisionAddr) == ExpectedCollisionFunction;
    }

    private void CancelAutomaticTest(string reason)
    {
        if (_autoTestState is AutoTestState.Idle or AutoTestState.Completed or AutoTestState.Cancelled) return;
        _virtualPressed = 0;
        _autoTestState = AutoTestState.Cancelled;
        _autoTestStatus = reason;
    }

    private char AutoTestCode() => _autoTestState switch
    {
        AutoTestState.Idle => 'I',
        AutoTestState.Queued => 'Q',
        AutoTestState.Running => 'R',
        AutoTestState.Completed => 'P',
        AutoTestState.Cancelled => 'X',
        _ => '?',
    };

    private static bool BlocksSide(uint effects) =>
        (effects & EffectSolid) != 0 && (effects & (EffectSolidFromAbove | EffectSolidFromBelow | EffectSlopeMask)) == 0;

    private static bool BlocksFloor(uint effects) =>
        (effects & EffectSolid) != 0 && (effects & EffectSolidFromBelow) == 0;

    private static bool BlocksCeiling(uint effects) =>
        (effects & EffectSolid) != 0 && (effects & EffectSolidFromAbove) == 0;

    private static void TrackAxis(byte value, ref byte minimum, ref byte maximum)
    {
        minimum = Math.Min(minimum, value);
        maximum = Math.Max(maximum, value);
    }

    private static int Bool(bool value) => value ? 1 : 0;
    private static string DisplayMinimum(int value) => value == int.MaxValue ? "-" : value.ToString();

    private static bool IsGuestPointer(uint value) => value >= 0x80010000 && value < 0x80200000;

    private static bool IsGpuBuffer(uint value) =>
        value == GpuBuffersAddress || value == GpuBuffersAddress + GpuBufferStride;

    private static bool IsSpecialTransition(uint value) =>
        value is >= 2 and <= 6 || (value & 0x88000000) != 0;

    private void BeginTransition()
    {
        if (!_roomKnown) return;
        CancelOwnedAttack("TRANSITION");
        ClearLatchedAttackProfile();
        DisarmAwareness("TRANS");
        SuspendContactScan("TRANS");
        _attackSpawnPending = false;
        _attackTimer = 0;
        _contactVisualConfirmed = false;
    }

    private void ClearInputObservations()
    {
        _pad2Source.Reset();
        _hostSeen = _padSeen = _gameSeen = _tapSeen = 0;
        _neutralSeen = false;
        _virtualDownSeen = _virtualUpSeen = 0;
        _virtualPreviousHeld = ReadVirtualKeysDown();
        _virtualRawHeld = _virtualPreviousHeld;
        _virtualRawSeen = 0;
        _virtualNeutralSeen = false;
        _virtualNeutralObserved = false;
        _leftXMin = _leftYMin = _rightXMin = _rightYMin = 0xFF;
        _leftXMax = _leftYMax = _rightXMax = _rightYMax = 0;
    }

    private int ActiveAxisCount()
    {
        int count = 0;
        if (_leftXMax - _leftXMin >= 64) count++;
        if (_leftYMax - _leftYMin >= 64) count++;
        if (_rightXMax - _rightXMin >= 64) count++;
        if (_rightYMax - _rightYMin >= 64) count++;
        return count;
    }

    private string ErrorCode() => _fatal ? "X" : _collisionDisabled ?
        (_collisionFunction != ExpectedCollisionFunction ? $"A{_collisionFunction:X8}" : $"C{_lastRejectedCorrection}") :
        _attackHardFailure ? (_attackQuarantineSlot >= 0 ? $"Q{_attackQuarantineSlot}" : "ATK") :
        _healthInvariantFailures > 0 ? $"HP{_healthInvariantFailures}" :
        _contactDisabled ? (_contactGuardFailures > 0 ? "G" : "M") :
        _independentVisualDisabled ? "V" : _equipmentRestoreFailures > 0 ? "EQ" :
        _awarenessDisabled ? "AW" : "0";

    private enum AutoTestState
    {
        Idle,
        Queued,
        Running,
        Completed,
        Cancelled,
    }

    private enum AttackKind
    {
        Contact,
        Projectile,
    }

    private enum AwarenessHelper
    {
        DistanceX,
        DistanceY,
        Side,
    }

    private readonly struct AttackProfile
    {
        public readonly AttackKind Kind;
        public readonly ushort Item;
        public readonly short Attack;
        public readonly ushort Element;
        public readonly byte InvincibilityFrames;
        public readonly ushort StunFrames;
        public readonly ushort HitState;
        public readonly ushort HitEffect;
        public readonly byte HalfWidth;
        public readonly byte HalfHeight;
        public readonly int Direction;
        public readonly ushort Source;

        public AttackProfile(AttackKind kind, ushort item, short attack, ushort element,
            byte invincibilityFrames, ushort stunFrames, ushort hitState, ushort hitEffect,
            byte halfWidth, byte halfHeight, int direction, ushort source)
        {
            Kind = kind;
            Item = item;
            Attack = attack;
            Element = element;
            InvincibilityFrames = invincibilityFrames;
            StunFrames = stunFrames;
            HitState = hitState;
            HitEffect = hitEffect;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
            Direction = direction;
            Source = source;
        }
    }

    private readonly struct ProxyPose
    {
        public readonly int Index;
        public readonly ushort NativeFrame;
        public readonly ProxyHurtbox Hurtbox;

        public ProxyPose(int index, ushort nativeFrame, ProxyHurtbox hurtbox)
        {
            Index = index;
            NativeFrame = nativeFrame;
            Hurtbox = hurtbox;
        }
    }

    private readonly struct SpriteFrame
    {
        public readonly uint Pixels;
        public readonly int Width;
        public readonly int Height;
        public readonly int PivotX;
        public readonly int PivotY;

        public SpriteFrame(uint pixels, int width, int height, int pivotX, int pivotY)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            PivotX = pivotX;
            PivotY = pivotY;
        }
    }

    private readonly struct ProxyHurtbox
    {
        public readonly int OffsetX;
        public readonly int OffsetY;
        public readonly int HalfWidth;
        public readonly int HalfHeight;

        public ProxyHurtbox(int offsetX, int offsetY, int halfWidth, int halfHeight)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
        }
    }

    private readonly struct ContactShape
    {
        public readonly int CenterX;
        public readonly int CenterY;
        public readonly int HalfWidth;
        public readonly int HalfHeight;
        public readonly int WidescreenShift;

        public ContactShape(int centerX, int centerY, int halfWidth, int halfHeight, int widescreenShift)
        {
            CenterX = centerX;
            CenterY = centerY;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
            WidescreenShift = widescreenShift;
        }
    }

    private readonly struct ContactScanResult
    {
        public readonly int Eligible;
        public readonly int Overlaps;
        public readonly int Damaging;
        public readonly int Current;
        public readonly int LastSlot;
        public readonly ushort LastEntityId;

        public ContactScanResult(int eligible, int overlaps, int damaging, int current,
            int lastSlot, ushort lastEntityId)
        {
            Eligible = eligible;
            Overlaps = overlaps;
            Damaging = damaging;
            Current = current;
            LastSlot = lastSlot;
            LastEntityId = lastEntityId;
        }
    }

    private readonly struct ContactGuardSnapshot
    {
        private readonly ulong _entities;
        private readonly ulong _playerRuntime;
        private readonly ulong _status;
        private readonly ulong _castleFlags;
        private readonly ulong _castleMap;
        private readonly ulong _saveWorkspace;

        private ContactGuardSnapshot(ulong entities, ulong playerRuntime, ulong status,
            ulong castleFlags, ulong castleMap, ulong saveWorkspace)
        {
            _entities = entities;
            _playerRuntime = playerRuntime;
            _status = status;
            _castleFlags = castleFlags;
            _castleMap = castleMap;
            _saveWorkspace = saveWorkspace;
        }

        public static ContactGuardSnapshot Capture(ReadOnlySpan<byte> ram) => new(
            HashRamRange(ram, Game.EntitiesAddr, Entity.Stride * 256),
            HashRamRange(ram, ProtectedPlayerRuntimeAddress, ProtectedPlayerRuntimeLength),
            HashRamRange(ram, ProtectedStatusAddress, ProtectedStatusLength),
            HashRamRange(ram, ProtectedCastleFlagsAddress, ProtectedCastleFlagsLength),
            HashRamRange(ram, ProtectedCastleMapAddress, ProtectedCastleMapLength),
            HashRamRange(ram, ProtectedSaveWorkspaceAddress, ProtectedSaveWorkspaceLength));

        public bool Matches(ContactGuardSnapshot other) =>
            _entities == other._entities && _playerRuntime == other._playerRuntime &&
            _status == other._status && _castleFlags == other._castleFlags &&
            _castleMap == other._castleMap && _saveWorkspace == other._saveWorkspace;

        public string FirstDifference(ContactGuardSnapshot other)
        {
            if (_entities != other._entities) return "entities";
            if (_playerRuntime != other._playerRuntime) return "player";
            if (_status != other._status) return "status";
            if (_castleFlags != other._castleFlags) return "flags";
            if (_castleMap != other._castleMap) return "map";
            if (_saveWorkspace != other._saveWorkspace) return "save";
            return "unknown";
        }
    }

    private readonly struct CollisionResult
    {
        public readonly uint Effects;
        public readonly int RightCorrection;
        public readonly int LeftCorrection;
        public readonly int FloorCorrection;
        public readonly int CeilingCorrection;

        public CollisionResult(uint effects, int rightCorrection, int leftCorrection, int floorCorrection, int ceilingCorrection)
        {
            Effects = effects;
            RightCorrection = rightCorrection;
            LeftCorrection = leftCorrection;
            FloorCorrection = floorCorrection;
            CeilingCorrection = ceilingCorrection;
        }
    }

    private readonly struct RoomIdentity : IEquatable<RoomIdentity>
    {
        private readonly byte _stage;
        private readonly byte _room;
        private readonly byte _area;
        private readonly int _left;
        private readonly int _top;
        private readonly int _right;
        private readonly int _bottom;

        public RoomIdentity(byte stage, byte room, byte area, int left, int top, int right, int bottom)
        {
            _stage = stage;
            _room = room;
            _area = area;
            _left = left;
            _top = top;
            _right = right;
            _bottom = bottom;
        }

        public bool Equals(RoomIdentity other) =>
            _stage == other._stage && _room == other._room && _area == other._area &&
            _left == other._left && _top == other._top &&
            _right == other._right && _bottom == other._bottom;

        public override bool Equals(object? obj) => obj is RoomIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_stage, _room, _area, _left, _top, _right, _bottom);

        public uint StableHash()
        {
            uint hash = 2166136261;
            hash = (hash ^ _stage) * 16777619;
            hash = (hash ^ _room) * 16777619;
            hash = (hash ^ _area) * 16777619;
            hash = (hash ^ unchecked((uint)_left)) * 16777619;
            hash = (hash ^ unchecked((uint)_top)) * 16777619;
            hash = (hash ^ unchecked((uint)_right)) * 16777619;
            return (hash ^ unchecked((uint)_bottom)) * 16777619;
        }

        public ManagedRoomKey ManagedKey() => new(_stage, _room, _area, _left, _top, _right, _bottom);
    }
}
