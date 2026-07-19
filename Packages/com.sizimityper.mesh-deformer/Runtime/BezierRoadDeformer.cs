using System;
using System.Collections.Generic;
using UnityEngine;

namespace SizimityperMeshDeformer
{
    public enum AxisDirection { X, Y, Z }
    public enum CurveMode    { Curve, Interpolation, Straight }
    public enum DeformMode   { Stretch, Cut }
    public enum TangentAxis  { PosZ, NegZ, PosX, NegX, PosY, NegY }
    public enum InterpTangentScaleMode { Manual, AngleBased, RadiusBased }

    /// <summary>設計速度の決定方式</summary>
    public enum DesignSpeedMode
    {
        /// <summary>曲線半径Rから線形補間で算出</summary>
        AutoFromR,
        /// <summary>Rから線形補間後、最近傍の規制速度にスナップ</summary>
        AutoSnap,
        /// <summary>規制速度 {20,30,40,50,60,80,100,120} km/h から選択</summary>
        Regulated,
        /// <summary>手動入力</summary>
        Manual,
    }

    /// <summary>規制速度テーブル（国内高速・一般道共通）</summary>
    public static class RegulatedSpeeds
    {
        public static readonly float[] Values = { 20f, 30f, 40f, 50f, 60f, 80f, 100f, 120f };

