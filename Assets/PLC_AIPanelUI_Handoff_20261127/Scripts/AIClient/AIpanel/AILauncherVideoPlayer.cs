using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace PLC_AIPanelUI_Handoff_20261127
{

// Both decoders keep independent textures, so switching never clears the visible frame.
[RequireComponent(typeof(Button))]
public sealed class AILauncherVideoPlayer : MonoBehaviour
{
    [SerializeField] private RawImage display;
    [SerializeField] private VideoPlayer smilePlayer, speakingPlayer;
    [SerializeField] private Texture smilePoster;
    [SerializeField] private string smilePath = "PLC_AIPanelUI_Handoff_20261127/Video_aiLauncher/smile.mp4";
    [SerializeField] private string speakingPath = "PLC_AIPanelUI_Handoff_20261127/Video_aiLauncher/speaking.mp4";
    [SerializeField] private float prepareTimeout = 15;

    private sealed class Slot
    {
        public VideoPlayer Player;
        public RenderTexture Texture;
        public string Path;
        public bool Ready, Failed;
        public int FirstFrame = -1;
    }

    private Slot[] slots;
    private Slot active;
    private bool speaking;

    private void Awake()
    {
        var hitArea = GetComponent<Image>();
        if (hitArea != null)
        {
            var colour = hitArea.color; colour.a = 0; hitArea.color = colour;
            hitArea.canvasRenderer.cullTransparentMesh = false;
        }
        display.raycastTarget = false;
        display.texture = smilePoster;
        FitDisplay();
        slots = new[] { CreateSlot(smilePlayer, smilePath), CreateSlot(speakingPlayer, speakingPath) };
    }

    private Slot CreateSlot(VideoPlayer player, string path)
    {
        var texture = new RenderTexture(1408, 704, 0, RenderTextureFormat.ARGB32)
        {
            name = "AI Launcher " + path,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        player.source = VideoSource.Url;
        player.url = StreamingUrl(path);
        player.playOnAwake = false;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = texture;
        player.aspectRatio = VideoAspectRatio.Stretch;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.isLooping = true;
        player.errorReceived += OnVideoError;
        player.frameReady += OnFrameReady;
        return new Slot { Player = player, Texture = texture, Path = path };
    }

    private void OnEnable()
    {
        speaking = false;
        if (slots == null) return;
        FitDisplay();
        foreach (var slot in slots)
        {
            // Disabling the button can release native decoder state even when the
            // last rendered texture survives. Re-prepare before displaying it again.
            if (!slot.Player.isPrepared || slot.Player.frame < 0 || !slot.Texture.IsCreated()) slot.Ready = false;
            if (!slot.Ready)
            {
                slot.Failed = false;
                if (Application.isPlaying) StartCoroutine(Prepare(slot));
            }
        }
        if (!slots[0].Ready) { active = null; display.texture = smilePoster; }
        ApplyState();
    }

    private void OnRectTransformDimensionsChange() { FitDisplay(); }

    private void FitDisplay()
    {
        if (display == null) return;
        var parent = ((RectTransform)transform).rect;
        float size = Mathf.Max(0, Mathf.Min(parent.width, parent.height));
        var rect = display.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);
    }

    public void SetSpeaking(bool value)
    {
        speaking = value;
        if (isActiveAndEnabled && slots != null) ApplyState();
    }

    private void ApplyState()
    {
        var requested = slots[speaking ? 1 : 0];
        if (!requested.Ready || requested.Failed) return;
        if (active != requested)
        {
            if (active != null) active.Player.Pause();
            active = requested;
            display.texture = active.Texture;
        }
        active.Player.Play();
    }

    private IEnumerator Prepare(Slot slot)
    {
        slot.FirstFrame = -1;
        slot.Player.sendFrameReadyEvents = false;
        slot.Player.Prepare();
        float deadline = Time.realtimeSinceStartup + Mathf.Max(1, prepareTimeout);
        while (!slot.Player.isPrepared && !slot.Failed && Time.realtimeSinceStartup < deadline) yield return null;
        if (!slot.Failed && slot.Player.isPrepared)
        {
            int width = Mathf.Max(2, (int)slot.Player.width), height = Mathf.Max(1, (int)slot.Player.height);
            if (slot.Texture.width != width || slot.Texture.height != height)
            { slot.Texture.Release(); slot.Texture.width = width; slot.Texture.height = height; }
            if (!slot.Texture.IsCreated()) slot.Texture.Create();
            slot.Player.sendFrameReadyEvents = true;
            slot.Player.Play();
            // frameReady can precede the GPU upload; wait for a complete render frame.
            while (slot.FirstFrame < 0 && !slot.Failed && Time.realtimeSinceStartup < deadline) yield return null;
            if (!slot.Failed && slot.FirstFrame >= 0) yield return null;
            slot.Player.Pause();
            slot.Player.sendFrameReadyEvents = false;
            if (!slot.Failed && slot.FirstFrame >= 0)
            { slot.Ready = true; ApplyState(); yield break; }
        }
        slot.Failed = true;
        slot.Player.sendFrameReadyEvents = false;
        slot.Player.Stop();
        Debug.LogWarning("小萌按钮视频准备失败：" + slot.Path, this);
    }

    private void OnFrameReady(VideoPlayer player, long frame)
    {
        foreach (var slot in slots)
            if (slot.Player == player && !slot.Ready && !slot.Failed)
            { slot.FirstFrame = Time.frameCount; player.sendFrameReadyEvents = false; return; }
    }

    private void OnVideoError(VideoPlayer player, string message)
    {
        if (slots != null)
            foreach (var slot in slots)
                if (slot.Player == player) slot.Failed = true;
        Debug.LogWarning("小萌按钮视频播放失败：" + message, this);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        speaking = false;
        if (slots == null) return;
        foreach (var slot in slots)
        {
            slot.Player.sendFrameReadyEvents = false;
            if (slot.Ready) slot.Player.Pause();
            else slot.Player.Stop();
        }
    }

    private void OnDestroy()
    {
        if (slots == null) return;
        foreach (var slot in slots)
        {
            if (slot.Player != null)
            {
                slot.Player.errorReceived -= OnVideoError;
                slot.Player.frameReady -= OnFrameReady;
                slot.Player.targetTexture = null;
            }
            slot.Texture.Release();
            if (Application.isPlaying) Destroy(slot.Texture);
            else DestroyImmediate(slot.Texture);
        }
        slots = null;
    }

    private static string StreamingUrl(string path)
    {
        string root = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/') + "/";
        if (!root.Contains("://")) return root + path;
        string[] segments = path.Split('/');
        for (int i = 0; i < segments.Length; i++) segments[i] = Uri.EscapeDataString(segments[i]);
        return root + string.Join("/", segments);
    }
}

}
