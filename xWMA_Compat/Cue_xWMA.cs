using System;
using Microsoft.Xna.Framework.Audio;

namespace xWMA_Compat
{
	public sealed class Cue_xWMA:IDisposable
	{
		private readonly Cue cue;

		public event EventHandler<EventArgs> Disposing
		{
			add		=>cue.Disposing+=value;
			remove	=>cue.Disposing-=value;
		}

		public string Name=>cue.Name;

		public bool IsCreated=>cue.IsCreated;

		public bool IsPreparing=>cue.IsPreparing;

		public bool IsPrepared=>cue.IsPrepared;

		public bool IsPlaying=>cue.IsPlaying;

		public bool IsStopping=>cue.IsStopping;

		public bool IsStopped=>cue.IsStopped;

		public bool IsPaused=>cue.IsPaused;

		public bool IsDisposed=>cue.IsDisposed;

		internal Cue_xWMA(Cue cue)=>this.cue=cue;

		public void Play()=>cue.Play();

		public void Pause()=>cue.Pause();

		public void Resume()=>cue.Resume();

		public void Stop(int options)=>cue.Stop((AudioStopOptions)options);

		public float GetVariable(string name)=>cue.GetVariable(name);

		public void SetVariable(string name, float value)=>cue.SetVariable(name,value);

		//public unsafe void Apply3D(AudioListener listener, AudioEmitter emitter){}

		public void Dispose()=>cue.Dispose();

		//protected override void Finalize()
		//{
		//	try
		//	{
		//		lock (AudioEngine.pSyncObject)
		//		{
		//			if (this.cueHandle != 4294967295u && !this.IsDisposed && !this.parent.IsDisposed && this.IsPlaying)
		//			{
		//				GC.ReRegisterForFinalize(this);
		//			}
		//			else
		//			{
		//				this.Dispose(false);
		//			}
		//		}
		//	}
		//	finally
		//	{
		//		base.Finalize();
		//	}
		//}
	}
}
