using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using ReLogic.OS;
using Terraria.ModLoader.Audio.XWB;

namespace Terraria.ModLoader.Audio
{
	/// <summary>
	/// Big thx to 0x0ade for the converter, this made this a lot more before!
	/// </summary>
	class WavebankConverter
	{
		public static bool IsFFMPEGAvailable
		{

			get
			{
				try
				{
					string _path = Environment.CurrentDirectory + "\\Content\\";
					using (Process process = new Process())
					{
						process.StartInfo.FileName = (Environment.OSVersion.Platform < (PlatformID)5 & Platform.IsWindows) == Platform.IsWindows ? "where" :
							"which";
						process.StartInfo.Arguments = "ffmpeg";
						process.StartInfo.UseShellExecute = false;
						process.StartInfo.CreateNoWindow = true;
						process.StartInfo.WorkingDirectory = _path;
						//process.StartInfo.WorkingDirectory = Environment.CurrentDirectory + "\\Content\\";
						process.Start();
						process.WaitForExit();
						return process.ExitCode == 0;
					}
				}
				catch (Exception e)
				{
					ErrorLogger.Log("Could not determine if FFMPEG available: " + e);
					return false;
				}
			}
		}

		public static class XWMAInfo
		{
			public readonly static int[] BytesPerSecond = { 12000, 24000, 4000, 6000, 8000, 20000 };
			public readonly static short[] BlockAlign = { 929, 1487, 1280, 2230, 8917, 8192, 4459, 5945, 2304, 1536, 1485, 1008, 2731, 4096, 6827, 5462 };
		}

		// Assume the same info - XMA is just WMA Pro, right? Right?! >.<
		public static class XMAInfo
		{
			public readonly static int[] BytesPerSecond = { 12000, 24000, 4000, 6000, 8000, 20000 };
			public readonly static short[] BlockAlign = { 929, 1487, 1280, 2230, 8917, 8192, 4459, 5945, 2304, 1536, 1485, 1008, 2731, 4096, 6827, 5462 };
		}

		public const uint XWBHeader = 0x444E4257; // WBND
		public const uint XWBHeaderX360 = 0x57424E44; // DNBW

		public static void UpdateContent(string path, bool patchXNB = true, bool patchXACT = true, bool patchWindowsMedia = true)
		{
			PatchContent(path, UpdateWaveBank);
			FFmpeg.DeleteFFmpegExe();
		}

