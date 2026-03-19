using System.Collections.Generic;
using UnityEngine;

namespace SizimityperMeshDeformer
{
    public enum AxisDirection { X, Y, Z }
    public enum CurveMode { Pivot, Parameter }
    public enum DeformMode { Stretch, Array }

    [System.Serializable]
    public class PrefabPlacementRule
    {
        public GameObject prefab;
        public float interval = 20f;
        public float offsetRight = 0f;
        public float offsetUp = 0f;
        public bool followCant = true;
    }

    /// <summary>ソース親オブジェクト配下の各MeshFilterから収集したエントリ</summary>
    [System.Serializable]
    public class SourceMeshEntry
    {
        /// <summary>親オブジェクトのローカル空間に変換済みのメッシュ</summary>
        public Mesh mesh;
        public Material[] materials;
        /// <summary>プレビュー/ベイク後の出力オブジェクト</summary>
        [HideInInspector] public GameObject outputObject;
    }

    [ExecuteInEditMode]
    [AddComponentMenu("Sizimityper/Bezier Road Deformer")]
    public class BezierRoadDeformer : MonoBehaviour
    {
        // ============================================================
        // Base Object Settings
        // ============================================================

        /// <summary>配下の全 MeshFilter を対象にする親オブジェクト</summary>
        public GameObject sourceParentObject;
        public AxisDirection axisDirection = AxisDirection.X;

        [HideInInspector] public float meshLength = 0f;

        /// <summary>収集済みのソースメッシュエントリ一覧（読み取り専用参照用）</summary>
        [HideInInspector] public List<SourceMeshEntry> sourceMeshEntries = new List<SourceMeshEntry>();

        // ============================================================
        // Curve Mode
        // ============================================================
        public CurveMode curveMode = CurveMode.Pivot;

        // ---- Pivot Mode ----
        public float handleLength = 10f;
        public List<Transform> pivots = new List<Transform>();

        // ---- Parameter Mode ----
        public float paramR = 300f;
        public float paramAngle = 90f;
        public float paramCantAngle = 6f;
        public float paramGrade = 0f;
        public bool paramUseEasement = true;
        public float paramEasementLength = 50f;
        /// <summary>true = 右カーブ、false = 左カーブ</summary>
        public bool paramTurnRight = true;

        // ---- カント自動計算用 ----
        /// <summary>true のとき、設計速度を R から自動推定する</summary>
        public bool paramAutoCalcSpeed = true;
        /// <summary>設計速度 (km/h)。paramAutoCalcSpeed = false のとき手動入力。</summary>
        public float paramDesignSpeed = 60f;
        /// <summary>true のとき、横すべり摩擦係数を設計速度から自動算出する</summary>
        public bool paramAutoCalcFriction = true;
        /// <summary>true のとき、カント角を自動計算結果で常に上書きする</summary>
        public bool paramAutoApplyCant = true;
        /// <summary>true のとき、緩和区間長を設計速度から自動算出して上書きする</summary>
        public bool paramAutoCalcEasement = true;
        /// <summary>横すべり摩擦係数 f。paramAutoCalcFriction = false のとき手動入力。</summary>
        public float paramFrictionCoeff = 0.13f;
        /// <summary>最大片勾配 (例: 0.08 = 8%)。カント上限クランプに使用。</summary>
        public float paramMaxSuperelevation = 0.08f;

        // 推奨R テーブル (道路構造令準拠)
        private static readonly float[] _speedTable    = { 20f,  30f,  40f,  50f,  60f,  80f, 100f, 120f };
        private static readonly float[] _recRTable     = { 15f,  30f,  60f, 100f, 150f, 280f, 460f, 710f };
        // 設計速度→横すべり摩擦係数テーブル (道路構造令準拠)
        private static readonly float[] _frictionTable  = { 0.15f, 0.15f, 0.15f, 0.14f, 0.13f, 0.12f, 0.11f, 0.10f };
        // 設計速度→緩和区間長テーブル (道路構造令第18条準拠)
        private static readonly float[] _easementTable  = {  20f,   25f,   35f,   40f,   50f,   70f,   85f,  100f };

        /// <summary>
        /// paramR から推奨Rテーブルを逆引きして設計速度 (km/h) を推定する。
        /// テーブル間は線形補間。
        /// </summary>
        public float CalcDesignSpeedFromR()
        {
            float R = Mathf.Max(paramR, 0.1f);
            if (R <= _recRTable[0]) return _speedTable[0];
            if (R >= _recRTable[_recRTable.Length - 1]) return _speedTable[_speedTable.Length - 1];
            for (int i = 0; i < _recRTable.Length - 1; i++)
            {
                if (R >= _recRTable[i] && R <= _recRTable[i + 1])
                {
                    float t = (R - _recRTable[i]) / (_recRTable[i + 1] - _recRTable[i]);
                    return Mathf.Lerp(_speedTable[i], _speedTable[i + 1], t);
                }
            }
            return _speedTable[_speedTable.Length - 1];
        }

