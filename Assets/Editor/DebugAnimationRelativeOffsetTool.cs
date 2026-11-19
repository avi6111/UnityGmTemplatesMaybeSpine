using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class DebugAnimationRelativeOffsetTool
{
    [MenuItem("GameObject/生成 Animation/Make Relative Offset Clip")]
    static void MakeRelativeOffsetClip()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("提示", "请先在 Hierarchy 中选中一个带 Animation 组件的 GameObject", "OK");
            return;
        }

        var animation = go.GetComponent<Animation>();
        if (animation == null)
        {
            EditorUtility.DisplayDialog("提示", "选中的物体没有 Animation 组件", "OK");
            return;
        }

        // 从 Animation 组件里找到所有引用的 Clip
        var clips = AnimationUtility.GetAnimationClips(go);
        if (clips == null || clips.Length == 0)
        {
            EditorUtility.DisplayDialog("提示", "该 Animation 组件没有引用任何 AnimationClip", "OK");
            return;
        }

        foreach (var clip in clips)
        {
            ConvertClipToRelative(clip, go.transform);
        }
    }

    static void ConvertClipToRelative(AnimationClip clip, Transform root)
    {
        var bindings = AnimationUtility.GetCurveBindings(clip);
        if (bindings.Length == 0) return;

        var newClip = Object.Instantiate(clip);
        newClip.name = clip.name + "_Relative";
        newClip.legacy = clip.legacy;

        // 记录每个绑定第 0 帧的基准值
        var baseValues = new Dictionary<EditorCurveBinding, float>();

        foreach (var binding in bindings)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.keys.Length == 0) continue;

            baseValues[binding] = curve.keys[0].value;
        }

        foreach (var binding in bindings)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.keys.Length == 0) continue;

            if (!baseValues.TryGetValue(binding, out float baseValue)) continue;

            var newCurve = new AnimationCurve();
            foreach (var key in curve.keys)
            {
                newCurve.AddKey(new Keyframe(
                    key.time,
                    key.value - baseValue,
                    key.inTangent,
                    key.outTangent
                ));
            }

            AnimationUtility.SetEditorCurve(newClip, binding, newCurve);
        }

        string path = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(path))
        {
            // FBX 内嵌 Clip 无法直接覆盖，保存到 Assets 根目录
            path = "Assets";
        }

        string dir = System.IO.Path.GetDirectoryName(path);
        string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        string ext = System.IO.Path.GetExtension(path);
        string savePath = $"{dir}/{fileName}_Relative{ext}";

        AssetDatabase.CreateAsset(newClip, savePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"已生成相对偏移 Clip: {savePath}", newClip);
    }
}