        /// <summary>速度値から最近傍の規制速度インデックスを返す</summary>
        public static int NearestIndex(float speedKmh)
        {
            int best = 0;
            float bestDist = Mathf.Abs(Values[0] - speedKmh);
            for (int i = 1; i < Values.Length; i++)
            {
                float d = Mathf.Abs(Values[i] - speedKmh);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }

    /// <summary>メッシュごとのカント追従方式</summary>
    public enum CantFollowMode
    {
        /// <summary>路面と一体でカントに追従して傾く（路面・床版など）</summary>
        Full,
        /// <summary>鉛直を保ち、接地補正でカント路面に接地させる（防音壁・高欄・ガードレールなど）</summary>
        Vertical,
        /// <summary>上面のみカント路面に追従し、下面は水平を保つ（橋桁など全幅の構造。実物の変断面・支承高変化に相当）</summary>
        TopOnly,
    }

    [Serializable]
    public class SourceMeshEntry
    {
        public Mesh       mesh;
        public Material[] materials;
        public string     meshName;
        /// <summary>カント追従方式。Full=路面と一体で傾く / Vertical=鉛直を保ち接地 / TopOnly=上面のみ追従（橋桁向け）</summary>
        public CantFollowMode cantFollowMode = CantFollowMode.Full;
        [HideInInspector] public bool followCant = true; // 旧フィールド（後方互換・非表示）
        /// <summary>旧データ（followCant=false）を Vertical として扱う実効モード</summary>
        public CantFollowMode EffectiveCantMode
            => (!followCant && cantFollowMode == CantFollowMode.Full) ? CantFollowMode.Vertical : cantFollowMode;
        [HideInInspector] public GameObject outputObject;
    }

    [Serializable]
    public class PrefabPlacementRule
    {
        public GameObject prefab;
        public Vector3 positionOffset  = Vector3.zero;
        public Vector3 rotationOffset  = Vector3.zero;
        public bool    followCant      = true;
    }

    [ExecuteInEditMode]
    [AddComponentMenu("Sizimityper/Bezier Road Deformer")]
    public class BezierRoadDeformer : MonoBehaviour
    {
        // ============================================================
        // Base
        // ============================================================
        public GameObject   sourceParentObject;
        public AxisDirection axisDirection   = AxisDirection.Z;
        public float         tileAxisPadding = 0.001f;

        [HideInInspector]
        public List<SourceMeshEntry> sourceMeshEntries = new List<SourceMeshEntry>();

        // ============================================================
        // Curve Mode
        // ============================================================
        public CurveMode curveMode = CurveMode.Curve;

        // --- Curve Mode ---
        public float paramR              = 300f;
        public float paramAngle          = 90f;
        public bool  paramTurnRight      = true;
        public float paramCantAngle      = 0f;
        public float paramGrade               = 0f;   // shared with Straight
        public bool  paramGradeVerticalCurve = false; // true: sinusoidal grade (0→peak→0)
        public bool  paramUseEasement    = true;
        public float paramEasementLength = 50f;
        public bool  paramCurveAutoGrade = false; // true: grade derived from height
        public float paramCurveHeight    = 0f;    // target height (used when paramCurveAutoGrade)

        // Curve: auto-calculate from design speed
        public DesignSpeedMode designSpeedMode          = DesignSpeedMode.AutoFromR;
        public int             paramRegulatedSpeedIndex = 4; // 60 km/h
        [HideInInspector] public bool paramAutoCalcDesignSpeed = true; // 旧フィールド（後方互換・非表示）
        public float paramDesignSpeed = 60f;
        public bool  paramAutoCalcFriction    = true;
        public float paramFrictionCoeff       = 0.13f;
        public bool  paramAutoApplyCant       = true;
        public bool  paramAutoCalcEasement    = true;
        public bool  invertCant              = false;
        public bool  invertNormals           = false; // すべての面の法線と巻き順を反転する
        public bool  paramIgnoreCantLimit    = false;

        // --- Interpolation Mode ---
        public Transform   interpStartObject;
        public Transform   interpEndObject;
        public TangentAxis interpStartTangentAxis    = TangentAxis.PosZ;
        public TangentAxis interpEndTangentAxis      = TangentAxis.PosZ;
        public bool  paramInterpAutoCalcCant    = false;
        public float interpMidCantAngle        = 0f;   // 中間点(t=0.5)のカント角(°)・手入力
        public InterpTangentScaleMode interpTangentScaleMode = InterpTangentScaleMode.Manual;
        public float interpTangentScale        = 1f;   // タンジェントスケール（共通・手動）
        public bool  interpTangentScaleIndividual = false;
        public float interpStartTangentScale   = 1f;   // タンジェントスケール（始点個別・手動）
        public float interpEndTangentScale     = 1f;   // タンジェントスケール（終点個別・手動）
        [HideInInspector] public float interpComputedTangentScale = 1f; // 自動算出時の算出値(表示用)
        [HideInInspector] public float interpMidCantComputed     = 0f;  // 表示用
        [HideInInspector] public float interpComputedR           = 0f;  // 表示用
        [HideInInspector] public float interpComputedDesignSpeed = 0f;  // 表示用
        [HideInInspector] public float interpComputedFriction    = 0f;  // 表示用
        [HideInInspector] public Vector3 interpStartTangent = Vector3.forward; // legacy
        [HideInInspector] public Vector3 interpEndTangent   = Vector3.forward; // legacy

        // --- Straight Mode ---
        public float paramStraightLength    = 100f;  // uses paramGrade
        public bool  paramStraightAutoGrade = false; // true: grade derived from height
        public float paramStraightHeight    = 0f;    // target height (used when paramStraightAutoGrade)

        // ============================================================
        // Deform Mode
        // ============================================================
        public DeformMode deformMode    = DeformMode.Cut;

        // ============================================================
        // Prefab Placement
        // ============================================================
        public List<PrefabPlacementRule> placementRules = new List<PrefabPlacementRule>();

        // ============================================================
        // Internal
        // ============================================================
        [HideInInspector] public List<GameObject> spawnedPrefabs = new List<GameObject>();

        private const int LUT_RESOLUTION = 200;
        [HideInInspector] public float[]       arcLengthLUT;
        [HideInInspector] public float         totalArcLength;
        [HideInInspector] public List<Vector3> paramPoints;
        [HideInInspector] public List<Vector3> paramTangents;
        [HideInInspector] public float[]       interpCantLUT;   // per-sample cant (Interpolation auto-cant)
        [HideInInspector] public bool          paramPointsBuilt = false;

        // ============================================================
        // Initialize
        // ============================================================

        public void Initialize()
        {
            CollectSourceMeshes();
            arcLengthLUT = null;
        }

        public void CollectSourceMeshes()
        {
            // 再収集前に、同名エントリのカント追従設定を退避しておく（ユーザーが設定した値を保持するため）
            var prevModes = new Dictionary<string, CantFollowMode>();
            if (sourceMeshEntries != null)
                foreach (var e in sourceMeshEntries)
                {
                    if (e.outputObject != null) DestroyImmediate(e.outputObject);
                    if (!string.IsNullOrEmpty(e.meshName) && !prevModes.ContainsKey(e.meshName))
                        prevModes[e.meshName] = e.EffectiveCantMode;
                }

            sourceMeshEntries = new List<SourceMeshEntry>();
            if (sourceParentObject == null) return;

            foreach (var mf in sourceParentObject.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;

                Matrix4x4 childToParent = sourceParentObject.transform.worldToLocalMatrix
                                        * mf.transform.localToWorldMatrix;
                Matrix4x4 normalMatrix  = childToParent.inverse.transpose;

                var srcVerts    = mf.sharedMesh.vertices;
                var srcNormsRaw = mf.sharedMesh.normals;
                bool hasNormals = srcNormsRaw != null && srcNormsRaw.Length == srcVerts.Length;

                var newVerts = new Vector3[srcVerts.Length];
                var newNorms = new Vector3[srcVerts.Length];
                for (int i = 0; i < srcVerts.Length; i++)
                {
                    newVerts[i] = childToParent.MultiplyPoint3x4(srcVerts[i]);
                    newNorms[i] = hasNormals
                        ? normalMatrix.MultiplyVector(srcNormsRaw[i]).normalized
                        : Vector3.up;
                }

                var newMesh = new Mesh
                {
                    name        = mf.sharedMesh.name,
                    indexFormat = mf.sharedMesh.indexFormat
                };
                newMesh.vertices = newVerts;
                newMesh.normals  = newNorms;
                var uvs = mf.sharedMesh.uv;
                if (uvs != null && uvs.Length > 0) newMesh.uv = uvs;
                var uv2 = mf.sharedMesh.uv2;
                if (uv2 != null && uv2.Length > 0) newMesh.uv2 = uv2;
                newMesh.subMeshCount = mf.sharedMesh.subMeshCount;
                for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    newMesh.SetTriangles(mf.sharedMesh.GetTriangles(sub), sub);
                newMesh.RecalculateBounds();

                Material[] mats = null;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr != null) mats = mr.sharedMaterials;

                CantFollowMode keepMode = prevModes.TryGetValue(mf.sharedMesh.name, out var m) ? m : CantFollowMode.Full;
                sourceMeshEntries.Add(new SourceMeshEntry
                {
                    mesh           = newMesh,
                    materials      = mats,
                    meshName       = mf.sharedMesh.name,
                    cantFollowMode = keepMode
                });
            }
        }

        // ============================================================
        // Auto-Calculate (道路構造令)
        // ============================================================

        /// <summary>道路構造令：最小曲線半径から設計速度を線形補間で算出</summary>
        public float CalcDesignSpeedFromR(float R)
        {
            float[] rTable = {  15f,  30f,  60f, 100f, 150f, 280f, 460f, 710f };
            float[] vTable = {  20f,  30f,  40f,  50f,  60f,  80f, 100f, 120f };

            if (R <= rTable[0]) return vTable[0];
            if (R >= rTable[rTable.Length - 1]) return vTable[vTable.Length - 1];

            for (int i = 0; i < rTable.Length - 1; i++)
            {
                if (R < rTable[i + 1])
                {
                    float t = (R - rTable[i]) / (rTable[i + 1] - rTable[i]);
                    return Mathf.Lerp(vTable[i], vTable[i + 1], t);
                }
            }
            return vTable[vTable.Length - 1];
        }

        /// <summary>道路構造令による横すべり摩擦係数（線形補間）</summary>
        public float CalcFrictionFromSpeed(float speedKmh)
        {
            float[] vTable = {  40f,   50f,   60f,   80f,  100f,  120f };
            float[] fTable = { 0.15f, 0.14f, 0.13f, 0.12f, 0.11f, 0.10f };

            if (speedKmh <= vTable[0]) return fTable[0];
            if (speedKmh >= vTable[vTable.Length - 1]) return fTable[fTable.Length - 1];

            for (int i = 0; i < vTable.Length - 1; i++)
            {
                if (speedKmh < vTable[i + 1])
                {
                    float t = (speedKmh - vTable[i]) / (vTable[i + 1] - vTable[i]);
                    return Mathf.Lerp(fTable[i], fTable[i + 1], t);
                }
            }
            return fTable[fTable.Length - 1];
        }

        /// <summary>道路構造令による緩和区間最小長（線形補間）</summary>
        public float CalcEasementLengthFromSpeed(float speedKmh)
        {
            float[] vTable = {  20f,  30f,  40f,  50f,  60f,  80f, 100f, 120f };
            float[] lTable = {  20f,  25f,  35f,  40f,  50f,  70f,  85f, 100f };

            if (speedKmh <= vTable[0]) return lTable[0];
            if (speedKmh >= vTable[vTable.Length - 1]) return lTable[lTable.Length - 1];

            for (int i = 0; i < vTable.Length - 1; i++)
            {
                if (speedKmh < vTable[i + 1])
                {
                    float t = (speedKmh - vTable[i]) / (vTable[i + 1] - vTable[i]);
                    return Mathf.Lerp(lTable[i], lTable[i + 1], t);
                }
            }
            return lTable[lTable.Length - 1];
        }

        /// <summary>設計速度・R からカント角を計算 (i = V²/127R - f)</summary>
        public float CalcCantAngle()
        {
            float f = paramAutoCalcFriction
                ? CalcFrictionFromSpeed(paramDesignSpeed)
                : paramFrictionCoeff;
            float R = Mathf.Max(paramR, 0.1f);
            float i = (paramDesignSpeed * paramDesignSpeed) / (127f * R) - f;
            i = paramIgnoreCantLimit ? Mathf.Max(i, 0f) : Mathf.Clamp(i, 0f, 0.10f);
            return Mathf.Atan(i) * Mathf.Rad2Deg;
        }

        // ============================================================
        // Spline Structures
        // ============================================================

        public struct SplinePoint
        {
            public Vector3 position;
            public Vector3 tangent;
            public Vector3 normal;
            public Vector3 binormal;
            public float   cant;
        }

        // ============================================================
        // Arc-Length LUT Construction
        // ============================================================

        public void BuildArcLengthLUT()
        {
            switch (curveMode)
            {
                case CurveMode.Curve:         GenerateParameterCurve(LUT_RESOLUTION);      break;
                case CurveMode.Interpolation: GenerateInterpolationCurve(LUT_RESOLUTION);  break;
                case CurveMode.Straight:      GenerateStraightCurve(LUT_RESOLUTION);       break;
            }

            if (paramPoints == null || paramPoints.Count < 2)
            {
                totalArcLength = 0f;
                arcLengthLUT   = new float[0];
                return;
            }

            arcLengthLUT    = new float[paramPoints.Count];
            arcLengthLUT[0] = 0f;
            float cum = 0f;
            for (int i = 1; i < paramPoints.Count; i++)
            {
                cum += Vector3.Distance(paramPoints[i - 1], paramPoints[i]);
                arcLengthLUT[i] = cum;
            }
            totalArcLength = cum;
        }

        // ============================================================
        // Curve Mode: Clothoid + Circular Arc
        // ============================================================

        public void GenerateParameterCurve(int resolution = 200)
        {
            float R             = Mathf.Max(paramR, 0.1f);
            float totalAngleRad = paramAngle * Mathf.Deg2Rad;
            float easLen        = paramUseEasement ? Mathf.Max(paramEasementLength, 0f) : 0f;
            float sign          = paramTurnRight ? 1f : -1f;

            // Each easement section turns easLen/(2R)  (integral of linear curvature ramp)
            float easAngle    = easLen > 0f ? easLen / (2f * R) : 0f;
            float arcAngleRad = Mathf.Max(0f, totalAngleRad - 2f * easAngle);
            float arcLen      = R * arcAngleRad;
            float totalLen    = 2f * easLen + arcLen;
            if (totalLen <= 0f) totalLen = 1f;

            int   steps      = Mathf.Max(resolution, 10);
            float ds         = totalLen / steps;
            float gradeSlope = paramGrade / 100f;

            paramPoints   = new List<Vector3>(steps + 1);
            paramTangents = new List<Vector3>(steps + 1);
            paramPoints.Add(Vector3.zero);
            paramTangents.Add(new Vector3(0f, paramGradeVerticalCurve ? 0f : gradeSlope, 1f).normalized);

            float heading = 0f, px = 0f, py = 0f, pz = 0f;
            for (int i = 1; i <= steps; i++)
            {
                float sMid = ((i - 0.5f) / steps) * totalLen;
                float gs   = paramGradeVerticalCurve
                    ? gradeSlope * Mathf.Sin(Mathf.PI * sMid / totalLen)
                    : gradeSlope;
                float curv = sign * GetCurvatureAtS(sMid, R, easLen, arcLen);
                heading += curv * ds;
                px      += Mathf.Sin(heading) * ds;
                pz      += Mathf.Cos(heading) * ds;
                py      += gs * ds;
                paramPoints.Add(new Vector3(px, py, pz));
                paramTangents.Add(new Vector3(Mathf.Sin(heading), gs, Mathf.Cos(heading)).normalized);
            }
            paramPointsBuilt = true;
        }

        private float GetCurvatureAtS(float s, float R, float easLen, float arcLen)
        {
            if (!paramUseEasement || easLen <= 0f) return 1f / R;
            float A2       = R * easLen;
            float totalLen = 2f * easLen + arcLen;
            if (s < easLen)              return s / A2;
            if (s < easLen + arcLen)     return 1f / R;
            return Mathf.Max(0f, totalLen - s) / A2;
        }

        public float GetCantAtS(float s)
        {
            if (curveMode == CurveMode.Straight) return 0f;

            if (curveMode == CurveMode.Interpolation)
            {
                // LUT is always built in GenerateInterpolationCurve
                if (interpCantLUT != null && interpCantLUT.Length > 1
                    && arcLengthLUT != null && arcLengthLUT.Length == interpCantLUT.Length)
                {
                    s = Mathf.Clamp(s, 0f, totalArcLength);
                    int lo = 0, hi = arcLengthLUT.Length - 1;
                    while (lo < hi - 1)
                    {
                        int mid = (lo + hi) / 2;
                        if (arcLengthLUT[mid] <= s) lo = mid; else hi = mid;
                    }
                    float segLen = arcLengthLUT[hi] - arcLengthLUT[lo];
                    float alpha  = segLen > 1e-6f ? (s - arcLengthLUT[lo]) / segLen : 0f;
                    return Mathf.Lerp(interpCantLUT[lo], interpCantLUT[hi], alpha);
                }
                return 0f;
            }

            // CurveMode.Curve
            float easLenC  = paramUseEasement ? paramEasementLength : 0f;
            float R        = Mathf.Max(paramR, 0.1f);
            float easAngle = easLenC > 0f ? easLenC / (2f * R) : 0f;
            float arcLenC  = Mathf.Max(0f, R * (paramAngle * Mathf.Deg2Rad - 2f * easAngle));
            float totalLen = 2f * easLenC + arcLenC;
            float cantSign = paramTurnRight ? 1f : -1f;

            if (!paramUseEasement || easLenC <= 0f) return cantSign * paramCantAngle;
            if (s < easLenC)              return cantSign * Mathf.Lerp(0f, paramCantAngle, s / easLenC);
            if (s < easLenC + arcLenC)    return cantSign * paramCantAngle;
            return cantSign * Mathf.Lerp(0f, paramCantAngle, Mathf.Clamp01((totalLen - s) / easLenC));
        }

        // ============================================================
        // Interpolation Mode: Hermite Spline
        // ============================================================

        public Vector3 GetTangentDirection(Transform t, TangentAxis axis)
        {
            switch (axis)
            {
                case TangentAxis.NegZ: return -t.forward;
                case TangentAxis.PosX: return  t.right;
                case TangentAxis.NegX: return -t.right;
                case TangentAxis.PosY: return  t.up;
                case TangentAxis.NegY: return -t.up;
                default:               return  t.forward; // PosZ
            }
        }

        /// <summary>
        /// オブジェクトのロール（接線軸まわりの傾き）をカント角(°)として返す。
        /// 接線軸に垂直な平面上で、ワールド上方向とオブジェクト上方向の符号付き角度を計算する。
        /// </summary>
        public float GetCantFromObjectRotation(Transform t, TangentAxis tangentAxis)
        {
            Vector3 tangent     = GetTangentDirection(t, tangentAxis);
            Vector3 worldUpPerp = Vector3.ProjectOnPlane(Vector3.up, tangent);
            Vector3 objUpPerp   = Vector3.ProjectOnPlane(t.up, tangent);
            if (worldUpPerp.sqrMagnitude < 1e-6f || objUpPerp.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(worldUpPerp.normalized, objUpPerp.normalized, tangent);
        }

        private void GenerateInterpolationCurve(int resolution = 200)
        {
            if (interpStartObject == null || interpEndObject == null) return;

            Vector3 p0        = interpStartObject.position;
            Vector3 p1        = interpEndObject.position;
            float   handleLen = Vector3.Distance(p0, p1);
            Vector3 dir0      = GetTangentDirection(interpStartObject, interpStartTangentAxis);
            Vector3 dir1      = GetTangentDirection(interpEndObject,   interpEndTangentAxis);

            // 自動カントモード時はタンジェントスケールもAngleBasedに固定
            var   effectiveScaleMode = paramInterpAutoCalcCant ? InterpTangentScaleMode.AngleBased : interpTangentScaleMode;
            float autoScale = 1f;
            switch (effectiveScaleMode)
            {
                case InterpTangentScaleMode.AngleBased:
                {
                    // scale = 1 + sin(θ/2)³  → [1.0, 2.0] に自然に収まる
                    float theta   = Vector3.Angle(dir0, dir1) * Mathf.Deg2Rad;
                    float sinHalf = Mathf.Sin(theta / 2f);
                    autoScale = 1f + sinHalf * sinHalf * sinHalf;
                    break;
                }
                case InterpTangentScaleMode.RadiusBased:
                {
                    // scale=1でR算出→スケールをR/handleLenに設定
                    Vector3 rt0   = dir0 * handleLen;
                    Vector3 rt1   = dir1 * handleLen;
                    const float h = 0.5f;
                    Vector3 rTan  = (6f*h*h-6f*h)*p0 + (3f*h*h-4f*h+1f)*rt0
                                  + (-6f*h*h+6f*h)*p1 + (3f*h*h-2f*h)*rt1;
                    Vector3 rTan2 = (12f*h-6f)*p0 + (6f*h-4f)*rt0
                                  + (-12f*h+6f)*p1 + (6f*h-2f)*rt1;
                    Vector3 cross = Vector3.Cross(rTan, rTan2);
                    float   dMag  = rTan.magnitude;
                    float   kappa = dMag > 1e-6f ? cross.magnitude / (dMag * dMag * dMag) : 0f;
                    float   R     = kappa > 1e-6f ? 1f / kappa : handleLen;
                    autoScale = Mathf.Clamp(R / Mathf.Max(handleLen, 0.01f), 0.1f, 5f);
                    break;
                }
            }
            if (effectiveScaleMode != InterpTangentScaleMode.Manual)
                interpComputedTangentScale = autoScale;

            float   scaleStart = effectiveScaleMode != InterpTangentScaleMode.Manual ? autoScale
                               : interpTangentScaleIndividual ? interpStartTangentScale : interpTangentScale;
            float   scaleEnd   = effectiveScaleMode != InterpTangentScaleMode.Manual ? autoScale
                               : interpTangentScaleIndividual ? interpEndTangentScale   : interpTangentScale;
            Vector3 t0         = dir0 * handleLen * scaleStart;
            Vector3 t1         = dir1 * handleLen * scaleEnd;

            int steps     = Mathf.Max(resolution, 10);
            paramPoints   = new List<Vector3>(steps + 1);
            paramTangents = new List<Vector3>(steps + 1);

            // 始点・終点のカント角は常にオブジェクトのRotationから取得
            float cantStart = GetCantFromObjectRotation(interpStartObject, interpStartTangentAxis);
            float cantEnd   = GetCantFromObjectRotation(interpEndObject,   interpEndTangentAxis);
            interpCantLUT   = new float[steps + 1];

            // 中間(t=0.5)のカント角: 自動算出 or 手入力
            float midCant;
            if (paramInterpAutoCalcCant)
            {
                // t=0.5の曲率からカント角を算出
                const float half = 0.5f;
                Vector3 mTan  = (6f*half*half - 6f*half)*p0 + (3f*half*half - 4f*half + 1f)*t0
                              + (-6f*half*half + 6f*half)*p1 + (3f*half*half - 2f*half)*t1;
                Vector3 mTan2 = (12f*half - 6f)*p0 + (6f*half - 4f)*t0
                              + (-12f*half + 6f)*p1 + (6f*half - 2f)*t1;
                Vector3 cross   = Vector3.Cross(mTan, mTan2);
                float   dMag    = mTan.magnitude;
                float   kappa   = dMag > 1e-6f ? cross.magnitude / (dMag * dMag * dMag) : 0f;
                float   R       = kappa > 1e-6f ? 1f / kappa : float.MaxValue;
                float   V       = CalcDesignSpeedFromR(R);
                float   fCoeff  = CalcFrictionFromSpeed(V);
                float   rawI    = V * V / (127f * R) - fCoeff;
                float   supelev = paramIgnoreCantLimit ? Mathf.Max(rawI, 0f) : Mathf.Clamp(rawI, 0f, 0.10f);
                float   cantSgn = cross.y < 0f ? 1f : -1f;
                midCant = cantSgn * Mathf.Atan(supelev) * Mathf.Rad2Deg;
                interpMidCantComputed     = midCant;
                interpComputedR           = R < float.MaxValue ? R : 0f;
                interpComputedDesignSpeed = V;
                interpComputedFriction    = fCoeff;
            }
            else
            {
                midCant = interpMidCantAngle;
                interpMidCantComputed = 0f;
            }

            // 始点・中間・終点を通る2次補間でLUTを構築
            // 制御点 B = (4*mid - start - end) / 2 でt=0.5が正確にmidを通る
            float bezB = (4f * midCant - cantStart - cantEnd) / 2f;
            for (int i = 0; i <= steps; i++)
            {
                float param  = (float)i / steps;
                float param2 = param * param;
                float param3 = param2 * param;

                Vector3 wPos = (2f * param3 - 3f * param2 + 1f) * p0 + (param3 - 2f * param2 + param) * t0
                             + (-2f * param3 + 3f * param2) * p1    + (param3 - param2) * t1;
                Vector3 wTan = (6f * param2 - 6f * param) * p0 + (3f * param2 - 4f * param + 1f) * t0
                             + (-6f * param2 + 6f * param) * p1 + (3f * param2 - 2f * param) * t1;

                paramPoints.Add(transform.InverseTransformPoint(wPos));
                paramTangents.Add(wTan.sqrMagnitude > 1e-8f
                    ? transform.InverseTransformDirection(wTan.normalized)
                    : Vector3.forward);

                float q = 1f - param;
                interpCantLUT[i] = cantStart * q * q + 2f * bezB * param * q + cantEnd * param * param;
            }

            paramPointsBuilt = true;
        }

        // ============================================================
        // Straight Mode
        // ============================================================

        private void GenerateStraightCurve(int resolution = 200)
        {
            float len        = Mathf.Max(paramStraightLength, 0.01f);
            float gradeSlope = paramGrade / 100f;

            int steps     = Mathf.Max(resolution, 10);
            paramPoints   = new List<Vector3>(steps + 1);
            paramTangents = new List<Vector3>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float s = (float)i / steps * len;
                float y, gs;
                if (paramGradeVerticalCurve)
                {
                    // sinusoidal: g(s) = gradeSlope * sin(π*s/len), y(s) = gradeSlope*len/π*(1-cos(π*s/len))
                    y  = gradeSlope * len / Mathf.PI * (1f - Mathf.Cos(Mathf.PI * s / len));
                    gs = gradeSlope * Mathf.Sin(Mathf.PI * s / len);
                }
                else
                {
                    y  = gradeSlope * s;
                    gs = gradeSlope;
                }
                paramPoints.Add(new Vector3(0f, y, s));
                paramTangents.Add(new Vector3(0f, gs, 1f).normalized);
            }
            paramPointsBuilt = true;
        }

        // ============================================================
        // Spline Evaluation
        // ============================================================

        public SplinePoint EvaluateAtArcLength(float s, float cantDeg = 0f)
        {
            if (arcLengthLUT == null || arcLengthLUT.Length == 0) BuildArcLengthLUT();
            s = Mathf.Clamp(s, 0f, totalArcLength);

            int lo = 0, hi = arcLengthLUT.Length - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (arcLengthLUT[mid] <= s) lo = mid; else hi = mid;
            }
            float segLen = arcLengthLUT[hi] - arcLengthLUT[lo];
            float alpha  = segLen > 1e-6f ? (s - arcLengthLUT[lo]) / segLen : 0f;

            Vector3 lPos = Vector3.Lerp(paramPoints[lo], paramPoints[hi], alpha);
            int hiC = Mathf.Min(hi, paramTangents.Count - 1);
            Vector3 lTan = paramTangents != null && paramTangents.Count > lo
                ? Vector3.Lerp(paramTangents[lo], paramTangents[hiC], alpha)
                : (paramPoints[hi] - paramPoints[lo]);

            Vector3 pos = transform.TransformPoint(lPos);
            Vector3 tan = transform.TransformDirection(lTan);
            if (tan.sqrMagnitude < 1e-8f) tan = transform.forward;
            tan.Normalize();

            Quaternion cantRot = Quaternion.AngleAxis(invertCant ? -cantDeg : cantDeg, tan);
            Vector3 up    = cantRot * Vector3.up;
            Vector3 right = Vector3.Cross(tan, up).normalized;
            up = Vector3.Cross(right, tan).normalized;

            return new SplinePoint { position = pos, tangent = tan, normal = up, binormal = right, cant = cantDeg };
        }

