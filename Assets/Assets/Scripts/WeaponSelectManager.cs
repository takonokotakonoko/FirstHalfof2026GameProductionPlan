
using UnityEngine;
using UnityEngine.SceneManagement;

public class WeaponSelectManager : MonoBehaviour
{
    // PlayerAttack.WeaponType の値を保存する
    private const string WeaponKey = "SelectedWeapon";

    [Header("効果音（SoundPlayerに登録した名前）")]
    [Tooltip("武器を選んだときに鳴らす音")]
    [SerializeField] private string selectSoundName = "tap";

    [Tooltip("シーンを移るときに鳴らす音")]
    [SerializeField] private string transitionSoundName = "screenTransition";

    // 拳を選択
    public void SelectFist()
    {
        PlayerPrefs.SetInt(
            WeaponKey,
            (int)PlayerAttack.WeaponType.Fist
        );
        PlayerPrefs.Save();
        SoundPlayer.PlayIfExists(selectSoundName);

        Debug.Log("武器選択：拳");
    }

    // 銃を選択
    public void SelectGun()
    {
        PlayerPrefs.SetInt(
            WeaponKey,
            (int)PlayerAttack.WeaponType.Gun
        );
        PlayerPrefs.Save();
        SoundPlayer.PlayIfExists(selectSoundName);

        Debug.Log("武器選択：銃");
    }

    // ゲーム開始
    public void StartGame()
    {
        SoundPlayer.PlayIfExists(transitionSoundName);
        SceneManager.LoadScene("game2");
    }

    // タイトルに戻る
    public void BackToTitle()
    {
        SoundPlayer.PlayIfExists(transitionSoundName);
        SceneManager.LoadScene("title");
    }

    public void OpenWeaponSelect()
    {
        SoundPlayer.PlayIfExists(transitionSoundName);
        SceneManager.LoadScene("WeaponSelect");
    }
}
