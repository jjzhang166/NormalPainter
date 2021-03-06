using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif


namespace UTJ.NormalPainter
{
    public enum EditMode
    {
        Select,
        Brush,
        Assign,
        Move,
        Rotate,
        Scale,
        Smooth,
        Projection,
        Reset,
    }

    public enum BrushMode
    {
        Paint,
        Replace,
        Smooth,
        Reset,
    }

    public enum SelectMode
    {
        Single,
        Rect,
        Lasso,
        Brush,
    }

    public enum MirrorMode
    {
        None,
        RightToLeft,
        LeftToRight,
        ForwardToBack,
        BackToForward,
        UpToDown,
        DownToUp,
    }

    public enum TangentsUpdateMode
    {
        Manual,
        Auto,
        Realtime,
    }
    public enum TangentsPrecision
    {
        Fast,
        Precise,
    }

    public enum ImageFormat
    {
        PNG,
        EXR,
    }

    public enum ModelOverlay
    {
        None,
        LocalSpaceNormals,
        TangentSpaceNormals,
        VertexColor,
    }

    public enum Coordinate
    {
        World,
        Local,
        Pivot,
    }

    public enum SceneGUIState
    {
        Repaint = 1 << 0,
        SelectionChanged = 1 << 1,
    }



    public partial class NormalPainter : MonoBehaviour
    {
#if UNITY_EDITOR

        public Vector3 ToWorldVector(Vector3 v, Coordinate c)
        {
            switch(c)
            {
                case Coordinate.Local: return GetComponent<Transform>().localToWorldMatrix.MultiplyVector(v);
                case Coordinate.Pivot: return m_settings.pivotRot * v;
            }
            return v;
        }


        public void ApplyAssign(Vector3 v, Coordinate c, bool pushUndo)
        {
            v = ToWorldVector(v, c).normalized;

            npAssign(ref m_npModelData, v);
            UpdateNormals();
            if (pushUndo) PushUndo();
        }

        public void ApplyMove(Vector3 v, Coordinate c, bool pushUndo)
        {
            v = ToWorldVector(v, c);

            npMove(ref m_npModelData, v);
            UpdateNormals();
            if (pushUndo) PushUndo();
        }

        public void ApplyRotate(Quaternion amount, Quaternion pivotRot, Coordinate c, bool pushUndo)
        {
            var backup = m_npModelData.transform;
            var t = GetComponent<Transform>();
            switch (c)
            {
                case Coordinate.World:
                    m_npModelData.transform = t.localToWorldMatrix;
                    pivotRot = Quaternion.identity;
                    break;
                case Coordinate.Local:
                    m_npModelData.transform = Matrix4x4.identity;
                    pivotRot = Quaternion.identity;
                    break;
                case Coordinate.Pivot:
                    m_npModelData.transform = t.localToWorldMatrix;
                    break;
                default: return;
            }

            npRotate(ref m_npModelData, amount, pivotRot);
            m_npModelData.transform = backup;

            UpdateNormals();
            if (pushUndo) PushUndo();
        }

        public void ApplyRotatePivot(Quaternion amount, Vector3 pivotPos, Quaternion pivotRot, Coordinate c, bool pushUndo)
        {
            var backup = m_npModelData.transform;
            var t = GetComponent<Transform>();
            switch (c)
            {
                case Coordinate.World:
                    m_npModelData.transform = t.localToWorldMatrix;
                    pivotRot = Quaternion.identity;
                    break;
                case Coordinate.Local:
                    m_npModelData.transform = Matrix4x4.identity;
                    pivotPos = t.worldToLocalMatrix.MultiplyPoint(pivotPos);
                    pivotRot = Quaternion.identity;
                    break;
                case Coordinate.Pivot:
                    m_npModelData.transform = t.localToWorldMatrix;
                    break;
                default: return;
            }

            npRotatePivot(ref m_npModelData, amount, pivotPos, pivotRot);
            m_npModelData.transform = backup;

            UpdateNormals();
            if (pushUndo) PushUndo();
        }

        public void ApplyScale(Vector3 amount, Vector3 pivotPos, Quaternion pivotRot, Coordinate c, bool pushUndo)
        {
            var backup = m_npModelData.transform;
            var t = GetComponent<Transform>();
            switch (c)
            {
                case Coordinate.World:
                    m_npModelData.transform = t.localToWorldMatrix;
                    pivotRot = Quaternion.identity;
                    break;
                case Coordinate.Local:
                    m_npModelData.transform = Matrix4x4.identity;
                    pivotPos = t.worldToLocalMatrix.MultiplyPoint(pivotPos);
                    pivotRot = Quaternion.identity;
                    break;
                case Coordinate.Pivot:
                    m_npModelData.transform = t.localToWorldMatrix;
                    break;
                default: return;
            }

            npScale(ref m_npModelData, amount, pivotPos, pivotRot);
            m_npModelData.transform = backup;

            UpdateNormals();
            if (pushUndo) PushUndo();
        }

