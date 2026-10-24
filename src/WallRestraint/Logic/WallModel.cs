using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace WallRestraint.Logic
{
    public enum PartKind : byte { Collar = 0, Arch = 1, ArmCuff = 2, LegTube = 3 }

    /// <summary>One piece of the model in wall coordinates (see <see cref="WallModel"/>).</summary>
    public sealed class ModelPart
    {
        public PartKind Kind;
        public int Side;               // 0 = model y < 0, 1 = model y > 0, 2 = centre
        public bool Leather;           // else metal
        public Vector3 Color;
        public Vector3[] Positions;
        public Vector3[] Normals;
        public int[] Indices;
    }

    public struct ArmAnchor
    {
        public Vector3 Center;         // middle of the ring (height: middle of the cuff)
        public float InnerRadius, Bottom, Top;
    }

    public struct LegAnchor
    {
        public Vector3 Mouth;          // tube axis at the room-side end
        public float InnerRadius, OuterRadius;
        public float Inside;           // how far the tube reaches behind the wall surface
    }

    /// <summary>
    /// The restraints exported by tools/export_model.py (format described there), in wall coordinates: x = out of the wall
    /// surface into the room, y = sideways, z = height above the floor (metres, Blender axes). <see cref="WallFrame"/> maps
    /// them into the world.
    /// </summary>
    public sealed class WallModel
    {
        public const uint Magic = 0x54535257; // "WRST"
        public Vector3 CollarCenter;
        public float CollarInnerRadius;
        public float ArchApexOut, ArchBottom, ArchTop, ArchHalfWidth;
        public ArmAnchor[] Arms = new ArmAnchor[2];
        public LegAnchor[] Legs = new LegAnchor[2];
        public List<ModelPart> Parts = new List<ModelPart>();

        public static WallModel Read(Stream stream)
        {
            using var r = new BinaryReader(stream);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a wall restraint model (magic)");
            uint version = r.ReadUInt32();
            if (version != 1) throw new InvalidDataException("unsupported model version " + version);
            var m = new WallModel { CollarCenter = V(r), CollarInnerRadius = r.ReadSingle() };
            m.ArchApexOut = r.ReadSingle();
            m.ArchBottom = r.ReadSingle();
            m.ArchTop = r.ReadSingle();
            m.ArchHalfWidth = r.ReadSingle();
            for (int s = 0; s < 2; s++)
                m.Arms[s] = new ArmAnchor { Center = V(r), InnerRadius = r.ReadSingle(), Bottom = r.ReadSingle(), Top = r.ReadSingle() };
            for (int s = 0; s < 2; s++)
                m.Legs[s] = new LegAnchor { Mouth = V(r), InnerRadius = r.ReadSingle(), OuterRadius = r.ReadSingle(), Inside = r.ReadSingle() };
            uint count = r.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                var p = new ModelPart { Kind = (PartKind)r.ReadByte(), Side = r.ReadByte(), Leather = r.ReadByte() == 1, Color = V(r) };
                if ((byte)p.Kind > 3 || p.Side > 2) throw new InvalidDataException("bad part header");
                int n = checked((int)r.ReadUInt32());
                p.Positions = new Vector3[n];
                p.Normals = new Vector3[n];
                for (int k = 0; k < n; k++) p.Positions[k] = V(r);
                for (int k = 0; k < n; k++) p.Normals[k] = V(r);
                int ni = checked((int)r.ReadUInt32());
                if (ni % 3 != 0) throw new InvalidDataException("index count is not a multiple of 3");
                p.Indices = new int[ni];
                for (int k = 0; k < ni; k++)
                {
                    uint idx = r.ReadUInt32();
                    if (idx >= n) throw new InvalidDataException("index out of range");
                    p.Indices[k] = (int)idx;
                }
                m.Parts.Add(p);
            }
            return m;
        }

        private static Vector3 V(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>The model's footprint on the wall (model y and z ranges, metres), for checking the wall is free there.</summary>
        public (float HalfWidth, float Bottom, float Top) Footprint()
        {
            float half = 0f, bottom = float.MaxValue, top = float.MinValue;
            foreach (var p in Parts)
                foreach (var v in p.Positions)
                {
                    half = Math.Max(half, Math.Abs(v.Y));
                    bottom = Math.Min(bottom, v.Z);
                    top = Math.Max(top, v.Z);
                }
            return (half, bottom, top);
        }
    }

    /// <summary>
    /// Wall coordinates -> world (any y-up frame, e.g. Unity's numbers): x along the wall normal, z up, y = up x normal, all
    /// scaled by <see cref="Scale"/> about <see cref="Origin"/> (the visible wall surface at floor height). The basis has
    /// determinant +1, so triangle winding is kept.
    /// </summary>
    public readonly struct WallFrame
    {
        public readonly Vector3 Origin, Normal, Side, Up;
        public readonly float Scale;

        public WallFrame(Vector3 origin, Vector3 normal, Vector3 up, float scale)
        {
            Up = Vector3.Normalize(up);
            Normal = Vector3.Normalize(normal - Up * Vector3.Dot(normal, Up)); // upright walls only: keep the normal level
            Side = Vector3.Cross(Up, Normal);
            Origin = origin;
            Scale = scale;
        }

        public Vector3 Point(Vector3 model) => Origin + Direction(model) * Scale;

        public Vector3 Direction(Vector3 model) => Normal * model.X + Side * model.Y + Up * model.Z;

        /// <summary>World -> wall coordinates (inverse of <see cref="Point"/>).</summary>
        public Vector3 ToModel(Vector3 world)
        {
            Vector3 d = (world - Origin) / Scale;
            return new Vector3(Vector3.Dot(d, Normal), Vector3.Dot(d, Side), Vector3.Dot(d, Up));
        }

        /// <summary>The rotation that turns model axes into world axes (for a transform that carries the meshes).</summary>
        public Quaternion Rotation()
        {
            // System.Numerics uses row vectors: row i is the image of model axis i.
            var m = new Matrix4x4(Normal.X, Normal.Y, Normal.Z, 0, Side.X, Side.Y, Side.Z, 0, Up.X, Up.Y, Up.Z, 0, 0, 0, 0, 1);
            return Quaternion.CreateFromRotationMatrix(m);
        }
    }
}