		public static void UpdateWaveBank(string path, BinaryReader reader, BinaryWriter writer)
		{
			if (!IsFFMPEGAvailable)
			{
				ErrorLogger.Log("[UpdateWaveBank] FFMPEG is missing - won't convert unsupported WaveBanks");
				reader.BaseStream.CopyTo(writer.BaseStream);
				return;
			}
			ErrorLogger.Log($"[UpdateWaveBank] Updating wave bank {path}");

			uint offset;

			// Check WaveBank header against XNBHeader / XNBHeaderX360
			uint header = reader.ReadUInt32();
			bool x360 = header == XWBHeaderX360;
			writer.Write(XWBHeader);

			// WaveBank versions (content, tool)
			writer.Write(SwapEndian(x360, reader.ReadUInt32()));
			writer.Write(SwapEndian(x360, reader.ReadUInt32()));

			uint[] regionOffsets = new uint[5];
			uint[] regionLengths = new uint[5];
			long regionPosition = reader.BaseStream.Position; // Used to update the regions after conversion
			for (int i = 0; i < 5; i++)
			{
				regionOffsets[i] = SwapEndian(x360, reader.ReadUInt32());
				writer.Write(regionOffsets[i]);
				regionLengths[i] = SwapEndian(x360, reader.ReadUInt32());
				writer.Write(regionLengths[i]);
			}

			// We don't really care about what's going on here... but we should, especially taking X360 into account.

			writer.Write(reader.ReadBytesUntil(regionOffsets[0])); // Offset

			uint flags = SwapEndian(x360, reader.ReadUInt32());
			writer.Write(flags);
			if ((flags & 0x00000002) == 0x00000002)
			{
				// Compact mode - abort!
				if (x360)
					throw new InvalidDataException("Can't handle compact mode Xbox 360 wave banks - Content directory left in unstable state");
				reader.BaseStream.CopyTo(writer.BaseStream);
				return;
			}
			uint count = SwapEndian(x360, reader.ReadUInt32());
			writer.Write(count);
			writer.Write(reader.ReadBytes(64)); // Name

			uint metaSize = SwapEndian(x360, reader.ReadUInt32());
			writer.Write(metaSize);
			writer.Write(SwapEndian(x360, reader.ReadUInt32())); // Name size
			writer.Write(SwapEndian(x360, reader.ReadUInt32())); // Alignment

			uint playRegionOffset = regionOffsets[4];
			if (playRegionOffset == 0)
				playRegionOffset = regionOffsets[1] + count * metaSize;

			uint[] duration = new uint[count];

			long[] playOffsetPos = new long[count]; // Used to update the offsets after conversion
			uint[] playOffset = new uint[count];
			uint[] playOffsetUpdated = new uint[count];

			long[] playLengthPos = new long[count]; // Used to update the lengths after conversion
			int[] playLength = new int[count];
			int[] playLengthUpdated = new int[count];

			long[] formatPos = new long[count]; // Used to update the codecs after conversion
			uint[] codec = new uint[count];
			uint[] channels = new uint[count];
			uint[] rate = new uint[count];
			uint[] align = new uint[count];
			uint[] depth = new uint[count];

			offset = regionOffsets[1];
			uint durationRaw;
			uint format = 0;
			// Metadata
			for (int i = 0; i < count; i++)
			{
				Console.Error.WriteLine("i value : " + i);
				// Whoops, we're leaving a bunch of data as-is...
				writer.Write(reader.ReadBytesUntil(offset));

				if (metaSize >= 4)
				{
					durationRaw = SwapEndian(x360, reader.ReadUInt32());
					writer.Write(durationRaw);
					duration[i] = durationRaw >> 4;
				}
				if (metaSize >= 8)
				{
					formatPos[i] = reader.BaseStream.Position;
					writer.Write(format = SwapEndian(x360, reader.ReadUInt32()));
				}
				if (metaSize >= 12)
				{
					playOffsetPos[i] = reader.BaseStream.Position;
					writer.Write(playOffset[i] = playOffsetUpdated[i] = SwapEndian(x360, reader.ReadUInt32()));
				}
				if (metaSize >= 16)
				{
					playLengthPos[i] = reader.BaseStream.Position;
					writer.Write(((uint)(playLength[i] = playLengthUpdated[i] = (int)SwapEndian(x360, reader.ReadUInt32()))));
				}
				if (metaSize >= 20)
					writer.Write(SwapEndian(x360, reader.ReadUInt32()));
				if (metaSize >= 24)
				{
					writer.Write(SwapEndian(x360, reader.ReadUInt32()));
				}
				else if (playLength[i] != 0)
					playLength[i] = (int)regionLengths[4];

				offset += metaSize;
				playOffset[i] += playRegionOffset;

				codec[i] = (format >> 0) & ((1 << 2) - 1);
				channels[i] = (format >> 2) & ((1 << 3) - 1);
				rate[i] = (format >> (2 + 3)) & ((1 << 18) - 1);
				align[i] = (format >> (2 + 3 + 18)) & ((1 << 8) - 1);
				depth[i] = (format >> (2 + 3 + 18 + 8));
			}


			// Read "seek tables" if they exist. They're required for XMA support. (Thanks, xnb_parse!)
			uint[][] seekTables = new uint[count][];
			if ((flags & 0x00080000) == 0x00080000)
			{
				// Whoops, we're leaving a bunch of data as-is...
				writer.Write(reader.ReadBytesUntil(regionOffsets[2]));

				uint[] seekOffsets = new uint[count];
				for (int i = 0; i < count; i++)
				{
					seekOffsets[i] = SwapEndian(x360, reader.ReadUInt32());
					writer.Write(seekOffsets[i]);
				}

				writer.Flush();
				offset = (uint)writer.BaseStream.Position;
				for (int i = 0; i < count - 1; i++)
				{
					if (i > 41)
					{
						i = 0;
					}

					writer.Write(reader.ReadBytesUntil(offset + seekOffsets[i]));
					uint packetCount = SwapEndian(x360, reader.ReadUInt32());
					writer.Write(packetCount);
					Console.Error.WriteLine("i : " + i);
					Console.Error.WriteLine("packet count : " + packetCount);
					uint[] data = seekTables[i] = new uint[packetCount];
					Console.Error.WriteLine("dataLength : " + data.Length);
					for (int pi = 0; pi < packetCount; pi++)
					{
						try
						{
							data[pi] = SwapEndian(x360, reader.ReadUInt32());
							writer.Write(data[pi]);
							Console.Error.WriteLine("pi : " + pi);
							Console.Error.WriteLine("data[pi] : " + data[pi]);
						}
						catch (Exception e)
						{
							Console.WriteLine(e);
						}
					}
				}
			}

			// Sound data
			for (int i = 0; i < count; i++)
			{
				try
				{
					writer.Write(reader.ReadBytesUntil(playOffset[i]));

					if (codec[i] != 1 && codec[i] != 3)
					{
						writer.Write(reader.ReadBytes(playLength[i]));
						continue;
					}

					writer.Flush();
					offset = (uint)writer.BaseStream.Position;

					// We need to feed FFMPEG with correctly formatted data.
					Action<Process> feeder = null;

					if (codec[i] == 3) // XWMA
						feeder = GenerateXWMAFeeder(reader, align[i], playLength[i], duration[i], channels[i], rate[i]);
					else if (codec[i] == 1) // XMA2
						feeder = GenerateXMA2Feeder(reader, align[i], playLength[i], duration[i], channels[i], rate[i], seekTables[i]);

					ErrorLogger.Log($"[UpdateWaveBank] Converting #{i}");
					ConvertAudio(reader.BaseStream, writer.BaseStream, feeder, playLength[i]);
					channels[i] = 1;

					writer.Flush();
					uint length = (uint)writer.BaseStream.Position - offset;
					offset = (uint)writer.BaseStream.Position;
					uint lengthOffset = length - (uint)playLength[i];

					// Update codec and format
					codec[i] = 0;
					depth[i] = 0; // 0: pcm_u8; 1: pcm_s16le
					align[i] = 0;
					if (formatPos[i] != 0)
					{
						writer.Flush();
						writer.BaseStream.Seek(formatPos[i], SeekOrigin.Begin);
						writer.Write(
							((codec[i] & ((1 << 2) - 1)) << 0) |
							((channels[i] & ((1 << 3) - 1)) << 2) |
							((rate[i] & ((1 << 18) - 1)) << (2 + 3)) |
							((align[i] & ((1 << 8) - 1)) << (2 + 3 + 18)) |
							(depth[i] << (2 + 3 + 18 + 8))
						);
					}

					// Update length and all subsequent positions
					if (playLengthPos[i] != 0)
					{
						writer.Flush();
						writer.BaseStream.Seek(playLengthPos[i], SeekOrigin.Begin);
						writer.Write(playLengthUpdated[i] = (int)length);
					}
					for (int ii = i + 1; ii < count; ii++)
					{
						if (playOffsetPos[ii] != 0)
						{
							writer.Flush();
							writer.BaseStream.Seek(playOffsetPos[ii], SeekOrigin.Begin);
							writer.Write(playOffsetUpdated[ii] += lengthOffset);
						}
					}

					writer.Flush();
					writer.BaseStream.Seek(offset, SeekOrigin.Begin);
				}
				catch (Exception e)
				{
					Console.Error.WriteLine(e);
				}				
			}

			writer.Flush();
			offset = (uint)writer.BaseStream.Position;

			// Rewrite regions
			regionLengths[4] = offset - regionOffsets[4];
			writer.Flush();
			writer.BaseStream.Seek(regionPosition, SeekOrigin.Begin);
			for (int i = 0; i < 5; i++)
			{
				writer.Write(regionOffsets[i]);
				writer.Write(regionLengths[i]);
			}

			writer.Flush();
			writer.BaseStream.Seek(offset, SeekOrigin.Begin);

			reader.BaseStream.CopyTo(writer.BaseStream);
		}

