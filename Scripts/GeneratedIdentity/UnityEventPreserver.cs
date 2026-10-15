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
using UnityEditor;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    /// <summary>
    /// UnityEvent 持久监听保留器。
    /// ComponentUtility.CopyComponent/PasteComponentValues 会把模板的空事件值覆盖到复用组件上
    /// (RebuildRootComponentsInPlace 的组件重建同样会销毁旧组件),因此覆盖生成时必须先捕获
    /// m_OnClick 等含 m_PersistentCalls 的序列化事件字段,重建后写回同型组件。
    /// 新旧组件同处一个 contents 场景,objectReferenceValue 直接解析到活对象,无 GUID 重映射问题。
    /// </summary>
    internal static class UnityEventPreserver
    {
        private sealed class CapturedCall
        {
            public Object Target;
            public string MethodName;
            public string TargetAssemblyTypeName;
            public string ObjectArgumentAssemblyTypeName;
            public Object ObjectArgument;
            public int IntArgument;
            public float FloatArgument;
            public string StringArgument;
            public bool BoolArgument;
            public int CallState;
            public bool HasMode;
            public int Mode;
        }

        private sealed class CapturedEventField
        {
            public string PropertyPath;
            public List<CapturedCall> Calls = new List<CapturedCall>();
        }

        private sealed class CapturedEvents
        {
            public string ComponentTypeName;
            public int ComponentTypeOrdinal;
            public List<CapturedEventField> Fields = new List<CapturedEventField>();
        }

        /// <summary>
        /// 捕获根节点上所有组件的序列化事件字段(含 m_PersistentCalls 的字段即为事件字段,通用探测不硬编码类型)。
        /// </summary>
        public static object Capture(GameObject root)
        {
            var result = new List<CapturedEvents>();
            if (root == null) return result;

            var components = root.GetComponents<Component>();
            var typeCounts = new Dictionary<string, int>();
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null) continue;
                var componentTypeName = component.GetType().AssemblyQualifiedName;
                typeCounts.TryGetValue(componentTypeName, out int componentTypeOrdinal);
                typeCounts[componentTypeName] = componentTypeOrdinal + 1;
                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                var captured = new CapturedEvents
                {
                    ComponentTypeName = componentTypeName,
                    ComponentTypeOrdinal = componentTypeOrdinal,
                };

                while (prop.NextVisible(true))
                {
                    var calls = prop.FindPropertyRelative("m_PersistentCalls.m_Calls");
                    if (calls == null || !calls.isArray) continue;

                    var field = new CapturedEventField { PropertyPath = prop.propertyPath };
                    for (int j = 0; j < calls.arraySize; j++)
                    {
                        var call = calls.GetArrayElementAtIndex(j);
                        var modeProp = call.FindPropertyRelative("m_Mode");
                        field.Calls.Add(new CapturedCall
                        {
                            Target = call.FindPropertyRelative("m_Target")?.objectReferenceValue,
                            MethodName = call.FindPropertyRelative("m_MethodName")?.stringValue,
                            TargetAssemblyTypeName = call.FindPropertyRelative("m_TargetAssemblyTypeName")?.stringValue,
                            ObjectArgumentAssemblyTypeName = call.FindPropertyRelative("m_Arguments.m_ObjectArgumentAssemblyTypeName")?.stringValue,
                            ObjectArgument = call.FindPropertyRelative("m_Arguments.m_ObjectArgument")?.objectReferenceValue,
                            IntArgument = call.FindPropertyRelative("m_Arguments.m_IntArgument")?.intValue ?? 0,
                            FloatArgument = call.FindPropertyRelative("m_Arguments.m_FloatArgument")?.floatValue ?? 0f,
                            StringArgument = call.FindPropertyRelative("m_Arguments.m_StringArgument")?.stringValue,
                            BoolArgument = call.FindPropertyRelative("m_Arguments.m_BoolArgument")?.boolValue ?? false,
                            CallState = call.FindPropertyRelative("m_CallState")?.intValue ?? 0,
                            HasMode = modeProp != null,
                            Mode = modeProp != null ? modeProp.intValue : 0,
                        });
                    }
                    captured.Fields.Add(field);
                }

                if (captured.Fields.Count > 0)
                {
                    result.Add(captured);
                }
            }
            return result;
        }

        /// <summary>
        /// 把捕获的事件写回根节点上同类型组件(按捕获条目驱动:每个捕获条目只恢复一次,
        /// 避免同类型多组件时互相清空)。未捕获的事件字段保持现状(组件复用时其事件本就保留)。
        /// 目标组件缺失(换型被销毁)时事件随旧组件消亡,记警告。
        /// </summary>
        public static void Restore(GameObject root, object capturedData)
        {
            if (root == null || !(capturedData is List<CapturedEvents> captured) || captured.Count < 1) return;

            var components = root.GetComponents<Component>();
            for (int i = 0; i < captured.Count; i++)
            {
                var capturedEntry = captured[i];
                Component target = null;
                int ordinal = 0;
                for (int j = 0; j < components.Length; j++)
                {
                    var component = components[j];
                    if (component != null
                        && component.GetType().AssemblyQualifiedName == capturedEntry.ComponentTypeName)
                    {
                        if (ordinal == capturedEntry.ComponentTypeOrdinal)
                        {
                            target = component;
                            break;
                        }
                        ordinal++;
                    }
                }

                if (target == null)
                {
                    int persistentCallCount = 0;
                    for (int j = 0; j < capturedEntry.Fields.Count; j++)
                    {
                        persistentCallCount += capturedEntry.Fields[j].Calls.Count;
                    }
                    if (persistentCallCount > 0)
                    {
                        Debug.LogWarning(
                            $"覆盖生成: 组件重建导致 {persistentCallCount} 个持久事件调用无法保留(旧组件类型 {capturedEntry.ComponentTypeName} 已不存在)");
                    }
                    continue;
                }

                var so = new SerializedObject(target);
                for (int j = 0; j < capturedEntry.Fields.Count; j++)
                {
                    var field = capturedEntry.Fields[j];
                    var prop = so.FindProperty(field.PropertyPath);
                    if (prop == null) continue;
                    var calls = prop.FindPropertyRelative("m_PersistentCalls.m_Calls");
                    if (calls == null || !calls.isArray) continue;

                    calls.arraySize = field.Calls.Count;
                    for (int callIndex = 0; callIndex < field.Calls.Count; callIndex++)
                    {
                        WriteCall(calls.GetArrayElementAtIndex(callIndex), field.Calls[callIndex]);
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void WriteCall(SerializedProperty call, CapturedCall data)
        {
            call.FindPropertyRelative("m_Target").objectReferenceValue = data.Target;
            call.FindPropertyRelative("m_MethodName").stringValue = data.MethodName;
            if (!string.IsNullOrEmpty(data.TargetAssemblyTypeName))
            {
                call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = data.TargetAssemblyTypeName;
            }
            var args = call.FindPropertyRelative("m_Arguments");
            if (args != null)
            {
                if (!string.IsNullOrEmpty(data.ObjectArgumentAssemblyTypeName))
                {
                    args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = data.ObjectArgumentAssemblyTypeName;
                }
                args.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = data.ObjectArgument;
                args.FindPropertyRelative("m_IntArgument").intValue = data.IntArgument;
                args.FindPropertyRelative("m_FloatArgument").floatValue = data.FloatArgument;
                args.FindPropertyRelative("m_StringArgument").stringValue = data.StringArgument;
                args.FindPropertyRelative("m_BoolArgument").boolValue = data.BoolArgument;
            }
            call.FindPropertyRelative("m_CallState").intValue = data.CallState;
            if (data.HasMode)
            {
                var modeProp = call.FindPropertyRelative("m_Mode");
                if (modeProp != null)
                {
                    modeProp.intValue = data.Mode;
                }
            }
        }
    }
}
#endif
