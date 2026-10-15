#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UGF.EditorTools.Psd2UGUI
{
    internal enum AiResultDocumentKind { None, Recognition }
    [Serializable] internal sealed class AiPromptTag { public string key; public string value; }

    // A flat target tree; UI identities are independent of source layer identities.
    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionDocument
    {
        public string version;
        public List<AiUiNode> ui = new List<AiUiNode>();
        public List<AiRecognitionIssue> issues = new List<AiRecognitionIssue>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiUiNode
    {
        public string id;
        public string parent;
        public string type;
        public string name;
        public string[] sources = Array.Empty<string>();
        public RectData rect;
        public AiTextSpec text;
    }

    // Native text bindings are independent of reusable style references.
    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiTextSpec
    {
        public string mode; // source: complete native text; value: explicit AI-authored text.
        public string source;
        public string value;
        public string styleSource;
        public AiTextStyle style;
        public float rotation;
        public bool wrap;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiTextStyle
    {
        public string font;
        public float fontSize;
        public string color;
        public string fontStyle;
        public string alignment;
        public float outlineSize;
        public string outlineColor;
        internal bool IsComplete => fontSize > 0 && !string.IsNullOrEmpty(color);
    }

    internal static class AiUiContract
    {
        internal static readonly GUIType[] MainTypes = {
            GUIType.Image, GUIType.RawImage, GUIType.Text, GUIType.Button, GUIType.Toggle,
            GUIType.ToggleGroup, GUIType.Slider, GUIType.InputField, GUIType.Dropdown,
            GUIType.ScrollView, GUIType.Panel, GUIType.Mask, GUIType.FillColor, GUIType.Null
        };
        internal static bool TryParse(string value, out GUIType type)
        {
            type = GUIType.Null;
            return Enum.TryParse(value, false, out type) && type.ToString() == value && (IsMain(type) || IsRole(type));
        }
        internal static bool IsMain(GUIType type) => Array.IndexOf(MainTypes, type) >= 0;
        internal static bool IsRole(GUIType type) => type >= GUIType.Background && type <= GUIType.ScrollView_VerticalBar;
        internal static bool IsText(GUIType type) => type == GUIType.Text || type == GUIType.TMPText || type == GUIType.Button_Text
            || type == GUIType.Toggle_Label || type == GUIType.Dropdown_Label
            || type == GUIType.InputField_Text || type == GUIType.InputField_Placeholder;
        internal static bool IsContainer(GUIType type)
        {
            type = Canonical(type);
            return type == GUIType.Button || type == GUIType.Toggle
                || type == GUIType.ToggleGroup || type == GUIType.Slider || type == GUIType.InputField
                || type == GUIType.Dropdown || type == GUIType.ScrollView || type == GUIType.Panel;
        }
        internal static bool CanContain(GUIType type) => IsContainer(type) || type == GUIType.Mask;
        internal static GUIType Canonical(GUIType type)
        {
            switch (type)
            {
                case GUIType.TMPText: return GUIType.Text;
                case GUIType.TMPButton: return GUIType.Button;
                case GUIType.TMPToggle: return GUIType.Toggle;
                case GUIType.TMPDropdown: return GUIType.Dropdown;
                case GUIType.TMPInputField: return GUIType.InputField;
                default: return type;
            }
        }
        internal static GUIType[] Roles(GUIType type)
        {
            switch (Canonical(type))
            {
                case GUIType.Button: return ButtonRoles;
                case GUIType.Toggle: return ToggleRoles;
                case GUIType.Slider: return SliderRoles;
                case GUIType.InputField: return InputRoles;
                case GUIType.Dropdown: return DropdownRoles;
                case GUIType.ScrollView: return ScrollRoles;
                case GUIType.Panel:
                case GUIType.ToggleGroup: return BackgroundRole;
                default: return Array.Empty<GUIType>();
            }
        }
        internal static bool Allows(GUIType owner, GUIType role) => Array.IndexOf(Roles(owner), role) >= 0;
        private static readonly GUIType[] BackgroundRole = { GUIType.Background };
        private static readonly GUIType[] ButtonRoles = { GUIType.Background, GUIType.Button_Text, GUIType.Button_Highlight, GUIType.Button_Press, GUIType.Button_Select, GUIType.Button_Disable };
        private static readonly GUIType[] ToggleRoles = { GUIType.Background, GUIType.Toggle_Checkmark, GUIType.Toggle_Label };
        private static readonly GUIType[] SliderRoles = { GUIType.Background, GUIType.Slider_Fill, GUIType.Slider_Handle };
        private static readonly GUIType[] InputRoles = { GUIType.Background, GUIType.InputField_Placeholder, GUIType.InputField_Text };
        private static readonly GUIType[] DropdownRoles = { GUIType.Background, GUIType.Dropdown_Label, GUIType.Dropdown_Arrow };
        private static readonly GUIType[] ScrollRoles = { GUIType.Background, GUIType.ScrollView_Viewport, GUIType.ScrollView_HorizontalBarBG, GUIType.ScrollView_HorizontalBar, GUIType.ScrollView_VerticalBarBG, GUIType.ScrollView_VerticalBar };
    }
}
#endif
