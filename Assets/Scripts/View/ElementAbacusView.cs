using System.Collections;
using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>显示真实收集进度：五颗下珠进位、上珠记五，集齐闪亮发牌后归位。</summary>
    [DisallowMultipleComponent]
    public sealed class ElementAbacusView : MonoBehaviour
    {
        [SerializeField] BattleUiController controller;
        [SerializeField] RectTransform[] lowerBeads;
        [SerializeField] RectTransform[] upperBeads;
        [SerializeField] Image[] icons;
        [SerializeField] CanvasGroup[] flashes;
        [SerializeField] Text[] counters;
        [SerializeField] Text hint;
        [SerializeField] Image rewardCard;
        [SerializeField] CanvasGroup rewardGroup;
        [SerializeField] BattleCardArt cardArt;
        readonly Queue<GameEvent> _events = new Queue<GameEvent>();
        readonly HashSet<int> _pendingCards = new HashSet<int>();
        readonly List<Card> _visibleHand = new List<Card>();
        readonly int[] _counts = new int[ElementDefs.Count];
        readonly int[] _lower = new int[ElementDefs.Count];
        readonly int[] _upper = new int[ElementDefs.Count];
        readonly int[] _capacities = new int[ElementDefs.Count];
        GameEngine _engine;
        int _historyIndex;
        bool _playing;
        bool Paused => Time.timeScale == 0 || controller != null && controller.SettingsOpen;
        public bool IsAnimating => _playing || _events.Count > 0;
        public int CompletedRewards { get; private set; }
        public int DisplayedCount(Element element) => _counts[(int)element];
        public int RaisedLowerCount(Element element) => _lower[(int)element];
        public int RaisedUpperCount(Element element) => _upper[(int)element];

        public void Setup(BattleUiController value) => controller = value;

        void OnEnable()
        {
            if (_engine == null) RenderSnapshot(5);
        }

        void OnDisable()
        {
            StopAllCoroutines();
            _events.Clear();
            _pendingCards.Clear();
            _playing = false;
            _engine = null;
        }

        public void Bind(GameEngine engine)
        {
            StopAllCoroutines();
            _events.Clear();
            _pendingCards.Clear();
            _playing = false;
            CompletedRewards = 0;
            _engine = engine;
            _historyIndex = engine.History.Count;
            foreach (var element in ElementDefs.All) _counts[(int)element] = engine.Bottle.Count(element);
            RenderSnapshot(engine.Bottle.Capacity);
        }

        void RenderSnapshot(int capacity)
        {
            if (lowerBeads == null || lowerBeads.Length != 25) return;
            if (rewardGroup != null) rewardGroup.alpha = 0;
            foreach (var element in ElementDefs.All)
            {
                int index = (int)element;
                _capacities[index] = capacity;
                flashes[index].alpha = 0;
                icons[index].transform.localScale = Vector3.one;
                SetProgress(index, _counts[index] % 5, _counts[index] / 5);
                ApplyPose(index, 1);
                RefreshCounter(index);
            }
        }

        public void Sync(GameEngine engine)
        {
            if (_engine != engine) Bind(engine);
            while (_historyIndex < engine.History.Count)
            {
                var step = engine.History[_historyIndex++];
                foreach (var change in step.Events)
                {
                    if (change.Type == GameEventType.BottleFill || change.Type == GameEventType.BottleCapacityChanged
                        || change.Type == GameEventType.GainCard && change.CardId > 0)
                    {
                        _events.Enqueue(change);
                        if (change.Type == GameEventType.GainCard) _pendingCards.Add(change.CardId);
                    }
                }
            }
            if (!_playing && _events.Count > 0) StartCoroutine(PlayChanges());
        }

        public IReadOnlyList<Card> HandForDisplay(IReadOnlyList<Card> hand)
        {
            if (_pendingCards.Count == 0) return hand;
            _visibleHand.Clear();
            foreach (var card in hand) if (!_pendingCards.Contains(card.Id)) _visibleHand.Add(card);
            return _visibleHand;
        }

        IEnumerator PlayChanges()
        {
            _playing = true;
            while (_events.Count > 0)
            {
                while (Paused) yield return null;
                var change = _events.Dequeue();
                int index = (int)change.Element;
                _capacities[index] = change.BottleCapacity;
                if (change.Type == GameEventType.BottleFill)
                {
                    int count = _counts[index];
                    if (count > change.BottleCount) count = 0;
                    while (count < change.BottleCount)
                    {
                        int next = Mathf.Min(change.BottleCount, (count / 5 + 1) * 5);
                        _counts[index] = next;
                        SetProgress(index, next % 5 == 0 ? 5 : next % 5, (next - 1) / 5);
                        RefreshCounter(index);
                        yield return MoveBeads(index);
                        if (next % 5 == 0 && next < change.BottleCapacity)
                        {
                            yield return new WaitForSeconds(.10f);
                            SetProgress(index, 0, (next - 1) / 5);
                            yield return MoveBeads(index);
                            SetProgress(index, 0, next / 5);
                            yield return MoveBeads(index);
                        }
                        count = next;
                    }
                }
                else if (change.Type == GameEventType.GainCard)
                {
                    yield return Flash(index);
                    yield return FlyCard(index);
                    _pendingCards.Remove(change.CardId);
                    CompletedRewards++;
                    _counts[index] = 0;
                    SetProgress(index, 0, 0);
                    RefreshCounter(index);
                    yield return MoveBeads(index);
                }
                else
                {
                    _counts[index] = change.BottleCount;
                    SetProgress(index, change.BottleCount % 5, change.BottleCount / 5);
                    RefreshCounter(index);
                    yield return MoveBeads(index);
                }
            }
            _playing = false;
        }

        void SetProgress(int index, int lower, int upper)
        {
            _lower[index] = lower;
            // ponytail: 两颗上珠覆盖本次 3/5/10 门槛；更高门槛仍显示准确数字，可扩充上珠。
            _upper[index] = Mathf.Min(upper, 2);
        }

        void RefreshCounter(int index)
        {
            counters[index].text = $"{_counts[index]}/{_capacities[index]}";
            if (hint != null) hint.text = $"集齐 {_capacities[index]} 点，生成对应元素牌";
        }

        IEnumerator MoveBeads(int index)
        {
            float elapsed = 0;
            while (elapsed < .18f)
            {
                if (Paused) { yield return null; continue; }
                elapsed += Time.deltaTime;
                ApplyPose(index, Mathf.Clamp01(Time.deltaTime * 28));
                yield return null;
            }
            while (Paused) yield return null;
            ApplyPose(index, 1);
        }

        void ApplyPose(int index, float t)
        {
            for (int bead = 0; bead < 5; bead++)
            {
                var rect = lowerBeads[index * 5 + bead];
                var pos = rect.anchoredPosition;
                float target = -(bead * 26.5f + (bead < _lower[index] ? 158 : 180));
                pos.y = Mathf.Lerp(pos.y, target, t);
                rect.anchoredPosition = pos;
            }
            for (int bead = 0; bead < 2; bead++)
            {
                var rect = upperBeads[index * 2 + bead];
                var pos = rect.anchoredPosition;
                float target = -(bead < _upper[index] ? 75 - bead * 26.5f : 53 - bead * 32);
                pos.y = Mathf.Lerp(pos.y, target, t);
                rect.anchoredPosition = pos;
                rect.GetComponent<Image>().color = new Color(1, 1, 1, _capacities[index] > 5 + bead * 5 ? 1 : .25f);
            }
        }

        IEnumerator Flash(int index)
        {
            float elapsed = 0;
            while (elapsed < .32f)
            {
                if (Paused) { yield return null; continue; }
                elapsed += Time.deltaTime;
                float strength = Mathf.Sin(Mathf.Clamp01(elapsed / .32f) * Mathf.PI);
                flashes[index].alpha = strength;
                icons[index].transform.localScale = Vector3.one * (1 + strength * .22f);
                yield return null;
            }
            while (Paused) yield return null;
            flashes[index].alpha = 0;
            icons[index].transform.localScale = Vector3.one;
        }

        IEnumerator FlyCard(int index)
        {
            if (rewardCard == null || controller == null || controller.Cards.Count == 0) yield break;
            rewardCard.sprite = cardArt != null ? cardArt.GetFace((Element)index) : null;
            if (rewardCard.sprite == null) yield break;
            var hand = (RectTransform)controller.Cards[0].transform.parent;
            Vector3 from = icons[index].transform.position;
            Vector3 to = hand.TransformPoint(hand.rect.center);
            rewardGroup.alpha = 1;
            float elapsed = 0;
            while (elapsed < .38f)
            {
                if (Paused) { yield return null; continue; }
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / .38f);
                rewardCard.transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, t));
                rewardGroup.alpha = t > .8f ? (1 - t) * 5 : 1;
                yield return null;
            }
            while (Paused) yield return null;
            rewardGroup.alpha = 0;
        }
    }
}
