using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

namespace PLC_AIPanelUI_Handoff_20261127
{

// Videos store colour on the left and an opacity mask on the right.
[RequireComponent(typeof(VideoPlayer), typeof(RawImage))]
public class RandomVideoPlayer : MonoBehaviour
{
    [Serializable]
    private class VideosConfig { public string[] videos; }

    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private string configPath = "PLC_AIPanelUI_Handoff_20261127/Video_aiIp/videosConfig.json";
    [SerializeField] private float prepareTimeout = 15f;

    private sealed class VideoSlot
    {
        public VideoPlayer Player;
        public RenderTexture Texture;
        public int Index = -1;
        public int FirstFrame = -1;
        public bool Ended, Failed, AwaitingFrame, Ready;
    }

    private RawImage display;
    private VideoSlot[] slots;
    private VideoSlot active;
    private GameObject preloadObject;
    private UnityWebRequest configRequest;
    private string[] videos;
    private int lastIndex = -1;
    private readonly List<int> available = new List<int>();
    private readonly HashSet<int> failed = new HashSet<int>();

    private void Awake()
    {
        if (!videoPlayer) videoPlayer = GetComponent<VideoPlayer>();
        display = GetComponent<RawImage>();
        display.enabled = false;
        display.raycastTarget = false;

        preloadObject = new GameObject("AI Video Preload");
        preloadObject.transform.SetParent(transform, false);
        slots = new[] { CreateSlot(videoPlayer), CreateSlot(preloadObject.AddComponent<VideoPlayer>()) };
    }

    private VideoSlot CreateSlot(VideoPlayer player)
    {
        // Never prepare or stop a decoder whose texture is still displayed.
        var texture = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32)
        {
            name = "AI Character Video (Instance)",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        player.targetTexture = texture;
        player.source = VideoSource.Url;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.aspectRatio = VideoAspectRatio.Stretch;
        player.playOnAwake = false;
        player.isLooping = false;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.loopPointReached += OnVideoEnded;
        player.errorReceived += OnVideoError;
        player.frameReady += OnFrameReady;
        return new VideoSlot { Player = player, Texture = texture };
    }

    private void OnEnable()
    {
        failed.Clear();
        StartCoroutine(PlayPlaylist());
    }

    private IEnumerator PlayPlaylist()
    {
        if (videos == null)
        {
            configRequest = UnityWebRequest.Get(StreamingUrl(configPath));
            configRequest.timeout = 15;
            yield return configRequest.SendWebRequest();
            string json = configRequest.result == UnityWebRequest.Result.Success
                ? configRequest.downloadHandler.text : null;
            string error = configRequest.error;
            configRequest.Dispose();
            configRequest = null;
            if (json == null)
            {
                Debug.LogWarning("AI 人物视频列表读取失败：" + error, this);
                yield break;
            }

            try
            {
                json = json.Trim().TrimStart('\uFEFF');
                if (json.StartsWith("[")) json = "{\"videos\":" + json + "}";
                var config = JsonUtility.FromJson<VideosConfig>(json);
                var names = new List<string>();
                if (config != null && config.videos != null)
                    foreach (string value in config.videos)
                        if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value.Trim()))
                            names.Add(value.Trim());
                if (names.Count > 0) videos = names.ToArray();
            }
            catch (Exception e)
            {
                Debug.LogWarning("AI 人物视频列表格式错误：" + e.Message, this);
            }
            if (videos == null)
            {
                Debug.LogWarning("AI 人物视频列表为空，请检查 StreamingAssets/" + configPath, this);
                yield break;
            }
        }

