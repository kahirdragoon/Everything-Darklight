using HarmonyLib;
using RimWorld;
using Verse;

namespace EverythingDarklight;

// The vanilla flame overlays are replaced because each draws one fixed texture, which is why vanilla has darklight fires
// only as separate buildings.
// Wood-burning darklight fires are hidden because their normal version can now be switched. Darklight fires that burn
// something else (fungus darktorch, Undergrounders' fungus fires) have no normal counterpart and stay buildable.
// Hiding goes through Designator_Build.Visible because vanilla's menu, Better Architect Menu and the copy command all
// check it. Clearing canGenerateDefaultDesignator instead would make existing ones count as another ideoligion's
// buildings for style dominance (IdeoUtility).
// Defs whose flame comes from a modded overlay (TabulaRasa, VFE Tribals' large fire, Vanilla Memes Expanded, ...) get no
// toggle because their flame would not follow the light.
[StaticConstructorOnStartup]
internal static class DarklightFireStartup
{
    private static readonly HashSet<BuildableDef> hidden = new();

    static DarklightFireStartup()
    {
        var harmony = new Harmony("kahirdragoon.everythingdarklight");

        // Forge has to convert the overlays it takes over first; the vanilla overlays it leaves are replaced below.
        try
        {
            Forge_DarklightFlames.RunForgeInit();
            Forge_DarklightFlames.TryApply(harmony);
        }
        catch (Exception ex)
        {
            Log.Warning("[Everything Darklight] Dynamic Effects Forge patch not applied: " + ex.Message);
        }

        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (def.comps.NullOrEmpty()) continue;
            bool followsLight = ReplaceVanillaOverlays(def) || Forge_DarklightFlames.HasSwitchableEmitter(def);

            CompProperties_Glower? glow = def.GetCompProperties<CompProperties_Glower>();
            if (glow == null) continue;
            if (!DarklightUtility.IsDarklight(glow.glowColor.ToColor))
            {
                if (followsLight) glow.darklightToggle = true;
            }
            else if (ModsConfig.IdeologyActive && def.designationCategory != null && BurnsWood(def)
                     && def.comps.Any(p => p is CompProperties_FireOverlay))
            {
                hidden.Add(def);
            }
        }

        if (hidden.Count != 0)
        {
            harmony.Patch(AccessTools.DeclaredPropertyGetter(typeof(Designator_Build), nameof(Designator_Build.Visible)),
                postfix: new HarmonyMethod(typeof(DarklightFireStartup), nameof(Visible_Postfix)));
        }
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
            // Checked separately so props shared with a def handled earlier, and already swapped there, still count.
            if (p.compClass == typeof(CompFireOverlay_DarklightAware)) any = true;
        }
        return any;
    }

    private static bool BurnsWood(ThingDef def)
    {
        ThingFilter? fuel = def.GetCompProperties<CompProperties_Refuelable>()?.fuelFilter;
        return fuel != null && fuel.Allows(ThingDefOf.WoodLog);
    }

    // Vanilla shows every designator in god mode, so hidden buildings stay placeable there too.
    private static void Visible_Postfix(Designator_Build __instance, ref bool __result)
    {
        if (__result && !DebugSettings.godMode && hidden.Contains(__instance.PlacingDef)) __result = false;
    }
}
