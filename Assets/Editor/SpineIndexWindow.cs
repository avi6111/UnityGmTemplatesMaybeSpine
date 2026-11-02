using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spine;
using Spine.Unity;

[System.Serializable]
public class SpineCacheItem
{
    public string skeletonKey;
    public string[] assetPaths;
}

[System.Serializable]
public class SpineCacheTable
{
    public List<SpineCacheItem> items = new List<SpineCacheItem>();
}

public class SpineSkeletonIndexWindow : EditorWindow
{
    private const string CacheFileName = "Library/SpineIndexCache.json";
    private SpineCacheTable _cacheTable;

    private string _inputText = "rambo_0_old_SkeletonData";
    private List<string> _resultFiles = new List<string>();
    private string _baseName = "";
    private string _skeletonName = "";

    private SkeletonDataAsset _selectedSkeletonAsset;
    private SkeletonData _selectedSkeletonData;
    private Skeleton _skeleton;
    private Spine.AnimationStateData _animStateData;
    private Spine.AnimationState _animState;
    private SkeletonRenderer _skeletonRenderer;
    private GameObject _tempPreviewGO;
    private RenderTexture _rtPreview;
    private Camera _tempCam;

    private List<string> _skinList = new List<string>();
    private int _selectedSkinIndex;
    private List<string> _animList = new List<string>();
    private int _selectedAnimIndex;
    private bool _isPlaying;
    private float _previewTime;
    private Vector2 _skinScrollPos;

    // 偏移
    private Vector2 _camOffset = Vector2.zero;
    private const float OffsetRange = 3f;
    private bool _isDraggingCross;
    private bool _foldoutOffset = false; // Foldout 默认展开

    private const int PreviewTexW = 320;
    private const int PreviewTexH = 320;
    private float _padding = 0.2f;

    [MenuItem("Tools/Spine骨架索引查找器(实时RT预览)")]
    public static void OpenWindow()
    {
        var win = GetWindow<SpineSkeletonIndexWindow>("Spine索引查找-RT预览");
        win.minSize = new Vector2(960, 520);
        win.LoadCache();
    }

    private void OnEnable()
    {
        LoadCache();
        EditorApplication.update += EditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorUpdate;
        CleanPreviewResources();
    }

    private void OnDestroy()
    {
        CleanPreviewResources();
    }

