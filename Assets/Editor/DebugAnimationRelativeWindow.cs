using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class DebugAnimationRelativeWindow : EditorWindow
{
    private GameObject referenceObject;
    private AnimationClip sourceClip;

    [MenuItem("GameObject/生成 Animation/Make Relative Offset(用 Editor Win)")]
    static void Open()
    {
        var window = GetWindow<DebugAnimationRelativeWindow>("相对偏移工具");
        window.minSize = new Vector2(300, 200);

        // 如果选中了带 Animation 的物体，自动填入
        var go = Selection.activeGameObject;
        if (go != null)
        {
            var anim = go.GetComponent<Animation>();
            if (anim != null)
            {
                var clips = AnimationUtility.GetAnimationClips(go);
                if (clips != null && clips.Length > 0)
                {
                    window.sourceClip = clips[0];
                    window.referenceObject = go;
                }
            }
        }

        window.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("步骤 1：选择原始动画 Clip", EditorStyles.boldLabel);
        sourceClip = (AnimationClip)EditorGUILayout.ObjectField("原始 Clip", sourceClip, typeof(AnimationClip), false);

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("步骤 2：选择参考物体", EditorStyles.boldLabel);
        referenceObject = (GameObject)EditorGUILayout.ObjectField("参考物体", referenceObject, typeof(GameObject), true);

        EditorGUILayout.HelpBox(
            "参考物体应该是动画中实际发生了位移的那个物体。\n" +
            "工具会以它当前的 localPosition 为基准，生成相对偏移动画。",
            MessageType.Info
        );

        EditorGUILayout.Space(10);

        GUI.enabled = sourceClip != null && referenceObject != null;
        if (GUILayout.Button("生成相对偏移 Clip", GUILayout.Height(30)))
        {
            // 调用处：直接把参考物体的三个分量传进去
            ConvertClipToRelative(
                sourceClip
            //   , referenceObject.transform.localPosition,
            //    referenceObject.transform.localRotation,
            //    referenceObject.transform.localScale
            );
        }
        

        GUILayout.Space(10);
        GUILayout.Label("注意事项:\n其实就是让动画“归零”（动画只控制 LocalPos , LocalRot），\n 之前用了节点位置做原点计算，所以节点需要有一个父节点\n（现在已经改了0帧，0 Pos，0 Rotate，所以应该不需要）", EditorStyles.boldLabel);
        GUI.enabled = true;
    }

    static void ConvertClipToRelative(AnimationClip clip)
    {
        ConvertClipToRelative(clip, Vector3.zero, Quaternion.identity, Vector3.zero);
    }
    // 改成传入 position / rot / scale 三个参数
    static void ConvertClipToRelative(AnimationClip clip, Vector3 position, Quaternion rot, Vector3 scale)
    {
        var bindings = AnimationUtility.GetCurveBindings(clip);
        if (bindings.Length == 0)
        {
            EditorUtility.DisplayDialog("提示", "该 Clip 没有曲线数据", "OK");
            return;
        }


        Vector3 basePos = position;
        Quaternion baseRot = rot;
        Vector3 baseScale = scale;

        // // 检查参考物体是否有实际位移
        // bool hasMovement = Mathf.Abs(basePos.x) > 0.0001f

        //                 || Mathf.Abs(basePos.y) > 0.0001f
        //                 || Mathf.Abs(basePos.z) > 0.0001f;

        // if (!hasMovement)
        // {
        //     bool confirm = EditorUtility.DisplayDialog(
        //         "确认",
        //         "参考物体的 localPosition 为 (0, 0, 0)。\n\n" +
        //         "请确认它是否是动画中实际发生了位移的物体。\n" +
        //         "如果不是，请关闭工具重新选择。\n\n" +
        //         "是否继续生成？",
        //         "继续", "取消"
        //     );
        //     if (!confirm) return;
        // }

        var newClip = Object.Instantiate(clip);
        newClip.name = clip.name + "_Relative";
        newClip.legacy = clip.legacy;
        /*
        动画可以设置"相对"位置的原理（其实就是第 0 帧归零）：
        原来的动画 Animation 就是绝对位置（而不用管原来的物体的位置，因为动画强制位置 Position)
        所以，能用那个强制的绝对位置，减去自身的位置，就是相对位置
        （就是这么简单）
        */
        foreach (var binding in bindings)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.keys.Length == 0) continue;

           // float referenceValue = GetReferenceValue(binding, basePos, baseRot, baseScale);
            float referenceValue = curve.keys[0].value;   // ← 关键

            var newCurve = new AnimationCurve();
            foreach (var key in curve.keys)
            {
                newCurve.AddKey(new Keyframe(
                    key.time,
                    key.value - referenceValue,
                    key.inTangent,
                    key.outTangent
                ));
            }

            AnimationUtility.SetEditorCurve(newClip, binding, newCurve);
        }

        string path = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(path)) path = "Assets";

        string dir = System.IO.Path.GetDirectoryName(path);
        string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        string ext = System.IO.Path.GetExtension(path);
        string savePath = $"{dir}/{fileName}_Relative{ext}";

        AssetDatabase.CreateAsset(newClip, savePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"已生成相对偏移 Clip: {savePath}\n基准 localPosition={basePos}", newClip);
        EditorUtility.DisplayDialog("完成", $"已生成：{savePath}", "OK");
    }

    static float GetReferenceValue(EditorCurveBinding binding, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        string prop = binding.propertyName;
        if (prop == "m_LocalPosition.x") return pos.x;
        if (prop == "m_LocalPosition.y") return pos.y;
        if (prop == "m_LocalPosition.z") return pos.z;
        if (prop == "m_LocalRotation.x") return rot.x;
        if (prop == "m_LocalRotation.y") return rot.y;
        if (prop == "m_LocalRotation.z") return rot.z;
        if (prop == "m_LocalRotation.w") return rot.w;
        if (prop == "m_LocalScale.x") return scale.x;
        if (prop == "m_LocalScale.y") return scale.y;
        if (prop == "m_LocalScale.z") return scale.z;
        return 0f;
    }
}