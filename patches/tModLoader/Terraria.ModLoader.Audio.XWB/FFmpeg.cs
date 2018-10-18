/*******************************************************************************
 * Copyright (C) 2014-2015 Anton Gustafsson
 *
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 ******************************************************************************/

using System;
using System.Diagnostics;
using System.IO;

namespace Terraria.ModLoader.Audio.XWB
{
	internal static class FFmpeg
	{
		private static readonly int windowsLastIndex = 5;
		private static readonly bool isWindows = Environment.OSVersion.Platform <= (PlatformID) windowsLastIndex;
		internal static string cmd="FFmpeg.exe";

		private static string ffmpegExecutable=null;
		private static string file_xWMA=null;
		private static string file_WAV=null;


		internal static void SetupFfmpegEXE()
		{
			string _path = Environment.CurrentDirectory + "\\Content\\";
			//Directory.CreateDirectory(_path);
			// Non-windows users will have to install ffmpeg 
			if (isWindows)
			{
				try
				{
					using(Stream ffmpegExecutableResource=typeof(FFmpeg).Assembly.GetManifestResourceStream(typeof(FFmpeg),"FFmpeg.exe"))
					{
						try
						{
							// Try to create a temporary file for the executable
							ffmpegExecutable = Path.Combine(_path, "FFmpeg.exe");
							using(Stream stream=File.OpenWrite(ffmpegExecutable))
							{
								ffmpegExecutableResource.CopyTo(stream);
							}
						}
						catch(UnauthorizedAccessException)
						{
							// Dump the executable in the local directory instead
							ffmpegExecutable = Path.Combine(_path,"FFmpeg.exe");
							using(Stream stream=File.OpenWrite(ffmpegExecutable))
							{
								ffmpegExecutableResource.CopyTo(stream);
							}
						}
					}
					cmd = ffmpegExecutable;
				}
				catch(Exception ex)
				{
					Console.Error.WriteLine("Failed to copy FFmpeg executable!", ex);
					// We can still try, the user might have ffmpeg installed
				}
			}
			file_xWMA=Path.Combine(_path, "temp_input.xwma");
			file_WAV=Path.Combine(_path, "temp_output.wav");
		}

		internal static void DeleteFFmpegExe()
		{
			if(ffmpegExecutable!=null){File.Delete(ffmpegExecutable);}
			if(file_xWMA!=null&&File.Exists(file_xWMA)){File.Delete(file_xWMA);}
			if(file_WAV!=null&&File.Exists(file_WAV)){File.Delete(file_WAV);}
		}

		//Converts a xWMA file stream to a wav/PCM file stream
		public static byte[] Convert(byte[] data_xWMA)
		{
			File.WriteAllBytes(file_xWMA,data_xWMA);
			ConvertInner(file_xWMA,file_WAV);
			//Process.Start(Path.GetTempPath());
			byte[] data_WAV=File.ReadAllBytes(file_WAV);
			File.Delete(file_WAV);
			return data_WAV;
		}

		private static void ConvertInner(string inputFile, string outputFile)
		{
			/*
			 * Note: From version 1.4, TExtract uses a special version of the
			 * ffmpeg executable configured with the following options:
			 * --disable-everything --enable-muxer=wav --enable-encoder=pcm_s16le
			 * --enable-demuxer=xwma --enable-decoder=wmav2
			 * --enable-protocol=file --enable-filter=aresample
			 * It can therefore not resample to another format without
			 * recompilation with appropriate options. The reason behind this
			 * is that the original weighted about 27 megabytes, whereas the new
			 * one weights only 1,5 megabytes.
			 */

			try
			{
				using (Process process = new Process())
				{
					process.StartInfo.Arguments = @"ffmpeg.exe";
					process.StartInfo.FileName = NormalizePath(cmd);
					process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
					process.StartInfo.ErrorDialog = true;
					process.StartInfo.WorkingDirectory = Environment.CurrentDirectory + "\\Content\\";
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.RedirectStandardInput = true;
					process.StartInfo.RedirectStandardError = true;
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.CreateNoWindow = true;
					process.Start();
					process.EnableRaisingEvents = true;
					string o = process.StandardError.ReadToEnd();
					if (!process.WaitForExit(1000))
					{
						Console.Error.WriteLine("Ffmpeg exited with abnormal exit code: " + process.ExitCode);
					}
					Console.Error.WriteLine(o);
					Console.Error.WriteLine(process.ExitCode);
				}

				//Process.Start(command).WaitForExit(1000);//builder.start();
				//process.WaitForExit();
				
				
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("An error occured when executing FFmpeg", ex);
				Console.Error.WriteLine(ex.Message, ex);
			}
		}

		private static string NormalizePath(string path)
		{
			return Path.GetFullPath(new Uri(path).LocalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
		}
	}
}