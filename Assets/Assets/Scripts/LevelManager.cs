using UnityEngine;

public class LevelManager : MonoBehaviour
{
    public int totalExp = 0;    //enemiesがもつexpをここに加算
    public int expToNextLv; //レベルアップのための必要経験値
    PlayerHealth player;

    private void Start()
    {
        //  PlayerHealth.csのコンポーネントを見つける。
        player = GameObject.FindAnyObjectByType<PlayerHealth>();
        if (player == null)
        {
            Debug.LogError("LevelManager: PlayerHealth が見つかりません。", this);
        }
    }

    private void Update()
    {

    }

    //  EnemyHealth.cs側でtotalExpを加算する
    public void GetExp(int amount)
    {
        totalExp += amount;
        if (expToNextLv <= 0)
        {
            Debug.LogError("LevelManager: expToNextLv は1以上に設定してください。", this);
            return;
        }

        while (totalExp >= expToNextLv)
        {
            if (player == null)
            {
                Debug.LogError("LevelManager: PlayerHealth が設定されていないため、レベルアップできません。", this);
                return;
            }

            totalExp -= expToNextLv;
            player.LevelUp();
        }
    }


}
