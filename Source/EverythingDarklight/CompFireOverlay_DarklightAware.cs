using RimWorld;
using UnityEngine;
using Verse;

namespace EverythingDarklight;

// Graphic_Flicker reads size and offset through CompFireOverlayBase, so both flames keep the def's fireSize/offset.
public class CompFireOverlay_DarklightAware : CompFireOverlay
{
    private CompGlower? glower;

    // DarklightUtility.IsDarklight is the test the darklight precept uses. DarklightFireStartup only swaps the compClass,
    // so without a glower the props class still tells which flame the def originally had.
    public bool ShowsDarklight => glower != null
        ? DarklightUtility.IsDarklight(glower.GlowColor.ToColor)
        : props is CompProperties_DarklightOverlay;

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        glower = parent.GetComp<CompGlower>();
    }

    // Mirrors CompFireOverlay.PostDraw. Both flames use parent.Rotation because vanilla's darklight overlay passes
    // Rot4.North, which puts the flame of a rotated building (wall torch) at its north offset when the def has
    // per-rotation offsets.
    public override void PostDraw()
    {
        if (refuelableComp != null && !refuelableComp.HasFuel) return;
        Vector3 drawPos = parent.DrawPos;
        drawPos.y += 0.03658537f;
        Graphic graphic = ShowsDarklight ? CompDarklightOverlay.DarklightGraphic : FireGraphic;
        graphic.Draw(drawPos, parent.Rotation, parent);
    }
}
