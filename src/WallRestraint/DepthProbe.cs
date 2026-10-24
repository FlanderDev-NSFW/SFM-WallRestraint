using System;
using UnityEngine;

namespace WallRestraint
{
    /// <summary>
    /// What is actually drawn on a wall patch. Colliders can be invisible (PlayerBlocker boxes) or lie up to ~18 cm in front of
    /// the drawn wall (docs/knowledge/api-notes/level-geometry.md), so an orthographic camera renders the patch with Unity's
    /// depth-normals replacement shader and the depth of every pixel is read back.
    /// </summary>
    internal static class DepthProbe
    {
        private const float Distance = 1f, Far = 2f; // camera 1 m in front of the reference plane, sees up to 1 m behind it
        private static Camera _camera;
        private static RenderTexture _rt;
        private static Texture2D _tex;
        private static Shader _shader;
        private static int _w, _h;

        /// <summary>
        /// Depth of the drawn surface behind the plane through <paramref name="center"/> with normal <paramref name="normal"/>
        /// (m, + = behind; NaN = nothing drawn), on a <paramref name="w"/> x <paramref name="h"/> grid over a patch of
        /// <paramref name="width"/> x <paramref name="height"/> metres. Row 0 is at the bottom; column index grows along
        /// up x normal (the wall frame's side axis). Null if the shader is missing.
        /// </summary>
        public static float[] Sample(Vector3 center, Vector3 normal, Vector3 up, float width, float height, int w, int h, int cullingMask)
        {
            if (_shader == null) _shader = Shader.Find("Hidden/Internal-DepthNormalsTexture");
            if (_shader == null) return null;
            if (_camera == null)
            {
                var go = new GameObject("WallRestraint.DepthProbe");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _camera = go.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.orthographic = true;
                _camera.nearClipPlane = 0.01f;
                _camera.farClipPlane = Far;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.5f, 0.5f, 1f, 1f); // decodes to depth ~1 = nothing
            }
            // Linear: the game renders in linear colour space, an sRGB target bends the encoded depth (1.0 m read as 1.47 m).
            // DontUnloadUnusedAsset: textures held only by managed code are destroyed on a map change.
            if (_rt == null || _w != w || _h != h)
            {
                if (_rt != null) UnityEngine.Object.Destroy(_rt);
                if (_tex != null) UnityEngine.Object.Destroy(_tex);
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.DontUnloadUnusedAsset };
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.DontUnloadUnusedAsset };
                _w = w;
                _h = h;
            }
            if (_tex == null) _tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            _camera.transform.SetPositionAndRotation(center + normal * Distance, Quaternion.LookRotation(-normal, up));
            _camera.orthographicSize = height / 2f;
            _camera.aspect = width / height;
            _camera.cullingMask = cullingMask;
            _camera.targetTexture = _rt;
            _camera.RenderWithShader(_shader, "");
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            var px = _tex.GetPixels32();
            var depth = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color32 c = px[y * w + x];
                    float d01 = c.b / 255f + c.a / 255f / 255f; // EncodeFloatRG in BA
                    // The camera's right is -side (it looks against the normal): mirror the columns.
                    depth[y * w + (w - 1 - x)] = d01 > 0.995f ? float.NaN : d01 * Far - Distance;
                }
            return depth;
        }

        /// <summary>
        /// <see cref="Sample"/> with the player's renderers switched off for the render (her renderers are on several layers,
        /// possibly ones the walls use too, so a layer mask cannot hide her).
        /// </summary>
        public static float[] SampleWithout(Transform player, Vector3 center, Vector3 normal, Vector3 up, float width, float height, int w, int h)
        {
            var renderers = player.GetComponentsInChildren<Renderer>(false);
            var was = new bool[renderers.Length];
            try
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    was[i] = renderers[i].forceRenderingOff;
                    renderers[i].forceRenderingOff = true;
                }
                return Sample(center, normal, up, width, height, w, h, SceneMask(-1));
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].forceRenderingOff = was[i];
            }
        }

        public static int SceneMask(int playerLayer)
        {
            int mask = Camera.main != null ? Camera.main.cullingMask : ~0;
            foreach (string layer in new[] { "Npc", "PlayerBlocker", "UI" })
            {
                int l = LayerMask.NameToLayer(layer);
                if (l >= 0) mask &= ~(1 << l);
            }
            if (playerLayer >= 0) mask &= ~(1 << playerLayer);
            return mask;
        }
    }
}
