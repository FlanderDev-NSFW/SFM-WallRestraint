using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using WallRestraint.Logic;
using NVector3 = System.Numerics.Vector3;

namespace WallRestraint
{
    /// <summary>
    /// The visible restraints: six groups (collar, back rest, arm cuff per side, leg tube per side), each holding its model
    /// parts with vertices relative to the group's anchor (collar ring centre, rest base, arm ring centre, tube mouth), so a
    /// group can be put on its body point and scaled to its limb (<see cref="PoseLayout"/>).
    /// </summary>
    internal sealed class WallRig
    {
        private readonly WallModel _model;
        private GameObject _root;
        private readonly Dictionary<(PartKind, int), Transform> _groups = new Dictionary<(PartKind, int), Transform>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>(); // meshes and materials

        public WallRig(WallModel model) { _model = model; }

        public bool Built => _root != null;

        /// <summary>The model point a group hangs on. Side 0 = model y &lt; 0 = her left, 1 = her right, 2 = centre.</summary>
        private NVector3 Anchor(PartKind kind, int side) => kind switch
        {
            PartKind.Collar => _model.CollarCenter,
            PartKind.Arch => new NVector3(0, 0, (_model.ArchBottom + _model.ArchTop) / 2f),
            PartKind.ArmCuff => _model.Arms[side].Center,
            _ => _model.Legs[side].Mouth,
        };