        public float GetTotalArcLength()
        {
            if (arcLengthLUT == null) BuildArcLengthLUT();
            return totalArcLength;
        }

        // ============================================================
        // Deformation Utilities
        // ============================================================

        private float GetAxisValue(Vector3 v)
        {
            switch (axisDirection)
            {
                case AxisDirection.X: return v.x;
                case AxisDirection.Y: return v.y;
                default:              return v.z;
            }
        }

        private (float right, float up) GetLateralOffsets(Vector3 v)
        {
            switch (axisDirection)
            {
                case AxisDirection.X: return (v.z, v.y);
                case AxisDirection.Y: return (v.x, v.z);
                default:              return (v.x, v.y);
            }
        }

        private Vector3 TransformNormal(Vector3 srcNorm, SplinePoint sp)
        {
            Vector3 worldNorm;
            switch (axisDirection)
            {
                case AxisDirection.X:
                    worldNorm = srcNorm.x * sp.tangent + srcNorm.y * sp.normal + srcNorm.z * sp.binormal; break;
                case AxisDirection.Y:
                    worldNorm = srcNorm.y * sp.tangent + srcNorm.z * sp.normal + srcNorm.x * sp.binormal; break;
                default:
                    worldNorm = srcNorm.z * sp.tangent + srcNorm.y * sp.normal + srcNorm.x * sp.binormal; break;
            }
            var n = transform.InverseTransformDirection(worldNorm).normalized;
            return invertNormals ? -n : n;
        }

