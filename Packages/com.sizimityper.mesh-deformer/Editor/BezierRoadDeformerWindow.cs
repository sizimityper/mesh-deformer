using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SizimityperMeshDeformer;

public class BezierRoadDeformerWindow : EditorWindow
{
    [MenuItem("Tools/sizimityper/mesh_deformer")]
    public static void Open() => GetWindow<BezierRoadDeformerWindow>("Mesh Deformer");

    private BezierRoadDeformer _target;
    private Vector2 _scroll;

    // ---- Change Detection Cache ----
    private CurveMode  _prevCurveMode;
    private DeformMode _prevDeformMode;
    private float      _prevTileAxisPadding;

    private float _prevParamR, _prevParamAngle, _prevParamCant, _prevParamGrade, _prevParamEaseLen;
    private bool  _prevTurnRight, _prevUseEasement, _prevGradeVerticalCurve;
    private float _prevDesignSpeed, _prevFrictionCoeff;
    private bool  _prevAutoDesignSpeed, _prevAutoFriction, _prevAutoApplyCant, _prevAutoEasement;

    private Transform _prevInterpStart, _prevInterpEnd;
    private Vector3   _prevInterpStartPos, _prevInterpEndPos;
    private Vector3   _prevInterpStartTan, _prevInterpEndTan;

    private float _prevStraightLength;

    // ============================================================

    void OnEnable()
    {
        EditorApplication.update  += OnEditorUpdate;
        Selection.selectionChanged += OnSelectionChanged;
        TrySelectFromScene();
    }

    void OnDisable()
    {
        EditorApplication.update  -= OnEditorUpdate;
        Selection.selectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        if (Selection.activeGameObject == null) return;
        var d = Selection.activeGameObject.GetComponent<BezierRoadDeformer>();
        if (d != null && d != _target)
        {
            _target = d;
            CacheAll();
            Repaint();
        }
    }

    private void TrySelectFromScene()
    {
        if (Selection.activeGameObject != null)
        {
            var d = Selection.activeGameObject.GetComponent<BezierRoadDeformer>();
            if (d != null) { _target = d; CacheAll(); return; }
        }
        // シーンに1つだけなら自動選択
        var all = Object.FindObjectsByType<BezierRoadDeformer>(FindObjectsSortMode.None);
        if (all.Length == 1) { _target = all[0]; CacheAll(); }
    }

    // ============================================================
    // Change Detection + Auto-Update
    // ============================================================

    private void OnEditorUpdate()
    {
        if (_target == null) return;
        if (!_target.IsPreviewActive()) return;

        bool changed = DetectChanges();
        if (changed)
        {
            if (_target.curveMode == CurveMode.Curve && _target.paramAutoCalcDesignSpeed)
            {
                float newSpeed = _target.CalcDesignSpeedFromR(_target.paramR);
                if (!Mathf.Approximately(newSpeed, _target.paramDesignSpeed))
                {
                    Undo.RecordObject(_target, "Auto Calc Design Speed");
                    _target.paramDesignSpeed = newSpeed;
                    EditorUtility.SetDirty(_target);
                }
            }
            if (_target.curveMode == CurveMode.Curve && _target.paramAutoApplyCant)
            {
                float nc = _target.CalcCantAngle();
                if (!Mathf.Approximately(nc, _target.paramCantAngle))
                {
                    Undo.RecordObject(_target, "Auto Calc Cant");
                    _target.paramCantAngle = nc;
                    EditorUtility.SetDirty(_target);
                }
            }
            if (_target.curveMode == CurveMode.Curve && _target.paramAutoCalcEasement)
            {
                float ne = _target.CalcEasementLengthFromSpeed(_target.paramDesignSpeed);
                if (!Mathf.Approximately(ne, _target.paramEasementLength))
                {
                    Undo.RecordObject(_target, "Auto Calc Easement");
                    _target.paramEasementLength = ne;
                    EditorUtility.SetDirty(_target);
                }
            }

            CacheAll();
            _target.arcLengthLUT = null;
            _target.UpdatePreview();
            EditorUtility.SetDirty(_target);
            Repaint();
        }
    }

