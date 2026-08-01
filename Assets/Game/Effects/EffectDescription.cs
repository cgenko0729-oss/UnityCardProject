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
        /// <param name="decorator">
        /// 可选的上色钩子，见 <see cref="IDescriptionDecorator"/>。
        /// ★ 传 null（默认）时输出与加这个参数之前**逐字符相同**——
        ///   自动模拟器、战斗日志、Editor 的卡表预览都走这条路。
        /// </param>
        public static string Format(string template, IReadOnlyList<CardEffect> effects, EffectContext ctx,
                                    IDescriptionDecorator decorator = null)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            // ★ 装饰模板要排在**最前面**，连「这张牌没有效果」的早退路径也要经过它：
            //   纯文本卡（「本回合结束时消耗此牌。」这类没有 {N} 的）照样有词条名要上色。
            if (decorator != null) template = decorator.DecorateTemplate(template);

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

                    // ★ 上色包在这里，而不是等 sb 拼完之后再回头找：
                    //   这是**唯一**还知道「这一段是谁产出的」的时刻，见 IDescriptionDecorator 的注释。
                    if (decorator != null) val = decorator.DecorateValue(val, effects[idx]);

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
