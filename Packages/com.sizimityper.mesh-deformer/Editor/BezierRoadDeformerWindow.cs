using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SizimityperMeshDeformer;

public class BezierRoadDeformerWindow : EditorWindow
{
    // ============================================================
    // Open / Init
    // ============================================================

    [MenuItem("Tools/sizimityper/mesh_deformer")]
    public static void Open()
    {
        var win = GetWindow<BezierRoadDeformerWindow>();
        win.titleContent = new GUIContent("Bezier Road Deformer");
        win.minSize = new Vector2(300, 400);
        win.Show();
    }

    // ============================================================
    // State
    // ============================================================

    private BezierRoadDeformer _target;
    private Vector2 _scroll;
    private bool _previewEnabled = false;

    // Change detection cache
    private readonly List<Vector3>    _prevPositions = new List<Vector3>();
    private readonly List<Quaternion> _prevRotations = new List<Quaternion>();
    private int   _prevPivotCount   = -1;
    private float _prevHandleLength = -1f;
    private float _prevParamR, _prevParamAngle, _prevParamCant, _prevParamGrade, _prevParamEaseLen;
    private bool  _prevParamEase;
    private bool  _prevParamTurnRight;

    // Deform mode cache
    private DeformMode _prevDeformMode      = (DeformMode)(-1);
    private int        _prevSubdivisions    = -1;
    private float      _prevTileAxisPadding = -1f;

    // ============================================================

