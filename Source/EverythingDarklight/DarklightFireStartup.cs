using HarmonyLib;
using RimWorld;
using Verse;

namespace EverythingDarklight;

// Darklight toggle for fire buildings (2026-10-08). Vanilla offers darklight fires only as separate buildings (darktorch,
// darklight brazier, ...) because each flame overlay draws one fixed texture. At startup this:
//  1. replaces vanilla CompFireOverlay / CompDarklightOverlay on every def with CompFireOverlay_DarklightAware, whose flame
//     follows the light colour; flames drawn by Dynamic Effects Forge follow it through Forge_DarklightFlames,
//  2. turns on CompGlower's vanilla darklight toggle (Ideology) for those defs when their light is not darklight already,
//  3. hides darklight fire buildings that burn wood from the architect menu, since their normal version can now be
//     switched. Darklight fires that burn something else (fungus darktorch, Undergrounders' fungus fires) have no normal
//     counterpart and stay buildable. Hiding goes through Designator_Build.Visible, which vanilla's menu, Better Architect
//     Menu and the copy command all check. Clearing canGenerateDefaultDesignator instead would make existing ones count as
//     another ideoligion's buildings for style dominance (IdeoUtility). Existing buildings keep working; god mode shows all.
// Defs whose flame comes from a modded overlay (TabulaRasa, VFE Tribals' large fire, Vanilla Memes Expanded, ...) are left
// alone and get no toggle, their flame would not follow the light.
[StaticConstructorOnStartup]
internal static class DarklightFireStartup
{
    private static readonly HashSet<BuildableDef> hidden = new();

    static DarklightFireStartup()
    {
        var harmony = new Harmony("kahirdragoon.everythingdarklight");

        // Forge has to convert the overlays it takes over first; the vanilla overlays it leaves are replaced below.
        string? forge = null;
        try
        {
            Forge_DarklightFlames.RunForgeInit();
            forge = Forge_DarklightFlames.TryApply(harmony);
        }
        catch (Exception ex)
        {
            Log.Warning("[Everything Darklight] Dynamic Effects Forge patch not applied: " + ex.Message);
        }

        int followDefs = 0;
        var toggled = new List<string>();
        var keptBuildable = new List<string>();
        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (def.comps.NullOrEmpty()) continue;
            bool followsLight = ReplaceVanillaOverlays(def) || Forge_DarklightFlames.HasSwitchableEmitter(def);
            if (followsLight) followDefs++;

            CompProperties_Glower? glow = def.GetCompProperties<CompProperties_Glower>();
            if (glow == null) continue;
            if (!DarklightUtility.IsDarklight(glow.glowColor.ToColor))
            {
                if (followsLight && !glow.darklightToggle)
                {
                    glow.darklightToggle = true;
                    toggled.Add(def.defName);
                }
            }
            else if (ModsConfig.IdeologyActive && def.designationCategory != null && def.comps.Any(p => p is CompProperties_FireOverlay))
            {
                if (BurnsWood(def)) hidden.Add(def);
                else keptBuildable.Add(def.defName);
            }
        }

        if (hidden.Count != 0)
        {
            harmony.Patch(AccessTools.DeclaredPropertyGetter(typeof(Designator_Build), nameof(Designator_Build.Visible)),
                postfix: new HarmonyMethod(typeof(DarklightFireStartup), nameof(Visible_Postfix)));
        }

        Log.Message($"[Everything Darklight] Flames follow the light on {followDefs} def(s)"
                    + (forge != null ? $" ({forge})" : "")
                    + $"; darklight toggle added to {toggled.Count}: {string.Join(", ", toggled)}"
                    + $"; hidden from the architect menu: {string.Join(", ", hidden.Select(d => d.defName))}"
                    + (keptBuildable.Count != 0 ? $"; darklight fires kept buildable (do not burn wood): {string.Join(", ", keptBuildable)}" : ""));
    }

    // Only the exact vanilla comp classes; modded subclasses draw their own way.
    private static bool ReplaceVanillaOverlays(ThingDef def)
    {
        bool any = false;
        foreach (CompProperties p in def.comps)
        {
            if (p is not CompProperties_FireOverlay) continue;
            if (p.compClass == typeof(CompFireOverlay) || p.compClass == typeof(CompDarklightOverlay))
                p.compClass = typeof(CompFireOverlay_DarklightAware);
            // Also counts props shared with a def handled earlier.
            if (p.compClass == typeof(CompFireOverlay_DarklightAware)) any = true;
        }
        return any;
    }

    private static bool BurnsWood(ThingDef def)
    {
        ThingFilter? fuel = def.GetCompProperties<CompProperties_Refuelable>()?.fuelFilter;
        return fuel != null && fuel.Allows(ThingDefOf.WoodLog);
    }

    // Vanilla returns true in god mode before any check, so hidden buildings stay placeable there.
    private static void Visible_Postfix(Designator_Build __instance, ref bool __result)
    {
        if (__result && !DebugSettings.godMode && hidden.Contains(__instance.PlacingDef)) __result = false;
    }
}
