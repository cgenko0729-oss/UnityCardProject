using System.Collections.Generic;
using Game.Events;
using Game.RunEffects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 随机事件界面。文本 + 若干选项，选项的效果全部是配置出来的 <see cref="RunEffect"/>，
    /// 加一个新事件不需要写任何代码。
    ///
    /// 选项里若产生了「需要玩家选牌」的请求（删卡 / 升级卡 / 选一张牌拿走），
    /// 会在效果跑完后逐个弹 <see cref="CardPickerScreen"/>，全部处理完才离开事件。
    /// </summary>
    public class EventScreen : ScreenBase
    {
        private EventDefinition _def;
        private RectTransform _optionList;
        private TMP_Text _bodyText;
        private TMP_Text _resultText;
        private Button _leaveButton;

        private readonly List<Button> _optionButtons = new List<Button>();
        private readonly Queue<RunChoiceRequest> _pendingChoices = new Queue<RunChoiceRequest>();

        private bool _resolved;

        protected override void Build()
        {
            _def = Manager.CurrentEvent;

            var title = UIFactory.CreateText(Root, "Title",
                _def != null ? _def.Title : "……", 40, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.7f));
            UIFactory.SetAnchored(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -140), new Vector2(0, -76));

            _bodyText = UIFactory.CreateText(Root, "Body",
                _def != null ? _def.Description : "这里空无一物。", 24, TextAnchor.UpperLeft,
                new Color(0.86f, 0.88f, 0.92f));
            UIFactory.SetAnchored(_bodyText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-460, -420), new Vector2(460, -150));

            _resultText = UIFactory.CreateText(Root, "Result", "", 24, TextAnchor.UpperCenter,
                new Color(1f, 0.88f, 0.6f));
            UIFactory.SetAnchored(_resultText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-460, -560), new Vector2(460, -420));

            _optionList = UIFactory.CreateScrollView(Root, "Options", 12f);
            UIFactory.SetAnchored((RectTransform)_optionList.parent, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(-460, 120), new Vector2(460, 420));

            BuildOptions();

            _leaveButton = UIFactory.CreateTextButton(Root, "Leave", "离　开", 30,
                new Color(0.30f, 0.36f, 0.42f), Leave);
            var rt = (RectTransform)_leaveButton.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(340, 76);
            rt.anchoredPosition = new Vector2(0, 26);
            _leaveButton.gameObject.SetActive(_def == null);
        }

        private void BuildOptions()
        {
            if (_def == null || _def.Options == null) return;

            var probe = Manager.NewEffectContext();

            for (int i = 0; i < _def.Options.Count; i++)
            {
                int index = i;
                var option = _def.Options[i];

                bool enabled = option.Condition.Test(probe)
                               && RunEffectResolver.CanApplyAll(option.Effects, probe);

                string detail = RunEffectResolver.DescribeAll(option.Effects, probe);
                string label = string.IsNullOrEmpty(detail) ? option.Text : $"{option.Text}　（{detail}）";
                if (!enabled && !string.IsNullOrEmpty(option.DisabledHint)) label += $"　— {option.DisabledHint}";

                var btn = UIFactory.CreateTextButton(_optionList, "Option" + i, label, 24,
                    new Color(0.26f, 0.32f, 0.40f), () => Choose(index));
                UIFactory.SetLayoutHeight((RectTransform)btn.transform, 76);

                var text = UIFactory.LabelOf(btn);
                UIFactory.SetAlignment(text, TextAnchor.MiddleLeft);
                text.rectTransform.offsetMin = new Vector2(18, 0);
                text.rectTransform.offsetMax = new Vector2(-18, 0);

                UIFactory.SetInteractable(btn, enabled, new Color(0.26f, 0.32f, 0.40f));
                _optionButtons.Add(btn);
            }
        }

        // ================================================================= 选择

        private void Choose(int index)
        {
            if (_resolved) return;
            var option = _def.Options[index];

            var ctx = Manager.NewEffectContext();
            RunEffectResolver.ResolveAll(option.Effects, ctx);

            // 结果文案：配置里的旁白 + 效果自己产出的日志
            var sb = new System.Text.StringBuilder(128);
            if (!string.IsNullOrEmpty(option.ResultText)) sb.AppendLine(option.ResultText);
            for (int i = 0; i < ctx.Log.Count; i++) sb.AppendLine(ctx.Log[i]);
            _resultText.text = sb.ToString();

            while (ctx.Choices.Count > 0) _pendingChoices.Enqueue(ctx.Choices.Dequeue());

            if (option.EndsEvent)
            {
                _resolved = true;
                for (int i = 0; i < _optionButtons.Count; i++)
                    UIFactory.SetInteractable(_optionButtons[i], false, Color.gray);
            }

            ProcessNextChoice();
        }

        /// <summary>逐个处理效果排出来的选牌请求，全部处理完才允许离开。</summary>
        private void ProcessNextChoice()
        {
            if (_pendingChoices.Count == 0)
            {
                _leaveButton.gameObject.SetActive(_resolved);
                return;
            }

            var req = _pendingChoices.Dequeue();
            switch (req.Kind)
            {
                // ★ 不可取消：代价（金币 / 生命）在效果里已经付掉了，
                //   允许取消等于白扣玩家的钱。休息点那种「还没付代价」的地方才给取消。
                case RunChoiceKind.RemoveCard:
                    App.ShowCardPicker(req.Title ?? "移除一张卡", Run.Deck, null, req.Count, false, picks =>
                    {
                        // 倒序移除，避免前面的下标影响后面的
                        picks.Sort();
                        for (int i = picks.Count - 1; i >= 0; i--) Run.RemoveCard(Run.Deck[picks[i]]);
                        ProcessNextChoice();
                    });
                    break;

                case RunChoiceKind.UpgradeCard:
                {
                    var upgradable = new List<Cards.CardInstance>();
                    Run.GetUpgradableCards(upgradable);
                    if (upgradable.Count == 0) { ProcessNextChoice(); break; }

                    App.ShowCardPicker(req.Title ?? "升级一张卡", upgradable, null,
                        Mathf.Min(req.Count, upgradable.Count), false, picks =>
                        {
                            for (int i = 0; i < picks.Count; i++) upgradable[picks[i]].Upgrade();
                            ProcessNextChoice();
                        });
                    break;
                }

                case RunChoiceKind.AddOneOfCards:
                    App.ShowCardPicker(req.Title ?? "选择一张卡", null, req.Options, 1, true, picks =>
                    {
                        if (picks.Count > 0) Run.AddCard(req.Options[picks[0]]);
                        ProcessNextChoice();
                    });
                    break;

                default:
                    ProcessNextChoice();
                    break;
            }
        }

        private void Leave() => Manager.ReturnToMap();
    }
}