        /// <summary>
        /// 設計速度 (km/h) から横すべり摩擦係数を算出する。
        /// テーブル間は線形補間。
        /// </summary>
        public float CalcFrictionFromSpeed(float speedKmh)
        {
            if (speedKmh <= _speedTable[0]) return _frictionTable[0];
            if (speedKmh >= _speedTable[_speedTable.Length - 1]) return _frictionTable[_frictionTable.Length - 1];
            for (int i = 0; i < _speedTable.Length - 1; i++)
            {
                if (speedKmh >= _speedTable[i] && speedKmh <= _speedTable[i + 1])
                {
                    float t = (speedKmh - _speedTable[i]) / (_speedTable[i + 1] - _speedTable[i]);
                    return Mathf.Lerp(_frictionTable[i], _frictionTable[i + 1], t);
                }
            }
            return _frictionTable[_frictionTable.Length - 1];
        }

        /// <summary>
        /// 設計速度 (km/h) から緩和区間長 (m) を算出する。
        /// テーブル間は線形補間。
        /// </summary>
        public float CalcEasementLengthFromSpeed(float speedKmh)
        {
            if (speedKmh <= _speedTable[0]) return _easementTable[0];
            if (speedKmh >= _speedTable[_speedTable.Length - 1]) return _easementTable[_easementTable.Length - 1];
            for (int i = 0; i < _speedTable.Length - 1; i++)
            {
                if (speedKmh >= _speedTable[i] && speedKmh <= _speedTable[i + 1])
                {
                    float t = (speedKmh - _speedTable[i]) / (_speedTable[i + 1] - _speedTable[i]);
                    return Mathf.Lerp(_easementTable[i], _easementTable[i + 1], t);
                }
            }
            return _easementTable[_easementTable.Length - 1];
        }

        /// <summary>
        /// 設計速度・曲線半径・横すべり摩擦係数からカント角を計算する。
        /// 式: i = V^2 / (127 * R) - f  (道路構造令)
        /// paramAutoCalcSpeed    = true のとき設計速度は R から自動推定。
        /// paramAutoCalcFriction = true のとき摩擦係数は設計速度から自動算出。
        /// </summary>
        public float CalcCantAngle()
        {
            float V = paramAutoCalcSpeed ? CalcDesignSpeedFromR() : paramDesignSpeed;
            float f = paramAutoCalcFriction ? CalcFrictionFromSpeed(V) : paramFrictionCoeff;
            float R = Mathf.Max(paramR, 0.1f);
            float i = (V * V) / (127f * R) - f;
            i = Mathf.Clamp(i, 0f, paramMaxSuperelevation);
            return Mathf.Atan(i) * Mathf.Rad2Deg;
        }

        // ============================================================
        // Deform Mode
        // ============================================================
        public DeformMode deformMode = DeformMode.Array;
        public int subdivisions = 10;

        [Tooltip("タイル境界のはみ出し吸収距離 (m)。ボルト等の突起でタイル境界面の頂点がはみ出している場合に設定。")]
        public float tileAxisPadding = 0.001f;

        // ============================================================
        // Prefab Placement
        // ============================================================
        public List<PrefabPlacementRule> placementRules = new List<PrefabPlacementRule>();

        // ============================================================
        // Internal State
        // ============================================================
        [HideInInspector] public List<GameObject> spawnedPrefabs = new List<GameObject>();

        private const int LUT_RESOLUTION = 200;
        [HideInInspector] public float[] arcLengthLUT;
        [HideInInspector] public float totalArcLength;

        [HideInInspector] public List<Vector3> paramPoints;
        [HideInInspector] public List<Vector3> paramTangents;
        [HideInInspector] public bool paramPointsBuilt = false;

        // ============================================================
        // Initialize
        // ============================================================

        public void Initialize()
        {
            if (sourceParentObject == null)
            {
                Debug.LogWarning("[BezierRoadDeformer] ソース親オブジェクトが設定されていません。");
                return;
            }

            CollectSourceMeshes();

            if (sourceMeshEntries.Count == 0)
            {
                Debug.LogWarning("[BezierRoadDeformer] 配下に MeshFilter が見つかりませんでした。");
                return;
            }

            UpdateMeshLength();
            handleLength = meshLength / 3f;
            CreateDefaultPivots();
            arcLengthLUT = null;
        }

