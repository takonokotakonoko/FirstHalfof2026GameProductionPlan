using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// シーン遷移を行うためのスクリプト。
/// ボタンや他のスクリプトからシーン名を指定して呼び出せます。
/// </summary>
public class SceneLoader : MonoBehaviour
{
    /// <summary>
    /// 指定した名前のシーンへ遷移します。
    /// 
    /// 使用例：
    /// SceneLoader.LoadScene("GameScene");
    /// </summary>
    /// <param name="sceneName">遷移先のシーン名</param>
    public static void LoadScene(string sceneName)
    {
        // シーン名が空の場合は処理しない
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("SceneLoader：シーン名が指定されていません。");
            return;
        }

        // 指定されたシーンを読み込む
        SceneManager.LoadScene(sceneName);
    }
}