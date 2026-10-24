using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace blueprinter.kestrel
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("com.nikkorap.blueprinter")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony harmony;

        private void Awake()
        {
            // Plugin startup logic 
            Log = base.Logger;
            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        }
    }
    internal class KestrelPatches
    {
        static readonly string AIRCRAFT_JSON = "kestrel";
        //buff g tolerance
        [HarmonyPatch(typeof(GLOC), "OnEnable")]
        private static class GLOCOnEnablePatch
        {
            [HarmonyPriority(Priority.Last)] //qol compat
            private static void Postfix(GLOC __instance)
            {
                if ((__instance.GetComponent<Pilot>()?.aircraft?.definition.jsonKey) != AIRCRAFT_JSON)
                    return;
                Traverse tr = Traverse.Create(__instance);
                tr.Field("bloodPumpRate").SetValue(10f);
                tr.Field("staminaRecoveryRate").SetValue(10f);
                //Resources.FindObjectsOfTypeAll<FlyByWireTuner>()?.FirstOrDefault(c => c.gameObject.scene.IsValid())?.gameObject?.SetActive(PlayerSettings.debugVis);
            }
        }
        // explosion prefab caching
        private static GameObject smallExplosion;
        private static GameObject SmallExplosion()
        {
            if (smallExplosion == null)
            {
                smallExplosion = Resources
                    .FindObjectsOfTypeAll<GameObject>()
                    .FirstOrDefault(go => go.name == "explosion_1000kg"); 

                if (smallExplosion == null)
                    Plugin.Log.LogError("explosion_1000kg not found");
            }
            return smallExplosion;
        }

        // Block pilot spawn
        [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.StartEjectionSequence))]
        internal static class AircraftSpawnEjectingPilotPatch
        {
            static void Prefix(Aircraft __instance)
            {
                if (__instance?.definition.jsonKey != AIRCRAFT_JSON) return;
                if (GameManager.IsLocalAircraft(__instance))
                {
                    SceneSingleton<CameraStateManager>.i.SetFollowingUnit(null);
                    __instance.Player.ShowMap(1f);
                }
                return;
            }
        }

        // Explode on eject
        [HarmonyPatch(typeof(Aircraft), "EjectionSequence")]
        private static class AircrafEjectionSequencePatch
        { 
            private static void Prefix(Aircraft __instance)
            {
                if (__instance?.definition.jsonKey != AIRCRAFT_JSON) return;
                __instance.pilots[0].ApplyDamage(0, 100000, 0, 0);
                if (__instance.IsLanded())
                {
                    __instance.ReturnToInventory();
                    return;
                }
                if (__instance.IsLanded()) return;

                GameObject prefab = SmallExplosion();
                if (prefab == null) return;

                GameObject gameObject = Object.Instantiate(prefab, Datum.origin);
                Quaternion rotation = FastMath.LookRotation(__instance.rb.velocity);
                gameObject.transform.SetPositionAndRotation(__instance.transform.position, rotation);
                Shockwave shock = gameObject.GetComponentInChildren<Shockwave>();
                shock.SetOwner(__instance.persistentID, 800 * 0.000001f); //800kg, yield is in KiloTons
            }
        }
        
        // block red damage effect
        [HarmonyPatch(typeof(GameplayUI), nameof(GameplayUI.FlashHurt))]
        private static class GameplayUIFlashHurtPatch
        {
            private static bool Prefix(GameplayUI __instance, ref float damage, ref float remainingHitPoints)
            {
                GameManager.GetLocalAircraft(out Aircraft aircraft);
                if (aircraft?.definition.jsonKey == AIRCRAFT_JSON) return false;
                return true;
            }
        }

        //reduce gforce damage
        [HarmonyPatch(typeof(Pilot), nameof(Pilot.TakeGForceDamage))]
        private static class PilotTakeGForceDamagePatch
        {
            private static void Prefix(Pilot __instance, ref float sqrGForces)
            {
                if ((__instance.aircraft?.definition.jsonKey) != AIRCRAFT_JSON) return;
                sqrGForces /= 10;
            }
        }
    }
}
