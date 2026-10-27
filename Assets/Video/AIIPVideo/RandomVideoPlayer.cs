using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

[System.Serializable]
public class VideosConfig { public string[] videos; }

public class RandomVideoPlayer : MonoBehaviour
{
    [SerializeField] private VideoPlayer videoPlayer;

    private string[] videos;
    private int lastIndex = -1;
    private bool configLoaded;

    private void Awake()
    {
        if (!videoPlayer) videoPlayer = GetComponent<VideoPlayer>();

        videoPlayer.source = VideoSource.Url;
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = false;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;

        videoPlayer.loopPointReached += _ => PlayRandom();
        videoPlayer.errorReceived += (vp, msg) => Debug.LogError($" ”∆µ ß∞‹£∫{vp.url}\n{msg}");
        videoPlayer.prepareCompleted += vp => { if (enabled) vp.Play(); };
    }

    private void OnEnable()
    {
        if (!configLoaded) StartCoroutine(LoadConfig());
        else PlayRandom();
    }

    private void OnDisable()
    {
        if (videoPlayer) videoPlayer.Stop();
    }

    private IEnumerator LoadConfig()
    {
        string url = $"{Application.streamingAssetsPath}/Video_aiIp/videosConfig.json";
        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"≈‰÷√∂¡»° ß∞‹£∫{req.error}");
            yield break;
        }

        string json = req.downloadHandler.text.Trim();
        if (json.StartsWith("[")) json = "{\"videos\":" + json + "}";

        videos = JsonUtility.FromJson<VideosConfig>(json)?.videos;
        if (videos == null || videos.Length == 0)
        {
            Debug.LogError(" ”∆µ¡–±ÌŒ™ø’");
            yield break;
        }

        configLoaded = true;
        PlayRandom();
    }

    private void PlayRandom()
    {
        if (videos == null || videos.Length == 0) return;

        int index;
        do { index = Random.Range(0, videos.Length); }
        while (videos.Length > 1 && index == lastIndex);
        lastIndex = index;

        string fileName = videos[index];
        string videoUrl = $"{Application.streamingAssetsPath}/Video_aiIp/{fileName}";

#if UNITY_WEBGL && !UNITY_EDITOR
        videoUrl = videoUrl.Substring(0, videoUrl.LastIndexOf('/') + 1) + UnityWebRequest.EscapeURL(fileName);
#endif

        videoPlayer.Stop();
        videoPlayer.url = videoUrl;
        videoPlayer.Prepare();
    }

    private void OnDestroy()
    {
        if (!videoPlayer) return;
        videoPlayer.loopPointReached -= _ => PlayRandom();
    }
}