        // ============================================================
        // Mesh Deformation
        // ============================================================

        // 全ソースメッシュの軸方向バウンズを返す。メッシュが無い場合は false を返す
        public bool GetSourceMeshAxisBounds(out float sharedMin, out float sharedMax)
        {
            sharedMin = float.MaxValue;
            sharedMax = float.MinValue;
            if (sourceMeshEntries == null) return false;
            foreach (var entry in sourceMeshEntries)
            {
                if (entry.mesh == null) continue;
                foreach (var v in entry.mesh.vertices)
                {
                    float a = GetAxisValue(v);
                    if (a < sharedMin) sharedMin = a;
                    if (a > sharedMax) sharedMax = a;
                }
            }
            return sharedMax > sharedMin;
        }

        public List<Mesh> DeformAllMeshes()
        {
            var result = new List<Mesh>();
            if (sourceMeshEntries == null || sourceMeshEntries.Count == 0) return result;

            arcLengthLUT = null;
            BuildArcLengthLUT();
            if (totalArcLength <= 0f) return result;

            // Pre-pass: shared axis bounds across ALL meshes so every mesh uses identical tile length
            if (!GetSourceMeshAxisBounds(out float sharedMin, out float sharedMax)) return result;

            // Vertical モードのメッシュ用の共有接地アンカー（左右サイドごと）を計算
            float[] groundAnchors = ComputeGroundAnchors();

            // TopOnly モードの高さ正規化基準は全 TopOnly メッシュ共有のバウンズを使う。
            // メッシュごとに正規化すると、桁に付随する小部品（フック等）が自分の小さな高さ範囲で
            // w=0〜1 を振られてしまい、桁本体と補正量が食い違って浮く（2026-07-20確認）。
            float topMinU = 0f, topRange = 0f;
            {
                float mn = float.MaxValue, mx = float.MinValue;
                foreach (var e in sourceMeshEntries)
                {
                    if (e.mesh == null || e.EffectiveCantMode != CantFollowMode.TopOnly) continue;
                    GetHeightBounds(e.mesh.vertices, out float a, out float b);
                    if (a < mn) mn = a;
                    if (b > mx) mx = b;
                }
                if (mx > mn) { topMinU = mn; topRange = Mathf.Max(mx - mn, 1e-6f); }
            }

            for (int i = 0; i < sourceMeshEntries.Count; i++)
            {
                var entry = sourceMeshEntries[i];
                if (entry.mesh == null) { result.Add(null); continue; }
                CantFollowMode mode = entry.EffectiveCantMode;
                float anchorOff = mode == CantFollowMode.Vertical ? groundAnchors[i] : 0f;
                result.Add(deformMode == DeformMode.Stretch
                    ? DeformStretch(entry.mesh, sharedMin, sharedMax, mode, anchorOff, topMinU, topRange)
                    : DeformCut(entry.mesh, sharedMin, sharedMax, mode, anchorOff, topMinU, topRange));
            }
            return result;
        }

