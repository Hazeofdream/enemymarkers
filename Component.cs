using System;
using UnityEngine; // MonoBehaviour
using Comfort.Common; // Singleton
using EFT; // AbstractBaseGame
using BepInEx; // UnityInput
using BepInEx.Configuration; // KeyboardShortcut
using BepInEx.Logging; // ManualLogSource

using System.Collections; // IEnumerator
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UI; // CanvasScaler

namespace flir.enemymarkers
{
    public class PlayerInfo
    {
        public GameObject Marker;
        public TextMesh TextMesh;
        public Light Light;
        public IPlayer Player; // Reference to player for color validation
        public Color OriginalColor; // Cached original color to prevent color changes
        public bool ColorValidated; // Track if color has been validated with fully loaded player data
        public DateTime LastObserved;
        public Vector3 LastKnownPosition;
        public bool IsGhostMode;
        public bool HadLineOfSight;
        public float LastDisplayedDistance;
        public bool LastDisplayedDistanceEnabled;
        public bool LastDisplayedGhostMode;
        public bool MarkerActive;

        public PlayerInfo(DateTime lastObserved, GameObject marker, TextMesh textMesh, Light light, Color originalColor, IPlayer player)
        {
            LastObserved = lastObserved;
            Marker = marker;
            TextMesh = textMesh;
            Light = light;
            Player = player;
            OriginalColor = originalColor;
            ColorValidated = false; // Will validate on first visibility
            LastKnownPosition = Vector3.zero;
            IsGhostMode = false;
            HadLineOfSight = false;
            LastDisplayedDistance = -1f; // -1 indicates never displayed
            LastDisplayedDistanceEnabled = false;
            LastDisplayedGhostMode = false;
            MarkerActive = false; // Markers start inactive
        }
    }

    public class EmComponent : MonoBehaviour
    {
        internal static ManualLogSource Logger;

        // Cache body part types to avoid Enum.GetValues() + LINQ allocation every frame
        private static readonly BodyPartType[] BodyParts = (BodyPartType[])Enum.GetValues(typeof(BodyPartType));

        private GameWorld _gameWorld;
        private Camera _mainCamera;

        // Config value cache invalidation flag
        private bool _configChanged = true;

        private readonly Vector2 _center = new Vector2(0.5f, 0.5f);
        private const string CanvasName = "flir.enemymarker.Canvas";
        private GameObject _canvasGo;
        private bool _updateMarkersRunning;
        private bool _stopMarkerUpdateCoroutine;
        private bool _resetMarkersUpdateCoroutine;
        private Dictionary<IPlayer, PlayerInfo> _markersDict = new Dictionary<IPlayer, PlayerInfo>();
        private readonly DateTime _longAgo = new DateTime(1999,01,01);
        private TimeSpan _allowedTimeSpan = TimeSpan.FromSeconds(5);

        private void Awake()
        {
            if (Logger == null)
                Logger = BepInEx.Logging.Logger.CreateLogSource(nameof(EmComponent));
        }

        public static void Enable()
        {
            if (!Singleton<AbstractGame>.Instantiated)
            {
                Logger.LogError("AbstractGame was not instantiated, cannot enable.");
                return;
            }

            var gw = Singleton<GameWorld>.Instance;
            gw.gameObject.AddComponent<EmComponent>();
            Logger.LogInfo("Component Added to GameWorld");
        }

        private void Start()
        {
            // get the gameworld reference in a non-static place
            _gameWorld = Singleton<GameWorld>.Instance;

            //add component to gameWorld
            Logger.LogInfo("Starting Component");
#if DEBUG
            Logger.LogInfo("Starting component in Debug mode");
#endif
            RegisterOnPersonAdd();
            RegisterOnResolutionChange();
            RegisterConfigChangeHandlers();

            _canvasGo = CreateCanvasObject();
            _updateMarkersRunning = false;

            InitializeMarkers();
        }