        public bool ApplyPaintBrush(bool useSelection, Vector3 pos, float radius, float strength, float[] bsamples, Vector3 baseDir, int blendMode)
        {
            if (npBrushPaint(ref m_npModelData, pos, radius, strength, bsamples.Length, bsamples, baseDir, blendMode, useSelection) > 0)
            {
                UpdateNormals();
                return true;
            }
            return false;
        }

        public bool ApplyReplaceBrush(bool useSelection, Vector3 pos, float radius, float strength, float[] bsamples, Vector3 amount)
        {
            amount = GetComponent<Transform>().worldToLocalMatrix.MultiplyVector(amount).normalized;

            if (npBrushReplace(ref m_npModelData, pos, radius, strength, bsamples.Length, bsamples, amount, useSelection) > 0)
            {
                UpdateNormals();
                return true;
            }
            return false;
        }

        public bool ApplySmoothBrush(bool useSelection, Vector3 pos, float radius, float strength, float[] bsamples)
        {
            if (npBrushSmooth(ref m_npModelData, pos, radius, strength, bsamples.Length, bsamples, useSelection) > 0)
            {
                UpdateNormals();
                return true;
            }
            return false;
        }

        public bool ApplyResetBrush(bool useSelection, Vector3 pos, float radius, float strength, float[] bsamples)
        {
            if (npBrushLerp(ref m_npModelData, pos, radius, strength, bsamples.Length, bsamples, m_normalsBase, m_normals, useSelection) > 0)
            {
                UpdateNormals();
                return true;
            }
            return false;
        }

        public void ResetNormals(bool useSelection, bool pushUndo)
        {
            if (!useSelection)
            {
                Array.Copy(m_normalsBase, m_normals, m_normals.Length);
            }
            else
            {
                for (int i = 0; i < m_normals.Length; ++i)
                    m_normals[i] = Vector3.Lerp(m_normals[i], m_normalsBase[i], m_selection[i]).normalized;
            }
            UpdateNormals();
            if (pushUndo) PushUndo();
        }

        void PushUndo()
        {
            PushUndo(m_normals, null);
        }

        void PushUndo(Vector3[] normals, History.Record[] records)
        {
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(this, "NormalEditor [" + m_history.index + "]");
            m_historyIndex = ++m_history.index;

            if (normals == null)
            {
                m_history.normals = null;
            }
            else
            {
                if (m_history.normals != null && m_history.normals.Length == normals.Length)
                    Array.Copy(normals, m_history.normals, normals.Length);
                else
                    m_history.normals = (Vector3[])normals.Clone();

                if (m_settings.tangentsMode == TangentsUpdateMode.Auto)
                    RecalculateTangents();
            }

            m_history.records = records != null ? (History.Record[])records.Clone() : null;

            Undo.FlushUndoRecordObjects();
        }

        public void OnUndoRedo()
        {
            if (m_historyIndex != m_history.index)
            {
                m_historyIndex = m_history.index;

                UpdateTransform();
                if (m_history.normals != null && m_normals != null && m_history.normals.Length == m_normals.Length)
                {
                    Array.Copy(m_history.normals, m_normals, m_normals.Length);
                    UpdateNormals(false);

                    if (m_settings.tangentsMode == TangentsUpdateMode.Auto)
                        RecalculateTangents();
                }

                if (m_history.records != null)
                {
                    foreach (var r in m_history.records)
                    {
                        if (r.normals != null) r.mesh.normals = r.normals;
                        if (r.colors != null) r.mesh.colors = r.colors;
                        r.mesh.UploadMeshData(false);
                    }
                }
            }
        }


        bool UpdateBoneMatrices()
        {
            bool ret = false;

            var rootMatrix = GetComponent<Transform>().localToWorldMatrix;
            if (m_npSkinData.root != rootMatrix)
            {
                m_npSkinData.root = rootMatrix;
                ret = true;
            }

            var bones = GetComponent<SkinnedMeshRenderer>().bones;
            for (int i = 0; i < m_boneMatrices.Length; ++i)
            {
                var l2w = bones[i].localToWorldMatrix;
                if (m_boneMatrices[i] != l2w)
                {
                    m_boneMatrices[i] = l2w;
                    ret = true;
                }
            }
            return ret;
        }

        void UpdateTransform()
        {
            m_npModelData.transform = GetComponent<Transform>().localToWorldMatrix;

            if (m_skinned && UpdateBoneMatrices())
            {
                npApplySkinning(ref m_npSkinData,
                    m_pointsPredeformed, m_normalsPredeformed, m_tangentsPredeformed,
                    m_points, m_normals, m_tangents);
                npApplySkinning(ref m_npSkinData,
                    null, m_normalsBasePredeformed, m_tangentsBasePredeformed,
                    null, m_normalsBase, m_tangentsBase);

                if (m_cbPoints != null) m_cbPoints.SetData(m_points);
                if (m_cbNormals != null) m_cbNormals.SetData(m_normals);
                if (m_cbBaseNormals != null) m_cbBaseNormals.SetData(m_normalsBase);
                if (m_cbTangents != null) m_cbTangents.SetData(m_tangents);
                if (m_cbBaseTangents != null) m_cbBaseTangents.SetData(m_tangentsBase);
            }
        }

