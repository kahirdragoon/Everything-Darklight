using RimWorld;
using UnityEngine;
using Verse;

namespace EverythingDarklight;

// Takes the place of vanilla CompFireOverlay and CompDarklightOverlay on every def (DarklightFireStartup swaps the
// compClass, the props stay as they are). Draws the darklight flame while the building's light is darklight
// (DarklightUtility.IsDarklight, the test the darklight precept uses) and the normal flame otherwise, so CompGlower's
// darklight toggle switches the flames too. Without a glower it draws the flame its props class stands for.
// Graphic_Flicker reads size and offset through CompFireOverlayBase, so both flames keep the def's fireSize/offset.
public class CompFireOverlay_DarklightAware : CompFireOverlay
{
    private CompGlower? glower;

    public bool ShowsDarklight => glower != null
        ? DarklightUtility.IsDarklight(glower.GlowColor.ToColor)
        : props is CompProperties_DarklightOverlay;

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        glower = parent.GetComp<CompGlower>();
    }

    // CompFireOverlay.PostDraw with the graphic picked per frame. Both flames use parent.Rotation; vanilla's darklight
    // overlay passes Rot4.North, which puts the flame of a rotated building (wall torch) at its north offset. The two
    // only differ when the def has per-rotation offsets.
    public override void PostDraw()
    {
        if (refuelableComp != null && !refuelableComp.HasFuel) return;
        Vector3 drawPos = parent.DrawPos;
        drawPos.y += 0.03658537f;
        Graphic graphic = ShowsDarklight ? CompDarklightOverlay.DarklightGraphic : FireGraphic;
        graphic.Draw(drawPos, parent.Rotation, parent);
    }
}