        /// <summary>sourceParentObject 配下の全 MeshFilter を収集し、頂点を親ローカル空間に変換して保持する</summary>
        public void CollectSourceMeshes()
        {
            sourceMeshEntries.Clear();

            var mfs = sourceParentObject.GetComponentsInChildren<MeshFilter>(true);
            var parentInv = sourceParentObject.transform.worldToLocalMatrix;

            foreach (var mf in mfs)
            {
                if (mf.sharedMesh == null) continue;

                // 頂点を親オブジェクトのローカル空間に変換して複製
                Mesh src = mf.sharedMesh;
                var verts = src.vertices;
                var childToParent = parentInv * mf.transform.localToWorldMatrix;

                for (int i = 0; i < verts.Length; i++)
                    verts[i] = childToParent.MultiplyPoint3x4(verts[i]);

                var mesh = new Mesh
                {
                    name = src.name,
                    indexFormat = src.indexFormat
                };
                mesh.vertices = verts;
                mesh.uv       = src.uv;
                mesh.uv2      = src.uv2;
                // 法線も childToParent の回転成分で変換する（スケール考慮のため逆転置行列を使用）
                var normalMatrix = childToParent.inverse.transpose;
                var srcNormsRaw  = src.normals;
                var normsOut     = new Vector3[srcNormsRaw.Length];
                for (int ni = 0; ni < srcNormsRaw.Length; ni++)
                    normsOut[ni] = normalMatrix.MultiplyVector(srcNormsRaw[ni]).normalized;
                mesh.normals = normsOut;
                mesh.tangents = src.tangents;
                mesh.colors   = src.colors;
                mesh.subMeshCount = src.subMeshCount;
                for (int s = 0; s < src.subMeshCount; s++)
                    mesh.SetTriangles(src.GetTriangles(s), s);
                mesh.RecalculateBounds();

                var mr = mf.GetComponent<MeshRenderer>();
                sourceMeshEntries.Add(new SourceMeshEntry
                {
                    mesh      = mesh,
                    materials = mr != null ? mr.sharedMaterials : new Material[0]
                });
            }
        }

        public void UpdateMeshLength()
        {
            if (sourceMeshEntries == null || sourceMeshEntries.Count == 0) return;

            // 全メッシュの軸方向の合算 bounds から meshLength を決定
            float minA = float.MaxValue, maxA = float.MinValue;
            foreach (var entry in sourceMeshEntries)
            {
                if (entry.mesh == null) continue;
                foreach (var v in entry.mesh.vertices)
                {
                    float a = GetAxisValue(v);
                    if (a < minA) minA = a;
                    if (a > maxA) maxA = a;
                }
            }
            meshLength = maxA - minA;
        }

        // ============================================================
        // Pivot Management
        // ============================================================

        private void CreateDefaultPivots()
        {
            foreach (var p in pivots)
                if (p != null) DestroyImmediate(p.gameObject);
            pivots.Clear();

            var startGO = new GameObject("Pivot_Start");
            startGO.transform.SetParent(transform, false);
            startGO.transform.localPosition = Vector3.zero;
            startGO.transform.localRotation = Quaternion.LookRotation(Vector3.forward);

            var endGO = new GameObject("Pivot_End");
            endGO.transform.SetParent(transform, false);
            endGO.transform.localPosition = new Vector3(0, 0, meshLength);
            endGO.transform.localRotation = Quaternion.LookRotation(Vector3.forward);

            pivots.Add(startGO.transform);
            pivots.Add(endGO.transform);
        }

        public void AddPivot()
        {
            if (pivots.Count < 2) return;

            int insertIdx = pivots.Count - 1;
            Transform prev = pivots[insertIdx - 1];
            Transform next = pivots[insertIdx];

            var go = new GameObject($"Pivot_{insertIdx}");
            go.transform.SetParent(transform, true);
            go.transform.position = (prev.position + next.position) * 0.5f;
            go.transform.rotation = Quaternion.Slerp(prev.rotation, next.rotation, 0.5f);

            pivots.Insert(insertIdx, go.transform);
            RenameAllPivots();
            arcLengthLUT = null;
        }

        public void RemovePivot(int index)
        {
            if (index <= 0 || index >= pivots.Count - 1) return;
            if (pivots[index] != null)
                DestroyImmediate(pivots[index].gameObject);
            pivots.RemoveAt(index);
            RenameAllPivots();
            arcLengthLUT = null;
        }

        private void RenameAllPivots()
        {
            for (int i = 0; i < pivots.Count; i++)
            {
                if (pivots[i] == null) continue;
                if (i == 0) pivots[i].name = "Pivot_Start";
                else if (i == pivots.Count - 1) pivots[i].name = "Pivot_End";
                else pivots[i].name = $"Pivot_{i}";
            }
        }

