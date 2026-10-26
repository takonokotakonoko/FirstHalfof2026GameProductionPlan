
using UnityEngine;
using UnityEngine.SceneManagement;

public class WeaponSelectManager : MonoBehaviour
{
    // PlayerAttack.WeaponType の値を保存する
    private const string WeaponKey = "SelectedWeapon";

    // 拳を選択
    public void SelectFist()
    {
        PlayerPrefs.SetInt(
            WeaponKey,
            (int)PlayerAttack.WeaponType.Fist
        );
        PlayerPrefs.Save();

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

        Debug.Log("武器選択：銃");
    }

    // ゲーム開始
    public void StartGame()
    {
        SceneManager.LoadScene("game");
    }

    // タイトルに戻る
    public void BackToTitle()
    {
        SceneManager.LoadScene("title");
    }

    public void OpenWeaponSelect()
    {
        SceneManager.LoadScene("WeaponSelect");
    }
}
