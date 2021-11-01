using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace CustomToolSpeed
{
    public class CustomToolSpeedMod : Mod
    {
        private const string PlayerPrefsToolSpeed = "traxam.CustomToolSpeed.toolSpeed";
        private const string HarmonyId = "traxam.CustomToolSpeed";
        private static Dictionary<GameMode, float> _originalToolSpeeds;
        private static float _currentToolsSpeed = 1;
        private Harmony _harmony;

        public void Start()
        {
            _originalToolSpeeds = new Dictionary<GameMode, float>();
            var gameModeValues =
                (SO_GameModeValue[])Traverse.Create(typeof(GameModeValueManager)).Field("gameModeValues").GetValue();
            foreach (var mode in gameModeValues)
                _originalToolSpeeds[mode.gameMode] = mode.toolVariables.removeSpeedMultiplier;

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            LogInfo("Mod was loaded successfully!");
            if (PlayerPrefs.HasKey(PlayerPrefsToolSpeed))
            {
                var toolSpeed = PlayerPrefs.GetFloat(PlayerPrefsToolSpeed);
                SetToolSpeedMultiplier(toolSpeed, false);
                FollowUpLog("The current tool speed is <color=green>" + _currentToolsSpeed + "</color>.");
            }

            FollowUpLog(
                "Type <color=green>toolspeed <speed></color> to change the tool speed, i.e. <color=green>toolspeed 2.5</color> (1 is the default tool speed).");
        }

        [ConsoleCommand("toolspeed", "Change the tool speed")]
        // ReSharper disable once UnusedMember.Local
        private static void HandleToolSpeedCommand(string[] arguments)
        {
            if (arguments.Length < 1)
            {
                LogInfo("The current tool speed is <color=green>" + _currentToolsSpeed + "</color>.");
                FollowUpLog(
                    "Type <color=green>toolspeed <speed></color> to change the tool speed, i.e. <color=green>toolspeed 2.5</color> (1 is the default tool speed).");
            }
            else
            {
                try
                {
                    var value = float.Parse(arguments[0], CultureInfo.InvariantCulture);
                    if (value <= 0)
                    {
                        LogError("The provided tool speed (<color=green>" + arguments[0] +
                                 "</color>) is not a positive value.");
                        FollowUpLog(
                            "Please provide a positive decimal tool speed value, i.e. <color=green>toolspeed 2.5</color> (1 is the default tool speed).");
                    }
                    else
                    {
                        SetToolSpeedMultiplier(value, true);
                        LogInfo("Tool speed was set to <color=green>" + value +
                                "</color>. Type <color=green>toolspeed 1</color> to reset it.");
                    }
                }
                catch (FormatException)
                {
                    LogError("<color=green>" + arguments[0] +
                             "</color> is not a valid value. Please provide a positive decimal tool speed value, i.e. <color=green>toolspeed 2.5</color> (1 is the default tool speed).");
                }
            }
        }

        private static void SetToolSpeedMultiplier(float multiplier, bool save)
        {
            var gameModeValues =
                (SO_GameModeValue[])Traverse.Create(typeof(GameModeValueManager)).Field("gameModeValues").GetValue();
            foreach (var mode in gameModeValues)
                mode.toolVariables.removeSpeedMultiplier =
                    _originalToolSpeeds[mode.gameMode] * multiplier;

            _currentToolsSpeed = multiplier;
            if (save)
            {
                PlayerPrefs.SetFloat(PlayerPrefsToolSpeed, multiplier);
                PlayerPrefs.Save();
            }
        }

        public void OnModUnload()
        {
            _harmony.UnpatchAll(HarmonyId);
            SetToolSpeedMultiplier(1, false);
            LogInfo("Tool speeds were reset.");
        }

        private static void LogInfo(string message)
        {
            Debug.Log("<color=#3498db>[info]</color>\t<b>traxam's CustomToolSpeed:</b> " + message);
        }

        private static void FollowUpLog(string message)
        {
            Debug.Log("\t" + message);
        }

        private static void LogError(string message)
        {
            Debug.LogError("<color=#e74c3c>[error]</color>\t<b>traxam's CustomToolSpeed:</b> " + message);
        }

        [HarmonyPatch(typeof(Axe))]
        [HarmonyPatch("OnAxeHit")]
        // This method is copied from the original Raft source code and was slightly modified to allow damage value
        // modification. To change as few things as possible, we are not considering ReSharper's code warnings.
        // Changes:
        // - replace "return" with "return false" and add "return false" at the end to cancel the execution of the
        //   original message
        // - replace references to private fields of the Axe object with the equivalent triple-underscore parameter
        // - replace all other references of "this" or "base" with "__instance"
        // - replace the "Harvest(...)" call with a loop that calls it as many times as needed for the custom speed
        // ReSharper disable All
        private class AxeHitPatch
        {
            private static bool Prefix(Axe __instance,
                ref Network_Player ___playerNetwork,
                ref HarvestableTree ___currentTreeToChop,
                ref RaycastHit ___rayHit,
                ref LayerMask ___hitmask,
                ref PlayerInventory ___playerInventory,
                ref AxeMode ___mode)
            {
                if (!___playerNetwork)
                {
                    ___playerNetwork = __instance.GetComponentInParent<Network_Player>();
                }

                if (!___playerNetwork.IsLocalPlayer)
                {
                    return false;
                }

                if (!__instance.gameObject.activeInHierarchy)
                {
                    return false;
                }

                ___currentTreeToChop = null;
                if (Helper.HitAtCursor(out ___rayHit, 5f, ___hitmask))
                {
                    if (___rayHit.transform.tag == "Tree")
                    {
                        HarvestableTree componentInParent = ___rayHit.transform.GetComponentInParent<HarvestableTree>();
                        if (componentInParent != null && !componentInParent.Depleted)
                        {
                            ___currentTreeToChop = componentInParent;
                        }
                    }

                    Message_AxeHit message_AxeHit =
                        new Message_AxeHit(Messages.AxeHit, ___playerNetwork, ___playerNetwork.steamID);
                    if (___currentTreeToChop != null)
                    {
                        message_AxeHit.treeObjectIndex = (int)___currentTreeToChop.PickupNetwork.ObjectIndex;
                        if (Semih_Network.IsHost)
                        {
                            int howOften = (int)Math.Floor(_currentToolsSpeed);
                            for (int i = 0; i < howOften; i++)
                            {
                                ___currentTreeToChop.Harvest(___playerInventory);
                            }
                        }
                    }

                    message_AxeHit.HitPoint = ___rayHit.point;
                    message_AxeHit.HitNormal = ___rayHit.normal;
                    ___playerNetwork.Inventory.RemoveDurabillityFromHotSlot(1);
                    if (Semih_Network.IsHost)
                    {
                        ___playerNetwork.Network.RPC(message_AxeHit, Target.Other, EP2PSend.k_EP2PSendReliable,
                            NetworkChannel.Channel_Game);
                        __instance.PlayEffect(___playerNetwork.transform.parent, message_AxeHit.HitPoint,
                            message_AxeHit.HitNormal);
                    }
                    else
                    {
                        ___playerNetwork.SendP2P(message_AxeHit, EP2PSend.k_EP2PSendReliable,
                            NetworkChannel.Channel_Game);
                    }

                    ___currentTreeToChop = null;
                    if (___mode == AxeMode.Chopping)
                    {
                        ___mode = AxeMode.None;
                    }

                    return false;
                }

                return false;
            }
        }
        // ReSharper restore All
    }
}