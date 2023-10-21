using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components.AVPro;
using VRC.SDK3.Video.Interfaces.AVPro;
using VRC.SDKBase;

#if AVPRO_IMPORTED
using RenderHeads.Media.AVProVideo;
#endif

namespace VRCVideoPlayerShim
{
    #region Scripting Define Control

    [InitializeOnLoad]
    internal class AVProScriptingDefineHandler
    {
        private const string filePath = "Assets/AVProVideo/Runtime/Scripts/Components/MediaPlayer.cs";
        private const string scriptingDefineSymbol = "AVPRO_IMPORTED";
        private static bool _hasCheckedDefines = false;

        static AVProScriptingDefineHandler()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        public static void OnEditorUpdate()
        {
            if (_hasCheckedDefines || EditorApplication.isUpdating || EditorApplication.isCompiling)
                return;

            if (File.Exists(filePath)) addScriptingDefine(scriptingDefineSymbol);
            else removeScriptingDefine(scriptingDefineSymbol);
            _hasCheckedDefines = true;
        }

        private static bool hasScriptingDefine(string name)
        {
            string[] defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone).Split(';');
            return defines.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        private static void addScriptingDefine(string name)
        {
            if (!hasScriptingDefine(name))
            {
                BuildTargetGroup buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);

                // update PC
                string[] defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup).Split(';');
                defines = defines.Append(name).ToArray();
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, string.Join(";", defines));
            }
        }

        private static void removeScriptingDefine(string name)
        {
            if (hasScriptingDefine(name))
            {
                BuildTargetGroup buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
                // update PC
                string[] defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup).Split(';');
                defines = defines.Where(s => s != name).ToArray();
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, string.Join(";", defines));
            }
        }
    }

    #endregion

    #region AVPro Injection

