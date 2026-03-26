using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SizimityperMeshDeformer;

[CustomEditor(typeof(BezierRoadDeformer))]
public class BezierRoadDeformerEditor : Editor
{
    private BezierRoadDeformer _target;

    // ---- Change Detection Cache ----
    private CurveMode  _prevCurveMode;
    private DeformMode _prevDeformMode;
    private float _prevTileAxisPadding;

    // Curve mode
    private float _prevParamR, _prevParamAngle, _prevParamCant, _prevParamGrade, _prevParamEaseLen;
    private bool  _prevTurnRight, _prevUseEasement, _prevGradeVerticalCurve;
    private float _prevDesignSpeed, _prevFrictionCoeff;
    private bool  _prevAutoDesignSpeed, _prevAutoFriction, _prevAutoApplyCant, _prevAutoEasement;

    // Interpolation mode
    private Transform   _prevInterpStart, _prevInterpEnd;
    private Vector3     _prevInterpStartPos, _prevInterpEndPos;
    private Quaternion  _prevInterpStartRot, _prevInterpEndRot;
    private TangentAxis _prevInterpStartAxis, _prevInterpEndAxis;
    private bool        _prevInterpAutoCalcCant;
    private float       _prevInterpMidCant;
    private InterpTangentScaleMode _prevInterpTangentScaleMode;
    private float       _prevInterpTangentScale;
    private bool        _prevInterpTangentScaleIndividual;
    private float       _prevInterpStartTangentScale;
    private float       _prevInterpEndTangentScale;

    // Straight mode
    private float _prevStraightLength;
    private bool  _prevStraightAutoGrade;
    private float _prevStraightHeight;

    // Prefab placement
    private int _prevPlacementRulesHash;

    // ============================================================

    void OnEnable()
    {
        _target = (BezierRoadDeformer)target;
        CacheAll();
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

        bool changed = DetectChanges();

        // プレビューOFF時: 変化があればLUTを無効化してシーンを再描画
        if (!_target.IsPreviewActive())
        {
            if (changed)
            {
                CacheAll();
                _target.arcLengthLUT    = null;
                _target.paramPointsBuilt = false;
                EditorUtility.SetDirty(_target);
            }
            if (!_target.paramPointsBuilt)
                SceneView.RepaintAll();
            return;
        }

        if (changed)
        {
            // Auto-apply design speed from R
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
            // Auto-apply cant if enabled
            if (_target.curveMode == CurveMode.Curve && _target.paramAutoApplyCant)
            {
                float newCant = _target.CalcCantAngle();
                if (!Mathf.Approximately(newCant, _target.paramCantAngle))
                {
                    Undo.RecordObject(_target, "Auto Calc Cant");
                    _target.paramCantAngle = newCant;
                    EditorUtility.SetDirty(_target);
                }
            }
            // Auto-apply easement if enabled
            if (_target.curveMode == CurveMode.Curve && _target.paramAutoCalcEasement)
            {
                float newEase = _target.CalcEasementLengthFromSpeed(_target.paramDesignSpeed);
                if (!Mathf.Approximately(newEase, _target.paramEasementLength))
                {
                    Undo.RecordObject(_target, "Auto Calc Easement");
                    _target.paramEasementLength = newEase;
                    EditorUtility.SetDirty(_target);
                }
            }

            CacheAll();
            _target.arcLengthLUT = null;
            _target.UpdatePreview();
            EditorUtility.SetDirty(_target);
        }
    }

