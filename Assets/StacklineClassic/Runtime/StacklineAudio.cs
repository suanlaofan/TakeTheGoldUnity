using UnityEngine;

namespace Wukong.StacklineClassic
{
    /// <summary>Small procedural audio palette so the mobile build has responsive feedback without streamed assets.</summary>
    public sealed class StacklineAudio : MonoBehaviour
    {
        private AudioSource source;
        private AudioClip place, perfect, cut, fail, rescue;

        public void Configure(bool spatial)
        {
            source = gameObject.GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatial ? 0.65f : 0f;
            source.volume = 0.42f;
            place = Tone("Place", 440f, 0.10f, 0.34f, 0.02f);
            perfect = Chime("Perfect", 660f, 880f, 0.22f);
            cut = Tone("Cut", 235f, 0.12f, 0.24f, 0.01f);
            fail = Tone("Fail", 155f, 0.28f, 0.30f, 0.01f);
            rescue = Chime("Rescue", 523f, 784f, 0.34f);
        }

        public void SetMuted(bool muted)
        {
            if (source == null) return;
            source.mute = muted;
            if (muted) source.Stop();
        }

        private void OnDestroy()
        {
            if (source != null) source.Stop();
            Destroy(place);
            Destroy(perfect);
            Destroy(cut);
            Destroy(fail);
            Destroy(rescue);
        }

        public void PlayPlace() { Play(place, 0.75f); }
        public void PlayPerfect() { Play(perfect, 1f); }
        public void PlayCut() { Play(cut, 0.8f); }
        public void PlayFail() { Play(fail, 0.9f); }
        public void PlayRescue() { Play(rescue, 0.9f); }

        private void Play(AudioClip clip, float volume)
        {
            if (source != null && !source.mute && clip != null) source.PlayOneShot(clip, volume);
        }

        private static AudioClip Tone(string name, float frequency, float seconds, float amplitude, float attack)
        {
            int rate = 22050, count = Mathf.Max(64, Mathf.RoundToInt(seconds * rate));
            var clip = AudioClip.Create(name, count, 1, rate, false);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Min(1f, t / Mathf.Max(0.001f, attack)) * Mathf.Clamp01((seconds - t) * 12f);
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * amplitude * envelope;
            }
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip Chime(string name, float first, float second, float seconds)
        {
            int rate = 22050, count = Mathf.Max(64, Mathf.RoundToInt(seconds * rate));
            var clip = AudioClip.Create(name, count, 1, rate, false);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Clamp01((seconds - t) * 8f);
                float f = t < seconds * 0.52f ? first : second;
                samples[i] = (Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 2f * t)) * 0.24f * envelope;
            }
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