    private bool DetectChanges()
    {
        if (_target.curveMode       != _prevCurveMode)       return true;
        if (_target.deformMode      != _prevDeformMode)      return true;
        if (_target.tileAxisPadding != _prevTileAxisPadding) return true;

        switch (_target.curveMode)
        {
            case CurveMode.Curve:
                return _target.paramR              != _prevParamR
                    || _target.paramAngle          != _prevParamAngle
                    || _target.paramTurnRight      != _prevTurnRight
                    || _target.paramCantAngle      != _prevParamCant
                    || _target.paramGrade          != _prevParamGrade
                    || _target.paramGradeVerticalCurve != _prevGradeVerticalCurve
                    || _target.paramUseEasement    != _prevUseEasement
                    || _target.paramEasementLength != _prevParamEaseLen
                    || _target.paramAutoCalcDesignSpeed != _prevAutoDesignSpeed
                    || _target.paramDesignSpeed         != _prevDesignSpeed
                    || _target.paramFrictionCoeff       != _prevFrictionCoeff
                    || _target.paramAutoCalcFriction    != _prevAutoFriction
                    || _target.paramAutoApplyCant       != _prevAutoApplyCant
                    || _target.paramAutoCalcEasement    != _prevAutoEasement;

            case CurveMode.Interpolation:
                return _target.interpStartObject != _prevInterpStart
                    || _target.interpEndObject   != _prevInterpEnd
                    || (_target.interpStartObject != null && _target.interpStartObject.position != _prevInterpStartPos)
                    || (_target.interpEndObject   != null && _target.interpEndObject.position   != _prevInterpEndPos)
                    || _target.interpStartTangent != _prevInterpStartTan
                    || _target.interpEndTangent   != _prevInterpEndTan;

            case CurveMode.Straight:
                return _target.paramStraightLength     != _prevStraightLength
                    || _target.paramGrade              != _prevParamGrade
                    || _target.paramGradeVerticalCurve != _prevGradeVerticalCurve;
        }
        return false;
    }

    private void CacheAll()
    {
        if (_target == null) return;
        _prevCurveMode        = _target.curveMode;
        _prevDeformMode       = _target.deformMode;
        _prevTileAxisPadding  = _target.tileAxisPadding;
        _prevParamR           = _target.paramR;
        _prevParamAngle       = _target.paramAngle;
        _prevTurnRight        = _target.paramTurnRight;
        _prevParamCant        = _target.paramCantAngle;
        _prevParamGrade       = _target.paramGrade;
        _prevGradeVerticalCurve = _target.paramGradeVerticalCurve;
        _prevUseEasement        = _target.paramUseEasement;
        _prevParamEaseLen       = _target.paramEasementLength;
        _prevAutoDesignSpeed  = _target.paramAutoCalcDesignSpeed;
        _prevDesignSpeed      = _target.paramDesignSpeed;
        _prevFrictionCoeff    = _target.paramFrictionCoeff;
        _prevAutoFriction     = _target.paramAutoCalcFriction;
        _prevAutoApplyCant    = _target.paramAutoApplyCant;
        _prevAutoEasement     = _target.paramAutoCalcEasement;
        _prevInterpStart      = _target.interpStartObject;
        _prevInterpEnd        = _target.interpEndObject;
        _prevInterpStartPos   = _target.interpStartObject != null ? _target.interpStartObject.position : Vector3.zero;
        _prevInterpEndPos     = _target.interpEndObject   != null ? _target.interpEndObject.position   : Vector3.zero;
        _prevInterpStartTan   = _target.interpStartTangent;
        _prevInterpEndTan     = _target.interpEndTangent;
        _prevStraightLength   = _target.paramStraightLength;
    }

    // ============================================================
    // GUI
    // ============================================================

    void OnGUI()
    {
        // Header: target selector
        DrawHeader();

        if (_target == null)
        {
            DrawNoTargetUI();
            return;
        }

        // Preview indicator
        DrawPreviewToggle();
        EditorGUILayout.Space(4);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawBaseObjectSection();
        EditorGUILayout.Space(6);
        DrawCurveModeSection();
        EditorGUILayout.Space(6);
        DrawDeformModeSection();
        EditorGUILayout.Space(6);
        DrawPrefabPlacementSection();
        EditorGUILayout.Space(8);
        DrawBakeButton();
        EditorGUILayout.EndScrollView();
    }

    // ---- No Target UI ---------------------------------------

