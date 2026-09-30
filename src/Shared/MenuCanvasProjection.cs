using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Disappearance.Shared
{
    // Owns only a menu's temporary presentation. Never changes the gameplay camera or a
    // shared font/material. The original parent, rect and sibling order are leased.
    internal sealed class MenuCanvasProjection : IDisposable
    {
        private readonly RectTransform target;
        private readonly Transform parent;
        private readonly int sibling;
        private readonly RectState original;
        private GameObject overlay;

        private struct RectState
        {
            private Vector2 min, max, pivot, size;
            private Vector3 position, scale;
            private Quaternion rotation;
            internal RectState(RectTransform rect)
            {
                min=rect.anchorMin; max=rect.anchorMax; pivot=rect.pivot;
                size=rect.sizeDelta; position=rect.anchoredPosition3D;
                scale=rect.localScale; rotation=rect.localRotation;
            }
            internal void Apply(RectTransform rect)
            {
                rect.anchorMin=min; rect.anchorMax=max; rect.pivot=pivot;
                rect.sizeDelta=size; rect.anchoredPosition3D=position;
                rect.localScale=scale; rect.localRotation=rotation;
            }
        }

        internal MenuCanvasProjection(RectTransform target, string canvasName = "UIControls_SettingsCanvas")
        {
            this.target=target; parent=target.parent; sibling=target.GetSiblingIndex();
            original=new RectState(target);
            Canvas source=target.GetComponentInParent<Canvas>()?.rootCanvas;
            if (source == null || source.renderMode != RenderMode.ScreenSpaceCamera) return;
            // Reproduce the target's ancestor layout only. Other menus, fades,
            // and scene objects keep their original canvas and lifecycle.
            var ancestors=new Stack<RectTransform>();
            for (Transform node=parent; node != source.transform; node=node.parent)
            {
                if (!(node is RectTransform rect)) throw new InvalidOperationException("Menu canvas ancestry is not a RectTransform chain");
                ancestors.Push(rect);
            }
            try
            {
                overlay=new GameObject(canvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(overlay, target.gameObject.scene);
                ((RectTransform)overlay.transform).pivot=((RectTransform)source.transform).pivot;
                var canvas=overlay.GetComponent<Canvas>();
                MenuTextStyle.ConfigureCanvas(canvas, source);
                canvas.scaleFactor=source.scaleFactor;
                canvas.referencePixelsPerUnit=source.referencePixelsPerUnit;
                var scaler=overlay.GetComponent<CanvasScaler>();
                var sourceScaler=source.GetComponent<CanvasScaler>();
                if (sourceScaler != null)
                {
                    scaler.uiScaleMode=sourceScaler.uiScaleMode;
                    scaler.referenceResolution=sourceScaler.referenceResolution;
                    scaler.screenMatchMode=sourceScaler.screenMatchMode;
                    scaler.matchWidthOrHeight=sourceScaler.matchWidthOrHeight;
                    scaler.physicalUnit=sourceScaler.physicalUnit;
                    scaler.fallbackScreenDPI=sourceScaler.fallbackScreenDPI;
                    scaler.defaultSpriteDPI=sourceScaler.defaultSpriteDPI;
                }
                scaler.scaleFactor=source.scaleFactor;
                scaler.referencePixelsPerUnit=source.referencePixelsPerUnit;
                Transform host=overlay.transform;
                while (ancestors.Count > 0)
                {
                    RectTransform ancestor=ancestors.Pop();
                    var copy=new GameObject("Layout_" + ancestor.name, typeof(RectTransform));
                    copy.transform.SetParent(host, false);
                    new RectState(ancestor).Apply((RectTransform)copy.transform);
                    host=copy.transform;
                }
                target.SetParent(host, false); original.Apply(target);
                Canvas.ForceUpdateCanvases();
                RebuildText();
            }
            catch { Dispose(); throw; }
        }

        private void RebuildText()
        {
            if (target == null) return;
            foreach (TMP_Text text in target.GetComponentsInChildren<TMP_Text>(true))
            {
                text.havePropertiesChanged=true;
                if (text.isActiveAndEnabled) text.ForceMeshUpdate();
            }
        }

        public void Dispose()
        {
            if (overlay == null) return;
            // During scene teardown both parent and target can already be gone.
            if (target != null && parent != null)
            {
                target.SetParent(parent, false); original.Apply(target);
                target.SetSiblingIndex(sibling);
            }
            // Retire the temporary hierarchy even if TMP regeneration fails.
            try { RebuildText(); }
            finally { UnityEngine.Object.Destroy(overlay); overlay=null; }
        }
    }
}