        // ============================================================
        // Spline Evaluation
        // ============================================================

        public struct SplinePoint
        {
            public Vector3 position;
            public Vector3 tangent;
            public Vector3 normal;
            public Vector3 binormal;
            public float cant;
        }

        public static Vector3 CubicBezier(Vector3 p0, Vector3 h0, Vector3 h1, Vector3 p1, float t)
        {
            float u = 1f - t;
            return u * u * u * p0
                 + 3f * u * u * t * h0
                 + 3f * u * t * t * h1
                 + t * t * t * p1;
        }

        public static Vector3 CubicBezierDerivative(Vector3 p0, Vector3 h0, Vector3 h1, Vector3 p1, float t)
        {
            float u = 1f - t;
            return 3f * u * u * (h0 - p0)
                 + 6f * u * t * (h1 - h0)
                 + 3f * t * t * (p1 - h1);
        }

        private void GetSegmentControlPoints(int segIdx,
            out Vector3 p0, out Vector3 h0, out Vector3 h1, out Vector3 p1)
        {
            Transform a = pivots[segIdx];
            Transform b = pivots[segIdx + 1];
            p0 = a.position;
            h0 = p0 + a.forward * handleLength;
            p1 = b.position;
            h1 = p1 - b.forward * handleLength;
        }

        // ============================================================
        // Arc Length LUT
        // ============================================================

        public void BuildArcLengthLUT()
        {
            if (curveMode == CurveMode.Pivot) BuildPivotLUT();
            else BuildParameterLUT();
        }

        private void BuildPivotLUT()
        {
            if (pivots == null || pivots.Count < 2) return;

            int segCount = pivots.Count - 1;
            int total = segCount * LUT_RESOLUTION + 1;
            arcLengthLUT = new float[total];
            arcLengthLUT[0] = 0f;

            float cum = 0f;
            Vector3 prev = pivots[0].position;
            for (int seg = 0; seg < segCount; seg++)
            {
                GetSegmentControlPoints(seg, out var p0, out var h0, out var h1, out var p1);
                for (int s = 1; s <= LUT_RESOLUTION; s++)
                {
                    Vector3 cur = CubicBezier(p0, h0, h1, p1, (float)s / LUT_RESOLUTION);
                    cum += Vector3.Distance(prev, cur);
                    arcLengthLUT[seg * LUT_RESOLUTION + s] = cum;
                    prev = cur;
                }
            }
            totalArcLength = cum;
        }

        private void BuildParameterLUT()
        {
            GenerateParameterCurve(LUT_RESOLUTION);
            if (paramPoints == null || paramPoints.Count < 2) return;

            arcLengthLUT = new float[paramPoints.Count];
            arcLengthLUT[0] = 0f;
            float cum = 0f;
            for (int i = 1; i < paramPoints.Count; i++)
            {
                cum += Vector3.Distance(paramPoints[i - 1], paramPoints[i]);
                arcLengthLUT[i] = cum;
            }
            totalArcLength = cum;
        }

        public SplinePoint EvaluateAtArcLength(float s, float cantDeg = 0f)
        {
            if (arcLengthLUT == null) BuildArcLengthLUT();
            s = Mathf.Clamp(s, 0f, totalArcLength);

            int lo = 0, hi = arcLengthLUT.Length - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (arcLengthLUT[mid] <= s) lo = mid;
                else hi = mid;
            }

            float segLen = arcLengthLUT[hi] - arcLengthLUT[lo];
            float alpha  = segLen > 1e-6f ? (s - arcLengthLUT[lo]) / segLen : 0f;

            Vector3 pos, tan;

            if (curveMode == CurveMode.Pivot)
            {
                int segCount = pivots.Count - 1;
                int seg      = Mathf.Clamp(lo / LUT_RESOLUTION, 0, segCount - 1);
                int localIdx = lo - seg * LUT_RESOLUTION;
                float localT = Mathf.Clamp01((localIdx + alpha) / LUT_RESOLUTION);

                GetSegmentControlPoints(seg, out var p0, out var h0, out var h1, out var p1);
                pos = CubicBezier(p0, h0, h1, p1, localT);
                tan = CubicBezierDerivative(p0, h0, h1, p1, localT);
            }
            else
            {
                pos = Vector3.Lerp(paramPoints[lo], paramPoints[hi], alpha);
                tan = paramTangents != null && paramTangents.Count > lo
                    ? Vector3.Lerp(paramTangents[lo], paramTangents[Mathf.Min(hi, paramTangents.Count - 1)], alpha)
                    : (paramPoints[hi] - paramPoints[lo]);
                pos = transform.TransformPoint(pos);
                tan = transform.TransformDirection(tan);
            }

