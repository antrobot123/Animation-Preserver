using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;

public class HierarchyAnimationPathUpdater : EditorWindow
{
    private GameObject sourceObject;
    private GameObject targetParent;

    private Dictionary<string, string> pathMap = new(); // oldPath → newPath
    private List<(AnimationClip, EditorCurveBinding, string newPath)> previewResults = new();

    private Vector2 scrollPos;

    [MenuItem("antrobot/Animation Preserver")]
    public static void ShowWindow()
    {
        GetWindow<HierarchyAnimationPathUpdater>("Animation Preserver");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Hierarchy Move Configuration", EditorStyles.boldLabel);
        sourceObject = (GameObject)EditorGUILayout.ObjectField("Object to Move", sourceObject, typeof(GameObject), true);
        targetParent = (GameObject)EditorGUILayout.ObjectField("New Parent", targetParent, typeof(GameObject), true);

        if (GUILayout.Button("Preview Affected Clips"))
        {
            if (!sourceObject || !targetParent)
            {
                Debug.LogWarning("Please assign both source object and target parent.");
                return;
            }

            CachePathMapByString();
            Debug.Log("Cache path size: " + pathMap.Count);
            previewResults = GeneratePreview();
            Debug.Log("Preview results count: " + previewResults.Count);
        }

        if (previewResults.Count > 0)
        {

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Preview Results", EditorStyles.boldLabel);
            GUILayout.Label($"Found {previewResults.Count} affected bindings", EditorStyles.helpBox);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(300));
            foreach (var (clip, binding, newPath) in previewResults)
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label($"Clip: {clip.name}");
                GUILayout.Label($"Property: {binding.propertyName}");
                GUILayout.Label($"Old Path: {binding.path}");
                GUILayout.Label($"New Path: {newPath}");
                GUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(5);
            if (GUILayout.Button("Apply Path Changes"))
                ApplyChanges();
        }
        else
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Preview Results", EditorStyles.boldLabel);
            GUILayout.Label($"No affected bindings found.", EditorStyles.helpBox);
        }
    }

    private void CachePathMapByString()
    {
        pathMap.Clear();
        // Get original paths
        Dictionary<Transform, string> old = new();
        foreach (Transform t in sourceObject.GetComponentsInChildren<Transform>(true))
            old[t] = AnimationUtility.CalculateTransformPath(t, sourceObject.transform.root);

        // Simulate new paths via clone
        GameObject clone = Instantiate(sourceObject);
        clone.hideFlags = HideFlags.HideAndDontSave;
        clone.transform.SetParent(targetParent.transform, false);
        clone.name = sourceObject.name;
        clone.SetActive(false);

        Dictionary<Transform, string> newDict = new();
        foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
        {
            //Debug.Log(t.name);
            newDict[t] = AnimationUtility.CalculateTransformPath(t, clone.transform.root);
        }
        // Map based on local hierarchy path (name + sibling order)
        foreach (var kvp in old)
        {
            string keyPath = kvp.Value;
            foreach (var newKvp in newDict)
            {
                if (newKvp.Key.name == kvp.Key.name) // crude match — could refine by full path later
                {
                    pathMap[keyPath] = newKvp.Value;
                    break;
                }
            }
        }

        DestroyImmediate(clone);
    }

    private List<(AnimationClip, EditorCurveBinding, string newPath)> GeneratePreview()
    {
        var results = new List<(AnimationClip, EditorCurveBinding, string)>();
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip");

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            var floatBindings = AnimationUtility.GetCurveBindings(clip);

            foreach (var binding in floatBindings)
            {
                if (binding.path == null) continue;
                if (binding.path == "") continue;
                if (pathMap.ContainsKey(binding.path))
                {
                    results.Add((clip, binding, pathMap[binding.path]));
                }
            }
            var objectRefBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var binding in objectRefBindings)
            {
                if (binding.path == null) continue;
                if (binding.path == "") continue;
                if (pathMap.ContainsKey(binding.path))
                {
                    results.Add((clip, binding, pathMap[binding.path]));
                }
            }
        }
        return results;
    }
    public static void MarkPrefabDirty(GameObject prefabRoot)
    {
        if (prefabRoot == null) return;

        // Marks the object itself as dirty
        EditorUtility.SetDirty(prefabRoot);

        // If it's part of an open prefab stage, also mark the prefab asset dirty
        var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null && prefabStage.prefabContentsRoot == prefabRoot)
        {
            EditorSceneManager.MarkSceneDirty(prefabStage.scene);
        }
    }
    public static GameObject GetNearestPrefabRoot(GameObject obj)
    {
        if (obj == null)
            return null;

        // Traverse up the hierarchy to find the root of the prefab
        Transform current = obj.transform;
        while (current.parent != null)
        {
            current = current.parent;
        }

        GameObject root = current.gameObject;

        // Check if this root object is part of a prefab editing stage
        var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null && prefabStage.scene.IsValid())
        {
            // Confirm this object is inside the prefab being edited
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot == root)
            {
                return root;
            }
        }

        // Alternatively, check if the object is part of a prefab instance
        if (PrefabUtility.IsPartOfPrefabInstance(obj) || PrefabUtility.IsPartOfPrefabAsset(obj))
        {
            return PrefabUtility.GetNearestPrefabInstanceRoot(obj);
        }

        return null;
    }


    private void ApplyChanges()
    {
        sourceObject.transform.SetParent(targetParent.transform, false);

        foreach (var (clip, oldBinding, newPath) in previewResults)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, oldBinding);

            var newBinding = oldBinding;
            newBinding.path = newPath;

            AnimationUtility.SetEditorCurve(clip, oldBinding, null);
            AnimationUtility.SetEditorCurve(clip, newBinding, curve);
            EditorUtility.SetDirty(clip);
        }
        //if in prefab, mark it as dirty
        GameObject prefabRoot = GetNearestPrefabRoot(sourceObject);
        if (prefabRoot != null)
        {
            MarkPrefabDirty(prefabRoot);
        }


        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        previewResults.Clear();
        //remove 'Object to Move' from input slot, since it has been moved
        sourceObject = null;

        Debug.Log("Animation paths updated and hierarchy moved successfully.");
    }
}