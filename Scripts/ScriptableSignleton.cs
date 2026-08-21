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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UGF.EditorTools.Psd2UGUI
{
    public abstract class ScriptableSingleton<T> : ScriptableObject where T : ScriptableObject
    {
        private static readonly Regex ScriptReferenceRegex = new Regex(@"^(\s*m_Script:\s*)\{fileID:\s*-?\d+,\s*guid:\s*[0-9a-fA-F]+,\s*type:\s*\d+\}\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex YamlFieldRegex = new Regex(@"^\s{2}([A-Za-z_][A-Za-z0-9_]*):(?:\s*(.*))?$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static T s_Instance;
        internal static T Instance
        {
            get
            {
                if (!s_Instance)
                {
                    LoadOrCreate();
                }
                return s_Instance;
            }
        }
        internal static T LoadOrCreate()
        {
            string filePath = GetFilePath();
            if (!string.IsNullOrEmpty(filePath))
            {
                bool hadSerializedFile = File.Exists(ResolveFilePath(filePath));
                var arr = InternalEditorUtility.LoadSerializedFileAndForget(filePath);
                s_Instance = arr.OfType<T>().FirstOrDefault();
                if (!s_Instance)
                {
                    if (hadSerializedFile && TryLoadLegacySerializedInstance(filePath, out var migratedInstance))
                    {
                        s_Instance = migratedInstance;
                        Save();
                        Debug.LogWarning($"{typeof(T).Name}: 检测到旧版配置反序列化失败，已自动迁移配置文件: {filePath}");
                    }
                    else
                    {
                        s_Instance = CreateInstance<T>();
                        if (hadSerializedFile)
                        {
                            Save();
                            Debug.LogWarning($"{typeof(T).Name}: 配置文件反序列化失败，已重建默认配置: {filePath}");
                        }
                    }
                }
            }
            else
            {
                Debug.LogError($"{nameof(ScriptableSingleton<T>)}: 请设置持久化存档路径！ ");
            }
            return s_Instance;
        }

        internal static void Save(bool saveAsText = true)
        {
            if (!s_Instance)
            {
                return;
            }

            string filePath = GetFilePath();
            if (!string.IsNullOrEmpty(filePath))
            {
                string directoryName = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }
                UnityEngine.Object[] obj = new T[1] { s_Instance };
                InternalEditorUtility.SaveToSerializedFileAndForget(obj, filePath, saveAsText);
            }
        }
        protected static string GetFilePath()
        {
            return typeof(T).GetCustomAttributes(inherit: true)
                  .Cast<FilePathAttribute>()
                  .FirstOrDefault(v => v != null)
                  ?.filepath;
        }

        private static bool TryLoadLegacySerializedInstance(string filePath, out T instance)
        {
            instance = null;
            string resolvedFilePath = ResolveFilePath(filePath);
            if (!File.Exists(resolvedFilePath))
            {
                return false;
            }

            if (TryLoadLegacyInstanceByFixingScriptReference(resolvedFilePath, out instance))
            {
                return true;
            }

            return TryLoadLegacyInstanceByParsingYaml(resolvedFilePath, out instance);
        }

        private static bool TryLoadLegacyInstanceByFixingScriptReference(string filePath, out T instance)
        {
            instance = null;
            if (!TryGetCurrentScriptReference(out string guid, out long fileId))
            {
                return false;
            }

            string yaml = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(yaml))
            {
                return false;
            }

            Match match = ScriptReferenceRegex.Match(yaml);
            if (!match.Success)
            {
                return false;
            }

            string replacement = $"{match.Groups[1].Value}{{fileID: {fileId}, guid: {guid}, type: 3}}";
            string migratedYaml = yaml.Substring(0, match.Index) + replacement + yaml.Substring(match.Index + match.Length);
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"{typeof(T).Name}_{Guid.NewGuid():N}.asset");

            try
            {
                File.WriteAllText(tempFilePath, migratedYaml);
                var arr = InternalEditorUtility.LoadSerializedFileAndForget(tempFilePath);
                instance = arr.OfType<T>().FirstOrDefault();
                return instance != null;
            }
            catch
            {
                instance = null;
                return false;
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        private static bool TryLoadLegacyInstanceByParsingYaml(string filePath, out T instance)
        {
            instance = CreateInstance<T>();
            try
            {
                Dictionary<string, string> values = ParseYamlFields(File.ReadAllText(filePath));
                bool assignedAnyField = false;

                foreach (FieldInfo field in GetSerializableFields())
                {
                    if (!values.TryGetValue(field.Name, out string rawValue))
                    {
                        continue;
                    }

                    if (!TryConvertYamlValue(rawValue, field.FieldType, out object parsedValue))
                    {
                        continue;
                    }

                    field.SetValue(instance, parsedValue);
                    assignedAnyField = true;
                }

                if (!assignedAnyField)
                {
                    DestroyImmediate(instance);
                    instance = null;
                    return false;
                }

                return true;
            }
            catch
            {
                if (instance != null)
                {
                    DestroyImmediate(instance);
                }
                instance = null;
                return false;
            }
        }

        private static bool TryGetCurrentScriptReference(out string guid, out long fileId)
        {
            guid = null;
            fileId = 0;

            T tempInstance = CreateInstance<T>();
            try
            {
                MonoScript script = MonoScript.FromScriptableObject(tempInstance);
                return script && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out guid, out fileId);
            }
            finally
            {
                DestroyImmediate(tempInstance);
            }
        }

        private static Dictionary<string, string> ParseYamlFields(string yaml)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in YamlFieldRegex.Matches(yaml))
            {
                values[match.Groups[1].Value] = match.Groups[2].Value;
            }
            return values;
        }

        private static IEnumerable<FieldInfo> GetSerializableFields()
        {
            return typeof(T)
                  .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                  .Where(field => !field.IsStatic
                               && !field.IsInitOnly
                               && !field.IsLiteral
                               && !field.IsNotSerialized
                               && (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null));
        }

        private static bool TryConvertYamlValue(string rawValue, Type fieldType, out object value)
        {
            string text = rawValue == null ? string.Empty : rawValue.Trim();
            if (fieldType == typeof(string))
            {
                value = UnquoteYamlString(text);
                return true;
            }

            if (fieldType == typeof(bool))
            {
                if (text == "1")
                {
                    value = true;
                    return true;
                }
                if (text == "0")
                {
                    value = false;
                    return true;
                }
                if (bool.TryParse(text, out bool boolValue))
                {
                    value = boolValue;
                    return true;
                }
            }
            else if (fieldType == typeof(int))
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                {
                    value = intValue;
                    return true;
                }
            }
            else if (fieldType == typeof(long))
            {
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
                {
                    value = longValue;
                    return true;
                }
            }
            else if (fieldType == typeof(float))
            {
                if (float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float floatValue))
                {
                    value = floatValue;
                    return true;
                }
            }
            else if (fieldType == typeof(double))
            {
                if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double doubleValue))
                {
                    value = doubleValue;
                    return true;
                }
            }
            else if (fieldType.IsEnum)
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int enumIntValue))
                {
                    value = Enum.ToObject(fieldType, enumIntValue);
                    return true;
                }

                string enumText = UnquoteYamlString(text);
                if (!string.IsNullOrEmpty(enumText))
                {
                    try
                    {
                        value = Enum.Parse(fieldType, enumText, true);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            value = null;
            return false;
        }

        private static string UnquoteYamlString(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "null" || value == "~")
            {
                return value == string.Empty ? string.Empty : null;
            }

            if (value.Length >= 2)
            {
                if (value[0] == '"' && value[value.Length - 1] == '"')
                {
                    return value.Substring(1, value.Length - 2)
                          .Replace("\\\\", "\\")
                          .Replace("\\\"", "\"")
                          .Replace("\\n", "\n")
                          .Replace("\\r", "\r")
                          .Replace("\\t", "\t");
                }

                if (value[0] == '\'' && value[value.Length - 1] == '\'')
                {
                    return value.Substring(1, value.Length - 2).Replace("''", "'");
                }
            }

            return value;
        }

        private static string ResolveFilePath(string filePath)
        {
            return Path.IsPathRooted(filePath) ? filePath : Path.GetFullPath(filePath);
        }
    }
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class FilePathAttribute : Attribute
    {
        internal string filepath;
        /// <summary>
        /// 单例存放路径
        /// </summary>
        /// <param name="path">相对 Project 路径</param>
        internal FilePathAttribute(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Invalid relative path (it is empty)");
            }
            if (path[0] == '/')
            {
                path = path.Substring(1);
            }
            filepath = path;
        }
    }
}
#endif
