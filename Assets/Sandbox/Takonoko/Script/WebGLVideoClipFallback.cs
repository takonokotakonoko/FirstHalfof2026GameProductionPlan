using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// WebGL（unityroomなど）で、VideoClipを設定したVideoPlayerを再生できるようにするスクリプト。
///
/// WebGLではVideoClipを再生できず、URLで指定する必要があります。
/// また、unityroomにはStreamingAssetsをアップロードできません。
///
/// そこで、動画ファイルを「Resources/WebGLVideos/（VideoClipと同じ名前）.bytes」として
/// ビルドに含めておき、WebGLで実行したときだけ、ブラウザ上でその動画のURLを作って
/// VideoPlayerの再生元をURLに切り替えます。
///
/// シーンへの取り付けは不要です。シーンを読み込むたびに自動で処理します。
/// エディタやWebGL以外では何もしません。
///
/// 【動画を追加するとき】
/// 動画ファイル（.mp4）をコピーして拡張子を .bytes に変え、
/// Assets/Sandbox/Takonoko/Resources/WebGLVideos/ に
/// VideoClipと同じ名前で置いてください。
/// </summary>
public static class WebGLVideoClipFallback
{
    // Resources内で動画のバイト列を置くフォルダ
    private const string ResourcesFolder = "WebGLVideos/";

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern string CreateVideoBlobUrl(byte[] data, int length, string mimeType);

    // 一度作ったURLを動画名ごとに保存し、シーンを読み直しても作り直さない
    private static readonly Dictionary<string, string> blobUrls = new Dictionary<string, string>();


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        // 最初のシーンは読み込み済みなので、ここで一度処理する
        ApplyToAllPlayers();

        // 以降はシーンを読み込むたびに処理する
        SceneManager.sceneLoaded += (scene, mode) => ApplyToAllPlayers();
    }


    private static void ApplyToAllPlayers()
    {
        // 非アクティブなオブジェクトのVideoPlayerも対象にする
        //
        // DontDestroyOnLoadで別シーンに移されたVideoPlayerも対象にするため、
        // シーンでは絞り込まない（切り替え済みのものはsourceがURLなので飛ばされる）
        VideoPlayer[] players = Object.FindObjectsByType<VideoPlayer>(
            FindObjectsInactive.Include
        );

        foreach (VideoPlayer player in players)
        {
            if (player.source != VideoSource.VideoClip || player.clip == null)
                continue;

            string url = GetBlobUrl(player.clip.name);
            if (url == null)
                continue;

            SwitchToUrl(player, url);
        }
    }


    private static string GetBlobUrl(string clipName)
    {
        if (blobUrls.TryGetValue(clipName, out string cachedUrl))
            return cachedUrl;

        TextAsset videoBytes = Resources.Load<TextAsset>(ResourcesFolder + clipName);
        if (videoBytes == null)
        {
            Debug.LogWarning(
                "WebGLVideoClipFallback：「" + ResourcesFolder + clipName
                + ".bytes」がResourcesにないため、この動画はWebGLで再生できません。"
            );

            return null;
        }

        byte[] data = videoBytes.bytes;
        string url = CreateVideoBlobUrl(data, data.Length, "video/mp4");

        // バイト列はブラウザ側にコピー済みなので、メモリから解放する
        Resources.UnloadAsset(videoBytes);

        blobUrls[clipName] = url;
        return url;
    }


    private static void SwitchToUrl(VideoPlayer player, string url)
    {
        bool wasPlaying = player.isPlaying
            || (player.playOnAwake && player.isActiveAndEnabled);

        player.Stop();
        player.source = VideoSource.Url;
        player.url = url;

        // URL再生では音声トラックを自分で有効にする必要がある
        if (player.audioOutputMode != VideoAudioOutputMode.None)
        {
            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, true);
        }

        // すでに再生が始まっていた場合は、URLで再生し直す
        // （非アクティブなものは、アクティブになったときにPlay On Awakeで再生される）
        if (wasPlaying)
            player.Play();

        Debug.Log("WebGLVideoClipFallback：「" + player.name + "」の動画をURL再生に切り替えました。");
    }
#endif
}
