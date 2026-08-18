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
    private const string Version = "0.1.5";
    private const uint ExpectedCollisionFunction = 0x800EF45C;

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
    private const int HeadOffset = -22;
    private const int FootOffset = 25;
    private const int ColliderSize = 0x24;
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
    private int _proxyResetRequests;
    private int _proxyResetCompletions;

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

    private bool _proxyInitialized;
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
    private bool _reinitializeRequested;

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

    private readonly ulong[] _contactIdentities = new ulong[ContactSlotCount];
    private readonly ulong[] _nextContactIdentities = new ulong[ContactSlotCount];
    private bool _contactBaselinePending = true;
    private bool _contactDisabled;
    private bool _contactOverlap;
    private bool _contactVisualConfirmed;
    private long _contactScanFrames;
    private long _contactSlotsScanned;
    private long _contactEligibleSamples;
    private long _contactOverlapSamples;
    private long _contactDamagingSamples;
    private long _contactStaySamples;
    private int _contactCurrent;
    private int _contactPeak;
    private int _contactEntries;
    private int _contactExits;
    private int _contactResets;
    private int _contactGuardChecks;
    private int _contactGuardFailures;
    private int _contactLastSlot = -1;
    private ushort _contactLastEntityId;
    private int _contactOffsetX;
    private int _contactOffsetY;
    private int _contactHalfWidth;
    private int _contactHalfHeight;
    private string _contactGuardRegion = "none";
    private string _contactStatus = "WAIT";
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
    private string _collisionFailureReason = "none";

    private RoomIdentity _room;
    private bool _roomKnown;
    private int _roomStableFrames;
    private bool _transitionPending;
    private RoomIdentity _transitionOrigin;
    private int _roomLayerEvents;
    private int _completedTransitions;
    private int _passedTransitions;
    private int _postTransitionOriginX;
    private bool _awaitingPostTransitionMovement;
    private bool _postTransitionMoved;

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
        _instance = this;
        Event.AddListener<VSyncEvent>(OnVSync);
        Event.AddListener<PadReadEvent>(OnPadRead);
        Event.AddListener<PlayerLoadedEvent>(OnPlayerLoaded);
        Event.AddListener<RoomLayerLoadEvent>(OnRoomLayerLoaded);
        Console.WriteLine($"[CoopProbe] Loaded v{Version}; target SymphonyRecomp v0.4.3b");
    }

    public void OnUnload()
    {
        if (ReferenceEquals(_instance, this)) _instance = null;
        _virtualPressed = 0;
        Event.RemoveListener<VSyncEvent>(OnVSync);
        Event.RemoveListener<PadReadEvent>(OnPadRead);
        Event.RemoveListener<PlayerLoadedEvent>(OnPlayerLoaded);
        Event.RemoveListener<RoomLayerLoadEvent>(OnRoomLayerLoaded);
        _proxyInitialized = false;
        SuspendContactScan("UNLOAD");
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
            if (!enabled) SuspendContactScan("DISABLED");
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

        if (ImGui.Button("Reset diagnostic")) ResetDiagnostic();
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
        ImGui.Text($"Collision API: 0x{_collisionFunction:X8} | calls: {_collisionCalls} | restore failures: {_collisionRestoreFailures} | rejected corrections: {_invalidCorrections}");
        ImGui.Text($"Avatar frames/eligible/DrawOTag: {_renderSubmitted}/{_renderEligible}/{_drawOtCalls} | HLE active/ready: {GpuHle.Active}/{GpuHle.Backend?.Ready == true}");
        ImGui.Text($"Native sprite submitted/captured/eligible/flipped/fallback: {_nativeSpriteSubmitted}/{_nativeSpriteCaptured}/{_nativeSpriteEligible}/{_nativeSpriteFlipped}/{_nativeSpriteFallbacks} | streak {_nativeSpriteStreak}, flipped {Bool(_nativeSpriteFlipSeenInStreak)} | {_nativeSpriteStatus}");
        ImGui.Text($"Contact shadow current/peak/enter/stay/exit: {_contactCurrent}/{_contactPeak}/{_contactEntries}/{_contactStaySamples}/{_contactExits} | {_contactStatus}");
        ImGui.Text($"Mirrored hurtbox offset {_contactOffsetX},{_contactOffsetY} half-size {_contactHalfWidth},{_contactHalfHeight} | last slot/id {_contactLastSlot}/{_contactLastEntityId}");
        ImGui.Text($"Contact scans/slots/eligible/overlap/damaging: {_contactScanFrames}/{_contactSlotsScanned}/{_contactEligibleSamples}/{_contactOverlapSamples}/{_contactDamagingSamples}");
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
        ImGui.TextDisabled("The proxy uses no SOTN entity or persistent primitive slot. Contact scanning cannot write Player 1, enemies, saves, or progression.");
    }

    private void OnVSync(VSyncEvent e)
    {
        try
        {
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
            _proxyInitialized = false;
            ResetNativeSpriteFrame();
            _visualConfirmed = false;
            _contactVisualConfirmed = false;
            SuspendContactScan("PLAYER");
            _roomKnown = false;
            _transitionPending = false;
            _awaitingPostTransitionMovement = false;
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
            _roomLayerEvents++;
            BeginTransition();
            _proxyInitialized = false;
            ResetNativeSpriteFrame();
            _contactVisualConfirmed = false;
            SuspendContactScan("TRANS");
            _roomStableFrames = 0;
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
            if (mod._fatal) return;
            mod.UpdateProxy(context, memory);
        }
        catch (Exception ex)
        {
            mod.Fail("UpdatePlayerEntities hook", ex);
        }
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

        _safeFrame = TryGetSafeState(memory, out _safeReason);
        if (_collisionDisabled)
        {
            _safeFrame = false;
            if (_collisionFunction == ExpectedCollisionFunction) _safeCode = "COL";
            _safeReason = _collisionFailureReason;
        }
        if (!_enabled || !_safeFrame)
        {
            if (_reinitializeRequested)
                _operationStatus = $"Proxy reset blocked: {_safeReason}";
            return;
        }

        UpdateRoomIdentity(memory);
        if (_reinitializeRequested || !_proxyInitialized)
        {
            bool requested = _reinitializeRequested;
            InitializeProxy(memory);
            _reinitializeRequested = false;
            if (requested)
            {
                _proxyResetCompletions++;
                _operationStatus = "Proxy reset completed at Player 1";
            }
            else _operationStatus = "Proxy initialized at Player 1";
        }

        _collisionThisFrame = false;
        TryCollision(context, memory, _proxyX >> 16, _proxyY >> 16, out _);

        _gamePressed = Game.Pressed2;
        _gameTapped = Game.Tapped2;
        bool sourceAvailable = _virtualKeyboard || Controller.Connected2;
        bool sourceNeutral = _virtualKeyboard ? _virtualNeutralSeen : _neutralSeen;
        bool canControl = sourceAvailable && sourceNeutral;
        int beforeX = _proxyX;
        bool commandedLeft = false;
        bool commandedRight = false;

        if (canControl)
        {
            bool left = IsGamePressed(GameButton.Left);
            bool right = IsGamePressed(GameButton.Right);
            commandedLeft = left && !right;
            commandedRight = right && !left;
            _velocityX = left == right ? 0 : left ? -RunSpeed : RunSpeed;
            if (_velocityX < 0) _facingLeft = true;
            else if (_velocityX > 0) _facingLeft = false;

            if ((_gameTapped & (ushort)GameButton.Cross) != 0 && _grounded)
            {
                _velocityY = JumpSpeed;
                _grounded = false;
                _jumpStartY = _proxyY;
                _jumpPending = true;
            }
        }
        else _velocityX = 0;

        if (!_grounded) _velocityY = Math.Min(MaxFallSpeed, _velocityY + Gravity);
        else if (_velocityY > 0) _velocityY = 0;

        MoveHorizontal(context, memory);
        MoveVertical(context, memory);
        RefreshGrounded(context, memory);

        int deltaX = _proxyX - beforeX;
        if (commandedLeft && deltaX < 0) _leftDistanceRaw += -(long)deltaX;
        else if (commandedRight && deltaX > 0) _rightDistanceRaw += deltaX;

        if (_jumpPending && _proxyY <= _jumpStartY - 4 * FixedOne)
        {
            _jumpObserved = true;
            _jumpPending = false;
        }

        if (_awaitingPostTransitionMovement && Math.Abs(_proxyX - _postTransitionOriginX) >= 8 * FixedOne)
        {
            _postTransitionMoved = true;
            _awaitingPostTransitionMovement = false;
            _passedTransitions++;
        }

        int playerX = unchecked((int)memory.ReadU32(PlayerWorldXAddress));
        int playerY = unchecked((int)memory.ReadU32(PlayerWorldYAddress));
        if (Math.Abs((_proxyX >> 16) - playerX) > 256 || Math.Abs((_proxyY >> 16) - playerY) > 192)
            InitializeProxy(memory);

    }

    private void MoveHorizontal(CpuContext context, IMemory memory)
    {
        int remaining = _velocityX;
        while (remaining != 0)
        {
            int step = Math.Clamp(remaining, -FixedOne, FixedOne);
            _proxyX += step;
            remaining -= step;

            bool movingRight = step > 0;
            int queryX = (_proxyX >> 16) + (movingRight ? HalfWidth : -HalfWidth);
            int correction = 0;
            bool blocked = false;
            int[] offsets = [HeadOffset + 1, 0, FootOffset - 2];
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!TryCollision(context, memory, queryX, (_proxyY >> 16) + offsets[i], out CollisionResult hit)) return;
                if (!BlocksSide(hit.Effects)) continue;
                int value = movingRight ? hit.RightCorrection : hit.LeftCorrection;
                correction = !blocked ? value : movingRight ? Math.Min(correction, value) : Math.Max(correction, value);
                blocked = true;
            }

            if (!blocked) continue;
            if (!TryApplyCorrection(ref _proxyX, correction)) return;
            _velocityX = 0;
            _wallCorrections++;
            _collisionThisFrame = true;
            return;
        }
    }

    private void MoveVertical(CpuContext context, IMemory memory)
    {
        int remaining = _velocityY;
        while (remaining != 0)
        {
            int step = Math.Clamp(remaining, -FixedOne, FixedOne);
            _proxyY += step;
            remaining -= step;

            bool falling = step > 0;
            int queryY = (_proxyY >> 16) + (falling ? FootOffset : HeadOffset);
            int correction = 0;
            bool blocked = false;
            int[] offsets = [-HalfWidth + 1, 0, HalfWidth - 1];
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!TryCollision(context, memory, (_proxyX >> 16) + offsets[i], queryY, out CollisionResult hit)) return;
                if (falling ? !BlocksFloor(hit.Effects) : !BlocksCeiling(hit.Effects)) continue;
                int value = falling ? hit.FloorCorrection : hit.CeilingCorrection;
                correction = !blocked ? value : falling ? Math.Min(correction, value) : Math.Max(correction, value);
                blocked = true;
            }

            if (!blocked) continue;
            if (!TryApplyCorrection(ref _proxyY, correction)) return;
            _velocityY = 0;
            _grounded = falling;
            if (falling) _groundContacts++;
            else _ceilingCorrections++;
            _collisionThisFrame = true;
            return;
        }
    }

    private void RefreshGrounded(CpuContext context, IMemory memory)
    {
        if (_velocityY < 0)
        {
            _grounded = false;
            return;
        }

        bool floor = false;
        int queryY = (_proxyY >> 16) + FootOffset + 1;
        int[] offsets = [-HalfWidth + 1, 0, HalfWidth - 1];
        for (int i = 0; i < offsets.Length; i++)
        {
            if (!TryCollision(context, memory, (_proxyX >> 16) + offsets[i], queryY, out CollisionResult hit)) return;
            if (BlocksFloor(hit.Effects) && hit.FloorCorrection is >= -4 and <= 0)
            {
                floor = true;
                _groundContacts++;
                if ((hit.Effects & EffectSolidFromAbove) != 0) _oneWayContacts++;
            }
        }
        _grounded = floor;
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

        var contextSnapshot = context.Snapshot();
        uint savedSp = context.SP;
        uint temporarySp = (savedSp - 0x80u) & ~7u;
        uint output = temporarySp + 0x10u;
        if (savedSp < 0x80010000 || savedSp > 0x80200000 || output < 0x80010000 || output + ColliderSize >= 0x80200000)
            throw new InvalidOperationException($"Guest stack is outside RAM: 0x{savedSp:X8}");

        Span<byte> saved = stackalloc byte[ColliderSize];
        int savedCount = 0;
        try
        {
            for (int i = 0; i < ColliderSize; i++)
            {
                saved[i] = memory.ReadU8(output + (uint)i);
                savedCount++;
                memory.WriteU8(output + (uint)i, 0);
            }

            context.SP = temporarySp;
            GameApi.CallApi(context, memory, GameApi.CheckCollisionAddr,
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
                for (int i = 0; i < savedCount; i++) memory.WriteU8(output + (uint)i, saved[i]);
                for (int i = 0; i < savedCount; i++)
                {
                    if (memory.ReadU8(output + (uint)i) == saved[i]) continue;
                    _collisionRestoreFailures++;
                    throw new InvalidOperationException($"Collision scratch restore failed at byte {i}");
                }
            }
            finally
            {
                context.Restore(contextSnapshot);
            }
        }

        _collisionCalls++;
        if ((result.Effects & EffectSolid) != 0) _sawSolid = true;
        else _sawEmpty = true;
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
        bool nativeSpriteDrawn = _nativeSpriteDrawnThisFrame;
        _nativeSpriteDrawnThisFrame = false;

        if (!_enabled || !_safeFrame || !_proxyInitialized || !Game.Available || !Game.InGame || Game.IsLoading ||
            Game.MenuOpen || Game.MapOpen || !DisplayModeHooks.IsStage)
            return;
        _renderEligible++;
        if (nativeSpriteDrawn)
        {
            _renderSubmitted++;
            return;
        }

        var gpu = RecompOne.Runtime.Runtime.Gpu;
        if (gpu == null) return;

        int scrollX = unchecked((int)memory.ReadU32(ScrollXAddress)) >> 16;
        int scrollY = unchecked((int)memory.ReadU32(ScrollYAddress)) >> 16;
        int x = unchecked((int)memory.ReadU32(BackbufferXAddress)) + (_proxyX >> 16) - scrollX;
        int y = unchecked((int)memory.ReadU32(BackbufferYAddress)) + (_proxyY >> 16) - scrollY;
        if (x < -32 || x > 288 || y < -48 || y > 288) return;

        if (!WriteStageDrawEnvironment(gpu, memory, currentBuffer)) return;
        DrawGpuTile(gpu, x - HalfWidth - 1, y + HeadOffset - 1, HalfWidth * 2 + 2,
            FootOffset - HeadOffset + 2, 0, 0, 0);

        byte r = _contactOverlap ? (byte)255 : _collisionThisFrame ? (byte)255 : (byte)32;
        byte g = _contactOverlap ? (byte)48 : _grounded ? (byte)255 : (byte)192;
        byte b = _contactOverlap ? (byte)160 : (byte)255;
        DrawGpuTile(gpu, x - HalfWidth, y + HeadOffset, HalfWidth * 2,
            FootOffset - HeadOffset, r, g, b);
        DrawGpuTile(gpu, x + (_facingLeft ? -5 : 2), y - 6, 3, 3, 255, 232, 32);
        _nativeSpriteFallbacks++;
        _nativeSpriteStreak = 0;
        _nativeSpriteFlipSeenInStreak = false;
        _renderSubmitted++;
    }

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
        uint destination = buffer + GpuGt4Offset + used * GpuGt4Stride;
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
        _nativeSpriteCaptured++;

        int scrollX = unchecked((int)memory.ReadU32(ScrollXAddress)) >> 16;
        int scrollY = unchecked((int)memory.ReadU32(ScrollYAddress)) >> 16;
        int targetX = unchecked((int)memory.ReadU32(BackbufferXAddress)) + (_proxyX >> 16) - scrollX;
        int targetY = unchecked((int)memory.ReadU32(BackbufferYAddress)) + (_proxyY >> 16) - scrollY;
        bool flip = _nativeCaptureFacingLeft != _facingLeft;

        Span<int> transformed = stackalloc int[8];
        ReadOnlySpan<uint> coordinateOffsets = [0x08, 0x14, 0x20, 0x2C];
        for (int i = 0; i < coordinateOffsets.Length; i++)
        {
            uint offset = coordinateOffsets[i];
            int sourceX = unchecked((short)memory.ReadU16(source + offset));
            int sourceY = unchecked((short)memory.ReadU16(source + offset + 2));
            int x = targetX + (flip ? -(sourceX - _nativeCaptureAnchorX) : sourceX - _nativeCaptureAnchorX);
            int y = targetY + sourceY - _nativeCaptureAnchorY;
            if (x is < -1024 or > 1023 || y is < -1024 or > 1023)
            {
                _nativeSpriteStatus = "POS";
                ResetNativeSpriteStreak();
                return;
            }
            transformed[i * 2] = x;
            transformed[i * 2 + 1] = y;
        }

        for (uint offset = 0x04; offset < GpuGt4Stride; offset += 4)
            memory.WriteU32(destination + offset, memory.ReadU32(source + offset));
        for (int i = 0; i < coordinateOffsets.Length; i++)
        {
            uint offset = coordinateOffsets[i];
            memory.WriteU32(destination + offset,
                ((uint)(ushort)transformed[i * 2 + 1] << 16) | (ushort)transformed[i * 2]);
        }

        // Tint only the copied packet: cyan normally, magenta on contact.
        byte tintR = _contactOverlap ? (byte)255 : (byte)96;
        byte tintG = _contactOverlap ? (byte)48 : (byte)176;
        byte tintB = _contactOverlap ? (byte)160 : (byte)255;
        memory.WriteU8(destination + 0x04, tintR);
        memory.WriteU8(destination + 0x05, tintG);
        memory.WriteU8(destination + 0x06, tintB);
        memory.WriteU8(destination + 0x07, (byte)(command & ~1));
        SetNativeSpriteColor(memory, destination + 0x10, tintR, tintG, tintB);
        SetNativeSpriteColor(memory, destination + 0x1C, tintR, tintG, tintB);
        SetNativeSpriteColor(memory, destination + 0x28, tintR, tintG, tintB);

        // Reserve first, then splice after Player 1. Any later write failure leaves
        // either an unlinked reserved packet or a linked packet that cannot be reused.
        memory.WriteU32(GpuUsageGt4Address, used + 1);
        memory.WriteU32(destination, 12u << 24 | (sourceTag & 0x00FFFFFFu));
        memory.WriteU32(source, (sourceTag & 0xFF000000u) | (destination & 0x00FFFFFFu));

        _nativeSpriteDrawnThisFrame = true;
        _nativeSpriteSubmitted++;
        if (flip) _nativeSpriteFlipped++;
        _nativeSpriteStreak++;
        if (flip) _nativeSpriteFlipSeenInStreak = true;
        _nativeSpriteStatus = "OK";
    }

    private static void SetNativeSpriteColor(IMemory memory, uint address, byte r, byte g, byte b)
    {
        memory.WriteU8(address, r);
        memory.WriteU8(address + 1, g);
        memory.WriteU8(address + 2, b);
    }

    private void ResetNativeSpriteFrame()
    {
        _nativeCapturePending = false;
        _nativeSpriteDrawnThisFrame = false;
        ResetNativeSpriteStreak();
        _nativeSpriteStatus = "WAIT";
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
        !_fatal && !_contactDisabled && _safeFrame && AutomaticInputSafe(memory) &&
        _roomKnown && ReadRoomIdentity(memory).Equals(_room);

    private bool TryBuildContactShape(ReadOnlySpan<byte> ram, out ContactShape shape)
    {
        shape = default;
        ushort state = ReadRamU16(ram, PlayerEntityAddress + EntityHitboxStateOffset);
        int offsetX = ReadRamS16(ram, PlayerEntityAddress + EntityHitboxOffsetX);
        int offsetY = ReadRamS16(ram, PlayerEntityAddress + EntityHitboxOffsetY);
        int halfWidth = ReadRamU8(ram, PlayerEntityAddress + EntityHitboxWidthOffset);
        int halfHeight = ReadRamU8(ram, PlayerEntityAddress + EntityHitboxHeightOffset);
        if ((state & 1) == 0 || halfWidth is 0 or > 32 || halfHeight is 0 or > 32 ||
            offsetX is < -64 or > 64 || offsetY is < -64 or > 64)
            return false;

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
        shape = new ContactShape(centerX, centerY, halfWidth, halfHeight, widescreenShift);
        return true;
    }

    private ContactScanResult ScanContactCandidates(ReadOnlySpan<byte> ram, ContactShape shape)
    {
        Array.Clear(_nextContactIdentities);
        int eligible = 0;
        int overlaps = 0;
        int damaging = 0;
        int current = 0;
        int lastSlot = -1;
        ushort lastEntityId = 0;

        for (int i = 0; i < ContactSlotCount; i++)
        {
            int slot = ContactSlotStart + i;
            uint entity = Game.EntitiesAddr + (uint)(slot * Entity.Stride);
            ushort state = ReadRamU16(ram, entity + EntityHitboxStateOffset);
            if ((state & 1) == 0) continue;

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
            eligible++;

            if (Math.Abs(centerX - shape.CenterX) >= halfWidth + shape.HalfWidth ||
                Math.Abs(centerY - shape.CenterY) >= halfHeight + shape.HalfHeight)
                continue;

            ushort enemyId = ReadRamU16(ram, entity + EntityEnemyIdOffset);
            ulong identity = ((ulong)update << 32) | ((ulong)enemyId << 16) | entityId;
            _nextContactIdentities[i] = identity;
            overlaps++;
            current++;
            if (ReadRamS16(ram, entity + EntityAttackOffset) > 0) damaging++;
            lastSlot = slot;
            lastEntityId = entityId;
        }

        return new ContactScanResult(eligible, overlaps, damaging, current, lastSlot, lastEntityId);
    }

    private void CommitContactScan(ContactScanResult result)
    {
        _contactScanFrames++;
        _contactSlotsScanned += ContactSlotCount;
        _contactEligibleSamples += result.Eligible;
        _contactOverlapSamples += result.Overlaps;
        _contactDamagingSamples += result.Damaging;
        if (result.LastSlot >= 0)
        {
            _contactLastSlot = result.LastSlot;
            _contactLastEntityId = result.LastEntityId;
        }

        if (_contactBaselinePending)
        {
            Array.Copy(_nextContactIdentities, _contactIdentities, ContactSlotCount);
            _contactBaselinePending = false;
        }
        else
        {
            for (int i = 0; i < ContactSlotCount; i++)
            {
                ulong previous = _contactIdentities[i];
                ulong current = _nextContactIdentities[i];
                if (previous == current)
                {
                    if (current != 0) _contactStaySamples++;
                }
                else
                {
                    if (previous != 0) _contactExits++;
                    if (current != 0) _contactEntries++;
                }
                _contactIdentities[i] = current;
            }
        }

        _contactCurrent = result.Current;
        _contactPeak = Math.Max(_contactPeak, _contactCurrent);
        _contactOverlap = _contactCurrent != 0;
        _contactStatus = _contactOverlap ? "CONTACT" : "OK";
    }

    private void SuspendContactScan(string status)
    {
        bool reset = !_contactBaselinePending || _contactCurrent != 0 || _contactOverlap;
        Array.Clear(_contactIdentities);
        Array.Clear(_nextContactIdentities);
        _contactBaselinePending = true;
        _contactCurrent = 0;
        _contactOverlap = false;
        if (reset) _contactResets++;
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

    private void UpdateRoomIdentity(IMemory memory)
    {
        RoomIdentity current = ReadRoomIdentity(memory);
        if (!_roomKnown)
        {
            _room = current;
            _roomKnown = true;
            _roomStableFrames = 1;
        }
        else if (!_room.Equals(current))
        {
            BeginTransition();
            _room = current;
            _roomStableFrames = 1;
            _proxyInitialized = false;
        }
        else _roomStableFrames++;

        if (_transitionPending && _roomStableFrames >= 30 && _proxyInitialized)
        {
            _transitionPending = false;
            if (!_transitionOrigin.Equals(_room))
            {
                _completedTransitions++;
                _postTransitionOriginX = _proxyX;
                _awaitingPostTransitionMovement = true;
                _postTransitionMoved = false;
            }
        }
    }

    private static RoomIdentity ReadRoomIdentity(IMemory memory) => new(
        memory.ReadU8(Game.StageIdAddr),
        memory.ReadU8(Game.RoomAddr),
        memory.ReadU8(Game.AreaAddr),
        unchecked((int)memory.ReadU32(RoomLeftAddress)),
        unchecked((int)memory.ReadU32(RoomTopAddress)),
        unchecked((int)memory.ReadU32(RoomRightAddress)),
        unchecked((int)memory.ReadU32(RoomBottomAddress)));

    private void InitializeProxy(IMemory memory)
    {
        SuspendContactScan("PROXY");
        _proxyX = unchecked((int)memory.ReadU32(PlayerWorldXAddress)) << 16;
        _proxyY = unchecked((int)memory.ReadU32(PlayerWorldYAddress)) << 16;
        _velocityX = 0;
        _velocityY = 0;
        _grounded = false;
        _jumpPending = false;
        _proxyInitialized = true;
    }

    private void SampleEntitySlots(IMemory memory)
    {
        _freePlayerCurrent = CountFree(memory, 0, 16, out _);
        _freeAttackCurrent = CountFree(memory, 16, 64, out _longestAttackCurrent);
        _freeStageCurrent = CountFree(memory, 64, 208, out _);
        _freeTailCurrent = CountFree(memory, 208, 256, out _);
        _minimumFreeAttack = Math.Min(_minimumFreeAttack, _freeAttackCurrent);
        _minimumLongestAttack = Math.Min(_minimumLongestAttack, _longestAttackCurrent);
        _slotSamples++;
    }

    private static int CountFree(IMemory memory, int start, int end, out int longestRun)
    {
        int free = 0;
        int run = 0;
        longestRun = 0;
        for (int slot = start; slot < end; slot++)
        {
            uint entity = Game.EntitiesAddr + (uint)(slot * Entity.Stride);
            bool available = memory.ReadU16(entity + 0x26) == 0;
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

    private void ResetDiagnostic()
    {
        _diagnosticGeneration++;
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
        _previousConnected = Controller.Connected2;
        _hostSeen = _padSeen = _gameSeen = _tapSeen = 0;
        _hostState = _padState = 0xFFFF;
        _gamePressed = _gameTapped = 0;
        _neutralSeen = false;
        _virtualPressed = 0;
        _virtualPreviousHeld = ReadVirtualKeysDown();
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
        _proxyInitialized = false;
        _reinitializeRequested = false;
        _proxyResetRequests = _proxyResetCompletions = 0;
        _leftDistanceRaw = _rightDistanceRaw = 0;
        _jumpObserved = false;
        _jumpPending = false;
        _collisionCalls = 0;
        _collisionRestoreFailures = 0;
        _invalidCorrections = 0;
        _lastRejectedCorrection = 0;
        _groundContacts = _wallCorrections = _ceilingCorrections = _oneWayContacts = 0;
        _collisionFunction = 0;
        _sawSolid = _sawEmpty = false;
        _renderEligible = _renderSubmitted = _drawOtCalls = 0;
        _nativeCapturePending = false;
        _nativeSpriteDrawnThisFrame = false;
        _nativeSpriteEligible = _nativeSpriteCaptured = _nativeSpriteSubmitted = 0;
        _nativeSpriteFlipped = _nativeSpriteFallbacks = 0;
        _nativeSpriteStreak = 0;
        _nativeSpriteFlipSeenInStreak = false;
        _nativeSpriteStatus = "WAIT";
        Array.Clear(_contactIdentities);
        Array.Clear(_nextContactIdentities);
        _contactBaselinePending = true;
        _contactDisabled = false;
        _contactOverlap = false;
        _contactScanFrames = _contactSlotsScanned = _contactEligibleSamples = 0;
        _contactOverlapSamples = _contactDamagingSamples = _contactStaySamples = 0;
        _contactCurrent = _contactPeak = _contactEntries = _contactExits = _contactResets = 0;
        _contactGuardChecks = _contactGuardFailures = 0;
        _contactLastSlot = -1;
        _contactLastEntityId = 0;
        _contactOffsetX = _contactOffsetY = _contactHalfWidth = _contactHalfHeight = 0;
        _contactGuardRegion = "none";
        _contactStatus = "WAIT";
        _roomKnown = false;
        _roomStableFrames = 0;
        _transitionPending = false;
        _roomLayerEvents = 0;
        _completedTransitions = 0;
        _passedTransitions = 0;
        _awaitingPostTransitionMovement = false;
        _postTransitionMoved = false;
        _slotSamples = 0;
        _freePlayerCurrent = _freeAttackCurrent = _freeStageCurrent = _freeTailCurrent = 0;
        _longestAttackCurrent = 0;
        _minimumFreeAttack = _minimumLongestAttack = int.MaxValue;
    }

    private void Fail(string subsystem, Exception ex)
    {
        if (_fatal) return;
        CancelAutomaticTest("diagnostic circuit breaker");
        _fatal = true;
        _enabled = false;
        _proxyInitialized = false;
        SuspendContactScan("FATAL");
        _firstError = $"{subsystem}: {ex.GetType().Name}: {ex.Message}";
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
        char render = _visualConfirmed && _renderSubmitted >= 60 ? 'P' : 'W';
        char nativeSprite = _visualConfirmed && _nativeSpriteSubmitted >= 60 &&
            _nativeSpriteStreak >= 60 && _nativeSpriteFlipSeenInStreak && _nativeSpriteStatus == "OK" ? 'P' : 'W';
        char contact = _contactDisabled || _contactGuardFailures != 0 ? 'F' :
            _contactScanFrames >= 120 && _contactSlotsScanned == _contactScanFrames * ContactSlotCount &&
            _contactGuardChecks == _contactScanFrames && _contactEntries > 0 && _contactStaySamples > 0 &&
            _contactExits > 0 && _contactDamagingSamples > 0 && _contactVisualConfirmed ? 'P' : 'W';
        char collision = _collisionDisabled || _collisionRestoreFailures != 0 ? 'F' :
            _collisionCalls >= 120 && _sawSolid && _sawEmpty && _groundContacts > 0 &&
            _wallCorrections > 0 && _ceilingCorrections > 0 ? 'P' : 'W';
        char transition = _completedTransitions > 0 && _passedTransitions == _completedTransitions &&
            !_transitionPending && !_awaitingPostTransitionMovement ? 'P' : 'W';
        char slots = _slotSamples < 5 ? 'W' : _minimumFreeAttack == 0 ? 'F' :
            _minimumFreeAttack >= 4 && _minimumLongestAttack >= 2 ? 'P' : 'W';

        string inputReport = _virtualKeyboard
            ? $"I={input}:K:-/{padCount}/{gameCount}/{tapCount}/A- K={virtualDownCount}/{virtualUpCount}/H{_virtualPressed:X4}/R{_virtualRawHeld:X4}/U{_virtualRawSeen:X4}/N{Bool(_virtualNeutralObserved)}/S{_virtualSuppressionFrames}"
            : $"I={input}:C:{hostCount}/{padCount}/{gameCount}/{tapCount}/A{axisCount} K=-";

        return $"P2D1 V={Version} H={hooks}:{_vsyncCalls}/{_mainEngineCalls}/{_updateCalls}/{_renderCalls}/{_pad2Reads} {inputReport} " +
               $"M={movement}:{_leftDistanceRaw >> 16}/{_rightDistanceRaw >> 16}/{Bool(_jumpObserved)} " +
               $"R={render}:{_renderSubmitted}/{_renderEligible}/{Bool(_visualConfirmed)}/D{_drawOtCalls}/H{Bool(GpuHle.Active)}{Bool(GpuHle.Backend?.Ready == true)} " +
               $"N={nativeSprite}:{_nativeSpriteSubmitted}/{_nativeSpriteCaptured}/{_nativeSpriteEligible}/{_nativeSpriteFlipped}/{_nativeSpriteFallbacks}/S{_nativeSpriteStreak}/F{Bool(_nativeSpriteFlipSeenInStreak)}/L{_nativeSpriteStatus} " +
               $"B={contact}:F{_contactScanFrames}/S{_contactSlotsScanned}/E{_contactEligibleSamples}/O{_contactOverlapSamples}/C{_contactCurrent}/P{_contactPeak}/D{_contactDamagingSamples}/I{_contactEntries}/T{_contactStaySamples}/X{_contactExits}/R{_contactResets}/G{_contactGuardChecks},{_contactGuardFailures}/V{Bool(_contactVisualConfirmed)}/H{_contactOffsetX},{_contactOffsetY},{_contactHalfWidth},{_contactHalfHeight}/Q{_contactGuardRegion}/L{_contactStatus} " +
               $"C={collision}:{_collisionCalls}/{_collisionRestoreFailures}/{_invalidCorrections}/{_groundContacts}/{_wallCorrections}/{_ceilingCorrections}/B{Bool(_sawSolid)}{Bool(_sawEmpty)} " +
               $"T={transition}:{_passedTransitions}/{_completedTransitions}/{_roomLayerEvents} " +
               $"S={slots}:{DisplayMinimum(_minimumFreeAttack)}/{DisplayMinimum(_minimumLongestAttack)}/{_slotSamples} " +
               $"G={_safeCode}:E{Bool(_enabled)}S{Bool(_safeFrame)}P{Bool(_proxyInitialized)} Q={_diagnosticGeneration}/{_proxyResetRequests}/{_proxyResetCompletions}/{Bool(_reinitializeRequested)} " +
               $"A={AutoTestCode()}:{_autoTestFrame}/{_autoTestRuns} E={ErrorCode()}";
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
        _proxyResetRequests++;
        _reinitializeRequested = true;
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

    private static bool IsGamePressed(GameButton button) => (Game.Pressed2 & (ushort)button) != 0;

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
        SuspendContactScan("TRANS");
        _contactVisualConfirmed = false;
        if (_awaitingPostTransitionMovement)
        {
            _awaitingPostTransitionMovement = false;
            _postTransitionMoved = false;
        }
        if (!_transitionPending) _transitionOrigin = _room;
        _transitionPending = true;
    }

    private void ClearInputObservations()
    {
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
        _contactDisabled ? (_contactGuardFailures > 0 ? "G" : "M") : "0";

    private enum AutoTestState
    {
        Idle,
        Queued,
        Running,
        Completed,
        Cancelled,
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
    }
}