        private void RegisterConfigChangeHandlers()
        {
            Plugin.UseGhostMarkers.SettingChanged += (_, __) => _configChanged = true;
            Plugin.ScreenVisibilityRadius.SettingChanged += (_, __) => _configChanged = true;
            Plugin.VisibilityDistance.SettingChanged += (_, __) => _configChanged = true;
            Plugin.ScaleMarkerWithDistance.SettingChanged += (_, __) => _configChanged = true;
            Plugin.MarkerScaleDistance.SettingChanged += (_, __) => _configChanged = true;
            Plugin.MarkerBaseScale.SettingChanged += (_, __) => _configChanged = true;
            Plugin.MarkerMaxScale.SettingChanged += (_, __) => _configChanged = true;
            Plugin.MarkerCharacter.SettingChanged += (_, __) => _configChanged = true;
            Plugin.GhostMarkerCharacter.SettingChanged += (_, __) => _configChanged = true;
            Plugin.DisplayDistance.SettingChanged += (_, __) => _configChanged = true;
            Plugin.FoliageMasking.SettingChanged += (_, __) => _configChanged = true;
        }

        private void OnDestroy()
        {
            Logger?.LogInfo("EmComponent is being destroyed");
            // Stop any co-routine that would still be running
            _stopMarkerUpdateCoroutine = true;
            // deregister our handler
            try
            {
                _gameWorld.OnPersonAdd -= OnPersonAdd;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"deregister OnPersonAdd failed with exception: {ex.Message}\nStack: {ex.StackTrace}");
            }

            try
            {
                GClass3825.OnResolutionChanged -= OnResolutionChange;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"deregister OnResolutionChanged failed with exception: {ex.Message}\nStack: {ex.StackTrace}");
            }

            // cleanup all remaining players OnPlayerDeadOrUnspawn handler
            foreach (var iPlayer in _markersDict.Keys)
            {
                if (!(iPlayer is Player player)) continue;

                try
                {
                    player.OnPlayerDeadOrUnspawn -= PlayerDeadOrUnspawn;
                    Logger?.LogInfo($"deregister OnPlayerDeadOrUnspawn for {player.name} ...");
                }
                catch (Exception e)
                {
                    Logger?.LogError($"deregister OnPlayerDeadOrUnspawn failed with exception for player: {player.name}: {e.Message}\nStack: {e.StackTrace}");
                    // no pb continue to next player
                }
            }

            // drop all markers
            _markersDict.Clear();
            // clear static references to avoid memory leaks
            Destroy(_canvasGo);
            _gameWorld = null;

            Logger?.LogInfo("EmComponent as been cleaned-up");