        /// <summary>メッシュの横方向バウンズを返す</summary>
        private bool GetLateralBounds(Mesh srcMesh, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            foreach (var v in srcMesh.vertices)
            {
                var (r, _) = GetLateralOffsets(v);
                if (r < min) min = r;
                if (r > max) max = r;
            }
            return max > min;
        }

        /// <summary>
        /// Vertical モードのメッシュ全体で共有する左右サイドごとの接地基準線を計算し、
        /// 各エントリに適用すべきアンカー横オフセットを返す。
        /// - 片側に収まるメッシュ（壁・パネル・高欄など）: 同じ側の全メッシュの横中心の平均を共通基準にする
        ///   → 積層された別メッシュ同士が同一の補正量で動き、ツライチのまま接地する
        /// - 道路全幅にまたがるメッシュ: 剛体のまま左右同時に接地できないため補正なし（0）。
        ///   橋桁など全幅の構造には TopOnly モードを使うこと。
        /// </summary>
        private float[] ComputeGroundAnchors()
        {
            int n = sourceMeshEntries.Count;
            var anchors = new float[n];          // エントリごとの適用アンカー（Vertical 以外や全幅メッシュは 0）
            var centers = new float[n];          // 各メッシュの横中心
            var side    = new int[n];            // -1=左, +1=右, 0=対象外（全幅 or Vertical以外）
            const float SPAN_EPS = 0.1f;         // 中心をまたぐ判定の許容量 [m]

            float sumL = 0f, sumR = 0f;
            int   cntL = 0,  cntR = 0;
            for (int i = 0; i < n; i++)
            {
                var entry = sourceMeshEntries[i];
                if (entry.mesh == null || entry.EffectiveCantMode != CantFollowMode.Vertical) continue;
                if (!GetLateralBounds(entry.mesh, out float bMin, out float bMax)) continue;

                if (bMin < -SPAN_EPS && bMax > SPAN_EPS) continue; // 全幅メッシュ → 補正なし

                centers[i] = (bMin + bMax) * 0.5f;
                side[i]    = centers[i] >= 0f ? 1 : -1;
                if (side[i] > 0) { sumR += centers[i]; cntR++; }
                else             { sumL += centers[i]; cntL++; }
            }

            float anchorL = cntL > 0 ? sumL / cntL : 0f;
            float anchorR = cntR > 0 ? sumR / cntR : 0f;
            for (int i = 0; i < n; i++)
                anchors[i] = side[i] > 0 ? anchorR : side[i] < 0 ? anchorL : 0f;
            return anchors;
        }

