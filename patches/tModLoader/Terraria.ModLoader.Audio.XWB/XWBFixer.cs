#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2017 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */

/* The unxwb project, written by Luigi Auriemma, was released in 2006 under the
 * GNU General Public License, version 2.0:
 *
 * http://www.gnu.org/licenses/gpl-2.0.html
 *
 * While the unxwb project was released under the GPL, Luigi has given express
 * permission to the MonoGame project to use code from unxwb under the MonoGame
 * project license. See LICENSE for details.
 *
 * The unxwb website can be found here:
 *
 * http://aluigi.altervista.org/papers.htm#xbox
 */
#endregion

#region Using Statements
using System;
using System.IO;
using System.Text;
using Terraria.ModLoader.Audio.XWB;
#endregion

namespace Terraria.ModLoader.Audio
{
	public static class XWBFixer
	{
		private const int AudioEngineContentVersion = 46;

		private const int MiniFormatTag_PCM		=0x0;
		private const int MiniFormatTag_XMA		=0x1;
		private const int MiniFormatTag_ADPCM	=0x2;
		private const int MiniFormatTag_WMA		=0x3;

		private const int FLAG_COMPACT			=0x00020000;

		// WAV Encoding
		private static readonly byte[] RIFF = Encoding.UTF8.GetBytes("RIFF");
		private static readonly byte[] WAVE = Encoding.UTF8.GetBytes("WAVE");
		private static readonly byte[] fmt_ = Encoding.UTF8.GetBytes("fmt ");//Note the space after fmt.
		private static readonly byte[] data = Encoding.UTF8.GetBytes("data");
		private static readonly byte[] XWMA = Encoding.UTF8.GetBytes("XWMA");
		private static readonly byte[] dpds = Encoding.UTF8.GetBytes("dpds");

		private static readonly int[] wmaAverageBytesPerSec	=new[]{12000,24000,4000,6000,8000,20000};
		private static readonly int[] wmaBlockAlign			=new[]{929,1487,1280,2230,8917,8192,4459,5945,2304,1536,1485,1008,2731,4096,6827,5462};

		private class HeaderInfo
		{
			public uint fileHeader;
			public uint contentVersion;
			public uint toolVersion;
			public readonly uint[] chunkOffsets = new uint[5];
			public readonly uint[] chunkLengths = new uint[5];

			public readonly uint Length=4+4+4+5+5;
		}

		private class WaveBankInfo
		{
			public bool isStreaming;
			public byte unused;
			public ushort wavebankFlags;
			public uint numEntries;
			public readonly byte[] name_bytes=new byte[64];
			public uint metadataElementSize;
			public uint nameElementSize;
			public uint alignment;
			
			public readonly uint Length=1+1+2+4+64+4+4+4;
		}

		// Used to store sound entry data, mainly for streaming WaveBanks
		private class SoundEntry
		{
			public uint FlagsAndDuration;//unused
			public uint PlayOffset;
			public uint PlayLength;
			public uint LoopOffset;
			public uint LoopLength;
			public byte[] Data;
			
			public uint Format;
			// Parse Format for Wavedata information
			public uint Codec
			{
				get=>(Format >> 0)	&0x03;
				set=>Format=(Format&~0x3U)|((value&0x03U)<<0);
			}
			public uint Channels	=>(Format >> 2)	&0x07;
			public uint Frequency	=>(Format >> 5)	&0x03FFFF;
			public uint Alignment	=>(Format >> 23)&0xFF;
			public uint BitDepth	=>(Format >> 31);
			
			private int referenceCount=0;

			public bool AddReference()
			{
				// Specifically 1. 2+ means it's already allocated
				return (++referenceCount == 1);
			}

			public bool SubReference()
			{
				return (--referenceCount == 0);
			}

			public SoundEntry(uint size){Length=size;}

			public readonly uint Length;
		}

