using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Terraria.ModLoader.Audio.XWB
{
	internal static class ConverterXWB
	{
		const int MiniFormatTag_PCM		=0x0;
		const int MiniFormatTag_XMA		=0x1;
		const int MiniFormatTag_ADPCM	=0x2;
		const int MiniFormatTag_WMA		=0x3;

		const int FLAG_COMPACT			=0x00020000;

		// WAV Encoding
		private static readonly byte[] RIFF = Encoding.UTF8.GetBytes("RIFF");
		private static readonly byte[] WAVE = Encoding.UTF8.GetBytes("WAVE");
		private static readonly byte[] fmt_ = Encoding.UTF8.GetBytes("fmt ");//Note the space after fmt.
		private static readonly byte[] data = Encoding.UTF8.GetBytes("data");
		private static readonly byte[] XWMA = Encoding.UTF8.GetBytes("XWMA");
		private static readonly byte[] dpds = Encoding.UTF8.GetBytes("dpds");

		private static readonly int wavHeaderSize = RIFF.Length + 4 + WAVE.Length + fmt_.Length + 4 + 2 + 2 + 4 + 4 + 2 + 2 + data.Length + 4;

		private static readonly int[] wmaAverageBytesPerSec	=new[]{12000,24000,4000,6000,8000,20000};
		private static readonly int[] wmaBlockAlign			=new[]{929,1487,1280,2230,8917,8192,4459,5945,2304,1536,1485,1008,2731,4096,6827,5462};


		//internal static IList<byte[]> trackByteData;

		struct Segment
		{
			public int Offset;
			public int Length;
		}

		public static IList<byte[]> ConvertWinXWB(string inputXWB)
		{
			int flagsAndDuration;
			int format;
			int playRegionOffset;
			int playRegionLength;
			int loopRegionOffset;
			int loopRegionLength;
			int wavebank_offset;
			int version;
			int version_header;
			Segment[] segments=new Segment[5];

			int flags;
			int entryCount;
			
			int entryMetaDataElementSize;
			int entryNameElementSize;
			int alignment;

			IList<byte[]> trackData=new List<byte[]>();

			using(FileStream fileIn=File.OpenRead(inputXWB))
			{

				FFmpeg.SetupFfmpegEXE();
				//bool firstWritten = false;

				BinaryReader reader = new BinaryReader(fileIn);

				//List<Music> tracks = new List<Music>();
				//trackByteData=new List<byte[]>();
				//trackByteData.Add(null);//Because track index 0 is unused

				flagsAndDuration=0;
				format=0;
				playRegionOffset=0;
				playRegionLength=0;
				loopRegionOffset=0;
				loopRegionLength=0;

				wavebank_offset = 0;

				if(Encoding.UTF8.GetString(reader.ReadBytes(4)) != "WBND")
				{
					throw new Exception("Not an XWB file");
				}

				version = reader.ReadInt32();
				version_header=reader.ReadInt32();// Skip header verion (only for version>=42)

				if(version != 46)
				{
					throw new Exception("unsupported version: " + version);
				}

				for(int i=0;i<5;i++)
				{
					segments[i].Offset=reader.ReadInt32();
					segments[i].Length=reader.ReadInt32();
				}
				
				//WaveBank Data
				reader.BaseStream.Position=segments[0].Offset;

				flags					=reader.ReadInt32();
				entryCount				=reader.ReadInt32();

				//Terraria's wave bank's name.
				string bankName = Encoding.UTF8.GetString(reader.ReadBytes(64)).Replace("\0", "");

				entryMetaDataElementSize=reader.ReadInt32();
				entryNameElementSize	=reader.ReadInt32();
				alignment				=reader.ReadInt32(); 
				wavebank_offset			=segments[1].Offset;
				
				if((flags & FLAG_COMPACT) != 0)
				{
					throw new Exception("compact wavebanks are not supported");
				}

				int playregion_offset = segments[segments.Length-1].Offset;

				
				//if(playRegionOffset == 0)
				//{
				//	//Left out from MonoGame
				//	playRegionOffset = wavebank_offset+(entryCount * entryMetaDataElementSize);
				//}

				//int segidx_entry_name = 2;
				//if (version >= 42)
				//{
				//	segidx_entry_name = 3;
				//}
				//if(segments[segidx_entry_name].Offset!=0 && segments[segidx_entry_name].Length!=0)
				//{
				//	if(entryNameElementSize==-1)
				//	{
				//		entryNameElementSize=0;
				//	}
				//	byte[] entry_name=new byte[entryNameElementSize+1];
				//	entry_name[entryNameElementSize]=0;
				//	reader.BaseStream.Position=segments[segidx_entry_name].Offset;
				//	Console.WriteLine(Encoding.UTF8.GetString(reader.ReadBytes(segments[segidx_entry_name].Length)));
				//}


				for(int current_entry = 0;current_entry < entryCount;current_entry++)
				{
					reader.BaseStream.Position=wavebank_offset;
					if(entryMetaDataElementSize >= 4)	{flagsAndDuration=reader.ReadInt32();}//FlagsAndDuration
					if(entryMetaDataElementSize >= 8)	{format=reader.ReadInt32();}
					if(entryMetaDataElementSize >= 12)	{playRegionOffset=reader.ReadInt32();}
					if(entryMetaDataElementSize >= 16)	{playRegionLength=reader.ReadInt32();}
					if(entryMetaDataElementSize >= 20)	{loopRegionOffset=reader.ReadInt32();}//LoopRegionOffset
					if(entryMetaDataElementSize >= 24)	{loopRegionLength=reader.ReadInt32();}//LoopRegionLength

					wavebank_offset += entryMetaDataElementSize;
					playRegionOffset += playregion_offset;

					int codec	= ((format)		&0x3);		//((1 << 2) - 1)
					int chans	= ((format>>2)	&0x7);		//((1 << 3) - 1)
					int rate	= ((format>>5)	&0x3FFFF);	//((1 << 18) - 1)
					int align	= ((format>>23)	&0xFF);		//((1 << 8) - 1)

					reader.BaseStream.Position=playRegionOffset;
					MemoryStream audioDataStream = new MemoryStream(reader.ReadBytes(playRegionLength));

					// The codecs used by Terraria are currently xWMA and ADPCM.
					// The xWMA format is not supported by FNA, so it's only used
					// on Windows. This implementation uses ffmpeg to convert the raw
					// xWMA data to WAVE; a minified Windows executable is embedded.
					// PCM was introduced for the last tracks in the 1.3.3 update.
					byte[] buffer_pcm;
					switch(codec)
					{
						case MiniFormatTag_ADPCM:
						{
							// Convert ADPCM data to PCM
							MSADPCMToPCM.MSADPCM_TO_PCM(ref audioDataStream,(short)chans,(short)align);
							goto case MiniFormatTag_PCM;
						}
						case MiniFormatTag_PCM:
						{
							buffer_pcm=Extract_PCM(audioDataStream,chans,rate,align);
							break;
						}
						case MiniFormatTag_WMA:
						{
							buffer_pcm=FFmpeg.Convert(Extract_xWMA(audioDataStream,chans,rate,align,playRegionLength));
							break;
						}
						default: throw new Exception("unimplemented codec " + codec);
					}
					//if(trackData.Count==4&&!firstWritten)
					//{
					//	firstWritten=true;
					//	using(FileStream testFile = File.Create("N:\\Rartrin\\Desktop\\Test.wav"))
					//	{
					//		testFile.Write(buffer_pcm,0,buffer_pcm.Length);
					//	}
					//}
					trackData.Add(buffer_pcm);
				}
				FFmpeg.DeleteFFmpegExe();
			}
			return trackData;
		}

		//Assembles the xWMA data and returns it in the format of a xWMA file stream. (Not an actual file stream though)
		private static byte[] Extract_xWMA(MemoryStream audiodata,int chans,int rate,int align,int playRegionLength)
		{
			MemoryStream xwmaStreamOut=new MemoryStream();
			// Note that it could still be another codec than xWma,
			// but that scenario isn't handled here.

			// This part has been ported from XWMA-to-pcm-u8
			// Not the most beautiful code in the world,
			// but it does the job.

			// I do not know if this code outputs valid XWMA files,
			// but FFMPEG accepts them so it's all right for this usage.

			//MemoryStream xwmaStreamOut = new MemoryStream();
			// xWmaOutput.write(output.array(), output.arrayOffset(), output.position());
			BinaryWriter writer = new BinaryWriter(xwmaStreamOut);
			//Riff Header chunk
			writer.Write(RIFF);
			int odRIChunkSize = (int)xwmaStreamOut.Position;
			writer.Write((int)0);		// Full file size, ignored by ffmpeg
			writer.Write(XWMA);
			//Format chunk
			
			int averageBytesPerSec = align > wmaAverageBytesPerSec.Length ? wmaAverageBytesPerSec[align >> 5] : wmaAverageBytesPerSec[align];
			int blockAlign = align > wmaBlockAlign.Length ? wmaBlockAlign[align & 0xf] : wmaBlockAlign[align];

			writer.Write(fmt_);	
			writer.Write((int)18);					//Header size
			writer.Write((short)0x161);				//WMA Version 2
			writer.Write((short)chans);				//Channel count
			writer.Write((int)rate);				//Samples per second
			writer.Write((int)averageBytesPerSec);	//Average bytes per second
			writer.Write((short)blockAlign);		//Block align
			writer.Write((short)16);				//Bits per sample
			writer.Write((short)0);					//ExtraData
			
			//dpds chunk
			writer.Write(dpds);

			int packetLength = blockAlign;
			int packetNum = (int)(audiodata.Length / packetLength);
			
			writer.Write((int)(packetNum * 4));

			int fullSize = (playRegionLength * averageBytesPerSec % 4096 != 0) ? (1 + (int)(playRegionLength * averageBytesPerSec / 4096)) * 4096 : playRegionLength;
			int allBlocks = fullSize / 4096;
			int avgBlocksPerPacket = allBlocks / packetNum;
			int spareBlocks = allBlocks - (avgBlocksPerPacket * packetNum);

			int accu = 0;
			for(int i = 0;i < packetNum;++i)
			{
				accu += avgBlocksPerPacket * 4096;
				if(spareBlocks != 0)
				{
					accu += 4096;
					spareBlocks--;
				}
				writer.Write((int)accu);
			}

			writer.Write(data);
			writer.Write((int)playRegionLength);
			audiodata.WriteTo(xwmaStreamOut);

			// Replacing the file size placeholder, dosen't matter with ffmpeg
			long pos = writer.BaseStream.Position;
			writer.BaseStream.Position=odRIChunkSize;
			writer.Write((int)(pos - 8));
			writer.BaseStream.Position=pos;
			return xwmaStreamOut.ToArray();
		}

		//Assembles the PCM data and returns it in the format of a wav file stream. (Not an actual file stream though)
		private static byte[] Extract_PCM(MemoryStream audiodata,int chans,int rate,int align)
		{
			MemoryStream wavStreamOut=new MemoryStream();
			int bitsPerSample = 16;
			int blockAlign = (bitsPerSample / 8) * chans;
			//MemoryStream wavStreamOut = new MemoryStream(wavHeaderSize);
			BinaryWriter writer = new BinaryWriter(wavStreamOut);

			writer.Write(RIFF);							// chunk id
			writer.Write((int)(audiodata.Length + 36));	// chunk size
			writer.Write(WAVE);							// RIFF type

			writer.Write(fmt_);							// chunk id
			writer.Write((int)16);						// format header size
			writer.Write((short)0x1);					// format (PCM)
			writer.Write((short)chans);					// channels
			writer.Write((int)rate);					// samples per second
			writer.Write((int)(rate*blockAlign));		// byte rate/average bytes per second
			writer.Write((short)blockAlign);
			writer.Write((short)bitsPerSample);

			writer.Write(data);							// chunk id
			writer.Write((int)audiodata.Length);		// data size
			audiodata.WriteTo(wavStreamOut);
			return wavStreamOut.ToArray();
		}
	}
}
