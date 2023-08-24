using System.Collections.Generic;
using HarmonyLib;
using KSP.Game;
using MonoMod.Cil;
using SpaceWarp.API.Loading;
using SpaceWarp.API.Mods;
using SpaceWarp.Patching.LoadingActions;

namespace SpaceWarp.Patching;

[HarmonyPatch]
internal static class BootstrapPatch
{
    internal static bool ForceSpaceWarpLoadDueToError = false;
    internal static SpaceWarpPluginDescriptor ErroredSWPluginDescriptor;
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Awake))]
    private static void GetSpaceWarpMods()
    {
        SpaceWarpManager.GetAllPlugins();
    }

    [HarmonyILManipulator]
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartBootstrap))]
    private static void PatchInitializationsIL(ILContext ilContext, ILLabel endLabel)
    {
        ILCursor ilCursor = new(ilContext);

        var flowProp = AccessTools.DeclaredProperty(typeof(GameManager), nameof(GameManager.LoadingFlow));

        ilCursor.GotoNext(
            MoveType.After,
            instruction => instruction.MatchCallOrCallvirt(flowProp.SetMethod)
        );

        ilCursor.EmitDelegate(static () =>
        {
            if (ForceSpaceWarpLoadDueToError)
            {
                GameManager.Instance.LoadingFlow.AddAction(new PreInitializeModAction(ErroredSWPluginDescriptor));
            }
            foreach (var plugin in SpaceWarpManager.AllPlugins)
            {
                if (plugin.Plugin != null)
                    GameManager.Instance.LoadingFlow.AddAction(new PreInitializeModAction(plugin));
            }


        });

        ilCursor.GotoLabel(endLabel, MoveType.Before);
        ilCursor.Index -= 1;
        ilCursor.EmitDelegate(static () =>
        {
            GameManager.Instance.LoadingFlow.AddAction(new InjectKspModPreInitializationActions());
            var flow = GameManager.Instance.LoadingFlow;
            IList<SpaceWarpPluginDescriptor> allPlugins;
            if (ForceSpaceWarpLoadDueToError)
            {
                var l = new List<SpaceWarpPluginDescriptor> { ErroredSWPluginDescriptor };
                l.AddRange(SpaceWarpManager.AllPlugins);
                allPlugins = l;
            }
            else
            {
                allPlugins = SpaceWarpManager.AllPlugins;
            }

            foreach (var plugin in allPlugins)
            {
                flow.AddAction(new LoadAddressablesAction(plugin));
                flow.AddAction(new LoadLocalizationAction(plugin));
                if (plugin.Plugin is BaseSpaceWarpPlugin baseSpaceWarpPlugin)
                {
                    foreach (var action in Loading.LoadingActionGenerators)
                    {
                        flow.AddAction(action(baseSpaceWarpPlugin));
                    }
                }
                else
                {
                    foreach (var action in Loading.FallbackDescriptorLoadingActionGenerators)
                    {
                        flow.AddAction(action(plugin));
                    }
                }

                foreach (var action in Loading.DescriptorLoadingActionGenerators)
                {
                    flow.AddAction(action(plugin));
                }
            }

            flow.AddAction(new InjectKspModAssetLoadingActions());

            flow.AddAction(new LoadAddressablesLocalizationsAction());
            foreach (var actionGenerator in Loading.GeneralLoadingActions)
            {
                flow.AddAction(actionGenerator());
            }
            
            foreach (var plugin in allPlugins)
            {
                if (plugin.Plugin != null)
                    flow.AddAction(new InitializeModAction(plugin));
            }
            flow.AddAction(new InjectKspModInitializationActions());
            
            foreach (var plugin in allPlugins)
            {
                flow.AddAction(new LoadLuaAction(plugin));
            }
            

            foreach (var plugin in allPlugins)
            {
                if (plugin.Plugin != null)
                    flow.AddAction(new PostInitializeModAction(plugin));
            }
            flow.AddAction(new InjectKspModPostInitializationActions());
        });
    }
}