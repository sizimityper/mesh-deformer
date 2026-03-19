using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SizimityperMeshDeformer;

[CustomEditor(typeof(BezierRoadDeformer))]
public class BezierRoadDeformerEditor : Editor
{
    private BezierRoadDeformer _target;

    // Change detection cache
    private readonly List<Vector3>    _prevPositions = new List<Vector3>();
    private readonly List<Quaternion> _prevRotations = new List<Quaternion>();
    private int   _prevPivotCount   = -1;
    private float _prevHandleLength = -1f;

    // Parameter mode cache
    private float _prevParamR;
    private float _prevParamAngle;
    private float _prevParamCant;
    private float _prevParamGrade;
    private bool  _prevParamEasement;
    private float _prevParamEasementLen;
    private bool  _prevParamTurnRight;

    // ============================================================

    void OnEnable()
    {
        _target = (BezierRoadDeformer)target;
        CachePivotTransforms();
        CacheParamValues();
        EditorApplication.update += OnEditorUpdate;
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    // ============================================================
    // Change Detection
    // ============================================================

    private void OnEditorUpdate()
    {
        if (_target == null) return;

        bool changed = _target.curveMode == CurveMode.Pivot
            ? DetectPivotChanges()
            : DetectParamChanges();

        if (changed && _target.sourceMeshEntries != null && _target.sourceMeshEntries.Count > 0)
        {
            if (_target.curveMode == CurveMode.Parameter) CacheParamValues();
            CachePivotTransforms();
            _target.UpdatePreview();
            EditorUtility.SetDirty(_target);
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
            || _target.paramUseEasement    != _prevParamEasement
            || _target.paramEasementLength != _prevParamEasementLen
            || _target.paramTurnRight      != _prevParamTurnRight;
    }

    private void CachePivotTransforms()
    {
        _prevPositions.Clear();
        _prevRotations.Clear();
        if (_target.pivots == null) return;
        foreach (var p in _target.pivots)
        {
            if (p == null) continue;
            _prevPositions.Add(p.position);
            _prevRotations.Add(p.rotation);
        }
        _prevPivotCount   = _target.pivots?.Count ?? 0;
        _prevHandleLength = _target.handleLength;
    }

    private void CacheParamValues()
    {
        _prevParamR           = _target.paramR;
        _prevParamAngle       = _target.paramAngle;
        _prevParamCant        = _target.paramCantAngle;
        _prevParamGrade       = _target.paramGrade;
        _prevParamEasement    = _target.paramUseEasement;
        _prevParamEasementLen = _target.paramEasementLength;
        _prevParamTurnRight   = _target.paramTurnRight;
    }

    // ============================================================
    // Inspector GUI
    // ============================================================

    public override void OnInspectorGUI()
    {
        _target = (BezierRoadDeformer)target;
        serializedObject.Update();

        EditorGUILayout.LabelField("Sizimityper's Mesh Deformer", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        DrawBaseObjectSection();
        EditorGUILayout.Space(6);
        DrawCurveModeSection();
        EditorGUILayout.Space(6);
        DrawDeformModeSection();
        EditorGUILayout.Space(6);
        DrawPrefabPlacementSection();
        EditorGUILayout.Space(8);
        DrawActionButtons();

        serializedObject.ApplyModifiedProperties();
    }

    // ---- Base Object ----------------------------------------

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

    // ---- Curve Mode -----------------------------------------

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
        float newMaxI         = EditorGUILayout.FloatField("最大片勾配 (例: 0.08)",    _target.paramMaxSuperelevation);
        bool  newAutoApply    = EditorGUILayout.Toggle("カント角を自動適用",           _target.paramAutoApplyCant);
        bool  newAutoEasement = EditorGUILayout.Toggle("緩和区間長を自動算出",         _target.paramAutoCalcEasement);
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

    // ---- Deform Mode ----------------------------------------

    private void DrawDeformModeSection()
    {
        EditorGUILayout.LabelField("■ 変形モード", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        EditorGUI.BeginChangeCheck();
        var   newDeform  = (DeformMode)EditorGUILayout.EnumPopup("モード", _target.deformMode);
        int   newSubdiv   = EditorGUILayout.IntSlider("分割数", _target.subdivisions, 1, 100);
        float newPadding  = EditorGUILayout.Slider(
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

    // ---- Prefab Placement -----------------------------------

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

    // ---- Action Buttons -------------------------------------

    private void DrawActionButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Initialize"))
            {
                Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Initialize Road Deformer");
                _target.Initialize();
                CachePivotTransforms();
                EditorUtility.SetDirty(_target);
            }

            if (GUILayout.Button("Preview"))
            {
                _target.UpdatePreview();
                EditorUtility.SetDirty(_target);
            }

            if (GUILayout.Button("Bake"))
            {
                DoBake();
            }
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
    }

    // ============================================================
    // Scene GUI
    // ============================================================

    private void OnSceneGUI()
    {
        _target = (BezierRoadDeformer)target;
        if (_target == null) return;

        if (_target.curveMode == CurveMode.Pivot)
            DrawPivotSceneGUI();
        else
            DrawParameterSceneGUI();
    }

    private void DrawPivotSceneGUI()
    {
        var pivots = _target.pivots;
        if (pivots == null || pivots.Count < 2) return;

        int segCount = pivots.Count - 1;
        for (int seg = 0; seg < segCount; seg++)
        {
            Transform a = pivots[seg];
            Transform b = pivots[seg + 1];
            if (a == null || b == null) continue;

            float hl = _target.handleLength;
            Vector3 p0 = a.position, h0 = p0 + a.forward * hl;
            Vector3 p1 = b.position, h1 = p1 - b.forward * hl;

            Handles.DrawBezier(p0, p1, h0, h1, Color.white, null, 2f);

            Handles.color = new Color(1f, 1f, 0f, 0.6f);
            Handles.DrawDottedLine(p0, h0, 4f);
            Handles.DrawDottedLine(p1, h1, 4f);

            float disc = HandleUtility.GetHandleSize(h0) * 0.08f;
            Handles.DrawSolidDisc(h0, Camera.current.transform.forward, disc);
            Handles.DrawSolidDisc(h1, Camera.current.transform.forward, disc);
        }

        Handles.color = Color.cyan;
        for (int i = 0; i < pivots.Count; i++)
        {
            if (pivots[i] == null) continue;
            float offset = HandleUtility.GetHandleSize(pivots[i].position) * 0.5f;
            Handles.Label(pivots[i].position + Vector3.up * offset, pivots[i].name);
        }
    }

    private void DrawParameterSceneGUI()
    {
        if (!_target.paramPointsBuilt)
            _target.BuildArcLengthLUT();

        if (_target.paramPoints == null || _target.paramPoints.Count < 2) return;

        var pts = _target.paramPoints;
        Handles.color = Color.cyan;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 a = _target.transform.TransformPoint(pts[i]);
            Vector3 b = _target.transform.TransformPoint(pts[i + 1]);
            Handles.DrawLine(a, b);
        }
    }
}
