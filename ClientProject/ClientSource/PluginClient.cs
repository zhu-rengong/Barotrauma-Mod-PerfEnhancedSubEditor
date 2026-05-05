using HarmonyLib;
using System.Diagnostics;

namespace PerfEnhancedSubEditor;

public partial class Plugin
{
    #region Frustum Culling

    public static int ParallelismLevel => Math.Min(Environment.ProcessorCount, 4);
    private static Stopwatch performanceTimer = new();
    private static AttachedProperty<bool> isEntityCulled = AttachedProperty<bool>.Create(false);

    [HarmonyPatch(typeof(SubEditorScreen), nameof(SubEditorScreen.Draw)), HarmonyPrefix]
    private static void SubEditorScreen_Draw_Prefix()
    {
        performanceTimer.Restart();

        var entities = MapEntity.MapEntityList;
        Rectangle camView = (Screen.Selected as SubEditorScreen)!.Cam.WorldView;

        if (entities.Count > 0)
        {
            Partitioner.Create(0, entities.Count, 256)
                .AsParallel()
                .WithDegreeOfParallelism(ParallelismLevel)
                .ForAll(range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        MapEntity e = entities[i];
                        isEntityCulled.SetValue(e, !e.IsVisible(camView));
                    }
                });
        }

        performanceTimer.Stop();

        GameMain.PerformanceCounter.AddElapsedTicks("Draw:PerfEnhancedSubEditor", performanceTimer.ElapsedTicks);
    }

    [HarmonyPatch(typeof(Submarine), nameof(Submarine.DrawBack)), HarmonyPrefix]
    static void Submarine_DrawBack_Prefix(ref Predicate<MapEntity>? predicate, bool editing)
        => InjectRenderCulling(ref predicate, editing);

    [HarmonyPatch(typeof(Submarine), nameof(Submarine.DrawDamageable)), HarmonyPrefix]
    static void Submarine_DrawDamageable_Prefix(ref Predicate<MapEntity>? predicate, bool editing)
        => InjectRenderCulling(ref predicate, editing);

    [HarmonyPatch(typeof(Submarine), nameof(Submarine.DrawFront)), HarmonyPrefix]
    static void Submarine_DrawFront_Prefix(ref Predicate<MapEntity>? predicate, bool editing)
        => InjectRenderCulling(ref predicate, editing);

    static void InjectRenderCulling(ref Predicate<MapEntity>? predicate, bool editing)
    {
        if (editing && SubEditorScreen.IsSubEditor())
        {
            var originalPredicate = predicate;

            predicate = originalPredicate == null
                 ? entity => !isEntityCulled.GetValue(entity)
                 : entity => !isEntityCulled.GetValue(entity) && originalPredicate(entity);
        }
    }

    #endregion

    #region Hide Entity Menu

    [HarmonyPatch(typeof(SubEditorScreen), nameof(SubEditorScreen.AddToGUIUpdateList)), HarmonyPrefix]
    private static void SubEditorScreen_AddToGUIUpdateList_Prefix(SubEditorScreen __instance)
    {
        __instance.EntityMenu.Visible = __instance.entityMenuOpenState > 0.0f;
    }

    [HarmonyPatch(typeof(SubEditorScreen), nameof(SubEditorScreen.AddToGUIUpdateList)), HarmonyPostfix]
    private static void SubEditorScreen_AddToGUIUpdateList_Postfix(SubEditorScreen __instance)
    {
        if (!__instance.EntityMenu.Visible)
        {
            __instance.EntityMenu.Visible = true;
            __instance.EntityMenu.AddToGUIUpdateList(ignoreChildren: true);
            __instance.ToggleEntityMenuButton.AddToGUIUpdateList();
        }
    }

    #endregion

    #region Performance fix for resizing hull

    private static bool isResizingHull;

    [HarmonyPatch(typeof(MapEntity), nameof(MapEntity.Resizing), MethodType.Setter), HarmonyPrefix]
    static void MapEntity_Resizing_Setter_Prefix(ref bool value)
    {
        if (!isResizingHull) { return; }

        if (!value)
        {
            Item.UpdateHulls();
            Gap.UpdateHulls();
        }
    }

    [HarmonyPatch(typeof(MapEntity), nameof(MapEntity.UpdateResizing)), HarmonyPrefix]
    static void MapEntity_UpdateResizing_Prefix(MapEntity __instance)
    {
        isResizingHull = __instance is Hull;
    }

    [HarmonyPatch(typeof(MapEntity), nameof(MapEntity.UpdateResizing)), HarmonyFinalizer]
    static void MapEntity_UpdateResizing_Finalizer(MapEntity __instance)
    {
        isResizingHull = false;
    }

    [HarmonyPatch(typeof(Hull), nameof(Hull.Rect), MethodType.Setter), HarmonyPrefix]
    static bool Hull_Rect_Setter_Prefix(Hull __instance, ref Rectangle value)
    {
        if (SubEditorScreen.IsSubEditor())
        {
            float prevOxygenPercentage = __instance.OxygenPercentage;
            __instance.rect = value;
            __instance.OxygenPercentage = prevOxygenPercentage;
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(Hull), nameof(Hull.Load)), HarmonyPrefix]
    static void Hull_Load_Prefix(Hull __instance, ContentXElement element)
    {
        if (SubEditorScreen.IsSubEditor())
        {
            element.GetAttribute("backgroundsections")?.Remove();
        }
    }

    [HarmonyPatch(typeof(Hull), nameof(Hull.CreateBackgroundSections)), HarmonyPrefix]
    static bool Hull_CreateBackgroundSections_Prefix(Hull __instance)
    {
        return !SubEditorScreen.IsSubEditor();
    }

    [HarmonyPatch(typeof(Hull), nameof(Hull.RefreshAveragePaintedColor)), HarmonyPrefix]
    static bool Hull_RefreshAveragePaintedColor_Prefix(Hull __instance)
    {
        return !SubEditorScreen.IsSubEditor();
    }

    private static bool isHullBackgroundSectionsDefined;

    [HarmonyPatch(typeof(Submarine), nameof(Submarine.DrawPaintedColors)), HarmonyPrefix]
    static bool Submarine_DrawPaintedColors_Prefix(bool editing)
    {
        if (editing && SubEditorScreen.IsSubEditor())
        {
            return false;
        }

        return true;
    }

    #endregion
}
