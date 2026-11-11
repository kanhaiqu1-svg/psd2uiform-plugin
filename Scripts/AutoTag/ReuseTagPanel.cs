#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UGF.EditorTools.Psd2UGUI
{
    /// <summary>
    /// Fatcat定制: 重复素材（复用）检测面板。
    /// Inspector 里的一段：跑检测 -> 勾选 -> 一键改名成 `ref xxx`。
    /// 只改 GameObject 名和 SourceLayerName，不动 UIType / 层级 / 素材绑定。
    /// </summary>
    internal static class ReuseTagPanel
    {
        private static Psd2UIFormConverter owner;
        private static List<ReuseSuggestion> suggestions;
        private static string status;
        private static MessageType statusType = MessageType.None;
        private static Vector2 scroll;

        internal static void Draw(Psd2UIFormConverter converter)
        {
            if (converter == null) return;
            if (!ReferenceEquals(owner, converter))
            {
                owner = converter;
                suggestions = null;
                status = null;
                statusType = MessageType.None;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                new GUIContent("重复素材 / 复用标记",
                    "按渲染像素比对找出完全一致的图层，一键改名为 ref <源图层>。纯本地比对，不调用 AI。"),
                EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("查找重复素材",
                    "只比对尺寸相同的图层；像素完全一致才会出现在结果里。"), GUILayout.Height(24)))
            {
                Run(converter);
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.HelpBox(status, statusType);
            }

            if (suggestions == null || suggestions.Count == 0) { EditorGUILayout.EndVertical(); return; }

            int selected = suggestions.Count(s => s.Selected);
            EditorGUILayout.LabelField(
                $"发现 {suggestions.Count} 个冗余图层（可省 {suggestions.Count} 张导出），已选 {selected} 个",
                EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(52)))
                foreach (var s in suggestions) s.Selected = true;
            if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(60)))
                foreach (var s in suggestions) s.Selected = false;
            if (GUILayout.Button("复制清单", EditorStyles.miniButton, GUILayout.Width(72)))
                EditorGUIUtility.systemCopyBuffer = BuildClipboardText();
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(220));
            foreach (var s in suggestions)
            {
                // prefab 重新导入/节点被销毁后旧结果会失效，跳过而不是报空引用。
                if (s.Node == null || s.Source == null) continue;
                EditorGUILayout.BeginHorizontal();
                s.Selected = EditorGUILayout.Toggle(s.Selected, GUILayout.Width(18));
                EditorGUILayout.LabelField(
                    $"{s.Node.name}  →  ref {s.Source.ResourceName}",
                    GUILayout.MinWidth(180));
                EditorGUILayout.LabelField($"{s.Width}×{s.Height}", EditorStyles.miniLabel, GUILayout.Width(80));
                if (!string.IsNullOrEmpty(s.Warning))
                {
                    EditorGUILayout.LabelField(new GUIContent("⚠", s.Warning), GUILayout.Width(18));
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUI.BeginDisabledGroup(selected == 0);
            if (GUILayout.Button($"应用选中（{selected}）", GUILayout.Height(26)))
            {
                Apply(converter);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private static void Run(Psd2UIFormConverter converter)
        {
            try
            {
                suggestions = ReuseTagFinder.Find(converter, out string error);
                status = error ?? $"比对完成：{suggestions.Count} 个可复用图层。确认后勾选并应用。";
                statusType = suggestions.Count > 0 ? MessageType.Info : MessageType.None;
            }
            catch (Exception ex)
            {
                suggestions = null;
                status = "检测失败: " + ex;
                statusType = MessageType.Error;
                Debug.LogError("[Psd2UIForm] 查找重复素材失败: " + ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Apply(Psd2UIFormConverter converter)
        {
            var chosen = suggestions.Where(s => s.Selected && s.Node != null).ToList();
            if (chosen.Count == 0) return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("标记复用(Fatcat定制)");
            int applied = 0;
            var issues = new List<string>();

            foreach (var suggestion in chosen)
            {
                try
                {
                    var node = suggestion.Node;
                    Undo.RecordObject(node, "标记复用");
                    Undo.RecordObject(node.gameObject, "标记复用");

                    // 名字要同时写两处：SourceLayerName 供标签解析，GameObject 名供 ref 解析
                    // （GetReferenceSourceName 在两者不一致时优先取 GameObject 名）。
                    node.SetSourceLayerName(suggestion.NewRawName);
                    node.gameObject.name = suggestion.NewObjectName;
                    node.markToExport = false;

                    EditorUtility.SetDirty(node);
                    EditorUtility.SetDirty(node.gameObject);
                    applied++;
                }
                catch (Exception ex)
                {
                    issues.Add(suggestion.Node.name + ": " + ex.Message);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            // 名字变了，按名字建的查找表必须重建，否则 ref 目标查不到。
            // 这里不调 RefreshAllLayerNodeHelpers：改名不改类型，而且它会连带触发
            // sprite 绑定/导出等副作用。后续【导出Images】【生成UIForm】本来就会逐节点刷新。
            foreach (var node in converter.GetComponentsInChildren<PsdLayerNode>(true))
            {
                if (node != null) EditorUtility.SetDirty(node);
            }
            converter.InvalidateResourceCatalog();
            converter.ApplyGroupDerivedStates();

            Scene scene = chosen[0].Node != null ? chosen[0].Node.gameObject.scene : default;
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();

            suggestions = null;
            if (issues.Count == 0)
            {
                status = $"已标记 {applied} 个复用图层。prefab 需要 Ctrl+S 保存；之后点【导出Images】【生成UIForm】，重复图不会再产出。";
                statusType = MessageType.Info;
                Debug.Log($"[Psd2UIForm] 已标记 {applied} 个复用图层（Fatcat定制）。");
            }
            else
            {
                status = $"已标记 {applied} 个，{issues.Count} 个失败：\n" + string.Join("\n", issues);
                statusType = MessageType.Warning;
                Debug.LogWarning($"[Psd2UIForm] 标记复用完成 {applied} 个，失败 {issues.Count} 个：\n" + string.Join("\n", issues));
            }
        }

        private static string BuildClipboardText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("图层\t复用目标\t尺寸\t说明");
            foreach (var s in suggestions)
            {
                if (s.Node == null || s.Source == null) continue;
                sb.Append(s.Node.name).Append('\t')
                  .Append("ref ").Append(s.Source.ResourceName).Append('\t')
                  .Append(s.Width).Append('×').Append(s.Height).Append('\t')
                  .AppendLine(s.Warning ?? "像素完全一致");
            }
            return sb.ToString();
        }
    }
}
#endif
