#if UNITY_EDITOR
using System;
using cn.efunstudio.psdreader;
using cn.efunstudio.psdreader.PsdParser;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    public sealed partial class PsdLayerNode
    {
        [SerializeField, HideInInspector] private AiTextSpec aiText;
        [SerializeField, HideInInspector] private int aiTextStyleIndex = -1;
        [SerializeField, HideInInspector] private int aiTextStyleLayerId = -1;
        private bool aiTextReady;
        internal bool HasAuthoredAiText => IsAiTarget && aiText?.mode == "value";
        internal bool HasExplicitAiTextRect => IsAiTarget && aiExplicitRect && AiUiContract.IsText(UIType);
        internal TextAnchor AiTextAlignment => !string.IsNullOrEmpty(aiText?.style?.alignment)
            ? (TextAnchor)Enum.Parse(typeof(TextAnchor), aiText.style.alignment) : TextAnchor.MiddleCenter;

        internal void ConfigureAiText(AiTextSpec text, int styleSourceIndex, PsdDocument document)
        {
            aiText = text;
            aiTextStyleIndex = styleSourceIndex;
            aiTextStyleLayerId = ResolveLayerId(document, styleSourceIndex);
            UpdateAiBinding(document);
        }

        private bool TryParseAiText(out TextLayerInfo info)
        {
            info = default;
            if (aiText == null) return false;
            if (aiText.mode == "source") return ParseNativeTextLayerInfo(out info);
            if (aiText.mode != "value") return false;

            bool inherited = ParseNativeTextLayerInfo(out info);
            var style = aiText.style;
            if (!inherited && style?.IsComplete != true) return false;
            var run = inherited ? info.StyleRuns[0] : new TextStyleRunInfo {
                Color = Color.white, HorizontalScale = 1f, IsAutoLineSpacing = true, AutoKerning = true
            };
            // A style reference copies appearance only, never its text, paragraph box or transform.
            info.Text = aiText.value;
            info.IsParagraphText = aiText.wrap;
            if (!inherited) { info.LayerOpacity = 1f; info.FillOpacity = 1f; }
            run.Start = 0;
            run.Length = info.Text.Length;
            if (style != null)
            {
                if (!string.IsNullOrEmpty(style.font)) run.FontName = style.font;
                if (style.fontSize > 0) run.FontSize = style.fontSize;
                if (!string.IsNullOrEmpty(style.color)) ColorUtility.TryParseHtmlString(style.color, out run.Color);
                if (!string.IsNullOrEmpty(style.fontStyle))
                {
                    run.FontStyle = (FontStyle)Enum.Parse(typeof(FontStyle), style.fontStyle);
                    run.TMPFontStyle = (run.FontStyle == FontStyle.Bold || run.FontStyle == FontStyle.BoldAndItalic ? TMPro.FontStyles.Bold : 0)
                        | (run.FontStyle == FontStyle.Italic || run.FontStyle == FontStyle.BoldAndItalic ? TMPro.FontStyles.Italic : 0);
                }
                if (style.outlineSize > 0 && !string.IsNullOrEmpty(style.outlineColor))
                {
                    info.HasOutline = true;
                    info.OutlineSize = style.outlineSize;
                    info.TMPOutlineSize = style.outlineSize;
                    ColorUtility.TryParseHtmlString(style.outlineColor, out info.OutlineColor);
                }
            }
            info.StyleRuns = new[] { run };
            info.ParagraphRuns = Array.Empty<TextParagraphRunInfo>();
            info.FontSize = run.FontSize;
            info.FontName = run.FontName;
            info.FontStyle = run.FontStyle;
            info.TMPFontStyle = run.TMPFontStyle;
            info.Color = run.Color;
            info.CharacterSpacing = run.CharacterSpacing;
            info.LineSpacing = run.LineSpacing;
            info.IsAutoLineSpacing = run.IsAutoLineSpacing;
            info.AutoKerning = run.AutoKerning;
            return true;
        }
    }
}
#endif
