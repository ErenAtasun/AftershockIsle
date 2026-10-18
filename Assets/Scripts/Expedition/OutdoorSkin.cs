using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum SkinRole { WallOuter, WallInner, Floor, Roof }

    /// <summary>
    /// Girilebilir yapının kutularına doku giydirir: kutunun görseli kit malzemesiyle (sıva, tuğla, beton) kaplanır ve
    /// doku, kutunun metre cinsinden gerçek boyuna göre tekrarlanır (gerilmez). Collider ve boyut aynı kalır.
    /// Kit yoksa hiçbir şey yapmaz (kutu düz renkli kalır).
    /// </summary>
    public static class OutdoorSkin
    {
        static readonly Dictionary<(int, int, int, int), Mesh> meshes = new Dictionary<(int, int, int, int), Mesh>();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static MaterialPropertyBlock block;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => meshes.Clear();

        public static void Apply(Transform box, SkinRole role, FieldLayout layout)
        {
            var kit = OutdoorKit.Instance;
            if (kit == null || box == null) return;

            string materialName;
            float tile;
            Color tint;
            switch (role)
            {
                case SkinRole.WallOuter:
                    materialName = layout switch
                    {
                        FieldLayout.House => "plaster_beige",
                        FieldLayout.Clinic => "plaster_grey",
                        FieldLayout.Market => "brick",
                        FieldLayout.Ruin => "brick",
                        _ => "concrete",
                    };
                    tile = 2.5f;
                    tint = Color.white;
                    break;
                case SkinRole.WallInner:
                    materialName = "plaster_grey";
                    tile = 2.5f;
                    tint = new Color(0.82f, 0.8f, 0.76f);
                    break;
                case SkinRole.Floor:
                    materialName = "sidewalk";
                    tile = 2f;
                    tint = new Color(0.72f, 0.7f, 0.66f);
                    break;
                default:
                    materialName = "concrete";
                    tile = 3f;
                    tint = new Color(0.55f, 0.53f, 0.5f);
                    break;
            }

            var material = kit.GetMaterial(materialName);
            var renderer = box.GetComponent<MeshRenderer>();
            var filter = box.GetComponent<MeshFilter>();
            if (material == null || renderer == null || filter == null) return;

            filter.sharedMesh = MeshFor(box.localScale, tile);
            renderer.sharedMaterial = material;
            block ??= new MaterialPropertyBlock();
            block.Clear();
            block.SetColor(BaseColorId, tint);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>Birim küp: köşeler ±0.5, doku koordinatları size boyutuna ve tile (metre) değerine göre. size = kutunun ölçeği.</summary>
        static Mesh MeshFor(Vector3 size, float tile)
        {
            var key = (Mathf.RoundToInt(size.x * 100f), Mathf.RoundToInt(size.y * 100f), Mathf.RoundToInt(size.z * 100f), Mathf.RoundToInt(tile * 10f));
            if (meshes.TryGetValue(key, out var cached) && cached != null) return cached;

            var vertices = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var normals = new List<Vector3>(24);
            var triangles = new List<int>(36);

            AddFace(vertices, uvs, normals, triangles, Vector3.right, Vector3.up, Vector3.forward, size, tile);
            AddFace(vertices, uvs, normals, triangles, Vector3.left, Vector3.up, Vector3.back, size, tile);
            AddFace(vertices, uvs, normals, triangles, Vector3.up, Vector3.right, Vector3.forward, size, tile);
            AddFace(vertices, uvs, normals, triangles, Vector3.down, Vector3.right, Vector3.back, size, tile);
            AddFace(vertices, uvs, normals, triangles, Vector3.forward, Vector3.right, Vector3.up, size, tile);
            AddFace(vertices, uvs, normals, triangles, Vector3.back, Vector3.left, Vector3.up, size, tile);

            var mesh = new Mesh { name = $"Kutu {size.x:0.##}x{size.y:0.##}x{size.z:0.##}" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            meshes[key] = mesh;
            return mesh;
        }

        // normal N, yüzey eksenleri A ve B. Köşeler birim küpte; uv = (a, b) * ölçek / tile.
        static void AddFace(List<Vector3> vertices, List<Vector2> uvs, List<Vector3> normals, List<int> triangles,
                            Vector3 normal, Vector3 a, Vector3 b, Vector3 size, float tile)
        {
            int start = vertices.Count;
            Vector3[] corners =
            {
                normal * 0.5f - a * 0.5f - b * 0.5f,
                normal * 0.5f + a * 0.5f - b * 0.5f,
                normal * 0.5f + a * 0.5f + b * 0.5f,
                normal * 0.5f - a * 0.5f + b * 0.5f,
            };

            float sa = Mathf.Abs(Vector3.Dot(a, size));
            float sb = Mathf.Abs(Vector3.Dot(b, size));
            Vector2[] corner =
            {
                new Vector2(0f, 0f), new Vector2(sa / tile, 0f), new Vector2(sa / tile, sb / tile), new Vector2(0f, sb / tile),
            };

            for (int i = 0; i < 4; i++)
            {
                vertices.Add(corners[i]);
                uvs.Add(corner[i]);
                normals.Add(normal);
            }

            // Ön yüz: cross(b - a, c - a) yüzey normaliyle aynı yönde olmalı.
            var cross = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
            if (Vector3.Dot(cross, normal) > 0f)
            {
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            else
            {
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
        }
    }
}
