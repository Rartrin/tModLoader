using Microsoft.Xna.Framework.Audio;

namespace xWMA_Compat
{
	public class Audio_xWMA
	{
		private readonly AudioEngine engine;
		private readonly SoundBank sound;
		private readonly WaveBank wave;

		internal Audio_xWMA(string enginePath,string soundPath,string wavePath)
		{
			engine = new AudioEngine(enginePath);
			sound = new SoundBank(engine,soundPath);
			wave = new WaveBank(engine,wavePath);
		}

		public static Audio_xWMA LoadXWB(string enginePath,string soundPath,string wavePath)=>new Audio_xWMA(enginePath,soundPath,wavePath);

		public Cue_xWMA GetCue(string name)=>new Cue_xWMA(sound.GetCue(name));
	}
}