		public static void PatchContent(string path, Action<string, BinaryReader, BinaryWriter> patcher, bool writeToTmp = true, string pathOutput = null)
		{
			pathOutput = pathOutput ?? path;
			if (writeToTmp)
				File.Delete(path + ".tmp");
			if (pathOutput != path)
				File.Delete(pathOutput);

			using (Stream input = File.OpenRead(path))
			using (BinaryReader reader = new BinaryReader(input))
				if (writeToTmp)
				{
					using (Stream output = File.OpenWrite(path + ".tmp"))
					using (BinaryWriter writer = new BinaryWriter(output))
						patcher(path, reader, writer);
				}
				else
				{
					patcher(path, reader, null);
				}

			if (writeToTmp)
			{
				if (pathOutput == path)
					File.Delete(path);
				File.Move(path + ".tmp", pathOutput);
			}
		}

		public static void ConvertAudio(Stream input, Stream output, Action<Process> feeder, long length)
			// FIXME: stereo causes "Hell Yeah!" to sound horrible with u8 and OpenAL to simply fail everywhere with s16le
			=> RunFFMPEG($"-y -i - -f u8 -ac 1 -", input, output, feeder, length);



		public static Action<Process> GenerateXWMAFeeder(
			BinaryReader reader,
			uint align, int playLength, uint duration, uint channels, uint rate
		) => (Process ffmpeg) => {
			Stream ffmpegStream = ffmpeg.StandardInput.BaseStream;

			using (BinaryWriter ffmpegWriter = new BinaryWriter(ffmpegStream, Encoding.ASCII))
			{
				typeof(BinaryWriter).GetField("_leaveOpen", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ffmpegWriter, true);
				short blockAlign =
					align >= XWMAInfo.BlockAlign.Length ?
					XWMAInfo.BlockAlign[align & 0x0F] :
					XWMAInfo.BlockAlign[align];
				int packets = playLength / blockAlign;
				int blocks = (int)Math.Ceiling(duration / 2048D);
				int blocksPerPacket = blocks / packets;
				int spareBlocks = blocks - blocksPerPacket * packets;

				ffmpegWriter.Write("RIFF".ToCharArray());
				ffmpegWriter.Write(playLength + 4 + 4 + 8 /**/ + 4 + 2 + 2 + 4 + 4 + 2 + 2 + 2 /**/ + 4 + 4 + packets * 4 /**/ + 4 + 4 - 8);
				ffmpegWriter.Write("XWMAfmt ".ToCharArray());

				ffmpegWriter.Write(18);
				ffmpegWriter.Write((short)0x0161);
				ffmpegWriter.Write((short)channels);
				ffmpegWriter.Write(rate);
				ffmpegWriter.Write(
					align >= XWMAInfo.BytesPerSecond.Length ?
					XWMAInfo.BytesPerSecond[align >> 5] :
					XWMAInfo.BytesPerSecond[align]
				);
				ffmpegWriter.Write(blockAlign);
				ffmpegWriter.Write((short)0x0F);
				ffmpegWriter.Write((short)0x00);

				ffmpegWriter.Write("dpds".ToCharArray());
				ffmpegWriter.Write(packets * 4);
				for (int packet = 0, accu = 0; packet < packets; packet++)
				{
					accu += blocksPerPacket * 4096;
					if (spareBlocks > 0)
					{
						accu += 4096;
						--spareBlocks;
					}
					ffmpegWriter.Write(accu);
				}
				ffmpegWriter.Write("data".ToCharArray());
				ffmpegWriter.Write(playLength);
				ffmpegWriter.Flush();
			}

			byte[] dataRaw = new byte[4096];
			int sizeRaw;
			long destination = reader.BaseStream.Position + playLength;
			while (!ffmpeg.HasExited && reader.BaseStream.Position < destination)
			{
				sizeRaw = reader.BaseStream.Read(dataRaw, 0, Math.Min(dataRaw.Length, (int)(destination - reader.BaseStream.Position)));
				ffmpegStream.Write(dataRaw, 0, sizeRaw);
				ffmpegStream.Flush();
			}

			ffmpegStream.Close();
		};

