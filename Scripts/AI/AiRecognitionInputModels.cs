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
    internal sealed class AiRecognitionInputManifestDocument
    {
        public string version;
        public string treeHash;
        public AiRecognitionInputDocumentInfo document;
        public AiAnalysisConfigInfo config;
        public int nodeCount;
        public string[] nodeShardPaths;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionInputDocumentInfo
    {
        public string previewImagePath;
        public string annotatedPreviewImagePath;
        public string nodePreviewDirectoryPath;
        public string nodeAtlasDirectoryPath;
        public int atlasPageCount;
        public int width;
        public int height;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionNodeShardDocument
    {
        public string version;
        public string treeHash;
        public int shardIndex;
        public int totalShards;
        public List<AiRecognitionInputNodeEntry> nodes = new List<AiRecognitionInputNodeEntry>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionInputNodeEntry
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
        public string previewFileName;
        public string previewKind;
        public string previewSourceId;
        public string visualHash;
        public int renderLeafCount;
        public AiRecognitionInputAtlasRef atlas;
        public string suffixMatch;
        public int siblingIndex;
        public int childCount;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionInputAtlasRef
    {
        public string page;
        public int cell;
        public string label;
    }
}
#endif
