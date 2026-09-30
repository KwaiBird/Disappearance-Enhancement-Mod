using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Unity.Mono;
using Cinemachine;
using HarmonyLib;
using InstantHorror.Scripts;
using InstantHorror.Scripts.InteractionSystems.DialPadlock;
using InstantHorror.Scripts.InteractionSystems;
using InstantHorror.Scripts.InteractionSystems.Door;
using InstantHorror.Scripts.InteractionSystems.Lock;
using InstantHorror.Scripts.InteractionSystems.PickableItems;
using UnityEngine;
using UnityEngine.InputSystem;
using Disappearance.Shared;
using UnityEngine.SceneManagement;
using GamePlayerInput = InstantHorror.Scripts.PlayerInput;

namespace Disappearance.GameplayQoL
{
    
    internal sealed class PadlockModule : MonoBehaviour
    {
        private const string PluginId = GameplayQoLPlugin.PluginId + ".padlock";
        private BepInEx.Logging.ManualLogSource Logger => GameplayQoLPlugin.Log;
        

        private const string VillageScene = "VillageScene";
        private const float ZoomVerticalFov = 33f;
        private const float ZoomDistance = 0.65f;
        private const float FocusRadius = 0.21f;
        private const float InteractionRange = 2f;
        private const float DialRotationSeconds = 0.16f;
        private static readonly FieldInfo DialsField = AccessTools.Field(typeof(DialPadlock), "dials");
        private static readonly FieldInfo RotatingField = AccessTools.Field(typeof(Dial), "onRotate");
        private static readonly PropertyInfo StateProperty = AccessTools.Property(typeof(GamePlayerInput), "GameStateHandler");
        private static readonly FieldInfo CurrentHitItemField = AccessTools.Field(typeof(GamePlayerInput), "currentHitItem");
        private static PadlockModule instance;
        private Harmony harmony;
        private DialPadlock activeLock;
        private DialPadlock targetLock;
        private FenceDoor nearbyDoor;
        private GameObject inspectTarget;
        private ItemMarkerUI inspectionMarker;
        private Renderer[] lockRenderers;
        private Renderer[] dialRenderers;
        private Renderer[][] rowRenderers;
        private GamePlayerInput playerInput;
        private GameStateHandler playerState;
        private List<Dial> dials;
        private GameStateHandler gameState;
        private CinemachineVirtualCamera zoomCamera;
        private int selectedRow;
        private int openedFrame;
        private int stickDirection;
        private float nextStickMove;
        private bool invokingDial;
        private readonly Dictionary<Dial, int> queuedRotations = new Dictionary<Dial, int>();
        private GUIStyle markerStyle;
        private GUIStyle helpStyle;
        private Texture2D markerTexture;
        private Texture2D reverseMarkerTexture;
        private float nextTargetSearch;
        private float nextMarkerSearch;
        private bool markerVisible;
        private bool showingGamepadControls;
        private readonly ActiveControlHints controlHints = new ActiveControlHints();
        private ModalSession modal;
        private int modalToken;
        internal bool IsOpen => modalToken != 0;
        private int capturedScene;
        private GameState capturedState;

