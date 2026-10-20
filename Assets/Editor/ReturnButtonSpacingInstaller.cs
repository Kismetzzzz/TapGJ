using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ReturnButtonSpacingInstaller
{
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        var panel = Object.FindFirstObjectByType<Canvas>().transform.Find("NoticePanel");
        var button = (RectTransform)panel.Find("CloseButton");
        button.anchoredPosition = new Vector2(0, -171);
        PrefabUtility.RecordPrefabInstancePropertyModifications(button);
        var message = (RectTransform)panel.Find("Message");
        message.anchoredPosition = new Vector2(0, 20);
        message.sizeDelta = new Vector2(760, 240);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[TapGJ] 返回按钮移入面板底部，正文与按钮之间保留间距。");
    }
}
