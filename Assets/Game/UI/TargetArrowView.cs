using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 拖拽出牌时从卡牌指向目标的曲线箭头（尖塔式）。
    ///
    /// 自己生成网格：一条从细到粗的二次贝塞尔带 + 一个三角箭头。
    /// ★ 不用 LineRenderer（那是世界空间的，和 ScreenSpaceOverlay 的 Canvas 对不上），
    ///   也不用「沿曲线摆一串小圆点 Image」（几十个 Graphic 每帧改位置，重建开销远大于一次 mesh）。
    ///
    /// 用法：改 <see cref="From"/> / <see cref="To"/> 后调 <see cref="Refresh"/>。
    /// 两个端点都是本组件 RectTransform 的本地坐标。
    /// </summary>
    public class TargetArrowView : MaskableGraphic
    {
        /// <summary>起点（卡牌顶边中点）。</summary>
        public Vector2 From;

        /// <summary>终点（鼠标 / 锁定的目标）。箭头尖端正好落在这里。</summary>
        public Vector2 To;

        private const int Segments = 26;
        private const float FromWidth = 6f;
        private const float ToWidth = 16f;
        private const float HeadLength = 44f;
        private const float HeadWidth = 40f;

        public static TargetArrowView Create(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TargetArrowView));
            go.transform.SetParent(parent, false);

            var view = go.GetComponent<TargetArrowView>();
            UIFactory.Stretch(view.rectTransform);
            view.raycastTarget = false;     // 箭头压在敌人面板上，绝不能挡住点击
            return view;
        }

        public void Refresh() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Vector2 delta = To - From;
            if (delta.sqrMagnitude < 16f) return;   // 太短就别画，切线会退化成 NaN

            Vector2 ctrl = ControlPoint(delta);

            // 箭头要占掉曲线末端的一段，带子只画到 tEnd 为止，否则箭头会被带子从里面顶穿
            float length = ApproxLength(ctrl);
            float tEnd = Mathf.Clamp(1f - HeadLength / Mathf.Max(HeadLength * 2f, length), 0.05f, 0.95f);

            var col = (Color32)color;

            // ---------------- 带子（三角形条带）
            int prev = -1;
            for (int s = 0; s <= Segments; s++)
            {
                float t = tEnd * s / Segments;
                Vector2 p = Bezier(ctrl, t);
                Vector2 dir = Tangent(ctrl, t);
                Vector2 nrm = new Vector2(-dir.y, dir.x);
                float half = Mathf.Lerp(FromWidth, ToWidth, (float)s / Segments) * 0.5f;

                int i = vh.currentVertCount;
                vh.AddVert(p + nrm * half, col, Vector2.zero);
                vh.AddVert(p - nrm * half, col, Vector2.zero);

                if (prev >= 0)
                {
                    vh.AddTriangle(prev, prev + 1, i + 1);
                    vh.AddTriangle(prev, i + 1, i);
                }
                prev = i;
            }

            // ---------------- 箭头
            Vector2 tipDir = Tangent(ctrl, 1f);
            Vector2 tipSide = new Vector2(-tipDir.y, tipDir.x);
            Vector2 headBase = To - tipDir * HeadLength;

            int b = vh.currentVertCount;
            vh.AddVert(To, col, Vector2.zero);
            vh.AddVert(headBase + tipSide * (HeadWidth * 0.5f), col, Vector2.zero);
            vh.AddVert(headBase - tipSide * (HeadWidth * 0.5f), col, Vector2.zero);
            vh.AddTriangle(b, b + 1, b + 2);
        }

        /// <summary>
        /// 控制点。先往上甩再拐向目标——曲线离开卡面时朝上，玩家一眼就能看出
        /// 「这条线是从我举着的这张牌上出来的」，而不是一条凭空出现的直线。
        /// </summary>
        private Vector2 ControlPoint(Vector2 delta)
        {
            float lift = 120f + Mathf.Abs(delta.x) * 0.22f + Mathf.Max(0f, delta.y) * 0.25f;
            return new Vector2(From.x + delta.x * 0.15f, From.y + delta.y * 0.5f + lift);
        }

        private Vector2 Bezier(Vector2 ctrl, float t)
        {
            float u = 1f - t;
            return u * u * From + 2f * u * t * ctrl + t * t * To;
        }

        private Vector2 Tangent(Vector2 ctrl, float t)
        {
            Vector2 d = 2f * (1f - t) * (ctrl - From) + 2f * t * (To - ctrl);
            return d.sqrMagnitude < 1e-6f ? Vector2.up : d.normalized;
        }

        private float ApproxLength(Vector2 ctrl)
        {
            const int samples = 8;
            float total = 0f;
            Vector2 prev = From;
            for (int i = 1; i <= samples; i++)
            {
                Vector2 p = Bezier(ctrl, (float)i / samples);
                total += Vector2.Distance(prev, p);
                prev = p;
            }
            return total;
        }
    }
}
