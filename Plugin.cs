using BepInEx;
using BepInEx.Configuration;
using EFT;
using System.Reflection;
using flir.enemymarkers;
using SPT.Reflection.Patching;
using UnityEngine;

namespace flir.enemymarkers
{
    [BepInPlugin("flir.enemymarkers", "Enemy Markers", Version)]
    [BepInDependency("com.SPT.core", "4.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Version = "1.3.0";

        private const string General = "2. General";
        internal static ConfigEntry<int> ScreenVisibilityRadius;
        internal static ConfigEntry<int> VisibilityDistance;
        internal static ConfigEntry<string> MarkerCharacter;
        internal static ConfigEntry<string> GhostMarkerCharacter;
        internal static ConfigEntry<bool> DisplayDistance;
        internal static ConfigEntry<bool> FoliageMasking;
        internal static ConfigEntry<bool> UseGhostMarkers;
        internal static ConfigEntry<KeyboardShortcut> ToggleDistanceKey;

        private const string MarkerScaling = "3. Marker Scaling";
        internal static ConfigEntry<bool> ScaleMarkerWithDistance;
        internal static ConfigEntry<int> FontSize;
        internal static ConfigEntry<float> MarkerBaseScale;
        internal static ConfigEntry<float> MarkerMaxScale;
        internal static ConfigEntry<float> MarkerScaleDistance;

        private const string LightSettings = "5. Light Sources";
        internal static ConfigEntry<bool> EnableLights;
        internal static ConfigEntry<float> LightIntensity;
        internal static ConfigEntry<float> LightRange;

        private const string MarkerColors = "4. Marker Colors";
        public static ConfigEntry<Color> PlayerGroupColor;
        public static ConfigEntry<Color> PmcBearColor;
        public static ConfigEntry<Color> PmcUsecColor;
        public static ConfigEntry<Color> ScavColor;
        public static ConfigEntry<Color> GuardColor;
        public static ConfigEntry<Color> BossColor;

        private void Awake()
        {
            ScreenVisibilityRadius = Config.Bind(General,
                "Screen Visibility Radius (px)",
                1024,
                new ConfigDescription("Range (pixels) from the centre of the screen within which markers are visible",
                new AcceptableValueRange<int>(128, 4096)));

            VisibilityDistance = Config.Bind(General,
                "Visibility distance (m)", 150,
                new ConfigDescription("Range (meters) within which enemies are marked",
                new AcceptableValueRange<int>(0, 300)));

            MarkerCharacter = Config.Bind(General, "Marker character", "v");

            GhostMarkerCharacter = Config.Bind(General, "Ghost marker character", "x",
                new ConfigDescription("Character displayed for ghost markers"));

            DisplayDistance = Config.Bind(General, "Display distance", false);

            FoliageMasking = Config.Bind(General, "Foliage masking", false);

            UseGhostMarkers = Config.Bind(General, "Ghost markers", true);

            ToggleDistanceKey = Config.Bind(General, "Toggle Distance Display Key",
                new KeyboardShortcut(KeyCode.U));

            ScaleMarkerWithDistance = Config.Bind(MarkerScaling, "Scale with distance", true);

            FontSize = Config.Bind(MarkerScaling, "Marker size", 64);

            MarkerBaseScale = Config.Bind(MarkerScaling, "Base scale", 0.04f);

            MarkerMaxScale = Config.Bind(MarkerScaling, "Max scale", 0.3f);

            MarkerScaleDistance = Config.Bind(MarkerScaling, "Scale reference distance (m)", 10f);

            EnableLights = Config.Bind(LightSettings, "Enable lights", false);
            LightIntensity = Config.Bind(LightSettings, "Light intensity", 2.0f);
            LightRange = Config.Bind(LightSettings, "Light range (m)", 5.0f);

            PlayerGroupColor = Config.Bind(MarkerColors, "1 Player group color", new Color(0, 1, 0));
            PmcBearColor = Config.Bind(MarkerColors, "2 Bear marker color", new Color(1, 1, 1));
            PmcUsecColor = Config.Bind(MarkerColors, "3 Usec marker color", new Color(1, 1, 0));
            ScavColor = Config.Bind(MarkerColors, "4 Scav marker color", new Color(1, 0.45f, 0.007f));
            GuardColor = Config.Bind(MarkerColors, "5 Guard marker color", new Color(0.93f, 0.51f, 0.93f));
            BossColor = Config.Bind(MarkerColors, "6 Boss marker color", new Color(1, 0, 0));

            new NewGamePatch().Enable();
        }
    }
}

internal class NewGamePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() => typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted));

    [PatchPrefix]
    public static void PatchPrefix()
    {
        EmComponent.Enable();
    }
}