		[Obsolete("Converts to wav but doesn't convert back to xwb")]
		private static void ParseWaveBank(string input,string output)
		{
			/* Until we finish the LoadWaveBank process, this WaveBank is NOT
			 * ready to run. For us this doesn't really matter, but the game
			 * could be loading WaveBanks asynchronously, so let's be careful.
			 * -flibit
			 */
			HeaderInfo header=new HeaderInfo();

			//Chunk 0: Wavebank info
			WaveBankInfo wavebank=new WaveBankInfo();

			//Chunk 1: Wavadata
			SoundEntry[] tracks;

			//Chunk 4: Play region


			using(FileStream fileIn=File.OpenRead(input))
			{
				BinaryReader reader=new BinaryReader(fileIn);

				header.fileHeader=reader.ReadUInt32();
				//Check the file header. Should be 'WBND'
				if(header.fileHeader!=0x444E4257){throw new ArgumentException("WBND format not recognized!");}
				
				header.contentVersion=reader.ReadUInt32();
				//Check the content version. Assuming XNA4 Refresh.
				if(header.contentVersion!=AudioEngineContentVersion){throw new ArgumentException("WBND Content version!");}

				header.toolVersion=reader.ReadUInt32();
				//Check the tool version. Assuming XNA4 Refresh.
				if(header.toolVersion!=44){throw new ArgumentException("WBND Tool version!");}

				// Obtain WaveBank chunk offsets/lengths
				for (int i = 0; i < 5; i += 1)
				{
					header.chunkOffsets[i] = reader.ReadUInt32();
					header.chunkLengths[i] = reader.ReadUInt32();
				}



				// Seek to the first offset, obtain WaveBank info
				reader.BaseStream.Seek(header.chunkOffsets[0], SeekOrigin.Begin);

				// IsStreaming bool, unused
				wavebank.isStreaming=reader.ReadBoolean();
				wavebank.unused=reader.ReadByte();

				// WaveBank Flags
				wavebank.wavebankFlags = reader.ReadUInt16();
				//bool containsEntryNames	=(wavebank.wavebankFlags & 0x0001) != 0;
				bool compact			=(wavebank.wavebankFlags & 0x0002) != 0;
				//bool syncDisabled		=(wavebank.wavebankFlags & 0x0004) != 0;
				//bool containsSeekTables =(wavebank.wavebankFlags & 0x0008) != 0;

				// WaveBank Entry Count
				wavebank.numEntries=reader.ReadUInt32();
				tracks = new SoundEntry[wavebank.numEntries];

				// WaveBank Name
				reader.Read(wavebank.name_bytes,0,64);
				string name = Encoding.UTF8.GetString(wavebank.name_bytes).Replace("\0", "");

				// WaveBank entry information
				wavebank.metadataElementSize = reader.ReadUInt32();
				wavebank.nameElementSize=reader.ReadUInt32();
				wavebank.alignment = reader.ReadUInt32();



				// Determine the generic play region offset
				uint playRegionOffset = header.chunkOffsets[4];
				if (playRegionOffset == 0)
				{
					playRegionOffset = header.chunkOffsets[1] + (wavebank.numEntries * wavebank.metadataElementSize);
				}
				
				if (compact){throw new NotSupportedException("Does not support compact format.");}

				// Read in the wavedata
				uint curOffset = header.chunkOffsets[1];
				for (int curEntry = 0; curEntry < wavebank.numEntries; curEntry += 1)
				{
					// Seek to the current entry
					reader.BaseStream.Seek(curOffset, SeekOrigin.Begin);

					// Entry Information
					SoundEntry entry=new SoundEntry(wavebank.metadataElementSize);

					// Obtain Entry Information
					if(wavebank.metadataElementSize >= 4)	{entry.FlagsAndDuration	= reader.ReadUInt32();}
					if(wavebank.metadataElementSize >= 8)	{entry.Format			= reader.ReadUInt32();}
					if(wavebank.metadataElementSize >= 12)	{entry.PlayOffset		= reader.ReadUInt32();}
					if(wavebank.metadataElementSize >= 16)	{entry.PlayLength		= reader.ReadUInt32();}
					if(wavebank.metadataElementSize >= 20)	{entry.LoopOffset		= reader.ReadUInt32();}
					if(wavebank.metadataElementSize >= 24)	{entry.LoopLength		= reader.ReadUInt32();}
					else
					{
						// FIXME: This is a bit hacky.
						if (entry.PlayLength != 0)
						{
							entry.PlayLength = header.chunkLengths[4];
						}
					}

					// Update seek offsets
					curOffset += wavebank.metadataElementSize;
					entry.PlayOffset += playRegionOffset;

					// Read Wavedata
					reader.BaseStream.Seek(entry.PlayOffset, SeekOrigin.Begin);
					entry.Data = reader.ReadBytes((int)entry.PlayLength);

					tracks[curEntry]=entry;
				}
			}





		}

