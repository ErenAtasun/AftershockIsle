using System;
using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.UI
{
    public enum IconKind
    {
        Slingshot, Pistol, Smg, Shotgun, Rifle, Sniper, Bullet, Kit, Pickaxe, Crowbar, Backpack, Can, Gear, Bolt, Beam, Plus, Block,
    }

    /// <summary>
    /// Envanter ikonları: yerleşik yazı tipi emoji çizemediği için (kutu görünür) kodla çizilen tek renkli (beyaz) silüetler.
    /// Image.color ile boyanır. 100×100 tasarım alanında şekil eklenir ya da oyulur; 3× süperörnekleme ile yumuşatılır.
    /// </summary>
    public static class UiIcons
    {
        const int Size = 96;
        const int Super = 3;

        static readonly Dictionary<IconKind, Sprite> cache = new Dictionary<IconKind, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => cache.Clear();

        public static Sprite Get(IconKind kind)
        {
            if (cache.TryGetValue(kind, out var sprite) && sprite != null) return sprite;
            sprite = Create(kind);
            cache[kind] = sprite;
            return sprite;
        }

        static Sprite Create(IconKind kind)
        {
            var pen = new Pen();
            Draw(kind, pen);

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Icon_" + kind,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color[Size * Size];
            pen.Resolve(pixels);
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }

        // ---------------------------------------------------------------- Çizimler (100×100, y aşağı)

        static void Draw(IconKind kind, Pen p)
        {
            switch (kind)
            {
                case IconKind.Slingshot:
                    p.Line(50, 92, 50, 56, 9);
                    p.Line(50, 58, 26, 18, 9);
                    p.Line(50, 58, 74, 18, 9);
                    p.Line(26, 22, 74, 22, 3);
                    p.Circle(50, 24, 6);
                    break;

                case IconKind.Pistol:
                    p.Poly(8, 28, 84, 28, 88, 34, 88, 46, 8, 46);
                    p.Poly(50, 46, 74, 46, 66, 86, 46, 86);
                    p.Line(36, 46, 36, 62, 4);
                    p.Line(36, 62, 52, 62, 4);
                    p.Rect(78, 22, 8, 6);
                    p.Subtract();
                    p.Rect(14, 34, 40, 4);
                    p.Add();
                    break;

                case IconKind.Smg:
                    p.Poly(6, 32, 78, 32, 78, 50, 6, 50);
                    p.Rect(78, 37, 18, 8);
                    p.Poly(34, 50, 46, 50, 48, 88, 34, 88);
                    p.Poly(58, 50, 72, 50, 68, 74, 56, 74);
                    p.Rect(2, 36, 6, 18);
                    p.Rect(20, 26, 30, 6);
                    break;

                case IconKind.Shotgun:
                    p.Rect(18, 34, 78, 8);
                    p.Rect(48, 42, 24, 9);
                    p.Poly(2, 36, 28, 36, 28, 52, 14, 66, 2, 62);
                    p.Rect(28, 42, 22, 8);
                    p.Subtract();
                    p.Line(52, 46, 70, 46, 2);
                    p.Add();
                    break;

                case IconKind.Rifle:
                    p.Rect(30, 34, 52, 14);
                    p.Rect(80, 38, 18, 6);
                    p.Poly(4, 38, 30, 34, 30, 50, 12, 64, 2, 58);
                    p.Poly(46, 48, 60, 48, 64, 82, 50, 82);
                    p.Rect(66, 28, 12, 6);
                    p.Rect(40, 28, 14, 6);
                    break;

                case IconKind.Sniper:
                    p.Rect(34, 42, 62, 6);
                    p.Rect(10, 38, 30, 14);
                    p.Poly(2, 40, 12, 38, 12, 54, 6, 64, 0, 60);
                    p.Rect(28, 24, 38, 10);
                    p.Circle(30, 29, 7);
                    p.Circle(66, 29, 7);
                    p.Line(46, 34, 46, 40, 4);
                    p.Line(78, 48, 70, 70, 3);
                    p.Line(78, 48, 88, 70, 3);
                    break;

                case IconKind.Bullet:
                    p.RoundRect(34, 38, 18, 48, 4);
                    p.Poly(34, 40, 52, 40, 43, 14);
                    p.Subtract();
                    p.Rect(34, 74, 18, 3);
                    p.Add();
                    p.RoundRect(58, 46, 18, 40, 4);
                    p.Poly(58, 48, 76, 48, 67, 24);
                    p.Subtract();
                    p.Rect(58, 74, 18, 3);
                    p.Add();
                    break;

                case IconKind.Kit:
                    p.RoundRect(12, 26, 76, 58, 10);
                    p.RoundRect(34, 14, 32, 16, 5);
                    p.Subtract();
                    p.RoundRect(40, 20, 20, 8, 3);
                    p.Rect(44, 38, 12, 36);
                    p.Rect(32, 50, 36, 12);
                    p.Add();
                    break;

                case IconKind.Pickaxe:
                    p.Line(28, 90, 68, 26, 8);
                    p.Poly(26, 24, 50, 14, 80, 26, 92, 46, 78, 40, 52, 30, 28, 34);
                    break;

                case IconKind.Crowbar:
                    p.Line(22, 88, 66, 30, 8);
                    p.Line(66, 30, 82, 20, 8);
                    p.Line(82, 20, 90, 34, 7);
                    p.Poly(12, 90, 26, 76, 32, 86, 20, 96);
                    break;

                case IconKind.Backpack:
                    p.RoundRect(22, 28, 56, 62, 14);
                    p.Ring(50, 24, 12, 5);
                    p.Subtract();
                    p.Line(26, 54, 74, 54, 3);
                    p.RoundRect(34, 62, 32, 20, 6);
                    p.Add();
                    p.RoundRect(38, 66, 24, 12, 3);
                    p.Rect(16, 44, 8, 30);
                    p.Rect(76, 44, 8, 30);
                    break;

                case IconKind.Can:
                    p.RoundRect(26, 26, 48, 62, 7);
                    p.RoundRect(23, 20, 54, 11, 5);
                    p.RoundRect(23, 83, 54, 9, 4);
                    p.Subtract();
                    p.Rect(26, 44, 48, 26);
                    p.Add();
                    p.Rect(34, 50, 32, 14);
                    break;

                case IconKind.Gear:
                    p.Circle(50, 50, 26);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        float cx = 50f + Mathf.Cos(a) * 32f;
                        float cy = 50f + Mathf.Sin(a) * 32f;
                        p.Line(50f + Mathf.Cos(a) * 24f, 50f + Mathf.Sin(a) * 24f, cx, cy, 13);
                    }
                    p.Subtract();
                    p.Circle(50, 50, 11);
                    p.Add();
                    break;

                case IconKind.Bolt:
                    p.Poly(60, 6, 28, 54, 46, 54, 38, 94, 74, 40, 54, 40);
                    break;

                case IconKind.Beam:
                    p.Rect(18, 22, 64, 12);
                    p.Rect(44, 34, 12, 32);
                    p.Rect(18, 66, 64, 12);
                    p.Subtract();
                    p.Circle(28, 28, 2);
                    p.Circle(72, 28, 2);
                    p.Circle(28, 72, 2);
                    p.Circle(72, 72, 2);
                    p.Add();
                    break;

                case IconKind.Block:
                    p.Rect(14, 20, 72, 60);
                    p.Subtract();
                    p.Line(14, 50, 86, 50, 3);
                    p.Line(50, 20, 50, 50, 3);
                    p.Line(32, 50, 32, 80, 3);
                    p.Line(68, 50, 68, 80, 3);
                    p.Add();
                    break;

                default: // Plus
                    p.Line(50, 28, 50, 72, 9);
                    p.Line(28, 50, 72, 50, 9);
                    break;
            }
        }

        // ---------------------------------------------------------------- Çizici

        sealed class Pen
        {
            const int N = Size * Super;
            readonly float[] coverage = new float[N * N];
            bool adding = true;
            const float Scale = N / 100f;

            public void Add() => adding = true;
            public void Subtract() => adding = false;

            void Fill(float minX, float minY, float maxX, float maxY, Func<float, float, bool> inside)
            {
                int x0 = Mathf.Clamp(Mathf.FloorToInt(minX * Scale), 0, N - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX * Scale), 0, N - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(minY * Scale), 0, N - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY * Scale), 0, N - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (inside((x + 0.5f) / Scale, (y + 0.5f) / Scale))
                            coverage[y * N + x] = adding ? 1f : 0f;
            }

            public void Rect(float x, float y, float w, float h) =>
                Fill(x, y, x + w, y + h, (px, py) => px >= x && px <= x + w && py >= y && py <= y + h);

            public void RoundRect(float x, float y, float w, float h, float r)
            {
                float cx = x + w * 0.5f, cy = y + h * 0.5f;
                float hx = w * 0.5f - r, hy = h * 0.5f - r;
                Fill(x, y, x + w, y + h, (px, py) =>
                {
                    float qx = Mathf.Max(Mathf.Abs(px - cx) - hx, 0f);
                    float qy = Mathf.Max(Mathf.Abs(py - cy) - hy, 0f);
                    return qx * qx + qy * qy <= r * r;
                });
            }

            public void Circle(float cx, float cy, float r) =>
                Fill(cx - r, cy - r, cx + r, cy + r, (px, py) => (px - cx) * (px - cx) + (py - cy) * (py - cy) <= r * r);

            public void Ring(float cx, float cy, float r, float thickness)
            {
                float inner = r - thickness;
                Fill(cx - r, cy - r, cx + r, cy + r, (px, py) =>
                {
                    float d = (px - cx) * (px - cx) + (py - cy) * (py - cy);
                    return d <= r * r && d >= inner * inner;
                });
            }

            public void Line(float x1, float y1, float x2, float y2, float thickness)
            {
                float half = thickness * 0.5f;
                float dx = x2 - x1, dy = y2 - y1;
                float lengthSq = Mathf.Max(0.0001f, dx * dx + dy * dy);
                Fill(Mathf.Min(x1, x2) - half, Mathf.Min(y1, y2) - half, Mathf.Max(x1, x2) + half, Mathf.Max(y1, y2) + half, (px, py) =>
                {
                    float t = Mathf.Clamp01(((px - x1) * dx + (py - y1) * dy) / lengthSq);
                    float qx = x1 + dx * t - px, qy = y1 + dy * t - py;
                    return qx * qx + qy * qy <= half * half;
                });
            }

            /// <summary>Çokgen (x, y çiftleri); çift-tek kuralı.</summary>
            public void Poly(params float[] xy)
            {
                int count = xy.Length / 2;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    minX = Mathf.Min(minX, xy[i * 2]);
                    maxX = Mathf.Max(maxX, xy[i * 2]);
                    minY = Mathf.Min(minY, xy[i * 2 + 1]);
                    maxY = Mathf.Max(maxY, xy[i * 2 + 1]);
                }

                Fill(minX, minY, maxX, maxY, (px, py) =>
                {
                    bool inside = false;
                    for (int i = 0, j = count - 1; i < count; j = i++)
                    {
                        float xi = xy[i * 2], yi = xy[i * 2 + 1], xj = xy[j * 2], yj = xy[j * 2 + 1];
                        if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi) inside = !inside;
                    }
                    return inside;
                });
            }

            public void Resolve(Color[] pixels)
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float sum = 0f;
                        for (int sy = 0; sy < Super; sy++)
                            for (int sx = 0; sx < Super; sx++)
                                sum += coverage[(y * Super + sy) * N + x * Super + sx];

                        // Doku y yukarı; tasarım y aşağı.
                        pixels[(Size - 1 - y) * Size + x] = new Color(1f, 1f, 1f, sum / (Super * Super));
                    }
                }
            }
        }
    }
}
