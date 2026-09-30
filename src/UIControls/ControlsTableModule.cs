using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Disappearance.Shared;

namespace Disappearance.UIControls
{
    internal sealed class ControlsTableModule
    {
        private static ControlsTableModule current;
        private readonly ActionRegistry registry;
        private readonly ManualLogSource log;
        private readonly List<GameObject> roots = new List<GameObject>();
        private Harmony harmony;
        private float nextLoadingScan;
        private int drawnRevision = -1;
        private Gamepad drawnGamepad;

        internal ControlsTableModule(ActionRegistry registry, ManualLogSource log)
        {
            this.registry = registry;
            this.log = log;
        }

        internal void Start()
        {
            if (harmony != null) return;
            current = this;
            harmony = new Harmony("local.disappearance.uicontrols.controlstable");
            harmony.Patch(AccessTools.Method(typeof(PauseMenuUIHandler), "Open"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ControlsTableModule), nameof(AfterPauseOpen))));
        }

        private static void AfterPauseOpen(PauseMenuUIHandler __instance)
        {
            try { current?.AttachPause(__instance); }
            catch (Exception ex) { current?.log.LogError("Pause controls table unavailable: " + ex); }
        }

        internal void Update()
        {
            if (registry.TableRevision != drawnRevision || Gamepad.current != drawnGamepad)
            {
                drawnRevision = registry.TableRevision;
                drawnGamepad = Gamepad.current;
                GameObject[] previous = roots.ToArray();
                foreach (GameObject root in previous)
                    if (root != null && root.transform.parent != null &&
                        root.transform.parent.gameObject.activeInHierarchy)
                    {
                        Image panel = root.transform.parent.GetComponent<Image>();
                        TMP_Text source = FindSource(panel);
                        if (panel != null && source != null)
                            Attach(panel, source, panel.gameObject.scene.name == "OpeningTextScene" ? "loading" : "pause");
                    }
            }
            if (SceneManager.GetActiveScene().name != "OpeningTextScene" ||
                Time.unscaledTime < nextLoadingScan) return;
            nextLoadingScan = Time.unscaledTime + 0.5f;
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
                if (image != null && image.gameObject.scene.IsValid() &&
                    image.gameObject.scene.name == "OpeningTextScene" && image.sprite != null &&
                    image.sprite.name == "pause" && image.transform.parent != null &&
                    image.transform.parent.name == "LoadingPanel")
                {
                    if (HasRoot(image)) return;
                    TMP_Text source = FindSource(image);
                    if (source != null) Attach(image, source, "loading");
                    return;
                }
        }

        private void AttachPause(PauseMenuUIHandler menu)
        {
            if (menu == null) return;
            Image panel = null;
            foreach (Image image in menu.GetComponentsInChildren<Image>(true))
                if (image.sprite != null && image.sprite.name == "pause") { panel = image; break; }
            if (panel == null) return;
            TMP_Text source = FindSource(panel);
            if (source != null) Attach(panel, source, "pause");
        }

        private static TMP_Text FindSource(Image panel)
        {
            if (panel == null) return null;
            if (panel.gameObject.scene.name == "OpeningTextScene")
            {
                foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
                    if (text != null && text.gameObject.scene == panel.gameObject.scene && text.name == "Text")
                        return text;
                return null;
            }
            PauseMenuUIHandler menu = panel.GetComponentInParent<PauseMenuUIHandler>();
            if (menu == null) return null;
            foreach (TMP_Text text in menu.GetComponentsInChildren<TMP_Text>(true))
                if (text.text.Contains("再開")) return text;
            return null;
        }

        private bool HasRoot(Image panel)
        {
            foreach (GameObject root in roots)
                if (root != null && root.transform.parent == panel.transform) return true;
            return false;
        }

        private void Attach(Image panel, TMP_Text source, string context)
        {
            for (int i = roots.Count - 1; i >= 0; i--)
                if (roots[i] == null) roots.RemoveAt(i);
                else if (roots[i].transform.parent == panel.transform)
                {
                    roots[i].SetActive(false);
                    UnityEngine.Object.Destroy(roots[i]);
                    roots.RemoveAt(i);
                }
            RectTransform root = ControlHintGraphics.Rect(panel.rectTransform, "ControlsTableOverlay", 0, 0, 1920, 1080);
            roots.Add(root.gameObject);
            try
            {
                List<ActionDefinition> actions = registry.TableActions(context);
                DrawCard(root, source, actions, false, -440);
                DrawCard(root, source, actions, true, 440);
            }
            catch
            {
                root.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(root.gameObject);
                roots.Remove(root.gameObject);
                throw;
            }
        }

        private static void DrawCard(Transform parent, TMP_Text source,
            List<ActionDefinition> actions, bool controller, float x)
        {
            RectTransform card = ControlHintGraphics.Rect(parent,
                controller ? "ControllerCard" : "KeyboardCard", x, 64, 830, 744);
            Image background = card.gameObject.AddComponent<Image>();
            background.color = Color.black;
            background.raycastTarget = false;
            Line(card, -414, 0, 2, 744); Line(card, 414, 0, 2, 744);
            Line(card, 0, 371, 830, 2); Line(card, 0, -371, 830, 2);
            ControlHintGraphics.Text(source, card, "Title", controller ? "コントローラー" : "キーボード・マウス",
                0, 306, 750, 58, 34, TextAlignmentOptions.Center);
            float rowHeight = actions.Count == 0 ? 76 : Mathf.Min(76f, 560f / actions.Count);
            float textSize = actions.Count > 12 ? 21 : actions.Count > 9 ? 24 : 27;
            for (int i = 0; i < actions.Count; i++)
            {
                ActionDefinition action = actions[i];
                float y = 230 - rowHeight * i;
                DrawBinding(source, card, controller ? action.Gamepad : action.KeyboardMouse, controller, y, rowHeight);
                float height = Mathf.Min(54f, rowHeight);
                string label = action.Label + (action.HoldSeconds > 0 ? "（" + action.HoldSeconds.ToString("0.#") + "秒長押し）" : "");
                ControlHintGraphics.Text(source, card, "Action", label, 230, y, 350, height,
                    textSize, TextAlignmentOptions.MidlineLeft);
            }
        }

        private static void Line(Transform parent, float x, float y, float width, float height)
        {
            RectTransform rect = ControlHintGraphics.Rect(parent, "Frame", x, y, width, height);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = ControlHintGraphics.Ink;
            image.raycastTarget = false;
        }

        private sealed class GlyphPart
        {
            internal string Label, Path;
            internal bool Separator, Mouse;
            internal float Width;
        }

        private static void DrawBinding(TMP_Text source, Transform parent, ActionBinding[] bindings,
            bool controller, float y, float rowHeight)
        {
            if (bindings == null || bindings.Length == 0) return;
            float nominalHeight = Mathf.Min(50f, rowHeight * 0.75f);
            float unit = nominalHeight / 50f;
            var parts = new List<GlyphPart>();
            foreach (ActionBinding binding in bindings)
            {
                if (parts.Count > 0) parts.Add(new GlyphPart { Label = "/", Separator = true, Width = 22 * unit });
                for (int i = 0; i < binding.Controls.Length; i++)
                {
                    if (i > 0 && binding.Mode == "chord")
                        parts.Add(new GlyphPart { Label = "+", Separator = true, Width = 22 * unit });
                    string path = binding.Controls[i];
                    string label = ControlGlyphRenderer.Label(path, controller);
                    bool mouse = path == "<Mouse>/delta";
                    float width = ControlGlyphRenderer.Width(nominalHeight, path);
                    parts.Add(new GlyphPart { Label = label, Path = path, Mouse = mouse, Width = width });
                }
            }
            float total = 0;
            foreach (GlyphPart part in parts) total += part.Width + 6 * unit;
            float scale = Mathf.Min(1f, 430f / Mathf.Max(1, total));
            float height = nominalHeight * scale;
            float left = -390;
            for (int i = 0; i < parts.Count; i++)
            {
                GlyphPart part = parts[i];
                float width = part.Width * scale;
                float x = left + width * 0.5f;
                if (part.Separator)
                    ControlHintGraphics.Text(source, parent, "Separator", part.Label, x, y, width, height,
                        26 * scale, TextAlignmentOptions.Center);
                else if (part.Mouse)
                    ControlHintGraphics.MouseIcon(parent, x, y, width, height);
                else if (controller && (part.Path == "<Gamepad>/leftStick" ||
                    part.Path == "<Gamepad>/rightStick" || part.Path == "<Gamepad>/leftStickPress"))
                    ControlHintGraphics.ControllerIcon(source, parent, "Stick" + i, part.Label, x, y, height);
                else
                    ControlHintGraphics.Icon(source, parent, "Control" + i, part.Label,
                        x, y, width, height, controller);
                left += width + 6 * unit * scale;
            }
            ControlHintGraphics.Dots(parent, "Leader", left + 8, 42, y);
        }

        internal void OnSceneChanged()
        {
            ClearRoots();
            nextLoadingScan = 0;
            drawnRevision = -1;
        }

        internal void Stop()
        {
            ClearRoots();
            if (harmony != null) { harmony.UnpatchSelf(); harmony = null; }
            if (current == this) current = null;
            ControlHintGraphics.Dispose();
        }

        private void ClearRoots()
        {
            foreach (GameObject root in roots)
                if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            roots.Clear();
        }
    }
}
