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
    internal sealed class AiPatchDocument
    {
        public string version;
        public string providerId;
        public string jobId;
        public string treeHash;
        public List<AiAuditEntry> analysis = new List<AiAuditEntry>();
        public List<AiPatchOperation> operations = new List<AiPatchOperation>();
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiAuditEntry
    {
        public string targetId;
        public string ownerId;
        public string semanticKind;
        public string currentUIType;
        public string predictedUIType;
        public string verdict;
        public float confidence;
        public string reason;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Feature = "renaming", Exclude = true, ApplyToMembers = true)]
    internal sealed class AiPatchOperation
    {
        public string op;
        public string id;
        public string targetId;
        public string parentId;
        public string newParentId;
        public int insertIndex = -1;
        public string name;
        public string uiType;
        public float confidence;
        public string reason;
    }

    internal enum AiPatchOperationKind
    {
        None = 0,
        CreateGroup = 1,
        MoveNode = 2,
        FlattenGroup = 3,
        SetUIType = 4,
        RenameNode = 5,
        DeleteGeneratedGroup = 6
    }

    internal static class AiPatchOperationNames
    {
        internal const string CreateGroup = "create_group";
        internal const string MoveNode = "move_node";
        internal const string FlattenGroup = "flatten_group";
        internal const string SetUIType = "set_ui_type";
        internal const string RenameNode = "rename_node";
        internal const string DeleteGeneratedGroup = "delete_generated_group";

        internal static bool TryParse(string value, out AiPatchOperationKind kind)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case CreateGroup:
                    kind = AiPatchOperationKind.CreateGroup;
                    return true;
                case MoveNode:
                    kind = AiPatchOperationKind.MoveNode;
                    return true;
                case FlattenGroup:
                    kind = AiPatchOperationKind.FlattenGroup;
                    return true;
                case SetUIType:
                    kind = AiPatchOperationKind.SetUIType;
                    return true;
                case RenameNode:
                    kind = AiPatchOperationKind.RenameNode;
                    return true;
                case DeleteGeneratedGroup:
                    kind = AiPatchOperationKind.DeleteGeneratedGroup;
                    return true;
                default:
                    kind = AiPatchOperationKind.None;
                    return false;
            }
        }
    }
}
#endif