        public void UpdateNormals(bool mirror = true)
        {
            if (m_meshTarget == null) return;

            if (m_skinned)
            {
                UpdateBoneMatrices();
                npApplyReverseSkinning(ref m_npSkinData,
                    null, m_normals, null,
                    null, m_normalsPredeformed, null);
                if (mirror)
                {
                    ApplyMirroringInternal();
                    npApplySkinning(ref m_npSkinData,
                        null, m_normalsPredeformed, null,
                        null, m_normals, null);
                }
                m_meshTarget.SetNormals(m_normalsPredeformed);
            }
            else
            {
                if (mirror)
                    ApplyMirroringInternal();
                m_meshTarget.SetNormals(m_normals);
            }

            if (m_settings.tangentsMode == TangentsUpdateMode.Realtime)
                RecalculateTangents();

            m_meshTarget.UploadMeshData(false);
            if (m_cbNormals != null)
                m_cbNormals.SetData(m_normals);
        }

        public void UpdateSelection()
        {
            int prevSelected = m_numSelected;

            Matrix4x4 trans = GetComponent<Transform>().localToWorldMatrix;
            m_numSelected = npUpdateSelection(ref m_npModelData, ref m_selectionPos, ref m_selectionNormal);

            m_selectionRot = Quaternion.identity;
            if (m_numSelected > 0)
            {
                m_selectionRot = Quaternion.LookRotation(m_selectionNormal);
                m_settings.pivotPos = m_selectionPos;
                m_settings.pivotRot = m_selectionRot;
            }

            if(prevSelected == 0 && m_numSelected == 0)
            {
                // no need to upload
            }
            else
            {
                m_cbSelection.SetData(m_selection);
            }
        }

        public void RecalculateTangents()
        {
            RecalculateTangents(m_settings.tangentsPrecision);
        }
        public void RecalculateTangents(TangentsPrecision precision)
        {
            if (precision == TangentsPrecision.Precise)
            {
                m_meshTarget.RecalculateTangents();
                m_tangentsPredeformed.LockList(l => {
                    m_meshTarget.GetTangents(l);
                });

                if (m_skinned)
                    npApplySkinning(ref m_npSkinData,
                        null, null, m_tangentsPredeformed,
                        null, null, m_tangents);
            }
            else
            {
                if (m_skinned)
                {
                    npModelData tmp = m_npModelData;
                    tmp.vertices = m_pointsPredeformed;
                    tmp.normals = m_normalsPredeformed;
                    npGenerateTangents(ref tmp, m_tangentsPredeformed);
                    npApplySkinning(ref m_npSkinData,
                        null, null, m_tangentsPredeformed,
                        null, null, m_tangents);
                }
                else
                {
                    npGenerateTangents(ref m_npModelData, m_tangents);
                }
                m_meshTarget.SetTangents(m_tangentsPredeformed);
            }

            if (m_cbTangents != null)
                m_cbTangents.SetData(m_tangents);
        }


        public bool Raycast(Event e, ref Vector3 pos, ref int ti)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            float d = 0.0f;
            if(Raycast(ray, ref ti, ref d))
            {
                pos = ray.origin + ray.direction * d;
                return true;
            }
            return false;
        }

        public bool Raycast(Ray ray, ref int ti, ref float distance)
        {
            bool ret = npRaycast(ref m_npModelData, ray.origin, ray.direction, ref ti, ref distance) > 0;
            return ret;
        }

        public Vector3 PickNormal(Vector3 pos, int ti)
        {
            return npPickNormal(ref m_npModelData, pos, ti);
        }

        public Vector3 PickBaseNormal(Vector3 pos, int ti)
        {
            m_npModelData.normals = m_normalsBase;
            var ret = npPickNormal(ref m_npModelData, pos, ti);
            m_npModelData.normals = m_normals;
            return ret;
        }


        public bool SelectEdge(float strength, bool clear)
        {
            bool mask = m_numSelected > 0;
            return npSelectEdge(ref m_npModelData, strength, clear, mask) > 0;
        }
        public bool SelectHole(float strength, bool clear)
        {
            bool mask = m_numSelected > 0;
            return npSelectHole(ref m_npModelData, strength, clear, mask) > 0;
        }

        public bool SelectConnected(float strength, bool clear)
        {
            if (m_numSelected == 0)
                return SelectAll();
            else
                return npSelectConnected(ref m_npModelData, strength, clear) > 0;
        }

        public bool SelectAll()
        {
            for (int i = 0; i < m_selection.Length; ++i)
                m_selection[i] = 1.0f;
            return m_selection.Length > 0;
        }