            if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward;
            tan.Normalize();

            Quaternion cantRot = Quaternion.AngleAxis(-cantDeg, tan);
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
        // Parameter Mode Curve Generation
        // ============================================================

        public void GenerateParameterCurve(int resolution = 200)
        {
            float R = Mathf.Max(paramR, 0.1f);
            float totalAngleRad = paramAngle * Mathf.Deg2Rad;
            float easLen = paramUseEasement ? Mathf.Max(paramEasementLength, 0f) : 0f;

            // クロソイド1区間が回頭する角度 = L / (2R)
            float easAngle    = easLen > 0f ? easLen / (2f * R) : 0f;
            float arcAngleRad = Mathf.Max(0f, totalAngleRad - 2f * easAngle);
            float arcLen      = R * arcAngleRad;
            float totalLen    = 2f * easLen + arcLen;
            if (totalLen <= 0f) totalLen = 1f;

            int steps = Mathf.Max(resolution, 10);
            float ds = totalLen / steps;
            float gradeSlope = paramGrade / 100f;

            paramPoints   = new List<Vector3>(steps + 1);
            paramTangents = new List<Vector3>(steps + 1);

            float heading = 0f, px = 0f, py = 0f, pz = 0f;
            paramPoints.Add(Vector3.zero);
            paramTangents.Add(Vector3.forward);

            float turnSign = paramTurnRight ? 1f : -1f;
            for (int i = 1; i <= steps; i++)
            {
                float sMid = ((float)(i - 1) + 0.5f) / steps * totalLen;
                heading += GetCurvatureAtS(sMid, R, easLen, arcLen, totalLen) * ds * turnSign;
                px += Mathf.Sin(heading) * ds;
                pz += Mathf.Cos(heading) * ds;
                py += gradeSlope * ds;
                paramPoints.Add(new Vector3(px, py, pz));
                paramTangents.Add(new Vector3(Mathf.Sin(heading), gradeSlope, Mathf.Cos(heading)).normalized);
            }
            paramPointsBuilt = true;
        }

        private float GetCurvatureAtS(float s, float R, float easLen, float arcLen, float totalLen)
        {
            if (!paramUseEasement || easLen <= 0f)
                return (s >= 0f && s <= arcLen) ? 1f / R : 0f;

            float A2 = R * easLen;
            if (s < easLen)            return s / A2;
            else if (s < easLen + arcLen) return 1f / R;
            else                          return Mathf.Max(0f, totalLen - s) / A2;
        }

        public float GetCantAtS(float s)
        {
            float easLen = paramUseEasement ? paramEasementLength : 0f;
            float R      = Mathf.Max(paramR, 0.1f);
            float easAngle    = easLen > 0f ? easLen / (2f * R) : 0f;
            float arcLen      = Mathf.Max(0f, R * (paramAngle * Mathf.Deg2Rad - 2f * easAngle));
            float totalLen    = 2f * easLen + arcLen;

            float cantSign = paramTurnRight ? 1f : -1f;
            if (!paramUseEasement || easLen <= 0f) return paramCantAngle * cantSign;
            if (s < easLen)               return Mathf.Lerp(0f, paramCantAngle, s / easLen) * cantSign;
            else if (s < easLen + arcLen) return paramCantAngle * cantSign;
            else return Mathf.Lerp(0f, paramCantAngle, Mathf.Clamp01((totalLen - s) / easLen)) * cantSign;
        }

        // ============================================================
        // Convert Parameter -> Pivot
        // ============================================================

        public void ConvertParameterToPivot()
        {
            if (curveMode != CurveMode.Parameter) return;
            BuildArcLengthLUT();
            if (totalArcLength <= 0f) return;

            foreach (var p in pivots)
                if (p != null) DestroyImmediate(p.gameObject);
            pivots.Clear();

            int pivotCount = Mathf.Max(2, Mathf.CeilToInt(totalArcLength / 50f) + 1);
            for (int i = 0; i < pivotCount; i++)
            {
                float s    = (float)i / (pivotCount - 1) * totalArcLength;
                float cant = GetCantAtS(s);
                SplinePoint sp = EvaluateAtArcLength(s, cant);

                string name = i == 0 ? "Pivot_Start" : (i == pivotCount - 1 ? "Pivot_End" : $"Pivot_{i}");
                var go = new GameObject(name);
                go.transform.SetParent(transform, true);
                go.transform.position = sp.position;
                if (sp.tangent.sqrMagnitude > 0.001f)
                    go.transform.rotation = Quaternion.LookRotation(sp.tangent, sp.normal);
                pivots.Add(go.transform);
            }

            handleLength = totalArcLength / (pivotCount - 1) / 3f;
            curveMode    = CurveMode.Pivot;
            arcLengthLUT = null;
        }

