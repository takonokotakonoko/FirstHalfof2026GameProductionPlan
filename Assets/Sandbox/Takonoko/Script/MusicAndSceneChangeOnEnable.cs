using System.Collections;
using UnityEngine;

/// <summary>
/// このスクリプトを取り付けたGameObjectが
/// アクティブ（SetActive(true)）になった瞬間に、
///
/// ・MusicPlayerで指定したBGMを再生
/// ・指定秒数待ったあと、指定したシーンへ遷移
///
/// を行うスクリプトです。
///
/// BGM名・待ち時間・遷移先シーン名はInspectorから設定できます。
/// </summary>
public class MusicAndSceneChangeOnEnable : MonoBehaviour
{
    // =========================================================
    // Inspector設定
    // =========================================================

    [Header("BGM設定")]
    [Tooltip("再生するBGM名（MusicPlayerに登録した名前）。空欄の場合は再生しません")]
    [SerializeField]
    private string musicName;


    [Header("シーン遷移設定")]
    [Tooltip("BGM再生からシーン遷移までの待ち時間（秒）")]
    [SerializeField]
    private float delaySeconds = 3f;

    [Tooltip("遷移先のシーン名（Build Settingsに登録されている必要があります）")]
    [SerializeField]
    private string sceneName;


    // =========================================================
    // アクティブになった瞬間の処理
    // =========================================================

    private void OnEnable()
    {
        // -------------------------
        // BGMの再生
        // -------------------------

        if (MusicPlayer.Instance != null)
        {
            MusicPlayer.Instance.Play(musicName);
        }
        else
        {
            Debug.LogWarning(
                "MusicAndSceneChangeOnEnable：MusicPlayerが見つからないため、BGMを再生できません。"
            );
        }


        // -------------------------
        // 指定秒数後にシーン遷移
        // -------------------------

        StartCoroutine(LoadSceneAfterDelay());
    }


    // =========================================================
    // シーン遷移
    // =========================================================

    /// <summary>
    /// delaySeconds秒待ってから、指定したシーンへ遷移します。
    ///
    /// ゲーム終了時などにTime.timeScaleが0になっていても
    /// 待ち時間が進むように、実時間で待機します。
    /// </summary>
    private IEnumerator LoadSceneAfterDelay()
    {
        yield return new WaitForSecondsRealtime(delaySeconds);

        SceneLoader.LoadScene(sceneName);


        // -------------------------
        // このGameObjectを非アクティブにする
        // -------------------------

        // SceneManager.LoadScene()の読み込みはフレームの最後に行われるため、
        // ここで非アクティブにしてもシーン遷移は止まりません。
        gameObject.SetActive(false);
    }
}
