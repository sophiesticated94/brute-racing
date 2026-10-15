using Godot;

namespace Game.Client;

public partial class GameAudio : Node
{
    private readonly Dictionary<string, AudioStreamWav> sounds = [];
    private AudioStreamPlayer? engine;
    private readonly List<AudioStreamPlayer3D> voices = [];
    public bool Muted;
    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless") return;
        sounds["machine_gun"] = Synthesize(.085f, 130, .7f, false);
        sounds["laser"] = Synthesize(.13f, 950, .08f, false);
        sounds["rocket"] = Synthesize(.23f, 95, .6f, false);
        sounds["explosion"] = Synthesize(.6f, 65, .95f, false);
        sounds["checkpoint"] = Synthesize(.15f, 660, 0, false);
        engine = new AudioStreamPlayer { Stream = Synthesize(.5f, 65, .15f, true), VolumeDb = -28 };
        AddChild(engine); engine.Play();
        for (int i = 0; i < 16; i++)
        {
            var voice = new AudioStreamPlayer3D { MaxDistance = 65, UnitSize = 9, VolumeDb = -14 };
            AddChild(voice); voices.Add(voice);
        }
    }
    public void UpdateEngine(float speed, bool racing)
    {
        if (engine is null) return;
        engine.VolumeDb = Muted || !racing ? -80 : -28;
        engine.PitchScale = .8f + Mathf.Abs(speed) / 22;
    }
    public void PlayAt(string sound, Vector3 position)
    {
        if (Muted || !sounds.TryGetValue(sound, out var stream)) return;
        var voice = voices.FirstOrDefault(v => !v.Playing);
        if (voice is null) return;
        voice.GlobalPosition = position; voice.Stream = stream; voice.Play();
    }
    private static AudioStreamWav Synthesize(float duration, float frequency, float noise, bool loop)
    {
        const int rate = 22050; int length = (int)(rate * duration);
        byte[] data = new byte[length * 2]; var random = new Random(42);
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / rate; float envelope = loop ? 1 : Mathf.Pow(1 - (float)i / length, 2);
            double tone = Math.Sin(t * frequency * Math.Tau * (loop ? 1 : 1 - .3f * t / duration));
            short sample = (short)((tone * (1 - noise) + (random.NextDouble() * 2 - 1) * noise) * envelope * 13000);
            data[i * 2] = (byte)sample; data[i * 2 + 1] = (byte)(sample >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate,
            Data = data, LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled, LoopEnd = length };
    }
}