        // ============================================================
        // Mesh Deformation
        // ============================================================

        private float GetAxisValue(Vector3 v)
        {
            switch (axisDirection)
            {
                case AxisDirection.X: return v.x;
                case AxisDirection.Y: return v.y;
                case AxisDirection.Z: return v.z;
            }
            return 0f;
        }

        private (float right, float up) GetLateralOffsets(Vector3 v)
        {
            switch (axisDirection)
            {
                case AxisDirection.X: return (v.z, v.y);
                case AxisDirection.Y: return (v.x, v.z);
                case AxisDirection.Z: return (v.x, v.y);
            }
            return (0f, 0f);
        }

        private (float minA, float maxA) GetCombinedAxisBounds()
        {
            float minA = float.MaxValue, maxA = float.MinValue;
            foreach (var entry in sourceMeshEntries)
            {
                if (entry.mesh == null) continue;
                foreach (var v in entry.mesh.vertices)
                {
                    float a = GetAxisValue(v);
                    if (a < minA) minA = a;
                    if (a > maxA) maxA = a;
                }
            }
            return (minA, maxA);
        }

        private Vector3 MapVertex(Vector3 parentLocalVert, float arcS, float cant)
        {
            SplinePoint sp = EvaluateAtArcLength(arcS, cant);
            var (rightOff, upOff) = GetLateralOffsets(parentLocalVert);
            Vector3 worldPos = sp.position + sp.binormal * rightOff + sp.normal * upOff;
            return transform.InverseTransformPoint(worldPos);
        }

        /// <summary>全エントリのメッシュを変形して返す。インデックスは sourceMeshEntries に対応。</summary>
        public List<Mesh> DeformAllMeshes()
        {
            if (sourceMeshEntries == null || sourceMeshEntries.Count == 0) return null;

            BuildArcLengthLUT();
            if (totalArcLength <= 0f) return null;

            var results = new List<Mesh>();

            // Stretch モードのみ全メッシュ共通のバウンズが必要
            float minA = 0f, axisRange = 1f;
            if (deformMode == DeformMode.Stretch)
            {
                var (cMin, cMax) = GetCombinedAxisBounds();
                minA      = cMin;
                axisRange = Mathf.Max(cMax - cMin, 1e-6f);
            }

            foreach (var entry in sourceMeshEntries)
            {
                Mesh deformed = deformMode == DeformMode.Stretch
                    ? DeformStretch(entry.mesh, minA, axisRange)
                    : DeformArray(entry.mesh);
                results.Add(deformed);
            }
            return results;
        }

        private Mesh DeformStretch(Mesh src, float minA, float axisRange)
        {
            var srcVerts = src.vertices;
            var newVerts = new Vector3[srcVerts.Length];
            for (int i = 0; i < srcVerts.Length; i++)
            {
                float t    = (GetAxisValue(srcVerts[i]) - minA) / axisRange;
                float s    = t * totalArcLength;
                float cant = curveMode == CurveMode.Parameter ? GetCantAtS(s) : 0f;
                newVerts[i] = MapVertex(srcVerts[i], s, cant);
            }
            return BuildMesh(src.name, newVerts, src.uv, src);
        }

