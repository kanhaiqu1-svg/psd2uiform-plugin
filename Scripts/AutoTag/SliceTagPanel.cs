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
    /// Fatcat定制: 九宫格自动判定面板。
    /// 跑规则判定 -> 勾选 -> 给选中图层追加 .sliced 标签。
    /// 只改 SourceLayerName，不动 UIType / 层级 / 素材绑定；边框由生成时的算法算。
    /// </summary>
    internal static class SliceTagPanel
    {
        private static Psd2UIFormConverter owner;
        private static List<SliceSuggestion> suggestions;
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
                new GUIContent("九宫格自动判定",
                    "用插件自己的边框算法判断哪些贴图适合九宫格，条件：四边都>1、中心区>=10%、左右上下大致对称。复用(ref)贴图同样参与判定，同一张图的所有实例共享同一份边框。纯本地计算，不调用 AI。"),
                EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("判定九宫格",
                    "只看已勾选导出或已复用的图层，且没有显式 imageType 标签。"),
                GUILayout.Height(24)))
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
            EditorGUILayout.LabelField($"发现 {suggestions.Count} 个候选，已选 {selected} 个", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选", EditorStyles.miniButton, GUILayout.Width(52)))
                foreach (var s in suggestions) s.Selected = true;
            if (GUILayout.Button("全不选", EditorStyles.miniButton, GUILayout.Width(60)))
                foreach (var s in suggestions) s.Selected = false;
            if (GUILayout.Button("复制清单", EditorStyles.miniButton, GUILayout.Width(72)))
                EditorGUIUtility.systemCopyBuffer = BuildClipboardText();
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(200));
            foreach (var s in suggestions)
            {
                if (s.Node == null) continue;
                EditorGUILayout.BeginHorizontal();
                s.Selected = EditorGUILayout.Toggle(s.Selected, GUILayout.Width(18));
                EditorGUILayout.LabelField(s.Node.name, GUILayout.MinWidth(140));
                EditorGUILayout.LabelField(
                    $"{s.Width}×{s.Height}  边 {s.Border.x:0}/{s.Border.y:0}/{s.Border.z:0}/{s.Border.w:0}  中心 {s.CenterWidth}×{s.CenterHeight}",
                    EditorStyles.miniLabel, GUILayout.MinWidth(210));
                if (!string.IsNullOrEmpty(s.Note))
                {
                    EditorGUILayout.LabelField(new GUIContent("↺", s.Note), GUILayout.Width(18));
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField(
                "边框顺序：左/下/右/上。边框属于贴图：同一张图被打标的所有实例共享一份边框，生成时写入（仅在空白时）。↺ 表示该节点是复用，边框取自它的源图层。",
                EditorStyles.miniLabel);

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
                var found = SliceTagFinder.Find(converter, out string message);
                if (!string.IsNullOrEmpty(message) && message.Contains("已取消"))
                {
                    suggestions = null;
                    status = message;
                    statusType = MessageType.Warning;
                    return;
                }

                suggestions = found;
                if (found.Count > 0)
                {
                    status = $"判定完成：{found.Count} 个候选。"
                             + (string.IsNullOrEmpty(message) ? string.Empty : " " + message);
                    statusType = MessageType.Info;
                }
                else
                {
                    status = message ?? "没有候选。";
                    statusType = MessageType.None;
                }
            }
            catch (Exception ex)
            {
                suggestions = null;
                status = "判定失败: " + ex;
                statusType = MessageType.Error;
                Debug.LogError("[Psd2UIForm] 九宫格判定失败: " + ex);
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
            Undo.SetCurrentGroupName("标记九宫格(Fatcat定制)");
            int applied = 0;
            var issues = new List<string>();

            foreach (var suggestion in chosen)
            {
                try
                {
                    var node = suggestion.Node;
                    Undo.RecordObject(node, "标记九宫格");
                    // 标签只从 SourceLayerName 读(ResolveFinalImageType)，GameObject 名不用动。
                    node.SetSourceLayerName(suggestion.NewRawName);
                    EditorUtility.SetDirty(node);
                    applied++;
                }
                catch (Exception ex)
                {
                    issues.Add(suggestion.Node.name + ": " + ex.Message);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

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
                status = $"已给 {applied} 个图层打上 .sliced。prefab 需 Ctrl+S；之后【导出Images】【生成UIForm】会写入边框，再跑 Crop Minimal 9-Slice 裁掉多余范围。";
                statusType = MessageType.Info;
                Debug.Log($"[Psd2UIForm] 已标记 {applied} 个九宫格图层（Fatcat定制）。");
            }
            else
            {
                status = $"已标记 {applied} 个，{issues.Count} 个失败：\n" + string.Join("\n", issues);
                statusType = MessageType.Warning;
                Debug.LogWarning($"[Psd2UIForm] 标记九宫格完成 {applied} 个，失败 {issues.Count} 个：\n" + string.Join("\n", issues));
            }
        }

        private static string BuildClipboardText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("图层\t尺寸\t边框(左/下/右/上)\t中心区\t备注");
            foreach (var s in suggestions)
            {
                if (s.Node == null) continue;
                sb.Append(s.Node.name).Append('\t')
                  .Append(s.Width).Append('×').Append(s.Height).Append('\t')
                  .Append($"{s.Border.x:0}/{s.Border.y:0}/{s.Border.z:0}/{s.Border.w:0}").Append('\t')
                  .Append(s.CenterWidth).Append('×').Append(s.CenterHeight).Append('\t')
                  .AppendLine(s.Note ?? string.Empty);
            }
            return sb.ToString();
        }
    }
}
#endif
