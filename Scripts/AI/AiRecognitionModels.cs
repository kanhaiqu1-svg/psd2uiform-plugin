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

#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UGF.EditorTools.Psd2UGUI
{
    internal enum AiResultDocumentKind
    {
        None = 0,
        MainType = 1,
        ChildRelation = 2,
        Structural = 3,
        Patch = 4,
        RecognitionCombined = 5
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiPromptTag
    {
        public string key;
        public string value;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiMainTypeResultDocument
    {
        public string version;
        public string treeHash;
        public List<AiMainTypeEntry> nodes = new List<AiMainTypeEntry>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiMainTypeEntry
    {
        public string targetId;
        public string currentUIType;
        public string predictedUIType;
        public float confidence;
        public string reason;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiChildRelationResultDocument
    {
        public string version;
        public string treeHash;
        public List<AiChildRelationEntry> relations = new List<AiChildRelationEntry>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiChildRelationEntry
    {
        public string ownerId;
        public string targetId;
        public string roleType;
        public string[] memberNodeIds;
        public float confidence;
        public string reason;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionCombinedResultDocument
    {
        public string version;
        public string treeHash;
        public List<AiRecognitionOwnerEntry> owners = new List<AiRecognitionOwnerEntry>();
        public List<AiRecognitionRoleEntry> roles = new List<AiRecognitionRoleEntry>();
        public List<AiRecognitionNodeLabelEntry> nodeLabels = new List<AiRecognitionNodeLabelEntry>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionOwnerEntry
    {
        public string ownerId;
        public string ownerType;
        public string carrierNodeId;
        public string[] memberNodeIds;
        public float confidence;
        public string reason;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionRoleEntry
    {
        public string ownerId;
        public string roleType;
        public string carrierNodeId;
        public string[] memberNodeIds;
        public float confidence;
        public string reason;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionNodeLabelEntry
    {
        public string nodeId;
        public string currentUIType;
        public string labelType;
        public float confidence;
        public string reason;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiStructuralResultDocument
    {
        public string version;
        public string treeHash;
        public List<AiPatchOperation> operations = new List<AiPatchOperation>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiOwnerScopeManifestDocument
    {
        public string version;
        public string treeHash;
        public List<AiOwnerScopeEntry> scopes = new List<AiOwnerScopeEntry>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiOwnerScopeEntry
    {
        public string ownerId;
        public string ownerShortId;
        public string ownerType;
        public RectData rect;
        public string scopeImagePath;
        public string annotatedScopeImagePath;
        public string[] candidateNodeIds;
    }

    internal static class AiHelperContractCatalog
    {
        internal enum RoleBackingKind
        {
            None = 0,
            Image = 1,
            Text = 2
        }

        internal sealed class OwnerContract
        {
            public GUIType ownerType;
            public bool requiresLayerGroupCarrier;
            public GUIType[] allowedRoles = Array.Empty<GUIType>();
            public GUIType[] requiredRoles = Array.Empty<GUIType>();
            public GUIType[] allowedDirectChildControls = Array.Empty<GUIType>();
        }

        private static readonly HashSet<GUIType> AllowedOwnerTypes = new HashSet<GUIType>
        {
            GUIType.Null,
            GUIType.Image,
            GUIType.Text,
            GUIType.Button,
            GUIType.Dropdown,
            GUIType.InputField,
            GUIType.Toggle,
            GUIType.Slider,
            GUIType.ScrollView,
            GUIType.Mask,
            GUIType.FillColor,
            GUIType.Panel,
            GUIType.ToggleGroup
        };

        private static readonly HashSet<GUIType> SafeNodeLabelTypes = new HashSet<GUIType>
        {
            GUIType.Null,
            GUIType.Image,
            GUIType.Text,
            GUIType.Mask,
            GUIType.FillColor,
            GUIType.Panel
        };

        private static readonly HashSet<GUIType> AllowedRoleTypes = new HashSet<GUIType>
        {
            GUIType.Background,
            GUIType.Button_Highlight,
            GUIType.Button_Press,
            GUIType.Button_Select,
            GUIType.Button_Disable,
            GUIType.Button_Text,
            GUIType.Dropdown_Label,
            GUIType.Dropdown_Arrow,
            GUIType.InputField_Placeholder,
            GUIType.InputField_Text,
            GUIType.Toggle_Checkmark,
            GUIType.Toggle_Label,
            GUIType.Slider_Fill,
            GUIType.Slider_Handle,
            GUIType.ScrollView_Viewport,
            GUIType.ScrollView_HorizontalBarBG,
            GUIType.ScrollView_HorizontalBar,
            GUIType.ScrollView_VerticalBarBG,
            GUIType.ScrollView_VerticalBar
        };

        private static readonly Dictionary<GUIType, RoleBackingKind> RoleBackings = new Dictionary<GUIType, RoleBackingKind>
        {
            { GUIType.Background, RoleBackingKind.Image },
            { GUIType.Button_Highlight, RoleBackingKind.Image },
            { GUIType.Button_Press, RoleBackingKind.Image },
            { GUIType.Button_Select, RoleBackingKind.Image },
            { GUIType.Button_Disable, RoleBackingKind.Image },
            { GUIType.Button_Text, RoleBackingKind.Text },
            { GUIType.Dropdown_Label, RoleBackingKind.Text },
            { GUIType.Dropdown_Arrow, RoleBackingKind.Image },
            { GUIType.InputField_Placeholder, RoleBackingKind.Text },
            { GUIType.InputField_Text, RoleBackingKind.Text },
            { GUIType.Toggle_Checkmark, RoleBackingKind.Image },
            { GUIType.Toggle_Label, RoleBackingKind.Text },
            { GUIType.Slider_Fill, RoleBackingKind.Image },
            { GUIType.Slider_Handle, RoleBackingKind.Image },
            { GUIType.ScrollView_Viewport, RoleBackingKind.Image },
            { GUIType.ScrollView_HorizontalBarBG, RoleBackingKind.Image },
            { GUIType.ScrollView_HorizontalBar, RoleBackingKind.Image },
            { GUIType.ScrollView_VerticalBarBG, RoleBackingKind.Image },
            { GUIType.ScrollView_VerticalBar, RoleBackingKind.Image }
        };

        private static readonly Dictionary<GUIType, OwnerContract> OwnerContracts = BuildOwnerContracts();

        internal static bool TryParseOwnerType(string value, out GUIType uiType)
        {
            return TryParseAiType(value, AllowedOwnerTypes, out uiType);
        }

        internal static bool TryParseNodeLabelType(string value, out GUIType uiType)
        {
            return TryParseAiType(value, SafeNodeLabelTypes, out uiType);
        }

        internal static bool TryParseRoleType(string value, out GUIType uiType)
        {
            return TryParseAiType(value, AllowedRoleTypes, out uiType);
        }

        internal static bool IsAllowedOwnerType(GUIType uiType)
        {
            return AllowedOwnerTypes.Contains(NormalizeAiType(uiType));
        }

        internal static bool IsSafeNodeLabelType(GUIType uiType)
        {
            return SafeNodeLabelTypes.Contains(NormalizeAiType(uiType));
        }

        internal static bool IsAllowedRoleType(GUIType uiType)
        {
            return AllowedRoleTypes.Contains(NormalizeAiType(uiType));
        }

        internal static bool IsForbiddenAiType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.RawImage:
                case GUIType.TMPText:
                case GUIType.TMPButton:
                case GUIType.TMPDropdown:
                case GUIType.TMPInputField:
                case GUIType.TMPToggle:
                    return true;
                default:
                    return false;
            }
        }

        internal static GUIType NormalizeAiType(GUIType uiType)
        {
            switch (uiType)
            {
                case GUIType.RawImage:
                    return GUIType.Image;
                case GUIType.TMPText:
                    return GUIType.Text;
                case GUIType.TMPButton:
                    return GUIType.Button;
                case GUIType.TMPDropdown:
                    return GUIType.Dropdown;
                case GUIType.TMPInputField:
                    return GUIType.InputField;
                case GUIType.TMPToggle:
                    return GUIType.Toggle;
                default:
                    return uiType;
            }
        }

        internal static OwnerContract GetOwnerContract(GUIType ownerType)
        {
            OwnerContracts.TryGetValue(NormalizeAiType(ownerType), out var contract);
            return contract;
        }

        internal static bool AllowsRole(GUIType ownerType, GUIType roleType)
        {
            var contract = GetOwnerContract(ownerType);
            if (contract == null)
            {
                return false;
            }

            roleType = NormalizeAiType(roleType);
            for (int i = 0; i < contract.allowedRoles.Length; i++)
            {
                if (contract.allowedRoles[i] == roleType)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool AllowsDirectChildControl(GUIType ownerType, GUIType childType)
        {
            var contract = GetOwnerContract(ownerType);
            if (contract == null)
            {
                return false;
            }

            childType = NormalizeAiType(childType);
            for (int i = 0; i < contract.allowedDirectChildControls.Length; i++)
            {
                if (contract.allowedDirectChildControls[i] == childType)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool RequiresLayerGroupCarrier(GUIType ownerType)
        {
            var contract = GetOwnerContract(ownerType);
            return contract != null && contract.requiresLayerGroupCarrier;
        }

        internal static GUIType[] GetAllowedRoles(GUIType ownerType)
        {
            return GetOwnerContract(ownerType)?.allowedRoles ?? Array.Empty<GUIType>();
        }

        internal static GUIType[] GetRequiredRoles(GUIType ownerType)
        {
            return GetOwnerContract(ownerType)?.requiredRoles ?? Array.Empty<GUIType>();
        }

        internal static RoleBackingKind GetRoleBacking(GUIType roleType)
        {
            roleType = NormalizeAiType(roleType);
            return RoleBackings.TryGetValue(roleType, out var backing) ? backing : RoleBackingKind.None;
        }

        internal static bool IsTextRole(GUIType roleType)
        {
            return GetRoleBacking(roleType) == RoleBackingKind.Text;
        }

        internal static bool IsImageRole(GUIType roleType)
        {
            return GetRoleBacking(roleType) == RoleBackingKind.Image;
        }

        internal static bool AllowsMultiMemberRole(GUIType roleType)
        {
            return IsImageRole(roleType);
        }

        internal static bool CanReuseOwnerCarrier(GUIType ownerType, AiAnalysisNodeEntry node)
        {
            ownerType = NormalizeAiType(ownerType);
            if (node == null)
            {
                return false;
            }

            if (ownerType == GUIType.Text)
            {
                return node.isTextLayer;
            }

            if (RequiresLayerGroupCarrier(ownerType))
            {
                return node.isGroupLayer;
            }

            if (ownerType == GUIType.Image || ownerType == GUIType.Mask || ownerType == GUIType.FillColor)
            {
                return !node.isTextLayer;
            }

            return true;
        }

        internal static bool CanReuseRoleCarrier(GUIType roleType, AiAnalysisNodeEntry node)
        {
            if (node == null)
            {
                return false;
            }

            return IsTextRole(roleType) ? node.isTextLayer : !node.isTextLayer;
        }

        internal static GUIType ResolveSafeNodeLabelFallback(AiAnalysisNodeEntry node)
        {
            if (node == null)
            {
                return GUIType.Null;
            }

            if (node.isTextLayer)
            {
                return GUIType.Text;
            }

            if (node.isGroupLayer)
            {
                return GUIType.Null;
            }

            if (AiPatchValidator.TryParseUIType(node.uiType, out var currentType))
            {
                currentType = NormalizeAiType(currentType);
                if (IsSafeNodeLabelType(currentType))
                {
                    return currentType;
                }
            }

            return GUIType.Image;
        }

        private static bool TryParseAiType(string value, HashSet<GUIType> allowedTypes, out GUIType uiType)
        {
            uiType = GUIType.Null;
            if (string.IsNullOrWhiteSpace(value) || !AiPatchValidator.TryParseUIType(value, out uiType))
            {
                return false;
            }

            uiType = NormalizeAiType(uiType);
            return allowedTypes.Contains(uiType);
        }

        private static Dictionary<GUIType, OwnerContract> BuildOwnerContracts()
        {
            var result = new Dictionary<GUIType, OwnerContract>();

            Add(result, GUIType.Null, true);
            Add(result, GUIType.Image, false);
            Add(result, GUIType.Text, false);
            Add(result, GUIType.Mask, false);
            Add(result, GUIType.FillColor, false);
            Add(result, GUIType.Panel, true, allowedRoles: new[] { GUIType.Background });
            Add(result, GUIType.ToggleGroup, true, allowedRoles: new[] { GUIType.Background }, allowedDirectChildControls: new[] { GUIType.Toggle });
            Add(result, GUIType.Button, true,
                allowedRoles: new[] { GUIType.Background, GUIType.Button_Text, GUIType.Button_Highlight, GUIType.Button_Press, GUIType.Button_Select, GUIType.Button_Disable },
                requiredRoles: new[] { GUIType.Background });
            Add(result, GUIType.Dropdown, true,
                allowedRoles: new[] { GUIType.Background, GUIType.Dropdown_Label, GUIType.Dropdown_Arrow },
                allowedDirectChildControls: new[] { GUIType.ScrollView, GUIType.Toggle });
            Add(result, GUIType.InputField, true,
                allowedRoles: new[] { GUIType.Background, GUIType.InputField_Placeholder, GUIType.InputField_Text });
            Add(result, GUIType.Toggle, true,
                allowedRoles: new[] { GUIType.Background, GUIType.Toggle_Checkmark, GUIType.Toggle_Label });
            Add(result, GUIType.Slider, true,
                allowedRoles: new[] { GUIType.Background, GUIType.Slider_Fill, GUIType.Slider_Handle },
                requiredRoles: new[] { GUIType.Background, GUIType.Slider_Fill });
            Add(result, GUIType.ScrollView, true,
                allowedRoles: new[]
                {
                    GUIType.Background,
                    GUIType.ScrollView_Viewport,
                    GUIType.ScrollView_HorizontalBarBG,
                    GUIType.ScrollView_HorizontalBar,
                    GUIType.ScrollView_VerticalBarBG,
                    GUIType.ScrollView_VerticalBar
                });

            return result;
        }

        private static void Add(
            Dictionary<GUIType, OwnerContract> result,
            GUIType ownerType,
            bool requiresLayerGroupCarrier,
            GUIType[] allowedRoles = null,
            GUIType[] requiredRoles = null,
            GUIType[] allowedDirectChildControls = null)
        {
            result[ownerType] = new OwnerContract
            {
                ownerType = ownerType,
                requiresLayerGroupCarrier = requiresLayerGroupCarrier,
                allowedRoles = allowedRoles ?? Array.Empty<GUIType>(),
                requiredRoles = requiredRoles ?? Array.Empty<GUIType>(),
                allowedDirectChildControls = allowedDirectChildControls ?? Array.Empty<GUIType>()
            };
        }
    }
}
#endif