		private static void Convert(SoundEntry entry)
		{
			if(entry.Codec!=0x3){throw new NotSupportedException();}
			MemoryStream audiodata=new MemoryStream(entry.Data);
			uint chans=entry.Channels;
			uint rate=entry.Frequency;
			uint align=entry.Alignment;
			uint playRegionLength=entry.PlayLength;

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
			BinaryWriter writerXWMA = new BinaryWriter(xwmaStreamOut);
			//Riff Header chunk
			writerXWMA.Write(RIFF);
			int odRIChunkSize = (int)xwmaStreamOut.Position;
			writerXWMA.Write((int)0);		// Full file size, ignored by ffmpeg
			writerXWMA.Write(XWMA);
			//Format chunk
			
			int averageBytesPerSec = align > wmaAverageBytesPerSec.Length ? wmaAverageBytesPerSec[align >> 5] : wmaAverageBytesPerSec[align];
			int blockAlignWMA = align > wmaBlockAlign.Length ? wmaBlockAlign[align & 0xf] : wmaBlockAlign[align];

			writerXWMA.Write(fmt_);	
			writerXWMA.Write((int)18);					//Header size
			writerXWMA.Write((short)0x161);				//WMA Version 2
			writerXWMA.Write((short)chans);				//Channel count
			writerXWMA.Write((int)rate);				//Samples per second
			writerXWMA.Write((int)averageBytesPerSec);	//Average bytes per second
			writerXWMA.Write((short)blockAlignWMA);		//Block align
			writerXWMA.Write((short)16);				//Bits per sample
			writerXWMA.Write((short)0);					//ExtraData
			
			//dpds chunk
			writerXWMA.Write(dpds);

			int packetLength = blockAlignWMA;
			int packetNum = (int)(audiodata.Length / packetLength);
			
			writerXWMA.Write((int)(packetNum * 4));

			int fullSize = (playRegionLength * averageBytesPerSec % 4096 != 0) ? (1 + (int)(playRegionLength * averageBytesPerSec / 4096)) * 4096 : (int)playRegionLength;
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
				writerXWMA.Write((int)accu);
			}

			writerXWMA.Write(data);
			writerXWMA.Write((int)playRegionLength);
			audiodata.WriteTo(xwmaStreamOut);

			// Replacing the file size placeholder, dosen't matter with ffmpeg
			long pos = writerXWMA.BaseStream.Position;
			writerXWMA.BaseStream.Position=odRIChunkSize;
			writerXWMA.Write((int)(pos - 8));
			writerXWMA.BaseStream.Position=pos;

			byte[] buffer_PCM=FFmpeg.Convert(xwmaStreamOut.ToArray());
			
			int		compression;
			short	channels=0;
			int		sampleRate=0;
			int		avgBPS;
			short	blockAlignWAV;
			short	bitDepth;
			byte[]	waveData=null;

			MemoryStream stream_PCM=new MemoryStream(buffer_PCM);
			BinaryReader readerWAV	= new BinaryReader(stream_PCM);
			string headerChunkID	= Encoding.UTF8.GetString(readerWAV.ReadBytes(4));//"RIFF"
			int fileSize			= readerWAV.ReadInt32();//fileSize-8 for the first two fields
			string riffType			= Encoding.UTF8.GetString(readerWAV.ReadBytes(4));//"WAVE"

			while(readerWAV.PeekChar()!=-1)//While reader has data in it
			{
				string chunkID=Encoding.UTF8.GetString(readerWAV.ReadBytes(4));
				int chunkSize=readerWAV.ReadInt32();

				switch(chunkID)
				{
					case "fmt ":
					{
						compression		= readerWAV.ReadInt16();//1 means no compression, anything else means it is compressed
						channels		= readerWAV.ReadInt16();
						sampleRate		= readerWAV.ReadInt32();
						avgBPS			= readerWAV.ReadInt32();
						blockAlignWAV	= readerWAV.ReadInt16();
						bitDepth		= readerWAV.ReadInt16();
						if (chunkSize == 18)
						{
							// Read any extra values
							int fmtExtraSize = readerWAV.ReadInt16();
							readerWAV.ReadBytes(fmtExtraSize);
						}
						break;
					}
					case "data":
					{
						waveData=readerWAV.ReadBytes(chunkSize);
						break;
					}
					default:
					{
						#if DEBUG
						Console.WriteLine("Unused Chunk: {0} - {1}",chunkID,chunkSize);
						#endif
						readerWAV.ReadBytes(chunkSize);
						break;
					}
				}
			}

		}
	}
}