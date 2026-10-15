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
        public List<AiAnalysisNodeEntry> nodes = new List<AiAnalysisNodeEntry>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAnalysisDocumentInfo
    {
        public string sourceHash;
        public string imageRoot;
        public string previewImagePath;
        public string annotatedPreviewImagePath;
        [NonSerialized] public string nodePreviewDirectoryPath;
        [NonSerialized] public string nodeAtlasDirectoryPath;
        [NonSerialized] public List<string> visualInputPaths = new List<string>();
        public int canvasLeft;
        public int canvasTop;
        public int psdWidth;
        public int psdHeight;
        public int width;
        public int height;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAnalysisNodeEntry
    {
        public string id;
        public string shortId;
        public string parentId;
        public string name;
        public string layerName;
        public string referenceKind;
        public string referenceKey;
        public string text;
        public AiTextStyle textStyle;
        public string layerType;
        public bool isGroupLayer;
        public bool isTextLayer;
        public int sourceIndex;
        public int sourceLayerId = -1;
        public RectData rect;
        public string imageFile;
        [NonSerialized] public string visualHash;
        public int renderLeafCount;
        public AiAtlasRef atlas;
        public bool active;
        public int siblingIndex;
        public int childCount;
        public int drawIndex = -1;
        public int leafStart = -1;
        public int leafEnd = -1;
        public bool compositeBoundary;
        public bool clipping;
        public bool requiresBackdrop;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAtlasRef
    {
        public string page;
        public int cell;
        public string label;
        [NonSerialized] public RectData imageRect;
        [NonSerialized] public RectData labelRect;
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