		public static Action<Process> GenerateXMA2Feeder(
			BinaryReader reader,
			uint align, int playLength, uint duration, uint channels, uint rate,
			uint[] seekData
		) => (Process ffmpeg) =>
		{
			Stream ffmpegStream = ffmpeg.StandardInput.BaseStream;

			using (BinaryWriter ffmpegWriter = new BinaryWriter(ffmpegStream, Encoding.ASCII))
			{
				typeof(BinaryWriter).GetField("_leaveOpen", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ffmpegWriter, true);
				ffmpegWriter.Write("RIFF".ToCharArray());
				ffmpegWriter.Write(playLength + 4 + 4 + 8 /**/ + 4 + 2 + 2 + 4 + 4 + 2 + 2 + 2 /**/ + 2 + 4 + 6 * 4 +
				                   1 + 1 + 2 /**/ + 4 + 4 + seekData.Length * 4 /**/ + 4 + 4 - 8);
				ffmpegWriter.Write("WAVEfmt ".ToCharArray());

				ffmpegWriter.Write(18 + 34);
				ffmpegWriter.Write((short) 0x0166);
				ffmpegWriter.Write((short) channels);
				ffmpegWriter.Write(rate);
				ffmpegWriter.Write(
					align >= XMAInfo.BytesPerSecond.Length
						? XMAInfo.BytesPerSecond[align >> 5]
						: XMAInfo.BytesPerSecond[align]
				);
				ffmpegWriter.Write(
					align >= XMAInfo.BlockAlign.Length ? XMAInfo.BlockAlign[align & 0x0F] : XMAInfo.BlockAlign[align]
				);
				ffmpegWriter.Write((short) 0x0F);
				ffmpegWriter.Write((short) 34); // size of header extra

				ffmpegWriter.Write((short) 1); // number of streams
				ffmpegWriter.Write(channels == 2 ? 3U : 0U); // channel mask
				// The following values are definitely incorrect, but they should work until they don't.
				ffmpegWriter.Write(0U); // samples encoded
				ffmpegWriter.Write(0U); // bytes per block
				ffmpegWriter.Write(0U); // start
				ffmpegWriter.Write(0U); // length
				ffmpegWriter.Write(0U); // loop start
				ffmpegWriter.Write(0U); // loop length
				ffmpegWriter.Write((byte) 0); // loop count
				ffmpegWriter.Write((byte) 0x04); // version
				ffmpegWriter.Write((short) 1); // block count

				ffmpegWriter.Write("seek".ToCharArray());
				ffmpegWriter.Write(seekData.Length * 4);
				for (int si = 0; si < seekData.Length; si++)
				{
					ffmpegWriter.Write(seekData[si]);
				}

				ffmpegWriter.Write("data".ToCharArray());
				ffmpegWriter.Write(playLength);
				ffmpegWriter.Flush();
			}

			byte[] dataRaw = new byte[4096];
			int sizeRaw;
			long destination = reader.BaseStream.Position + playLength;
			while (!ffmpeg.HasExited && reader.BaseStream.Position < destination)
			{
				sizeRaw = reader.BaseStream.Read(dataRaw, 0,
					Math.Min(dataRaw.Length, (int) (destination - reader.BaseStream.Position)));
				ffmpegStream.Write(dataRaw, 0, sizeRaw);
				ffmpegStream.Flush();
			}

			ffmpegStream.Close();
		};

