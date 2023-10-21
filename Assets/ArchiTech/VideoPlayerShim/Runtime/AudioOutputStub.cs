
#if AVPRO_IMPORTED
using RenderHeads.Media.AVProVideo;
#endif
using UnityEngine;

//-----------------------------------------------------------------------------
// Copyright 2015-2022 RenderHeads Ltd.  All rights reserved.
//-----------------------------------------------------------------------------
// Modified for use alongside VRCSDK and ClientSim

namespace VRCVideoPlayerShim
{
    /// <summary>
    /// Audio is grabbed from the MediaPlayer and rendered via Unity AudioSource
    /// This allows audio to have 3D spatial control, effects applied and to be spatialised for VR
    /// Currently supported on Windows and UWP (Media Foundation API only), macOS, iOS, tvOS and Android (ExoPlayer API only)
    /// </summary>
    public class AudioOutputStub : MonoBehaviour
    {
#if AVPRO_IMPORTED
        public enum AudioOutputMode
        {
            OneToAllChannels,
            MultipleChannels
        }

        [SerializeField] MediaPlayer _mediaPlayer = null;
        [SerializeField] AudioOutputMode _audioOutputMode = AudioOutputMode.MultipleChannels;
        [HideInInspector, SerializeField] int _channelMask = 0xffff;
        [SerializeField] bool _supportPositionalAudio = false;

        public MediaPlayer Player
        {
            get { return _mediaPlayer; }
            set { _mediaPlayer = value; }
        }

        public AudioOutputMode OutputMode
        {
            get { return _audioOutputMode; }
            set { _audioOutputMode = value; }
        }

        public int ChannelMask
        {
            get { return _channelMask; }
            set { _channelMask = value; }
        }

        private AudioSource _audioSource;
        private float _volume;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            Debug.Assert(_audioSource != null);
        }

        private void Update()
        {
            _volume = _audioSource.volume;
        }

        public void ChangeMediaPlayer(MediaPlayer newPlayer)
        {
            // When changing the media player, handle event subscriptions
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Events.RemoveListener(OnMediaPlayerEvent);
                _mediaPlayer = null;
            }

            _mediaPlayer = newPlayer;
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Events.AddListener(OnMediaPlayerEvent);
            }

            if (_supportPositionalAudio)
            {
                if (_audioSource.clip == null)
                {
                    // Position audio is implemented from hints found on this thread:
                    // https://forum.unity.com/threads/onaudiofilterread-sound-spatialisation.362782/
                    int frameCount = 2048 * 10;
                    int sampleCount = frameCount * Helper.GetUnityAudioSpeakerCount();
                    AudioClip clip = AudioClip.Create("dummy", frameCount, Helper.GetUnityAudioSpeakerCount(), Helper.GetUnityAudioSampleRate(), false);
                    float[] samples = new float[sampleCount];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        samples[i] = 1f;
                    }

                    clip.SetData(samples, 0);
                    _audioSource.clip = clip;
                    _audioSource.loop = true;
                }
            }
            else if (_audioSource.clip != null)
            {
                _audioSource.clip = null;
            }
        }


        // Callback function to handle events
        private void OnMediaPlayerEvent(MediaPlayer mp, MediaPlayerEvent.EventType et, ErrorCode errorCode)
        {
            switch (et)
            {
                case MediaPlayerEvent.EventType.Closing:
                    _audioSource.Stop();
                    break;
                case MediaPlayerEvent.EventType.Started:
                    _audioSource.Play();
                    break;
            }
        }

#if (UNITY_EDITOR_WIN || UNITY_EDITOR_OSX) || (!UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_WSA_10_0 || UNITY_STANDALONE_OSX || UNITY_IOS || UNITY_TVOS || UNITY_ANDROID))
        private void OnAudioFilterRead(float[] audioData, int channelCount)
        {
            if (_mediaPlayer == null || _mediaPlayer.Control == null || _audioSource == null) return;
            _mediaPlayer.AudioVolume = _volume;
            AudioOutputManagerStub.Instance.RequestAudio(this, _mediaPlayer, audioData, channelCount, _channelMask, _audioOutputMode, _supportPositionalAudio);
        }
#endif
#endif
    }
}