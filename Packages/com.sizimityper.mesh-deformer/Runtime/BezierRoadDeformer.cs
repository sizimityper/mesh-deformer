using System;
using System.Collections.Generic;
using UnityEngine;

namespace SizimityperMeshDeformer
{
    public enum AxisDirection { X, Y, Z }
    public enum CurveMode    { Curve, Interpolation, Straight }
    public enum DeformMode   { Stretch, Cut }
    public enum TangentAxis  { PosZ, NegZ, PosX, NegX, PosY, NegY }

    [Serializable]
    public class SourceMeshEntry
    {
        public Mesh       mesh;
        public Material[] materials;
        public string     meshName;
        [HideInInspector] public GameObject outputObject;
    }

    [Serializable]
    public class PrefabPlacementRule
    {
        public GameObject prefab;
        public float intervalM   = 20f;
        public float offsetRight = 0f;
        public float offsetUp    = 0f;
        public bool  followCant  = true;
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
        public bool  paramAutoCalcDesignSpeed = true;
        public float paramDesignSpeed         = 60f;
        public bool  paramAutoCalcFriction    = true;
        public float paramFrictionCoeff       = 0.13f;
        public bool  paramAutoApplyCant       = true;
        public bool  paramAutoCalcEasement    = true;

        // --- Interpolation Mode ---
        public Transform   interpStartObject;
        public Transform   interpEndObject;
        public TangentAxis interpStartTangentAxis = TangentAxis.PosZ;
        public TangentAxis interpEndTangentAxis   = TangentAxis.PosZ;
        [HideInInspector] public Vector3 interpStartTangent = Vector3.forward; // legacy
        [HideInInspector] public Vector3 interpEndTangent   = Vector3.forward; // legacy

        // --- Straight Mode ---
        public float paramStraightLength    = 100f;  // uses paramGrade
        public bool  paramStraightAutoGrade = false; // true: grade derived from height
        public float paramStraightHeight    = 0f;    // target height (used when paramStraightAutoGrade)

        // ============================================================
        // Deform Mode
        // ============================================================
        public DeformMode deformMode = DeformMode.Cut;

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
            if (sourceMeshEntries != null)
                foreach (var e in sourceMeshEntries)
                    if (e.outputObject != null) DestroyImmediate(e.outputObject);

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

