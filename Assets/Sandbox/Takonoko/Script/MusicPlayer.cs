using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BGMを管理するスクリプト。
/// Inspectorに登録した音楽を「名前」で指定して再生できます。
///
/// また、Inspectorから「最初に再生するBGM」を指定できます。
/// 初期BGM名が空欄、または対応するBGMが登録されていない場合は
/// 何も再生しません。
///
/// 使用例：
/// MusicPlayer.Instance.Play("Title");
/// MusicPlayer.Instance.SetMasterVolume(0.5f);
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    /// <summary>
    /// Inspectorから登録するBGM1曲分のデータ。
    /// </summary>
    [System.Serializable]
    public class MusicData
    {
        [Tooltip("外部からBGMを呼び出すときに使用する名前")]
        public string musicName;

        [Tooltip("再生するAudioClip")]
        public AudioClip clip;

        [Range(0f, 1f)]
        [Tooltip("このBGM自体の音量")]
        public float volume = 1f;
    }


    // =========================================================
    // シングルトン
    // =========================================================

    // どこからでもMusicPlayerを取得できるようにする
    public static MusicPlayer Instance { get; private set; }


    // =========================================================
    // Inspector設定
    // =========================================================

    [Header("BGM設定")]
    [Tooltip("使用するBGMを登録してください")]
    [SerializeField]
    private List<MusicData> musicList = new List<MusicData>();


    [Header("初期BGM設定")]
    [Tooltip("ゲーム開始時に再生するBGM名。空欄の場合は何も再生しません")]
    [SerializeField]
    private string initialMusicName;


    [Header("フェード設定")]

    [Tooltip("現在のBGMが消えるまでの時間")]
    [SerializeField]
    private float fadeOutTime = 1f;

    [Tooltip("新しいBGMが最大音量になるまでの時間")]
    [SerializeField]
    private float fadeInTime = 1f;


    [Header("マスターボリューム")]

    [Range(0f, 1f)]
    [SerializeField]
    private float masterVolume = 1f;


    // =========================================================
    // 内部で使用する変数
    // =========================================================

    // 実際に音を鳴らすAudioSource
    private AudioSource audioSource;

    // 現在実行中のフェード処理
    private Coroutine fadeCoroutine;

    // 現在再生中のBGMデータ
    private MusicData currentMusic;


    // =========================================================
    // 初期化
    // =========================================================

    private void Awake()
    {
        // MusicPlayerが既に存在している場合、
        // 新しく生成された方を削除する
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // シーンが変わってもMusicPlayerを残す
        DontDestroyOnLoad(gameObject);


        // -------------------------
        // AudioSourceの準備
        // -------------------------

        // AudioSourceを取得
        audioSource = GetComponent<AudioSource>();

        // AudioSourceが付いていない場合は自動追加
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // BGMなのでループ再生する
        audioSource.loop = true;

        // Awake時に勝手に再生されないようにする
        audioSource.playOnAwake = false;
    }


    private void Start()
    {
        // -------------------------
        // 初期BGMの再生
        // -------------------------

        // 初期BGM名が空欄の場合は何もしない
        if (string.IsNullOrWhiteSpace(initialMusicName))
        {
            return;
        }

        // 登録されているBGMから
        // 初期BGMと同じ名前のものを探す
        MusicData music = musicList.Find(
            x => x.musicName == initialMusicName
        );

        // 対応するBGMが存在しない場合
        if (music == null)
        {
            Debug.LogWarning(
                "MusicPlayer：初期BGM「"
                + initialMusicName
                + "」は登録されていないため、再生しません。"
            );

            return;
        }

        // AudioClipが設定されていない場合
        if (music.clip == null)
        {
            Debug.LogWarning(
                "MusicPlayer：初期BGM「"
                + initialMusicName
                + "」にはAudioClipが設定されていないため、再生しません。"
            );

            return;
        }

        // 初期BGMを再生
        Play(initialMusicName);
    }


    // =========================================================
    // BGM再生
    // =========================================================

    /// <summary>
    /// 名前を指定してBGMを再生します。
    ///
    /// 使用例：
    /// MusicPlayer.Instance.Play("Battle");
    /// </summary>
    public void Play(string musicName)
    {
        // 名前が空欄の場合は何もしない
        if (string.IsNullOrWhiteSpace(musicName))
        {
            return;
        }


        // 登録されているBGMから同じ名前を探す
        MusicData music = musicList.Find(
            x => x.musicName == musicName
        );


        // 見つからなかった場合
        if (music == null)
        {
            Debug.LogWarning(
                "MusicPlayer：「"
                + musicName
                + "」というBGMは登録されていません。"
            );

            return;
        }


        // AudioClipが設定されていない場合
        if (music.clip == null)
        {
            Debug.LogWarning(
                "MusicPlayer：「"
                + musicName
                + "」にはAudioClipが設定されていません。"
            );

            return;
        }


        // フェード処理中なら一度止める
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }


        // 既にBGMが鳴っている場合
        if (audioSource.isPlaying)
        {
            // 現在の曲をフェードアウトしてから、
            // 新しい曲をフェードインする
            fadeCoroutine = StartCoroutine(
                ChangeMusicCoroutine(music)
            );
        }
        else
        {
            // 何も鳴っていない場合は
            // そのままフェードインする
            fadeCoroutine = StartCoroutine(
                FadeInCoroutine(music)
            );
        }
    }


    // =========================================================
    // BGM切り替え
    // =========================================================

    /// <summary>
    /// 現在のBGMをフェードアウトしたあと、
    /// 新しいBGMをフェードインします。
    /// </summary>
    private IEnumerator ChangeMusicCoroutine(MusicData newMusic)
    {
        // =====================================================
        // フェードアウト
        // =====================================================

        float startVolume = audioSource.volume;
        float timer = 0f;


        // fadeOutTimeが0以下なら
        // 即座に音量を0にする
        if (fadeOutTime <= 0f)
        {
            audioSource.volume = 0f;
        }
        else
        {
            // fadeOutTime秒かけて音量を0にする
            while (timer < fadeOutTime)
            {
                timer += Time.unscaledDeltaTime;

                float rate = timer / fadeOutTime;

                audioSource.volume =
                    Mathf.Lerp(startVolume, 0f, rate);

                yield return null;
            }

            // 確実に0にする
            audioSource.volume = 0f;
        }


        // 現在の曲を停止
        audioSource.Stop();


        // =====================================================
        // 新しいBGMに変更
        // =====================================================

        currentMusic = newMusic;

        audioSource.clip = newMusic.clip;

        audioSource.volume = 0f;

        audioSource.Play();


        // =====================================================
        // フェードイン
        // =====================================================

        timer = 0f;


        // 曲本来の音量 × マスターボリューム
        float targetVolume =
            newMusic.volume * masterVolume;


        // fadeInTimeが0以下なら
        // 即座に指定音量にする
        if (fadeInTime <= 0f)
        {
            audioSource.volume = targetVolume;
        }
        else
        {
            // fadeInTime秒かけて音量を上げる
            while (timer < fadeInTime)
            {
                timer += Time.unscaledDeltaTime;

                float rate = timer / fadeInTime;

                audioSource.volume =
                    Mathf.Lerp(0f, targetVolume, rate);

                yield return null;
            }

            // 確実に目的の音量にする
            audioSource.volume = targetVolume;
        }


        // フェード処理終了
        fadeCoroutine = null;
    }


    // =========================================================
    // BGMフェードイン
    // =========================================================

    /// <summary>
    /// BGMが鳴っていない状態から
    /// 新しいBGMをフェードインします。
    /// </summary>
    private IEnumerator FadeInCoroutine(MusicData music)
    {
        // 現在のBGMとして記録
        currentMusic = music;


        // 再生するAudioClipを設定
        audioSource.clip = music.clip;


        // 最初は無音
        audioSource.volume = 0f;


        // 再生開始
        audioSource.Play();


        float timer = 0f;


        // 曲本来の音量 × マスターボリューム
        float targetVolume =
            music.volume * masterVolume;


        // fadeInTimeが0以下なら
        // 即座に指定音量にする
        if (fadeInTime <= 0f)
        {
            audioSource.volume = targetVolume;
        }
        else
        {
            // fadeInTime秒かけて音量を上げる
            while (timer < fadeInTime)
            {
                timer += Time.unscaledDeltaTime;

                float rate = timer / fadeInTime;

                audioSource.volume =
                    Mathf.Lerp(0f, targetVolume, rate);

                yield return null;
            }

            // 確実に目的の音量にする
            audioSource.volume = targetVolume;
        }


        // フェード処理終了
        fadeCoroutine = null;
    }


    // =========================================================
    // マスターボリューム
    // =========================================================

    /// <summary>
    /// BGM全体のマスターボリュームを設定します。
    /// 0～1の値を指定してください。
    ///
    /// 0   = 無音
    /// 0.5 = 半分
    /// 1   = 最大
    ///
    /// 使用例：
    /// MusicPlayer.Instance.SetMasterVolume(0.5f);
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        // 0～1の範囲に収める
        masterVolume = Mathf.Clamp01(volume);


        // 現在BGMが再生されている場合は
        // すぐに音量へ反映する
        if (currentMusic != null)
        {
            audioSource.volume =
                currentMusic.volume * masterVolume;
        }
    }


    /// <summary>
    /// 現在のマスターボリュームを取得します。
    ///
    /// 使用例：
    /// float volume = MusicPlayer.Instance.GetMasterVolume();
    /// </summary>
    public float GetMasterVolume()
    {
        return masterVolume;
    }
}