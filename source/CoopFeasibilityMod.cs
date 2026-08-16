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
    private const string Version = "0.1.2";
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
    private const uint BackbufferXAddress = 0x8006C39C;
    private const uint BackbufferYAddress = 0x8006C3A0;
    private const uint OrderingTableOffset = 0x474;

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
    private ushort _virtualDownSeen;
    private ushort _virtualUpSeen;
    private bool _virtualNeutralSeen;
    private bool _virtualNeutralObserved;
    private int _virtualSuppressionFrames;
    private int _virtualForcedReleases;

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
    }

    public void DrawSettings()
    {
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
        }

        bool virtualKeyboard = _virtualKeyboard;
        if (ImGui.Checkbox("Use virtual Player 2 keyboard", ref virtualKeyboard))
            SetVirtualKeyboard(virtualKeyboard);

        bool visible = _visualConfirmed;
        if (ImGui.Checkbox("I can see the proxy", ref visible)) _visualConfirmed = visible;

        bool physicalController = _physicalControllerTest;
        if (ImGui.Checkbox("Require analog test (physical controller 2)", ref physicalController))
            _physicalControllerTest = physicalController;

        if (ImGui.Button("Reset diagnostic")) ResetDiagnostic();
        ImGui.SameLine();
        if (ImGui.Button("Reset proxy to Player 1")) QueueProxyReset();
        ImGui.SameLine();
        if (ImGui.Button("Release virtual keys")) ReleaseVirtualKeys();
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
        ImGui.Text($"Virtual held/down/up: 0x{_virtualPressed:X4}/0x{_virtualDownSeen:X4}/0x{_virtualUpSeen:X4} | forced releases: {_virtualForcedReleases}");
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
        ImGui.Text($"GP0 draws/eligible/DrawOTag: {_renderSubmitted}/{_renderEligible}/{_drawOtCalls} | HLE active/ready: {GpuHle.Active}/{GpuHle.Backend?.Ready == true}");
        ImGui.Text($"Collision contacts ground/wall/ceiling/one-way: {_groundContacts}/{_wallCorrections}/{_ceilingCorrections}/{_oneWayContacts}");
        ImGui.Text($"Transitions passed/completed/layer events/post-move: {_passedTransitions}/{_completedTransitions}/{_roomLayerEvents}/{_postTransitionMoved}");
        ImGui.Text($"Free slots player/attack/stage/tail: {_freePlayerCurrent}/{_freeAttackCurrent}/{_freeStageCurrent}/{_freeTailCurrent}");
        ImGui.Text($"Attack-pool minimum free/run: {DisplayMinimum(_minimumFreeAttack)}/{DisplayMinimum(_minimumLongestAttack)} over {_slotSamples} samples");

        ImGui.Separator();
        string report = BuildReport();
        ImGui.TextWrapped(report);
        if (_fatal) ImGui.TextWrapped($"First error: {_firstError}");
        else if (_collisionDisabled) ImGui.TextWrapped($"Collision disabled: {_collisionFailureReason}");
        ImGui.TextDisabled("The proxy uses no SOTN entity slot and does not modify saves, progression, Player 1, or enemies.");
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
            ReleaseVirtualKeys();
            _proxyInitialized = false;
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
            ReleaseVirtualKeys();
            _roomLayerEvents++;
            BeginTransition();
            _proxyInitialized = false;
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
            if (!Game.Available || !Game.InGame) return;
            mod._gamePressed = Game.Pressed2;
            mod._gameTapped = Game.Tapped2;
            mod._gameSeen |= mod._gamePressed;
            mod._tapSeen |= mod._gameTapped;
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

    [PostHook("dra", "RenderEntities")]
    private static void AfterRenderEntities(CpuContext context, IMemory memory)
    {
        CoopFeasibility? mod = _instance;
        if (mod == null) return;
        try
        {
            mod._renderCalls++;
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
        if (!_enabled || !_safeFrame || !_proxyInitialized || !Game.Available || !Game.InGame || Game.IsLoading ||
            Game.MenuOpen || Game.MapOpen || !DisplayModeHooks.IsStage)
            return;

        uint currentBuffer = memory.ReadU32(CurrentBufferPointer);
        if (currentBuffer == 0) return;
        uint expectedOt = (currentBuffer + OrderingTableOffset) & 0x1FFFFC;
        if ((context.A0 & 0x1FFFFC) != expectedOt) return;
        _renderEligible++;

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

        byte r = _collisionThisFrame ? (byte)255 : (byte)32;
        byte g = _grounded ? (byte)255 : (byte)192;
        DrawGpuTile(gpu, x - HalfWidth, y + HeadOffset, HalfWidth * 2,
            FootOffset - HeadOffset, r, g, 255);
        DrawGpuTile(gpu, x + (_facingLeft ? -5 : 2), y - 6, 3, 3, 255, 232, 32);
        _renderSubmitted++;
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
        _virtualDownSeen = _virtualUpSeen = 0;
        _virtualNeutralSeen = false;
        _virtualNeutralObserved = false;
        _virtualSuppressionFrames = 2;
        _virtualForcedReleases = 0;
        _visualConfirmed = false;
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
        _fatal = true;
        _enabled = false;
        _proxyInitialized = false;
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
        char collision = _collisionDisabled || _collisionRestoreFailures != 0 ? 'F' :
            _collisionCalls >= 120 && _sawSolid && _sawEmpty && _groundContacts > 0 &&
            _wallCorrections > 0 && _ceilingCorrections > 0 ? 'P' : 'W';
        char transition = _completedTransitions > 0 && _passedTransitions == _completedTransitions &&
            !_transitionPending && !_awaitingPostTransitionMovement ? 'P' : 'W';
        char slots = _slotSamples < 5 ? 'W' : _minimumFreeAttack == 0 ? 'F' :
            _minimumFreeAttack >= 4 && _minimumLongestAttack >= 2 ? 'P' : 'W';

        string inputReport = _virtualKeyboard
            ? $"I={input}:K:-/{padCount}/{gameCount}/{tapCount}/A- K={virtualDownCount}/{virtualUpCount}/H{_virtualPressed:X4}/N{Bool(_virtualNeutralObserved)}"
            : $"I={input}:C:{hostCount}/{padCount}/{gameCount}/{tapCount}/A{axisCount} K=-";

        return $"P2D1 V={Version} H={hooks}:{_vsyncCalls}/{_mainEngineCalls}/{_updateCalls}/{_renderCalls}/{_pad2Reads} {inputReport} " +
               $"M={movement}:{_leftDistanceRaw >> 16}/{_rightDistanceRaw >> 16}/{Bool(_jumpObserved)} " +
               $"R={render}:{_renderSubmitted}/{_renderEligible}/{Bool(_visualConfirmed)}/D{_drawOtCalls}/H{Bool(GpuHle.Active)}{Bool(GpuHle.Backend?.Ready == true)} " +
               $"C={collision}:{_collisionCalls}/{_collisionRestoreFailures}/{_invalidCorrections}/{_groundContacts}/{_wallCorrections}/{_ceilingCorrections}/B{Bool(_sawSolid)}{Bool(_sawEmpty)} " +
               $"T={transition}:{_passedTransitions}/{_completedTransitions}/{_roomLayerEvents} " +
               $"S={slots}:{DisplayMinimum(_minimumFreeAttack)}/{DisplayMinimum(_minimumLongestAttack)}/{_slotSamples} " +
               $"G={_safeCode}:E{Bool(_enabled)}S{Bool(_safeFrame)}P{Bool(_proxyInitialized)} Q={_diagnosticGeneration}/{_proxyResetRequests}/{_proxyResetCompletions}/{Bool(_reinitializeRequested)} E={ErrorCode()}";
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
        if (_virtualSuppressionFrames > 0 || !_enabled || _fatal || HostUiCapturesKeyboard())
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
        !HostUiCapturesKeyboard() &&
        Game.Available && Game.InAlucardMode() && !Game.IsLoading &&
        !Game.MenuOpen && !Game.MapOpen && DisplayModeHooks.IsStage &&
        memory.ReadU32(CutsceneControlAddress) == 0 &&
        !IsSpecialTransition(memory.ReadU32(SpecialTransitionAddress));

    private void SetVirtualKeyboard(bool enabled)
    {
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

    private static bool IsSpecialTransition(uint value) =>
        value is >= 2 and <= 6 || (value & 0x88000000) != 0;

    private static bool HostUiCapturesKeyboard() => ImGui.GetIO().WantCaptureKeyboard;

    private void BeginTransition()
    {
        if (!_roomKnown) return;
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
        (_collisionFunction != ExpectedCollisionFunction ? $"A{_collisionFunction:X8}" : $"C{_lastRejectedCorrection}") : "0";

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
