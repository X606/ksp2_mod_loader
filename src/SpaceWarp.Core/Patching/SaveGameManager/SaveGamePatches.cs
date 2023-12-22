﻿using HarmonyLib;
using KSP.Game.Load;
using KSP.IO;
using SpaceWarp.API.Logging;
using SpaceWarp.API.SaveGameManager;
using SpaceWarp.Backend.SaveGameManager;
using SpaceWarp.InternalUtilities;
using System;

namespace SpaceWarp.Patching.SaveGameManager;

[HarmonyPatch]
internal class SaveLoadPatches
{
    private static readonly ILogger _logger = new UnityLogSource("SpaceWarp.SaveLoadPatches");

    /// SAVING ///

    [HarmonyPatch(typeof(SerializeGameDataFlowAction), MethodType.Constructor), HarmonyPostfix]
    [HarmonyPatch(new Type[] { typeof(string), typeof(LoadGameData) })]
    private static void InjectPluginSaveGameData(string filename, LoadGameData data, SerializeGameDataFlowAction __instance)
    {
        // Skip plugin data injection if there are no mods that have registered for save/load actions
        if (ModSaves.InternalPluginSaveData.Count == 0)
            return;

        // Take the game's LoadGameData, extend it with our own class and copy plugin save data to it
        SpaceWarpSerializedSavedGame modSaveData = new();
        InternalExtensions.CopyFieldAndPropertyDataFromSourceToTargetObject(data.SavedGame, modSaveData);
        modSaveData.serializedPluginSaveData = ModSaves.InternalPluginSaveData;
        data.SavedGame = modSaveData;

        // Initiate save callback for plugins that specified a callback function
        foreach (var plugin in ModSaves.InternalPluginSaveData)
        {
            plugin.SaveEventCallback(plugin.SaveData);
        }
    }

    /// LOADING ///

    [HarmonyPatch(typeof(DeserializeContentsFlowAction), "DoAction"), HarmonyPrefix]
    private static bool DeserializeLoadedPluginData(Action resolve, Action<string> reject, DeserializeContentsFlowAction __instance)
    {
        // Skip plugin deserialization if there are no mods that have registered for save/load actions
        if (ModSaves.InternalPluginSaveData.Count == 0)
            return true;

        __instance._game.UI.SetLoadingBarText(__instance.Description);
        try
        {
            // Deserialize save data to our own class that extends game's SerializedSavedGame
            SpaceWarpSerializedSavedGame serializedSavedGame = new();
            IOProvider.FromJsonFile<SpaceWarpSerializedSavedGame>(__instance._filename, out serializedSavedGame);
            __instance._data.SavedGame = serializedSavedGame;
            __instance._data.DataLength = IOProvider.GetFileSize(__instance._filename);

            // Perform plugin load data if plugin data is found in the save file
            if (serializedSavedGame.serializedPluginSaveData.Count > 0)
            {
                // Iterate through each plugin
                foreach (var loadedData in serializedSavedGame.serializedPluginSaveData)
                {
                    // Match registered plugin GUID with the GUID found in the save file
                    var existingData = ModSaves.InternalPluginSaveData.Find(p => p.ModGuid == loadedData.ModGuid);
                    if (existingData == null)
                    {
                        _logger.LogWarning($"Saved data for plugin '{loadedData.ModGuid}' found during a load event, however that plugin isn't registered for save/load events. Skipping load for this plugin.");
                        continue;
                    }

                    // Perform a callback if plugin specified a callback function. This is done before plugin data is actually updated.
                    existingData.LoadEventCallback(loadedData.SaveData);

                    // Copy loaded data to the SaveData object plugin registered
                    InternalExtensions.CopyFieldAndPropertyDataFromSourceToTargetObject(loadedData.SaveData, existingData.SaveData);
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogException(ex);
            reject(ex.Message);
        }
        resolve();

        return false;
    }
}