                sourceMeshEntries.Add(new SourceMeshEntry
                {
                    mesh     = newMesh,
                    materials = mats,
                    meshName  = mf.sharedMesh.name
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
            i = Mathf.Clamp(i, 0f, 0.12f);
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
                float total  = Mathf.Max(totalArcLength, 1e-6f);
                float easLen = paramUseEasement ? Mathf.Min(Mathf.Max(paramEasementLength, 0f), total * 0.5f) : 0f;
                if (!paramUseEasement || easLen <= 0f) return paramCantAngle;
                float midLen = total - 2f * easLen;
                if (s < easLen)          return Mathf.Lerp(0f, paramCantAngle, s / easLen);
                if (s < easLen + midLen) return paramCantAngle;
                return Mathf.Lerp(paramCantAngle, 0f, Mathf.Clamp01((s - easLen - midLen) / easLen));
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

        private void GenerateInterpolationCurve(int resolution = 200)
        {
            if (interpStartObject == null || interpEndObject == null) return;

            Vector3 p0        = interpStartObject.position;
            Vector3 p1        = interpEndObject.position;
            float   handleLen = Vector3.Distance(p0, p1);
            Vector3 t0        = GetTangentDirection(interpStartObject, interpStartTangentAxis) * handleLen;
            Vector3 t1        = GetTangentDirection(interpEndObject,   interpEndTangentAxis)   * handleLen;

            int steps     = Mathf.Max(resolution, 10);
            paramPoints   = new List<Vector3>(steps + 1);
            paramTangents = new List<Vector3>(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                float u  = (float)i / steps;
                float u2 = u * u, u3 = u2 * u;

                Vector3 wPos = (2f * u3 - 3f * u2 + 1f) * p0 + (u3 - 2f * u2 + u) * t0
                             + (-2f * u3 + 3f * u2) * p1    + (u3 - u2) * t1;
                Vector3 wTan = (6f * u2 - 6f * u) * p0 + (3f * u2 - 4f * u + 1f) * t0
                             + (-6f * u2 + 6f * u) * p1 + (3f * u2 - 2f * u) * t1;

                paramPoints.Add(transform.InverseTransformPoint(wPos));
                Vector3 localTan = wTan.sqrMagnitude > 1e-8f
                    ? transform.InverseTransformDirection(wTan.normalized)
                    : Vector3.forward;
                paramTangents.Add(localTan);
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

            Quaternion cantRot = Quaternion.AngleAxis(cantDeg, tan);
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
            return transform.InverseTransformDirection(worldNorm).normalized;
        }

        // ============================================================
        // Mesh Deformation
        // ============================================================

        public List<Mesh> DeformAllMeshes()
        {
            var result = new List<Mesh>();
            if (sourceMeshEntries == null || sourceMeshEntries.Count == 0) return result;

            arcLengthLUT = null;
            BuildArcLengthLUT();
            if (totalArcLength <= 0f) return result;

            foreach (var entry in sourceMeshEntries)
            {
                if (entry.mesh == null) { result.Add(null); continue; }
                result.Add(deformMode == DeformMode.Stretch
                    ? DeformStretch(entry.mesh)
                    : DeformCut(entry.mesh));
            }
            return result;
        }

        private Mesh DeformStretch(Mesh srcMesh)
        {
            var srcVerts = srcMesh.vertices;
            var srcNorms = srcMesh.normals;
            bool hasNormals = srcNorms != null && srcNorms.Length == srcVerts.Length;
            var  srcUVs     = srcMesh.uv;
            int  subCount   = srcMesh.subMeshCount;

            // Per-mesh axis bounds
            float meshMinA = float.MaxValue, meshMaxA = float.MinValue;
            foreach (var v in srcVerts)
            {
                float a = GetAxisValue(v);
                if (a < meshMinA) meshMinA = a;
                if (a > meshMaxA) meshMaxA = a;
            }
            float meshLen   = Mathf.Max(meshMaxA - meshMinA, 1e-6f);
            int   tileCount = Mathf.Max(1, Mathf.CeilToInt(totalArcLength / meshLen));

            // Padded range for localT
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

                    float cant = GetCantAtS(s);
                    SplinePoint sp = EvaluateAtArcLength(s, cant);
                    var (rightOff, upOff) = GetLateralOffsets(srcVerts[i]);

                    combinedVerts.Add(transform.InverseTransformPoint(sp.position + sp.binormal * rightOff + sp.normal * upOff));
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
            for (int sub = 0; sub < subCount; sub++) mesh.SetTriangles(subTriLists[sub], sub);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh DeformCut(Mesh srcMesh)
        {
            var srcVerts = srcMesh.vertices;
            var srcNorms = srcMesh.normals;
            bool hasNormals = srcNorms != null && srcNorms.Length == srcVerts.Length;
            var  srcUVs     = srcMesh.uv;
            int  subCount   = srcMesh.subMeshCount;

            // Per-mesh axis bounds
            float meshMinA = float.MaxValue, meshMaxA = float.MinValue;
            foreach (var v in srcVerts)
            {
                float a = GetAxisValue(v);
                if (a < meshMinA) meshMinA = a;
                if (a > meshMaxA) meshMaxA = a;
            }
            float meshLen = Mathf.Max(meshMaxA - meshMinA, 1e-6f);
            int   tileCount = Mathf.Max(1, Mathf.CeilToInt(totalArcLength / meshLen));

            // Padded range for localT
            float adjMin   = meshMinA + tileAxisPadding;
            float adjMax   = meshMaxA - tileAxisPadding;
            float adjRange = Mathf.Max(adjMax - adjMin, 1e-6f);

            var combinedVerts   = new List<Vector3>();
            var combinedNorms   = new List<Vector3>();
            var combinedUVs     = new List<Vector2>();
            var combinedTrimmed = new List<bool>();
            var subTriLists     = new List<List<int>>();
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

                    float cant = GetCantAtS(s);
                    SplinePoint sp = EvaluateAtArcLength(s, cant);
                    var (rightOff, upOff) = GetLateralOffsets(srcVerts[i]);

                    combinedVerts.Add(transform.InverseTransformPoint(sp.position + sp.binormal * rightOff + sp.normal * upOff));
                    combinedNorms.Add(hasNormals ? TransformNormal(srcNorms[i], sp) : Vector3.up);
                    combinedUVs.Add(srcUVs != null && i < srcUVs.Length ? srcUVs[i] : Vector2.zero);
                    combinedTrimmed.Add(trimmed);

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
                        int ia = baseIdx + tris[t];
                        int ib = baseIdx + tris[t + 1];
                        int ic = baseIdx + tris[t + 2];
                        // Skip triangles where any vertex falls beyond the spline end
                        if (combinedTrimmed[ia] || combinedTrimmed[ib] || combinedTrimmed[ic]) continue;
                        subTriLists[sub].Add(ia);
                        subTriLists[sub].Add(ib);
                        subTriLists[sub].Add(ic);
                    }
                }
            }

            // Position weld at junctions (1 cm threshold)
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

        public void UpdatePrefabPlacements()
        {
            ClearSpawnedPrefabs();
            if (arcLengthLUT == null) BuildArcLengthLUT();
            if (placementRules == null) return;

            foreach (var rule in placementRules)
            {
                if (rule.prefab == null || rule.intervalM <= 0f) continue;
                for (float s = 0f; s <= totalArcLength + 1e-4f; s += rule.intervalM)
                {
                    float cant = GetCantAtS(s);
                    SplinePoint sp = EvaluateAtArcLength(s, cant);
                    Vector3    pos = sp.position + sp.binormal * rule.offsetRight + sp.normal * rule.offsetUp;
                    Quaternion rot = rule.followCant
                        ? Quaternion.LookRotation(sp.tangent, sp.normal)
                        : Quaternion.LookRotation(sp.tangent, Vector3.up);
#if UNITY_EDITOR
                    var go = UnityEditor.PrefabUtility.InstantiatePrefab(rule.prefab, transform) as GameObject;
                    if (go != null)
                    {
                        go.transform.position = pos;
                        go.transform.rotation = rot;
                        go.hideFlags          = HideFlags.DontSave;
                        spawnedPrefabs.Add(go);
                    }
#endif
                }
            }
        }

        public void ClearSpawnedPrefabs()
        {
            if (spawnedPrefabs == null) spawnedPrefabs = new List<GameObject>();
            foreach (var go in spawnedPrefabs) if (go != null) DestroyImmediate(go);
            spawnedPrefabs.Clear();
        }
    }
}
