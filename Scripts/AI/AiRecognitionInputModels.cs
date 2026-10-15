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
        public AiRecognitionInputDocumentInfo document;
        public int nodeCount;
        public string nodeFilePath;
        public string nodeColumns = "id\tparent\tname\tlayerName\tkind\tdraw(first,last)\trect(x,y,w,h)\tatlas(page:cell)\tdetail\tactive(1/0)\ttext\treuse\ttextStyle";
        public string[] atlasPaths;
        public List<AiRecognitionHelperContract> helperContracts;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiRecognitionInputDocumentInfo
    {
        // All image references below are relative to this directory and never carry
        // the Unity project path into the CLI session.
        public string imageRoot;
        public string previewImagePath;
        public string annotatedPreviewImagePath;
        public int width;
        public int height;
        // 附件里的整图可能被等比缩小，而 width/height 和 nodes.tsv 的 rect 始终是原始画布坐标。
        public float previewScale = 1f;
    }

}
#endif