        public bool InvertSelection()
        {
            for (int i = 0; i < m_selection.Length; ++i)
                m_selection[i] = 1.0f - m_selection[i];
            return m_selection.Length > 0;
        }

        public bool ClearSelection()
        {
            System.Array.Clear(m_selection, 0, m_selection.Length);
            return m_selection.Length > 0;
        }

        public static Vector2 ScreenCoord11(Vector2 v)
        {
            var cam = SceneView.lastActiveSceneView.camera;
            var pixelRect = cam.pixelRect;
            var rect = cam.rect;
            return new Vector2(
                    v.x / pixelRect.width * rect.width * 2.0f - 1.0f,
                    (v.y / pixelRect.height * rect.height * 2.0f - 1.0f) * -1.0f);
        }

        public bool SelectVertex(Event e, float strength, bool frontFaceOnly)
        {
            var center = e.mousePosition;
            var size = new Vector2(15.0f, 15.0f);
            var r1 = center - size;
            var r2 = center + size;
            return SelectVertex(r1, r2, strength, frontFaceOnly);
        }
        public bool SelectVertex(Vector2 r1, Vector2 r2, float strength, bool frontFaceOnly)
        {
            var cam = SceneView.lastActiveSceneView.camera;
            if (cam == null) { return false; }

            var campos = cam.GetComponent<Transform>().position;
            var trans = GetComponent<Transform>().localToWorldMatrix;
            var mvp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false) * cam.worldToCameraMatrix * trans;
            r1 = ScreenCoord11(r1);
            r2 = ScreenCoord11(r2);
            var rmin = new Vector2(Math.Min(r1.x, r2.x), Math.Min(r1.y, r2.y));
            var rmax = new Vector2(Math.Max(r1.x, r2.x), Math.Max(r1.y, r2.y));

