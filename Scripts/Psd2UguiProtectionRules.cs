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
using System.Reflection;

// Unity / Editor convention callbacks and MonoBehaviour/UIBehaviour messages are discovered by
// name and should not be renamed.
[assembly: Obfuscation(
    Feature = @"rule regex: (-renaming) (namespace=UGF\.EditorTools\.Psd2UGUI; types=.*) (methods=Awake|OnAwake|Reset|Start|Update|FixedUpdate|LateUpdate|OnEnable|OnDisable|OnDestroy|OnApplicationFocus|OnApplicationPause|OnApplicationQuit|OnBeforeTransformParentChanged|OnTransformParentChanged|OnTransformChildrenChanged|OnRectTransformDimensionsChange|OnCanvasGroupChanged|OnCanvasHierarchyChanged|OnDidApplyAnimationProperties|OnGUI|OnValidate|OnFocus|OnLostFocus|OnSelectionChange|OnHierarchyChange|OnProjectChange|OnInspectorUpdate|OnInspectorGUI|OnSceneGUI|OnPreviewGUI|OnPreviewSettings|CreateGUI|CreateInspectorGUI|OnBeforeSerialize|OnAfterDeserialize|HasPreviewGUI|GetInfoString|GetPropertyHeight|OnDrawGizmos|OnDrawGizmosSelected|OnRenderObject|OnPostRender|OnPreRender|OnWillRenderObject|OnBecameVisible|OnBecameInvisible|OnAnimatorMove|OnAnimatorIK)",
    Exclude = false)]

// EditorWindow subclasses are restored by Unity editor state/layout metadata and should keep
// their type names and standard callback members stable.
[assembly: Obfuscation(
    Feature = @"rule regex: (-renaming) where (inherited_public) (namespace=UnityEditor; types=EditorWindow) (-typenames; methods=OnEnable|OnDisable|OnFocus|OnGUI|OnInspectorUpdate|CreateGUI; fields=m_.*)",
    Exclude = false)]

// UGUIParser stores helper type names as strings in Psd2UIFormConfig.asset and resolves them
// via Type.GetType at runtime, so helper concrete type names must remain stable.
[assembly: Obfuscation(
    Feature = @"rule regex: (-renaming) where (inherited_public) (namespace=UGF\.EditorTools\.Psd2UGUI; types=UIHelperBase) (-typenames)",
    Exclude = false)]
#endif
