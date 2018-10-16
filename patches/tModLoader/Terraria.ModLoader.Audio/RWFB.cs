using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terraria.ModLoader.Audio.XWB;

namespace Terraria.ModLoader.Audio
{
	//Reduced Wave File Bank
	public class RWFB
	{
		private static readonly byte[] RWFB_FORMAT=Encoding.UTF8.GetBytes("RWFB");

		private const byte CURRENT_RWFB_VERSION=0;

		internal class TrackData
		{
			private readonly int channels;
			private readonly int sampleRate;
			private readonly byte[] data;

			private TrackData(int channels,int sampleRate,byte[] data)
			{
				this.channels=channels;
				this.sampleRate=sampleRate;
				this.data=data;
			}

			public MusicRWF ToMusic(){return new MusicRWF(channels,sampleRate,data);}

			public static TrackData FromRFW(BinaryReader reader_RWF)
			{
				int channels	=reader_RWF.ReadByte();
				int sampleRate	=reader_RWF.ReadInt32();
				int dataLength	=reader_RWF.ReadInt32();
				byte[] data		=reader_RWF.ReadBytes(dataLength);
				return new TrackData(channels,sampleRate,data);
			}

			public static TrackData FromPCM(byte[] buffer_PCM)
			{
				if(buffer_PCM==null){return null;}
				int		compression;
				short	channels=0;
				int		sampleRate=0;
				int		avgBPS;
				short	blockAlign;
				short	bitDepth;
				byte[] data=null;

				MemoryStream stream_PCM=new MemoryStream(buffer_PCM);
				BinaryReader reader		= new BinaryReader(stream_PCM);
				string headerChunkID	= Encoding.UTF8.GetString(reader.ReadBytes(4));//"RIFF"
				int fileSize			= reader.ReadInt32();//fileSize-8 for the first two fields
				string riffType			= Encoding.UTF8.GetString(reader.ReadBytes(4));//"WAVE"

				while(reader.PeekChar()!=-1)//While reader has data in it
				{
					string chunkID=Encoding.UTF8.GetString(reader.ReadBytes(4));
					int chunkSize=reader.ReadInt32();

					switch(chunkID)
					{
						case "fmt ":
						{
							compression	= reader.ReadInt16();//1 means no compression, anything else means it is compressed
							channels	= reader.ReadInt16();
							sampleRate	= reader.ReadInt32();
							avgBPS		= reader.ReadInt32();
							blockAlign	= reader.ReadInt16();
							bitDepth	= reader.ReadInt16();
							if (chunkSize == 18)
							{
								// Read any extra values
								int fmtExtraSize = reader.ReadInt16();
								reader.ReadBytes(fmtExtraSize);
							}
							break;
						}
						case "data":
						{
							data=reader.ReadBytes(chunkSize);
							break;
						}
						default:
						{
							#if DEBUG
							Console.WriteLine("Unused Chunk: {0} - {1}",chunkID,chunkSize);
							#endif
							reader.ReadBytes(chunkSize);
							break;
						}
					}
				}
				return new TrackData(channels,sampleRate,data);
			}

			public void ToRFW(BinaryWriter writer_RWF)
			{
				writer_RWF.Write((byte)channels);
				writer_RWF.Write((int)sampleRate);
				writer_RWF.Write((int)data.Length);
				writer_RWF.Write(data);
			}
		}

		private IList<TrackData> tracks;

		private RWFB()
		{
			tracks=new List<TrackData>();
			tracks.Add(null);
		}

		internal static RWFB LoadBank(string path,int expectedTrackCount)
		{
			string extension=Path.GetExtension(path);
			if(extension==".xwb")
			{
				string pathRWFB=Path.ChangeExtension(path,"rwfb");
				if(File.Exists(pathRWFB))
				{
					extension=".rwfb";
					path=pathRWFB;
				}
				else
				{
					return LoadBankXWB(path);
				}
			}
			if(extension==".rwfb")
			{
				return LoadBankRWFB(path,expectedTrackCount);
			}
			else
			{
				throw new NotSupportedException("Extensions type "+extension+" not supported");
			}
		}

		private static RWFB LoadBankXWB(string path)
		{
			RWFB rwfb=new RWFB();
			foreach(byte[] data in ConverterXWB.ConvertWinXWB(path))
			{
				rwfb.tracks.Add(TrackData.FromPCM(data));
			}

			//Correct the order
			CorrectTrackOrderXWB(rwfb.tracks);

			//Save the rwfb file
			SaveRWFB(rwfb,Path.ChangeExtension(path,"rwfb"));
			return rwfb;
		}

		private static void CorrectTrackOrderXWB(IList<TrackData> tracks)
		{
			//Swap Overworld Day and Night (track 1 and 3)
			TrackData overworldDay=tracks[3];
			tracks[3]=tracks[1];
			tracks[1]=overworldDay;

			//Move Underground from position 12 to 4
			TrackData undergroundTrack=tracks[12];
			tracks.RemoveAt(12);
			tracks.Insert(4,undergroundTrack);
		}

		private static RWFB LoadBankRWFB(string path,int expectedTrackCount)
		{
			using(FileStream fileIn=File.OpenRead(path))
			{
				BinaryReader reader=new BinaryReader(fileIn);
				if(!ArrayEquals(RWFB_FORMAT,reader.ReadBytes(4))){throw new FileFormatException("Invalid RWFB file");}
				byte version=reader.ReadByte();
				byte trackCount=reader.ReadByte();
				if(version==CURRENT_RWFB_VERSION && trackCount==expectedTrackCount)//Correct version and number of tracks so load it
				{
					RWFB rwfb=new RWFB();
					while(reader.PeekChar()!=-1)//While has data//(--trackCount>=0)
					{
						rwfb.tracks.Add(TrackData.FromRFW(reader));
					}
					return rwfb;
				}
			}
			return LoadBankXWB(Path.ChangeExtension(path,"xwb"));//Remake the file
		}

		private static void SaveRWFB(RWFB rwfb,string path)
		{
			using(FileStream fileOut=File.OpenWrite(path))
			{
				BinaryWriter writer=new BinaryWriter(fileOut);
				writer.Write(RWFB_FORMAT);
				writer.Write((byte)CURRENT_RWFB_VERSION);
				writer.Write((byte)(rwfb.tracks.Count-1));
				for(int i=1;i<rwfb.tracks.Count;i++)
				{
					rwfb.tracks[i].ToRFW(writer);
				}
			}
		}

		private static bool ArrayEquals(byte[] a,byte[] b)
		{
			if(a.Length==b.Length)
			{
				for(int i=0;i<a.Length;i++)
				{
					if(a[i]!=b[i])
					{
						return false;
					}
				}
				return true;
			}
			return false;
		}

		public MusicRWF GetMusic(int index)
		{
			return tracks[index].ToMusic();
		}
	}

	public class MusicRWF:MusicStreaming
	{
		public MusicRWF(int channels,int sampleRate,byte[] data)
		{
			this.stream=new MemoryStream(data);
			dataStart=0;
			SetupSoundEffectInstance(sampleRate,channels);
		}
	}
}