            return npSelectSingle(ref m_npModelData, ref mvp, rmin, rmax, campos, strength, frontFaceOnly) > 0;
        }

        public bool SelectTriangle(Event e, float strength)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            return SelectTriangle(ray, strength);
        }
        public bool SelectTriangle(Ray ray, float strength)
        {
            Matrix4x4 trans = GetComponent<Transform>().localToWorldMatrix;
            return npSelectTriangle(ref m_npModelData, ray.origin, ray.direction, strength) > 0;
        }


        public bool SelectRect(Vector2 r1, Vector2 r2, float strength, bool frontFaceOnly)
        {
            var cam = SceneView.lastActiveSceneView.camera;
            if (cam == null) { return false; }

            var campos = cam.GetComponent<Transform>().position;
            var trans = GetComponent<Transform>().localToWorldMatrix;
            var mvp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false) * cam.worldToCameraMatrix * trans;
            r1 = ScreenCoord11(r1);
            r2 = ScreenCoord11(r2);
            var rmin = new Vector2(Math.Min(r1.x, r2.x), Math.Min(r1.y, r2.y));
            var rmax = new Vector2(Math.Max(r1.x, r2.x), Math.Max(r1.y, r2.y));

            return npSelectRect(ref m_npModelData,
                ref mvp, rmin, rmax, campos, strength, frontFaceOnly) > 0;
        }

        public bool SelectLasso(Vector2[] lasso, float strength, bool frontFaceOnly)
        {
            var cam = SceneView.lastActiveSceneView.camera;
            if (cam == null) { return false; }

            var campos = cam.GetComponent<Transform>().position;
            var trans = GetComponent<Transform>().localToWorldMatrix;
            var mvp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false) * cam.worldToCameraMatrix * trans;

            return npSelectLasso(ref m_npModelData,
                ref mvp, lasso, lasso.Length, campos, strength, frontFaceOnly) > 0;
        }

        public bool SelectBrush(Vector3 pos, float radius, float strength, float[] bsamples)
        {
            Matrix4x4 trans = GetComponent<Transform>().localToWorldMatrix;
            return npSelectBrush(ref m_npModelData, pos, radius, strength, bsamples.Length, bsamples) > 0;
        }

        public static Vector3 GetMirrorPlane(MirrorMode mirrorMode)
        {
            switch (mirrorMode)
            {
                case MirrorMode.RightToLeft:    return Vector3.left;
                case MirrorMode.LeftToRight:    return Vector3.right;
                case MirrorMode.ForwardToBack:  return Vector3.back;
                case MirrorMode.BackToForward:  return Vector3.forward;
                case MirrorMode.UpToDown:       return Vector3.down;
                case MirrorMode.DownToUp:       return Vector3.up;
            }
            return Vector3.up;
        }

        MirrorMode m_prevMirrorMode;

        bool ApplyMirroringInternal()
        {
            if (m_settings.mirrorMode == MirrorMode.None) return false;

            bool needsSetup = false;
            if (m_mirrorRelation == null || m_mirrorRelation.Length != m_points.Length)
            {
                m_mirrorRelation = new PinnedList<int>(m_points.Length);
                needsSetup = true;
            }
            else if(m_prevMirrorMode != m_settings.mirrorMode)
            {
                m_prevMirrorMode = m_settings.mirrorMode;
                needsSetup = true;
            }

            Vector3 planeNormal = GetMirrorPlane(m_settings.mirrorMode);
            if (needsSetup)
            {
                npModelData tmp = m_npModelData;
                tmp.vertices = m_pointsPredeformed;
                tmp.normals = m_normalsBasePredeformed;
                if (npBuildMirroringRelation(ref tmp, planeNormal, 0.0001f, m_mirrorRelation) == 0)
                {
                    Debug.LogWarning("NormalEditor: this mesh seems not symmetric");
                    m_mirrorRelation = null;
                    m_settings.mirrorMode = MirrorMode.None;
                    return false;
                }
            }

            npApplyMirroring(m_normals.Length, m_mirrorRelation, planeNormal, m_normalsPredeformed);
            return true;
        }
        public bool ApplyMirroring(bool pushUndo)
        {
            ApplyMirroringInternal();
            UpdateNormals();
            if (pushUndo) PushUndo();
            return true;
        }


        static Rect FromToRect(Vector2 start, Vector2 end)
        {
            Rect r = new Rect(start.x, start.y, end.x - start.x, end.y - start.y);
            if (r.width < 0)
            {
                r.x += r.width;
                r.width = -r.width;
            }
            if (r.height < 0)
            {
                r.y += r.height;
                r.height = -r.height;
            }
            return r;
        }

        public bool BakeToTexture(int width, int height, string path)
        {
            if (path == null || path.Length == 0)
                return false;

            m_matBake.SetBuffer("_BaseNormals", m_cbBaseNormals);
            m_matBake.SetBuffer("_BaseTangents", m_cbBaseTangents);

            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false);
            rt.Create();

            m_cmdDraw.Clear();
            m_cmdDraw.SetRenderTarget(rt);
            for (int si = 0; si < m_meshTarget.subMeshCount; ++si)
                m_cmdDraw.DrawMesh(m_meshTarget, Matrix4x4.identity, m_matBake, si, 0);
            Graphics.ExecuteCommandBuffer(m_cmdDraw);

            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
            tex.Apply();
            RenderTexture.active = null;

            if (path.EndsWith(".png"))
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            else
                System.IO.File.WriteAllBytes(path, tex.EncodeToEXR());


            DestroyImmediate(tex);
            DestroyImmediate(rt);

            return true;
        }

        public bool BakeToVertexColor(bool pushUndo)
        {
            if (pushUndo)
            {
                var record = new History.Record { mesh = m_meshTarget, colors = m_meshTarget.colors };
                PushUndo(null, new History.Record[1] { record });
            }

            var colors = new Color[m_normals.Length];
            for (int i = 0; i < m_normals.Length; ++i)
            {
                colors[i].r = m_normals[i].x * 0.5f + 0.5f;
                colors[i].g = m_normals[i].y * 0.5f + 0.5f;
                colors[i].b = m_normals[i].z * 0.5f + 0.5f;
                colors[i].a = 1.0f;
            }
            m_meshTarget.colors = colors;

            if (pushUndo)
            {
                var record = new History.Record { mesh = m_meshTarget, colors = colors };
                PushUndo(null, new History.Record[1] { record });
            }
            return true;
        }

        public bool LoadTexture(Texture tex, bool pushUndo)
        {
            if (tex == null)
                return false;

            bool packed = false;
            {
                var path = AssetDatabase.GetAssetPath(tex);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                    packed = importer.textureType == TextureImporterType.NormalMap;
            }

            var cbUV = new ComputeBuffer(m_normals.Length, 8);
            cbUV.SetData(m_meshTarget.uv);

            m_csBakeFromMap.SetInt("_Packed", packed ? 1 : 0);
            m_csBakeFromMap.SetTexture(0, "_NormalMap", tex);
            m_csBakeFromMap.SetBuffer(0, "_UV", cbUV);
            m_csBakeFromMap.SetBuffer(0, "_Normals", m_cbBaseNormals);
            m_csBakeFromMap.SetBuffer(0, "_Tangents", m_cbBaseTangents);
            m_csBakeFromMap.SetBuffer(0, "_Dst", m_cbNormals);
            m_csBakeFromMap.Dispatch(0, m_normals.Length, 1, 1);

            m_cbNormals.GetData(m_normals);
            cbUV.Dispose();

            UpdateNormals();
            if (pushUndo) PushUndo();

            return true;
        }

        public bool LoadVertexColor(bool pushUndo)
        {
            var color = m_meshTarget.colors;
            if (color.Length != m_normals.Length)
                return false;

            for (int i = 0; i < color.Length; ++i)
            {
                var c = color[i];
                m_normals[i] = new Vector3(
                    c.r * 2.0f - 1.0f,
                    c.g * 2.0f - 1.0f,
                    c.b * 2.0f - 1.0f);
            }
            UpdateNormals();
            if (pushUndo) PushUndo();
            return true;
        }

        public void ApplySmoothing(float radius, float strength, bool pushUndo)
        {
            bool mask = m_numSelected > 0;
            npSmooth(ref m_npModelData, radius, strength, mask);

            UpdateNormals();
            if (pushUndo) PushUndo();
        }

        public bool ApplyWelding(bool smoothing, float weldAngle, bool pushUndo)
        {
            bool mask = m_numSelected > 0;
            if (npWeld(ref m_npModelData, smoothing, weldAngle, mask) > 0)
            {
                UpdateNormals();
                if (pushUndo) PushUndo();
                return true;
            }
            return false;
        }


        class WeldData
        {
            public bool skinned;
            public Mesh mesh;
            public Matrix4x4 transform;
            public PinnedList<Vector3> vertices;
            public PinnedList<Vector3> normals;

            public PinnedList<BoneWeight> weights;
            public PinnedList<Matrix4x4> bones;
            public PinnedList<Matrix4x4> bindposes;
            public npSkinData skinData;
        }

        public static bool IsValidMesh(Mesh mesh)
        {
            if (!mesh || mesh.vertexCount == 0) return false;
            if (!mesh.isReadable)
            {
                Debug.LogWarning("Mesh " + mesh.name + " is not readable.");
                return false;
            }
            return true;
        }

        public bool ApplyWelding2(GameObject[] targets, int weldMode, float weldAngle, bool pushUndo)
        {
            var data = new List<WeldData>();

            // build weld data
            foreach (var t in targets)
            {
                if (!t || t == this.gameObject) { continue; }

                var smr = t.GetComponent<SkinnedMeshRenderer>();
                if (smr)
                {
                    var mesh = smr.sharedMesh;
                    if (!IsValidMesh(mesh)) continue;

                    var d = new WeldData();
                    d.skinned = true;
                    d.mesh = mesh;
                    d.transform = t.GetComponent<Transform>().localToWorldMatrix;
                    d.vertices = new PinnedList<Vector3>(mesh.vertices);
                    d.normals = new PinnedList<Vector3>(mesh.normals);

                    d.weights = new PinnedList<BoneWeight>(mesh.boneWeights);
                    d.bindposes = new PinnedList<Matrix4x4>(mesh.bindposes);
                    d.bones = new PinnedList<Matrix4x4>(d.bindposes.Length);

                    var bones = smr.bones;
                    int n = System.Math.Min(d.bindposes.Length, smr.bones.Length);
                    for (int bi = 0; bi < n; ++bi)
                        d.bones[bi] = smr.bones[bi].localToWorldMatrix;

                    d.skinData.num_vertices = d.vertices.Length;
                    d.skinData.num_bones = d.bindposes.Length;
                    d.skinData.weights = d.weights;
                    d.skinData.bindposes = d.bindposes;
                    d.skinData.bones = d.bones;
                    d.skinData.root = d.transform;

                    npApplySkinning(ref d.skinData,
                        d.vertices, d.normals, null,
                        d.vertices, d.normals, null);

                    data.Add(d);
                }

                var mr = t.GetComponent<MeshRenderer>();
                if (mr)
                {
                    var mesh = t.GetComponent<MeshFilter>().sharedMesh;
                    if (!IsValidMesh(mesh)) continue;

                    var d = new WeldData();
                    d.mesh = mesh;
                    d.transform = t.GetComponent<Transform>().localToWorldMatrix;
                    d.vertices = new PinnedList<Vector3>(mesh.vertices);
                    d.normals = new PinnedList<Vector3>(mesh.normals);
                    data.Add(d);
                }
            }

            if(data.Count == 0)
            {
                Debug.LogWarning("Nothing to weld.");
                return false;
            }

            // create data to pass to C++
            bool mask = m_numSelected > 0;
            npModelData[] tdata = new npModelData[data.Count];
            for (int i = 0; i < data.Count; ++i)
            {
                tdata[i].num_vertices = data[i].vertices.Length;
                tdata[i].vertices = data[i].vertices;
                tdata[i].normals = data[i].normals;
                tdata[i].transform = data[i].transform;
            }

            Vector3[] normalsPreWeld = null;
            if (pushUndo)
                normalsPreWeld = m_normals.Clone();

            // do weld
            bool ret = false;
            if (npWeld2(ref m_npModelData, tdata.Length, tdata, weldMode, weldAngle, mask) > 0)
            {
                // data to undo
                History.Record[] records = null;

                if (pushUndo && (weldMode == 0 || weldMode == 2))
                {
                    records = new History.Record[data.Count];

                    for (int i = 0; i < data.Count; ++i)
                    {
                        records[i] = new History.Record();
                        records[i].mesh = data[i].mesh;
                        records[i].normals = data[i].mesh.normals;
                    }
                    PushUndo(normalsPreWeld, records);
                }

                // update normals
                if (weldMode == 1 || weldMode == 2)
                {
                    UpdateNormals();
                }
                if (weldMode == 0 || weldMode == 2)
                {
                    foreach (var d in data)
                    {
                        if (d.skinned)
                        {
                            npApplyReverseSkinning(ref d.skinData,
                                null, d.normals, null,
                                null, d.normals, null);
                        }
                        d.mesh.normals = d.normals;
                        d.mesh.UploadMeshData(false);
                    }

                    if (pushUndo)
                    {
                        for (int i = 0; i < data.Count; ++i)
                        {
                            records[i] = new History.Record();
                            records[i].mesh = data[i].mesh;
                            records[i].normals = data[i].normals;
                        }
                    }
                }

                if (pushUndo)
                    PushUndo(m_normals, records);

                ret = true;
            }

            return ret;
        }

        public void ApplyProjection(GameObject go, bool baseNormalsAsRayDirection, bool pushUndo)
        {
            if (go == null) { return; }

            Mesh mesh = null;
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null)
                    mesh = mf.sharedMesh;
                else
                {
                    var smi = go.GetComponent<SkinnedMeshRenderer>();
                    if (smi != null)
                    {
                        mesh = new Mesh();
                        smi.BakeMesh(mesh);
                    }
                }
            }
            if (mesh == null)
            {
                Debug.LogWarning("ProjectNormals(): projector has no mesh!");
                return;
            }

            var ptrans = go.GetComponent<Transform>().localToWorldMatrix;
            ApplyProjection(mesh, ptrans, baseNormalsAsRayDirection, pushUndo);
        }

        public void ApplyProjection(Mesh target, Matrix4x4 ttrans, bool baseNormalsAsRayDirection, bool pushUndo)
        {
            if (!IsValidMesh(target)) { return; }

            var tpoints = new PinnedList<Vector3>(target.vertices);
            var tnormals = new PinnedList<Vector3>(target.normals);
            var tindices = new PinnedList<int>(target.triangles);

            var tdata = new npModelData();
            tdata.transform = ttrans;
            tdata.num_vertices = tpoints.Length;
            tdata.num_triangles = tindices.Length / 3;
            tdata.vertices = tpoints;
            tdata.normals = tnormals;
            tdata.indices = tindices;

            var rayDirections = baseNormalsAsRayDirection ? m_normalsBase : m_normals;
            bool mask = m_numSelected > 0;
            npProjectNormals(ref m_npModelData, ref tdata, rayDirections, mask);

            UpdateNormals();
            if (pushUndo) PushUndo();
        }

        public void ResetToBindpose(bool pushUndo)
        {
            var smr = GetComponent<SkinnedMeshRenderer>();
            if (smr == null || smr.bones == null || smr.sharedMesh == null) { return; }

            var bones = smr.bones;
            var bindposes = smr.sharedMesh.bindposes;
            var bindposeMap = new Dictionary<Transform, Matrix4x4>();

            for (int i = 0; i < bones.Length; i++)
            {
                if (!bindposeMap.ContainsKey(bones[i]))
                {
                    bindposeMap.Add(bones[i], bindposes[i]);
                }
            }

            if (pushUndo)
                Undo.RecordObjects(bones, "NormalPainter: ResetToBindpose");

            foreach (var kvp in bindposeMap)
            {
                var bone = kvp.Key;
                var imatrix = kvp.Value;
                var localMatrix =
                    bindposeMap.ContainsKey(bone.parent) ? (imatrix * bindposeMap[bone.parent].inverse).inverse : imatrix.inverse;

                bone.localPosition = localMatrix.MultiplyPoint(Vector3.zero);
                bone.localRotation = Quaternion.LookRotation(localMatrix.GetColumn(2), localMatrix.GetColumn(1));
                bone.localScale = new Vector3(localMatrix.GetColumn(0).magnitude, localMatrix.GetColumn(1).magnitude, localMatrix.GetColumn(2).magnitude);
            }

            if (pushUndo)
                Undo.FlushUndoRecordObjects();
        }

        public void ExportSettings(string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(Instantiate(m_settings), path);
        }


        struct npModelData
        {
            public IntPtr indices;
            public IntPtr vertices;
            public IntPtr normals;
            public IntPtr tangents;
            public IntPtr uv;
            public IntPtr selection;
            public int num_vertices;
            public int num_triangles;
            public Matrix4x4 transform;
        }
        struct npSkinData
        {
            public IntPtr weights;
            public IntPtr bones;
            public IntPtr bindposes;
            public int num_vertices;
            public int num_bones;
            public Matrix4x4 root;
        }

        [DllImport("NormalPainterCore")] static extern int npRaycast(
            ref npModelData model, Vector3 pos, Vector3 dir, ref int tindex, ref float distance);

        [DllImport("NormalPainterCore")] static extern Vector3 npPickNormal(
            ref npModelData model, Vector3 pos, int ti);

        [DllImport("NormalPainterCore")] static extern int npSelectSingle(
            ref npModelData model, ref Matrix4x4 viewproj, Vector2 rmin, Vector2 rmax, Vector3 campos, float strength, bool frontfaceOnly);

        [DllImport("NormalPainterCore")] static extern int npSelectTriangle(
            ref npModelData model, Vector3 pos, Vector3 dir, float strength);
        
        [DllImport("NormalPainterCore")] static extern int npSelectEdge(
            ref npModelData model, float strength, bool clear, bool mask);

        [DllImport("NormalPainterCore")] static extern int npSelectHole(
            ref npModelData model, float strength, bool clear, bool mask);

        [DllImport("NormalPainterCore")] static extern int npSelectConnected(
            ref npModelData model, float strength, bool clear);

        [DllImport("NormalPainterCore")] static extern int npSelectRect(
            ref npModelData model, ref Matrix4x4 viewproj, Vector2 rmin, Vector2 rmax, Vector3 campos, float strength, bool frontfaceOnly);

        [DllImport("NormalPainterCore")] static extern int npSelectLasso(
            ref npModelData model, ref Matrix4x4 viewproj, Vector2[] lasso, int numLassoPoints, Vector3 campos, float strength, bool frontfaceOnly);
        
        [DllImport("NormalPainterCore")] static extern int npSelectBrush(
            ref npModelData model,
            Vector3 pos, float radius, float strength, int num_bsamples, float[] bsamples);

        [DllImport("NormalPainterCore")] static extern int npUpdateSelection(
            ref npModelData model,
            ref Vector3 selection_pos, ref Vector3 selection_normal);


        [DllImport("NormalPainterCore")] static extern int npBrushReplace(
            ref npModelData model,
            Vector3 pos, float radius, float strength, int num_bsamples, float[] bsamples, Vector3 amount, bool mask);

        [DllImport("NormalPainterCore")] static extern int npBrushPaint(
            ref npModelData model,
            Vector3 pos, float radius, float strength, int num_bsamples, float[] bsamples, Vector3 baseNormal, int blend_mode, bool mask);

        [DllImport("NormalPainterCore")] static extern int npBrushSmooth(
            ref npModelData model,
            Vector3 pos, float radius, float strength, int num_bsamples, float[] bsamples, bool mask);

        [DllImport("NormalPainterCore")] static extern int npBrushLerp(
            ref npModelData model,
            Vector3 pos, float radius, float strength, int num_bsamples, float[] bsamples, Vector3[] baseNormals, Vector3[] normals, bool mask);

        [DllImport("NormalPainterCore")] static extern int npAssign(
            ref npModelData model, Vector3 value);
        
        [DllImport("NormalPainterCore")] static extern int npMove(
            ref npModelData model, Vector3 amount);
        
        [DllImport("NormalPainterCore")] static extern int npRotate(
            ref npModelData model, Quaternion amount, Quaternion pivotRot);

        [DllImport("NormalPainterCore")] static extern int npRotatePivot(
            ref npModelData model, Quaternion amount, Vector3 pivotPos, Quaternion pivotRot);

        [DllImport("NormalPainterCore")] static extern int npScale(
            ref npModelData model, Vector3 amount, Vector3 pivotPos, Quaternion pivotRot);

        [DllImport("NormalPainterCore")] static extern int npSmooth(
            ref npModelData model, float radius, float strength, bool mask);

        [DllImport("NormalPainterCore")] static extern int npWeld(
            ref npModelData model, bool smoothing, float weldAngle, bool mask);
        [DllImport("NormalPainterCore")] static extern int npWeld2(
            ref npModelData model, int num_targets, npModelData[] targets,
            int weldMode, float weldAngle, bool mask);

        [DllImport("NormalPainterCore")] static extern int npBuildMirroringRelation(
            ref npModelData model, Vector3 plane_normal, float epsilon, int[] relation);

        [DllImport("NormalPainterCore")] static extern void npApplyMirroring(
            int num_vertices, int[] relation, Vector3 plane_normal, Vector3[] normals);

        [DllImport("NormalPainterCore")] static extern void npProjectNormals(
            ref npModelData model, ref npModelData target, IntPtr ray_dir, bool mask);

        [DllImport("NormalPainterCore")] static extern void npApplySkinning(
            ref npSkinData skin,
            Vector3[] ipoints, Vector3[] inormals, Vector4[] itangents,
            Vector3[] opoints, Vector3[] onormals, Vector4[] otangents);

        [DllImport("NormalPainterCore")] static extern void npApplyReverseSkinning(
            ref npSkinData skin,
            Vector3[] ipoints, Vector3[] inormals, Vector4[] itangents,
            Vector3[] opoints, Vector3[] onormals, Vector4[] otangents);
        
        [DllImport("NormalPainterCore")] static extern int npGenerateNormals(
            ref npModelData model, Vector3[] dst);
        [DllImport("NormalPainterCore")] static extern int npGenerateTangents(
            ref npModelData model, Vector4[] dst);

        [DllImport("NormalPainterCore")] static extern void npInitializePenInput();
#endif
    }
}
