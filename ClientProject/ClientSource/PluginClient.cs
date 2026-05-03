using HarmonyLib;
using System.Diagnostics;

namespace PerfEnhancedSubEditor;

public partial class Plugin
{
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
                .WithMergeOptions(ParallelMergeOptions.AutoBuffered)
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

    [HarmonyPatch(typeof(Submarine), nameof(Submarine.DrawPaintedColors)), HarmonyPrefix]
    static void Submarine_DrawPaintedColors_Prefix(ref Predicate<MapEntity>? predicate, bool editing)
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
}
