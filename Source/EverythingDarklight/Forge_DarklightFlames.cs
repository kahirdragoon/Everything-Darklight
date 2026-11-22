using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace EverythingDarklight;

// Dynamic Effects Forge (blues.forge, workshop 3735443951) replaces the vanilla fire overlays at startup
// (ForgeInit.SwapFireOverlays, its "buildings" option, on by default): the vanilla comp becomes a silent subclass that
// draws nothing, and an added CompProperties_ForgedEmitter runs the particle effect Flamme, or DarkFlamme for a
// CompProperties_DarklightOverlay def. The effect is fixed per def. The light colour only reaches the flicker layer
// (CompForgedEmitter.UpdateColorPalette feeds instructions marked isFlicker); the flame particles keep the effect's colours.
// This makes the effect follow the light: CompForgedEffector.EffectDef answers DarkFlamme while the building's light is
// darklight and Flamme otherwise, and CompForgedEffector.CheckStatus (every 40 ticks, from ActionManager.MapComponentTick,
// so not while paused) stops a running effect that is the wrong one; Forge's CheckStatus then recreates it with EffectDef.
// Only emitters running Flamme or DarkFlamme are touched (not FlammeWild on real fires, not other effectors).
// The effects are matched by defName: Forge's DefReloader.ReloadBlastDefs (run when its mod settings window closes, and by
// its debug "Reload Forge Defs" gizmo) clears DefDatabase<SingleTakeDef>, builds new instances and relinks the props by
// name, while ForgeDefs keeps the old ones. Comparing instances made the switch stop after the settings window was opened.
// Checked against the live BluesForge.dll on 2026-10-08. Everything is looked up by name, Forge is not referenced.
internal static class Forge_DarklightFlames
{
    private const string FlammeName = "Flamme";
    private const string DarkFlammeName = "DarkFlamme";

    private static Type? emitterType;
    private static Type? emitterPropsType;
    private static Type? singleTakeDefType;
    private static FieldInfo? propsEffectDef;     // CompProperties_ForgeEffector.effectDef
    private static FieldInfo? activeRunner;       // CompForgedEffector.activeRunner
    private static FieldInfo? runnerDef;          // EffectRunner.rDef
    private static PropertyInfo? runnerDestroyed; // EffectRunner.IsDestroyed
    private static MethodInfo? stopRunner;        // CompForgedEffector.StopRunner(Map)

    // The instances currently in DefDatabase<SingleTakeDef>; refetched by Sync after a Forge reload.
    private static Def? flamme;
    private static Def? darkFlamme;

    public static bool Active { get; private set; }

    // Forge swaps the overlays in ForgeInit's [StaticConstructorOnStartup] constructor. Running it here makes sure the swap
    // is done before DarklightFireStartup replaces the vanilla overlays Forge left; it is a no-op once it has run.
    public static void RunForgeInit()
    {
        Type? init = GenTypes.GetTypeInAnyAssembly("Blues.ForgeInit");
        if (init != null) RuntimeHelpers.RunClassConstructor(init.TypeHandle);
    }

