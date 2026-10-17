using System;
using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Core
{
    /// <summary>
    /// Yatay düzlemde (X-Z) yürünebilirlik ızgarası ve A* yol bulma. Sığınak katları ve dış saha kullanır.
    /// Bulunan hücre yolu görüş hattıyla sadeleştirilir: geriye yalnızca dönülmesi gereken köşeler kalır.
    /// Başlangıç ya da hedef engelin içindeyse (mobilyaya yaslanmış karakter) en yakın boş hücreden aranır.
    /// </summary>
    public sealed class NavGrid
    {
        const float Diagonal = 1.41421356f;

        readonly Vector2 min;
        readonly float cell;
        readonly int width;
        readonly int height;
        readonly bool[] blocked;

        // A* durumları her aramada temizlenmez; arama damgası eski kayıtları geçersiz sayar.
        readonly float[] cost;
        readonly int[] parent;
        readonly int[] stamp;
        readonly bool[] closed;
        readonly List<(float f, int index)> open = new List<(float f, int index)>();
        readonly List<Vector2> cellPath = new List<Vector2>();
        int search;

        public NavGrid(Vector2 min, Vector2 max, float cellSize)
        {
            this.min = min;
            cell = Mathf.Max(0.05f, cellSize);
            width = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / cell));
            height = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / cell));

            int count = width * height;
            blocked = new bool[count];
            cost = new float[count];
            parent = new int[count];
            stamp = new int[count];
            closed = new bool[count];
        }

        /// <summary>Her hücrenin merkezini sorarak engelleri yeniden doldurur.</summary>
        public void Fill(Func<Vector2, bool> isBlocked)
        {
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    blocked[z * width + x] = isBlocked(Center(x, z));
        }

        /// <summary>
        /// from'dan to'ya yol arar; corners'a ara köşeleri yazar (from ve to hariç).
        /// Hedefe ulaşılamıyorsa hedefe en yakın ulaşılabilen hücreye kadar yol yazar ve false döner.
        /// </summary>
        public bool FindPath(Vector2 from, Vector2 to, List<Vector2> corners)
        {
            corners.Clear();
            if (IsClear(from, to)) return true;

            int start = NearestFree(IndexOf(from));
            int goal = NearestFree(IndexOf(to));
            if (start < 0 || goal < 0) return false;

            bool reached = Search(start, goal, out int end);

            // Hücre merkezleri: hedefe ulaşıldıysa son hücre yerine gerçek hedef.
            cellPath.Clear();
            for (int index = end; index != start; index = parent[index]) cellPath.Add(Center(index));
            cellPath.Add(Center(start));
            cellPath.Reverse();
            if (reached) cellPath[cellPath.Count - 1] = to;

            // Görüş hattıyla sadeleştir: köşeyi atlayan doğru engele çarpıyorsa köşe kalır.
            var anchor = from;
            for (int i = 0; i < cellPath.Count - 1; i++)
            {
                if (IsClear(anchor, cellPath[i + 1])) continue;
                corners.Add(cellPath[i]);
                anchor = cellPath[i];
            }

            if (!reached) corners.Add(cellPath[cellPath.Count - 1]);
            return reached;
        }

        /// <summary>
        /// a ile b arasındaki doğru boş hücrelerden mi geçiyor. Uçlardaki engelli kısım sayılmaz
        /// (engelin dibinde duran karakter oradan uzaklaşabilmeli).
        /// </summary>
        public bool IsClear(Vector2 a, Vector2 b)
        {
            float length = Vector2.Distance(a, b);
            int samples = Mathf.Max(2, Mathf.CeilToInt(length / (cell * 0.5f)) + 1);

            int firstFree = -1;
            int lastFree = -1;
            for (int i = 0; i < samples; i++)
            {
                if (blocked[IndexOf(Vector2.Lerp(a, b, i / (float)(samples - 1)))]) continue;
                if (firstFree < 0) firstFree = i;
                lastFree = i;
            }

            if (firstFree < 0) return length <= cell;
            for (int i = firstFree; i <= lastFree; i++)
                if (blocked[IndexOf(Vector2.Lerp(a, b, i / (float)(samples - 1)))]) return false;
            return true;
        }

        bool Search(int start, int goal, out int end)
        {
            search++;
            open.Clear();
            Visit(start, 0f, start);
            Push(Heuristic(start, goal), start);

            end = start;
            float bestHeuristic = Heuristic(start, goal);

            while (open.Count > 0)
            {
                int current = Pop();
                if (closed[current]) continue;
                closed[current] = true;

                float h = Heuristic(current, goal);
                if (h < bestHeuristic)
                {
                    bestHeuristic = h;
                    end = current;
                }
                if (current == goal) return true;

                int cx = current % width;
                int cz = current / width;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx;
                        int nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= width || nz >= height) continue;

                        int next = nz * width + nx;
                        if (blocked[next]) continue;
                        // Köşe kesilmez: çaprazda iki komşu da boş olmalı.
                        if (dx != 0 && dz != 0 && (blocked[cz * width + nx] || blocked[nz * width + cx])) continue;

                        float g = cost[current] + (dx != 0 && dz != 0 ? Diagonal : 1f);
                        if (stamp[next] == search && (closed[next] || g >= cost[next])) continue;

                        Visit(next, g, current);
                        Push(g + Heuristic(next, goal), next);
                    }
                }
            }

            return false;
        }

        void Visit(int index, float g, int from)
        {
            if (stamp[index] != search)
            {
                stamp[index] = search;
                closed[index] = false;
            }
            cost[index] = g;
            parent[index] = from;
        }

        float Heuristic(int a, int b)
        {
            int dx = Mathf.Abs(a % width - b % width);
            int dz = Mathf.Abs(a / width - b / width);
            return Mathf.Max(dx, dz) + (Diagonal - 1f) * Mathf.Min(dx, dz);
        }

        // İkili yığın (en küçük f üstte).
        void Push(float f, int index)
        {
            open.Add((f, index));
            int i = open.Count - 1;
            while (i > 0)
            {
                int up = (i - 1) / 2;
                if (open[up].f <= open[i].f) break;
                (open[up], open[i]) = (open[i], open[up]);
                i = up;
            }
        }

        int Pop()
        {
            int top = open[0].index;
            int last = open.Count - 1;
            open[0] = open[last];
            open.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int left = i * 2 + 1;
                int right = left + 1;
                int smallest = i;
                if (left < open.Count && open[left].f < open[smallest].f) smallest = left;
                if (right < open.Count && open[right].f < open[smallest].f) smallest = right;
                if (smallest == i) break;
                (open[smallest], open[i]) = (open[i], open[smallest]);
                i = smallest;
            }
            return top;
        }

        /// <summary>Hücre engelliyse halka halka genişleyerek en yakın boş hücreyi bulur.</summary>
        int NearestFree(int index)
        {
            if (!blocked[index]) return index;

            int cx = index % width;
            int cz = index / width;
            int maxRing = Mathf.Max(width, height);
            for (int ring = 1; ring < maxRing; ring++)
            {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int dz = -ring; dz <= ring; dz++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring) continue;
                        int nx = cx + dx;
                        int nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= width || nz >= height) continue;

                        int next = nz * width + nx;
                        float distance = dx * dx + dz * dz;
                        if (blocked[next] || distance >= bestDistance) continue;
                        best = next;
                        bestDistance = distance;
                    }
                }
                if (best >= 0) return best;
            }
            return -1;
        }

        int IndexOf(Vector2 point)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((point.x - min.x) / cell), 0, width - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((point.y - min.y) / cell), 0, height - 1);
            return z * width + x;
        }

        Vector2 Center(int index) => Center(index % width, index / width);

        Vector2 Center(int x, int z) => new Vector2(min.x + (x + 0.5f) * cell, min.y + (z + 0.5f) * cell);
    }
}
