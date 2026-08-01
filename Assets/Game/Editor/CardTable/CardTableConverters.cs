using System;
using System.Collections;
using System.Collections.Generic;
using Game.Effects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Editor.CardTables
{
    internal static class Json
    {
        /// <summary>PascalCase → camelCase。字段名与 JSON 键之间唯一的转换规则。</summary>
        public static string CamelCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (char.IsLower(name[0])) return name;
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }
    }

    // =================================================================== EffectValue

    /// <summary>
    /// <see cref="EffectValue"/> ⇄ JSON。
    ///
    /// ★ 90% 的数值就是一个固定数字，所以固定值写成**裸数字**：<c>"amount": 7</c>。
    ///   只有真的要缩放时才展开成对象。这一条单独就把
    ///   「一个 DamageEffect 在 Inspector 里占 14 行」里的 6 行消掉了。
    ///
    /// <code>
    /// "amount": 7
    /// "amount": { "base": 3, "per": "statusOnSelf:strength", "each": 2 }
    /// </code>
    ///
    /// <para><c>per</c> 把 <see cref="ValueScale"/> 和 <c>ScaleId</c> 编码成一个字符串，
    /// 因为对 <c>PerStatusStackOn*</c> 两种缩放来说，「缩放什么」和「哪个状态」是一件事——
    /// 只写 Scale 不写 ScaleId 是一个恒等于 0 的哑配置。合成一个键，就写不出那种半成品。</para>
    /// </summary>
    internal sealed class EffectValueConverter : JsonConverter
    {
        // 刻意不用枚举名直接 ToString()：枚举名带着 Per 前缀（PerCardInHand），
        // 而表里已经有 each 表达「每一个」，写成 per: "perCardInHand" 会读成「每每张手牌」。
        private static readonly Dictionary<ValueScale, string> ToText =
            new Dictionary<ValueScale, string>
            {
                { ValueScale.PerStatusStackOnSelf,   "statusOnSelf"       },
                { ValueScale.PerStatusStackOnTarget, "statusOnTarget"     },
                { ValueScale.PerCardInHand,          "cardInHand"         },
                { ValueScale.PerCardInDiscard,       "cardInDiscard"      },
                { ValueScale.PerCardPlayedThisTurn,  "cardPlayedThisTurn" },
                { ValueScale.PerEnemyAlive,          "enemyAlive"         },
                { ValueScale.PerBlockOnSelf,         "blockOnSelf"        },
                { ValueScale.PerMissingHpOnSelf,     "missingHpOnSelf"    },
                { ValueScale.XValue,                 "x"                  },
                { ValueScale.PerRepeatIndex,         "repeatIndex"        },
            };

        private static readonly Dictionary<string, ValueScale> FromText = BuildReverse();

        private static Dictionary<string, ValueScale> BuildReverse()
        {
            var d = new Dictionary<string, ValueScale>(StringComparer.Ordinal);
            foreach (var kv in ToText) d.Add(kv.Value, kv.Key);
            return d;
        }

        /// <summary>需要 ScaleId 才有意义的两种缩放。</summary>
        private static bool NeedsId(ValueScale s)
            => s == ValueScale.PerStatusStackOnSelf || s == ValueScale.PerStatusStackOnTarget;

        public override bool CanConvert(Type objectType) => objectType == typeof(EffectValue);

        public override void WriteJson(JsonWriter w, object value, JsonSerializer s)
        {
            var v = (EffectValue)value;

            bool plain = v.Scale == ValueScale.None
                         && v.PerUnit == 0
                         && v.Min == 0
                         && v.Max == 0
                         && string.IsNullOrEmpty(v.ScaleId);

            if (plain)
            {
                w.WriteValue(v.Base);
                return;
            }

            w.WriteStartObject();

            w.WritePropertyName("base");
            w.WriteValue(v.Base);

            if (v.Scale != ValueScale.None)
            {
                w.WritePropertyName("per");
                w.WriteValue(EncodeScale(v.Scale, v.ScaleId));
            }

            if (v.PerUnit != 0) { w.WritePropertyName("each"); w.WriteValue(v.PerUnit); }
            if (v.Min != 0) { w.WritePropertyName("min"); w.WriteValue(v.Min); }
            if (v.Max != 0) { w.WritePropertyName("max"); w.WriteValue(v.Max); }

            w.WriteEndObject();
        }

        public override object ReadJson(JsonReader r, Type objectType, object existing, JsonSerializer s)
        {
            if (r.TokenType == JsonToken.Integer)
                return EffectValue.Flat(Convert.ToInt32(r.Value));

            if (r.TokenType == JsonToken.StartObject)
            {
                var o = JObject.Load(r);
                var v = new EffectValue
                {
                    Base = o.Value<int?>("base") ?? 0,
                    PerUnit = o.Value<int?>("each") ?? 0,
                    Min = o.Value<int?>("min") ?? 0,
                    Max = o.Value<int?>("max") ?? 0,
                };

                string per = o.Value<string>("per");
                if (!string.IsNullOrEmpty(per)) DecodeScale(per, ref v);

                foreach (var p in o.Properties())
                {
                    switch (p.Name)
                    {
                        case "base": case "per": case "each": case "min": case "max": break;
                        default:
                            throw new CardTableFormatException(
                                $"数值对象里有未知字段「{p.Name}」。可用字段：base, per, each, min, max");
                    }
                }

                return v;
            }

            throw new CardTableFormatException(
                $"数值必须是一个整数（如 7）或一个对象（如 {{\"base\":3,\"per\":\"statusOnSelf:strength\",\"each\":2}}），" +
                $"实际读到 {r.TokenType}。");
        }

        private static string EncodeScale(ValueScale scale, string scaleId)
        {
            if (!ToText.TryGetValue(scale, out var text))
                throw new CardTableFormatException($"无法序列化缩放类型 {scale}——请在 EffectValueConverter 的映射表里补上它。");

            return NeedsId(scale) ? $"{text}:{scaleId}" : text;
        }

        private static void DecodeScale(string per, ref EffectValue v)
        {
            string text = per;
            string id = null;

            int colon = per.IndexOf(':');
            if (colon >= 0)
            {
                text = per.Substring(0, colon);
                id = per.Substring(colon + 1);
            }

            if (!FromText.TryGetValue(text, out var scale))
            {
                throw new CardTableFormatException(
                    $"未知的缩放来源「{text}」。合法取值：{string.Join(", ", FromText.Keys)}");
            }

            if (NeedsId(scale) && string.IsNullOrEmpty(id))
            {
                throw new CardTableFormatException(
                    $"缩放「{text}」必须指定状态 Id，写成 \"{text}:strength\" 这样。" +
                    $"不写 Id 的话这个缩放恒等于 0，而且不会报错。");
            }

            v.Scale = scale;
            v.ScaleId = id;
        }
    }

    // ================================================================ TargetSelector

    /// <summary>
    /// <see cref="TargetSelector"/> ⇄ JSON。5 个字段里通常只有 Kind 有意义，
    /// 所以默认写成一个裸字符串：<c>"target": "chosen"</c>。
    /// 只要任何一个附加字段非默认，就整体展开成对象——**读写规则完全对称**，
    /// 于是同一份表反复导入导出不会自己改变形状。
    /// </summary>
    internal sealed class TargetSelectorConverter : JsonConverter
    {
        private static readonly Dictionary<TargetKind, string> ToText =
            new Dictionary<TargetKind, string>
            {
                { TargetKind.None,            "none"           },
                { TargetKind.Self,            "self"           },
                { TargetKind.ChosenTarget,    "chosen"         },
                { TargetKind.AllEnemies,      "allEnemies"     },
                { TargetKind.AllAllies,       "allAllies"      },
                { TargetKind.AllUnits,        "allUnits"       },
                { TargetKind.RandomEnemy,     "randomEnemy"    },
                { TargetKind.LowestHpEnemy,   "lowestHpEnemy"  },
                { TargetKind.HighestHpEnemy,  "highestHpEnemy" },
                { TargetKind.PreviousTargets, "prev"           },
            };

        private static readonly Dictionary<string, TargetKind> FromText = BuildReverse();

        private static Dictionary<string, TargetKind> BuildReverse()
        {
            var d = new Dictionary<string, TargetKind>(StringComparer.Ordinal);
            foreach (var kv in ToText) d.Add(kv.Value, kv.Key);
            return d;
        }

        public override bool CanConvert(Type objectType) => objectType == typeof(TargetSelector);

        public override void WriteJson(JsonWriter w, object value, JsonSerializer s)
        {
            var v = (TargetSelector)value;

            bool plain = v.Count == 0
                         && !v.AllowDuplicates
                         && !v.ExcludeSelf
                         && string.IsNullOrEmpty(v.RequireStatusId);

            if (plain)
            {
                w.WriteValue(TextOf(v.Kind));
                return;
            }

            w.WriteStartObject();
            w.WritePropertyName("kind"); w.WriteValue(TextOf(v.Kind));
            if (v.Count != 0) { w.WritePropertyName("count"); w.WriteValue(v.Count); }
            if (v.AllowDuplicates) { w.WritePropertyName("allowDuplicates"); w.WriteValue(true); }
            if (v.ExcludeSelf) { w.WritePropertyName("excludeSelf"); w.WriteValue(true); }
            if (!string.IsNullOrEmpty(v.RequireStatusId))
            {
                w.WritePropertyName("requireStatusId"); w.WriteValue(v.RequireStatusId);
            }
            w.WriteEndObject();
        }

        public override object ReadJson(JsonReader r, Type objectType, object existing, JsonSerializer s)
        {
            if (r.TokenType == JsonToken.String)
                return new TargetSelector { Kind = KindOf((string)r.Value) };

            if (r.TokenType == JsonToken.StartObject)
            {
                var o = JObject.Load(r);
                var v = new TargetSelector
                {
                    Kind = KindOf(o.Value<string>("kind")),
                    Count = o.Value<int?>("count") ?? 0,
                    AllowDuplicates = o.Value<bool?>("allowDuplicates") ?? false,
                    ExcludeSelf = o.Value<bool?>("excludeSelf") ?? false,
                    RequireStatusId = o.Value<string>("requireStatusId"),
                };

                foreach (var p in o.Properties())
                {
                    switch (p.Name)
                    {
                        case "kind": case "count": case "allowDuplicates":
                        case "excludeSelf": case "requireStatusId": break;
                        default:
                            throw new CardTableFormatException(
                                $"目标对象里有未知字段「{p.Name}」。可用字段：" +
                                $"kind, count, allowDuplicates, excludeSelf, requireStatusId");
                    }
                }

                return v;
            }

            throw new CardTableFormatException(
                $"目标必须是一个字符串（如 \"chosen\"）或一个对象，实际读到 {r.TokenType}。");
        }

        private static string TextOf(TargetKind k)
            => ToText.TryGetValue(k, out var t) ? t : k.ToString();

        private static TargetKind KindOf(string text)
        {
            if (text != null && FromText.TryGetValue(text, out var k)) return k;
            throw new CardTableFormatException(
                $"未知的目标「{text}」。合法取值：{string.Join(", ", FromText.Keys)}");
        }
    }

    // ============================================================ ScriptableObject

    /// <summary>
    /// 内容资产引用 ⇄ Id 字符串。表里绝不出现 GUID 或文件路径——
    /// Id 是本工程唯一稳定的内容身份（存档、本地化 key、数据库查找全用它）。
    ///
    /// <para>★ 解析失败一律**抛异常中断导入**，不做「解析不到就置 null」。
    /// 置 null 会产出一张能进游戏、打出去什么都不发生、且不报任何错的卡——
    /// 正是本工程反复吃过的那种静默故障。</para>
    /// </summary>
    internal sealed class ScriptableObjectIdConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
            => typeof(ScriptableObject).IsAssignableFrom(objectType);

        public override void WriteJson(JsonWriter w, object value, JsonSerializer s)
        {
            var so = value as ScriptableObject;
            if (so == null) { w.WriteNull(); return; }

            string id = AssetIndex.IdOf(so);
            if (string.IsNullOrEmpty(id))
            {
                throw new CardTableFormatException(
                    $"资产「{so.name}」（{so.GetType().Name}）的 Id 是空的，无法写进卡表。");
            }

            w.WriteValue(id);
        }

        public override object ReadJson(JsonReader r, Type objectType, object existing, JsonSerializer s)
        {
            if (r.TokenType == JsonToken.Null) return null;

            if (r.TokenType != JsonToken.String)
            {
                throw new CardTableFormatException(
                    $"{objectType.Name} 引用必须写成 Id 字符串，实际读到 {r.TokenType}。");
            }

            string id = (string)r.Value;
            var found = AssetIndex.Find(objectType, id);
            if (found != null) return found;

            throw new CardTableFormatException(
                $"找不到 Id 为「{id}」的 {objectType.Name}。已知的 Id：" +
                $"{string.Join(", ", AssetIndex.KnownIds(objectType))}");
        }
    }

    // =================================================================== CardEffect

    /// <summary>
    /// 效果树 ⇄ JSON。多态靠 <see cref="EffectKinds.KindKey"/> 短名，字段靠反射，
    /// **两者都不需要注册表**（见 <see cref="EffectKinds"/> 的类注释）。
    /// </summary>
    internal sealed class CardEffectConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
            => typeof(CardEffect).IsAssignableFrom(objectType);

        public override void WriteJson(JsonWriter w, object value, JsonSerializer s)
        {
            if (value == null) { w.WriteNull(); return; }

            var spec = EffectKinds.ForType(value.GetType());

            w.WriteStartObject();
            w.WritePropertyName(EffectKinds.KindKey);
            w.WriteValue(spec.ShortName);

            foreach (var f in spec.Fields)
            {
                object v = f.GetValue(value);
                object dv = f.GetValue(spec.Template);

                // 与构造函数默认值相同的字段一律省略。这是表可读性的主要来源：
                // BlockEffect 只剩 {"kind":"block","amount":5}。
                if (SameAsDefault(v, dv)) continue;

                w.WritePropertyName(spec.JsonNameOf(f));

                // 用同一个 serializer 递归——于是嵌套的 List<CardEffect>（组合子的子效果）
                // 会再次走到本转换器，EffectValue / TargetSelector / SO 引用各自走它们的。
                s.Serialize(w, v);
            }

            w.WriteEndObject();
        }

        public override object ReadJson(JsonReader r, Type objectType, object existing, JsonSerializer s)
        {
            if (r.TokenType == JsonToken.Null) return null;

            if (r.TokenType != JsonToken.StartObject)
            {
                throw new CardTableFormatException(
                    $"效果必须是一个对象（含 \"{EffectKinds.KindKey}\" 字段），实际读到 {r.TokenType}。");
            }

            var o = JObject.Load(r);

            string kind = o.Value<string>(EffectKinds.KindKey);
            if (string.IsNullOrEmpty(kind))
            {
                throw new CardTableFormatException(
                    $"效果缺少 \"{EffectKinds.KindKey}\" 字段：{o.ToString(Formatting.None)}");
            }

            var spec = EffectKinds.ForShortName(kind);

            // ★ Activator 而不是 FormatterServices：必须让构造函数跑，
            //   因为「省略的键保持默认值」这条语义完全依赖构造函数
            //   （BlockEffect() 把 Target 设成 SelfOnly，DrawEffect() 设成 NoTarget）。
            //   跳过构造函数的话，所有省略了 target 的效果都会静默变成 Chosen——
            //   自己获得护甲的牌会变成「必须点一个敌人」。
            var inst = Activator.CreateInstance(spec.Type);

            foreach (var p in o.Properties())
            {
                if (p.Name == EffectKinds.KindKey) continue;

                var field = spec.FieldByJsonName(p.Name);
                if (field == null)
                {
                    throw new CardTableFormatException(
                        $"效果「{kind}」没有字段「{p.Name}」。可用字段：{spec.FieldListForError()}");
                }

                object v;
                using (var sub = p.Value.CreateReader())
                {
                    sub.Read();
                    v = s.Deserialize(sub, field.FieldType);
                }

                field.SetValue(inst, v);
            }

            return inst;
        }

        /// <summary>
        /// 字段值是否与模板一致（一致则省略不写）。
        /// 集合按「都为空」判等，因为两个空 List 是不同实例，引用比较会让每个组合子
        /// 都多写一对空数组。
        /// </summary>
        private static bool SameAsDefault(object v, object dv)
        {
            if (v == null && dv == null) return true;
            if (v == null || dv == null) return false;

            if (v is IList lv)
            {
                var ld = dv as IList;
                return lv.Count == 0 && (ld == null || ld.Count == 0);
            }

            if (v is ScriptableObject) return ReferenceEquals(v, dv);

            return v.Equals(dv);
        }
    }
}