		public static ushort SwapEndian(bool swap, ushort data)
		{
			if (!swap)
				return data;
			return (ushort)(
				((ushort)((data & 0xFF) << 8)) |
				((ushort)((data >> 8) & 0xFF))
			);
		}

		public static uint SwapEndian(bool swap, uint data)
		{
			if (!swap)
				return data;
			return
				((data & 0xFF) << 24) |
				(((data >> 8) & 0xFF) << 16) |
				(((data >> 16) & 0xFF) << 8) |
				((data >> 24) & 0xFF);
		}

		public static ulong SwapEndian(bool swap, ulong data)
		{
			if (!swap)
				return data;
			return
				((data & 0xFF) << 56) |
				(((data >> 8) & 0xFF) << 48) |
				(((data >> 16) & 0xFF) << 40) |
				(((data >> 24) & 0xFF) << 32) |
				(((data >> 32) & 0xFF) << 24) |
				(((data >> 40) & 0xFF) << 16) |
				(((data >> 48) & 0xFF) << 8) |
				((data >> 56) & 0xFF);
		}

		public static void RunFFMPEG(string args, Stream input, Stream output, Action<Process> feeder = null, long inputLength = 0)
		{
			string _path = Environment.CurrentDirectory + "\\Content\\";
			Process ffmpeg = new Process();
			ffmpeg.StartInfo = new ProcessStartInfo
			{
				FileName = Environment.CurrentDirectory + "\\Content\\FFmpeg.exe",
				Arguments = args,
				WorkingDirectory = _path,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardInput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			ffmpeg.Start();

			ffmpeg.AsyncPipeErr();

			Thread inputPipeThread = input == null ? null : new Thread(
				// Reading from feeder
				feeder != null ? (() => feeder(ffmpeg)) :

				// Reading until the end of input
				inputLength == 0 ? delegate () {
					input.CopyTo(ffmpeg.StandardInput.BaseStream);
					ffmpeg.StandardInput.BaseStream.Flush();
					ffmpeg.StandardInput.BaseStream.Close();
				}
			:

				// Reading a section from input only
				(ThreadStart)delegate () {
					byte[] dataRaw = new byte[4096];
					int sizeRaw;
					Stream ffmpegInStream = ffmpeg.StandardInput.BaseStream;
					long offset = 0;
					while (!ffmpeg.HasExited && offset < inputLength)
					{
						offset += sizeRaw = input.Read(dataRaw, 0, Math.Min(dataRaw.Length, (int)(inputLength - offset)));
						ffmpegInStream.Write(dataRaw, 0, sizeRaw);
						ffmpegInStream.Flush();
					}
					ffmpegInStream.Close();
				}
			)
			{
				IsBackground = true
			};
			inputPipeThread?.Start();

			// Probably writing to file instead.
			if (output == null)
			{
				ffmpeg.AsyncPipeOut();
				ffmpeg.WaitForExit();
				return;
			}

			Stream ffmpegStream = ffmpeg.StandardOutput.BaseStream;

			byte[] data = new byte[1024];
			int size;
			/*
            while (!ffmpeg.HasExited) {
                size = ffmpegStream.Read(data, 0, data.Length);
                output.Write(data, 0, size);
            }
            */
			while ((size = ffmpegStream.Read(data, 0, data.Length)) > 0)
			{
				output.Write(data, 0, size);
			}
		}
	}

