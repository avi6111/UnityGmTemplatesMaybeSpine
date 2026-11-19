using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SwitchGameObjectUIWindow : EditorWindow
{
    [SerializeField] private GameObject first;
    [SerializeField] private GameObject second;
    private string firstId;
    private string secondId;
    private SwitchState pendingSwitchState;

    private static string PreferencesPrefix =>
        "GameObjectSwitchWindow." + Hash128.Compute(Application.dataPath) + ".";
    private static string SwitchStatePath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "GameObjectSwitchWindow.json");

    [Serializable]
    private sealed class SwitchState
    {
        public string firstId;
        public string secondId;
        public bool firstActive;
        public bool secondActive;
    }

    [MenuItem("Tools/Menu场景 -GameObject 切换(UI调试)",false,101)]
    private static void OpenWindow()
    {
        GetWindow<SwitchGameObjectUIWindow>("GameObject 切换");
    }

    private void OnEnable()
    {
        firstId = EditorPrefs.GetString(PreferencesPrefix + "First", "");
        secondId = EditorPrefs.GetString(PreferencesPrefix + "Second", "");
        RestoreReferences();
        pendingSwitchState = LoadSwitchState();
        TryRestoreSwitchState();
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EditorSceneManager.sceneSaved += OnSceneSaved;
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        EditorSceneManager.sceneSaved -= OnSceneSaved;
    }

    private void OnDestroy()
    {
        if (first == null && second == null && string.IsNullOrEmpty(firstId) && string.IsNullOrEmpty(secondId))
            return;

        pendingSwitchState = null;
        SwitchToFirst(false);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("关联 GameObject 切换", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("关闭窗口时会启用第一个 GameObject，并禁用第二个。请保存场景以便下次恢复选择。", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        var newFirst = (GameObject)EditorGUILayout.ObjectField("第一个 GameObject", first, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            if (IsSceneObject(newFirst))
            {
                pendingSwitchState = null;
                first = newFirst;
                firstId = SaveReference("First", first);
            }
            else
                Debug.LogWarning("[GameObject 切换] 请选择场景中的对象，不能选择资源文件。");
        }

        EditorGUI.BeginChangeCheck();
        var newSecond = (GameObject)EditorGUILayout.ObjectField("第二个 GameObject", second, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            if (IsSceneObject(newSecond))
            {
                pendingSwitchState = null;
                second = newSecond;
                secondId = SaveReference("Second", second);
            }
            else
                Debug.LogWarning("[GameObject 切换] 请选择场景中的对象，不能选择资源文件。");
        }

        if ((first == null && !string.IsNullOrEmpty(firstId)) ||
            (second == null && !string.IsNullOrEmpty(secondId)))
        {
            EditorGUILayout.HelpBox("上次选择的对象无法找到。请打开对应场景，或重新选择对象。", MessageType.Warning);
            if (GUILayout.Button("清除无法找到的记录"))
            {
                if (first == null)
                    firstId = SaveReference("First", null);
                if (second == null)
                    secondId = SaveReference("Second", null);
            }
        }

        if ((first != null && string.IsNullOrEmpty(firstId)) ||
            (second != null && string.IsNullOrEmpty(secondId)))
        {
            EditorGUILayout.HelpBox("对象尚不能持久记录：请使用场景中的对象并保存场景。", MessageType.Warning);
        }

        bool validPair = first != null && second != null && first != second &&
                         !first.transform.IsChildOf(second.transform) &&
                         !second.transform.IsChildOf(first.transform);
        if (first != null && second != null && !validPair)
        {
            EditorGUILayout.HelpBox("请选择两个不同且不存在父子关系的对象。", MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(!validPair))
        {
            if (GUILayout.Button((first!=null && first.activeSelf) ? "启用第一个(√)" : "启用第一个"))
            {
                pendingSwitchState = null;
                SwitchToFirst();
            }
            if (GUILayout.Button((second!=null && second.activeSelf) ? "启用第二个(√)" : "启用第二个"))
            {
                pendingSwitchState = null;
                ApplySwitch(second, first);
            }
        }
    }

    private static string SaveReference(string key, GameObject target)
    {
        string id = "";
        if (target != null && !EditorUtility.IsPersistent(target) &&
            target.scene.IsValid() && !string.IsNullOrEmpty(target.scene.path))
        {
            GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(target);
            if (globalId.targetObjectId != 0)
                id = globalId.ToString();
        }

        EditorPrefs.SetString(PreferencesPrefix + key, id);
        return id;
    }

    private static bool IsSceneObject(GameObject target)
    {
        return target == null || (!EditorUtility.IsPersistent(target) && target.scene.IsValid());
    }

    private static GameObject ResolveReference(string id)
    {
        GlobalObjectId globalId;
        return !string.IsNullOrEmpty(id) && GlobalObjectId.TryParse(id, out globalId)
            ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as GameObject
            : null;
    }

    private void RestoreReferences()
    {
        if (first == null)
            first = ResolveReference(firstId);
        if (second == null)
            second = ResolveReference(secondId);
    }

    private void OnHierarchyChanged()
    {
        RestoreReferences();
        TryRestoreSwitchState();
        Repaint();
    }

    private void OnSceneSaved(Scene scene)
    {
        if (first != null && first.scene == scene)
            firstId = SaveReference("First", first);
        if (second != null && second.scene == scene)
            secondId = SaveReference("Second", second);
    }

    private void SwitchToFirst(bool recordState = true)
    {
        if (first == null)
        {
            Debug.LogWarning("[GameObject 切换] 无法恢复第一个对象：对象不存在或对应场景未打开。");
            return;
        }

        if (second == null || first == second || first.transform.IsChildOf(second.transform))
        {
            Undo.RecordObject(first, "启用第一个 GameObject");
            first.SetActive(true);
            PrefabUtility.RecordPrefabInstancePropertyModifications(first);
            Debug.LogWarning("[GameObject 切换] 已启用第一个对象，但无法禁用第二个对象，请检查选择及对象的父子关系。", first);
            return;
        }

        ApplySwitch(first, second, recordState);
    }

    private static void ApplySwitch(GameObject enabledObject, GameObject disabledObject, bool recordState = true)
    {
        Undo.RecordObjects(new UnityEngine.Object[] { enabledObject, disabledObject }, "切换 GameObject");
        disabledObject.SetActive(false);
        enabledObject.SetActive(true);
        PrefabUtility.RecordPrefabInstancePropertyModifications(disabledObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications(enabledObject);
        if (recordState)
            SaveSwitchState(enabledObject, disabledObject);
        Debug.Log($"[GameObject 切换] 已启用 {enabledObject.name}，禁用 {disabledObject.name}。", enabledObject);
    }

    private static void SaveSwitchState(GameObject firstObject, GameObject secondObject)
    {
        if (!TryGetPersistentId(firstObject, out string savedFirstId) ||
            !TryGetPersistentId(secondObject, out string savedSecondId))
        {
            Debug.LogWarning("[GameObject 切换] 对象所在场景尚未保存，无法将切换状态记录到 Library。");
            return;
        }

        var state = new SwitchState
        {
            firstId = savedFirstId,
            secondId = savedSecondId,
            firstActive = firstObject.activeSelf,
            secondActive = secondObject.activeSelf
        };

        try
        {
            File.WriteAllText(SwitchStatePath, JsonUtility.ToJson(state));
        }
        catch (IOException exception)
        {
            Debug.LogError($"[GameObject 切换] 无法保存切换状态：{exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.LogError($"[GameObject 切换] 无法保存切换状态：{exception.Message}");
        }
    }

    private static bool TryGetPersistentId(GameObject target, out string id)
    {
        id = "";
        if (target == null || EditorUtility.IsPersistent(target) ||
            !target.scene.IsValid() || string.IsNullOrEmpty(target.scene.path))
            return false;

        GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(target);
        if (globalId.targetObjectId == 0)
            return false;

        id = globalId.ToString();
        return true;
    }

    private static SwitchState LoadSwitchState()
    {
        if (!File.Exists(SwitchStatePath))
            return null;

        try
        {
            return JsonUtility.FromJson<SwitchState>(File.ReadAllText(SwitchStatePath));
        }
        catch (IOException exception)
        {
            Debug.LogError($"[GameObject 切换] 无法读取已保存的切换状态：{exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.LogError($"[GameObject 切换] 无法读取已保存的切换状态：{exception.Message}");
        }
        catch (ArgumentException exception)
        {
            Debug.LogError($"[GameObject 切换] 已保存的切换状态格式无效：{exception.Message}");
        }

        return null;
    }

    private void TryRestoreSwitchState()
    {
        if (pendingSwitchState == null)
            return;

        SwitchState state = pendingSwitchState;
        GameObject savedFirst = ResolveReference(state.firstId);
        GameObject savedSecond = ResolveReference(state.secondId);
        if (savedFirst == null || savedSecond == null)
            return;

        pendingSwitchState = null;
        Undo.RecordObjects(new UnityEngine.Object[] { savedFirst, savedSecond }, "恢复 GameObject 切换状态");
        savedFirst.SetActive(state.firstActive);
        savedSecond.SetActive(state.secondActive);
        PrefabUtility.RecordPrefabInstancePropertyModifications(savedFirst);
        PrefabUtility.RecordPrefabInstancePropertyModifications(savedSecond);
    }
}