#if AVPRO_IMPORTED

    public static class PlayModeAVProShim
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void SceneInit()
        {
            VRCAVProVideoPlayer.Initialize = player => new AVProMediaPlayerShim(player);
            VRCAVProVideoSpeaker.Initialize = AVProSpeakerBuilder;
            VRCAVProVideoScreen.Initialize = AVProScreenBuilder;
        }

        internal static void AVProSpeakerBuilder(VRCAVProVideoSpeaker settings)
        {
            if (!settings.TryGetComponent(out AudioOutputStub component))
                component = settings.gameObject.AddComponent<AudioOutputStub>();

            // channel 1 & 2 mix is left/right stereo
            if (settings.Mode == VRCAVProVideoSpeaker.ChannelMode.StereoMix)
                component.ChannelMask = 1 << 1 | 1 << 2;
            // grabs the enum channel and exclude the stereomix option
            else component.ChannelMask = 1 << (((int)settings.Mode) - 1);
            component.OutputMode = AudioOutputStub.AudioOutputMode.OneToAllChannels;


            // preemtively add the MediaPlayer component if it doesn't exist yet
            // the initialize of the videoplayer will handle the rest
            if (!settings.VideoPlayer.TryGetComponent(out MediaPlayer mediaPlayer))
                mediaPlayer = settings.VideoPlayer.gameObject.AddComponent<MediaPlayer>();
            component.ChangeMediaPlayer(mediaPlayer);
        }

        internal static void AVProScreenBuilder(VRCAVProVideoScreen settings)
        {
            if (!settings.TryGetComponent(out ApplyToMaterial component))
                component = settings.gameObject.AddComponent<ApplyToMaterial>();

            var renderer = settings.GetComponent<MeshRenderer>();
            var mats = settings.UseSharedMaterial ? renderer.sharedMaterials : renderer.materials;
            component.Material = mats[settings.MaterialIndex];
            component.TexturePropertyName = settings.TextureProperty;
            if (component.Material.GetTexture(settings.TextureProperty) is Texture2D tex)
                component.DefaultTexture = tex;

            // preemtively add the MediaPlayer component if it doesn't exist yet
            // the initialize of the videoplayer will handle the rest
            if (!settings.VideoPlayer.TryGetComponent(out MediaPlayer mediaPlayer))
                mediaPlayer = settings.VideoPlayer.gameObject.AddComponent<MediaPlayer>();
            component.Player = mediaPlayer;
        }

        internal static MediaPlayer AVProPlayerBuilder(VRCAVProVideoPlayer settings)
        {
            if (!settings.TryGetComponent(out MediaPlayer component))
                component = settings.gameObject.AddComponent<MediaPlayer>();


            if (settings.VideoURL.Get() != string.Empty)
            {
                component.AutoOpen = true;
                component.AutoStart = settings.AutoPlay;
                setProperty(component, nameof(MediaPlayer.MediaSource), MediaSource.Path);
                setProperty(component, nameof(MediaPlayer.MediaPath), new MediaPath(settings.VideoURL.Get(), MediaPathType.AbsolutePathOrURL));
            }
            else
            {
                component.AutoOpen = false;
                component.AutoStart = false;
            }

            component.Loop = settings.Loop;

            // PC settings
            component.PlatformOptionsWindows.useLowLatency = settings.UseLowLatency;
            component.PlatformOptionsWindows.videoApi = Windows.VideoApi.MediaFoundation;
            component.PlatformOptionsWindows.audioOutput = Windows.AudioOutput.Unity;
            // Quest settings
            component.PlatformOptionsAndroid.videoApi = Android.VideoApi.ExoPlayer;
            component.PlatformOptionsAndroid.audioOutput = Android.AudioOutput.Unity;


            return component;
        }

        private static void setProperty(object obj, string name, object value)
        {
            if (obj == null) return;
            PropertyInfo propInfo = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (propInfo == null || !propInfo.CanWrite) return;
            propInfo.SetValue(obj, value);
        }
    }

    public class AVProMediaPlayerShim : IAVProVideoPlayerInternal
    {
        public static Action<VRCUrl, int, UnityEngine.Object, Action<string>, Action<VideoError>> StartResolveURLCoroutine { get; set; }

        private readonly VRCAVProVideoPlayer playerProxy;
        private readonly MediaPlayer backingPlayer;
        private readonly int maximumResolution;
        private string sourceUrl;
        private bool autoplayAfterResolve;

        public AVProMediaPlayerShim(VRCAVProVideoPlayer proxy)
        {
            playerProxy = proxy;
            backingPlayer = PlayModeAVProShim.AVProPlayerBuilder(proxy);
            maximumResolution = proxy.MaximumResolution;
            // Capture events: ReadyToPlay, Started, FinishedPlaying, Closing, Error
            backingPlayer.EventMask = 1 << 1 | 1 << 2 | 1 << 4 | 1 << 5 | 1 << 6;
            backingPlayer.Events.AddListener(OnEventReceived);
        }

        private bool hasEnded = false;

        public void OnEventReceived(MediaPlayer mediaPlayer, MediaPlayerEvent.EventType eventType, ErrorCode error)
        {
            UnityEngine.Debug.Log($"Media Event Received: {eventType} - error? {error}");
            switch (eventType)
            {
                case MediaPlayerEvent.EventType.Error:
                    playerProxy.OnVideoError(VideoError.PlayerError);
                    break;
                case MediaPlayerEvent.EventType.ReadyToPlay:
                    playerProxy.OnVideoReady();
                    break;
                case MediaPlayerEvent.EventType.Started:
                    playerProxy.OnVideoStart();
                    break;
                case MediaPlayerEvent.EventType.FinishedPlaying:
                    if (playerProxy.Loop) playerProxy.OnVideoLoop();
                    else
                    {
                        hasEnded = true;
                        playerProxy.OnVideoEnd();
                    }
                    break;
                case MediaPlayerEvent.EventType.Closing:
                    if (!hasEnded) playerProxy.OnVideoEnd();
                    break;
            }
        }

        public void UrlResolved(string url) => backingPlayer.OpenMedia(MediaPathType.AbsolutePathOrURL, url, autoplayAfterResolve);

        public void UrlFailed(VideoError videoError) => UrlResolved(sourceUrl);

        public void LoadURL(VRCUrl url)
        {
            sourceUrl = url.Get();
            autoplayAfterResolve = false;
            StartResolveURLCoroutine(url, maximumResolution, backingPlayer, UrlResolved, UrlFailed);
        }

        public void PlayURL(VRCUrl url)
        {
            sourceUrl = url.Get();
            autoplayAfterResolve = true;
            StartResolveURLCoroutine(url, maximumResolution, backingPlayer, UrlResolved, UrlFailed);
        }

        public void Play() => backingPlayer.Play();
        public void Pause() => backingPlayer.Pause();
        public void Stop() => backingPlayer.Stop();

        public void SetTime(float value) => backingPlayer.Control?.Seek(value);
        public float GetTime() => (float)(backingPlayer.Control?.GetCurrentTime() ?? 0f);
        public float GetDuration() => (float)(backingPlayer.Info?.GetDuration() ?? 0f);

        public bool Loop
        {
            get => backingPlayer.Control?.IsLooping() ?? false;
            set => backingPlayer.Control?.SetLooping(value);
        }

        public bool IsPlaying => backingPlayer.Control?.IsPlaying() ?? false;

        public bool IsReady => backingPlayer.MediaOpened;

        public bool UseLowLatency => backingPlayer.PlatformOptionsWindows.useLowLatency;
    }

#endif

    #endregion
}