	public static class XnaToFNAExtLite
	{
		public static byte[] ReadBytesUntil(this BinaryReader reader, long position)
			=> reader.ReadBytes((int)(position - reader.BaseStream.Position));

		public static Thread AsyncPipeOut(this Process p, bool nullify = false)
		{
			Thread t = nullify ?

				new Thread(() => {
					try { StreamReader @out = p.StandardOutput; while (!p.HasExited) @out.ReadLine(); } catch { }
				})
				{
					Name = $"STDOUT pipe thread for {p.ProcessName}",
					IsBackground = true
				} :

				new Thread(() => {
					try { StreamReader @out = p.StandardOutput; while (!p.HasExited) Console.WriteLine(@out.ReadLine()); } catch { }
				})
				{
					Name = $"STDOUT pipe thread for {p.ProcessName}",
					IsBackground = true
				};
			t.Start();
			return t;
		}

		public static Thread AsyncPipeErr(this Process p, bool nullify = false)
		{
			Thread t = nullify ?

				new Thread(() => {
					try { StreamReader err = p.StandardError; while (!p.HasExited) err.ReadLine(); } catch { }
				})
				{
					Name = $"STDERR pipe thread for {p.ProcessName}",
					IsBackground = true
				} :

				new Thread(() => {
					try { StreamReader err = p.StandardError; while (!p.HasExited) Console.WriteLine(err.ReadLine()); } catch { }
				})
				{
					Name = $"STDERR pipe thread for {p.ProcessName}",
					IsBackground = true
				};
			t.Start();
			return t;
		}
	}
}
