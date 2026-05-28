/*
 * Copyright (c) 2023-2026 Beijing Yiqu Technology Co., Ltd.
 *
 * All Rights Reserved.
 *
 * The software and associated documentation (including but not limited to source code,
 * object code, documents, images, audio, and video files) are the intellectual property
 * of Beijing Yiqu Technology Co., Ltd.
 *
 * For commercial use or licensing inquiries, please contact:
 * Email: efunstudio@gmail.com
 * Website: https://efunstudio.cn
 */

/*
插件获取:
https://efunstudio.cn
https://shop106471535.taobao.com
*/

#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

namespace UGF.EditorTools.Psd2UGUI
{
    [DisallowMultipleComponent]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = false)]
    public sealed class SliderHelper : UIHelperBase
    {
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode background;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode fill;
        [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true)]
        [SerializeField] PsdLayerNode handle;
        internal override PsdLayerNode[] GetDependencies()
        {
            return CalculateDependencies(background, fill, handle);
        }

        internal override void ParseAndAttachUIElements()
        {
            background = LayerNode.FindSubLayerNode(GUIType.Background, GUIType.Image, GUIType.RawImage);
            fill = LayerNode.FindSubLayerNode(GUIType.Slider_Fill);
            handle = LayerNode.FindSubLayerNode(GUIType.Slider_Handle);
        }

        protected override void InitUIElements(GameObject uiRoot)
        {
            var slider = uiRoot.GetComponent<Slider>();
            if (slider == null) return;

            UGUIParser.SetRectTransform(LayerNode, slider);

            var sliderInfo = new RectInfo(LayerNode.LayerRect);
            var trackInfo = background != null ? new RectInfo(background.LayerRect) : sliderInfo;
            var fillInfo = fill != null ? new RectInfo?(new RectInfo(fill.LayerRect)) : null;
            var handleInfo = handle != null ? new RectInfo?(new RectInfo(handle.LayerRect)) : null;
            bool horizontal = trackInfo.Width >= trackInfo.Height;

            SetupBackground(slider);
            SetupTrackAreas(slider, sliderInfo, trackInfo);
            SetupFill(slider, trackInfo, fillInfo, horizontal);
            SetupHandle(slider, trackInfo, handleInfo, horizontal);
            ApplyDirectionAndValue(slider, trackInfo, fillInfo, handleInfo, horizontal);
        }

        private void SetupBackground(Slider slider)
        {
            var bg = slider.transform.Find("Background")?.GetComponent<Image>();
            if (bg == null) return;

            var sourceNode = background ?? LayerNode;
            UGUIParser.SetRectTransform(sourceNode, bg);
            UGUIParser.Instance.BindImage(sourceNode, bg);
        }

        private void SetupTrackAreas(Slider slider, RectInfo sliderInfo, RectInfo trackInfo)
        {
            AlignTrackArea(slider.fillRect?.parent as RectTransform, sliderInfo, trackInfo);
            AlignTrackArea(slider.handleRect?.parent as RectTransform, sliderInfo, trackInfo);
        }

        private void SetupFill(Slider slider, RectInfo trackInfo, RectInfo? fillInfo, bool horizontal)
        {
            var fillRect = slider.fillRect;
            if (fillRect == null) return;

            var fillImg = fillRect.GetComponent<Image>();
            if (fillImg != null)
            {
                UGUIParser.Instance.BindImage(fill, fillImg);
            }

            if (fillInfo.HasValue)
            {
                ApplyCrossAxis(fillRect, trackInfo, fillInfo.Value, horizontal);
            }
        }

        private void SetupHandle(Slider slider, RectInfo trackInfo, RectInfo? handleInfo, bool horizontal)
        {
            var handleRect = slider.handleRect;
            var handleImg = handleRect?.GetComponent<Image>();
            if (handleRect == null || handleImg == null) return;

            var noHandleLayer = handle == null;
            handleImg.gameObject.SetActive(!noHandleLayer);
            slider.transition = noHandleLayer ? Selectable.Transition.None : Selectable.Transition.ColorTint;
            slider.interactable = !noHandleLayer;
            UGUIParser.Instance.BindImage(handle, handleImg);

            if (handleInfo.HasValue)
            {
                var info = handleInfo.Value;
                handleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, info.Width);
                handleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, info.Height);
                var anchored = handleRect.anchoredPosition;
                if (horizontal)
                {
                    anchored.y = info.Center.y - trackInfo.Center.y;
                }
                else
                {
                    anchored.x = info.Center.x - trackInfo.Center.x;
                }
                handleRect.anchoredPosition = anchored;
            }
        }

        private void ApplyDirectionAndValue(Slider slider, RectInfo trackInfo, RectInfo? fillInfo, RectInfo? handleInfo, bool horizontal)
        {
            var normalized = CalculateNormalizedValue(trackInfo, fillInfo, handleInfo, horizontal, out var direction);
            slider.direction = direction;
            var targetValue = Mathf.Lerp(slider.minValue, slider.maxValue, normalized);
            slider.SetValueWithoutNotify(targetValue);
        }

        private static void AlignTrackArea(RectTransform target, RectInfo parentInfo, RectInfo trackInfo)
        {
            if (target == null) return;

            target.anchorMin = target.anchorMax = new Vector2(0.5f, 0.5f);
            target.pivot = new Vector2(0.5f, 0.5f);
            target.anchoredPosition = trackInfo.Center - parentInfo.Center;
            target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, trackInfo.Width);
            target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, trackInfo.Height);
        }

        private static void ApplyCrossAxis(RectTransform target, RectInfo trackInfo, RectInfo info, bool horizontal)
        {
            var anchored = target.anchoredPosition;
            if (horizontal)
            {
                anchored.y = info.Center.y - trackInfo.Center.y;
                target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, info.Height);
            }
            else
            {
                anchored.x = info.Center.x - trackInfo.Center.x;
                target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, info.Width);
            }
            target.anchoredPosition = anchored;
        }

        private static float CalculateNormalizedValue(RectInfo trackInfo, RectInfo? fillInfo, RectInfo? handleInfo, bool horizontal, out Slider.Direction direction)
        {
            direction = horizontal ? Slider.Direction.LeftToRight : Slider.Direction.BottomToTop;
            float length = horizontal ? trackInfo.Width : trackInfo.Height;
            if (length <= Mathf.Epsilon)
            {
                return 1f;
            }

            if (fillInfo.HasValue)
            {
                var fill = fillInfo.Value;
                float startEdge = horizontal ? trackInfo.Left : trackInfo.Bottom;
                float endEdge = horizontal ? trackInfo.Right : trackInfo.Top;
                float fillStart = horizontal ? fill.Left : fill.Bottom;
                float fillEnd = horizontal ? fill.Right : fill.Top;
                bool startAnchor = Mathf.Abs(fillStart - startEdge) <= Mathf.Abs(endEdge - fillEnd);

                direction = horizontal
                    ? (startAnchor ? Slider.Direction.LeftToRight : Slider.Direction.RightToLeft)
                    : (startAnchor ? Slider.Direction.BottomToTop : Slider.Direction.TopToBottom);

                float covered = startAnchor ? (fillEnd - startEdge) : (endEdge - fillStart);
                return Mathf.Clamp01(covered / length);
            }

            if (handleInfo.HasValue)
            {
                var handleRect = handleInfo.Value;
                float startEdge = horizontal ? trackInfo.Left : trackInfo.Bottom;
                float handleCenter = horizontal ? handleRect.Center.x : handleRect.Center.y;
                return Mathf.Clamp01((handleCenter - startEdge) / length);
            }

            return 1f;
        }

        private readonly struct RectInfo
        {
            public RectInfo(Rect rect)
            {
                Center = rect.position;
                Size = rect.size;
            }

            public Vector2 Center { get; }
            public Vector2 Size { get; }
            public float Width => Size.x;
            public float Height => Size.y;
            public float Left => Center.x - Width * 0.5f;
            public float Right => Center.x + Width * 0.5f;
            public float Bottom => Center.y - Height * 0.5f;
            public float Top => Center.y + Height * 0.5f;
        }
    }
}
#endif