    void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
        EditorApplication.update   += OnEditorUpdate;
        OnSelectionChanged();
    }

    void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        EditorApplication.update   -= OnEditorUpdate;
    }

    private void OnSelectionChanged()
    {
        var go = Selection.activeGameObject;
        _target = go != null ? go.GetComponent<BezierRoadDeformer>() : null;
        CachePivotTransforms();
        CacheParamValues();
        Repaint();
    }

    // ============================================================
    // Change Detection → auto-preview
    // ============================================================

    private void OnEditorUpdate()
    {
        if (_target == null || !_previewEnabled) return;

        bool changed = (_target.curveMode == CurveMode.Pivot
            ? DetectPivotChanges()
            : DetectParamChanges())
            || DetectDeformChanges();

        if (changed && _target.sourceMeshEntries != null && _target.sourceMeshEntries.Count > 0)
        {
            if (_target.curveMode == CurveMode.Parameter) CacheParamValues();
            CachePivotTransforms();
            CacheDeformValues();
            _target.UpdatePreview();
            EditorUtility.SetDirty(_target);
            Repaint();
        }
    }

    private bool DetectPivotChanges()
    {
        if (_target.pivots == null) return false;
        if (_target.pivots.Count != _prevPivotCount) return true;
        if (_target.handleLength != _prevHandleLength) return true;
        for (int i = 0; i < _target.pivots.Count; i++)
        {
            if (_target.pivots[i] == null) continue;
            if (i >= _prevPositions.Count) return true;
            if (_target.pivots[i].position != _prevPositions[i]) return true;
            if (_target.pivots[i].rotation != _prevRotations[i]) return true;
        }
        return false;
    }

    private bool DetectParamChanges()
    {
        return _target.paramR              != _prevParamR
            || _target.paramAngle          != _prevParamAngle
            || _target.paramCantAngle      != _prevParamCant
            || _target.paramGrade          != _prevParamGrade
            || _target.paramUseEasement    != _prevParamEase
            || _target.paramEasementLength != _prevParamEaseLen
            || _target.paramTurnRight      != _prevParamTurnRight;
    }

    private bool DetectDeformChanges()
    {
        return _target.deformMode      != _prevDeformMode
            || _target.subdivisions    != _prevSubdivisions
            || _target.tileAxisPadding != _prevTileAxisPadding;
    }

    private void CacheDeformValues()
    {
        if (_target == null) return;
        _prevDeformMode      = _target.deformMode;
        _prevSubdivisions    = _target.subdivisions;
        _prevTileAxisPadding = _target.tileAxisPadding;
    }

    private void CachePivotTransforms()
    {
        _prevPositions.Clear();
        _prevRotations.Clear();
        if (_target?.pivots == null) return;
        foreach (var p in _target.pivots)
        {
            if (p == null) continue;
            _prevPositions.Add(p.position);
            _prevRotations.Add(p.rotation);
        }
        _prevPivotCount   = _target.pivots.Count;
        _prevHandleLength = _target.handleLength;
    }

    private void CacheParamValues()
    {
        if (_target == null) return;
        _prevParamR          = _target.paramR;
        _prevParamAngle      = _target.paramAngle;
        _prevParamCant       = _target.paramCantAngle;
        _prevParamGrade      = _target.paramGrade;
        _prevParamEase       = _target.paramUseEasement;
        _prevParamEaseLen    = _target.paramEasementLength;
        _prevParamTurnRight  = _target.paramTurnRight;
    }

    // ============================================================
    // GUI
    // ============================================================

    void OnGUI()
    {
        DrawHeader();

        if (_target == null)
        {
            DrawNoTarget();
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawBaseObjectSection();
        DrawSeparator();
        DrawCurveModeSection();
        DrawSeparator();
        DrawDeformModeSection();
        DrawSeparator();
        DrawPrefabPlacementSection();
        DrawSeparator();
        DrawActionButtons();

        EditorGUILayout.EndScrollView();
    }

    // ---- Header ------------------------------------------------

    private void DrawHeader()
    {
        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Sizimityper's Mesh Deformer", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (_target != null && GUILayout.Button("Ping", EditorStyles.toolbarButton, GUILayout.Width(40)))
                EditorGUIUtility.PingObject(_target.gameObject);
        }

        // Preview status bar
        if (_target != null)
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = _previewEnabled ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.7f, 0.7f, 0.7f);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUI.backgroundColor = prevBg;
                string icon  = _previewEnabled ? "● " : "○ ";
                string label = _previewEnabled ? "プレビュー ON — 変形をリアルタイム更新中" : "プレビュー OFF";
                GUILayout.Label(icon + label, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(_previewEnabled ? "停止" : "開始", EditorStyles.miniButton, GUILayout.Width(42)))
                    SetPreviewEnabled(!_previewEnabled);
            }
        }

        EditorGUILayout.Space(2);
    }

    // ---- No target ---------------------------------------------

    private void DrawNoTarget()
    {
        EditorGUILayout.Space(20);
        EditorGUILayout.HelpBox(
            "シーン上の BezierRoadDeformer コンポーネントを持つ\nGameObject を選択してください。",
            MessageType.Info);

        EditorGUILayout.Space(8);

        if (GUILayout.Button("新規 GameObject を作成して追加"))
        {
            var go = new GameObject("BezierRoad");
            Undo.RegisterCreatedObjectUndo(go, "Create BezierRoad");
            Undo.AddComponent<BezierRoadDeformer>(go);
            Selection.activeGameObject = go;
            OnSelectionChanged();
        }
    }

    // ---- Sections (same logic as CustomEditor) -----------------

    private void DrawBaseObjectSection()
    {
        EditorGUILayout.LabelField("■ ベースオブジェクト", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        EditorGUI.BeginChangeCheck();
        var newParent = (GameObject)EditorGUILayout.ObjectField(
            "ソース親オブジェクト", _target.sourceParentObject, typeof(GameObject), true);
        var newAxis = (AxisDirection)EditorGUILayout.EnumPopup("軸線方向", _target.axisDirection);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Source Parent");
            _target.sourceParentObject = newParent;
            _target.axisDirection      = newAxis;
            if (_target.sourceParentObject != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Initialize Road Deformer");
                _target.Initialize();
                CachePivotTransforms();
            }
            EditorUtility.SetDirty(_target);
        }

        // 収集済みメッシュ一覧
        if (_target.sourceMeshEntries != null && _target.sourceMeshEntries.Count > 0)
        {
            EditorGUILayout.LabelField($"検出メッシュ ({_target.sourceMeshEntries.Count}):", EditorStyles.miniLabel);
            EditorGUI.indentLevel++;
            foreach (var entry in _target.sourceMeshEntries)
                EditorGUILayout.LabelField(entry.mesh != null ? entry.mesh.name : "(null)", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }

        using (new EditorGUI.DisabledGroupScope(true))
            EditorGUILayout.FloatField("メッシュ長 (m)", _target.meshLength);

        EditorGUI.indentLevel--;
    }

    private void DrawCurveModeSection()
    {
        EditorGUILayout.LabelField("■ カーブ定義モード", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        EditorGUI.BeginChangeCheck();
        var newMode = (CurveMode)EditorGUILayout.EnumPopup("モード", _target.curveMode);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Curve Mode");
            _target.curveMode        = newMode;
            _target.arcLengthLUT     = null;
            _target.paramPointsBuilt = false;
            EditorUtility.SetDirty(_target);
        }

        if (_target.curveMode == CurveMode.Pivot)
            DrawPivotModeUI();
        else
            DrawParameterModeUI();

        EditorGUI.indentLevel--;
    }

    private void DrawPivotModeUI()
    {
        EditorGUI.BeginChangeCheck();
        float newHL = EditorGUILayout.FloatField("ハンドル長さ", _target.handleLength);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Handle Length");
            _target.handleLength = newHL;
            _target.arcLengthLUT = null;
            EditorUtility.SetDirty(_target);
        }

        EditorGUILayout.LabelField("ピボット一覧:");
        if (_target.pivots != null)
        {
            for (int i = 0; i < _target.pivots.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string label = _target.pivots[i] != null ? _target.pivots[i].name : "(null)";
                    EditorGUILayout.LabelField($"  {i}: {label}");

                    if (i > 0 && i < _target.pivots.Count - 1)
                    {
                        if (GUILayout.Button("−", GUILayout.Width(24)))
                        {
                            Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Remove Pivot");
                            _target.RemovePivot(i);
                            EditorUtility.SetDirty(_target);
                            GUIUtility.ExitGUI();
                            return;
                        }
                    }
                }
            }
        }

        if (GUILayout.Button("+ ピボット追加"))
        {
            Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Add Pivot");
            _target.AddPivot();
            EditorUtility.SetDirty(_target);
        }
    }

    private void DrawParameterModeUI()
    {
        EditorGUI.BeginChangeCheck();
        bool  newTurnRight = EditorGUILayout.Toggle("右カーブ",          _target.paramTurnRight);
        float newR         = EditorGUILayout.FloatField("R (曲率半径) m",  _target.paramR);
        float newAngle     = EditorGUILayout.FloatField("角度 (展開角) °", _target.paramAngle);
        float newCant      = EditorGUILayout.FloatField("カント角 °",      _target.paramCantAngle);
        float newGrade     = EditorGUILayout.FloatField("縦断勾配 %",      _target.paramGrade);
        bool  newEase      = EditorGUILayout.Toggle("緩和曲線",            _target.paramUseEasement);
        float newEaseLen   = _target.paramEasementLength;
        if (newEase)
            newEaseLen = EditorGUILayout.FloatField("緩和区間長 m", _target.paramEasementLength);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Parameter Values");
            _target.paramTurnRight       = newTurnRight;
            _target.paramR               = Mathf.Max(0.1f, newR);
            _target.paramAngle           = Mathf.Clamp(newAngle, 0f, 360f);
            _target.paramCantAngle       = newCant;
            _target.paramGrade           = newGrade;
            _target.paramUseEasement     = newEase;
            _target.paramEasementLength  = Mathf.Max(0f, newEaseLen);
            _target.arcLengthLUT         = null;
            _target.paramPointsBuilt     = false;
            EditorUtility.SetDirty(_target);
        }

        EditorGUILayout.Space(4);
        DrawCantCalcUI();

        EditorGUILayout.Space(4);
        if (GUILayout.Button("ピボットに変換"))
        {
            if (EditorUtility.DisplayDialog("確認",
                "パラメータモードの内容をピボットに変換します。\nこの操作は元に戻せません。よろしいですか？",
                "変換する", "キャンセル"))
            {
                Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Convert to Pivot");
                _target.ConvertParameterToPivot();
                EditorUtility.SetDirty(_target);
            }
        }
    }

    private void DrawCantCalcUI()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("カント自動計算 (道路構造令)", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        EditorGUI.BeginChangeCheck();
        bool  newAutoSpeed    = EditorGUILayout.Toggle("設計速度を R から自動推定",        _target.paramAutoCalcSpeed);
        float newV = _target.paramDesignSpeed;
        if (!newAutoSpeed)
            newV = EditorGUILayout.FloatField("設計速度 V (km/h)", _target.paramDesignSpeed);
        bool  newAutoFriction = EditorGUILayout.Toggle("横すべり摩擦係数を速度から自動算出", _target.paramAutoCalcFriction);
        float newF = _target.paramFrictionCoeff;
        if (!newAutoFriction)
            newF = EditorGUILayout.FloatField("横すべり摩擦係数 f", _target.paramFrictionCoeff);
        float newMaxI          = EditorGUILayout.FloatField("最大片勾配 (例: 0.08)",    _target.paramMaxSuperelevation);
        bool  newAutoApply     = EditorGUILayout.Toggle("カント角を自動適用",           _target.paramAutoApplyCant);
        bool  newAutoEasement  = EditorGUILayout.Toggle("緩和区間長を自動算出",         _target.paramAutoCalcEasement);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Cant Calc Params");
            _target.paramAutoCalcSpeed     = newAutoSpeed;
            _target.paramDesignSpeed       = Mathf.Max(0f, newV);
            _target.paramAutoCalcFriction  = newAutoFriction;
            _target.paramFrictionCoeff     = Mathf.Clamp(newF, 0f, 1f);
            _target.paramMaxSuperelevation = Mathf.Clamp(newMaxI, 0f, 0.2f);
            _target.paramAutoApplyCant     = newAutoApply;
            _target.paramAutoCalcEasement  = newAutoEasement;
            EditorUtility.SetDirty(_target);
        }

        // プレビュー表示
        float estSpeed    = _target.paramAutoCalcSpeed    ? _target.CalcDesignSpeedFromR()               : _target.paramDesignSpeed;
        float estFriction = _target.paramAutoCalcFriction ? _target.CalcFrictionFromSpeed(estSpeed)      : _target.paramFrictionCoeff;
        float estEasement = _target.CalcEasementLengthFromSpeed(estSpeed);
        float previewCant = _target.CalcCantAngle();
        float previewI    = Mathf.Tan(previewCant * Mathf.Deg2Rad);
        if (_target.paramAutoCalcSpeed)
            EditorGUILayout.LabelField($"推定速度: {estSpeed:F0} km/h", EditorStyles.miniLabel);
        if (_target.paramAutoCalcFriction)
            EditorGUILayout.LabelField($"算出摩擦係数: f = {estFriction:F3}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"→ 片勾配 i = {previewI * 100f:F2} %  /  カント角 = {previewCant:F3} °", EditorStyles.miniLabel);
        if (_target.paramAutoCalcEasement)
            EditorGUILayout.LabelField($"算出緩和区間長: {estEasement:F1} m", EditorStyles.miniLabel);

        // 自動適用
        bool needsRebuild = false;
        if (_target.paramAutoApplyCant && !Mathf.Approximately(_target.paramCantAngle, previewCant))
        {
            Undo.RecordObject(_target, "Auto Apply Cant");
            _target.paramCantAngle = previewCant;
            needsRebuild = true;
        }
        if (_target.paramAutoCalcEasement && _target.paramUseEasement
            && !Mathf.Approximately(_target.paramEasementLength, estEasement))
        {
            Undo.RecordObject(_target, "Auto Apply Easement");
            _target.paramEasementLength = estEasement;
            needsRebuild = true;
        }
        if (needsRebuild)
        {
            _target.arcLengthLUT     = null;
            _target.paramPointsBuilt = false;
            EditorUtility.SetDirty(_target);
        }

        if (!_target.paramAutoApplyCant && GUILayout.Button("カント角に適用"))
        {
            Undo.RecordObject(_target, "Apply Calc Cant");
            _target.paramCantAngle   = previewCant;
            _target.arcLengthLUT     = null;
            _target.paramPointsBuilt = false;
            EditorUtility.SetDirty(_target);
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.EndVertical();
    }

    private void DrawDeformModeSection()
    {
        EditorGUILayout.LabelField("■ 変形モード", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        EditorGUI.BeginChangeCheck();
        var   newDeform = (DeformMode)EditorGUILayout.EnumPopup("モード", _target.deformMode);
        int   newSubdiv  = EditorGUILayout.IntSlider("分割数", _target.subdivisions, 1, 100);
        float newPadding = EditorGUILayout.Slider(
            new GUIContent("境界パディング (m)", "タイル境界より外側にはみ出した突起ジオメトリを境界に吸収する距離。デフォルト 0.001m。"),
            _target.tileAxisPadding, 0f, 0.5f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Deform Mode");
            _target.deformMode      = newDeform;
            _target.subdivisions    = newSubdiv;
            _target.tileAxisPadding = newPadding;
            EditorUtility.SetDirty(_target);
        }

        EditorGUI.indentLevel--;
    }

    private void DrawPrefabPlacementSection()
    {
        EditorGUILayout.LabelField("■ プレハブ配置", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        if (_target.placementRules == null)
            _target.placementRules = new List<PrefabPlacementRule>();

        for (int i = 0; i < _target.placementRules.Count; i++)
        {
            var rule = _target.placementRules[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Rule {i}", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            rule.prefab      = (GameObject)EditorGUILayout.ObjectField("プレハブ",       rule.prefab,      typeof(GameObject), false);
            rule.interval    = EditorGUILayout.FloatField("間隔 m",         rule.interval);
            rule.offsetRight = EditorGUILayout.FloatField("右オフセット m", rule.offsetRight);
            rule.offsetUp    = EditorGUILayout.FloatField("上オフセット m", rule.offsetUp);
            rule.followCant  = EditorGUILayout.Toggle("カント追従",         rule.followCant);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_target);

            EditorGUI.indentLevel--;

            if (GUILayout.Button("ルール削除"))
            {
                Undo.RecordObject(_target, "Remove Placement Rule");
                _target.placementRules.RemoveAt(i);
                EditorUtility.SetDirty(_target);
                EditorGUILayout.EndVertical();
                GUIUtility.ExitGUI();
                return;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (GUILayout.Button("+ ルール追加"))
        {
            Undo.RecordObject(_target, "Add Placement Rule");
            _target.placementRules.Add(new PrefabPlacementRule());
            EditorUtility.SetDirty(_target);
        }

        EditorGUI.indentLevel--;
    }

    private void DrawActionButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = _previewEnabled ? new Color(0.3f, 0.9f, 0.4f) : Color.white;
            if (GUILayout.Button(_previewEnabled ? "Preview ●" : "Preview ○"))
                SetPreviewEnabled(!_previewEnabled);
            GUI.backgroundColor = prevBg;

            if (GUILayout.Button("Bake"))
                DoBake();
        }
    }

    // ============================================================
    // Bake
    // ============================================================

    private void DoBake()
    {
        if (_target.sourceMeshEntries == null || _target.sourceMeshEntries.Count == 0)
        {
            EditorUtility.DisplayDialog("Bake Error", "ソースメッシュが設定されていません。", "OK");
            return;
        }

        var deformedMeshes = _target.DeformAllMeshes();
        if (deformedMeshes == null || deformedMeshes.Count == 0)
        {
            EditorUtility.DisplayDialog("Bake Error", "変形メッシュの生成に失敗しました。", "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Bake Road Mesh");

        for (int i = 0; i < _target.sourceMeshEntries.Count; i++)
        {
            var entry = _target.sourceMeshEntries[i];
            var mesh  = deformedMeshes[i];
            if (mesh == null) continue;

            if (entry.outputObject == null)
            {
                entry.outputObject = new GameObject();
                entry.outputObject.transform.SetParent(_target.transform, false);
                entry.outputObject.AddComponent<MeshFilter>();
                entry.outputObject.AddComponent<MeshRenderer>();
            }

            entry.outputObject.name      = entry.mesh != null ? entry.mesh.name : _target.gameObject.name;
            entry.outputObject.hideFlags = HideFlags.None;

            var mf = entry.outputObject.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = mesh;

            var mr = entry.outputObject.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterials = entry.materials ?? new Material[0];

            entry.outputObject = null;
        }

        // プレハブを永続化
        _target.UpdatePrefabPlacements();
        foreach (var go in _target.spawnedPrefabs)
            if (go != null) go.hideFlags = HideFlags.None;
        _target.spawnedPrefabs.Clear();

        // ピボットを削除
        foreach (var p in _target.pivots)
            if (p != null) DestroyImmediate(p.gameObject);
        _target.pivots.Clear();

        // コンポーネントだけ Remove
        DestroyImmediate(_target);
        _target = null;

        Repaint();
    }

    // ============================================================
    // Preview Toggle
    // ============================================================

    private void SetPreviewEnabled(bool enabled)
    {
        _previewEnabled = enabled;
        if (_target == null) return;

        if (_previewEnabled)
        {
            // まだ初期化されていなければ自動初期化
            if (_target.sourceMeshEntries == null || _target.sourceMeshEntries.Count == 0)
            {
                if (_target.sourceParentObject != null)
                {
                    Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Initialize Road Deformer");
                    _target.Initialize();
                    CachePivotTransforms();
                }
            }
            if (_target.sourceMeshEntries != null && _target.sourceMeshEntries.Count > 0)
            {
                _target.UpdatePreview();
                EditorUtility.SetDirty(_target);
            }
        }
        else
        {
            // プレビューOFF → プレビュー用メッシュ・プレハブをすべて削除
            _target.ClearPreviewObjects();
            _target.ClearSpawnedPrefabs();
            EditorUtility.SetDirty(_target);
        }
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static void DrawSeparator()
    {
        EditorGUILayout.Space(4);
        var rect = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        EditorGUILayout.Space(4);
    }
}
