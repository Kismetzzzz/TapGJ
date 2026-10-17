using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>显示场景里已有的排行榜控件；平台接入层通过事件请求和显示方法对接。</summary>
    [DisallowMultipleComponent]
    public sealed class LeaderboardPanel : MonoBehaviour
    {
        public enum Scope { Global, Friends }

        public struct Entry
        {
            public int Rank;
            public string PlayerName;
            public long ElapsedMilliseconds;
        }

        [Serializable]
        public sealed class Row
        {
            public GameObject root;
            public Text rank;
            public Text playerName;
            public Text duration;
        }

        [SerializeField] Button loginButton;
        [SerializeField] Button globalButton;
        [SerializeField] Button friendsButton;
        [SerializeField] Button refreshButton;
        [SerializeField] Button closeButton;
        [SerializeField] Text accountLabel;
        [SerializeField] Text loginButtonLabel;
        [SerializeField] Text personalBestLabel;
        [SerializeField] Text personalRankLabel;
        [SerializeField] Text statusLabel;
        [SerializeField] GameObject globalIndicator;
        [SerializeField] GameObject friendsIndicator;
        [SerializeField] GameObject emptyState;
        [SerializeField] ScrollRect scrollRect;
        [SerializeField] Row[] rows;

        public Scope SelectedScope { get; private set; }
        public Button FirstSelectedButton => loginButton.interactable ? loginButton : globalButton;
        public event Action LoginRequested;
        public event Action<Scope> RefreshRequested;

        void OnEnable()
        {
            loginButton.onClick.AddListener(RequestLogin);
            globalButton.onClick.AddListener(SelectGlobal);
            friendsButton.onClick.AddListener(SelectFriends);
            refreshButton.onClick.AddListener(RequestRefresh);
            RefreshScopeIndicators();
        }

        void OnDisable()
        {
            loginButton.onClick.RemoveListener(RequestLogin);
            globalButton.onClick.RemoveListener(SelectGlobal);
            friendsButton.onClick.RemoveListener(SelectFriends);
            refreshButton.onClick.RemoveListener(RequestRefresh);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.PageDown))
                scrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollRect.verticalNormalizedPosition - .4f);
            if (Input.GetKeyDown(KeyCode.PageUp))
                scrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollRect.verticalNormalizedPosition + .4f);
        }

        void SelectGlobal() => SelectScope(Scope.Global);
        void SelectFriends() => SelectScope(Scope.Friends);

        void SelectScope(Scope scope)
        {
            if (SelectedScope == scope) return;
            SelectedScope = scope;
            RefreshScopeIndicators();
            RequestRefresh();
        }

        void RefreshScopeIndicators()
        {
            globalIndicator.SetActive(SelectedScope == Scope.Global);
            friendsIndicator.SetActive(SelectedScope == Scope.Friends);
        }

        void RequestLogin()
        {
            if (LoginRequested == null)
                statusLabel.text = "登录服务暂不可用，请稍后再试。";
            else
                LoginRequested.Invoke();
        }

        void RequestRefresh()
        {
            if (RefreshRequested == null)
                ShowUnavailable();
            else
            {
                ShowEmpty("正在获取排行榜…");
                RefreshRequested.Invoke(SelectedScope);
            }
        }

        public void ShowAccount(string playerName, long? bestMilliseconds = null, int? rank = null)
        {
            bool loggedIn = !string.IsNullOrWhiteSpace(playerName);
            accountLabel.text = loggedIn ? playerName : "尚未登录 TapTap";
            loginButtonLabel.text = loggedIn ? "已登录" : "TapTap 登录";
            loginButton.interactable = !loggedIn;
            var globalNavigation = globalButton.navigation;
            globalNavigation.selectOnUp = globalNavigation.selectOnLeft = loggedIn ? closeButton : loginButton;
            globalButton.navigation = globalNavigation;
            var closeNavigation = closeButton.navigation;
            closeNavigation.selectOnDown = closeNavigation.selectOnRight = FirstSelectedButton;
            closeButton.navigation = closeNavigation;
            personalBestLabel.text = loggedIn && bestMilliseconds.HasValue
                ? FormatTime(bestMilliseconds.Value) : "--:--.--";
            personalRankLabel.text = loggedIn && rank.HasValue && rank.Value > 0
                ? $"第 {rank.Value} 名" : "暂无排名";
        }

        public void ShowEntries(IReadOnlyList<Entry> entries)
        {
            // ponytail: 页面只展示已排序的前 10 名；需要更长的榜单时再接分页。
            int count = entries == null ? 0 : Mathf.Min(entries.Count, rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].root.SetActive(i < count);
                if (i >= count) continue;
                rows[i].rank.text = entries[i].Rank > 0 ? entries[i].Rank.ToString("00") : "—";
                rows[i].playerName.text = string.IsNullOrWhiteSpace(entries[i].PlayerName)
                    ? "玩家" : entries[i].PlayerName;
                rows[i].duration.text = FormatTime(entries[i].ElapsedMilliseconds);
            }
            emptyState.SetActive(count == 0);
            scrollRect.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, count * 52f);
            scrollRect.verticalNormalizedPosition = 1f;
            statusLabel.text = count == 0 ? "还没有通关纪录，来创造第一个吧。" : "仅记录成功通关的最快用时。";
        }

        public void ShowUnavailable() => ShowEmpty("排行榜暂不可用，请稍后再试。");

        void ShowEmpty(string message)
        {
            ShowEntries(null);
            statusLabel.text = message;
        }

        public static string FormatTime(long milliseconds)
        {
            if (milliseconds < 0) return "--:--.--";
            return $"{milliseconds / 60000:00}:{milliseconds / 1000 % 60:00}.{milliseconds / 10 % 100:00}";
        }
    }
}
