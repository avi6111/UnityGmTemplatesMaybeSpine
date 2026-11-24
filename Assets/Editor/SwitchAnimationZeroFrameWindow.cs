using UnityEditor;
using UnityEngine;

public class SwitchAnimationZeroFrameWindow : EditorWindow
{
    private const string RelativeAnimationMenuItem =
        "GameObject/生成 Animation/Make Relative Offset(用 Editor Win)";

    [MenuItem("Tools/生成 Animation - 动画偏移归零工具", false, 102)]
    private static void OpenWindow()
    {
        GetWindow<SwitchAnimationZeroFrameWindow>("动画偏移归零工具");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "动画相对偏移工具位于 GameObject 菜单中：",
            EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField(
            "GameObject > 生成 Animation > Make Relative Offset(用 Editor Win)",
            EditorStyles.wordWrappedLabel);

        if (GUILayout.Button("打开 DebugAnimationRelativeWindow", GUILayout.Height(30)) &&
            !EditorApplication.ExecuteMenuItem(RelativeAnimationMenuItem))
        {
            Debug.LogWarning($"无法执行菜单项：{RelativeAnimationMenuItem}");
        }
    }
}