        /// <summary>followCant=false 用の接地補正ベクトルを返す。
        /// 接地基準線（anchorOff）における「カント込み路面上の位置」と「鉛直基準面上の位置」の差。
        /// 断面全体をこのベクトルで平行移動することで、鉛直を保ったまま壁底面をカント路面に接地させる。</summary>
        private Vector3 GetGroundShift(float s, SplinePoint spVertical, float anchorOff)
        {
            if (Mathf.Abs(anchorOff) < 1e-6f) return Vector3.zero;
            float cant = GetCantAtS(s);
            if (Mathf.Abs(cant) < 1e-6f) return Vector3.zero;
            SplinePoint spCant = EvaluateAtArcLength(s, cant);
            return (spCant.binormal - spVertical.binormal) * anchorOff;
        }

        /// <summary>TopOnly モード用: メッシュの上下バウンズを計算して高さ割合の基準にする</summary>
        private void GetHeightBounds(Vector3[] srcVerts, out float minU, out float maxU)
        {
            minU = float.MaxValue; maxU = float.MinValue;
            foreach (var v in srcVerts)
            {
                var (_, u) = GetLateralOffsets(v);
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
            }
        }

        private Mesh DeformStretch(Mesh srcMesh, float meshMinA, float meshMaxA, CantFollowMode mode, float anchorOff, float topMinU, float topRange)
        {
            var srcVerts = srcMesh.vertices;
            var srcNorms = srcMesh.normals;
            bool hasNormals = srcNorms != null && srcNorms.Length == srcVerts.Length;
            var  srcUVs     = srcMesh.uv;
            int  subCount   = srcMesh.subMeshCount;
            if (topRange <= 0f) topRange = 1e-6f;

            float meshLen   = Mathf.Max(meshMaxA - meshMinA, 1e-6f);
            int   tileCount = Mathf.Max(1, Mathf.CeilToInt(totalArcLength / meshLen));

            float adjMin   = meshMinA + tileAxisPadding;
            float adjMax   = meshMaxA - tileAxisPadding;
            float adjRange = Mathf.Max(adjMax - adjMin, 1e-6f);

            var combinedVerts   = new List<Vector3>();
            var combinedNorms   = new List<Vector3>();
            var combinedUVs     = new List<Vector2>();
            var subTriLists     = new List<List<int>>();
            for (int sub = 0; sub < subCount; sub++) subTriLists.Add(new List<int>());

            var tileEndVerts   = new List<List<int>>();
            var tileStartVerts = new List<List<int>>();

            for (int tile = 0; tile < tileCount; tile++)
            {
                float tileStartS = tile * meshLen;
                // Last tile: clamp end to spline length → tileLen shrinks (stretch)
                float tileEndS   = Mathf.Min(tileStartS + meshLen, totalArcLength);
                float tileLen    = tileEndS - tileStartS;
                int   baseIdx    = combinedVerts.Count;

                var thisStarts = new List<int>();
                var thisEnds   = new List<int>();

                for (int i = 0; i < srcVerts.Length; i++)
                {
                    float axisVal = GetAxisValue(srcVerts[i]);
                    float localT  = Mathf.Clamp01((axisVal - adjMin) / adjRange);
                    float s       = localT <= 0f ? tileStartS
                                  : localT >= 1f ? tileEndS
                                  : tileStartS + localT * tileLen;

                    // Full 以外のメッシュはカントなし（cant=0）で評価した鉛直フレームを使う。
                    // EvaluateAtArcLength の position はカント角に依存しないため、設置位置はカーブに正しく追従する。
                    // Vertical: 接地補正（groundShift）で断面全体を平行移動し、壁底面をカント込み路面に接地させる。
                    // TopOnly:  高さ割合 w に応じた補正で上面のみカント路面に追従させ、下面は水平を保つ。
                    float cant = mode == CantFollowMode.Full ? GetCantAtS(s) : 0f;
                    SplinePoint sp = EvaluateAtArcLength(s, cant);
                    var (rightOff, upOff) = GetLateralOffsets(srcVerts[i]);
                    Vector3 groundShift = Vector3.zero;
                    if (mode == CantFollowMode.Vertical)
                        groundShift = GetGroundShift(s, sp, anchorOff);
                    else if (mode == CantFollowMode.TopOnly)
                        groundShift = GetGroundShift(s, sp, rightOff * Mathf.Clamp01((upOff - topMinU) / topRange));

                    combinedVerts.Add(transform.InverseTransformPoint(sp.position + groundShift + sp.binormal * rightOff + sp.normal * upOff));
                    combinedNorms.Add(hasNormals ? TransformNormal(srcNorms[i], sp) : Vector3.up);
                    combinedUVs.Add(srcUVs != null && i < srcUVs.Length ? srcUVs[i] : Vector2.zero);

                    if (localT <= 0f) thisStarts.Add(baseIdx + i);
                    if (localT >= 1f) thisEnds.Add(baseIdx + i);
                }

                tileStartVerts.Add(thisStarts);
                tileEndVerts.Add(thisEnds);

                for (int sub = 0; sub < subCount; sub++)
                {
                    var tris = srcMesh.GetTriangles(sub);
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        subTriLists[sub].Add(baseIdx + tris[t]);
                        subTriLists[sub].Add(baseIdx + tris[t + 1]);
                        subTriLists[sub].Add(baseIdx + tris[t + 2]);
                    }
                }
            }