    // Returns null when Forge is not loaded or no longer matches.
    public static string? TryApply(Harmony harmony)
    {
        Type? effectorType = GenTypes.GetTypeInAnyAssembly("Blues.CompForgedEffector");
        if (effectorType == null) return null;

        emitterType = GenTypes.GetTypeInAnyAssembly("Blues.CompForgedEmitter");
        emitterPropsType = GenTypes.GetTypeInAnyAssembly("Blues.CompProperties_ForgedEmitter");
        singleTakeDefType = GenTypes.GetTypeInAnyAssembly("Blues.SingleTakeDef");
        Type? effectorPropsType = GenTypes.GetTypeInAnyAssembly("Blues.CompProperties_ForgeEffector");
        Type? runnerType = GenTypes.GetTypeInAnyAssembly("Blues.EffectRunner");
        if (emitterType == null || emitterPropsType == null || singleTakeDefType == null || effectorPropsType == null || runnerType == null)
        {
            Log.Warning("[Everything Darklight] Dynamic Effects Forge types not found, its flames will not follow the light.");
            return null;
        }

        propsEffectDef = AccessTools.Field(effectorPropsType, "effectDef");
        activeRunner = AccessTools.Field(effectorType, "activeRunner");
        runnerDef = AccessTools.Field(runnerType, "rDef");
        runnerDestroyed = AccessTools.Property(runnerType, "IsDestroyed");
        stopRunner = AccessTools.DeclaredMethod(effectorType, "StopRunner");
        MethodInfo? effectDefGetter = AccessTools.DeclaredPropertyGetter(effectorType, "EffectDef");
        MethodInfo? checkStatus = AccessTools.DeclaredMethod(effectorType, "CheckStatus");
        Refetch();
        if (propsEffectDef == null || activeRunner == null || runnerDef == null || runnerDestroyed == null || stopRunner == null
            || effectDefGetter == null || checkStatus == null || flamme == null || darkFlamme == null)
        {
            Log.Warning("[Everything Darklight] Dynamic Effects Forge members not found, its flames will not follow the light.");
            return null;
        }

        harmony.Patch(effectDefGetter, postfix: new HarmonyMethod(typeof(Forge_DarklightFlames), nameof(EffectDef_Postfix)));
        harmony.Patch(checkStatus, prefix: new HarmonyMethod(typeof(Forge_DarklightFlames), nameof(CheckStatus_Prefix)));
        Active = true;
        return "Dynamic Effects Forge flames follow the light";
    }

    // The def's flame is a Forge emitter running Flamme or DarkFlamme (Forge converted its vanilla overlay).
    public static bool HasSwitchableEmitter(ThingDef def) =>
        Active && def.comps.Any(p => p.GetType() == emitterPropsType && IsFlame(propsEffectDef!.GetValue(p)));

    private static bool IsFlame(object? effect) => effect is Def d && (d.defName == FlammeName || d.defName == DarkFlammeName);

    private static void Refetch()
    {
        flamme = GenDefDatabase.GetDefSilentFail(singleTakeDefType!, FlammeName, false);
        darkFlamme = GenDefDatabase.GetDefSilentFail(singleTakeDefType!, DarkFlammeName, false);
    }

    // `current` is the flame the emitter's props point at now. Forge relinks the props on a reload, so a current flame that
    // is not one of the cached instances means the cache is stale.
    private static void Sync(Def current)
    {
        if (current != flamme && current != darkFlamme) Refetch();
    }

    // Null when the building has no light to follow.
    private static Def? WantedFor(ThingComp comp, Def current)
    {
        CompGlower? glower = comp.parent?.GetComp<CompGlower>();
        if (glower == null) return null;
        Sync(current);
        return DarklightUtility.IsDarklight(glower.GlowColor.ToColor) ? darkFlamme : flamme;
    }

    // __result is a SingleTakeDef; only Flamme/DarkFlamme (both SingleTakeDefs) are ever written to it.
    private static void EffectDef_Postfix(ThingComp __instance, ref Def __result)
    {
        if (!IsFlame(__result) || !emitterType!.IsInstanceOfType(__instance)) return;
        Def? wanted = WantedFor(__instance, __result);
        if (wanted != null) __result = wanted;
    }

    private static void CheckStatus_Prefix(ThingComp __instance)
    {
        if (!emitterType!.IsInstanceOfType(__instance)) return;
        try
        {
            if (propsEffectDef!.GetValue(__instance.props) is not Def current || !IsFlame(current)) return;
            object? runner = activeRunner!.GetValue(__instance);
            if (runner == null || (bool)runnerDestroyed!.GetValue(runner)) return;
            // A runner started before a reload still holds the old instance, so compare names.
            if (runnerDef!.GetValue(runner) is not Def running || !IsFlame(running)) return;
            Def? wanted = WantedFor(__instance, current);
            if (wanted != null && wanted.defName != running.defName) stopRunner!.Invoke(__instance, new object?[] { null });
        }
        catch (Exception ex)
        {
            Log.ErrorOnce("[Everything Darklight] Switching a Dynamic Effects Forge flame failed: " + ex, 0x5D4F1A2);
        }
    }
}