        private void Awake()
        {
            instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
            harmony = new Harmony(PluginId);
            try
            {
                harmony.Patch(AccessTools.Method(typeof(GamePlayerInput), "OnInteract"),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(PadlockModule), nameof(BeforePlayerInteract))));
                harmony.Patch(AccessTools.Method(typeof(GamePlayerInput), "ShowInteractableItemMarker"),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(PadlockModule), nameof(BeforeShowItemMarker))));
                harmony.Patch(AccessTools.Method(typeof(Dial), "Interact"),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(PadlockModule), nameof(BeforeDialInteract))));
                harmony.Patch(AccessTools.Method(typeof(Dial), "RotateDial"),
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(PadlockModule), nameof(ShortenDialAnimation))));
                harmony.Patch(AccessTools.Method(typeof(DialPadlock), "OnUnlocked"),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(PadlockModule), nameof(AfterUnlocked))));
                Logger.LogInfo("Padlock inspection target ready: up/down selects, A/E queues rotation, B/Escape exits.");
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                harmony = null;
                Logger.LogError("Could not patch padlock interaction: " + exception);
            }
        }

        private static IEnumerable<CodeInstruction> ShortenDialAnimation(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float seconds &&
                    Mathf.Abs(seconds - 0.5f) < 0.0001f)
                {
                    instruction.operand = DialRotationSeconds;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1)
                instance?.Logger.LogWarning("Expected one padlock rotation duration; found " + replaced + ".");
        }

        private static bool BeforePlayerInteract(GamePlayerInput __instance)
        {
            if (CheckpointShortcut.PadChordHeld(Gamepad.current)) return true;
            if (instance == null) return true;
            bool focused = instance.IsFocusedOnLock(__instance, out DialPadlock padlock);
            if (!focused)
                return true;
            bool opened = instance.Open(padlock, 0);
            return !opened;
        }

        private static bool BeforeShowItemMarker(GamePlayerInput __instance)
        {
            if (instance == null || !instance.IsFocusedOnLock(__instance, out _))
                return true;
            GameObject previous = CurrentHitItemField?.GetValue(__instance) as GameObject;
            InteractableItemBase marker = previous == null ? null : previous.GetComponent<InteractableItemBase>();
            if (marker != null)
                marker.OnCursorProperty.Value = InteractableItemBase.OnCursor.Exit;
            CurrentHitItemField?.SetValue(__instance, null);
            return false;
        }

        private static bool BeforeDialInteract(Dial __instance, ref bool __result)
        {
            if (instance == null || instance.invokingDial)
                return true;
            DialPadlock padlock = __instance.GetComponentInParent<DialPadlock>();
            if (padlock == null || padlock.IsUnlocked)
                return true;
            if (!instance.Open(padlock, instance.IndexOfDial(padlock, __instance)))
                return true;
            __result = true;
            return false;
        }

        private static void AfterUnlocked(DialPadlock __instance)
        {
            if (instance == null)
                return;
            if (instance.targetLock == __instance && instance.inspectTarget != null)
                instance.inspectTarget.SetActive(false);
            if (instance.targetLock == __instance)
                instance.RetireInspectionMarker();
            if (instance.activeLock == __instance)
                instance.Close(false);
        }

        private int IndexOfDial(DialPadlock padlock, Dial dial)
        {
            List<Dial> lockDials = DialsField?.GetValue(padlock) as List<Dial>;
            return lockDials == null ? 0 : Mathf.Max(0, lockDials.IndexOf(dial));
        }

        private void FindInspectionTarget()
        {
            if (SceneManager.GetActiveScene().name != VillageScene ||
                targetLock != null && playerInput != null && playerState != null)
                return;
            if (Time.unscaledTime < nextTargetSearch)
                return;
            nextTargetSearch = Time.unscaledTime + 1f;
            DialPadlock padlock = targetLock == null ? FindObjectOfType<DialPadlock>() : targetLock;
            GamePlayerInput player = playerInput == null ? FindObjectOfType<GamePlayerInput>() : playerInput;
            GameStateHandler state = player == null ? null : StateProperty?.GetValue(player) as GameStateHandler;
            if (padlock == null || player == null || state == null)
                return;
            List<Dial> lockDials = DialsField?.GetValue(padlock) as List<Dial>;
            if (lockDials == null || lockDials.Count != 4)
                return;
            foreach (Dial dial in lockDials)
                if (dial == null)
                    return;

            targetLock = padlock;
            float nearestDoorDistance = float.PositiveInfinity;
            foreach (FenceDoor door in FindObjectsOfType<FenceDoor>())
            {
                float distance = (door.transform.position - padlock.transform.position).sqrMagnitude;
                if (distance < nearestDoorDistance)
                {
                    nearestDoorDistance = distance;
                    nearbyDoor = door;
                }
            }
            playerInput = player;
            playerState = state;
            lockRenderers = padlock.GetComponentsInChildren<Renderer>();
            var dialMeshes = new List<Renderer>();
            rowRenderers = new Renderer[lockDials.Count][];
            for (int i = 0; i < lockDials.Count; i++)
            {
                rowRenderers[i] = lockDials[i].GetComponentsInChildren<Renderer>();
                dialMeshes.AddRange(rowRenderers[i]);
            }
            dialRenderers = dialMeshes.ToArray();
            if (inspectTarget == null)
            {
                GameObject target = new GameObject("Padlock inspection interaction");
                target.layer = lockDials[0].gameObject.layer;
                target.transform.position = DialCenter(lockDials);
                SphereCollider collider = target.AddComponent<SphereCollider>();
                collider.radius = FocusRadius;
                collider.isTrigger = true;
                PadlockInspectTarget interactable = target.AddComponent<PadlockInspectTarget>();
                interactable.Lock = padlock;
                inspectTarget = target;
                Logger.LogInfo("Added dedicated padlock interaction at " + target.transform.position.ToString("F3") + ".");
            }
        }

        private static Vector3 DialCenter(List<Dial> lockDials)
        {
            Vector3 center = Vector3.zero;
            foreach (Dial dial in lockDials)
                center += dial.transform.position;
            return center / lockDials.Count;
        }

        private bool IsFocusedOnLock(GamePlayerInput player, out DialPadlock padlock)
        {
            padlock = null;
            if (player == null || player != playerInput || targetLock == null || targetLock.IsUnlocked ||
                playerState == null || playerState.CurrentState.Value != GameState.PlayGame ||
                SceneManager.GetActiveScene().name != VillageScene || Time.timeScale <= 0f)
                return false;
            Camera camera = Camera.main;
            List<Dial> lockDials = DialsField?.GetValue(targetLock) as List<Dial>;
            if (camera == null || lockDials == null || lockDials.Count == 0)
                return false;
            Vector3 towardLock = DialCenter(lockDials) - camera.transform.position;
            float along = Vector3.Dot(towardLock, camera.transform.forward);
            if (along <= 0f || along > InteractionRange)
                return false;
            Vector3 radial = towardLock - camera.transform.forward * along;
            if (radial.sqrMagnitude > FocusRadius * FocusRadius)
                return false;
            padlock = targetLock;
            return true;
        }

        internal static bool RequestOpen(DialPadlock padlock)
        {
            return !CheckpointShortcut.PadChordHeld(Gamepad.current) && instance != null &&
                padlock != null && !padlock.IsUnlocked && instance.Open(padlock, 0);
        }

        private bool Open(DialPadlock padlock, int row)
        {
            if (CheckpointShortcut.PadChordHeld(Gamepad.current)) return false;
            PauseMenuHandler pause = FindObjectOfType<PauseMenuHandler>();
            if (pause != null && pause.CurrentPauseStatus == PauseMenuHandler.PauseStatus.Paused) return false;
            if (SceneManager.GetActiveScene().name != VillageScene || modalToken != 0 || activeLock != null || padlock == null ||
                padlock.IsUnlocked)
                return activeLock != null && activeLock == padlock;
            List<Dial> lockDials = DialsField?.GetValue(padlock) as List<Dial>;
            GamePlayerInput player = FindObjectOfType<GamePlayerInput>();
            GameStateHandler state = player == null ? null : StateProperty?.GetValue(player) as GameStateHandler;
            Camera main = Camera.main;
            if (lockDials == null || lockDials.Count == 0 || state == null ||
                state.CurrentState.Value != GameState.PlayGame || main == null)
                return false;
            foreach (Dial dial in lockDials)
                if (dial == null)
                    return false;

            ModalSession session = GameplayQoLPlugin.Instance?.Modal;
            int sceneHandle = SceneManager.GetActiveScene().handle;
            if (session == null || !session.TryAcquire(ModalOwner.Padlock, sceneHandle, out int token))
            {
                Logger.LogWarning("Padlock modal unavailable: sceneHandle=" + sceneHandle +
                    " owner=" + (session == null ? "missing" : session.Owner.ToString()));
                return false;
            }
            modal = session;
            modalToken = token;
            MenuInputIsolation.Transition();
            capturedScene = sceneHandle;
            capturedState = state.CurrentState.Value;

            Vector3 center = DialCenter(lockDials);
            // The lock root is stationary; individual dials rotate while the player turns them.
            // Use the lock's face axis so an oblique approach cannot skew the inspection view.
            Vector3 towardPlayer = main.transform.position - center;
            Vector3 faceNormal = padlock.transform.forward;
            if (Vector3.Dot(faceNormal, towardPlayer) < 0f)
                faceNormal = -faceNormal;
            Vector3 zoomPosition = center + faceNormal * ZoomDistance;

            GameObject cameraObject = null;
            try
            {
                cameraObject = new GameObject("Padlock inspection camera");
                cameraObject.SetActive(false);
                cameraObject.transform.SetPositionAndRotation(zoomPosition,
                    Quaternion.LookRotation(center - zoomPosition, Vector3.up));
                CinemachineVirtualCamera camera = cameraObject.AddComponent<CinemachineVirtualCamera>();
                LensSettings lens = camera.m_Lens;
                lens.FieldOfView = ZoomVerticalFov;
                camera.m_Lens = lens;
                camera.Priority = 10000;
                zoomCamera = camera;
                activeLock = padlock;
                dials = lockDials;
                gameState = state;
                selectedRow = Mathf.Clamp(row, 0, lockDials.Count - 1);
                openedFrame = Time.frameCount;
                stickDirection = 0;
                nextStickMove = 0f;
                queuedRotations.Clear();
                UpdateActiveInput();
                gameState.ChangeState(GameState.OnEvent);
                cameraObject.SetActive(true);
                modal.Activate(ModalOwner.Padlock, token);
                Logger.LogInfo("Padlock inspection opened at row " + (selectedRow + 1));
                return true;
            }
            catch (Exception exception)
            {
                Logger.LogError("Padlock inspection could not open: " + exception);
                bool cameraWasAssigned = zoomCamera != null;
                Close(true);
                if (cameraObject != null && !cameraWasAssigned) Destroy(cameraObject);
                return false;
            }
        }

        private void Update()
        {
            try { UpdateCore(); }
            catch (Exception exception)
            {
                Logger.LogError("Padlock update failed; releasing inspection: " + exception);
                Close(true);
            }
        }

        private void UpdateCore()
        {
            FindInspectionTarget();
            UpdateInspectionMarker();
            UpdateActiveInput();
            if (modalToken == 0)
                return;
            if (SceneManager.GetActiveScene().name != VillageScene || gameState == null || zoomCamera == null ||
                activeLock.IsUnlocked || gameState.CurrentState.Value != GameState.OnEvent)
            {
                Close(false);
                return;
            }
            if (Time.frameCount == openedFrame)
                return;

            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            bool exit = (gamepad != null && gamepad.buttonEast.wasPressedThisFrame) ||
                (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame));
            if (exit)
            {
                Close(true);
                return;
            }

            ServiceQueuedRotations();

            int direction = 0;
            if ((gamepad != null && gamepad.dpad.up.wasPressedThisFrame) ||
                (keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)))
                direction = -1;
            else if ((gamepad != null && gamepad.dpad.down.wasPressedThisFrame) ||
                (keyboard != null && (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)))
                direction = 1;
            if (direction == 0 && gamepad != null)
            {
                float vertical = gamepad.leftStick.ReadValue().y;
                int currentDirection = vertical > 0.55f ? -1 : vertical < -0.55f ? 1 : 0;
                if (currentDirection != 0 && (currentDirection != stickDirection || Time.unscaledTime >= nextStickMove))
                {
                    direction = currentDirection;
                    nextStickMove = Time.unscaledTime + (currentDirection == stickDirection ? 0.18f : 0.34f);
                }
                stickDirection = currentDirection;
            }
            if (direction != 0)
                selectedRow = (selectedRow + direction + dials.Count) % dials.Count;

            bool advance = ConfirmInput.WasPressedThisFrame(keyboard, gamepad);
            if (advance && selectedRow >= 0 && selectedRow < dials.Count && dials[selectedRow] != null)
                QueueRotation(dials[selectedRow]);
        }

        private void QueueRotation(Dial dial)
        {
            queuedRotations.TryGetValue(dial, out int queued);
            queuedRotations[dial] = queued + 1;
            ServiceQueuedRotations();
        }

        private void ServiceQueuedRotations()
        {
            if (dials == null || RotatingField == null)
                return;
            foreach (Dial dial in dials)
            {
                if (dial == null || !queuedRotations.TryGetValue(dial, out int queued) || queued <= 0 ||
                    (bool)RotatingField.GetValue(dial))
                    continue;
                queuedRotations[dial] = queued - 1;
                invokingDial = true;
                try { dial.Interact(null); }
                finally { invokingDial = false; }
            }
        }

        private void Close(bool resumeGame)
        {
            if (modalToken == 0)
                return;
            int token = modalToken;
            MenuInputIsolation.Transition();
            modal?.BeginClosing(ModalOwner.Padlock, token);
            try
            {
                if (zoomCamera != null) Destroy(zoomCamera.gameObject);
                if (resumeGame && SceneManager.GetActiveScene().handle == capturedScene &&
                    gameState != null && gameState.CurrentState.Value == GameState.OnEvent)
                    try { gameState.ChangeState(capturedState); }
                    catch (Exception exception) { Logger.LogError("Could not restore padlock GameState: " + exception); }
            }
            finally
            {
                activeLock = null;
                dials = null;
                gameState = null;
                zoomCamera = null;
                invokingDial = false;
                queuedRotations.Clear();
                modalToken = 0;
                capturedScene = 0;
                modal?.Release(ModalOwner.Padlock, token);
                modal = null;
                Logger.LogInfo("Padlock inspection closed.");
            }
        }

        private static bool CombinedBounds(Renderer[] renderers, out Bounds combined)
        {
            combined = default;
            bool found = false;
            if (renderers == null)
                return false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                    continue;
                if (!found)
                {
                    combined = renderer.bounds;
                    found = true;
                }
                else
                    combined.Encapsulate(renderer.bounds);
            }
            return found;
        }

        private Vector3 MarkerSurfacePosition(Camera camera)
        {
            Vector3 center = DialCenter(DialsField.GetValue(targetLock) as List<Dial>);
            // The dial group sits at the desired lock-body height. Its centre is more reliable
            // than the renderer bounds, whose top includes geometry above the visible lock.
            return center - camera.transform.right * 0.04f;
        }

        private static bool ScreenEdges(Camera camera, Renderer[] renderers, out float left, out float right)
        {
            left = float.PositiveInfinity;
            right = float.NegativeInfinity;
            if (renderers == null)
                return false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                    continue;
                Bounds bounds = renderer.bounds;
                for (int ix = -1; ix <= 1; ix += 2)
                    for (int iy = -1; iy <= 1; iy += 2)
                        for (int iz = -1; iz <= 1; iz += 2)
                        {
                            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                                new Vector3(ix, iy, iz));
                            Vector3 screen = camera.WorldToScreenPoint(corner);
                            if (screen.z <= 0f)
                                continue;
                            left = Mathf.Min(left, screen.x);
                            right = Mathf.Max(right, screen.x);
                        }
            }
            return left < right;
        }

        private void UpdateActiveInput()
        {
            showingGamepadControls = controlHints.Update(activeLock == null);
        }

        private bool TryGetSelectionLayout(out Rect marker, out Rect button, out bool onRight)
        {
            marker = button = default;
            onRight = false;
            if (activeLock == null || dials == null || selectedRow < 0 || selectedRow >= dials.Count ||
                dials[selectedRow] == null || gameState == null || gameState.CurrentState.Value != GameState.OnEvent)
                return false;
            Camera main = Camera.main;
            if (main == null || Screen.width <= 0 || Screen.height <= 0) return false;
            Renderer[] selectedRenderers = rowRenderers != null && selectedRow < rowRenderers.Length
                ? rowRenderers[selectedRow] : null;
            Vector3 rowCenter = dials[selectedRow].transform.position;
            Vector3 point = main.WorldToScreenPoint(rowCenter);
            if (point.z > 0f)
            {
                float size = Mathf.Max(44f, Screen.height / 25f);
                float buttonSize = size * 1.9f;
                float gap = size * 0.1f;
                float left = point.x;
                float right = point.x;
                if (ScreenEdges(main, selectedRenderers, out float dialLeft, out float dialRight))
                {
                    left = dialLeft;
                    right = dialRight;
                }
                float bodyLeft = left - size * 3f;
                if (ScreenEdges(main, lockRenderers, out float lockLeft, out _))
                    bodyLeft = lockLeft;
                // Place the triangle's tip in the empty lock body, well left of the printed digits.
                float arrowX = left - Mathf.Max(size * 2.5f, (left - bodyLeft) * 0.40f) - size;
                float buttonX = arrowX - gap - buttonSize;
                onRight = buttonX < size * 0.5f;
                if (onRight)
                {
                    arrowX = right + size * 2.5f;
                    buttonX = arrowX + size + gap;
                }
                else
                    arrowX += size * 0.2f;
                arrowX = Mathf.Clamp(arrowX, size * 0.5f, Screen.width - size * 1.5f);
                buttonX = Mathf.Clamp(buttonX, size * 0.5f, Screen.width - buttonSize - size * 0.5f);
                int adjacentRow = selectedRow + 1 < dials.Count ? selectedRow + 1 : selectedRow - 1;
                float rowPitch = 0f;
                if (adjacentRow >= 0 && dials[adjacentRow] != null)
                {
                    Vector3 adjacentPoint = main.WorldToScreenPoint(dials[adjacentRow].transform.position);
                    if (adjacentPoint.z > 0f)
                        rowPitch = Mathf.Abs(point.y - adjacentPoint.y);
                }
                // The dial pivot and renderer bounds both sit below the visible number band.
                // With this fixed camera, the offset tapers slightly from the top row to the bottom.
                float bandOffset = rowPitch * (0.35f - selectedRow * 0.025f);
                float y = Screen.height - point.y - bandOffset - buttonSize * 0.5f;
                button = new Rect(buttonX, y, buttonSize, buttonSize);
                marker = new Rect(arrowX, y + (buttonSize - size) * 0.5f, size, size);
                return point.x >= 0 && point.x <= Screen.width && point.y >= 0 && point.y <= Screen.height;
            }
            return false;
        }

        private void OnGUI()
        {
            if (activeLock == null)
                return;
            if (activeLock == null || dials == null || selectedRow >= dials.Count ||
                dials[selectedRow] == null || gameState == null || gameState.CurrentState.Value != GameState.OnEvent)
                return;
            Camera main = Camera.main;
            if (main == null)
                return;
            if (markerStyle == null)
            {
                markerStyle = new GUIStyle(GUI.skin.label) {
                    alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold,
                    fontSize = Mathf.Max(28, Screen.height / 30)
                };
                markerStyle.normal.textColor = Color.white;
                helpStyle = new GUIStyle(markerStyle) { alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.Max(16, Screen.height / 58) };
                JapaneseGuiFont.Apply(markerStyle);
                JapaneseGuiFont.Apply(helpStyle);
                markerTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                reverseMarkerTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                    {
                        markerTexture.SetPixel(x, y, x <= 15f - Mathf.Abs(y - 7.5f) * 1.65f ?
                            new Color(1f, 0.15f, 0.2f, 0.95f) : Color.clear);
                        reverseMarkerTexture.SetPixel(x, y, x >= Mathf.Abs(y - 7.5f) * 1.65f ?
                            new Color(1f, 0.15f, 0.2f, 0.95f) : Color.clear);
                    }
                markerTexture.Apply();
                reverseMarkerTexture.Apply();
            }
            if (TryGetSelectionLayout(out Rect marker, out Rect button, out bool onRight))
            {
                ControlHintGUI.Button(button,
                    showingGamepadControls && Gamepad.current != null
                        ? ActiveControlHints.SouthButton(Gamepad.current) : "E",
                    showingGamepadControls && Gamepad.current != null);
                GUI.DrawTexture(marker, onRight ? reverseMarkerTexture : markerTexture);
            }
            float panelWidth = Mathf.Min(Screen.width * 0.67f, 1050f);
            Rect panel = new Rect((Screen.width - panelWidth) * 0.5f,
                Screen.height - Mathf.Max(100f, Screen.height * 0.09f), panelWidth, Mathf.Max(54f, Screen.height * 0.05f));
            Color oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = oldColor;
            DrawHelpIcons(panel);
        }

        private void DrawHelpIcons(Rect panel)
        {
            bool pad = showingGamepadControls && Gamepad.current != null;
            float size = Mathf.Min(panel.height * 0.65f, 50f);
            float y = panel.y + (panel.height - size) * 0.5f;
            float first = panel.x + panel.width * 0.035f;
            float second = panel.x + panel.width * 0.37f;
            float third = panel.x + panel.width * 0.71f;
            GUIStyle label = new GUIStyle(helpStyle) { alignment = TextAnchor.MiddleLeft };
            if (pad)
            {
                ControlHintGUI.Stick(new Rect(first, y, size, size), "L");
                GUI.Label(new Rect(first + size + 8f, y, panel.width * 0.23f, size), "行選択", label);
                ControlHintGUI.Button(new Rect(second, y, size, size),
                    ActiveControlHints.SouthButton(Gamepad.current), true);
                GUI.Label(new Rect(second + size + 8f, y, panel.width * 0.23f, size), "次の数字", label);
                ControlHintGUI.Button(new Rect(third, y, size, size),
                    ActiveControlHints.EastButton(Gamepad.current), true);
                GUI.Label(new Rect(third + size + 8f, y, panel.width * 0.19f, size), "戻る", label);
            }
            else
            {
                ControlHintGUI.Button(new Rect(first, y, size, size), "W", false);
                ControlHintGUI.Button(new Rect(first + size + 3f, y, size, size), "S", false);
                GUI.Label(new Rect(first + size * 2f + 12f, y, panel.width * 0.18f, size), "行選択", label);
                ControlHintGUI.Button(new Rect(second, y, size, size), "E", false);
                ControlHintGUI.Button(new Rect(second + size + 3f, y, size * 1.6f, size),
                    "Enter", false);
                GUI.Label(new Rect(second + size * 2.6f + 11f, y,
                    panel.width * 0.19f, size), "次の数字", label);
                ControlHintGUI.Button(new Rect(third, y, size * 1.3f, size), "Esc", false);
                GUI.Label(new Rect(third + size * 1.3f + 8f, y, panel.width * 0.15f, size), "戻る", label);
            }
        }

        private void UpdateInspectionMarker()
        {
            if (inspectTarget == null || targetLock == null || targetLock.IsUnlocked)
            {
                RetireInspectionMarker();
                return;
            }
            if (inspectionMarker == null && Time.unscaledTime >= nextMarkerSearch)
            {
                nextMarkerSearch = Time.unscaledTime + 1f;
                ItemMarkerUIHandler handler = FindObjectOfType<ItemMarkerUIHandler>();
                if (handler != null)
                {
                    GameObject markerPoint = new GameObject("Padlock marker point");
                    markerPoint.transform.SetParent(inspectTarget.transform, false);
                    markerPoint.transform.localPosition = Vector3.zero;
                    inspectionMarker = handler.InstantiateItemMarkerUI(markerPoint.transform, inspectTarget.transform);
                    inspectionMarker.ShowInteractableMarker();
                    inspectionMarker.SetCanvasActive(false);
                    markerVisible = false;
                    Logger.LogInfo("Using the game's item marker UI for the padlock.");
                }
            }
            if (inspectionMarker == null)
                return;
            Camera camera = Camera.main;
            if (camera != null)
                inspectionMarker.transform.position = MarkerSurfacePosition(camera);
            bool show = activeLock == null && playerState != null &&
                playerState.CurrentState.Value == GameState.PlayGame && camera != null &&
                nearbyDoor != null && nearbyDoor.ItemMarkerUI != null &&
                (inspectionMarker.transform.position - camera.transform.position).sqrMagnitude <= 9f;
            SetInspectionMarkerVisible(show);
            if (show)
            {
                if (IsFocusedOnLock(playerInput, out _))
                    inspectionMarker.ShowMouseMarker();
                else
                    inspectionMarker.ShowInteractableMarker();
            }
        }

        private void RetireInspectionMarker()
        {
            if (inspectionMarker != null)
            {
                inspectionMarker.SetCanvasActive(false);
                inspectionMarker.DestroyItemMarkerUI();
                inspectionMarker = null;
            }
            markerVisible = false;
        }

        private void SetInspectionMarkerVisible(bool visible)
        {
            if (inspectionMarker == null || markerVisible == visible)
                return;
            inspectionMarker.SetCanvasActive(visible);
            markerVisible = visible;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Close(false);
            if (inspectTarget != null)
                Destroy(inspectTarget);
            inspectTarget = null;
            inspectionMarker = null;
            lockRenderers = null;
            dialRenderers = null;
            rowRenderers = null;
            markerVisible = false;
            nextMarkerSearch = 0f;
            targetLock = null;
            nearbyDoor = null;
            playerInput = null;
            playerState = null;
            nextTargetSearch = 0f;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Close(true);
            if (inspectTarget != null)
                Destroy(inspectTarget);
            if (harmony != null)
                harmony.UnpatchSelf();
            if (markerTexture != null)
                Destroy(markerTexture);
            if (reverseMarkerTexture != null)
                Destroy(reverseMarkerTexture);
            ControlHintGUI.Dispose();
            ControlGlyphs.DisposeSprites();
            instance = null;
        }
    }

    public sealed class PadlockInspectTarget : MonoBehaviour, IInteractable
    {
        public DialPadlock Lock { get; set; }
        public string InteractionPrompt => "Inspect padlock";

        public bool Interact(GamePlayerInput interactor)
        {
            return Lock != null && PadlockModule.RequestOpen(Lock);
        }
    }
}