        private Mesh DeformArray(Mesh src)
        {
            if (meshLength <= 0f) return null;

            int tileCount = Mathf.CeilToInt(totalArcLength / meshLength);

            var srcVerts   = src.vertices;
            var srcNormals = src.normals;   // ★ ソース法線を取得
            var srcUVs     = src.uv;
            int subCount   = src.subMeshCount;
            int srcVCount  = srcVerts.Length;
            bool hasNormals = srcNormals != null && srcNormals.Length == srcVCount;

            // このメッシュ自身のバウンズで localT を計算
            // → Clamp01 だけで自然に 0/1 となり隣接タイルの境界 s 値が一致する
            float meshMinA = float.MaxValue, meshMaxA = float.MinValue;
            foreach (var v in srcVerts)
            {
                float a = GetAxisValue(v);
                if (a < meshMinA) meshMinA = a;
                if (a > meshMaxA) meshMaxA = a;
            }
            // tileAxisPadding でタイル境界の「有効範囲」を内側に縮小する
            // → 境界面より外側にある突起ジオメトリを Clamp01 で境界に吸収できる
            float adjustedMin   = meshMinA + tileAxisPadding;
            float adjustedMax   = meshMaxA - tileAxisPadding;
            float adjustedRange = Mathf.Max(adjustedMax - adjustedMin, 1e-6f);

            var combinedVerts   = new List<Vector3>();
            var combinedNormals = new List<Vector3>();
            var combinedUVs     = new List<Vector2>();
            var subTriLists     = new List<List<int>>();
            for (int sub = 0; sub < subCount; sub++)
                subTriLists.Add(new List<int>());

            // ジャンクション頂点追跡（位置スナップ用）
            var junctionEndIdx   = new List<List<int>>();
            var junctionStartIdx = new List<List<int>>();
            for (int t = 0; t < tileCount; t++)
            {
                junctionEndIdx.Add(new List<int>());
                junctionStartIdx.Add(new List<int>());
            }

            for (int tile = 0; tile < tileCount; tile++)
            {
                float tileStartS = tile * meshLength;
                float tileEndS   = (tile == tileCount - 1)
                    ? totalArcLength
                    : (tile + 1) * meshLength;
                float tileLen = tileEndS - tileStartS;
                int   baseIdx = combinedVerts.Count;

                for (int i = 0; i < srcVCount; i++)
                {
                    float axisVal = GetAxisValue(srcVerts[i]);
                    float localT  = Mathf.Clamp01((axisVal - adjustedMin) / adjustedRange);
                    float s = localT <= 0f ? tileStartS
                            : localT >= 1f ? tileEndS
                            : tileStartS + localT * tileLen;

                    float      cant = curveMode == CurveMode.Parameter ? GetCantAtS(s) : 0f;
                    SplinePoint sp  = EvaluateAtArcLength(s, cant);

                    // 位置
                    var (rightOff, upOff) = GetLateralOffsets(srcVerts[i]);
                    Vector3 worldPos = sp.position + sp.binormal * rightOff + sp.normal * upOff;
                    combinedVerts.Add(transform.InverseTransformPoint(worldPos));

                    // ★ 法線：ソース法線をスプラインフレームで変換する
                    //    同じ s 値では必ず同じフレームになるため、タイル境界の継ぎ目が原理的に消える
                    //    RecalculateNormals は使わない
                    Vector3 srcNorm = hasNormals ? srcNormals[i] : Vector3.up;
                    combinedNormals.Add(TransformNormal(srcNorm, sp));

                    combinedUVs.Add((srcUVs != null && i < srcUVs.Length) ? srcUVs[i] : Vector2.zero);

                    int gIdx = combinedVerts.Count - 1;
                    if (localT <= 0f) junctionStartIdx[tile].Add(gIdx);
                    if (localT >= 1f) junctionEndIdx[tile].Add(gIdx);
                }

                for (int sub = 0; sub < subCount; sub++)
                {
                    int[] tris = src.GetTriangles(sub);
                    for (int t = 0; t < tris.Length; t++)
                        subTriLists[sub].Add(baseIdx + tris[t]);
                }
            }

            // ジャンクション頂点インデックスの記録（続き）
            // ※ 上の for ループ内でも追加しているが、ここで loop を締める
            // （実際にはループ内で追加済み）

            // タイル境界の位置スナップ：残存する微小ズレを解消する
            // 同一 s 値を持つはずの終端/始端頂点を平均位置に揃える
            const float SNAP_DIST_SQ = 0.01f * 0.01f; // 1cm 以内なら同一点とみなしてスナップ
            for (int tile = 0; tile < tileCount - 1; tile++)
            {
                var ends   = junctionEndIdx[tile];
                var starts = junctionStartIdx[tile + 1];
                if (ends.Count == 0 || starts.Count == 0) continue;

                // ends の各頂点に最近傍の starts 頂点を探してスナップ
                var startUsed = new bool[starts.Count];
                foreach (int ei in ends)
                {
                    int   bestSj   = -1;
                    float bestDist = SNAP_DIST_SQ;
                    for (int sj = 0; sj < starts.Count; sj++)
                    {
                        if (startUsed[sj]) continue;
                        float d = (combinedVerts[ei] - combinedVerts[starts[sj]]).sqrMagnitude;
                        if (d < bestDist) { bestDist = d; bestSj = sj; }
                    }
                    if (bestSj < 0) continue;

                    int si  = starts[bestSj];
                    Vector3 avg = (combinedVerts[ei] + combinedVerts[si]) * 0.5f;
                    combinedVerts[ei] = avg;
                    combinedVerts[si] = avg;
                    startUsed[bestSj]  = true;
                }
            }

            var mesh = new Mesh
            {
                name = src.name + "_deformed",
                indexFormat = combinedVerts.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(combinedVerts);
            mesh.SetNormals(combinedNormals);   // ★ 変換済み法線を直接セット
            mesh.SetUVs(0, combinedUVs);
            mesh.subMeshCount = subCount;
            for (int sub = 0; sub < subCount; sub++)
                mesh.SetTriangles(subTriLists[sub], sub);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// ソース法線をスプラインのローカルフレーム（tangent/normal/binormal）で変換する。
        /// 軸線方向に応じてソース座標系とスプライン座標系のマッピングを切り替える。
        /// </summary>
        private Vector3 TransformNormal(Vector3 srcNorm, SplinePoint sp)
        {
            // 軸線方向 → スプライン tangent、
            // 上方向   → sp.normal、
            // 右方向   → sp.binormal  にそれぞれ対応させる
            Vector3 worldNorm;
            switch (axisDirection)
            {
                case AxisDirection.X:
                    // X=forward, Y=up, Z=right
                    worldNorm = srcNorm.x * sp.tangent
                              + srcNorm.y * sp.normal
                              + srcNorm.z * sp.binormal;
                    break;
                case AxisDirection.Y:
                    // Y=forward, Z=up, X=right
                    worldNorm = srcNorm.y * sp.tangent
                              + srcNorm.z * sp.normal
                              + srcNorm.x * sp.binormal;
                    break;
                default: // Z
                    // Z=forward, Y=up, X=right
                    worldNorm = srcNorm.z * sp.tangent
                              + srcNorm.y * sp.normal
                              + srcNorm.x * sp.binormal;
                    break;
            }
            return transform.InverseTransformDirection(worldNorm).normalized;
        }

        private Mesh BuildMesh(string meshName, Vector3[] verts, Vector2[] uvs, Mesh src)
        {
            var mesh = new Mesh { name = meshName + "_deformed" };
            mesh.vertices = verts;
            if (uvs != null && uvs.Length == verts.Length) mesh.uv = uvs;
            mesh.subMeshCount = src.subMeshCount;
            for (int sub = 0; sub < src.subMeshCount; sub++)
                mesh.SetTriangles(src.GetTriangles(sub), sub);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ============================================================
        // Preview Update
        // ============================================================

        public void UpdatePreview()
        {
            arcLengthLUT = null;
            var deformedMeshes = DeformAllMeshes();
            if (deformedMeshes == null) return;

            // 出力用子オブジェクトを作成/更新
            for (int i = 0; i < sourceMeshEntries.Count; i++)
            {
                var entry   = sourceMeshEntries[i];
                var deformed = deformedMeshes[i];
                if (deformed == null) continue;

                if (entry.outputObject == null)
                {
                    entry.outputObject = new GameObject($"_Deformed_{i}");
                    entry.outputObject.transform.SetParent(transform, false);
                    entry.outputObject.hideFlags = HideFlags.DontSave;
                }

                var mf = entry.outputObject.GetComponent<MeshFilter>();
                if (mf == null) mf = entry.outputObject.AddComponent<MeshFilter>();
                mf.sharedMesh = deformed;

                var mr = entry.outputObject.GetComponent<MeshRenderer>();
                if (mr == null) mr = entry.outputObject.AddComponent<MeshRenderer>();
                mr.sharedMaterials = entry.materials ?? new Material[0];
            }

            UpdatePrefabPlacements();
        }

        public void ClearPreviewObjects()
        {
            foreach (var entry in sourceMeshEntries)
            {
                if (entry.outputObject != null)
                {
                    DestroyImmediate(entry.outputObject);
                    entry.outputObject = null;
                }
            }

            // DontSave の残存オブジェクトも掃除
            var toDelete = new List<Transform>();
            foreach (Transform child in transform)
            {
                if (child.hideFlags == HideFlags.DontSave
                    && !child.name.StartsWith("Pivot_"))
                    toDelete.Add(child);
            }
            foreach (var t in toDelete)
                if (t != null) DestroyImmediate(t.gameObject);
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
                if (rule.prefab == null || rule.interval <= 0f) continue;
                float s = 0f;
                while (s <= totalArcLength + 1e-4f)
                {
                    float cant = (rule.followCant && curveMode == CurveMode.Parameter) ? GetCantAtS(s) : 0f;
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
                        go.hideFlags = HideFlags.DontSave;
                        spawnedPrefabs.Add(go);
                    }
#endif
                    s += rule.interval;
                }
            }
        }

        public void ClearSpawnedPrefabs()
        {
            if (spawnedPrefabs == null) spawnedPrefabs = new List<GameObject>();
            foreach (var go in spawnedPrefabs)
                if (go != null) DestroyImmediate(go);
            spawnedPrefabs.Clear();
        }
    }
}