    private void DrawNoTargetUI()
    {
        var existing = Object.FindObjectsByType<BezierRoadDeformer>(FindObjectsSortMode.None);

        if (existing.Length == 0)
        {
            EditorGUILayout.HelpBox("シーンに BezierRoadDeformer がありません。", MessageType.Info);
            EditorGUILayout.Space(4);
            if (GUILayout.Button("新規オブジェクトを作成", GUILayout.Height(32)))
            {
                var go = new GameObject("BezierRoad");
                Undo.RegisterCreatedObjectUndo(go, "Create BezierRoad");
                _target = Undo.AddComponent<BezierRoadDeformer>(go);
                Selection.activeGameObject = go;
                CacheAll();
                Repaint();
            }
        }
        else
        {
            EditorGUILayout.LabelField("対象を選択：", EditorStyles.boldLabel);
            foreach (var d in existing)
            {
                if (GUILayout.Button(d.gameObject.name))
                {
                    _target = d;
                    Selection.activeGameObject = d.gameObject;
                    CacheAll();
                    Repaint();
                }
            }
        }
    }

    // ---- Header ---------------------------------------------

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Sizimityper's Mesh Deformer", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        var newTarget = (BezierRoadDeformer)EditorGUILayout.ObjectField(
            "対象コンポーネント", _target, typeof(BezierRoadDeformer), true);
        if (EditorGUI.EndChangeCheck())
        {
            _target = newTarget;
            CacheAll();
        }
        EditorGUILayout.Space(4);
    }

    // ---- Preview Toggle -------------------------------------

    private void DrawPreviewToggle()
    {
        bool isPreview = _target != null && _target.IsPreviewActive();
        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = isPreview ? new Color(0.4f, 1f, 0.4f) : new Color(0.9f, 0.9f, 0.9f);

        if (GUILayout.Button(isPreview ? "■ PREVIEW ON  [クリックでOFF]" : "□ PREVIEW OFF [クリックでON]",
            GUILayout.Height(32)))
        {
            SetPreviewEnabled(!isPreview);
        }
        GUI.backgroundColor = prevBg;
    }

    private void SetPreviewEnabled(bool enable)
    {
        if (_target == null) return;

        if (!enable)
        {
            Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Disable Preview");
            _target.ClearPreviewObjects();
        }
        else
        {
            if (_target.sourceMeshEntries == null || _target.sourceMeshEntries.Count == 0)
            {
                if (_target.sourceParentObject != null)
                {
                    Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Initialize");
                    _target.Initialize();
                    CacheAll();
                }
                else
                {
                    EditorUtility.DisplayDialog("Preview Error", "ソース親オブジェクトを設定してください。", "OK");
                    return;
                }
            }
            _target.arcLengthLUT = null;
            _target.UpdatePreview();
        }
        EditorUtility.SetDirty(_target);
    }

    // ---- Base Object ----------------------------------------

    private void DrawBaseObjectSection()
    {
        EditorGUILayout.LabelField("■ ベースオブジェクト", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        EditorGUI.BeginChangeCheck();
        var newParent = (GameObject)EditorGUILayout.ObjectField(
            "ソース親オブジェクト", _target.sourceParentObject, typeof(GameObject), true);
        var newAxis   = (AxisDirection)EditorGUILayout.EnumPopup("軸線方向", _target.axisDirection);
        float newPad  = EditorGUILayout.Slider("タイル境界パディング", _target.tileAxisPadding, 0f, 0.1f);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Base Settings");
            bool parentChanged = newParent != _target.sourceParentObject;
            _target.sourceParentObject = newParent;
            _target.axisDirection      = newAxis;
            _target.tileAxisPadding    = newPad;

            if (parentChanged && newParent != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Initialize");
                _target.Initialize();
                CacheAll();
            }
            EditorUtility.SetDirty(_target);
        }

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
            CacheAll();
            EditorUtility.SetDirty(_target);
        }

        EditorGUILayout.Space(2);
        switch (_target.curveMode)
        {
            case CurveMode.Curve:         DrawCurveModeUI();         break;
            case CurveMode.Interpolation: DrawInterpolationModeUI(); break;
            case CurveMode.Straight:      DrawStraightModeUI();      break;
        }

        EditorGUI.indentLevel--;
    }

    private void DrawCurveModeUI()
    {
        EditorGUI.BeginChangeCheck();
        float newR      = EditorGUILayout.FloatField("R (曲率半径) m", _target.paramR);
        float newAngle  = EditorGUILayout.FloatField("展開角 °",        _target.paramAngle);
        bool  newRight  = EditorGUILayout.Toggle("右カーブ",            _target.paramTurnRight);
        float newGrade  = EditorGUILayout.FloatField("縦断勾配 %",       _target.paramGrade);
        bool  newVCurve = EditorGUILayout.Toggle("両端水平（縦断曲線）", _target.paramGradeVerticalCurve);
        bool  newEase   = EditorGUILayout.Toggle("緩和曲線",             _target.paramUseEasement);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Curve Params");
            _target.paramR                  = Mathf.Max(0.1f, newR);
            _target.paramAngle              = Mathf.Clamp(newAngle, 0f, 720f);
            _target.paramTurnRight          = newRight;
            _target.paramGrade             = newGrade;
            _target.paramGradeVerticalCurve = newVCurve;
            _target.paramUseEasement        = newEase;
            _target.arcLengthLUT            = null;
            _target.paramPointsBuilt        = false;
            EditorUtility.SetDirty(_target);
        }

        // --- 設計速度・自動計算 ---
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("  設計速度から自動計算", EditorStyles.miniLabel);

        EditorGUI.BeginChangeCheck();
        bool  newAutoSpeed = EditorGUILayout.Toggle("設計速度をRから自動取得", _target.paramAutoCalcDesignSpeed);
        float newSpeed;
        if (_target.paramAutoCalcDesignSpeed)
        {
            float autoSpeed = _target.CalcDesignSpeedFromR(_target.paramR);
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.FloatField("  設計速度 (算出) km/h", autoSpeed);
            newSpeed = autoSpeed;
        }
        else
        {
            newSpeed = EditorGUILayout.FloatField("設計速度 km/h", _target.paramDesignSpeed);
        }
        bool  newAutoFric = EditorGUILayout.Toggle("摩擦係数を自動取得",   _target.paramAutoCalcFriction);
        bool  newAutoEase = EditorGUILayout.Toggle("緩和区間長を自動設定", _target.paramAutoCalcEasement);
        bool  newAutoCant = EditorGUILayout.Toggle("カント角を自動適用",    _target.paramAutoApplyCant);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Auto Calc");
            _target.paramAutoCalcDesignSpeed = newAutoSpeed;
            _target.paramDesignSpeed         = Mathf.Max(0f, newSpeed);
            _target.paramAutoCalcFriction    = newAutoFric;
            _target.paramAutoCalcEasement    = newAutoEase;
            _target.paramAutoApplyCant       = newAutoCant;
            EditorUtility.SetDirty(_target);
        }

        // Readonly displays
        using (new EditorGUI.DisabledGroupScope(true))
        {
            if (_target.paramAutoCalcFriction)
                EditorGUILayout.FloatField("  摩擦係数 (算出)", _target.CalcFrictionFromSpeed(_target.paramDesignSpeed));
            if (_target.paramAutoCalcEasement && _target.paramUseEasement)
                EditorGUILayout.FloatField("  緩和区間長 (算出) m", _target.CalcEasementLengthFromSpeed(_target.paramDesignSpeed));
            if (_target.paramAutoApplyCant)
                EditorGUILayout.FloatField("  カント角 (算出) °", _target.CalcCantAngle());
        }

        if (!_target.paramAutoCalcFriction)
        {
            EditorGUI.BeginChangeCheck();
            float f = EditorGUILayout.FloatField("横すべり摩擦係数", _target.paramFrictionCoeff);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "Change Friction");
                _target.paramFrictionCoeff = f;
                EditorUtility.SetDirty(_target);
            }
        }
        if (_target.paramUseEasement && !_target.paramAutoCalcEasement)
        {
            EditorGUI.BeginChangeCheck();
            float el = EditorGUILayout.FloatField("緩和区間長 m", _target.paramEasementLength);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "Change Easement Length");
                _target.paramEasementLength = Mathf.Max(0f, el);
                _target.arcLengthLUT        = null;
                _target.paramPointsBuilt    = false;
                EditorUtility.SetDirty(_target);
            }
        }
        if (!_target.paramAutoApplyCant)
        {
            EditorGUI.BeginChangeCheck();
            float ca = EditorGUILayout.FloatField("カント角 °", _target.paramCantAngle);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "Change Cant Angle");
                _target.paramCantAngle   = ca;
                _target.arcLengthLUT     = null;
                _target.paramPointsBuilt = false;
                EditorUtility.SetDirty(_target);
            }
        }
    }

    private void DrawInterpolationModeUI()
    {
        EditorGUI.BeginChangeCheck();
        var newStart    = (Transform)EditorGUILayout.ObjectField("始点オブジェクト", _target.interpStartObject, typeof(Transform), true);
        var newStartTan = EditorGUILayout.Vector3Field("始点接線方向",              _target.interpStartTangent);
        var newEnd      = (Transform)EditorGUILayout.ObjectField("終点オブジェクト", _target.interpEndObject,   typeof(Transform), true);
        var newEndTan   = EditorGUILayout.Vector3Field("終点接線方向",              _target.interpEndTangent);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Interpolation Params");
            _target.interpStartObject  = newStart;
            _target.interpStartTangent = newStartTan;
            _target.interpEndObject    = newEnd;
            _target.interpEndTangent   = newEndTan;
            _target.arcLengthLUT       = null;
            _target.paramPointsBuilt   = false;
            EditorUtility.SetDirty(_target);
        }
    }

    private void DrawStraightModeUI()
    {
        EditorGUI.BeginChangeCheck();
        float newLen    = EditorGUILayout.FloatField("長さ m",           _target.paramStraightLength);
        float newGrade  = EditorGUILayout.FloatField("縦断勾配 %",       _target.paramGrade);
        bool  newVCurve = EditorGUILayout.Toggle("両端水平（縦断曲線）", _target.paramGradeVerticalCurve);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Straight Params");
            _target.paramStraightLength     = Mathf.Max(0.1f, newLen);
            _target.paramGrade              = newGrade;
            _target.paramGradeVerticalCurve = newVCurve;
            _target.arcLengthLUT            = null;
            _target.paramPointsBuilt        = false;
            EditorUtility.SetDirty(_target);
        }
    }

    // ---- Deform Mode ----------------------------------------

    private void DrawDeformModeSection()
    {
        EditorGUILayout.LabelField("■ 端部処理", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        EditorGUI.BeginChangeCheck();
        var newDeform = (DeformMode)EditorGUILayout.EnumPopup("端部処理", _target.deformMode);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Deform Mode");
            _target.deformMode = newDeform;
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
            rule.prefab      = (GameObject)EditorGUILayout.ObjectField("プレハブ",     rule.prefab,      typeof(GameObject), false);
            rule.intervalM   = EditorGUILayout.FloatField("間隔 m",        rule.intervalM);
            rule.offsetRight = EditorGUILayout.FloatField("右オフセット m", rule.offsetRight);
            rule.offsetUp    = EditorGUILayout.FloatField("上オフセット m", rule.offsetUp);
            rule.followCant  = EditorGUILayout.Toggle("カント追従",          rule.followCant);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_target);

            EditorGUI.indentLevel--;
            if (GUILayout.Button("ルール削除"))
            {
                Undo.RecordObject(_target, "Remove Placement Rule");
                _target.placementRules.RemoveAt(i);
                EditorUtility.SetDirty(_target);
                EditorGUILayout.EndVertical();
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

    // ---- Bake -----------------------------------------------

    private void DrawBakeButton()
    {
        if (GUILayout.Button("Bake", GUILayout.Height(28)))
            DoBake();
    }

    private void DoBake()
    {
        if (_target == null) return;

        if (_target.sourceMeshEntries == null || _target.sourceMeshEntries.Count == 0)
        {
            if (_target.sourceParentObject != null)
            {
                _target.Initialize();
                CacheAll();
            }
            else
            {
                EditorUtility.DisplayDialog("Bake Error", "ソース親オブジェクトが設定されていません。", "OK");
                return;
            }
        }

        Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Bake");

        var meshes = _target.DeformAllMeshes();
        for (int i = 0; i < _target.sourceMeshEntries.Count; i++)
        {
            var entry = _target.sourceMeshEntries[i];
            var mesh  = i < meshes.Count ? meshes[i] : null;
            if (mesh == null) continue;

            GameObject go;
            if (entry.outputObject != null)
            {
                go           = entry.outputObject;
                go.hideFlags = HideFlags.None;
            }
            else
            {
                go = new GameObject(entry.meshName ?? $"Mesh_{i}");
                go.transform.SetParent(_target.transform, false);
            }

            var mf = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
            if (entry.materials != null) mr.sharedMaterials = entry.materials;

            go.hideFlags       = HideFlags.None;
            entry.outputObject = null;
            Undo.RegisterCreatedObjectUndo(go, "Bake");
        }

        _target.UpdatePrefabPlacements();
        foreach (var go in _target.spawnedPrefabs)
            if (go != null) go.hideFlags = HideFlags.None;
        _target.spawnedPrefabs.Clear();

        DestroyImmediate(_target);
        _target = null;
        Repaint();
    }
}
