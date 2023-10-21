using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components;
using VRC.SDKBase;

namespace VRCVideoPlayerShim
{
    /// <summary>
    /// Code originally by Merlin via USharpVideo asset, modified for various improvements and stability.
    /// Allows people to put in links to YouTube videos and other supported video services and have links just work
    /// Hooks into VRC's video player URL resolve callback and uses the VRC installation of YouTubeDL to resolve URLs in the editor.
    /// </summary>
    internal static class PlayModeUrlResolverShim
    {
        private static string youtubeDLPath = "";
        private static readonly HashSet<System.Diagnostics.Process> runningYTDLProcesses = new HashSet<System.Diagnostics.Process>();
        private static readonly HashSet<MonoBehaviour> registeredBehaviours = new HashSet<MonoBehaviour>();
        private static readonly Regex pattern = new Regex(".*(?:youtube|yt)-dl.*\\.exe");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void SetupURLResolveCallback()
        {
            string[] splitPath = Application.persistentDataPath.Split('/', '\\');

            string[] files = Directory.GetFiles(string.Join("\\", splitPath.Take(splitPath.Length - 2)) + "\\VRChat\\VRChat\\Tools");
            foreach (string file in files)
            {
                if (pattern.IsMatch(file))
                {
                    youtubeDLPath = file;
                    VRCUnityVideoPlayer.StartResolveURLCoroutine = ResolveURLCallback;
#if AVPRO_IMPORTED
                    AVProMediaPlayerShim.StartResolveURLCoroutine = ResolveURLCallback;
#endif
                    EditorApplication.playModeStateChanged += PlayModeChanged;
                    return;
                }
            }

            Debug.LogWarning("[YTDL] Unable to find VRC YouTube-dl installation, URLs will not be resolved.");
        }

        /// <summary>
        /// Cleans up any remaining YTDL processes from this play.
        /// In some cases VRC's YTDL has hung indefinitely eating CPU so this is a precaution against that potentially happening.
        /// </summary>
        /// <param name="change"></param>
        private static void PlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                foreach (var process in runningYTDLProcesses.Where(process => !process.HasExited))
                {
                    process.Close();
                }

                runningYTDLProcesses.Clear();

                // Apparently the URLResolveCoroutine will run after this method in some cases magically. So don't because the process will throw an exception.
                foreach (MonoBehaviour behaviour in registeredBehaviours)
                    behaviour.StopAllCoroutines();

                registeredBehaviours.Clear();
            }
        }

        static void ResolveURLCallback(VRCUrl url, int resolution, UnityEngine.Object videoPlayer, Action<string> urlResolvedCallback, Action<VideoError> errorCallback)
        {
            System.Diagnostics.Process ytdlProcess = new System.Diagnostics.Process();
            ytdlProcess.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            ytdlProcess.StartInfo.CreateNoWindow = true;
            ytdlProcess.StartInfo.UseShellExecute = false;
            ytdlProcess.StartInfo.RedirectStandardOutput = true;
            ytdlProcess.StartInfo.FileName = youtubeDLPath;
            ytdlProcess.StartInfo.Arguments = $"--no-check-certificate --no-cache-dir --rm-cache-dir -f \"mp4[height<=?{resolution}]/best[height<=?{resolution}]\" --get-url \"{url}\"";

            Debug.Log($"[<color=#9C6994>Video Player</color>] Attempting to resolve URL '{url}'");

            ytdlProcess.Start();
            runningYTDLProcesses.Add(ytdlProcess);

            ((MonoBehaviour)videoPlayer).StartCoroutine(URLResolveCoroutine(url.ToString(), ytdlProcess, videoPlayer, urlResolvedCallback, errorCallback));

            registeredBehaviours.Add((MonoBehaviour)videoPlayer);
        }

        static IEnumerator URLResolveCoroutine(string originalUrl, System.Diagnostics.Process ytdlProcess, UnityEngine.Object videoPlayer, Action<string> urlResolvedCallback, Action<VideoError> errorCallback)
        {
            while (!ytdlProcess.HasExited)
                yield return new WaitForSeconds(0.1f);

            runningYTDLProcesses.Remove(ytdlProcess);

            var stdout = ytdlProcess.StandardOutput;
            string resolvedURL = "";
            bool stillNeedsUrl = true;
            while (stillNeedsUrl)
            {
                resolvedURL = stdout.ReadLine();
                // if url is null, end of stream reached
                stillNeedsUrl = resolvedURL != null && !resolvedURL.Contains("://");
            }

            // Valid URL was found prior to the end of the output stream
            if (resolvedURL != null)
            {
                Debug.Log($"[<color=#9C6994>Video Player</color>] Successfully resolved URL '{originalUrl}' to '{resolvedURL}'");
                urlResolvedCallback(resolvedURL);
            }
            // If a URL fails to resolve, YTDL will send error to stderror and nothing will be output to stdout
            else
            {
                Debug.Log($"[<color=#9C6994>Video Player</color>] Failed to resolved URL '{originalUrl}'.");
                errorCallback(VideoError.InvalidURL);
            }
        }
    }
}