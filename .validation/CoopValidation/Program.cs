using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Reflection;
using CoopFeasibilityMod;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace CoopValidation;

internal static partial class Program
{
    private const uint DispatchProbeAddress = 0x801FF000;
    private const uint DispatchProbeReturn = 0xC0111D3D;
    private static bool _dispatchProbeThrows;
    private static bool _dispatchArgumentsObserved;

    private static readonly string[] ManifestProperties =
    [
        "author", "dependencies", "description", "id", "name", "version"
    ];

    private static readonly string[] ExpectedHooks =
    [
        "PostHook:cen:GetDistanceToPlayerX_cen",
        "PostHook:cen:GetDistanceToPlayerY_cen",
        "PostHook:cen:GetSideToPlayer_cen",
        "PostHook:dra:RenderEntities",
        "PostHook:dra:RunMainEngine",
        "PostHook:dra:UpdatePlayerEntities",
        "PostHook:main:DrawOTag",
        "PostHook:sel:ApplySaveData_sel",
        "PreHook:dra:RenderEntities",
        "PreHook:dra:RunMainEngine"
    ];

    private static readonly Dictionary<string, string> ExpectedConstants = new(StringComparer.Ordinal)
    {
        ["ExpectedCollisionFunction"] = "800EF45C",
        ["AssignAttackerIdSlot"] = "8003C804",
        ["ExpectedAssignAttackerId"] = "80118894",
        ["ExpectedDealDamage"] = "800FF128",
        ["ExpectedEnemyDefinitions"] = "800A8900",
        ["ExpectedGetEquipProperties"] = "800FE728",
        ["ExpectedCalcAttack"] = "800F4D38"
    };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3)
                throw new InvalidOperationException("Usage: CoopValidation <coop-root> <surface-label> <SymphonyRecomp-root>");

            string root = Path.GetFullPath(args[0]);
            string label = args[1];
            string hostRoot = Path.GetFullPath(args[2]);
            string manifestText = File.ReadAllText(Path.Combine(root, "mod.json"));
            string sourceRoot = Path.Combine(root, "source");
            string[] sourcePaths = DiscoverRuntimeSources(root);
            if (sourcePaths.Length == 0) throw new InvalidOperationException("No mod source files found.");
            string[] sourcesOutsideProductionRoot = sourcePaths
                .Where(path => !Path.GetRelativePath(sourceRoot, path).Split(Path.DirectorySeparatorChar)
                    .All(part => part != ".."))
                .ToArray();
            if (sourcesOutsideProductionRoot.Length != 0)
                throw new InvalidOperationException("Runtime-loaded C# must live under source/; validation tooling belongs under a dot-prefixed directory.");

            string sourceText = string.Join("\n", sourcePaths.Select(File.ReadAllText));
            string version = ValidateManifest(manifestText);
            ValidateSource(sourceText, version);
            ValidateHostContracts(hostRoot);
            ValidateFileSelectPostHook();
            RunNegativeContractTests(manifestText, sourceText, version, hostRoot);
            ValidateGeneratedDiagnostics();
            ValidateGuardedDirectDispatch();
            ValidateScratchDirectDispatch();
            ValidateGuestScratchRestore();

            _ = typeof(ImGuiNET.ImGui).Assembly;
            _ = typeof(Silk.NET.Input.Key).Assembly;
            _ = typeof(Sotn.Game).Assembly;
            _ = typeof(Recompiled.RoomLayerLoadEvent).Assembly;

            var sources = sourcePaths
                .Select(path => (Path: path, Text: File.ReadAllText(path)))
                .ToList();
            byte[]? assembly = ModCompiler.Compile($"coop-validation-{label}", sources);
            if (assembly is null) throw new InvalidOperationException("Runtime ModCompiler rejected the mod source.");

            Console.WriteLine($"[CoopValidation] {label}: manifest, hooks, safety contracts, allocation-free CpuContext guard, static compile, and runtime ModCompiler passed ({assembly.Length} bytes). Version={version}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CoopValidation] failed: {ex.Message}");
            return 1;
        }
    }

    private static string ValidateManifest(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("mod.json must contain one object.");

        string[] actualProperties = root.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actualProperties.SequenceEqual(ManifestProperties))
            throw new InvalidOperationException($"mod.json properties must be exactly: {string.Join(", ", ManifestProperties)}.");

        string id = RequiredString(root, "id");
        if (!ModIdRegex().IsMatch(id))
            throw new InvalidOperationException("mod.json id must use lowercase ASCII words separated by single hyphens.");

        _ = RequiredString(root, "name");
        string version = RequiredString(root, "version");
        if (!SemVerRegex().IsMatch(version))
            throw new InvalidOperationException("mod.json version must be semantic version syntax.");
        _ = RequiredString(root, "author");
        _ = RequiredString(root, "description");

        JsonElement dependencies = root.GetProperty("dependencies");
        if (dependencies.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("mod.json dependencies must be an array.");
        var seenDependencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement dependency in dependencies.EnumerateArray())
        {
            if (dependency.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(dependency.GetString()))
                throw new InvalidOperationException("mod.json dependencies must contain nonempty strings.");
            if (!seenDependencies.Add(dependency.GetString()!))
                throw new InvalidOperationException("mod.json dependencies must be unique.");
        }

        return version;
    }

    private static string[] DiscoverRuntimeSources(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path)
                .Split(Path.DirectorySeparatorChar)
                .Any(part => part is "obj" or "bin" || part.StartsWith(".", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void ValidateSource(string source, string manifestVersion)
    {
        Match versionMatch = SourceVersionRegex().Match(source);
        if (!versionMatch.Success)
            throw new InvalidOperationException("Source must declare one private const string Version.");
        if (!string.Equals(versionMatch.Groups[1].Value, manifestVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Source Version must match mod.json version.");
        if (!source.Contains("P2D4 VER={Version}", StringComparison.Ordinal))
            throw new InvalidOperationException("P2D4 must report the source Version value.");
        if (!source.Contains("return P2D4Report.Parse(report).CanonicalLine;", StringComparison.Ordinal))
            throw new InvalidOperationException("BuildReport must validate and canonicalize P2D4 output.");
        string[] inputPreferenceContract =
        [
            "public const string Key = \"mods.coop-feasibility.virtualPlayer2Keyboard\";",
            "store.GetBool(Key, true)",
            "store.SetBool(Key, enabled)",
            "store.Save();"
        ];
        if (inputPreferenceContract.Any(fragment => !source.Contains(fragment, StringComparison.Ordinal)))
            throw new InvalidOperationException("Virtual Player 2 input preference must remain mod-scoped, persisted, and safe-defaulted.");

        string[] hooks = HookRegex().Matches(source)
            .Select(match => $"{match.Groups[1].Value}:{match.Groups[2].Value}:{match.Groups[3].Value}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!hooks.SequenceEqual(ExpectedHooks))
            throw new InvalidOperationException($"Hook contract mismatch. Expected exactly: {string.Join(", ", ExpectedHooks)}.");

        foreach ((string name, string expectedValue) in ExpectedConstants)
        {
            Match constant = Regex.Match(source,
                $@"private\s+const\s+uint\s+{Regex.Escape(name)}\s*=\s*0x([0-9A-Fa-f]+)\s*;");
            if (!constant.Success || !string.Equals(constant.Groups[1].Value, expectedValue, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Compatibility constant {name} must remain 0x{expectedValue}.");
        }

        if (Regex.IsMatch(source, @"GameApi\s*\.\s*DealDamage\s*\("))
            throw new InvalidOperationException("Direct GameApi.DealDamage calls are prohibited.");
        if (Regex.IsMatch(source, @"GameApi\s*\.\s*(?:Call|CallApi)\s*\([^\r\n]*(?:DealDamageAddr|ExpectedDealDamage)"))
            throw new InvalidOperationException("Indirect DealDamage dispatch is prohibited.");
        if (Regex.IsMatch(source, @"GameApi\s*\.\s*(?:Call|CallApi)\s*\("))
            throw new InvalidOperationException("M4 guest paths must use guarded direct dispatch, not allocating GameApi calls.");
        string[] dropContracts =
        [
            "public const int FirstSlot = 160;", "public const int SlotCount = 32;",
            "public const ushort PrizeEntityId = 3;", "public const ushort EquipmentEntityId = 10;",
            "public const uint PrizeUpdate = 0x801C9220;",
            "public const uint EquipmentUpdate = 0x801C9C34;",
            "Native EXP has no validated read contract yet"
        ];
        if (dropContracts.Any(fragment => !source.Contains(fragment, StringComparison.Ordinal)))
            throw new InvalidOperationException("NO0 native drop observation contract changed.");
        int trackerStart = source.IndexOf("public sealed class NativeDropObservationTracker", StringComparison.Ordinal);
        if (trackerStart < 0 || source[trackerStart..].Contains("WriteU", StringComparison.Ordinal))
            throw new InvalidOperationException("Native drop tracker must remain read-only.");

        int collisionStart = source.IndexOf("private bool TryCollision(", StringComparison.Ordinal);
        int collisionEnd = collisionStart < 0 ? -1 : source.IndexOf("private int CurrentHeadOffset", collisionStart,
            StringComparison.Ordinal);
        if (collisionStart < 0 || collisionEnd < 0)
            throw new InvalidOperationException("Collision guest-call adapter is missing.");
        string collision = source[collisionStart..collisionEnd];
        if (collision.Contains(".Snapshot()", StringComparison.Ordinal) ||
            Regex.IsMatch(collision, @"GameApi\s*\.\s*(?:Call|CallApi)\s*\(") ||
            !collision.Contains("stackalloc uint[CpuContextRegisterGuard.StateWordCount]", StringComparison.Ordinal) ||
            !collision.Contains("CpuContextDirectCall.Invoke(context, memory, _collisionFunction", StringComparison.Ordinal) ||
            !collision.Contains("GuestScratchRestore.RestoreAll(memory, scratchStart, saved[..savedCount]);", StringComparison.Ordinal) ||
            !collision.Contains("contextGuard.Restore();", StringComparison.Ordinal) ||
            Regex.Matches(collision, Regex.Escape("_collisionRestoreFailures++;")).Count != 1)
            throw new InvalidOperationException("Collision adapter must use direct dispatch and one best-effort restore failure path.");

        int publicationStart = source.IndexOf("private sealed class NativeAttackPublicationAdapter", StringComparison.Ordinal);
        int publicationEnd = publicationStart < 0 ? -1 : source.IndexOf("private int _slotSamples", publicationStart,
            StringComparison.Ordinal);
        if (publicationStart < 0 || publicationEnd < 0)
            throw new InvalidOperationException("Native attack publication adapter is missing.");
        string publication = source[publicationStart..publicationEnd];
        if (!publication.Contains("uint function = _memory.ReadU32(AssignAttackerIdSlot);", StringComparison.Ordinal) ||
            !publication.Contains("CpuContextGuardedDirectCall.Invoke(_context, _memory, function, _entity, 0, 0, 0)", StringComparison.Ordinal))
            throw new InvalidOperationException("Native attack publication must use guarded direct dispatch.");
        string[] markerCensusPaths =
        [
            "_attackStatus = \"ARMED\";\n            CensusOwnedAttackMarkers(memory);",
            "ClearOwnedAttackMetadata(authorization);\n        CensusOwnedAttackMarkers(memory);",
            "_attackStatus = \"QUARANTINE-CLEAN\";"
        ];
        if (markerCensusPaths.Any(fragment => !source.Contains(fragment, StringComparison.Ordinal)) ||
            Regex.Matches(source, Regex.Escape("CensusOwnedAttackMarkers(memory);")).Count < 6)
            throw new InvalidOperationException("Attack marker metrics must be refreshed in publication and cleanup updates.");

        int profileStart = source.IndexOf("private bool TryExtractAttackProfile(", StringComparison.Ordinal);
        int profileEnd = profileStart < 0 ? -1 : source.IndexOf("private void TrySpawnOwnedAttack(", profileStart,
            StringComparison.Ordinal);
        if (profileStart < 0 || profileEnd < 0)
            throw new InvalidOperationException("Attack profile extraction adapter is missing.");
        string profile = source[profileStart..profileEnd];
        if (profile.Contains(".Snapshot()", StringComparison.Ordinal) ||
            !profile.Contains("CpuContextScratchDirectCall.TryInvoke(context, memory, ExpectedCalcAttack", StringComparison.Ordinal) ||
            !profile.Contains("GuestScratchRestore.RestoreAll(memory, scratchStart, saved[..savedCount]);", StringComparison.Ordinal) ||
            Regex.Matches(profile, Regex.Escape("_equipmentRestoreFailures++;")).Count != 1)
            throw new InvalidOperationException("Attack profile extraction must use guarded direct dispatch and shared scratch restoration.");

        int resetStart = source.IndexOf("private bool TryResetDiagnostic()", StringComparison.Ordinal);
        int resetEnd = resetStart < 0 ? -1 : source.IndexOf("private void Fail(", resetStart,
            StringComparison.Ordinal);
        if (resetStart < 0 || resetEnd < 0)
            throw new InvalidOperationException("Diagnostic reset adapter is missing.");
        string reset = source[resetStart..resetEnd];
        int preparation = reset.IndexOf("DiagnosticResetPreparationPolicy.TryPrepare", StringComparison.Ordinal);
        int attackPreflight = reset.IndexOf("AttackResetPreflightOutcome attackPreflight = PreflightAttackReset(preparation.Publication);", StringComparison.Ordinal);
        int refusal = reset.IndexOf("if (!AttackResetPreflight.AllowsReset(attackPreflight))", StringComparison.Ordinal);
        int sessionReset = reset.IndexOf("DiagnosticResetPreparationPolicy.CommitPreparedReducers", StringComparison.Ordinal);
        int generationReset = reset.IndexOf("_diagnosticGeneration = preparation.NextDiagnosticGeneration;", StringComparison.Ordinal);
        int fatalReset = reset.IndexOf("_fatal = false;", StringComparison.Ordinal);
        if (preparation < 0 || attackPreflight < preparation || refusal < attackPreflight || sessionReset < refusal ||
            generationReset < 0 || fatalReset < 0 || sessionReset > generationReset || sessionReset > fatalReset)
            throw new InvalidOperationException("Attack and movement reset preflight must complete before outer diagnostics mutate.");
        if (!reset.Contains("DiagnosticResetPreparationPolicy.CommitPreparedReducers", StringComparison.Ordinal) ||
            reset.Contains("_diagnosticGeneration++", StringComparison.Ordinal) ||
            reset.Contains("_locomotionState.DiagnosticReset()", StringComparison.Ordinal) ||
            reset.Contains("_stance.Initialize(false)", StringComparison.Ordinal) ||
            reset.Contains("ClearJumpForgiveness();", StringComparison.Ordinal))
            throw new InvalidOperationException("Diagnostic reset must use prepared nonthrowing reducer commits.");

        string[] reconstructionEvidence =
        [
            "ManagedReconstructionCommitOrchestration.Run(ref commit)",
            "PrepareReconstructionCompletion(_continuation, ManagedMovementReconstructionResult.Selected)",
            "PrepareAllProxyPoses(_memory)",
            "CanCommitReconstructionCompletion(_session)",
            "ReconstructionRetryPolicy.SafeUpdate(_reconstructionRetry)",
            "if (retry.Command != ReconstructionRetryCommand.Retry)",
            "SuspendContactScan(\"RETRY_WAIT\");",
        ];
        if (reconstructionEvidence.Any(fragment => !source.Contains(fragment, StringComparison.Ordinal)) ||
            source.Contains("private void InitializeProxyAt(", StringComparison.Ordinal))
            throw new InvalidOperationException("Live reconstruction must use the shared prepare/commit seam.");
        int retryGate = source.IndexOf("ReconstructionRetryPolicy.SafeUpdate(_reconstructionRetry)",
            StringComparison.Ordinal);
        int roomObservation = source.IndexOf("_movementSession.ObserveSafeRoom(observedRoom.ManagedKey())",
            StringComparison.Ordinal);
        int reconstructionCall = source.IndexOf("ReconstructionRunResult reconstruction = TryReconstructProxy(",
            StringComparison.Ordinal);
        if (retryGate < 0 || roomObservation <= retryGate || reconstructionCall <= roomObservation)
            throw new InvalidOperationException("Reconstruction retry suppression must precede session attempts and native probes.");

        ValidateOrdering(source, "private void OnPlayerLoaded(", "private void OnSaveLoaded(",
            ["_movementSession.PlayerReloaded();", "_locomotionState.Invalidate();", "ResetManagedHealth();"]);
        if (!source.Contains("Event.AddListener<SaveLoadedEvent>(OnSaveLoaded);", StringComparison.Ordinal) ||
            source.Contains("_nativeLoadBootstrap.Arm(beforeReload", StringComparison.Ordinal) ||
            !source.Contains("private void OnSaveLoaded(SaveLoadedEvent e)", StringComparison.Ordinal) ||
            !source.Contains("[PostHook(\"sel\", \"ApplySaveData_sel\")]", StringComparison.Ordinal) ||
            !source.Contains("private static void AfterFileSelectApplySaveData(CpuContext context, IMemory memory)", StringComparison.Ordinal) ||
            !source.Contains("if (context.V0 != 0) return;", StringComparison.Ordinal) ||
            !source.Contains("ArmNativeLoadBootstrap(MovementTransitionTraceSource.SaveLoaded, \"armed:sel-post\")", StringComparison.Ordinal) ||
            !source.Contains("ArmNativeLoadBootstrap(MovementTransitionTraceSource.SaveLoaded, \"event-supplementary\")", StringComparison.Ordinal) ||
            source.Contains("e.Block", StringComparison.Ordinal))
            throw new InvalidOperationException("Native load bootstrap must use the direct file-select post-hook with SaveLoadedEvent supplementary only.");
        if (!source.Contains("IsNativeLoadQualifyingPostUpdate(memory)", StringComparison.Ordinal) ||
            !source.Contains("_nativeLoadBootstrap.Stable", StringComparison.Ordinal) ||
            !source.Contains("memory.ReadU32(GameStepAddress) == (uint)PlayStep.Default", StringComparison.Ordinal) ||
            !source.Contains("memory.ReadU32(EngineStepAddress) == 1", StringComparison.Ordinal))
            throw new InvalidOperationException("Native load bootstrap must gate baseline and reconstruction on stable post-update Play samples.");
        ValidateOrdering(source, "private void OnRoomLayerLoaded(", "[PostHook(\"dra\", \"RunMainEngine\")]",
            ["BeginTransition();", "_movementSession.RoomLayerLoaded();"]);
        if (!source.Contains("if (_nativeLoadBootstrap.Armed)", StringComparison.Ordinal) ||
            !source.Contains("MovementTransitionTraceSource.BootstrapLayer", StringComparison.Ordinal))
            throw new InvalidOperationException("Native load bootstrap must suppress layer accounting in the adapter.");
        ValidateOrdering(source, "private void Fail(", "private string BuildReport()",
            ["_movementSession.Fatal();", "_locomotionState.Invalidate();", "CancelOwnedAttack(\"FATAL\");"]);
        ValidateOrdering(source, "public void OnUnload()", "public string CaptureAutomationDiagnostics(",
            ["_movementSession.Unload();", "_locomotionState.Invalidate();", "DisarmAwareness(\"UNLOAD\")"]);
    }

    private static void ValidateOrdering(string source, string startMarker, string endMarker,
        IReadOnlyList<string> fragments)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = start < 0 ? -1 : source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (start < 0 || end < 0) throw new InvalidOperationException($"Lifecycle adapter {startMarker} is missing.");
        string section = source[start..end];
        int prior = -1;
        foreach (string fragment in fragments)
        {
            int current = section.IndexOf(fragment, StringComparison.Ordinal);
            if (current < 0 || current <= prior)
                throw new InvalidOperationException($"Lifecycle adapter ordering failed at {fragment}.");
            prior = current;
        }
    }

    private static void ValidateGeneratedDiagnostics()
    {
        var mod = new CoopFeasibility();
        string json = mod.CaptureAutomationDiagnostics(0);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (!string.Equals(root.GetProperty("schema").GetString(), P2D4DiagnosticsEnvelope.Schema,
                StringComparison.Ordinal))
            throw new InvalidOperationException("Generated diagnostics schema mismatch.");
        string legacy = root.GetProperty("legacy").GetString()
            ?? throw new InvalidOperationException("Generated diagnostics omitted the legacy line.");
        _ = P2D4Report.Parse(legacy);
    }

    private static void ValidateGuardedDirectDispatch()
    {
        var overlay = new DispatchProbeOverlay();
        Dispatcher.Register(overlay.Name, overlay);
        Dispatcher.Load(overlay.Name);

        var context = new CpuContext();
        var memory = new FaultMemory(64);
        SeedContext(context);
        Span<uint> expected = stackalloc uint[CpuContextRegisterGuard.StateWordCount];
        CaptureContext(context, expected);

        _dispatchProbeThrows = true;
        _dispatchArgumentsObserved = false;
        bool threw = false;
        try
        {
            RunDirectDispatchCycle(context, memory);
        }
        catch (GuestCallProbeException)
        {
            threw = true;
        }
        finally
        {
            _dispatchProbeThrows = false;
        }

        if (!threw) throw new InvalidOperationException("CpuContext guard probe did not inject its guest-call fault.");
        if (!_dispatchArgumentsObserved) throw new InvalidOperationException("Direct dispatch did not publish A0-A3.");
        AssertContext(context, expected, "throwing direct dispatch");

        uint result = RunDirectDispatchCycle(context, memory);
        if (result != DispatchProbeReturn) throw new InvalidOperationException("Direct dispatch return capture failed.");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            if (RunDirectDispatchCycle(context, memory) != DispatchProbeReturn)
                throw new InvalidOperationException("Direct dispatch warmed return changed.");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0)
            throw new InvalidOperationException($"Direct collision dispatch allocated {allocated} warmed bytes.");
        AssertContext(context, expected, "warmed direct dispatch");
        Console.WriteLine($"[CoopValidation] Guarded publication dispatch evidence: words={CpuContextRegisterGuard.StateWordCount}, registered-throw-restored=true, warmed-calls=10000, bytes={allocated}.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint RunDirectDispatchCycle(CpuContext context, IMemory memory)
    {
        return CpuContextGuardedDirectCall.Invoke(context, memory, DispatchProbeAddress, 11, 22, 33, 44);
    }

    private static void SeedContext(CpuContext context)
    {
        for (int i = 0; i < 32; i++) context[i] = 0x10203040u + (uint)i * 0x01010101u;
        context.HI = 0x89ABCDEF;
        context.LO = 0x76543210;
        context.SR = 0x13579BDF;
        context.Cause = 0x2468ACE0;
        context.EPC = 0x80012340;
        context.BadVAddr = 0xDEADBEEF;
        context.PRId = 0x00000002;
    }

    private static void MutatingDispatchProbe(CpuContext context, IMemory memory)
    {
        _dispatchArgumentsObserved = context.A0 == 11 && context.A1 == 22 && context.A2 == 33 && context.A3 == 44;
        for (int i = 0; i < 32; i++) context[i] = 0xFFFFFFFFu - (uint)i;
        context.V0 = DispatchProbeReturn;
        context.HI = 1;
        context.LO = 2;
        context.SR = 3;
        context.Cause = 4;
        context.EPC = 5;
        context.BadVAddr = 6;
        context.PRId = 7;
        if (_dispatchProbeThrows) throw new GuestCallProbeException();
    }

    private static void ValidateScratchDirectDispatch()
    {
        const int length = 0x80 + 0x10;
        const uint start = 8;
        var context = new CpuContext();
        var memory = new FaultMemory(length + 16);
        for (int i = 0; i < length; i++) memory.Bytes[checked((int)start) + i] = (byte)(1 + i % 251);
        SeedContext(context);
        Span<uint> expectedContext = stackalloc uint[CpuContextRegisterGuard.StateWordCount];
        CaptureContext(context, expectedContext);
        byte[] expectedScratch = (byte[])memory.Bytes.Clone();

        _dispatchProbeThrows = true;
        bool callSucceeded = RunScratchDispatchCycle(context, memory, start);
        _dispatchProbeThrows = false;
        if (callSucceeded) throw new InvalidOperationException("Scratch direct dispatch did not report its guest throw.");
        AssertContext(context, expectedContext, "throwing extraction dispatch");
        if (!memory.Bytes.AsSpan().SequenceEqual(expectedScratch))
            throw new InvalidOperationException("Scratch direct dispatch did not restore bytes after guest throw.");

        if (!RunScratchDispatchCycle(context, memory, start))
            throw new InvalidOperationException("Scratch direct dispatch warmup failed.");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            if (!RunScratchDispatchCycle(context, memory, start))
                throw new InvalidOperationException("Scratch direct dispatch warmed call failed.");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0)
            throw new InvalidOperationException($"Scratch direct dispatch allocated {allocated} warmed bytes.");
        AssertContext(context, expectedContext, "warmed extraction dispatch");
        if (!memory.Bytes.AsSpan().SequenceEqual(expectedScratch))
            throw new InvalidOperationException("Scratch direct dispatch changed warmed scratch bytes.");
        Console.WriteLine($"[CoopValidation] Extraction dispatch evidence: scratch={length}, guest-throw-restored=true, warmed-calls=10000, bytes={allocated}.");
    }

    private static int _fileSelectOriginalCalls;
    private static int _fileSelectPostCalls;
    private static bool _fileSelectSuccessResultObserved;

    private static void ValidateFileSelectPostHook()
    {
        Dispatcher.Register("sel", new FileSelectProbeOverlay());
        SymbolRegistry.Build();
        MethodInfo target = SymbolRegistry.Resolve("sel", "ApplySaveData_sel", 0)
            ?? throw new InvalidOperationException("HookManager could not resolve sel/ApplySaveData_sel.");
        MethodInfo post = typeof(Program).GetMethod(nameof(AfterFileSelectApplyProbe),
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("File-select post-hook probe is missing.");
        var mod = new ModInfo { Id = "coop-validation-file-select" };
        if (!HookManager.AddPost(mod, target, post))
            throw new InvalidOperationException("HookManager rejected the file-select post-hook signature.");

        try
        {
            HookManager.Commit();
            _fileSelectOriginalCalls = 0;
            _fileSelectPostCalls = 0;
            _fileSelectSuccessResultObserved = false;
            FileSelectProbeOverlay.ApplySaveData_sel(new CpuContext(), new FaultMemory(1));
            if (_fileSelectOriginalCalls != 1 || _fileSelectPostCalls != 1 || !_fileSelectSuccessResultObserved)
                throw new InvalidOperationException("File-select post-hook did not run after the successful original apply function.");
        }
        finally
        {
            HookManager.RemoveMod(mod);
        }

        Console.WriteLine("[CoopValidation] File-select post-hook evidence: sel/ApplySaveData_sel resolved, successful-original-then-post=true.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FileSelectApplyProbe(CpuContext context, IMemory memory)
    {
        _fileSelectOriginalCalls++;
        context.V0 = 0;
    }

    private static void AfterFileSelectApplyProbe(CpuContext context, IMemory memory)
    {
        _fileSelectPostCalls++;
        _fileSelectSuccessResultObserved = context.V0 == 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool RunScratchDispatchCycle(CpuContext context, IMemory memory, uint scratchStart)
    {
        Span<byte> saved = stackalloc byte[0x80 + 0x10];
        bool succeeded = CpuContextScratchDirectCall.TryInvoke(context, memory, DispatchProbeAddress,
            11, 22, 0x8001FF00, scratchStart, saved, out uint result, out int savedCount,
            out Exception? restoreFailure);
        if (restoreFailure is not null) throw restoreFailure;
        if (savedCount != saved.Length) throw new InvalidOperationException("Extraction scratch save was incomplete.");
        if (succeeded && result != DispatchProbeReturn)
            throw new InvalidOperationException("Extraction dispatch return capture failed.");
        return succeeded;
    }

    private static void ValidateGuestScratchRestore()
    {
        ValidateGuestScratchRestoreLength("collision", 0xD0 + 0x10 + 0x24);
        ValidateGuestScratchRestoreLength("equipment", 0x80 + 0x10);
    }

    private static void ValidateGuestScratchRestoreLength(string label, int length)
    {
        const uint start = 8;
        Span<byte> saved = stackalloc byte[length];
        for (int i = 0; i < length; i++) saved[i] = (byte)(1 + i % 251);

        for (int fault = 0; fault < length; fault++)
        {
            var memory = new FaultMemory(length + 16) { WriteFaultOrdinal = fault };
            Exception expected = memory.WriteFailure;
            Exception actual = CaptureScratchFailure(memory, start, saved);
            if (!ReferenceEquals(actual, expected) || memory.WriteAttempts != length || memory.ReadAttempts != length)
                throw new InvalidOperationException($"Scratch write fault {fault} did not retain first failure and exhaust attempts.");
            for (int i = fault + 1; i < length; i++)
                if (memory.Bytes[checked((int)start) + i] != saved[i])
                    throw new InvalidOperationException($"Scratch write fault {fault} stranded later byte {i}.");
        }

        for (int fault = 0; fault < length; fault++)
        {
            var memory = new FaultMemory(length + 16) { ReadFaultOrdinal = fault };
            Exception expected = memory.ReadFailure;
            Exception actual = CaptureScratchFailure(memory, start, saved);
            if (!ReferenceEquals(actual, expected) || memory.WriteAttempts != length || memory.ReadAttempts != length)
                throw new InvalidOperationException($"Scratch read fault {fault} did not retain first failure and exhaust attempts.");
            for (int i = 0; i < length; i++)
                if (memory.Bytes[checked((int)start) + i] != saved[i])
                    throw new InvalidOperationException($"Scratch read fault {fault} left byte {i} unrestored.");
        }

        for (int mismatch = 0; mismatch < length; mismatch++)
        {
            var memory = new FaultMemory(length + 16) { DroppedWriteOrdinal = mismatch };
            Exception actual = CaptureScratchFailure(memory, start, saved);
            if (actual is not InvalidOperationException || memory.WriteAttempts != length || memory.ReadAttempts != length)
                throw new InvalidOperationException($"Scratch mismatch {mismatch} was not detected after exhaustive verification.");
            for (int i = mismatch + 1; i < length; i++)
                if (memory.Bytes[checked((int)start) + i] != saved[i])
                    throw new InvalidOperationException($"Scratch mismatch {mismatch} stranded later byte {i}.");
        }

        ValidateContextRestoreAfterScratchFailure(start, saved);
        Console.WriteLine($"[CoopValidation] Scratch restore evidence: path={label}, bytes={length}, every-write-fault=true, every-read-fault=true, every-mismatch=true, context-finally=true.");
    }

    private static Exception CaptureScratchFailure(FaultMemory memory, uint start, ReadOnlySpan<byte> saved)
    {
        try
        {
            GuestScratchRestore.RestoreAll(memory, start, saved);
        }
        catch (Exception ex)
        {
            return ex;
        }
        throw new InvalidOperationException("Scratch restore fault probe unexpectedly succeeded.");
    }

    private static void ValidateContextRestoreAfterScratchFailure(uint start, ReadOnlySpan<byte> saved)
    {
        var context = new CpuContext();
        SeedContext(context);
        Span<uint> expected = stackalloc uint[CpuContextRegisterGuard.StateWordCount];
        CaptureContext(context, expected);
        var memory = new FaultMemory(saved.Length + 16) { WriteFaultOrdinal = saved.Length / 2 };
        try
        {
            Span<uint> storage = stackalloc uint[CpuContextRegisterGuard.StateWordCount];
            var guard = new CpuContextRegisterGuard(context, storage);
            try
            {
                MutatingDispatchProbe(context, memory);
                GuestScratchRestore.RestoreAll(memory, start, saved);
            }
            finally
            {
                guard.Restore();
            }
        }
        catch (Exception ex) when (ReferenceEquals(ex, memory.WriteFailure))
        {
        }
        AssertContext(context, expected, "scratch restore failure");
    }

    private static void CaptureContext(CpuContext context, Span<uint> destination)
    {
        for (int i = 0; i < 32; i++) destination[i] = context[i];
        destination[32] = context.HI;
        destination[33] = context.LO;
        destination[34] = context.SR;
        destination[35] = context.Cause;
        destination[36] = context.EPC;
        destination[37] = context.BadVAddr;
        destination[38] = context.PRId;
    }

    private static void AssertContext(CpuContext context, ReadOnlySpan<uint> expected, string phase)
    {
        Span<uint> actual = stackalloc uint[CpuContextRegisterGuard.StateWordCount];
        CaptureContext(context, actual);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException($"CpuContext guard did not completely restore after {phase}.");
    }

    private sealed class GuestCallProbeException : Exception;

    private sealed class DispatchProbeOverlay : IOverlay
    {
        private static readonly IReadOnlyDictionary<uint, Action<CpuContext, IMemory>> DispatchFunctions =
            new Dictionary<uint, Action<CpuContext, IMemory>> { [DispatchProbeAddress] = MutatingDispatchProbe };

        public string Name => "coop-validation-direct-dispatch";
        public IReadOnlyDictionary<uint, Action<CpuContext, IMemory>> Functions => DispatchFunctions;
    }

    private sealed class FileSelectProbeOverlay : IOverlay
    {
        private static readonly IReadOnlyDictionary<uint, Action<CpuContext, IMemory>> FileSelectFunctions =
            new Dictionary<uint, Action<CpuContext, IMemory>> { [0x801FF004] = ApplySaveData_sel };

        public string Name => "sel";
        public IReadOnlyDictionary<uint, Action<CpuContext, IMemory>> Functions => FileSelectFunctions;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ApplySaveData_sel(CpuContext context, IMemory memory)
        {
            FileSelectApplyProbe(context, memory);
        }
    }

    private sealed class FaultMemory : IMemory
    {
        internal readonly byte[] Bytes;
        internal readonly Exception WriteFailure = new FaultMemoryException("write");
        internal readonly Exception ReadFailure = new FaultMemoryException("read");
        internal int WriteFaultOrdinal = -1;
        internal int ReadFaultOrdinal = -1;
        internal int DroppedWriteOrdinal = -1;
        internal int WriteAttempts;
        internal int ReadAttempts;

        internal FaultMemory(int length) => Bytes = new byte[length];

        public byte ReadU8(uint address)
        {
            int ordinal = ReadAttempts++;
            if (ordinal == ReadFaultOrdinal) throw ReadFailure;
            return Bytes[checked((int)address)];
        }

        public void WriteU8(uint address, byte value)
        {
            int ordinal = WriteAttempts++;
            if (ordinal == WriteFaultOrdinal) throw WriteFailure;
            if (ordinal != DroppedWriteOrdinal) Bytes[checked((int)address)] = value;
        }

        public ushort ReadU16(uint address) => throw new NotSupportedException();
        public uint ReadU32(uint address) => throw new NotSupportedException();
        public void WriteU16(uint address, ushort value) => throw new NotSupportedException();
        public void WriteU32(uint address, uint value) => throw new NotSupportedException();
        public uint ReadWordLeft(uint current, uint address) => throw new NotSupportedException();
        public uint ReadWordRight(uint current, uint address) => throw new NotSupportedException();
        public void WriteWordLeft(uint address, uint value) => throw new NotSupportedException();
        public void WriteWordRight(uint address, uint value) => throw new NotSupportedException();
        public void LoadBytes(uint address, byte[] data) => throw new NotSupportedException();
        public void ZeroRange(uint address, uint length) => throw new NotSupportedException();
        public void SetCd(RecompOne.Runtime.Cdrom.CdController cd) => throw new NotSupportedException();
    }

    private sealed class FaultMemoryException(string operation) : Exception(operation);

    private static void ValidateHostContracts(string hostRoot)
    {
        string configText = File.ReadAllText(Path.Combine(hostRoot, "config", "sotn.json"));
        using JsonDocument config = JsonDocument.Parse(configText);
        JsonElement configRoot = config.RootElement;
        if (!string.Equals(configRoot.GetProperty("funcMap").GetString(), "funcmaps/main.json", StringComparison.Ordinal))
            throw new InvalidOperationException("Host main function-map contract is missing.");

        var overlays = configRoot.GetProperty("overlays").EnumerateArray()
            .ToDictionary(
                overlay => RequiredString(overlay, "name"),
                overlay => RequiredString(overlay, "funcMap"),
                StringComparer.Ordinal);
        if (!overlays.TryGetValue("dra", out string? draMap) || !string.Equals(draMap, "funcmaps/dra.json", StringComparison.Ordinal))
            throw new InvalidOperationException("Host dra overlay/function-map contract is missing.");
        if (!overlays.TryGetValue("sel", out string? selMap) || !string.Equals(selMap, "funcmaps/stsel.json", StringComparison.Ordinal))
            throw new InvalidOperationException("Host sel overlay/function-map contract is missing.");
        if (!overlays.TryGetValue("cen", out string? cenMap) || !string.Equals(cenMap, "funcmaps/stcen.json", StringComparison.Ordinal))
            throw new InvalidOperationException("Host cen overlay/function-map contract is missing.");

        ValidateFunctionMap(File.ReadAllText(Path.Combine(hostRoot, "config", "funcmaps", "main.json")),
            "main", ["DrawOTag"]);
        ValidateFunctionMap(File.ReadAllText(Path.Combine(hostRoot, "config", "funcmaps", "dra.json")),
            "dra", ["RenderEntities", "RunMainEngine", "UpdatePlayerEntities"]);
        ValidateFunctionMap(File.ReadAllText(Path.Combine(hostRoot, "config", "funcmaps", "stsel.json")),
            "sel", ["ApplySaveData"]);
        ValidateFunctionMap(File.ReadAllText(Path.Combine(hostRoot, "config", "funcmaps", "stcen.json")),
            "cen", ["GetDistanceToPlayerX", "GetDistanceToPlayerY", "GetSideToPlayer"]);

        ValidateEmittedHooks(hostRoot, configRoot);
    }

    private static void ValidateEmittedHooks(string hostRoot, JsonElement configRoot)
    {
        string recompilerPath = Path.Combine(hostRoot, "RecompOne", "RecompOne.Recompiler", "CodeGen", "OverlayWriter.cs");
        string recompiler = File.ReadAllText(recompilerPath);
        string[] namingContract =
        [
            "crossOverlayDups.Contains(func.Name) && !string.IsNullOrEmpty(func.OverlayName)",
            "$\"{func.Name}_{SafeIdentifier(func.OverlayName)}\"",
            "$\"{func.EmittedName}_{func.Start:X8}\""
        ];
        if (namingContract.Any(fragment => !recompiler.Contains(fragment, StringComparison.Ordinal)))
            throw new InvalidOperationException("Recompiler emitted-name algorithm changed; review hook expectations before accepting this host.");

        var functions = new List<(string Overlay, string Name)>();
        AddFunctionNames(functions, "", File.ReadAllText(Path.Combine(hostRoot, "config", "funcmaps", "main.json")));
        foreach (JsonElement overlay in configRoot.GetProperty("overlays").EnumerateArray())
        {
            string overlayName = RequiredString(overlay, "name");
            string mapPath = RequiredString(overlay, "funcMap").Replace('/', Path.DirectorySeparatorChar);
            AddFunctionNames(functions, overlayName, File.ReadAllText(Path.Combine(hostRoot, "config", mapPath)));
        }

        var expected = new Dictionary<(string Overlay, string BaseName), string>
        {
            [("", "DrawOTag")] = "DrawOTag",
            [("dra", "RenderEntities")] = "RenderEntities",
            [("dra", "RunMainEngine")] = "RunMainEngine",
            [("dra", "UpdatePlayerEntities")] = "UpdatePlayerEntities",
            [("sel", "ApplySaveData")] = "ApplySaveData_sel",
            [("cen", "GetDistanceToPlayerX")] = "GetDistanceToPlayerX_cen",
            [("cen", "GetDistanceToPlayerY")] = "GetDistanceToPlayerY_cen",
            [("cen", "GetSideToPlayer")] = "GetSideToPlayer_cen"
        };

        foreach (((string overlay, string baseName), string expectedName) in expected)
        {
            int targetCount = functions.Count(function => function.Overlay == overlay && function.Name == baseName);
            if (targetCount != 1)
                throw new InvalidOperationException($"Host {overlay}/{baseName} must resolve to exactly one function before hook emission.");
            bool crossOverlayDuplicate = functions.Where(function => function.Name == baseName)
                .Select(function => function.Overlay)
                .Distinct(StringComparer.Ordinal)
                .Count() > 1;
            string emittedName = crossOverlayDuplicate && overlay.Length != 0 ? $"{baseName}_{overlay}" : baseName;
            if (!string.Equals(emittedName, expectedName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Host {overlay}/{baseName} emits {emittedName}, expected {expectedName}.");
        }
    }

    private static void AddFunctionNames(List<(string Overlay, string Name)> functions, string overlay, string mapText)
    {
        using JsonDocument map = JsonDocument.Parse(mapText);
        functions.AddRange(map.RootElement.GetProperty("functions").EnumerateArray()
            .Select(function => (overlay, RequiredString(function, "name"))));
    }

    private static void ValidateFunctionMap(string text, string label, IReadOnlyCollection<string> expectedNames)
    {
        using JsonDocument map = JsonDocument.Parse(text);
        var actualNames = map.RootElement.GetProperty("functions").EnumerateArray()
            .Select(function => RequiredString(function, "name"))
            .ToHashSet(StringComparer.Ordinal);
        string[] missing = expectedNames.Where(name => !actualNames.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"Host {label} function map is missing: {string.Join(", ", missing)}.");
    }

    private static void RunNegativeContractTests(string manifest, string source, string version, string hostRoot)
    {
        ExpectFailure(() => ValidateManifest("{\"id\":\"missing-required-fields\"}"), "missing manifest version");
        ExpectFailure(() => ValidateSource(source.Replace("[PostHook(\"main\", \"DrawOTag\")]", "[PostHook(\"main\", \"MissingDrawHook\")]", StringComparison.Ordinal), version),
            "missing required hook");
        ExpectFailure(() => ValidateSource(source.Replace("private const uint ExpectedDealDamage = 0x800FF128;", "private const uint ExpectedDealDamage = 0x00000000;", StringComparison.Ordinal), version),
            "changed compatibility constant");
        ExpectFailure(() => ValidateSource(source + "\n// GameApi.DealDamage(context, memory, 0, 0);", version),
            "prohibited direct damage call");
        ExpectFailure(() => ValidateSource(source.Replace(
                "_ = CpuContextDirectCall.Invoke(context, memory, _collisionFunction,",
                "_ = GameApi.Call(context, memory, _collisionFunction,", StringComparison.Ordinal), version),
            "allocating collision GameApi call");
        ExpectFailure(() => ValidateSource(source.Replace(
                "_ = CpuContextGuardedDirectCall.Invoke(_context, _memory, function, _entity, 0, 0, 0);",
                "_ = GameApi.Call(_context, _memory, function, _entity);", StringComparison.Ordinal), version),
            "allocating publication GameApi call");
        ExpectFailure(() => ValidateSource(source.Replace(
                "callOk = CpuContextScratchDirectCall.TryInvoke(context, memory, ExpectedCalcAttack,",
                "callOk = GameApi.Call(context, memory, ExpectedCalcAttack,", StringComparison.Ordinal), version),
            "allocating profile GameApi call");
        string draMap = File.ReadAllText(Path.Combine(hostRoot, "config", "funcmaps", "dra.json"));
        ExpectFailure(() => ValidateFunctionMap(draMap.Replace("\"RunMainEngine\"", "\"MissingRunMainEngine\"", StringComparison.Ordinal),
            "dra", ["RenderEntities", "RunMainEngine", "UpdatePlayerEntities"]), "missing host symbol");
    }

    private static void ExpectFailure(Action action, string name)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException($"Negative contract test did not reject {name}.");
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        JsonElement value = root.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"mod.json {propertyName} must be a nonempty string.");
        return value.GetString()!;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ModIdRegex();

    [GeneratedRegex("^(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemVerRegex();

    [GeneratedRegex("private\\s+const\\s+string\\s+Version\\s*=\\s*\"([^\"]+)\"\\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex SourceVersionRegex();

    [GeneratedRegex("\\[(PreHook|PostHook)\\(\"([^\"]+)\",\\s*\"([^\"]+)\"\\)\\]", RegexOptions.CultureInvariant)]
    private static partial Regex HookRegex();
}