        VideoSlot standby = slots[0];
        while (isActiveAndEnabled)
        {
            int index = PickNext();
            if (index < 0) yield break;
            yield return PrepareSlot(standby, index);
            if (!standby.Ready) continue;

            // The current player keeps its last frame if preparation runs past its end.
            while (active != null && !active.Ended && !active.Failed && !standby.Failed)
                yield return null;
            if (standby.Failed)
            {
                failed.Add(standby.Index);
                continue;
            }

            VideoSlot previous = active;
            active = standby;
            lastIndex = active.Index;
            active.Ended = false;
            display.texture = active.Texture;
            display.enabled = true;
            active.Player.Play();
            standby = previous ?? slots[1];

            // Allow the canvas to bind the new texture before recycling the old decoder.
            yield return null;
        }
    }

    private int PickNext()
    {
        available.Clear();
        for (int i = 0; i < videos.Length; i++)
            if (!failed.Contains(i)) available.Add(i);
        if (available.Count == 0) return -1;
        if (available.Count > 1) available.Remove(lastIndex);
        return available[UnityEngine.Random.Range(0, available.Count)];
    }

    private IEnumerator PrepareSlot(VideoSlot slot, int index)
    {
        slot.AwaitingFrame = slot.Ready = false;
        slot.Player.Stop();
        slot.Index = index;
        slot.FirstFrame = -1;
        slot.Ended = slot.Failed = false;
        string directory = configPath.Substring(0, configPath.LastIndexOf('/') + 1);
        slot.Player.url = StreamingUrl(directory + videos[index], true);
        slot.Player.Prepare();
        float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, prepareTimeout);
        while (!slot.Player.isPrepared && !slot.Failed && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (!slot.Failed && slot.Player.isPrepared)
        {
            int width = Mathf.Max(2, (int)slot.Player.width);
            int height = Mathf.Max(1, (int)slot.Player.height);
            if (slot.Texture.width != width || slot.Texture.height != height)
            {
                slot.Texture.Release();
                slot.Texture.width = width;
                slot.Texture.height = height;
            }
            if (!slot.Texture.IsCreated()) slot.Texture.Create();
            slot.AwaitingFrame = true;
            slot.Player.sendFrameReadyEvents = true;
            slot.Player.Play();

            // frameReady can precede the GPU texture update. Keep the old picture for
            // a full render frame before making this texture visible.
            while ((slot.FirstFrame < 0 || Time.frameCount <= slot.FirstFrame) &&
                !slot.Failed && Time.realtimeSinceStartup < deadline)
                yield return null;
            slot.AwaitingFrame = false;
            if (!slot.Failed && slot.FirstFrame >= 0 && Time.frameCount > slot.FirstFrame)
            {
                slot.Player.Pause();
                slot.Ready = true;
                yield break;
            }
        }

        slot.Player.sendFrameReadyEvents = false;
        slot.Player.Stop();
        failed.Add(index);
        if (!slot.Failed) Debug.LogWarning("AI 人物视频准备超时：" + videos[index], this);
    }

    private static string StreamingUrl(string relativePath, bool forVideo = false)
    {
        string root = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/') + "/";
        if (!root.Contains("://"))
        {
            // Native video decoders expect unescaped local paths, including Chinese names.
            // UnityWebRequest and browser video URLs instead require URL escaping.
            if (forVideo) return root + relativePath.Replace('\\', '/');
            root = new Uri(root).AbsoluteUri;
        }
        string[] segments = relativePath.Replace('\\', '/').Split('/');
        for (int i = 0; i < segments.Length; i++) segments[i] = Uri.EscapeDataString(segments[i]);
        return root + string.Join("/", segments);
    }

    private VideoSlot FindSlot(VideoPlayer source)
    {
        if (slots != null)
            foreach (var slot in slots)
                if (slot.Player == source) return slot;
        return null;
    }

    private void OnVideoEnded(VideoPlayer source)
    {
        var slot = FindSlot(source);
        if (slot != null) slot.Ended = true;
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        var slot = FindSlot(source);
        if (slot == null || !isActiveAndEnabled) return;
        slot.Failed = true;
        failed.Add(slot.Index);
        Debug.LogWarning("AI 人物视频播放失败：" + source.url + "\n" + message, this);
    }

    private void OnFrameReady(VideoPlayer source, long frame)
    {
        var slot = FindSlot(source);
        if (!isActiveAndEnabled || slot == null || slot.Failed || !slot.AwaitingFrame) return;
        slot.FirstFrame = Time.frameCount;
        source.sendFrameReadyEvents = false;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (configRequest != null)
        {
            configRequest.Abort();
            configRequest.Dispose();
            configRequest = null;
        }
        if (display) display.enabled = false;
        active = null;
        if (slots != null)
            foreach (var slot in slots)
            {
                slot.AwaitingFrame = slot.Ready = false;
                if (!slot.Player) continue;
                slot.Player.sendFrameReadyEvents = false;
                slot.Player.Stop();
            }
    }

    private void OnDestroy()
    {
        if (slots != null)
            foreach (var slot in slots)
            {
                if (slot.Player)
                {
                    slot.Player.loopPointReached -= OnVideoEnded;
                    slot.Player.errorReceived -= OnVideoError;
                    slot.Player.frameReady -= OnFrameReady;
                    slot.Player.targetTexture = null;
                }
                if (slot.Texture)
                {
                    slot.Texture.Release();
                    Destroy(slot.Texture);
                }
            }
        if (preloadObject) Destroy(preloadObject);
    }
}

}
