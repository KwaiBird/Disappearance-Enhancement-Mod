using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Bootstrap;
using Disappearance.Shared;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Disappearance.UIControls
{
    // A client of settings v2. No Comfort assembly reference and no gameplay/config writes.
    internal sealed class SettingsPresentation : IDisposable
    {
        private static SettingsPresentation current;
        private readonly SettingsRegistry registry;
        private readonly ManualLogSource log;
        private readonly Harmony patch = new Harmony(UIControlsPlugin.PluginId + ".settings");
        private static readonly string[] Ids = { "mouse_sensitivity", "controller_sensitivity", "horizontal_fov", "player_brightness" };
        private static readonly FieldInfo MenuField = AccessTools.Field(typeof(SettingMenuUIHandler), "settingMenu");
        private static readonly FieldInfo MouseField = AccessTools.Field(typeof(SettingMenuUIHandler), "sensitivitySlider");
        private static readonly FieldInfo LightField = AccessTools.Field(typeof(SettingMenuUIHandler), "lightingSlider");
        private static readonly FieldInfo MouseTriangle = AccessTools.Field(typeof(SettingMenuUIHandler), "sensitivityTriangle");
        private static readonly FieldInfo LightTriangle = AccessTools.Field(typeof(SettingMenuUIHandler), "lightingTriangle");
        private static readonly FieldInfo LightingImage = AccessTools.Field(typeof(SettingMenuUIHandler), "lightingImage");
        private static readonly MethodInfo SelectMethod = AccessTools.Method(typeof(SettingMenuUIHandler), "SelectMenu");
        private readonly Dictionary<SettingMenuUIHandler, Menu> menus = new Dictionary<SettingMenuUIHandler, Menu>();
        private float nextDiscover;
        internal int SelectedIndex { get { foreach (Menu m in menus.Values) if (m.Open && m.Owner != null) return m.Selected; return -1; } }
        private sealed class Row { internal Slider Slider; internal TMP_Text Label, Value; internal GameObject Triangle; internal SettingDescriptor Definition; }
        private sealed class Menu
        {
            internal SettingMenuUIHandler Owner;
            internal MenuCanvasProjection Projection;
            internal readonly List<GameObject> Added = new List<GameObject>();
            internal readonly Dictionary<GameObject, bool> Original = new Dictionary<GameObject, bool>();
            internal readonly Row[] Rows = new Row[4];
            internal TMP_Text GlyphLabel, GlyphValue;
            internal GameObject GlyphTriangle;
            internal Image Image;
            internal Color Color;
            internal bool Open, Synchronizing;
            internal int Selected, Direction;
            internal float NextRead, NextRepeat;
        }
        internal SettingsPresentation(SettingsRegistry registry, ManualLogSource log)
        {
            this.registry = registry; this.log = log; current = this;
            try
            {
                if (MenuField == null || MouseField == null || LightField == null || MouseTriangle == null || LightTriangle == null || LightingImage == null || SelectMethod == null) throw new MissingFieldException("SettingMenuUIHandler contract");
                Hook("Open", null, nameof(Opened)); Hook("Close", null, nameof(Closed));
                Hook("Up", nameof(Up)); Hook("Down", nameof(Down)); Hook("Left", nameof(Left)); Hook("Right", nameof(Right));
            }
            catch { patch.UnpatchSelf(); current = null; throw; }
        }
        private void Hook(string method, string prefix = null, string postfix = null) => patch.Patch(AccessTools.Method(typeof(SettingMenuUIHandler), method), prefix: prefix == null ? null : new HarmonyMethod(typeof(SettingsPresentation), prefix), postfix: postfix == null ? null : new HarmonyMethod(typeof(SettingsPresentation), postfix));
        private bool Available()
        {
            foreach (string id in Ids) if (!registry.Definitions.ContainsKey(id)) return false;
            var provider = Provider();
            return provider != null && provider.GetType().GetMethod("GetSettingsApiVersion")?.Invoke(provider, null) is int version && version == 2;
        }
        private BaseUnityPlugin Provider()
        {
            var chain = UnityChainloader.Instance;
            if (chain == null || !chain.Plugins.TryGetValue(SettingsRegistry.Owner, out PluginInfo info)) return null;
            var plugin = info.Instance as BaseUnityPlugin;
            return plugin != null && plugin.isActiveAndEnabled ? plugin : null;
        }
        private bool Read(string id, out double value)
        {
            value = 0;
            try
            {
                BaseUnityPlugin provider = Provider(); if (provider == null) return false;
                if (!(provider.GetType().GetMethod("GetSettingsApiVersion")?.Invoke(provider, null) is int version) || version != 2) return false;
                string response = provider.GetType().GetMethod("ReadSetting")?.Invoke(provider, new object[] { id }) as string;
                if (!Decode(response, out value, out double min, out double max, out double defaultValue)) return false;
                if (!registry.Definitions.TryGetValue(id, out SettingDescriptor spec) || min < (id == "horizontal_fov" ? 20 : spec.Min) || max != spec.Max) return false;
                spec.Min = min; return true;
            }
            catch (Exception e) { log.LogWarning("Settings read failed: " + e.Message); return false; }
        }
        private static bool Decode(string json, out double value, out double min, out double max, out double defaultValue)
        {
            value = min = max = defaultValue = 0;
            return Stage2Json.TryParse(json, out object raw, out _) && Stage2Json.TryObject(raw, out var data) && data.Count == 6 && Stage2Json.TryInteger(data, "schema", out int schema) && schema == 2 && Stage2Json.TryString(data, "status", out string status) && status == "ok" && Stage2Json.TryNumber(data, "value", out value) && Stage2Json.TryNumber(data, "min", out min) && Stage2Json.TryNumber(data, "max", out max) && Stage2Json.TryNumber(data, "defaultValue", out defaultValue) && min < max && value >= min && value <= max && defaultValue >= min && defaultValue <= max;
        }
        private static void ResetSelected(Menu menu)
        {
            if (menu.Selected < 0 || menu.Selected >= menu.Rows.Length) return;
            Row row = menu.Rows[menu.Selected];
            if (row.Slider.interactable) current.Write(menu, row, 0, true);
        }
        private void Write(Menu menu, Row row, double value, bool reset = false)
        {
            // Slider bounds can clamp their current value and notify listeners.
            // Rendering the owner's state must never become a user write.
            if (menu.Synchronizing) return;
            try
            {
                var provider = Provider();
                if (provider != null) provider.GetType().GetMethod("WriteSetting")?.Invoke(provider, new object[] { row.Definition.Id, reset ? "{\"schema\":2,\"operation\":\"reset\"}" : "{\"schema\":2,\"operation\":\"set\",\"value\":" + value.ToString("R", CultureInfo.InvariantCulture) + "}" });
            }
            catch (Exception e) { log.LogWarning("Settings write failed: " + e.Message); }
            Refresh(menu); // The owner's accepted state is authoritative after success or failure.
        }
        private static void Opened(SettingMenuUIHandler __instance)
        {
            if (current == null) return;
            try
            {
                if (!current.Available()) return;
                Menu menu = current.Prepare(__instance); if (menu == null) return;
                menu.Open = true;
                if (menu.Projection == null) menu.Projection = new MenuCanvasProjection((RectTransform)((GameObject)MenuField.GetValue(__instance)).transform);
                current.HideOriginal(menu); current.Refresh(menu); current.Select(menu, 0);
            }
            catch (Exception e) { current.log.LogError("Settings presentation failed; restoring game rows: " + e); current.Clear(); }
        }
        private static void Closed(SettingMenuUIHandler __instance)
        {
            if (current != null && current.menus.TryGetValue(__instance, out Menu menu))
            { menu.Open = false; menu.Direction = 0; current.Restore(() => { menu.Projection?.Dispose(); menu.Projection = null; }); foreach (GameObject obj in menu.Added) if (obj != null) obj.SetActive(false); }
        }
        private Menu Prepare(SettingMenuUIHandler owner)
        {
            if (menus.TryGetValue(owner, out Menu existing)) return existing;
            Slider mouse = MouseField.GetValue(owner) as Slider, brightness = LightField.GetValue(owner) as Slider;
            GameObject triangle = MouseTriangle.GetValue(owner) as GameObject, lightTriangle = LightTriangle.GetValue(owner) as GameObject;
            Image image = LightingImage.GetValue(owner) as Image;
            TMP_Text label = mouse == null ? null : mouse.transform.parent.Find("SensitivityText")?.GetComponent<TMP_Text>();
            TMP_Text lightLabel = brightness == null ? null : brightness.transform.parent.Find("LightText")?.GetComponent<TMP_Text>();
            if (mouse == null || brightness == null || triangle == null || lightTriangle == null || image == null || label == null || lightLabel == null) return null;
            var menu = new Menu { Owner=owner, Image=image, Color=image.color, Open=true };
            menus.Add(owner, menu); // Register before allocation so a partial construction can be unwound.
            foreach (GameObject obj in new[] { mouse.gameObject, brightness.gameObject, triangle, lightTriangle, label.gameObject, lightLabel.gameObject }) menu.Original[obj] = obj.activeSelf;
            Transform backRow = ((GameObject)MenuField.GetValue(owner)).transform.Find("BackToPauseMenu");
            if (backRow != null) menu.Original[backRow.gameObject] = backRow.gameObject.activeSelf;
            for (int i = 0; i < 4; i++)
            {
                bool lighting = i == 3;
                Slider template = lighting ? brightness : mouse;
                TMP_Text textTemplate = lighting ? lightLabel : label;
                float offset = lighting ? 53 : i * 53;
                var row = new Row { Definition=registry.Definitions[Ids[i]] };
                row.Slider = Clone(menu, template.gameObject, offset).GetComponent<Slider>();
                row.Label = Clone(menu, textTemplate.gameObject, offset).GetComponent<TMP_Text>();
                row.Triangle = Clone(menu, lighting ? lightTriangle : triangle, offset);
                row.Slider.onValueChanged = new Slider.SliderEvent();
                row.Slider.minValue = (float)row.Definition.Min; row.Slider.maxValue = (float)row.Definition.Max; row.Slider.wholeNumbers = false;
                row.Label.text = row.Definition.Label;
                Widen(row.Label); row.Value = ValueLabel(menu, row.Label, row.Slider);
                ((RectTransform)row.Triangle.transform).sizeDelta = new Vector2(52, 52); Align(row.Triangle, row.Label);
                row.Slider.onValueChanged.AddListener(v => Write(menu, row, v));
                menu.Rows[i] = row;
            }
            Row lightRow = menu.Rows[3];
            menu.GlyphLabel = Clone(menu, lightRow.Label.gameObject, 70).GetComponent<TMP_Text>(); menu.GlyphLabel.text = "ボタン表示";
            menu.GlyphValue = Clone(menu, lightRow.Label.gameObject, 70).GetComponent<TMP_Text>();
            var rect = (RectTransform)menu.GlyphValue.transform; rect.sizeDelta = new Vector2(480, rect.sizeDelta.y); rect.anchoredPosition = new Vector2(((RectTransform)lightRow.Slider.transform).anchoredPosition.x, rect.anchoredPosition.y); menu.GlyphValue.alignment = TextAlignmentOptions.MidlineLeft;
            menu.GlyphTriangle = Clone(menu, lightRow.Triangle, 70);
            log.LogInfo("Settings view prepared: owner-provided four rows; original listeners preserved.");
            return menu;
        }
        private static GameObject Clone(Menu menu, GameObject template, float offset)
        {
            var obj = UnityEngine.Object.Instantiate(template, template.transform.parent, false); menu.Added.Add(obj); obj.name = "UIControls_" + template.name;
            TMP_Text sourceText = template.GetComponent<TMP_Text>();
            if (sourceText != null) new MenuTextStyle(sourceText).Apply(obj.GetComponent<TMP_Text>());
            var rect = (RectTransform)obj.transform; rect.anchoredPosition -= new Vector2(0, offset); return obj;
        }
        private static void Widen(TMP_Text label)
        {
            var rect = (RectTransform)label.transform; rect.anchoredPosition -= new Vector2(75, 0); rect.sizeDelta += new Vector2(150, 0);
            label.enableWordWrapping = false; label.enableAutoSizing = true; label.fontSizeMin = 28; label.fontSizeMax = 36;
        }
        private static TMP_Text ValueLabel(Menu menu, TMP_Text label, Slider slider)
        {
            var a = (RectTransform)label.transform; var b = (RectTransform)slider.transform;
            float left = a.anchoredPosition.x - a.rect.width * a.pivot.x;
            float valueLeft = b.anchoredPosition.x - b.rect.width * b.pivot.x - 120;
            TMP_Text value = Clone(menu, label.gameObject, 0).GetComponent<TMP_Text>(); var rect = (RectTransform)value.transform;
            rect.sizeDelta = new Vector2(80, rect.sizeDelta.y); rect.anchoredPosition = new Vector2(valueLeft + 80 * rect.pivot.x, a.anchoredPosition.y);
            value.alignment = TextAlignmentOptions.Right; value.raycastTarget = false; value.fontSizeMin = 26;
            a.sizeDelta = new Vector2(valueLeft - 8 - left, a.sizeDelta.y); a.anchoredPosition = new Vector2(left + a.sizeDelta.x * a.pivot.x, a.anchoredPosition.y);
            return value;
        }
        private static void Align(GameObject triangle, TMP_Text label)
        {
            var a = (RectTransform)triangle.transform; var b = (RectTransform)label.transform;
            a.anchoredPosition = new Vector2(b.anchoredPosition.x - b.rect.width * b.pivot.x - a.rect.width * (1 - a.pivot.x) - 8, b.anchoredPosition.y);
        }
        private void HideOriginal(Menu menu)
        {
            foreach (GameObject obj in menu.Original.Keys) if (obj != null) obj.SetActive(false);
            foreach (GameObject obj in menu.Added) if (obj != null) obj.SetActive(menu.Open);
        }
        private void Refresh(Menu menu)
        {
            if (!menu.Open || menu.Synchronizing) return;
            menu.Synchronizing = true;
            try { RefreshFromOwner(menu); }
            finally { menu.Synchronizing = false; }
        }
        private void RefreshFromOwner(Menu menu)
        {
            menu.NextRead = Time.unscaledTime + .1f;
            foreach (Row row in menu.Rows)
            {
                bool ok = Read(row.Definition.Id, out double value) && value >= row.Definition.Min && value <= row.Definition.Max;
                row.Slider.interactable = ok;
                if (ok) { row.Slider.minValue=(float)row.Definition.Min; row.Slider.maxValue=(float)row.Definition.Max; row.Slider.SetValueWithoutNotify((float)value); }
                string format = row.Definition.Decimals == 0 ? "0" : row.Definition.Trim ? "0.##" : "0.00";
                row.Value.text = ok ? value.ToString(format, CultureInfo.InvariantCulture) : "--";
                if (ok && row.Definition.Id == "player_brightness")
                {
                    float offset = ((float)value - 5) * .1f; Color c = menu.Color;
                    menu.Image.color = new Color(Mathf.Clamp01(c.r + offset), Mathf.Clamp01(c.g + offset), Mathf.Clamp01(c.b + offset), c.a);
                }
            }
            ControllerLayout mode = ControllerGlyphSettings.Mode;
            ControllerLayout detected = ControllerGlyphSettings.Detect(Gamepad.current);
            menu.GlyphValue.text = mode == ControllerLayout.Auto ? "自動（" + (detected == ControllerLayout.Unknown ? "Xbox型" : ControllerGlyphSettings.ModeName(detected)) + "）" : ControllerGlyphSettings.ModeName(mode);
        }
        private void Select(Menu menu, int index)
        {
            menu.Selected = Mathf.Clamp(index, 0, 4); menu.Direction = 0;
            int game = menu.Selected >= 3 ? 1 : 0;
            SelectMethod.Invoke(menu.Owner, new[] { Enum.ToObject(SelectMethod.GetParameters()[0].ParameterType, game) });
            foreach (GameObject obj in new[] { MouseTriangle.GetValue(menu.Owner) as GameObject, LightTriangle.GetValue(menu.Owner) as GameObject }) if (obj != null) obj.SetActive(false);
            for (int i = 0; i < 4; i++) menu.Rows[i].Triangle.SetActive(menu.Open && menu.Selected == i);
            menu.GlyphTriangle.SetActive(menu.Open && menu.Selected == 4);
        }
        private static bool Up(SettingMenuUIHandler __instance) => !Navigate(__instance, -1);
        private static bool Down(SettingMenuUIHandler __instance) => !Navigate(__instance, 1);
        private static bool Left(SettingMenuUIHandler __instance) => !Adjust(__instance, -1);
        private static bool Right(SettingMenuUIHandler __instance) => !Adjust(__instance, 1);
        private static bool Navigate(SettingMenuUIHandler owner, int delta)
        {
            if (current == null || !current.menus.TryGetValue(owner, out Menu menu) || !menu.Open) return false;
            current.Select(menu, menu.Selected + delta); return true;
        }
        private static bool Adjust(SettingMenuUIHandler owner, int direction)
        {
            if (current == null || !current.menus.TryGetValue(owner, out Menu menu) || !menu.Open) return false;
            menu.Direction = direction; menu.NextRepeat = Time.unscaledTime + .38f; current.Change(menu, direction); return true;
        }
        private void Change(Menu menu, int direction)
        {
            if (menu.Selected < 4)
            {
                Row row = menu.Rows[menu.Selected]; if (!row.Slider.interactable || !Read(row.Definition.Id, out double value)) return;
                Write(menu, row, Math.Max(row.Definition.Min, Math.Min(row.Definition.Max, value + direction * row.Definition.Step)));
            }
            else if (menu.Selected == 4) { ControllerGlyphSettings.SetMode((ControllerLayout)(((int)ControllerGlyphSettings.Mode + direction + 4) % 4)); Refresh(menu); }
        }
        internal void Update()
        {
            if (!Available()) { if (menus.Count > 0) Clear(); return; }
            // A provider can register after the game's Open event. Attach only to an actually open menu.
            if (Time.unscaledTime >= nextDiscover)
            {
                nextDiscover = Time.unscaledTime + .5f;
                foreach (var owner in UnityEngine.Object.FindObjectsOfType<SettingMenuUIHandler>())
                {
                    if (menus.ContainsKey(owner)) continue;
                    var mouse = MouseField.GetValue(owner) as Slider;
                    if (mouse != null && mouse.gameObject.activeInHierarchy) Opened(owner);
                }
            }
            foreach (Menu menu in menus.Values)
            {
                if (menu.Owner == null || !menu.Open) continue;
                if (Time.unscaledTime >= menu.NextRead) Refresh(menu);
                Keyboard k = Keyboard.current; Gamepad p = Gamepad.current;
                if (menu.Selected < menu.Rows.Length && (k != null && k.rKey.wasPressedThisFrame || p != null && p.buttonNorth.wasPressedThisFrame))
                { ResetSelected(menu); continue; }
                bool left = k != null && (k.aKey.isPressed || k.leftArrowKey.isPressed) || p != null && (p.dpad.left.isPressed || p.leftStick.ReadValue().x < -.55f);
                bool right = k != null && (k.dKey.isPressed || k.rightArrowKey.isPressed) || p != null && (p.dpad.right.isPressed || p.leftStick.ReadValue().x > .55f);
                int direction = left == right ? 0 : left ? -1 : 1;
                if (direction == 0) { menu.Direction = 0; continue; }
                if (direction != menu.Direction) { menu.Direction = direction; menu.NextRepeat = Time.unscaledTime + .38f; if (k != null && (k.leftArrowKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame)) Change(menu, direction); }
                else if (Time.unscaledTime >= menu.NextRepeat) { Change(menu, direction); menu.NextRepeat = Time.unscaledTime + .1f; }
            }
        }
        internal void Clear()
        {
            foreach (Menu menu in menus.Values)
            {
                Restore(() => { menu.Projection?.Dispose(); menu.Projection = null; });
                foreach (GameObject obj in menu.Added)
                    Restore(() => { if (obj != null) { try { obj.SetActive(false); } finally { UnityEngine.Object.Destroy(obj); } } });
                foreach (var original in menu.Original)
                    Restore(() => { if (original.Key != null) original.Key.SetActive(menu.Open && original.Value); });
                Restore(() => { if (menu.Image != null) menu.Image.color = menu.Color; });
                Restore(() => { if (menu.Open && menu.Owner != null) SelectMethod.Invoke(menu.Owner, new[] { Enum.ToObject(SelectMethod.GetParameters()[0].ParameterType, menu.Selected >= 3 ? 1 : 0) }); });
            }
            menus.Clear();
        }
        private void Restore(Action restore)
        {
            try { restore(); }
            catch (Exception e) { log.LogError("Settings row restoration failed: " + e); }
        }
        public void Dispose() { current = null; try { Clear(); } finally { patch.UnpatchSelf(); } }
    }
}