    #region 缓存读写
    private void LoadCache()
    {
        _cacheTable = new SpineCacheTable();
        if (!File.Exists(CacheFileName))
        {
            Debug.LogWarning("[SpineIndex] 缓存文件不存在，请点【刷新全索引】");
            return;
        }
        try
        {
            string json = File.ReadAllText(CacheFileName);
            _cacheTable = JsonUtility.FromJson<SpineCacheTable>(json);
            Debug.Log($"[SpineIndex] 读取缓存成功，共 {_cacheTable.items.Count} 条骨架记录");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SpineIndex] 读取缓存失败：{e.Message}");
            _cacheTable = new SpineCacheTable();
        }
    }

    private void SaveCache()
    {
        string json = JsonUtility.ToJson(_cacheTable, prettyPrint: true);
        if (!Directory.Exists("Library"))
        {
            Directory.CreateDirectory("Library");
        }
        File.WriteAllText(CacheFileName, json);
        Debug.Log($"[SpineIndex] 缓存写入完成，路径：{CacheFileName}");
    }

    private void RebuildAllIndex()
    {
        _cacheTable.items.Clear();
        string suffix = "_SkeletonData";
        string[] allGuids = AssetDatabase.FindAssets($"*{suffix}");
        Dictionary<string, List<string>> dict = new Dictionary<string, List<string>>();

        foreach (var guid in allGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileName(path);
            string key = Path.GetFileNameWithoutExtension(fileName);
            if (!key.EndsWith(suffix)) continue;
            if (!dict.ContainsKey(key)) dict[key] = new List<string>();
            dict[key].Add(path);
        }

        foreach (var kv in dict)
        {
            var item = new SpineCacheItem { skeletonKey = kv.Key, assetPaths = kv.Value.ToArray() };
            _cacheTable.items.Add(item);
        }
        SaveCache();
        EditorUtility.DisplayDialog("索引重建完成", $"一共找到 {_cacheTable.items.Count} 个 _SkeletonData 骨架", "OK");
    }
    #endregion

    #region 预览生命周期
    private void CleanPreviewResources()
    {
        if (_tempPreviewGO != null)
        {
            Debug.Log($"[SpineIndex] 销毁临时GO：{_tempPreviewGO.name}");
            DestroyImmediate(_tempPreviewGO);
        }
        if (_rtPreview != null)
        {
            _rtPreview.Release();
            DestroyImmediate(_rtPreview);
        }
        _tempCam = null;
        _skeletonRenderer = null;
        _skeleton = null;
        _animState = null;
        _animStateData = null;
        _selectedSkeletonData = null;
        _selectedSkeletonAsset = null;
        _skinList.Clear();
        _animList.Clear();
        _isPlaying = false;
        _previewTime = 0;
        _camOffset = Vector2.zero;
    }

    private void SetupPreviewRenderer(SkeletonDataAsset dataAsset)
    {
        CleanPreviewResources();

        _selectedSkeletonAsset = dataAsset;
        _selectedSkeletonData = _selectedSkeletonAsset.GetSkeletonData(false);
        if (_selectedSkeletonData == null)
        {
            Debug.LogError("[SpineIndex] GetSkeletonData 返回null");
            return;
        }

        _skinList = _selectedSkeletonData.Skins.Select(s => s.Name).ToList();
        _animList = _selectedSkeletonData.Animations.Select(a => a.Name).ToList();
        _selectedSkinIndex = 0;
        _selectedAnimIndex = 0;

        _skeleton = new Skeleton(_selectedSkeletonData);
        _animStateData = new Spine.AnimationStateData(_selectedSkeletonData);
        _animState = new Spine.AnimationState(_animStateData);

        _skeleton.SetSkin(_skinList[0]);
        _skeleton.SetSlotsToSetupPose();
        _skeleton.UpdateWorldTransform();

        _tempPreviewGO = new GameObject("SpinePreviewTemp");
        Debug.Log($"[SpineIndex] 创建临时GO：{_tempPreviewGO.name}，实例ID:{_tempPreviewGO.GetInstanceID()}");
        _tempPreviewGO.hideFlags = HideFlags.HideAndDontSave;
        _skeletonRenderer = _tempPreviewGO.AddComponent<SkeletonRenderer>();

        _skeletonRenderer.skeletonDataAsset = _selectedSkeletonAsset;
        _skeletonRenderer.Initialize(false);
        _skeletonRenderer.skeleton = _skeleton;
        _skeletonRenderer.initialSkinName = _skinList[0];

        _rtPreview = new RenderTexture(PreviewTexW, PreviewTexH, 24, RenderTextureFormat.Default);
        _rtPreview.hideFlags = HideFlags.HideAndDontSave;

        GameObject camGo = new GameObject("PreviewCam");
        camGo.hideFlags = HideFlags.HideAndDontSave;
        _tempCam = camGo.AddComponent<Camera>();
        _tempCam.clearFlags = CameraClearFlags.SolidColor;
        _tempCam.backgroundColor = Color.black;
        _tempCam.orthographic = true;
        _tempCam.cullingMask = -1;
        _tempCam.targetTexture = _rtPreview;

        UpdateCameraFromBounds();
        _isPlaying = false;
        _previewTime = 0;

        RenderPreviewOnce();
    }

    private void UpdateCameraFromBounds()
    {
        if (_skeleton == null || _tempCam == null) return;

        float[] vertexBuffer = null;
        _skeleton.GetBounds(out float x, out float y, out float w, out float h, ref vertexBuffer);
        float centerX = x + w / 2f;
        float centerY = y + h / 2f;

        float aspect = (float)PreviewTexW / PreviewTexH;
        float orthoSize;
        if (w / h > aspect)
            orthoSize = (w * (1 + _padding)) / (2 * aspect);
        else
            orthoSize = (h * (1 + _padding)) / 2;

        _tempCam.orthographicSize = orthoSize;
        _tempCam.transform.position = new Vector3(centerX + _camOffset.x, centerY + _camOffset.y, -10);
        _tempPreviewGO.layer = 10;
    }

    private void EditorUpdate()
    {
        if (_skeleton == null || _animState == null || _tempCam == null || _skeletonRenderer == null) return;
        if (!_isPlaying) return;

        float delta = Time.deltaTime;
        _previewTime += delta;
        _animState.Update(delta);
        _animState.Apply(_skeleton);
        _skeleton.UpdateWorldTransform();
        _skeletonRenderer.LateUpdate();
        _tempCam.Render();
        Repaint();
    }

    private void RenderPreviewOnce()
    {
        if (_skeleton == null || _tempCam == null || _skeletonRenderer == null) return;
        _animState.Apply(_skeleton);
        _skeleton.UpdateWorldTransform();
        _skeletonRenderer.LateUpdate();
        _tempCam.Render();
        Repaint();
    }
    #endregion

    #region GUI
    private void OnGUI()
    {
        EditorGUILayout.LabelField("输入 SkeletonData 全名（带_SkeletonData后缀）", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        _inputText = EditorGUILayout.TextField(_inputText);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("🔍 从缓存查找", GUILayout.Height(30)))
            {
                SearchFromCache();
                CleanPreviewResources();
            }
            if (GUILayout.Button("🔄 刷新全索引（扫描项目）", GUILayout.Height(30)))
            {
                RebuildAllIndex();
            }
        }
        EditorGUILayout.Space(8);

        using (new EditorGUILayout.HorizontalScope())
        {
            // 左侧文件列表
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(340)))
            {
                if (!string.IsNullOrEmpty(_baseName))
                {
                    EditorGUILayout.LabelField($"✅ 骨架Key：{_skeletonName}");
                    EditorGUILayout.LabelField($"✅ 基础前缀：{_baseName}");
                    EditorGUILayout.Space(4);
                }
                EditorGUILayout.LabelField("匹配到的文件列表：", EditorStyles.miniBoldLabel);
                foreach (var file in _resultFiles)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(file, EditorStyles.miniLabel);
                        if (GUILayout.Button("选中", GUILayout.Width(50), GUILayout.Height(20)))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<Object>(file);
                            if (asset != null) { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
                        }
                        if (GUILayout.Button("显示", GUILayout.Width(50), GUILayout.Height(20)))
                        {
                            var skAsset = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(file);
                            if (skAsset != null) SetupPreviewRenderer(skAsset);
                        }
                    }
                }
                if (_resultFiles.Count == 0)
                    EditorGUILayout.LabelField("（无匹配结果，检查缓存或刷新索引）", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.Space(10);
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("Spine RT实时预览面板", EditorStyles.boldLabel);
                EditorGUILayout.Space(4);
                if (_selectedSkeletonData == null)
                {
                    EditorGUILayout.LabelField("点击【显示】加载骨架", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(340));
                }
                else
                {
                    EditorGUILayout.LabelField($"骨架名称：{_selectedSkeletonData.Name}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Skin总数：{_skinList.Count} | 动画总数：{_animList.Count}", EditorStyles.miniLabel);
                    EditorGUILayout.Space(4);

                    // ===== Foldout 折叠偏移面板 =====
                    _foldoutOffset = EditorGUILayout.Foldout(_foldoutOffset, "🎯 镜头锚点偏移", true);
                    if (_foldoutOffset)
                    {
                        EditorGUILayout.BeginVertical("Box");
                        Rect crossRect = EditorGUILayout.GetControlRect(false, 120);
                        DrawCrossPad(crossRect);

                        // 数值输入
                        EditorGUI.BeginChangeCheck();
                        _camOffset.x = EditorGUILayout.FloatField("Offset X", _camOffset.x);
                        _camOffset.y = EditorGUILayout.FloatField("Offset Y", _camOffset.y);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _camOffset.x = Mathf.Clamp(_camOffset.x, -OffsetRange, OffsetRange);
                            _camOffset.y = Mathf.Clamp(_camOffset.y, -OffsetRange, OffsetRange);
                            UpdateCameraFromBounds();
                            RenderPreviewOnce();
                        }

                        if (GUILayout.Button("重置偏移", GUILayout.Height(22)))
                        {
                            _camOffset = Vector2.zero;
                            UpdateCameraFromBounds();
                            RenderPreviewOnce();
                        }
                        EditorGUILayout.EndVertical();
                    }
                    EditorGUILayout.Space(4);

                    int newSkinIdx = EditorGUILayout.Popup("切换Skin", _selectedSkinIndex, _skinList.ToArray());
                    if (newSkinIdx != _selectedSkinIndex)
                    {
                        _selectedSkinIndex = newSkinIdx;
                        _skeleton.SetSkin(_skinList[_selectedSkinIndex]);
                        _skeleton.SetSlotsToSetupPose();
                        _skeleton.UpdateWorldTransform();
                        UpdateCameraFromBounds();
                        RenderPreviewOnce();
                    }
                    EditorGUILayout.Space(2);

                    if (_animList.Count > 0)
                    {
                        _selectedAnimIndex = EditorGUILayout.Popup("动画", _selectedAnimIndex, _animList.ToArray());
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(_isPlaying ? "暂停" : "播放", GUILayout.Width(70)))
                            {
                                _isPlaying = !_isPlaying;
                                if (_isPlaying)
                                {
                                    _animState.SetAnimation(0, _animList[_selectedAnimIndex], true);
                                }
                            }
                            if (GUILayout.Button("重置姿态", GUILayout.Width(70)))
                            {
                                _isPlaying = false;
                                _animState.ClearTracks();
                                _skeleton.SetSlotsToSetupPose();
                                _skeleton.UpdateWorldTransform();
                                RenderPreviewOnce();
                            }
                        }
                    }
                    EditorGUILayout.Space(4);

                    Rect rtRect = EditorGUILayout.GetControlRect(GUILayout.Width(PreviewTexW), GUILayout.Height(PreviewTexH));
                    EditorGUI.DrawPreviewTexture(rtRect, _rtPreview, null, ScaleMode.ScaleToFit);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Skin列表：", EditorStyles.miniBoldLabel);
                    _skinScrollPos = EditorGUILayout.BeginScrollView(_skinScrollPos, GUILayout.Height(100));
                    foreach (var skinName in _skinList)
                    {
                        EditorGUILayout.LabelField($"- {skinName}", EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
        }
    }

    private void DrawCrossPad(Rect r)
    {
        // 绘制背景+十字基准线
        EditorGUI.DrawRect(r, new Color(0.15f,0.15f,0.15f));
        Handles.color = new Color(0.6f,0.6f,0.6f);
        // 中线
        Handles.DrawLine(new Vector2(r.xMin, r.center.y), new Vector2(r.xMax, r.center.y));
        Handles.DrawLine(new Vector2(r.center.x, r.yMin), new Vector2(r.center.x, r.yMax));

        // 把 offset(-range~range) 映射到面板rect坐标
        float halfW = r.width / 2f;
        float halfH = r.height / 2f;
        Vector2 dotPos = new Vector2(
            r.center.x + (_camOffset.x / OffsetRange) * halfW,
            r.center.y - (_camOffset.y / OffsetRange) * halfH
        );
        dotPos.x = Mathf.Clamp(dotPos.x, r.xMin, r.xMax);
        dotPos.y = Mathf.Clamp(dotPos.y, r.yMin, r.yMax);

        // 绘制圆点
        Handles.color = Color.yellow;
        Handles.DrawSolidDisc(dotPos, Vector3.forward, 5f);

        // 鼠标事件
        UnityEngine.Event evt = UnityEngine.Event.current;
        if (evt.type == EventType.MouseDown && r.Contains(evt.mousePosition))
        {
            _isDraggingCross = true;
            evt.Use();
        }
        if (evt.type == EventType.MouseUp)
        {
            _isDraggingCross = false;
        }
        if (_isDraggingCross && evt.type == EventType.MouseDrag)
        {
            Vector2 local = evt.mousePosition - r.center;
            // 映射回 offset
            float ox = (local.x / halfW) * OffsetRange;
            float oy = -(local.y / halfH) * OffsetRange;
            _camOffset.x = Mathf.Clamp(ox, -OffsetRange, OffsetRange);
            _camOffset.y = Mathf.Clamp(oy, -OffsetRange, OffsetRange);

            UpdateCameraFromBounds();
            RenderPreviewOnce();
            evt.Use();
        }
    }
    #endregion

    private void SearchFromCache()
    {
        _resultFiles.Clear();
        _baseName = "";
        _skeletonName = _inputText.Trim();
        string suffix = "_SkeletonData";
        if (!_skeletonName.EndsWith(suffix))
        {
            EditorUtility.DisplayDialog("格式提示", "名字必须以 _SkeletonData 结尾\n示例：rambo_0_old_SkeletonData", "OK");
            return;
        }
        _baseName = _skeletonName.Substring(0, _skeletonName.Length - suffix.Length);
        var findItem = _cacheTable.items.FirstOrDefault(x => x.skeletonKey == _skeletonName);
        if (findItem != null) _resultFiles.AddRange(findItem.assetPaths);
    }
}