            // Remove the log source if it was created
            Logger?.Dispose();
            Logger = null;
        }

        private void Update()
        {
            // Always enabled

            if (IsKeyPressed(Plugin.ToggleDistanceKey.Value))
            {
                Plugin.DisplayDistance.Value = !Plugin.DisplayDistance.Value;
            }

            if (_updateMarkersRunning || _stopMarkerUpdateCoroutine) return;

            _updateMarkersRunning = true;
            StartCoroutine(UpdateMarkers());
        }

        bool IsKeyPressed(KeyboardShortcut key)
        {
            return UnityInput.Current.GetKeyDown(key.MainKey) &&
                   key.Modifiers.All(modifier => UnityInput.Current.GetKey(modifier));
        }

        private void RegisterOnPersonAdd()
        {
            #if DEBUG
            Logger.LogInfo("Register OnPersonAdd event");
            #endif
            // receive event each time a "person" is added in the game world
            _gameWorld.OnPersonAdd += OnPersonAdd;
        }

        private void RegisterOnResolutionChange()
        {
            GClass3825.OnResolutionChanged += OnResolutionChange;
        }

        private void OnResolutionChange()
        {
            if (!GameObject.Find(CanvasName))
            {
                Logger.LogError("markers Canvas not found!");
                return;
            }

            // Update canvas scaler with new resolution
            var canvasScaler = _canvasGo.GetComponent<CanvasScaler>();
            if (canvasScaler)
            {
                canvasScaler.referenceResolution = Utils.ScreenResolution();
            }

            // force co-routine to restart
            // to recalculate normalized values and super sampling factor
            _resetMarkersUpdateCoroutine = true;
        }

        // OnPersonAdd event handler to receive new players notifications
        private void OnPersonAdd(IPlayer iPlayer)
        {
            if (!GameUtils.IsMainPlayerScav())
                return;

            if (!(iPlayer is Player player) || player.IsHeadlessClient())
                return;

            if (player.IsPMC())
                return;

            player.OnIPlayerDeadOrUnspawn += PlayerDeadOrUnspawn;

            var (marker, textMesh, light, color) = CreateMarker(player);
            _markersDict.Add(player, new PlayerInfo(_longAgo, marker, textMesh, light, color, player));
        }

        // PlayerDeadOrUnspawn cleans up our markers on player death or unspawn
        private void PlayerDeadOrUnspawn(IPlayer iPlayer)
        {
#if DEBUG
            Logger.LogInfo("OnIPlayerDeadOrUnspawn event fired"); 
#endif
            if (!(iPlayer is Player player) || player.IsHeadlessClient()) return;
#if DEBUG
            Logger.LogInfo("Player died or unspawned: "+ player.Id);
#endif
            // no need for this handler anymore
            iPlayer.OnIPlayerDeadOrUnspawn -= PlayerDeadOrUnspawn;
            _markersDict[player].Marker.SetActive(false);
            Destroy(_markersDict[player].Marker);
            _markersDict.Remove(player);
        }

        private (GameObject, TextMesh, Light, Color) CreateMarker(Player target)
        {
            var markerObject = new GameObject($"marker:{target.name}");

            // Use 3D world space instead of UI canvas
            // This lets Unity's camera system handle all rendering (scopes, red dots, etc.)
            var textMesh = markerObject.AddComponent<TextMesh>();

            textMesh.text = $"{Plugin.MarkerCharacter.Value}";
            textMesh.fontSize = Plugin.FontSize.Value;
            // Anchor at bottom center so the marker sits on top of the head
            textMesh.anchor = TextAnchor.LowerCenter;
            textMesh.alignment = TextAlignment.Center;

            var color = Utils.MarkerColor(target);
            textMesh.color = color;

            // Add billboard component to make text always face the camera
            markerObject.AddComponent<Billboard>();

            // Add light component if enabled
            Light light = null;
            if (Plugin.EnableLights.Value)
            {
                light = markerObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.intensity = Plugin.LightIntensity.Value;
                light.range = Plugin.LightRange.Value;
                light.renderMode = LightRenderMode.ForcePixel;
                // Disable shadows for performance
                light.shadows = LightShadows.None;
            }

            // Scale the 3D text appropriately (TextMesh uses different units than UI Text)
            // 0.01 is a good starting scale for TextMesh to be visible but not huge
            markerObject.transform.localScale = Vector3.one * 0.01f;

            // by default, we create inactive markers and if
            // the player sees this enemy, then we set it active
            markerObject.SetActive(false);

            return (markerObject, textMesh, light, color);
        }

        private void SetMarkerGhostMode(PlayerInfo playerInfo, IPlayer target, bool isGhost)
        {
            if (!playerInfo.TextMesh) return;

            Color markerColor;
            if (isGhost)
            {
                // Ghost mode: gray color with 70% opacity for visibility
                markerColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
                playerInfo.TextMesh.color = markerColor;
            }
            else
            {
                // Normal mode: restore cached original color with full opacity
                // Use cached color to prevent color changes due to dynamic game state
                markerColor = new Color(playerInfo.OriginalColor.r, playerInfo.OriginalColor.g, playerInfo.OriginalColor.b, 1.0f);
                playerInfo.TextMesh.color = markerColor;
            }

            // Update light color if it exists
            if (playerInfo.Light)
            {
                playerInfo.Light.color = markerColor;
            }

            // Note: Text content (character and distance) is handled separately in the main update loop
        }

        // Billboard component to make 3D markers always face the camera
        private class Billboard : MonoBehaviour
        {
            private Camera _mainCamera;

            private void Start()
            {
                _mainCamera = Camera.main;
            }

            private void LateUpdate()
            {
                if (_mainCamera)
                {
                    // Make the marker face the camera
                    transform.rotation = _mainCamera.transform.rotation;
                }
            }
        }

        private IEnumerator UpdateMarkers()
        {
            #if DEBUG
            Logger.LogInfo("Starting UpdateMarkers co-routine");
            #endif

            // Cache camera reference (Camera.main is slow as it searches all cameras)
            if (!_mainCamera)
            {
                _mainCamera = Camera.main;
            }
            var cam = _mainCamera;

            var res = Utils.ScreenResolution();
            var screenDimension = Mathf.Min(res.x, res.y);

            // Config value cache (updated when _configChanged flag is set)
            bool enabledPlugin = true;
            bool useGhostMarkers = true;
            int screenVisibilityRadius = 1024;
            int visibilityDistance = 150;
            bool scaleMarkerWithDistance = true;
            float markerScaleDistance = 10f;
            float markerBaseScale = 0.04f;
            float markerMaxScale = 0.3f;
            string markerCharacter = "v";
            string ghostMarkerCharacter = "x";
            bool displayDistance = false;
            bool foliageMasking = false;

            while (true) {
                // Only refresh cache when config values have changed
                if (_configChanged)
                {
                    useGhostMarkers = Plugin.UseGhostMarkers.Value;
                    screenVisibilityRadius = Plugin.ScreenVisibilityRadius.Value;
                    visibilityDistance = Plugin.VisibilityDistance.Value;
                    scaleMarkerWithDistance = Plugin.ScaleMarkerWithDistance.Value;
                    markerScaleDistance = Plugin.MarkerScaleDistance.Value;
                    markerBaseScale = Plugin.MarkerBaseScale.Value;
                    markerMaxScale = Plugin.MarkerMaxScale.Value;
                    markerCharacter = Plugin.MarkerCharacter.Value;
                    ghostMarkerCharacter = Plugin.GhostMarkerCharacter.Value;
                    displayDistance = Plugin.DisplayDistance.Value;
                    foliageMasking = Plugin.FoliageMasking.Value;

                    // Handle mid-raid toggle: if option was just enabled during Scav run, hide PMC markers
                    if (GameUtils.IsMainPlayerScav())
                    {
                        // Hide all PMC markers
                        foreach (var kvp in _markersDict)
                        {
                            if (kvp.Key.IsPMC() && kvp.Value.Marker)
                            {
                                kvp.Value.Marker.SetActive(false);
                                kvp.Value.MarkerActive = false;
                            }
                        }
                    }

                    // config changes have been taken into account. Set the flag
                    // to false again.
                    _configChanged = false;
                }

                // if plugin is disabled get out of this coroutine
                if (!enabledPlugin) break;

                if (_stopMarkerUpdateCoroutine || _resetMarkersUpdateCoroutine)
                {
                    _resetMarkersUpdateCoroutine = false;
                    break;
                }

                if (_gameWorld.RegisteredPlayers.Count <= 1)
                {
                    #if DEBUG
                    Logger.LogInfo("UpdateMarkers: registered players count is less or equal to 1! Breaking out");
                    #endif

                    break;
                }

                if (_markersDict.Count == 0)
                {
                    #if DEBUG
                    Logger.LogInfo("UpdateMarkers: marker count is 0! Breaking out");
                    #endif
                    break;
                }
                if (!cam) break;

                var localNow = DateTime.Now;

                // Cache line-of-sight parameters outside enemy loop to avoid repeated access
                var playerHeadPos = _gameWorld.MainPlayer.MainParts[BodyPartType.head].Position;
                var layerMask = LayerMaskClass.HighPolyWithTerrainMask;
                if (foliageMasking)
                {
                    layerMask |= LayerMaskClass.Foliage;
                }

                foreach(var p in _markersDict.Keys) {
                    if (!(p is Player player)) break;

                    var visible = false;
                    // no LINQ for performance reasons
                    foreach (var part in BodyParts)
                    {
                        if (!Utils.IsLineOfSight(playerHeadPos, player.MainParts[part].Position, layerMask)) continue;
                        visible = true;
                        break; // Early exit - no need to check remaining body parts
                    }

                    var playerInfo = _markersDict[p];
                    Vector3 topPosition;
                    var markerExpired = false;

                    if (useGhostMarkers)
                    {
                        // Ghost marker mode: show faded marker at last known position
                        if (!visible)
                        {
                            // Lost line of sight
                            if (playerInfo.HadLineOfSight && !playerInfo.IsGhostMode)
                            {
                                // Transition to ghost mode: save last known position
                                playerInfo.IsGhostMode = true;
                                playerInfo.LastKnownPosition = player.MainParts[BodyPartType.head].Position + Vector3.up * 0.3f;
                                SetMarkerGhostMode(playerInfo, p, true);
                            }

                            // Check if ghost marker has expired (5 seconds)
                            var ts = localNow - playerInfo.LastObserved;
                            if (ts > _allowedTimeSpan)
                            {
                                // Ghost marker expired, mark for cleanup
                                markerExpired = true;
                                playerInfo.IsGhostMode = false;
                                playerInfo.HadLineOfSight = false;
                            }

                            // Use last known position for ghost marker
                            topPosition = playerInfo.LastKnownPosition;
                        }
                        else
                        {
                            // Has line of sight
                            if (playerInfo.IsGhostMode)
                            {
                                // Exiting ghost mode: return to normal
                                playerInfo.IsGhostMode = false;
                                SetMarkerGhostMode(playerInfo, p, false);
                            }

                            // Update tracking
                            playerInfo.HadLineOfSight = true;
                            playerInfo.LastObserved = localNow;

                            // Use current position
                            topPosition = player.MainParts[BodyPartType.head].Position + Vector3.up * 0.3f;
                        }
                    }
                    else
                    {
                        // Original behavior: active tracking behind walls for 5 seconds
                        if (!visible)
                        {
                            var ts = localNow - playerInfo.LastObserved;
                            if (ts > _allowedTimeSpan)
                            {
                                // Marker expired, mark for cleanup
                                markerExpired = true;
                            }
                        }
                        else
                        {
                            // Update last observed if still visible
                            playerInfo.LastObserved = localNow;
                        }

                        // Always use current position (active tracking)
                        topPosition = player.MainParts[BodyPartType.head].Position + Vector3.up * 0.3f;
                    }

                    // adjust height so the marker is not in the feet of the person

                    // Use WorldToViewportPoint with main camera for consistent coordinates
                    var viewportPosition = cam.WorldToViewportPoint(topPosition);

                    // Calculate distance to marker position (not player position)
                    // This ensures ghost markers show distance to last known position
                    var targetDistance = Vector3.Distance(cam.transform.position, topPosition);

                    // Determine if marker should be visible based on all constraints
                    // Including expiration, toggle state, viewport, radius, distance, and Scav run filter
                    var isScavPlayer = GameUtils.IsMainPlayerScav();

                    var shouldBeVisible = isScavPlayer &&
                                         !markerExpired &&
                                         true &&
                                         Utils.IsInViewport(viewportPosition) &&
                                         (new Vector2(viewportPosition.x, viewportPosition.y) - _center).magnitude < (screenVisibilityRadius / screenDimension) &&
                                         targetDistance < visibilityDistance &&
                                         !player.IsPMC();

                    // Only call SetActive if state actually changed (avoid unnecessary Unity overhead)
                    if (shouldBeVisible != playerInfo.MarkerActive)
                    {
                        playerInfo.Marker.SetActive(shouldBeVisible);
                        playerInfo.MarkerActive = shouldBeVisible;

                        // When marker becomes visible for first time, validate and update color
                        if (shouldBeVisible && playerInfo.TextMesh)
                        {
                            // Validate color on first visibility - player data may have been incomplete at creation
                            if (!playerInfo.ColorValidated)
                            {
                                var validatedColor = Utils.MarkerColor(playerInfo.Player);
                                playerInfo.OriginalColor = validatedColor;
                                playerInfo.ColorValidated = true;
                            }

                            // Apply the (potentially updated) color
                            // This also fixes Unity TextMesh not applying color when GameObject is inactive
                            playerInfo.TextMesh.color = playerInfo.OriginalColor;
                            if (playerInfo.Light)
                            {
                                playerInfo.Light.color = playerInfo.OriginalColor;
                            }
                        }
                    }

                    // If marker not visible, skip transform updates
                    if (!shouldBeVisible)
                    {
                        continue;
                    }

                    // Simply position the 3D marker in world space
                    // Unity's camera system handles all rendering (scopes, DLSS, etc.)
                    playerInfo.Marker.transform.position = topPosition;

                    // Scale markers based on distance to compensate for perspective
                    // 3D objects naturally shrink with distance, so we scale UP to keep them readable
                    float finalScale;
                    if (scaleMarkerWithDistance)
                    {
                        // Calculate scale: grows linearly with distance
                        var distanceScale = markerBaseScale * (targetDistance / markerScaleDistance);
                        finalScale = Mathf.Min(distanceScale, markerMaxScale);
                    }
                    else
                    {
                        // Fixed scale when distance scaling is disabled
                        finalScale = markerBaseScale;
                    }

                    playerInfo.Marker.transform.localScale = Vector3.one * finalScale;

                    // Update marker text only when distance changes significantly or display mode changes
                    if (!playerInfo.TextMesh) continue;

                    // Use appropriate marker character based on ghost mode
                    var markerChar = playerInfo.IsGhostMode ? ghostMarkerCharacter : markerCharacter;

                    // Round distance to 1 decimal place for comparison
                    var roundedDistance = Mathf.Round(targetDistance * 10f) / 10f;

                    // Only update text if ghost mode, display mode, or distance changed
                    var distanceChanged = Mathf.Abs(roundedDistance - playerInfo.LastDisplayedDistance) >= 0.1f;
                    var displayModeChanged = displayDistance != playerInfo.LastDisplayedDistanceEnabled;
                    var ghostModeChanged = playerInfo.IsGhostMode != playerInfo.LastDisplayedGhostMode;

                    if (!distanceChanged && !displayModeChanged && !ghostModeChanged) continue;
                    playerInfo.TextMesh.text = displayDistance ? $"{roundedDistance:F1}m\n{markerChar}" : markerChar;

                    // Update cached values
                    playerInfo.LastDisplayedDistance = roundedDistance;
                    playerInfo.LastDisplayedDistanceEnabled = displayDistance;
                    playerInfo.LastDisplayedGhostMode = playerInfo.IsGhostMode;
                }
                yield return Task.Yield();
            }
            // update the flag to let Update know we are not running anymore
            _updateMarkersRunning = false;
        }

        private void InitializeMarkers()
        {
            if (!GameUtils.IsInRaid())
            {
                Logger.LogError("InitializeMarkers: Raid is not in raid!!!");
                return;
            }

            _markersDict.Clear();

            // add all players that have spawned already in raid
            foreach (var player in _gameWorld.AllAlivePlayersList.Where(player => !player.IsYourPlayer))
            {
                if (!GameUtils.IsMainPlayerScav())
                    continue;

                if (player.IsHeadlessClient()) continue;
                if (player.IsPMC()) continue;

                player.OnIPlayerDeadOrUnspawn += PlayerDeadOrUnspawn;
                var (marker, textMesh, light, color) = CreateMarker(player);
                _markersDict.Add(player, new PlayerInfo(_longAgo, marker, textMesh, light, color, player));
            }
        }

        private GameObject CreateCanvasObject()
        {
            if (GameObject.Find(CanvasName))
            {
                Logger.LogInfo("Canvas GameObject already exists! returning existing GameObject");
                return GameObject.Find(CanvasName);
            }

            _canvasGo = new GameObject(CanvasName);
            if (!_canvasGo.transform)
            {
                Logger.LogError("_canvasGo.transform is null!");
            }

            if (!gameObject)
            {
                Logger.LogError("parent gameObject is null!!");
            }
            
            try
            {
                // set parent as root gameObject.transform
                _canvasGo.transform.SetParent(gameObject.transform, false);
            }
            catch (Exception e)
            {
                Logger.LogError($"Failed to set canvasGo transform Parent: {e.Message}, {e.StackTrace}");
            }

            var canvas = _canvasGo.GetOrAddComponent<Canvas>();
            // make sure we have less priority that other UI elements
            canvas.sortingOrder = 0;
            var canvasScaler = _canvasGo.GetOrAddComponent<CanvasScaler>();
            _canvasGo.GetOrAddComponent<GraphicRaycaster>();

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            canvasScaler.referenceResolution = Utils.ScreenResolution();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight = 0.5f; // Balanced scaling

            return _canvasGo;
        }
    }
};
