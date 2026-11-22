using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace EverythingDarklight;

// Dynamic Effects Forge (blues.forge, workshop 3735443951) replaces the vanilla fire overlays with a particle emitter
// whose effect (Flamme, or DarkFlamme for a darklight def) is fixed per def. The light colour reaches only the flicker
// layer, not the flame particles, so without this its flames would not switch with the darklight toggle.
// Effects are matched by defName, not instance: Forge's DefReloader.ReloadBlastDefs (run when its mod settings window
// closes, and by its debug "Reload Forge Defs" gizmo) rebuilds DefDatabase<SingleTakeDef> and relinks the props by name,
// while ForgeDefs keeps the old instances. Comparing instances made the switch stop after the settings window was opened.
// Everything is looked up by name so Forge stays optional. Checked against BluesForge.dll on 2026-10-08.
internal static class Forge_DarklightFlames
{
    private const string FlammeName = "Flamme";
    private const string DarkFlammeName = "DarkFlamme";

    private static Type? emitterType;
    private static Type? emitterPropsType;
    private static Type? singleTakeDefType;
    private static FieldInfo? propsEffectDef;
    private static FieldInfo? activeRunner;
    private static FieldInfo? runnerDef;
    private static PropertyInfo? runnerDestroyed;
    private static MethodInfo? stopRunner;

    private static Def? flamme;
    private static Def? darkFlamme;

    public static bool Active { get; private set; }

    // Forge swaps the overlays in ForgeInit's [StaticConstructorOnStartup] constructor, which may not have run yet.
    // Running a class constructor that already ran is a no-op.
    public static void RunForgeInit()
    {
        Type? init = GenTypes.GetTypeInAnyAssembly("Blues.ForgeInit");
        if (init != null) RuntimeHelpers.RunClassConstructor(init.TypeHandle);
    }

    public static void TryApply(Harmony harmony)
    {
        Type? effectorType = GenTypes.GetTypeInAnyAssembly("Blues.CompForgedEffector");
        if (effectorType == null) return;

        emitterType = GenTypes.GetTypeInAnyAssembly("Blues.CompForgedEmitter");
        emitterPropsType = GenTypes.GetTypeInAnyAssembly("Blues.CompProperties_ForgedEmitter");
        singleTakeDefType = GenTypes.GetTypeInAnyAssembly("Blues.SingleTakeDef");
        Type? effectorPropsType = GenTypes.GetTypeInAnyAssembly("Blues.CompProperties_ForgeEffector");
        Type? runnerType = GenTypes.GetTypeInAnyAssembly("Blues.EffectRunner");
        if (emitterType == null || emitterPropsType == null || singleTakeDefType == null || effectorPropsType == null || runnerType == null)
        {
            Log.Warning("[Everything Darklight] Dynamic Effects Forge types not found, its flames will not follow the light.");
            return;
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
            return;
        }

        harmony.Patch(effectDefGetter, postfix: new HarmonyMethod(typeof(Forge_DarklightFlames), nameof(EffectDef_Postfix)));
        harmony.Patch(checkStatus, prefix: new HarmonyMethod(typeof(Forge_DarklightFlames), nameof(CheckStatus_Prefix)));
        Active = true;
    }

    public static bool HasSwitchableEmitter(ThingDef def) =>
        Active && def.comps.Any(p => p.GetType() == emitterPropsType && IsFlame(propsEffectDef!.GetValue(p)));

    // Keeps FlammeWild on real fires and other effectors out of the switch.
    private static bool IsFlame(object? effect) => effect is Def d && (d.defName == FlammeName || d.defName == DarkFlammeName);

    private static void Refetch()
    {
        flamme = GenDefDatabase.GetDefSilentFail(singleTakeDefType!, FlammeName, false);
        darkFlamme = GenDefDatabase.GetDefSilentFail(singleTakeDefType!, DarkFlammeName, false);
    }

    // Forge relinks the props on a reload, so a props flame that is not one of the cached instances means the cache is stale.
    private static void Sync(Def current)
    {
        if (current != flamme && current != darkFlamme) Refetch();
    }

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

    // Only stops a wrong runner: Forge's CheckStatus then starts the effect again from EffectDef. CheckStatus runs every
    // 40 ticks from a MapComponentTick, so the flame does not switch while paused.
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