    private bool DetectChanges()
    {
        if (_target.curveMode     != _prevCurveMode)     return true;
        if (_target.deformMode    != _prevDeformMode)    return true;
        if (_target.tileAxisPadding != _prevTileAxisPadding) return true;
        if (ComputePlacementRulesHash() != _prevPlacementRulesHash) return true;

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
                return _target.interpStartObject        != _prevInterpStart
                    || _target.interpEndObject          != _prevInterpEnd
                    || _target.interpStartTangentAxis   != _prevInterpStartAxis
                    || _target.interpEndTangentAxis     != _prevInterpEndAxis
                    || _target.paramInterpAutoCalcCant  != _prevInterpAutoCalcCant
                    || (_target.interpStartObject != null && _target.interpStartObject.position != _prevInterpStartPos)
                    || (_target.interpEndObject   != null && _target.interpEndObject.position   != _prevInterpEndPos)
                    || (_target.interpStartObject != null && _target.interpStartObject.rotation != _prevInterpStartRot)
                    || (_target.interpEndObject   != null && _target.interpEndObject.rotation   != _prevInterpEndRot)
                    || _target.paramCantAngle      != _prevParamCant
                    || _target.paramUseEasement    != _prevUseEasement
                    || _target.paramEasementLength != _prevParamEaseLen
                    || (!_target.paramInterpAutoCalcCant && _target.interpMidCantAngle != _prevInterpMidCant)
                    || _target.interpTangentScaleMode         != _prevInterpTangentScaleMode
                    || _target.interpTangentScale             != _prevInterpTangentScale
                    || _target.interpTangentScaleIndividual   != _prevInterpTangentScaleIndividual
                    || _target.interpStartTangentScale        != _prevInterpStartTangentScale
                    || _target.interpEndTangentScale          != _prevInterpEndTangentScale;

            case CurveMode.Straight:
                return _target.paramStraightLength     != _prevStraightLength
                    || _target.paramStraightAutoGrade  != _prevStraightAutoGrade
                    || _target.paramStraightHeight     != _prevStraightHeight
                    || _target.paramGrade              != _prevParamGrade
                    || _target.paramGradeVerticalCurve != _prevGradeVerticalCurve;
        }
        return false;
    }

    private void CacheAll()
    {
        if (_target == null) return;
        _prevCurveMode    = _target.curveMode;
        _prevDeformMode   = _target.deformMode;
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

        _prevInterpStart         = _target.interpStartObject;
        _prevInterpEnd           = _target.interpEndObject;
        _prevInterpStartAxis     = _target.interpStartTangentAxis;
        _prevInterpEndAxis       = _target.interpEndTangentAxis;
        _prevInterpAutoCalcCant          = _target.paramInterpAutoCalcCant;
        _prevInterpMidCant               = _target.interpMidCantAngle;
        _prevInterpTangentScaleMode       = _target.interpTangentScaleMode;
        _prevInterpTangentScale          = _target.interpTangentScale;
        _prevInterpTangentScaleIndividual = _target.interpTangentScaleIndividual;
        _prevInterpStartTangentScale     = _target.interpStartTangentScale;
        _prevInterpEndTangentScale       = _target.interpEndTangentScale;
        _prevInterpStartPos      = _target.interpStartObject != null ? _target.interpStartObject.position : Vector3.zero;
        _prevInterpEndPos        = _target.interpEndObject   != null ? _target.interpEndObject.position   : Vector3.zero;
        _prevInterpStartRot      = _target.interpStartObject != null ? _target.interpStartObject.rotation : Quaternion.identity;
        _prevInterpEndRot        = _target.interpEndObject   != null ? _target.interpEndObject.rotation   : Quaternion.identity;

        _prevStraightLength     = _target.paramStraightLength;
        _prevStraightAutoGrade  = _target.paramStraightAutoGrade;
        _prevStraightHeight     = _target.paramStraightHeight;
        _prevPlacementRulesHash = ComputePlacementRulesHash();
    }

    private int ComputePlacementRulesHash()
    {
        if (_target.placementRules == null) return 0;
        int h = _target.placementRules.Count;
        foreach (var r in _target.placementRules)
        {
            if (r == null) continue;
            h = h * 31 + (r.prefab != null ? r.prefab.GetHashCode() : 0);
            h = h * 31 + r.intervalM.GetHashCode();
            h = h * 31 + r.autoInterval.GetHashCode();
            h = h * 31 + r.positionOffset.GetHashCode();
            h = h * 31 + r.rotationOffset.GetHashCode();
            h = h * 31 + r.followCant.GetHashCode();
        }
        return h;
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

        float newR         = EditorGUILayout.FloatField("R (曲率半径) m",  _target.paramR);
        float newAngle     = EditorGUILayout.FloatField("展開角 °",         _target.paramAngle);
        bool  newRight     = EditorGUILayout.Toggle("右カーブ",             _target.paramTurnRight);
        bool  newAutoGrade = EditorGUILayout.Toggle("高さから勾配を自動算出", _target.paramCurveAutoGrade);
        float newGrade, newCurveHeight;
        if (newAutoGrade)
        {
            newCurveHeight = EditorGUILayout.FloatField("高さ m", _target.paramCurveHeight);
            float arcLen   = CalcCurveArcLength(Mathf.Max(0.1f, newR), Mathf.Clamp(newAngle, 0f, 720f),
                                                _target.paramUseEasement, _target.paramEasementLength);
            newGrade = arcLen > 0f ? newCurveHeight / arcLen * 100f : 0f;
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.FloatField("  縦断勾配 (算出) %", newGrade);
        }
        else
        {
            newCurveHeight = _target.paramCurveHeight;
            newGrade       = EditorGUILayout.FloatField("縦断勾配 %", _target.paramGrade);
        }
        bool  newVCurve = EditorGUILayout.Toggle("両端水平（縦断曲線）",   _target.paramGradeVerticalCurve);
        bool  newEase   = EditorGUILayout.Toggle("緩和曲線",              _target.paramUseEasement);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Curve Params");
            _target.paramR                  = Mathf.Max(0.1f, newR);
            _target.paramAngle              = Mathf.Clamp(newAngle, 0f, 720f);
            _target.paramTurnRight          = newRight;
            _target.paramCurveAutoGrade     = newAutoGrade;
            _target.paramCurveHeight        = newCurveHeight;
            _target.paramGrade              = newGrade;
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
        bool  newAutoFric  = EditorGUILayout.Toggle("摩擦係数を自動取得",    _target.paramAutoCalcFriction);
        float newFric      = _target.paramAutoCalcFriction
            ? _target.CalcFrictionFromSpeed(_target.paramDesignSpeed)
            : EditorGUILayout.FloatField("横すべり摩擦係数", _target.paramFrictionCoeff);
        bool  newAutoEase  = EditorGUILayout.Toggle("緩和区間長を自動設定",  _target.paramAutoCalcEasement);
        bool  newAutoCant  = EditorGUILayout.Toggle("カント角を自動適用",     _target.paramAutoApplyCant);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Auto Calc");
            _target.paramAutoCalcDesignSpeed = newAutoSpeed;
            _target.paramDesignSpeed         = Mathf.Max(0f, newSpeed);
            _target.paramAutoCalcFriction    = newAutoFric;
            if (!newAutoFric) _target.paramFrictionCoeff = newFric;
            _target.paramAutoCalcEasement = newAutoEase;
            _target.paramAutoApplyCant    = newAutoCant;
            EditorUtility.SetDirty(_target);
        }

        // Readonly friction display
        if (_target.paramAutoCalcFriction)
        {
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.FloatField("  横すべり摩擦係数 (算出)", _target.CalcFrictionFromSpeed(_target.paramDesignSpeed));
        }

        // Easement length
        EditorGUI.BeginChangeCheck();
        float displayEase = _target.paramAutoCalcEasement
            ? _target.CalcEasementLengthFromSpeed(_target.paramDesignSpeed)
            : _target.paramEasementLength;
        if (_target.paramAutoCalcEasement)
        {
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.FloatField("  緩和区間長 (算出) m", displayEase);
        }
        else if (_target.paramUseEasement)
        {
            float editedEase = EditorGUILayout.FloatField("緩和区間長 m", _target.paramEasementLength);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "Change Easement Length");
                _target.paramEasementLength = Mathf.Max(0f, editedEase);
                _target.arcLengthLUT        = null;
                _target.paramPointsBuilt    = false;
                EditorUtility.SetDirty(_target);
            }
        }
        else EditorGUI.EndChangeCheck();

        // Sync auto-calc values
        if (_target.paramAutoCalcEasement && _target.paramUseEasement)
        {
            float auto = _target.CalcEasementLengthFromSpeed(_target.paramDesignSpeed);
            if (!Mathf.Approximately(auto, _target.paramEasementLength))
            {
                Undo.RecordObject(_target, "Sync Easement");
                _target.paramEasementLength = auto;
                _target.arcLengthLUT        = null;
                EditorUtility.SetDirty(_target);
            }
        }

        // Cant angle
        float previewCant = _target.paramAutoApplyCant
            ? _target.CalcCantAngle()
            : _target.paramCantAngle;

        EditorGUI.BeginChangeCheck();
        if (_target.paramAutoApplyCant)
        {
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.FloatField("  カント角 (算出) °", previewCant);
        }
        else
        {
            float editedCant = EditorGUILayout.FloatField("カント角 °", _target.paramCantAngle);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_target, "Change Cant Angle");
                _target.paramCantAngle   = editedCant;
                _target.arcLengthLUT     = null;
                _target.paramPointsBuilt = false;
                EditorUtility.SetDirty(_target);
                return;
            }
        }
        EditorGUI.EndChangeCheck();

        if (_target.paramAutoApplyCant && !Mathf.Approximately(previewCant, _target.paramCantAngle))
        {
            Undo.RecordObject(_target, "Sync Cant");
            _target.paramCantAngle = previewCant;
            _target.arcLengthLUT   = null;
            EditorUtility.SetDirty(_target);
        }
    }

    private void DrawInterpolationModeUI()
    {
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.HelpBox("始点・終点オブジェクトのRotation（接線軸まわりのロール）がカント角として使われます。", MessageType.None);
        var  newStart     = (Transform)EditorGUILayout.ObjectField("始点オブジェクト", _target.interpStartObject, typeof(Transform), true);
        var  newStartAxis = (TangentAxis)EditorGUILayout.EnumPopup("  接線軸",         _target.interpStartTangentAxis);
        var  newEnd       = (Transform)EditorGUILayout.ObjectField("終点オブジェクト", _target.interpEndObject,   typeof(Transform), true);
        var  newEndAxis   = (TangentAxis)EditorGUILayout.EnumPopup("  接線軸",         _target.interpEndTangentAxis);

        // 現在のカント角をリアルタイム表示
        if (_target.interpStartObject != null)
        {
            float c = _target.GetCantFromObjectRotation(_target.interpStartObject, _target.interpStartTangentAxis);
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.FloatField("  始点カント角 (算出) °", c);
        }
        if (_target.interpEndObject != null)
        {
            float c = _target.GetCantFromObjectRotation(_target.interpEndObject, _target.interpEndTangentAxis);
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.FloatField("  終点カント角 (算出) °", c);
        }

        // --- カント自動算出（曲率ベース）---
        bool  newAutoCalcCant = EditorGUILayout.Toggle("自動モード", _target.paramInterpAutoCalcCant);
        float newMidCant = _target.interpMidCantAngle;
        if (newAutoCalcCant)
        {
            // R → V → f → カント角 を全自動算出、結果のみ表示
            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.FloatField("  R (算出) m",          _target.interpComputedR);
                EditorGUILayout.FloatField("  設計速度 (算出) km/h", _target.interpComputedDesignSpeed);
                EditorGUILayout.FloatField("  摩擦係数 (算出)",      _target.interpComputedFriction);
                EditorGUILayout.FloatField("  中間カント角 (算出) °", _target.interpMidCantComputed);
            }
        }
        else
        {
            newMidCant = EditorGUILayout.FloatField("中間カント角 (t=0.5) °", _target.interpMidCantAngle);
        }

        // --- タンジェントスケール ---
        EditorGUILayout.Space(4);
        var   newScaleMode     = _target.interpTangentScaleMode;
        bool  newIndividual    = _target.interpTangentScaleIndividual;
        float newTanScale      = _target.interpTangentScale;
        float newTanScaleStart = _target.interpStartTangentScale;
        float newTanScaleEnd   = _target.interpEndTangentScale;
        if (newAutoCalcCant)
        {
            // 自動モード時はAngleBasedに固定、算出値のみ表示
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.FloatField("タンジェントスケール (算出)", _target.interpComputedTangentScale);
        }
        else
        {
            newIndividual = EditorGUILayout.Toggle("タンジェントスケール個別調整", _target.interpTangentScaleIndividual);
            if (newIndividual)
            {
                newTanScaleStart = EditorGUILayout.FloatField("  始点スケール", _target.interpStartTangentScale);
                newTanScaleEnd   = EditorGUILayout.FloatField("  終点スケール", _target.interpEndTangentScale);
            }
            else
            {
                newTanScale = EditorGUILayout.FloatField("タンジェントスケール", _target.interpTangentScale);
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Interpolation Params");
            _target.interpStartObject             = newStart;
            _target.interpStartTangentAxis        = newStartAxis;
            _target.interpEndObject               = newEnd;
            _target.interpEndTangentAxis          = newEndAxis;
            _target.paramInterpAutoCalcCant       = newAutoCalcCant;
            _target.interpMidCantAngle            = newMidCant;
            _target.interpTangentScaleMode        = newScaleMode;
            _target.interpTangentScaleIndividual  = newIndividual;
            _target.interpTangentScale            = Mathf.Max(0f, newTanScale);
            _target.interpStartTangentScale       = Mathf.Max(0f, newTanScaleStart);
            _target.interpEndTangentScale         = Mathf.Max(0f, newTanScaleEnd);
            _target.arcLengthLUT                  = null;
            _target.paramPointsBuilt              = false;
            EditorUtility.SetDirty(_target);
        }
    }

    private void DrawStraightModeUI()
    {
        EditorGUI.BeginChangeCheck();
        float newLen       = EditorGUILayout.FloatField("長さ m",               _target.paramStraightLength);
        bool  newAutoGrade = EditorGUILayout.Toggle("高さから勾配を自動算出",    _target.paramStraightAutoGrade);
        float newGrade, newHeight;
        if (newAutoGrade)
        {
            newHeight = EditorGUILayout.FloatField("高さ m", _target.paramStraightHeight);
            float len = Mathf.Max(0.1f, newLen);
            newGrade  = newHeight / len * 100f;
            using (new EditorGUI.DisabledGroupScope(true))
                EditorGUILayout.FloatField("  縦断勾配 (算出) %", newGrade);
        }
        else
        {
            newHeight = _target.paramStraightHeight;
            newGrade  = EditorGUILayout.FloatField("縦断勾配 %", _target.paramGrade);
        }
        bool newVCurve = EditorGUILayout.Toggle("両端水平（縦断曲線）", _target.paramGradeVerticalCurve);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Straight Params");
            _target.paramStraightLength     = Mathf.Max(0.1f, newLen);
            _target.paramStraightAutoGrade  = newAutoGrade;
            _target.paramStraightHeight     = newHeight;
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
            rule.prefab          = (GameObject)EditorGUILayout.ObjectField("プレハブ", rule.prefab, typeof(GameObject), false);
            rule.autoInterval    = EditorGUILayout.Toggle("メッシュ長に追従",      rule.autoInterval);
            using (new EditorGUI.DisabledScope(rule.autoInterval))
                rule.intervalM   = EditorGUILayout.FloatField("間隔 m",           rule.intervalM);
            EditorGUILayout.LabelField("位置オフセット m");
            EditorGUI.indentLevel++;
            rule.positionOffset = new Vector3(
                EditorGUILayout.FloatField("左右", rule.positionOffset.x),
                EditorGUILayout.FloatField("上下", rule.positionOffset.y),
                EditorGUILayout.FloatField("前後", rule.positionOffset.z));
            EditorGUI.indentLevel--;
            EditorGUILayout.LabelField("回転オフセット °");
            EditorGUI.indentLevel++;
            rule.rotationOffset = new Vector3(
                EditorGUILayout.FloatField("左右軸", rule.rotationOffset.x),
                EditorGUILayout.FloatField("上下軸", rule.rotationOffset.y),
                EditorGUILayout.FloatField("前後軸", rule.rotationOffset.z));
            EditorGUI.indentLevel--;
            rule.followCant      = EditorGUILayout.Toggle("カント追従",            rule.followCant);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_target);

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
        bool isPreview = _target.IsPreviewActive();

        // Preview toggle
        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = isPreview ? new Color(0.4f, 1f, 0.4f) : Color.white;
        if (GUILayout.Button(isPreview ? "■ PREVIEW ON" : "□ PREVIEW OFF", GUILayout.Height(28)))
        {
            if (isPreview)
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
                }
                if (!ConfirmIfExcessiveTiles()) return;
                _target.arcLengthLUT = null;
                _target.UpdatePreview();
            }
            EditorUtility.SetDirty(_target);
        }
        GUI.backgroundColor = prevColor;

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Bake"))
            DoBake();
    }

    /// <summary>Curve モードの弧長を近似計算（LUT不要）</summary>
    private static float CalcCurveArcLength(float R, float angleDeg, bool useEasement, float easementLength)
    {
        float totalAngleRad = angleDeg * Mathf.Deg2Rad;
        float easLen        = useEasement ? Mathf.Max(easementLength, 0f) : 0f;
        float easAngle      = easLen > 0f ? easLen / (2f * R) : 0f;
        float arcAngleRad   = Mathf.Max(0f, totalAngleRad - 2f * easAngle);
        return 2f * easLen + R * arcAngleRad;
    }

    private static readonly int TILE_WARN_THRESHOLD = 500;

    /// <summary>タイル数が過大な場合に確認ダイアログを表示。続行するなら true を返す。</summary>
    private bool ConfirmIfExcessiveTiles()
    {
        int maxTiles = CalcMaxEstimatedTileCount();
        if (maxTiles <= TILE_WARN_THRESHOLD) return true;

        return EditorUtility.DisplayDialog(
            "タイル数が多すぎる可能性があります",
            $"推定タイル数: {maxTiles} 回\n\n" +
            $"軸線方向「{_target.axisDirection}」に対するメッシュの奥行きが非常に小さいため、\n" +
            $"繰り返し回数が過大になっています。\n\n" +
            "軸線方向の設定が正しいか確認してください。\n\nそれでも続けますか？",
            "続ける",
            "キャンセル");
    }

    private int CalcMaxEstimatedTileCount()
    {
        if (_target.sourceMeshEntries == null || _target.sourceMeshEntries.Count == 0) return 0;
        if (_target.arcLengthLUT == null || !_target.paramPointsBuilt)
            _target.BuildArcLengthLUT();
        if (_target.totalArcLength <= 0f) return 0;

        int max = 0;
        foreach (var entry in _target.sourceMeshEntries)
        {
            if (entry.mesh == null) continue;
            var verts = entry.mesh.vertices;
            float minA = float.MaxValue, maxA = float.MinValue;
            foreach (var v in verts)
            {
                float a = _target.axisDirection == AxisDirection.X ? v.x
                        : _target.axisDirection == AxisDirection.Y ? v.y
                        : v.z;
                if (a < minA) minA = a;
                if (a > maxA) maxA = a;
            }
            float meshLen = Mathf.Max(maxA - minA, 1e-6f);
            int tiles = Mathf.Max(1, Mathf.CeilToInt(_target.totalArcLength / meshLen));
            if (tiles > max) max = tiles;
        }
        return max;
    }

    // ============================================================
    // Bake
    // ============================================================

    private void DoBake()
    {
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

        if (!ConfirmIfExcessiveTiles()) return;

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
                go            = entry.outputObject;
                go.hideFlags  = HideFlags.None;
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

            go.hideFlags = HideFlags.None;
            entry.outputObject = null;
            Undo.RegisterCreatedObjectUndo(go, "Bake");
        }

        _target.UpdatePrefabPlacements();
        foreach (var go in _target.spawnedPrefabs)
            if (go != null) go.hideFlags = HideFlags.None;
        _target.spawnedPrefabs.Clear();

        DestroyImmediate(_target);
    }

    // ============================================================
    // Scene GUI（選択時のインタラクティブハンドル）
    // ============================================================

    private void OnSceneGUI()
    {
        _target = (BezierRoadDeformer)target;
        if (_target == null) return;

        // Interpolation モードの始点・終点ハンドル
        if (_target.curveMode == CurveMode.Interpolation)
        {
            if (_target.interpStartObject != null)
            {
                Handles.color = Color.green;
                float   sz  = HandleUtility.GetHandleSize(_target.interpStartObject.position) * 0.2f;
                Vector3 dir = _target.GetTangentDirection(_target.interpStartObject, _target.interpStartTangentAxis);
                Handles.DrawSolidDisc(_target.interpStartObject.position, Camera.current.transform.forward, sz);
                Handles.DrawLine(_target.interpStartObject.position, _target.interpStartObject.position + dir * sz * 3f);
            }
            if (_target.interpEndObject != null)
            {
                Handles.color = Color.red;
                float   sz  = HandleUtility.GetHandleSize(_target.interpEndObject.position) * 0.2f;
                Vector3 dir = _target.GetTangentDirection(_target.interpEndObject, _target.interpEndTangentAxis);
                Handles.DrawSolidDisc(_target.interpEndObject.position, Camera.current.transform.forward, sz);
                Handles.DrawLine(_target.interpEndObject.position, _target.interpEndObject.position + dir * sz * 3f);
            }
        }

        // プレハブ配置のビジュアライズ
        DrawPrefabPlacementGizmos();
    }

    private void DrawPrefabPlacementGizmos()
    {
        if (_target.placementRules == null || _target.placementRules.Count == 0) return;
        if (!_target.paramPointsBuilt) return;

        foreach (var rule in _target.placementRules)
        {
            if (rule.prefab == null) continue;

            var sValues = _target.GetPlacementSValues(rule);

            foreach (float s in sValues)
            {
                float cant = _target.GetCantAtS(s);
                var sp = _target.EvaluateAtArcLength(s, cant);

                Vector3 pos = sp.position
                    + sp.binormal * rule.positionOffset.x
                    + sp.normal   * rule.positionOffset.y
                    + sp.tangent  * rule.positionOffset.z;

                Quaternion rot = rule.followCant
                    ? Quaternion.LookRotation(sp.tangent, sp.normal)
                    : Quaternion.LookRotation(sp.tangent, Vector3.up);
                rot = rot * Quaternion.Euler(rule.rotationOffset);

                float sz = HandleUtility.GetHandleSize(pos) * 0.3f;

                // 前後（青）
                Handles.color = Color.blue;
                Handles.DrawLine(pos, pos + rot * Vector3.forward * sz);
                // 上下（緑）
                Handles.color = Color.green;
                Handles.DrawLine(pos, pos + rot * Vector3.up * sz);
                // 左右（赤）
                Handles.color = Color.red;
                Handles.DrawLine(pos, pos + rot * Vector3.right * sz);
                // 中心点
                Handles.color = Color.yellow;
                Handles.DrawSolidDisc(pos, Camera.current.transform.forward, sz * 0.15f);
            }
        }
    }

    // ============================================================
    // Gizmo（選択状態に関係なく常に描画）
    // ============================================================

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.InSelectionHierarchy)]
    static void DrawCurveGizmo(BezierRoadDeformer target, GizmoType gizmoType)
    {
        bool needRebuild = !target.paramPointsBuilt || target.paramPoints == null
            || (target.curveMode == CurveMode.Interpolation && target.interpCantLUT == null);
        if (needRebuild) target.BuildArcLengthLUT();
        if (target.paramPoints == null || target.paramPoints.Count < 2) return;

        var   pts   = target.paramPoints;
        var   lut   = target.arcLengthLUT;
        float total = target.totalArcLength;

        // 緩和曲線区間の判定
        bool  hasEase = target.curveMode == CurveMode.Curve
                     && target.paramUseEasement
                     && target.paramEasementLength > 0f;
        float easeLen = hasEase ? target.paramEasementLength : 0f;
        float arcEnd  = total - easeLen;

        // ---- 1. カーブライン（緩和曲線は黄、円弧はオレンジ）----
        for (int i = 0; i < pts.Count - 1; i++)
        {
            float s = lut != null && lut.Length > i ? lut[i] : 0f;
            bool  isEase = hasEase && (s < easeLen || s > arcEnd);
            Gizmos.color = isEase ? new Color(1f, 0.9f, 0f) : new Color(1f, 0.5f, 0.1f);
            Gizmos.DrawLine(target.transform.TransformPoint(pts[i]),
                            target.transform.TransformPoint(pts[i + 1]));
        }

        if (total <= 0f) return;

        // ---- 2. 断面（幅・高さ・カント）を等間隔で描画 ----
        float minR = -0.5f, maxR = 0.5f, minU = 0f, maxU = 1f;
        if (target.sourceMeshEntries != null && target.sourceMeshEntries.Count > 0)
        {
            float tMinR = float.MaxValue, tMaxR = float.MinValue;
            float tMinU = float.MaxValue, tMaxU = float.MinValue;
            foreach (var entry in target.sourceMeshEntries)
            {
                if (entry.mesh == null) continue;
                foreach (var v in entry.mesh.vertices)
                {
                    float r, u;
                    switch (target.axisDirection)
                    {
                        case AxisDirection.X: r = v.z; u = v.y; break;
                        case AxisDirection.Y: r = v.x; u = v.z; break;
                        default:             r = v.x; u = v.y; break;
                    }
                    if (r < tMinR) tMinR = r;
                    if (r > tMaxR) tMaxR = r;
                    if (u < tMinU) tMinU = u;
                    if (u > tMaxU) tMaxU = u;
                }
            }
            if (tMinR < tMaxR) { minR = tMinR; maxR = tMaxR; }
            if (tMinU < tMaxU) { minU = tMinU; maxU = tMaxU; }
        }

        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        for (int j = 0; j <= 10; j++)
        {
            float s    = (float)j / 10f * total;
            float cant = target.GetCantAtS(s);
            var   sp   = target.EvaluateAtArcLength(s, cant);

            Vector3 p0 = sp.position + sp.binormal * minR + sp.normal * minU; // 底・左
            Vector3 p1 = sp.position + sp.binormal * maxR + sp.normal * minU; // 底・右
            Vector3 p2 = sp.position + sp.binormal * maxR + sp.normal * maxU; // 上・右
            Vector3 p3 = sp.position + sp.binormal * minR + sp.normal * maxU; // 上・左

            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p0);
        }
    }
}
