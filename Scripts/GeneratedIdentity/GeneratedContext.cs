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
using System.Collections.Generic;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    /// <summary>
    /// 单次生成的内存态。取代旧的 PsdGeneratedKey 组件:身份信息不再挂到生成对象上,
    /// 全部驻留内存,保证最终 prefab 纯净。编辑器单线程流程,以 static Current 传递。
    /// </summary>
    internal sealed class GenerationContext
    {
        public static GenerationContext Current { get; private set; }

        /// <summary>期望节点 → 生成的 GameObject(Build 阶段填充)。</summary>
        public Dictionary<GeneratedTreeNode, GameObject> DesiredToGenerated = new Dictionary<GeneratedTreeNode, GameObject>();

        /// <summary>源层节点 → 期望树节点(后处理查表用)。</summary>
        public Dictionary<PsdLayerNode, GeneratedTreeNode> DesiredNodeByLayer = new Dictionary<PsdLayerNode, GeneratedTreeNode>();
        // Includes controls created inside another Helper's template, such as Dropdown's ScrollView.
        public Dictionary<PsdLayerNode, GameObject> HelperRoots = new Dictionary<PsdLayerNode, GameObject>();

        /// <summary>本次生成/复用的容器节点(FitGeneratedContainerHierarchy 判定用)。</summary>
        public HashSet<GameObject> GeneratedContainerNodes = new HashSet<GameObject>();
        // Generation-only evidence: source element -> actual template Graphic placement.
        public List<AiRecognitionIssue> GenerationIssues = new List<AiRecognitionIssue>();
        public int BasicElementCount;
        public HashSet<string> WriteScope;
        public readonly HashSet<GameObject> FailedOutputRoots = new HashSet<GameObject>();
        public readonly HashSet<Object> ExistingOutputObjects = new HashSet<Object>();
        public readonly HashSet<GameObject> RetiredObjects = new HashSet<GameObject>();
        public Dictionary<Transform, PsdLayerNode> GraphicSources = new Dictionary<Transform, PsdLayerNode>();

        /// <summary>未认领的旧生成节点 → 孤儿化(Build 阶段处理)。</summary>
        public List<GameObject> NodesToOrphan = new List<GameObject>();

        private static readonly Stack<GenerationContext> previousContexts = new Stack<GenerationContext>();

        /// <summary>
        /// 栈式上下文:嵌套导出(共享 prefab 引用在生成过程中递归调 ExportLayerTreeAsPrefab)时,
        /// 内层 Begin 保存外层上下文,End 恢复之,避免外层生成在嵌套返回后读到空 Current(NRE/静默跳过)。
        /// </summary>
        public static GenerationContext Begin()
        {
            previousContexts.Push(Current);
            Current = new GenerationContext();
            return Current;
        }

        public static void End()
        {
            Current = previousContexts.Count > 0 ? previousContexts.Pop() : null;
        }
    }

    [System.Serializable]
    internal sealed class PsdGenerationReport
    {
        public string prefabPath;
        public string status;
        public int generatedUnitCount;
        public int retainedUnitCount;
        public int unresolvedUnitCount;
        public int basicElementCount;
        public long elapsedMilliseconds;
        public List<AiRecognitionIssue> issues;
    }

    internal enum NodeMatchKind
    {
        Create,           // 新建
        ReuseSameType,    // 复用(GO+组件全部保留;兼容性由 helper 判定)
        ReviveOrphan,     // 孤儿复活(原 fileID 复活)
    }
}
#endif