            // Position weld at junctions (1 cm threshold)
            {
                const float SNAP_SQ = 0.01f * 0.01f;
                for (int tile = 0; tile < tileCount - 1; tile++)
                {
                    foreach (int ei in tileEndVerts[tile])
                    {
                        int bestSj = -1; float bestDist = SNAP_SQ;
                        var nextStarts = tileStartVerts[tile + 1];
                        for (int sj = 0; sj < nextStarts.Count; sj++)
                        {
                            float d = (combinedVerts[ei] - combinedVerts[nextStarts[sj]]).sqrMagnitude;
                            if (d < bestDist) { bestDist = d; bestSj = sj; }
                        }
                        if (bestSj < 0) continue;
                        int si = nextStarts[bestSj];
                        Vector3 avgPos  = (combinedVerts[ei] + combinedVerts[si]) * 0.5f;
                        combinedVerts[ei] = avgPos; combinedVerts[si] = avgPos;
                        Vector3 avgNorm = (combinedNorms[ei] + combinedNorms[si]).normalized;
                        combinedNorms[ei] = avgNorm; combinedNorms[si] = avgNorm;
                    }
                }
            }

            var mesh = new Mesh
            {
                name        = srcMesh.name + "_deformed",
                indexFormat = combinedVerts.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(combinedVerts);
            mesh.SetNormals(combinedNorms);
            mesh.SetUVs(0, combinedUVs);
            mesh.subMeshCount = subCount;
            // invertNormals が有効な場合は巻き順を反転してカリングも正しくする
            if (invertNormals)
                foreach (var tris in subTriLists)
                    for (int i = 0; i < tris.Count; i += 3) { int tmp = tris[i+1]; tris[i+1] = tris[i+2]; tris[i+2] = tmp; }
            for (int sub = 0; sub < subCount; sub++) mesh.SetTriangles(subTriLists[sub], sub);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh DeformCut(Mesh srcMesh, float meshMinA, float meshMaxA, CantFollowMode mode, float anchorOff, float topMinU, float topRange)
        {
            var srcVerts = srcMesh.vertices;
            var srcNorms = srcMesh.normals;
            bool hasNormals = srcNorms != null && srcNorms.Length == srcVerts.Length;
            var  srcUVs     = srcMesh.uv;
            int  subCount   = srcMesh.subMeshCount;
            if (topRange <= 0f) topRange = 1e-6f;

            float meshLen = Mathf.Max(meshMaxA - meshMinA, 1e-6f);
            int   tileCount = Mathf.Max(1, Mathf.CeilToInt(totalArcLength / meshLen));

            float adjMin   = meshMinA + tileAxisPadding;
            float adjMax   = meshMaxA - tileAxisPadding;
            float adjRange = Mathf.Max(adjMax - adjMin, 1e-6f);

            var combinedVerts    = new List<Vector3>();
            var combinedNorms    = new List<Vector3>();
            var combinedUVs      = new List<Vector2>();
            var combinedTrimmed  = new List<bool>();
            var combinedSRaw     = new List<float>();
            var combinedRightOff = new List<float>();
            var combinedUpOff    = new List<float>();
            var subTriLists      = new List<List<int>>();
            for (int sub = 0; sub < subCount; sub++) subTriLists.Add(new List<int>());

            var tileEndVerts   = new List<List<int>>();
            var tileStartVerts = new List<List<int>>();

            for (int tile = 0; tile < tileCount; tile++)
            {
                float tileStartS = tile * meshLen;
                float tileEndS   = tileStartS + meshLen;   // no clamping — always full tile
                float tileLen    = meshLen;
                int   baseIdx    = combinedVerts.Count;

                var thisStarts = new List<int>();
                var thisEnds   = new List<int>();

                for (int i = 0; i < srcVerts.Length; i++)
                {
                    float axisVal = GetAxisValue(srcVerts[i]);
                    float localT  = Mathf.Clamp01((axisVal - adjMin) / adjRange);
                    float sRaw    = localT <= 0f ? tileStartS
                                  : localT >= 1f ? tileEndS
                                  : tileStartS + localT * tileLen;
                    bool  trimmed = sRaw > totalArcLength;
                    float s       = Mathf.Clamp(sRaw, 0f, totalArcLength);

                    // Full 以外のメッシュはカントなし（cant=0）で評価した鉛直フレームを使う。
                    // Vertical: 接地補正で断面全体を平行移動。TopOnly: 高さ割合 w に応じて上面のみカント追従。
                    float cant = mode == CantFollowMode.Full ? GetCantAtS(s) : 0f;
                    SplinePoint sp = EvaluateAtArcLength(s, cant);
                    var (rightOff, upOff) = GetLateralOffsets(srcVerts[i]);
                    Vector3 groundShift = Vector3.zero;
                    if (mode == CantFollowMode.Vertical)
                        groundShift = GetGroundShift(s, sp, anchorOff);
                    else if (mode == CantFollowMode.TopOnly)
                        groundShift = GetGroundShift(s, sp, rightOff * Mathf.Clamp01((upOff - topMinU) / topRange));

                    combinedVerts.Add(transform.InverseTransformPoint(sp.position + groundShift + sp.binormal * rightOff + sp.normal * upOff));
                    combinedNorms.Add(hasNormals ? TransformNormal(srcNorms[i], sp) : Vector3.up);
                    combinedUVs.Add(srcUVs != null && i < srcUVs.Length ? srcUVs[i] : Vector2.zero);
                    combinedTrimmed.Add(trimmed);
                    combinedSRaw.Add(sRaw);
                    combinedRightOff.Add(rightOff);
                    combinedUpOff.Add(upOff);

                    if (localT <= 0f) thisStarts.Add(baseIdx + i);
                    if (localT >= 1f) thisEnds.Add(baseIdx + i);
                }

                tileStartVerts.Add(thisStarts);
                tileEndVerts.Add(thisEnds);

                // Precompute boundary SplinePoint once per tile（Full 以外は鉛直フレーム＋接地補正）
                float       boundaryCant  = mode == CantFollowMode.Full ? GetCantAtS(totalArcLength) : 0f;
                SplinePoint boundarySP    = EvaluateAtArcLength(totalArcLength, boundaryCant);
                Vector3     boundaryShift = mode == CantFollowMode.Vertical
                    ? GetGroundShift(totalArcLength, boundarySP, anchorOff)
                    : Vector3.zero; // TopOnly は頂点ごとの高さ割合が必要なためクリップ時に個別計算

                for (int sub = 0; sub < subCount; sub++)
                {
                    var tris = srcMesh.GetTriangles(sub);
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        int ia = baseIdx + tris[t];
                        int ib = baseIdx + tris[t + 1];
                        int ic = baseIdx + tris[t + 2];

                        bool trimA = combinedSRaw[ia] > totalArcLength;
                        bool trimB = combinedSRaw[ib] > totalArcLength;
                        bool trimC = combinedSRaw[ic] > totalArcLength;
                        if (trimA && trimB && trimC) continue;

                        if (!trimA && !trimB && !trimC)
                        {
                            subTriLists[sub].Add(ia);
                            subTriLists[sub].Add(ib);
                            subTriLists[sub].Add(ic);
                            continue;
                        }

                        // Sutherland-Hodgman clip against s <= totalArcLength
                        int[] polyIdx = { ia, ib, ic };
                        var   clipped = new List<int>(5);
                        for (int e = 0; e < 3; e++)
                        {
                            int  curr   = polyIdx[e];
                            int  next   = polyIdx[(e + 1) % 3];
                            bool currIn = combinedSRaw[curr] <= totalArcLength;
                            bool nextIn = combinedSRaw[next] <= totalArcLength;
                            if (currIn) clipped.Add(curr);
                            if (currIn != nextIn)
                            {
                                float sA   = combinedSRaw[curr], sB = combinedSRaw[next];
                                float tt   = (totalArcLength - sA) / (sB - sA);
                                float rOff = Mathf.Lerp(combinedRightOff[curr], combinedRightOff[next], tt);
                                float uOff = Mathf.Lerp(combinedUpOff[curr],    combinedUpOff[next],    tt);
                                Vector3 bShift = boundaryShift;
                                if (mode == CantFollowMode.TopOnly)
                                    bShift = GetGroundShift(totalArcLength, boundarySP, rOff * Mathf.Clamp01((uOff - topMinU) / topRange));
                                Vector3 bWorldPos = boundarySP.position + bShift + boundarySP.binormal * rOff + boundarySP.normal * uOff;
                                combinedVerts.Add(transform.InverseTransformPoint(bWorldPos));
                                combinedNorms.Add(Vector3.Slerp(combinedNorms[curr], combinedNorms[next], tt).normalized);
                                combinedUVs.Add(Vector2.Lerp(combinedUVs[curr], combinedUVs[next], tt));
                                combinedSRaw.Add(totalArcLength);
                                combinedRightOff.Add(rOff);
                                combinedUpOff.Add(uOff);
                                combinedTrimmed.Add(false);
                                clipped.Add(combinedVerts.Count - 1);
                            }
                        }
                        // Fan triangulation
                        for (int v = 1; v < clipped.Count - 1; v++)
                        {
                            subTriLists[sub].Add(clipped[0]);
                            subTriLists[sub].Add(clipped[v]);
                            subTriLists[sub].Add(clipped[v + 1]);
                        }
                    }
                }
            }

            // Position weld at junctions (1 cm threshold)
            {
                const float SNAP_SQ = 0.01f * 0.01f;
                for (int tile = 0; tile < tileCount - 1; tile++)
                {
                    foreach (int ei in tileEndVerts[tile])
                    {
                        int bestSj = -1; float bestDist = SNAP_SQ;
                        var nextStarts = tileStartVerts[tile + 1];
                        for (int sj = 0; sj < nextStarts.Count; sj++)
                        {
                            float d = (combinedVerts[ei] - combinedVerts[nextStarts[sj]]).sqrMagnitude;
                            if (d < bestDist) { bestDist = d; bestSj = sj; }
                        }
                        if (bestSj < 0) continue;
                        int si = nextStarts[bestSj];
                        Vector3 avgPos = (combinedVerts[ei] + combinedVerts[si]) * 0.5f;
                        combinedVerts[ei] = avgPos; combinedVerts[si] = avgPos;
                        Vector3 avgNorm = (combinedNorms[ei] + combinedNorms[si]).normalized;
                        combinedNorms[ei] = avgNorm; combinedNorms[si] = avgNorm;
                    }
                }
            }

            var mesh = new Mesh
            {
                name        = srcMesh.name + "_deformed",
                indexFormat = combinedVerts.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(combinedVerts);
            mesh.SetNormals(combinedNorms);
            mesh.SetUVs(0, combinedUVs);
            mesh.subMeshCount = subCount;
            // invertNormals が有効な場合は巻き順を反転してカリングも正しくする
            if (invertNormals)
                foreach (var tris in subTriLists)
                    for (int i = 0; i < tris.Count; i += 3) { int tmp = tris[i+1]; tris[i+1] = tris[i+2]; tris[i+2] = tmp; }
            for (int sub = 0; sub < subCount; sub++) mesh.SetTriangles(subTriLists[sub], sub);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ============================================================
        // Preview
        // ============================================================

        public void UpdatePreview()
        {
            if (sourceMeshEntries == null || sourceMeshEntries.Count == 0) return;

            var meshes = DeformAllMeshes();
            for (int i = 0; i < sourceMeshEntries.Count; i++)
            {
                var entry = sourceMeshEntries[i];
                var mesh  = i < meshes.Count ? meshes[i] : null;
                if (mesh == null) continue;

                if (entry.outputObject == null)
                {
                    var go = new GameObject(entry.meshName ?? $"Preview_{i}");
                    go.transform.SetParent(transform, false);
                    go.hideFlags = HideFlags.DontSave;
                    entry.outputObject = go;
                }

                var mf = entry.outputObject.GetComponent<MeshFilter>();
                if (mf == null) mf = entry.outputObject.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = entry.outputObject.GetComponent<MeshRenderer>();
                if (mr == null) mr = entry.outputObject.AddComponent<MeshRenderer>();
                if (entry.materials != null) mr.sharedMaterials = entry.materials;
            }
            UpdatePrefabPlacements();
        }

        public void ClearPreviewObjects()
        {
            if (sourceMeshEntries != null)
                foreach (var e in sourceMeshEntries)
                    if (e.outputObject != null) { DestroyImmediate(e.outputObject); e.outputObject = null; }
            ClearSpawnedPrefabs();
        }

        public bool IsPreviewActive()
        {
            if (sourceMeshEntries == null) return false;
            foreach (var e in sourceMeshEntries)
                if (e.outputObject != null) return true;
            return false;
        }

        // ============================================================
        // Prefab Placement
        // ============================================================

        // 1ルール分のs値リストを計算（ビジュアライズ共有用）
        public List<float> GetPlacementSValues(PrefabPlacementRule rule)
        {
            var sValues = new List<float>();
            if (arcLengthLUT == null) BuildArcLengthLUT();

            if (!GetSourceMeshAxisBounds(out float bMin, out float bMax)) return sValues;
            float sourceMeshLen = bMax - bMin;
            if (sourceMeshLen <= 0f) return sValues;

            int tileCount = Mathf.Max(1, Mathf.CeilToInt(totalArcLength / sourceMeshLen));
            for (int tile = 0; tile < tileCount; tile++)
                sValues.Add(tile * sourceMeshLen);

            return sValues;
        }

        public void UpdatePrefabPlacements()
        {
            ClearSpawnedPrefabs();
            if (arcLengthLUT == null) BuildArcLengthLUT();
            if (placementRules == null) return;

            foreach (var rule in placementRules)
            {
                if (rule.prefab == null) continue;

                var sValues = GetPlacementSValues(rule);

                foreach (float s in sValues)
                {
                    // 前後オフセットはs値をずらしてからスプラインを再評価（tangent近似ではなく正確なカーブ追従）
                    float sEff = Mathf.Clamp(s + rule.positionOffset.z, 0f, totalArcLength);
                    float cant = GetCantAtS(sEff);
                    SplinePoint sp = EvaluateAtArcLength(sEff, cant);
                    Vector3    pos = sp.position
                        + sp.binormal * rule.positionOffset.x
                        + sp.normal   * rule.positionOffset.y;
                    Quaternion rot = rule.followCant
                        ? Quaternion.LookRotation(sp.tangent, sp.normal)
                        : Quaternion.LookRotation(sp.tangent, Vector3.up);
                    rot = rot * Quaternion.Euler(r