/*
 * RIFF common functions and data
 * copyright (c) 2000 Fabrice Bellard
 *
 * This file is part of FFmpeg.
 *
 * FFmpeg is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 *
 * FFmpeg is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with FFmpeg; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA
 */

/**
 * @file
 * internal header for RIFF based (de)muxers
 * do NOT include this in end user applications
 */

//#ifndef AVFORMAT_RIFF_H
//#define AVFORMAT_RIFF_H

using static FFmpeg.libavcodec.avcodec;
//#include "avio.h"
//#include "internal.h"
//#include "metadata.h"

using uint8_t = System.Byte;
using int32_t = System.Int32;
using uint32_t = System.UInt32;
using int64_t = System.Int64;
using uint64_t = System.UInt64;

namespace FFmpeg.libavformat
{
	public unsafe static partial class riff
	{
		extern const AVMetadataConv[] ff_riff_info_conv;

		public static partial int64_t ff_start_tag(AVIOContext* pb,/*const*/char* tag);
		public static partial void ff_end_tag(AVIOContext *pb, int64_t start);

		/**
		 * Read BITMAPINFOHEADER structure and set AVStream codec width, height and
		 * bits_per_encoded_sample fields. Does not read extradata.
		 * Writes the size of the BMP file to *size.
		 * @return codec tag
		 */
		public static partial int ff_get_bmp_header(AVIOContext *pb, AVStream *st, uint32_t *size);

		public static partial void ff_put_bmp_header(AVIOContext *pb, AVCodecParameters *par, int for_asf, int ignore_extradata);

		/**
		 * Tell ff_put_wav_header() to use WAVEFORMATEX even for PCM codecs.
		 */
		public const int FF_PUT_WAV_HEADER_FORCE_WAVEFORMATEX=0x00000001;

		/**
		 * Tell ff_put_wav_header() to write an empty channel mask.
		 */
		public const int FF_PUT_WAV_HEADER_SKIP_CHANNELMASK=0x00000002;

		/**
		 * Write WAVEFORMAT header structure.
		 *
		 * @param flags a combination of FF_PUT_WAV_HEADER_* constants
		 *
		 * @return the size or -1 on error
		 */
		public static partial int ff_put_wav_header(AVFormatContext *s, AVIOContext *pb, AVCodecParameters *par, int flags);

		public static partial AVCodecID ff_wav_codec_get_id(uint tag, int bps);
		public static partial int ff_get_wav_header(AVFormatContext *s, AVIOContext *pb, AVCodecParameters *par, int size, int big_endian);

		public static readonly AVCodecTag[] ff_codec_bmp_tags; // exposed through avformat_get_riff_video_tags()
		public static readonly AVCodecTag[] ff_codec_wav_tags;

		public static partial void ff_parse_specific_params(AVStream *st, int *au_rate, int *au_ssize, int *au_scale);

		public static partial int ff_read_riff_info(AVFormatContext *s, int64_t size);

		/**
		 * Write all recognized RIFF tags from s->metadata
		 */
		public static partial void ff_riff_write_info(AVFormatContext *s);

		/**
		 * Write a single RIFF info tag
		 */
		public static partial void ff_riff_write_info_tag(AVIOContext *pb, /*const*/ char *tag, /*const*/ char *str);

		public struct ff_asf_guid
		{
			public fixed uint8_t values[16];
		}

		public struct AVCodecGuid
		{
			public AVCodecID id;
			public ff_asf_guid guid;
		}

		public static readonly AVCodecGuid[] ff_codec_wav_guids;

		public const string FF_PRI_GUID=
			"%02x%02x%02x%02x%02x%02x%02x%02x%02x%02x%02x%02x%02x%02x%02x%02x "+
			"{%02x%02x%02x%02x-%02x%02x-%02x%02x-%02x%02x-%02x%02x%02x%02x%02x%02x}";

		public static byte[] FF_ARG_GUID(byte* g)
		{
			return new byte[]
			{
				g[0], g[1], g[2],  g[3],  g[4],  g[5],  g[6],  g[7],
				g[8], g[9], g[10], g[11], g[12], g[13], g[14], g[15],
				g[3], g[2], g[1],  g[0],  g[5],  g[4],  g[7],  g[6],
				g[8], g[9], g[10], g[11], g[12], g[13], g[14], g[15]
			};
		}

		public static readonly byte[] FF_MEDIASUBTYPE_BASE_GUID=new byte[]{0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71};
		public static readonly byte[] FF_AMBISONIC_BASE_GUID=new byte[]{0x21, 0x07, 0xD3, 0x11, 0x86, 0x44, 0xC8, 0xC1, 0xCA, 0x00, 0x00, 0x00};
		public static readonly byte[] FF_BROKEN_BASE_GUID=new byte[]{0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA};

		public static /*av_always_inline*/ int ff_guidcmp(/*const*/void *g1,/*const*/ void *g2)
		{
			byte* b1=(byte*)g1;
			byte* b2=(byte*)g2;
			for(int i=0;i<sizeof(ff_asf_guid);i++)
			{
				if(b1[i]!=b2[i])
				{
					return 0;
				}
			}
			return 1;
		}

		public static partial int ff_get_guid(AVIOContext *s, ff_asf_guid *g);
		public static partial void ff_put_guid(AVIOContext *s, /*const*/ ff_asf_guid *g);
		public static partial /*const */ff_asf_guid* ff_get_codec_guid(AVCodecID id, /*const*/AVCodecGuid *av_guid);

		public static partial AVCodecID ff_codec_guid_get_id(/*const*/AVCodecGuid *guids, ff_asf_guid guid);

		//#endif /* AVFORMAT_RIFF_H */
	}
}