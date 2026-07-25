using System.Collections.Generic;
using System.Text;

namespace Game.Effects
{
    /// <summary>
    /// 把 <c>"造成 {0} 点伤害"</c> 里的 <c>{N}</c> 换成第 N 个效果的 <see cref="CardEffect.Describe"/> 结果。
    /// ★ 卡牌与药水共用同一份实现——两边各写一遍的话，
    ///   下次给模板加语法（例如条件文本）必然只改一边。
    /// </summary>
    public static class EffectDescription
    {
        public static string Format(string template, IReadOnlyList<CardEffect> effects, EffectContext ctx)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            if (effects == null || effects.Count == 0) return template;

            var sb = new StringBuilder(template.Length + 16);
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                if (c != '{') { sb.Append(c); continue; }

                int close = template.IndexOf('}', i);
                if (close < 0) { sb.Append(c); continue; }

                string inner = template.Substring(i + 1, close - i - 1);
                if (int.TryParse(inner, out int idx) && idx >= 0 && idx < effects.Count && effects[idx] != null)
                {
                    string val;
                    // 描述每帧都会算，一个配错的效果不该把整个界面拖垮
                    try { val = effects[idx].Describe(ctx); }
                    catch { val = "?"; }
                    sb.Append(val);
                    i = close;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