        /// <summary>Make the restraint's objects: under <paramref name="parent"/> (a placed one: goes with its map), else on its own
        /// across maps.</summary>
        public void Build(int layer, Transform parent = null)
        {
            Destroy();
            _root = new GameObject("WallRestraint");
            if (parent != null) _root.transform.SetParent(parent, false);
            else UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.layer = layer;
            Material source = FindHandcuffMaterial();
            foreach (var part in _model.Parts)
            {
                var key = (part.Kind, part.Side);
                if (!_groups.TryGetValue(key, out Transform group))
                {
                    var g = new GameObject(part.Kind + (part.Side == 2 ? "" : part.Side == 0 ? "L" : "R"));
                    g.layer = layer;
                    g.transform.SetParent(_root.transform, false);
                    group = g.transform;
                    _groups[key] = group;
                }
                NVector3 anchor = Anchor(part.Kind, part.Side);
                var go = new GameObject(part.Leather ? "Leather" : "Metal");
                go.layer = layer;
                go.transform.SetParent(group, false);
                var mesh = new Mesh { name = "WallRestraint." + group.name + go.name };
                if (part.Positions.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var v = new Vector3[part.Positions.Length];
                var n = new Vector3[part.Normals.Length];
                for (int i = 0; i < v.Length; i++) v[i] = WallPlacer.U(part.Positions[i] - anchor);
                for (int i = 0; i < n.Length; i++) n[i] = WallPlacer.U(part.Normals[i]);
                mesh.vertices = v;
                mesh.normals = n;
                mesh.triangles = part.Indices;
                mesh.RecalculateBounds();
                _owned.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = MakeMaterial(source, part);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            // The holes: the level's wall cannot be cut (static-batched, unreadable meshes), so a dark disc inside each tube at
            // the wall surface stands in for the hole behind the leg.
            for (int side = 0; side < 2; side++)
                if (_groups.TryGetValue((PartKind.LegTube, side), out Transform tube))
                    AddHole(tube, _model.Legs[side], layer, source);
            _root.SetActive(false);
        }

        private void AddHole(Transform tube, LegAnchor leg, int layer, Material source)
        {
            const int segments = 32;
            var v = new Vector3[segments + 1];
            var n = new Vector3[segments + 1];
            var tris = new int[segments * 3];
            float x = -leg.Mouth.X + 0.003f; // the wall surface (model x = 0) in the tube's frame, a hair in front of it
            v[0] = new Vector3(x, 0, 0);
            for (int i = 0; i <= segments; i++) n[Math.Min(i, segments)] = Vector3.right;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                v[i + 1] = new Vector3(x, Mathf.Cos(a) * leg.InnerRadius, Mathf.Sin(a) * leg.InnerRadius);
                // Facing +x (out of the wall): with y = up x normal, (centre, i, i+1) runs counter-clockwise seen from +x.
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segments + 1;
            }
            var mesh = new Mesh { name = "WallRestraint.Hole", vertices = v, normals = n, triangles = tris };
            mesh.RecalculateBounds();
            _owned.Add(mesh);
            var go = new GameObject("Hole");
            go.layer = layer;
            go.transform.SetParent(tube, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var hole = new ModelPart { Kind = PartKind.LegTube, Leather = true, Color = new NVector3(0.02f, 0.015f, 0.015f) };
            r.sharedMaterial = MakeMaterial(source, hole);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Put every group on its body point (wall coordinates of <paramref name="frame"/>, which has scale 1).</summary>
        public void Place(WallFrame frame, PoseLayout layout)
        {
            if (_root == null) return;
            var q = frame.Rotation();
            var rot = new Quaternion(q.X, q.Y, q.Z, q.W);
            Set(PartKind.Collar, 2, frame, rot, layout.CollarAt, layout.CollarScale);
            Set(PartKind.Arch, 2, frame, rot, layout.ArchAt, layout.ArchScale);
            Set(PartKind.ArmCuff, 0, frame, rot, layout.ArmAtL, layout.ArmScale);
            Set(PartKind.ArmCuff, 1, frame, rot, layout.ArmAtR, layout.ArmScale);
            Set(PartKind.LegTube, 0, frame, rot, layout.LegAtL, layout.LegScale);
            Set(PartKind.LegTube, 1, frame, rot, layout.LegAtR, layout.LegScale);
        }

        private void Set(PartKind kind, int side, WallFrame frame, Quaternion rot, NVector3 at, float scale)
        {
            if (!_groups.TryGetValue((kind, side), out Transform t)) return;
            t.SetPositionAndRotation(WallPlacer.U(frame.Point(at)), rot);
            t.localScale = Vector3.one * scale;
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        /// <summary>The game's handcuff material (UnityChan toon shader), so the restraints are lit and outlined like the game's own items.</summary>
        private static Material FindHandcuffMaterial()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>());
                foreach (var o in all)
                {
                    var mat = o.TryCast<Material>();
                    if (mat != null && mat.name != null && mat.name.StartsWith("tejyou", StringComparison.Ordinal)) return mat;
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("handcuff material lookup failed: " + e.Message); }
            Plugin.Logger.LogWarning("handcuff material not found; using a toon/standard shader");
            return null;
        }

        private Material MakeMaterial(Material source, ModelPart part)
        {
            Material mat;
            if (source != null) mat = new Material(source);
            else
            {
                Shader shader = Shader.Find("UnityChanToonShader/Toon_DoubleShadeWithFeather");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
            }
            mat.name = "WallRestraint." + (part.Leather ? "Leather" : "Metal");
            Texture white = Texture2D.whiteTexture;
            foreach (string tex in new[] { "_MainTex", "_BaseMap", "_1st_ShadeMap", "_2nd_ShadeMap" })
                if (mat.HasProperty(tex)) mat.SetTexture(tex, white);
            // The model's metal is near-black chrome (0.14); toon shading has no reflections, so lift it to a dark gunmetal.
            var c = part.Leather ? part.Color : new NVector3(Plugin.MetalShade.Value);
            Color baseColor = new Color(c.X, c.Y, c.Z, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_1st_ShadeColor")) mat.SetColor("_1st_ShadeColor", baseColor * 0.62f);
            if (mat.HasProperty("_2nd_ShadeColor")) mat.SetColor("_2nd_ShadeColor", baseColor * 0.38f);
            _owned.Add(mat);
            return mat;
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _groups.Clear();
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
        }
    }
}
