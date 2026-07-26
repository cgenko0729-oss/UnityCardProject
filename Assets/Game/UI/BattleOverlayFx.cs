using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 盖在战斗界面上的两个整屏效果：回合过场横幅、受创全屏闪光。
    ///
    /// ★ 单独一个组件而不是塞进 BattleScreen：那个文件已经 1200 行，
    ///   而这两样东西与战斗状态**完全无关**——它们只认「播一下」这一个指令。
    ///
    /// ★ 整层不吃射线。它铺在战场和 HUD 之上，只要有一个节点开着 raycastTarget，
    ///   横幅扫过的那一秒里玩家点什么都点不动，而且完全看不出原因。
    /// </summary>
    public class BattleOverlayFx : MonoBehaviour
    {
        // ============================================================ 参数

        private const float BannerGrowTime = 0.18f;
        private const float BannerSlideTime = 0.22f;
        private const float BannerHold = 0.42f;
        private const float BannerSlideOut = 0.20f;

        /// <summary>横幅文字滑入 / 滑出的横向距离。</summary>
        private const float BannerSlide = 300f;

        private const float FlashFadeTime = 0.34f;

        private RectTransform _bannerRoot;
        private RectTransform _bannerBar;
        private CanvasGroup _bannerGroup;
        private TMP_Text _bannerText;

        private Image _flash;

        public static BattleOverlayFx Create(RectTransform parent)
        {
            var root = UIFactory.CreateEmpty(parent, "OverlayFx");
            UIFactory.Stretch(root);

            var fx = root.gameObject.AddComponent<BattleOverlayFx>();
            fx.BuildFlash(root);
            fx.BuildBanner(root);
            return fx;
        }

        private void BuildFlash(RectTransform root)
        {
            var rt = UIFactory.CreatePanel(root, "ScreenFlash", new Color(1f, 0.15f, 0.15f, 0f));
            UIFactory.Stretch(rt);

            _flash = rt.GetComponent<Image>();
            _flash.raycastTarget = false;
        }

        private void BuildBanner(RectTransform root)
        {
            _bannerRoot = UIFactory.CreateEmpty(root, "Banner");
            UIFactory.SetAnchored(_bannerRoot, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, 76f), new Vector2(0f, 166f));

            _bannerGroup = _bannerRoot.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.blocksRaycasts = false;
            _bannerGroup.interactable = false;

            _bannerBar = UIFactory.CreatePanel(_bannerRoot, "Bar", new Color(0f, 0f, 0f, 0.62f));
            UIFactory.Stretch(_bannerBar);
            _bannerBar.GetComponent<Image>().raycastTarget = false;

            _bannerText = UIFactory.CreateText(_bannerRoot, "Text", "", 46);
            UIFactory.Stretch(_bannerText.rectTransform);

            _bannerRoot.gameObject.SetActive(false);
        }

        // ============================================================ 横幅

        /// <summary>
        /// 播一条过场横幅。
        ///
        /// ★ 解决的是「点完结束回合，一堆事情同时发生，看不清谁打了谁」——
        ///   逻辑上敌人回合是一整段，但画面上它和玩家回合之间没有任何分界线。
        ///   一条扫过去的横幅就是那条线。
        /// </summary>
        public void ShowBanner(string text, Color color)
        {
            if (_bannerRoot == null) return;

            DOTween.Kill(_bannerRoot);

            _bannerRoot.gameObject.SetActive(true);
            _bannerText.text = text;
            _bannerText.color = color;

            _bannerGroup.alpha = 0f;
            _bannerBar.localScale = new Vector3(0f, 1f, 1f);
            _bannerText.rectTransform.anchoredPosition = new Vector2(-BannerSlide, 0f);

            var seq = DOTween.Sequence().SetTarget(_bannerRoot);

            seq.Append(_bannerBar.DOScaleX(1f, BannerGrowTime).SetEase(Ease.OutQuad));
            seq.Join(DOTween.To(() => _bannerGroup.alpha, a => _bannerGroup.alpha = a, 1f, BannerGrowTime)
                            .SetTarget(_bannerRoot));
            seq.Join(DOTween.To(() => _bannerText.rectTransform.anchoredPosition,
                                v => _bannerText.rectTransform.anchoredPosition = v,
                                Vector2.zero, BannerSlideTime).SetEase(Ease.OutQuad).SetTarget(_bannerRoot));

            seq.AppendInterval(BannerHold);

            seq.Append(DOTween.To(() => _bannerText.rectTransform.anchoredPosition,
                                  v => _bannerText.rectTransform.anchoredPosition = v,
                                  new Vector2(BannerSlide, 0f), BannerSlideOut).SetEase(Ease.InQuad)
                              .SetTarget(_bannerRoot));
            seq.Join(DOTween.To(() => _bannerGroup.alpha, a => _bannerGroup.alpha = a, 0f, BannerSlideOut)
                            .SetTarget(_bannerRoot));

            seq.OnComplete(() => { if (_bannerRoot != null) _bannerRoot.gameObject.SetActive(false); });
        }

        // ============================================================ 全屏闪光

        /// <summary>
        /// 全屏闪一下。★ 受 <see cref="FeedbackSettings.FlashEnabled"/> 控制——
        /// 整屏变色是光敏感人群最需要能关掉的一项。
        /// </summary>
        public void Flash(Color color, float alpha, float duration = FlashFadeTime)
        {
            if (_flash == null || !FeedbackSettings.FlashEnabled) return;

            DOTween.Kill(_flash.rectTransform);

            color.a = Mathf.Clamp01(alpha);
            _flash.color = color;

            DOTween.To(() => _flash.color, c => _flash.color = c,
                       new Color(color.r, color.g, color.b, 0f), duration)
                   .SetEase(Ease.OutQuad)
                   .SetTarget(_flash.rectTransform);
        }

        /// <summary>★ 铁律 45：界面消失时收掉自己的 tween，并把两样东西复位。</summary>
        private void OnDisable()
        {
            if (_bannerRoot != null)
            {
                DOTween.Kill(_bannerRoot);
                _bannerRoot.gameObject.SetActive(false);
            }

            if (_flash != null)
            {
                DOTween.Kill(_flash.rectTransform);
                var c = _flash.color;
                c.a = 0f;
                _flash.color = c;
            }
        }
    }
}
