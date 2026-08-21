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
    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAnalysisPackageDocument
    {
        public string version;
        public string treeHash;
        public AiAnalysisDocumentInfo document;
        public AiAnalysisConfigInfo config;
        public List<AiAnalysisNodeEntry> nodes = new List<AiAnalysisNodeEntry>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAnalysisDocumentInfo
    {
        public string previewImagePath;
        public string annotatedPreviewImagePath;
        public string nodePreviewDirectoryPath;
        public string nodeAtlasDirectoryPath;
        public List<string> visualInputPaths = new List<string>();
        public int width;
        public int height;
    }

    /// <summary>
    /// Psd2UIFormConfig.asset 中 Rules 的精简导出。
    /// 只导出 AI 需要的"名字 → uiType 候选"映射。
    /// </summary>
    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAnalysisConfigInfo
    {
        public List<AiAnalysisUiTypeRuleInfo> uiTypeRules = new List<AiAnalysisUiTypeRuleInfo>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAnalysisUiTypeRuleInfo
    {
        public string uiType;
        public string uiTypeDesc;
        public string[] typeMatches;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAnalysisNodeEntry
    {
        public string id;
        public string shortId;
        public string parentId;
        public string[] childIds;
        public string onlyChildId;
        public string idPath;
        public string displayPath;
        public string name;
        public string layerName;
        public string[] nameTokens;
        public string layerType;
        public bool isGroupLayer;
        public bool isTextLayer;
        public bool isGeneratedNode;
        public string uiType;
        public RectData rect;
        public string imageFile;
        public string previewKind;
        public string previewSourceId;
        public string visualHash;
        public int renderLeafCount;
        public AiAtlasRef atlas;
        public string suffixMatch;
        public int siblingIndex;
        public int childCount;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAtlasRef
    {
        public string page;
        public int cell;
        public string label;
        public RectData imageRect;
        public RectData labelRect;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class RectData
    {
        public float x;
        public float y;
        public float w;
        public float h;
    }
}
#endif
