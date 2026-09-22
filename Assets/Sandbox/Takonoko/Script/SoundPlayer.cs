using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 効果音（SE）を管理するスクリプト。
/// Inspectorに登録した効果音を「名前」で指定して再生できます。
///
/// 使用例：
/// SoundPlayer.Instance.Play("Click");
/// SoundPlayer.Instance.SetMasterVolume(0.5f);
/// </summary>
public class SoundPlayer : MonoBehaviour
{
    /// <summary>
    /// Inspectorから登録する効果音1つ分のデータ。
    /// </summary>
    [System.Serializable]
    public class SoundData
    {
        [Tooltip("外部から効果音を呼び出すときに使用する名前")]
        public string soundName;

        [Tooltip("再生するAudioClip")]
        public AudioClip clip;

        [Range(0f, 1f)]
        [Tooltip("この効果音自体の音量")]
        public float volume = 1f;

        [Min(0f)]
        [Tooltip("この音を鳴らしてから、次に同じ音を鳴らせるまでの秒数")]
        public float cooldown = 0.1f;
    }


    // どこからでもSoundPlayerを取得できるようにする
    public static SoundPlayer Instance { get; private set; }


    [Header("効果音設定")]
    [Tooltip("使用する効果音を登録してください")]
    [SerializeField]
    private List<SoundData> soundList = new List<SoundData>();


    [Header("マスターボリューム")]
    [Range(0f, 1f)]
    [SerializeField]
    private float masterVolume = 1f;


    // 実際に効果音を再生するAudioSource
    private AudioSource audioSource;


    // 各効果音を最後に再生した時間を保存する
    //
    // 例：
    // "Click" → 10.5秒
    // "Explosion" → 15.2秒
    private Dictionary<string, float> lastPlayTimes
        = new Dictionary<string, float>();


    private void Awake()
    {
        // SoundPlayerが既に存在している場合、
        // 新しく生成された方を削除する
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // シーンが変わっても残す
        DontDestroyOnLoad(gameObject);

        // AudioSourceを取得
        audioSource = GetComponent<AudioSource>();

        // AudioSourceがなければ自動で追加
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // 効果音はループさせない
        audioSource.loop = false;

        // Awake時に勝手に音を鳴らさない
        audioSource.playOnAwake = false;
    }


    /// <summary>
    /// 名前を指定して効果音を再生します。
    ///
    /// 使用例：
    /// SoundPlayer.Instance.Play("Click");
    /// </summary>
    public void Play(string soundName)
    {
        // 登録されている効果音から
        // 指定された名前のものを探す
        SoundData sound =
            soundList.Find(x => x.soundName == soundName);

        // 見つからなかった場合
        if (sound == null)
        {
            Debug.LogWarning(
                "SoundPlayer：「" + soundName +
                "」という効果音は登録されていません。"
            );

            return;
        }

        // AudioClipが設定されていない場合
        if (sound.clip == null)
        {
            Debug.LogWarning(
                "SoundPlayer：「" + soundName +
                "」にAudioClipが設定されていません。"
            );

            return;
        }


        // ----------------------------
        // 連続再生防止処理
        // ----------------------------

        if (lastPlayTimes.TryGetValue(
            soundName,
            out float lastPlayTime))
        {
            // 最後に鳴らしてから何秒経ったか計算
            float elapsedTime =
                Time.unscaledTime - lastPlayTime;

            // cooldown秒経っていなければ再生しない
            if (elapsedTime < sound.cooldown)
            {
                return;
            }
        }


        // ----------------------------
        // 効果音を再生
        // ----------------------------

        // 効果音ごとの音量 × マスターボリューム
        float finalVolume =
            sound.volume * masterVolume;

        // PlayOneShotを使うことで、
        // 他の効果音が再生中でも重ねて鳴らせる
        audioSource.PlayOneShot(
            sound.clip,
            finalVolume
        );


        // 最後に鳴らした時間を保存
        lastPlayTimes[soundName] =
            Time.unscaledTime;
    }


    /// <summary>
    /// 効果音全体のマスターボリュームを設定します。
    /// 0～1の値を指定してください。
    ///
    /// 0   = 無音
    /// 0.5 = 半分
    /// 1   = 最大
    ///
    /// 使用例：
    /// SoundPlayer.Instance.SetMasterVolume(0.5f);
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        // 0～1の範囲に収める
        masterVolume = Mathf.Clamp01(volume);
    }


    /// <summary>
    /// 現在のマスターボリュームを取得します。
    /// </summary>
    public float GetMasterVolume()
    {
        return masterVolume;
    }
}