//https://www.ffmpeg.org/doxygen/2.7/xwma_8c_source.html

/*
 * xWMA demuxer
 * Copyright 2011(c)Max Horn
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
using uint8_t = System.Byte;
using int32_t = System.Int32;
using uint32_t = System.UInt32;
using int64_t = System.Int64;
using uint64_t = System.UInt64;
using System;
using System.Collections.Generic;
using System.Text;
//#include <inttypes.h>
//#include <stdint.h>

//#include "avformat.h"
//#include "internal.h"
//#include "riff.h"
using static FFmpeg.libavcodec.avcodec;

namespace FFmpeg
{
	/*
	 * Demuxer for xWMA, a Microsoft audio container used by XAudio 2.
	 */
	unsafe static class xWMA
	{
		private const int AVPROBE_SCORE_MAX = 100;

		public struct XWMAContext
		{
			public int64_t data_end;
		}

		public class AVProbeData//avformat.h//was struct
		{
			public /*const*/ string filename;//char*
			public byte[] buf; /**< Buffer must have AVPROBE_PADDING_SIZE of extra allocated bytes filled with zero. */
			public int buf_size;       /**< Size of buf except extra allocated bytes */
			public /*const*/ string mime_type; /**< mime_type, when known. *///char*
		}

		static int xwma_probe(AVProbeData p)
		{
			if(Encoding.UTF8.GetString(p.buf,0,4)=="RIFF" && Encoding.UTF8.GetString(p.buf,8,4)=="XWMA")
			{
				return AVPROBE_SCORE_MAX;
			}
			return 0;
		}

		private static int MKTAG(char a,char b,char c,char d) => (((byte)a) | (((byte)b) << 8) | (((byte)c) << 16) | (((byte)d) << 24));

		static int xwma_read_header(AVFormatContext s)
		{
			int64_t size;
			int ret = 0;
			uint32_t dpds_table_size = 0;
			uint32_t[] dpds_table = null;
			uint tag;
			AVIOContext pb = s.pb;
			AVStream st;
			XWMAContext* xwma = (XWMAContext*)s.priv_data;
			int i;

			/* The following code is mostly copied from wav.c, with some
			 * minor alterations.
			 */

			/* check RIFF header */
			tag = avio_rl32(pb);
			if(tag != MKTAG('R','I','F','F'))
				return -1;
			avio_rl32(pb); /* file size */
			tag = avio_rl32(pb);
			if(tag != MKTAG('X','W','M','A'))
			{
				return -1;
			}
			/* parse fmt header */
			tag = avio_rl32(pb);
			if(tag != MKTAG('f','m','t',' '))
			{
				return -1;
			}
			size = avio_rl32(pb);
			st = avformat_new_stream(s,null);
			if(st==null)
				throw new OutOfMemoryException();//return AVERROR(ENOMEM);

			ret = ff_get_wav_header(pb,st.codec,size,0);
			if(ret < 0)
			{
				return ret;
			}
			st.need_parsing = AVSTREAM_PARSE_NONE;

			/* All xWMA files I have seen contained WMAv2 data. If there are files
			 * using WMA Pro or some other codec, then we need to figure out the right
			 * extradata for that. Thus, ask the user for feedback, but try to go on
			 * anyway.
			 */
			if(st.codec->codec_id != AV_CODEC_ID_WMAV2)
			{
				avpriv_request_sample(s,"Unexpected codec (tag 0x04%x; id %d)",st.codec->codec_tag,st.codec->codec_id);
			}
			else
			{
				/* In all xWMA files I have seen, there is no extradata. But the WMA
				 * codecs require extradata, so we provide our own fake extradata.
				 *
				 * First, check that there really was no extradata in the header. If
				 * there was, then try to use it, after asking the user to provide a
				 * sample of this unusual file.
				 */
				if(st.codec->extradata_size != 0)
				{
					/* Surprise, surprise: We *did* get some extradata. No idea
					 * if it will work, but just go on and try it, after asking
					 * the user for a sample.
					 */
					avpriv_request_sample(s,"Unexpected extradata (%d bytes)",st.codec->extradata_size);
				}
				else
				{
					if(ff_alloc_extradata(st.codec,6))
					{
						throw new OutOfMemoryException();//return AVERROR(ENOMEM);
					}
					for(int codecIndex=0;codecIndex<st.codec->extradata_size;codecIndex++)//memset(st.codec->extradata,0,st.codec->extradata_size);
					{
						st.codec->extradata[codecIndex]=0;
					}
					
					/* setup extradata with our experimentally obtained value */
					st.codec->extradata[4] = 31;
				}
			}

			if(!st.codec->channels)
			{
				Console.Write("Invalid channel count: %d\n",st.codec->channels);//av_log(s, AV_LOG_WARNING, "Invalid channel count: %d\n",st.codec->channels);
				throw new System.IO.InvalidDataException();//return AVERROR_INVALIDDATA;
			}
			if(!st.codec->bits_per_coded_sample)
			{
				Console.Write("Invalid bits_per_coded_sample: %d\n",st.codec->bits_per_coded_sample);//av_log(s, AV_LOG_WARNING, "Invalid bits_per_coded_sample: %d\n",st.codec->bits_per_coded_sample);
				throw new System.IO.InvalidDataException();//return AVERROR_INVALIDDATA;
			}

			/* set the sample rate */
			avpriv_set_pts_info(st,64,1,st.codec->sample_rate);

			/* parse the remaining RIFF chunks */
			for(;;)
			{
				if(pb.eof_reached)
				{
					ret = AVERROR_EOF;
					goto fail;
				}
				/* read next chunk tag */
				tag = avio_rl32(pb);
				size = avio_rl32(pb);
				if(tag == MKTAG('d','a','t','a'))
				{
					/* We assume that the data chunk comes last. */
					break;
				}
				else if(tag == MKTAG('d','p','d','s'))
				{
					/* Quoting the MSDN xWMA docs on the dpds chunk: "Contains the
					 * decoded packet cumulative data size array, each element is the
					 * number of bytes accumulated after the corresponding xWMA packet
					 * is decoded in order."
					 *
					 * Each packet has size equal to st.codec->block_align, which in
					 * all cases I saw so far was always 2230. Thus, we can use the
					 * dpds data to compute a seeking index.
					 */

					/* Error out if there is more than one dpds chunk. */
					if(dpds_table!=null)
					{
						Console.Write("two dpds chunks present\n");//av_log(s,AV_LOG_ERROR,"two dpds chunks present\n");
						ret = AVERROR_INVALIDDATA;
						goto fail;
					}

					/* Compute the number of entries in the dpds chunk. */
					if((size & 3)!=0)
					{
						/* Size should be divisible by four */
						Console.Write("dpds chunk size %{0} not divisible by 4\n",size);//av_log(s,AV_LOG_WARNING,"dpds chunk size %"PRId64" not divisible by 4\n",size);
					}
					dpds_table_size = (uint)(size / 4);
					if(dpds_table_size == 0 || dpds_table_size >= int.MaxValue / 4)//INT_MAX
					{
						Console.Write("dpds chunk size %{0} invalid\n",size);//av_log(s,AV_LOG_ERROR,"dpds chunk size %"PRId64" invalid\n",size);
						throw new System.IO.InvalidDataException();//return AVERROR_INVALIDDATA;
					}

					/* Allocate some temporary storage to keep the dpds data around.
					 * for processing later on.
					 */
					dpds_table = new uint32_t[dpds_table_size];
					if(dpds_table==null)
					{
						throw new OutOfMemoryException();//return AVERROR(ENOMEM);
					}

					for(i = 0;i < dpds_table_size;++i)
					{
						dpds_table[i] = avio_rl32(pb);
						size -= 4;
					}
				}
				avio_skip(pb,size);
			}

			/* Determine overall data length */
			if(size < 0)
			{
				ret = AVERROR_INVALIDDATA;
				goto fail;
			}
			if(size==0)
			{
				xwma->data_end = long.MaxValue;//INT64_MAX
			}
			else
			{
				xwma->data_end = avio_tell(pb) + size;
			}

			if(dpds_table!=null && dpds_table_size!=0)
			{
				int64_t cur_pos;
				/*const*/ uint32_t bytes_per_sample = (st.codec->channels * st.codec->bits_per_coded_sample) >> 3;

				/* Estimate the duration from the total number of output bytes. */
				/*const*/ uint64_t total_decoded_bytes = dpds_table[dpds_table_size - 1];

				if(bytes_per_sample==0)
				{
					Console.Write("Invalid bits_per_coded_sample %d for %d channels\n",st.codec->bits_per_coded_sample,st.codec->channels);//av_log(s,AV_LOG_ERROR,"Invalid bits_per_coded_sample %d for %d channels\n",st.codec->bits_per_coded_sample,st.codec->channels);
					ret = AVERROR_INVALIDDATA;
					goto fail;
				}

				st.duration = (long)(total_decoded_bytes / bytes_per_sample);

				/* Use the dpds data to build a seek table.  We can only do this after
				 * we know the offset to the data chunk, as we need that to determine
				 * the actual offset to each input block.
				 * Note: If we allowed ourselves to assume that the data chunk always
				 * follows immediately after the dpds block, we could of course guess
				 * the data block's start offset already while reading the dpds chunk.
				 * I decided against that, just in case other chunks ever are
				 * discovered.
				 */
				cur_pos = avio_tell(pb);
				for(i = 0;i < dpds_table_size;++i)
				{
					/* From the number of output bytes that would accumulate in the
					 * output buffer after decoding the first (i+1) packets, we compute
					 * an offset / timestamp pair.
					 */
					av_add_index_entry
					(
						st,
						cur_pos + (i+1) * st.codec->block_align, /* pos */
						dpds_table[i] / bytes_per_sample,         /* timestamp */
						st.codec->block_align,                   /* size */
						0,                                        /* duration */
						AVINDEX_KEYFRAME
					);
				}
			}
			else if(st.codec->bit_rate)
			{
				/* No dpds chunk was present (or only an empty one), so estimate
				 * the total duration using the average bits per sample and the
				 * total data length.
				 */
				st.duration = (size<<3) * st.codec->sample_rate / st.codec->bit_rate;
			}

			fail:
			dpds_table=null;//av_free(dpds_table);

			return ret;
		}

		static int xwma_read_packet(AVFormatContext s,AVPacket pkt)
		{
			int ret, size;
			int64_t left;
			AVStream st;
			XWMAContext* xwma = (XWMAContext*)s.priv_data;

			st = s.streams[0];

			left = xwma->data_end - avio_tell(s.pb);
			if(left <= 0)
			{
				throw new System.IO.EndOfStreamException();//return AVERROR_EOF;
			}

			/* read a single block; the default block size is 2230. */
			size = (st.codec->block_align > 1) ? st.codec->block_align : 2230;
			size = (int)Math.Min(size,left);

			ret  = av_get_packet(s.pb,pkt,size);
			if(ret < 0)
				return ret;

			pkt.stream_index = 0;
			return ret;
		}

		private static AVInputFormat ff_xwma_demuxer = new AVInputFormat
		{
			name           = "xwma",
			long_name      = NULL_IF_CONFIG_SMALL("Microsoft xWMA"),
			priv_data_size = sizeof(XWMAContext),
			read_probe     = xwma_probe,
			read_header    = xwma_read_header,
			read_packet    = xwma_read_packet,
		};

		static int av_get_packet(AVIOContext s, AVPacket pkt, int size)
		{
		    av_init_packet(pkt);
		    pkt.data = null;
		    pkt.size = 0;
		    pkt.pos  = avio_tell(s);
			return append_packet_chunked(s, pkt, size);
		}
		private const long AV_NOPTS_VALUE=unchecked(((int64_t)/*UINT64_C*/(0x8000000000000000)));

		static void av_init_packet(AVPacket pkt)
		{
			pkt.pts                  = AV_NOPTS_VALUE;
			pkt.dts                  = AV_NOPTS_VALUE;
			pkt.pos                  = -1;
			pkt.duration             = 0;
			pkt.convergence_duration = 0;
			pkt.flags                = 0;
			pkt.stream_index         = 0;
		#if FF_API_DESTRUCT_PACKET
		FF_DISABLE_DEPRECATION_WARNINGS
			pkt.destruct             = NULL;
		FF_ENABLE_DEPRECATION_WARNINGS
		#endif
			pkt.buf                  = null;
			pkt.side_data            = null;
			pkt.side_data_elems      = 0;
		}

		private const int SANE_CHUNK_SIZE=50000000;

		static int ffio_limit(AVIOContext s, int size)
		{
			if (s.maxsize>= 0) {
				int64_t remaining= s.maxsize - avio_tell(s);
				if (remaining < size) {
					int64_t newsize = avio_size(s);
					if (s.maxsize==0 || s.maxsize<newsize)
					{
						if(newsize!=0)
						{
							s.maxsize = newsize;
						}
						else
						{
							s.maxsize=1;
						}
					}
					remaining= s.maxsize - avio_tell(s);
					remaining= Math.Max(remaining, 0);
				}

				if (s.maxsize>= 0 && remaining+1 < size)
				{
					Console.Write("Truncating packet of size %{0} to %{1}\n", size, remaining+1);//av_log(NULL, remaining ? AV_LOG_ERROR : AV_LOG_DEBUG, "Truncating packet of size %d to %"PRId64"\n", size, remaining+1);
					size = (int)(remaining+1);
				}
			}
			return size;
		}

		static int64_t avio_size(AVIOContext s)
		{
			int64_t size;

			if (s==null)
			{
				throw new ArgumentNullException();//return AVERROR(EINVAL);
			}
			if (s.seek==null)
			{
				throw new NotSupportedException();//return AVERROR(ENOSYS);
			}
			size = s.seek(s.opaque, 0, AVSEEK_SIZE);
			if (size < 0)
			{
				if ((size = s.seek(s.opaque, -1, SEEK_END)) < 0)
				{
					return size;
				}
				size++;
				s.seek(s.opaque, s.pos, SEEK_SET);
			}
			return size;
		}


		/* Read the data in sane-sized chunks and append to pkt.
		 * Return the number of bytes read or an error. */
		static int append_packet_chunked(AVIOContext s, AVPacket pkt, int size)
		{
			int64_t orig_pos   = pkt.pos; // av_grow_packet might reset pos
			int orig_size      = pkt.size;
			int ret;

			do
			{
				int prev_size = pkt.size;
				int read_size;

				/* When the caller requests a lot of data, limit it to the amount
				 * left in file or SANE_CHUNK_SIZE when it is not known. */
				read_size = size;
				if (read_size > SANE_CHUNK_SIZE/10)
				{
					read_size = ffio_limit(s, read_size);
					// If filesize/maxsize is unknown, limit to SANE_CHUNK_SIZE
					if (s.maxsize < 0)
					{
						read_size = Math.Min(read_size, SANE_CHUNK_SIZE);
					}
				}

				ret = av_grow_packet(pkt, read_size);
				if (ret < 0)
				{
					break;
				}

				ret = avio_read(s, pkt.data + prev_size, read_size);
				if (ret != read_size)
				{
					av_shrink_packet(pkt, prev_size + Math.Max(ret, 0));
					break;
				}

				size -= read_size;
			} while (size > 0);
			if (size > 0)
				pkt.flags |= AV_PKT_FLAG_CORRUPT;

			pkt.pos = orig_pos;
			if (pkt.size==0)
			{
				av_free_packet(pkt);
			}
			return pkt.size > orig_size ? pkt.size - orig_size : ret;
		}

		private const int AV_PKT_FLAG_KEY=0x0001;///< The packet contains a keyframe
		private const int AV_PKT_FLAG_CORRUPT=0x0002;///< The packet content is corrupted
		
		private const int FF_INPUT_BUFFER_PADDING_SIZE=32;

		public static int av_grow_packet(AVPacket pkt, int grow_by)
		{
			int new_size;
			av_assert0((uint)pkt.size <= int.MaxValue - FF_INPUT_BUFFER_PADDING_SIZE);
			if (pkt.size==0)
			{
				return av_new_packet(pkt, grow_by);
			}
			if ((uint)grow_by>int.MaxValue - (pkt.size + FF_INPUT_BUFFER_PADDING_SIZE))
			{
				return -1;
			}

			new_size = pkt.size + grow_by + FF_INPUT_BUFFER_PADDING_SIZE;
			if (pkt.buf!=null)
			{
				int ret = av_buffer_realloc(&pkt.buf, new_size);
				if (ret < 0)
				{
					return ret;
				}
			}
			else
			{
				pkt.buf = av_buffer_alloc(new_size);
				if (pkt.buf==null)
				{
					throw new OutOfMemoryException();//return AVERROR(ENOMEM);
				}
				memcpy(pkt.buf->data, pkt.data, Math.Min(pkt.size, pkt.size + grow_by));
		#if FF_API_DESTRUCT_PACKET
		FF_DISABLE_DEPRECATION_WARNINGS
				pkt.destruct = dummy_destruct_packet;
		FF_ENABLE_DEPRECATION_WARNINGS
		#endif
			}
			pkt.data  = pkt.buf->data;
			pkt.size += grow_by;
			memset(pkt.data + pkt.size, 0, FF_INPUT_BUFFER_PADDING_SIZE);

			return 0;
		}
		
		public static void fill_buffer(AVIOContext s)
		{
			int max_buffer_size = s.max_packet_size!=0 ? s.max_packet_size : IO_BUFFER_SIZE;
			uint8_t* dst = s.buf_end - s.buffer + max_buffer_size < s.buffer_size ? s.buf_end : s.buffer;
			int len = s.buffer_size - (int32_t)(dst - s.buffer);

			/* can't fill the buffer without read_packet, just set EOF if appropriate */
			if(s.read_packet==null && s.buf_ptr >= s.buf_end)
			{
				s.eof_reached = 1;
			}
			/* no need to do anything if EOF already reached */
			if(s.eof_reached!=0)
			{
				return;
			}
			if(s.update_checksum!=null && dst == s.buffer)
			{
				if(s.buf_end > s.checksum_ptr)
				{
					s.checksum = s.update_checksum(s.checksum,s.checksum_ptr,(uint)(s.buf_end - s.checksum_ptr));
				}
				s.checksum_ptr = s.buffer;
			}

			/* make buffer smaller in case it ended up large after probing */
			if(s.read_packet!=null && s.orig_buffer_size!=0 && s.buffer_size > s.orig_buffer_size)
			{
				if(dst == s.buffer)
				{
					int ret = ffio_set_buf_size(s,s.orig_buffer_size);
					if(ret < 0)
					{
						Console.Write("Failed to decrease buffer size\n");//av_log(s, AV_LOG_WARNING, "Failed to decrease buffer size\n");
					}
					s.checksum_ptr = dst = s.buffer;
				}
				av_assert0(len >= s.orig_buffer_size);
				len = s.orig_buffer_size;
			}

			if(s.read_packet!=null)
			{
				len = s.read_packet((IntPtr)s.opaque,dst,len);
			}
			else
			{
				len = 0;
			}
			if(len <= 0)
			{
				/* do not modify buffer if EOF reached so that a seek back can
				   be done without rereading data */
				s.eof_reached = 1;
				if(len < 0)
				{
					s.error = len;
				}
			}
			else
			{
				s.pos += len;
				s.buf_ptr = dst;
				s.buf_end = dst + len;
				s.bytes_read += len;
			}
		}

		public static int avio_read(AVIOContext s, byte* buf, int size)
		{
			int len, size1;

			size1 = size;
			while (size > 0)
			{
				len = (int)(s.buf_end - s.buf_ptr);
				if (len > size)
					len = size;
				if (len == 0 || s.write_flag!=0)
				{
					if((s.direct!=0 || size > s.buffer_size) && s.update_checksum==null)
					{
						if(s.read_packet!=null)
						{
							len = s.read_packet((IntPtr)s.opaque, buf, size);
						}
						if (len <= 0)
						{
							/* do not modify buffer if EOF reached so that a seek back can
							be done without rereading data */
							s.eof_reached = 1;
							if(len<0)
							{
								s.error= len;
							}
							break;
						}
						else
						{
							s.pos += len;
							s.bytes_read += len;
							size -= len;
							buf += len;
							s.buf_ptr = s.buffer;
							s.buf_end = s.buffer/* + len*/;
						}
					}
					else
					{
						fill_buffer(s);
						len = (int)(s.buf_end - s.buf_ptr);
						if (len == 0)
							break;
					}
				}
				else
				{
					memcpy(buf, s.buf_ptr, len);
					buf += len;
					s.buf_ptr += len;
					size -= len;
				}
			}
			if (size1 == size)
			{
				if (s.error!=0)		return s.error;
				if (avio_feof(s))	return AVERROR_EOF;
			}
			return size1 - size;
		}

		public static byte avio_r8(AVIOContext s)
		{
			if(s.buf_ptr >= s.buf_end)
			{
				fill_buffer(s);
			}
			if(s.buf_ptr < s.buf_end)
			{
				return *s.buf_ptr++;
			}
			return 0;
		}

		public static ushort avio_rl16(AVIOContext s)
		{
			ushort val;
			val = avio_r8(s);
			val |= (ushort)(avio_r8(s) << 8);
			return val;
		}

		public static uint avio_rl32(AVIOContext s)
		{
			uint val;
			val = avio_rl16(s);
			val |= (uint)avio_rl16(s) << 16;
			return val;
		}

		public static void avio_skip(AVIOContext s,long skip)//Unoriginal
		{
			for(;skip>0;skip--){avio_r8(s);}
		}

		public static long avio_tell(AVIOContext s)
		{
			return *(long*)s.buf_ptr;
		}

		
		public class AVStream
		{
			public int index;    /**< stream index in AVFormatContext */
			/**
			 * Format-specific stream ID.
			 * decoding: set by libavformat
			 * encoding: set by the user, replaced by libavformat if left unset
			 */
			public int id;
			/**
			 * Codec context associated with this stream. Allocated and freed by
			 * libavformat.
			 *
			 * - decoding: The demuxer exports codec information stored in the headers
			 *             here.
			 * - encoding: The user sets codec information, the muxer writes it to the
			 *             output. Mandatory fields as specified in AVCodecContext
			 *             documentation must be set even if this AVCodecContext is
			 *             not actually used for encoding.
			 */
			public AVCodecContext* codec;
			public void* priv_data;

#if FF_API_LAVF_FRAC
			/**
			 * @deprecated this field is unused
			 */
			attribute_deprecated
			struct AVFrac pts;
#endif

			/**
			 * This is the fundamental unit of time (in seconds) in terms
			 * of which frame timestamps are represented.
			 *
			 * decoding: set by libavformat
			 * encoding: May be set by the caller before avformat_write_header() to
			 *           provide a hint to the muxer about the desired timebase. In
			 *           avformat_write_header(), the muxer will overwrite this field
			 *           with the timebase that will actually be used for the timestamps
			 *           written into the file (which may or may not be related to the
			 *           user-provided one, depending on the format).
			 */
			public AVRational time_base;

			/**
			 * Decoding: pts of the first frame of the stream in presentation order, in stream time base.
			 * Only set this if you are absolutely 100% sure that the value you set
			 * it to really is the pts of the first frame.
			 * This may be undefined (AV_NOPTS_VALUE).
			 * @note The ASF header does NOT contain a correct start_time the ASF
			 * demuxer must NOT set this.
			 */
			public int64_t start_time;

			/**
			 * Decoding: duration of the stream, in stream time base.
			 * If a source file does not specify a duration, but does specify
			 * a bitrate, this value will be estimated from bitrate and file size.
			 */
			public int64_t duration;

			public int64_t nb_frames;                 ///< number of frames in this stream if known or 0

			public int disposition; /**< AV_DISPOSITION_* bit field */

			public AVDiscard discard; ///< Selects which packets can be discarded at will and do not need to be demuxed.

			/**
			 * sample aspect ratio (0 if unknown)
			 * - encoding: Set by user.
			 * - decoding: Set by libavformat.
			 */
			public AVRational sample_aspect_ratio;

			public AVDictionary* metadata;

			/**
			 * Average framerate
			 *
			 * - demuxing: May be set by libavformat when creating the stream or in
			 *             avformat_find_stream_info().
			 * - muxing: May be set by the caller before avformat_write_header().
			 */
			public AVRational avg_frame_rate;

			/**
			 * For streams with AV_DISPOSITION_ATTACHED_PIC disposition, this packet
			 * will contain the attached picture.
			 *
			 * decoding: set by libavformat, must not be modified by the caller.
			 * encoding: unused
			 */
			public AVPacket attached_pic;//Was value type

			/**
			 * An array of side data that applies to the whole stream (i.e. the
			 * container does not allow it to change between packets).
			 *
			 * There may be no overlap between the side data in this array and side data
			 * in the packets. I.e. a given side data is either exported by the muxer
			 * (demuxing) / set by the caller (muxing) in this array, then it never
			 * appears in the packets, or the side data is exported / sent through
			 * the packets (always in the first packet where the value becomes known or
			 * changes), then it does not appear in this array.
			 *
			 * - demuxing: Set by libavformat when the stream is created.
			 * - muxing: May be set by the caller before avformat_write_header().
			 *
			 * Freed by libavformat in avformat_free_context().
			 *
			 * @see av_format_inject_global_side_data()
			 */
			public AVPacketSideData* side_data;
			/**
			 * The number of elements in the AVStream.side_data array.
			 */
			public int nb_side_data;

			/**
			 * Flags for the user to detect events happening on the stream. Flags must
			 * be cleared by the user once the event has been handled.
			 * A combination of AVSTREAM_EVENT_FLAG_*.
			 */
			public int event_flags;
			public const int AVSTREAM_EVENT_FLAG_METADATA_UPDATED = 0x0001;///< The call resulted in updated metadata.

			/*****************************************************************
			 * All fields below this line are not part of the public API. They
			 * may not be used outside of libavformat and can be changed and
			 * removed at will.
			 * New public fields should be added right above.
			 *****************************************************************
			 */

			/**
			 * Stream information used internally by av_find_stream_info()
			 */
			public const int MAX_STD_TIMEBASES = (30*12+7+6);
			public class _info
			{
				int64_t last_dts;
				int64_t duration_gcd;
				int duration_count;
				int64_t rfps_duration_sum;
				double[,] duration_error = new double[2,MAX_STD_TIMEBASES];
				int64_t codec_info_duration;
				int64_t codec_info_duration_fields;

				/**
				 * 0  -> decoder has not been searched for yet.
				 * >0 -> decoder found
				 * <0 -> decoder with codec_id == -found_decoder has not been found
				 */
				int found_decoder;

				int64_t last_duration;

				/**
				 * Those are used for average framerate estimation.
				 */
				int64_t fps_first_dts;
				int fps_first_dts_idx;
				int64_t fps_last_dts;
				int fps_last_dts_idx;

			}
			public _info info;

			public int pts_wrap_bits; /**< number of bits in pts (used for wrapping control) */

			// Timestamp generation support:
			/**
			 * Timestamp corresponding to the last dts sync point.
			 *
			 * Initialized when AVCodecParserContext.dts_sync_point >= 0 and
			 * a DTS is received from the underlying container. Otherwise set to
			 * AV_NOPTS_VALUE by default.
			 */
			public int64_t first_dts;
			public int64_t cur_dts;
			public int64_t last_IP_pts;
			public int last_IP_duration;

			/**
			 * Number of packets to buffer for codec probing
			 */
			public const int MAX_PROBE_PACKETS = 2500;
			public int probe_packets;

			/**
			 * Number of frames that have been demuxed during av_find_stream_info()
			 */
			public int codec_info_nb_frames;

			/* av_read_frame() support */
			public AVStreamParseType need_parsing;
			public AVCodecParserContext* parser;

			/**
			 * last packet in packet_buffer for this stream when muxing.
			 */
			public AVPacketList* last_in_packet_buffer;
			public AVProbeData probe_data;
			public const int MAX_REORDER_DELAY = 16;
			public int64_t pts_buffer[MAX_REORDER_DELAY+1];

			public AVIndexEntry* index_entries; /**< Only used if the format does not
											support seeking natively. */
			public int nb_index_entries;
			public uint index_entries_allocated_size;

			/**
			 * Real base framerate of the stream.
			 * This is the lowest framerate with which all timestamps can be
			 * represented accurately (it is the least common multiple of all
			 * framerates in the stream). Note, this value is just a guess!
			 * For example, if the time base is 1/90000 and all frames have either
			 * approximately3600 or1800 timer ticks, then r_frame_rate will be 50/1.
			 *
			 * Code outside avformat should access this field using:
			 * av_stream_get/set_r_frame_rate(stream)
			 */
			public AVRational r_frame_rate;

			/**
			 * Stream Identifier
			 * This is the MPEG-TS stream identifier +1
			 * 0 means unknown
			 */
			public int stream_identifier;

			public int64_t interleaver_chunk_size;
			public int64_t interleaver_chunk_duration;

			/**
			 * stream probing state
			 * -1   -> probing finished
			 *  0   -> no probing requested
			 * rest -> perform probing with request_probe being the minimum score to accept.
			 * NOT PART OF PUBLIC API
			 */
			public int request_probe;
			/**
			 * Indicates that everything up to the next keyframe
			 * should be discarded.
			 */
			public int skip_to_keyframe;

			/**
			 * Number of samples to skip at the start of the frame decoded from the next packet.
			 */
			public int skip_samples;

			/**
			 * If not 0, the number of samples that should be skipped from the start of
			 * the stream (the samples are removed from packets with pts==0, which also
			 * assumes negative timestamps do not happen).
			 * Intended for use with formats such as mp3 with ad-hoc gapless audio
			 * support.
			 */
			public int64_t start_skip_samples;

			/**
			 * If not 0, the first audio sample that should be discarded from the stream.
			 * This is broken by design (needs global sample count), but can't be
			 * avoided for broken by design formats such as mp3 with ad-hoc gapless
			 * audio support.
			 */
			public int64_t first_discard_sample;

			/**
			 * The sample after last sample that is intended to be discarded after
			 * first_discard_sample. Works on frame boundaries only. Used to prevent
			 * early EOF if the gapless info is broken (considered concatenated mp3s).
			 */
			public int64_t last_discard_sample;

			/**
			 * Number of internally decoded frames, used internally in libavformat, do not access
			 * its lifetime differs from info which is why it is not in that structure.
			 */
			public int nb_decoded_frames;

			/**
			 * Timestamp offset added to timestamps before muxing
			 * NOT PART OF PUBLIC API
			 */
			public int64_t mux_ts_offset;

			/**
			 * Internal data to check for wrapping of the time stamp
			 */
			public int64_t pts_wrap_reference;

			/**
			 * Options for behavior, when a wrap is detected.
			 *
			 * Defined by AV_PTS_WRAP_ values.
			 *
			 * If correction is enabled, there are two possibilities:
			 * If the first time stamp is near the wrap point, the wrap offset
			 * will be subtracted, which will create negative time stamps.
			 * Otherwise the offset will be added.
			 */
			public int pts_wrap_behavior;

			/**
			 * Internal data to prevent doing update_initial_durations() twice
			 */
			public int update_initial_durations_done;

			/**
			 * Internal data to generate dts from pts
			 */
			public int64_t pts_reorder_error[MAX_REORDER_DELAY+1];
			public uint8_t pts_reorder_error_count[MAX_REORDER_DELAY+1];

			/**
			 * Internal data to analyze DTS and detect faulty mpeg streams
			 */
			public int64_t last_dts_for_order_check;
			public uint8_t dts_ordered;
			public uint8_t dts_misordered;

			/**
			 * Internal data to inject global side data
			 */
			public int inject_global_side_data;

			/**
			 * String containing paris of key and values describing recommended encoder configuration.
			 * Paris are separated by ','.
			 * Keys are separated from values by '='.
			 */
			public char* recommended_encoder_configuration;

			/**
			 * display aspect ratio (0 if unknown)
			 * - encoding: unused
			 * - decoding: Set by libavformat to calculate sample_aspect_ratio internally
			 */
			public AVRational display_aspect_ratio;
		}
		public enum AVStreamParseType { }
		public enum AVDiscard { }
		public class AVIOContext//struct
		{
			/**
			 * A class for private options.
			 *
			 * If this AVIOContext is created by avio_open2(), av_class is set and
			 * passes the options down to protocols.
			 *
			 * If this AVIOContext is manually allocated, then av_class may be set by
			 * the caller.
			 *
			 * warning -- this field can be NULL, be sure to not pass this AVIOContext
			 * to any av_opt_* functions in that case.
			 */
			public readonly AVClass* av_class;
			public int buffer_size;	/**< Maximum buffer size */
			public byte* buffer;	/**< Start of the buffer. */
			public byte* buf_ptr;	/**< Current position in the buffer */
			public byte* buf_end;	/**< End of the data, may be less than
										 buffer+buffer_size if the read function returned
										 less data than requested, e.g. for streams where
										 no more data has been received yet. */
			public void* opaque;           /**< A private pointer, passed to the read/write/seek/...
										 functions. */
			public Func<IntPtr /*opaque*/,uint8_t* /*buf*/,int /*buf_size*/,int> read_packet;//IntPtr was void*
			public Func<IntPtr /*opaque*/,uint8_t* /*buf*/,int /*buf_size*/,int> write_packet;//IntPtr was void*
			public Func<IntPtr /*opaque*/,int64_t /*offset*/,int /*whence*/,int64_t> seek;
			public int64_t pos;            /**< position in the file of the current buffer */
			public int must_flush;         /**< true if the next seek should flush */
			public int eof_reached;        /**< true if eof reached */
			public int write_flag;         /**< true if open for writing */
			public int max_packet_size;
			public ulong checksum;
			public byte* checksum_ptr;
			public Func<ulong /*checksum*/, /*const*/ uint8_t* /*buf*/,uint /*size*/,ulong> update_checksum;
			public int error;              /**< contains the error code or 0 if no error happened */
			/**
			 * Pause or resume playback for network streaming protocols - e.g. MMS.
			 */
			public Func<IntPtr /*opaque*/,int /*pause*/,int> read_pause;
			/**
			 * Seek to a given timestamp in stream with the specified stream_index.
			 * Needed for some network streaming protocols which don't support seeking
			 * to byte position.
			 */
			public Func<IntPtr /*opaque*/,int /*stream_index*/,int64_t /*timestamp*/,int /*flags*/,int64_t> read_seek;
			/**
			 * A combination of AVIO_SEEKABLE_ flags or 0 when the stream is not seekable.
			 */
			public int seekable;

			/**
			 * max filesize, used to limit allocations
			 * This field is internal to libavformat and access from outside is not allowed.
			 */
			public int64_t maxsize;

			/**
			 * avio_read and avio_write should if possible be satisfied directly
			 * instead of going through a buffer, and avio_seek will always
			 * call the underlying seek function directly.
			 */
			public int direct;

			/**
			 * Bytes read statistic
			 * This field is internal to libavformat and access from outside is not allowed.
			 */
			public int64_t bytes_read;

			/**
			 * seek statistic
			 * This field is internal to libavformat and access from outside is not allowed.
			 */
			public int seek_count;

			/**
			 * writeout statistic
			 * This field is internal to libavformat and access from outside is not allowed.
			 */
			public int writeout_count;

			/**
			 * Original buffer size
			 * used internally after probing and ensure seekback to reset the buffer size
			 * This field is internal to libavformat and access from outside is not allowed.
			 */
			public int orig_buffer_size;
		}

		public struct AVInputFormat
		{
			/**
			 * A comma separated list of short names for the format. New names
			 * may be appended with a minor bump.
			 */
			public /*readonly*/ string name;//char*

			/**
			 * Descriptive name for the format, meant to be more human-readable
			 * than name. You should use the NULL_IF_CONFIG_SMALL() macro
			 * to define it.
			 */
			public /*readonly*/ string long_name;//char*

			/**
			 * Can use flags: AVFMT_NOFILE, AVFMT_NEEDNUMBER, AVFMT_SHOW_IDS,
			 * AVFMT_GENERIC_INDEX, AVFMT_TS_DISCONT, AVFMT_NOBINSEARCH,
			 * AVFMT_NOGENSEARCH, AVFMT_NO_BYTE_SEEK, AVFMT_SEEK_TO_PTS.
			 */
			public int flags;

			/**
			 * If extensions are defined, then no probe is done. You should
			 * usually not use extension format guessing because it is not
			 * reliable enough
			 */
			public /*readonly*/ string extensions;//char*

			public /*readonly*/ AVCodecTag* codec_tag;//Also can't modify thing at the pointer? //const struct AVCodecTag * const *codec_tag;

			public /*readonly*/ AVClass* priv_class; ///< AVClass for the private context

			/**
			 * Comma-separated list of mime types.
			 * It is used check for matching mime types while probing.
			 * @see av_probe_input_format2
			 */
			public /*readonly*/ string mime_type;//char*

			/*****************************************************************
			 * No fields below this line are part of the public API. They
			 * may not be used outside of libavformat and can be changed and
			 * removed at will.
			 * New public fields should be added right above.
			 *****************************************************************
			 */
			public AVInputFormat* next;

			/**
			 * Raw demuxers store their codec ID here.
			 */
			public int raw_codec_id;

			/**
			 * Size of private data so that it can be allocated in the wrapper.
			 */
			public int priv_data_size;

			/**
			 * Tell if a given file has a chance of being parsed as this format.
			 * The buffer provided is guaranteed to be AVPROBE_PADDING_SIZE bytes
			 * big so you do not have to check for that unless you need more.
			 */
			public Func<AVProbeData,int> read_probe;

			/**
			 * Read the format header and initialize the AVFormatContext
			 * structure. Return 0 if OK. 'avformat_new_stream' should be
			 * called to create new streams.
			 */
			public Func<AVFormatContext,int> read_header;

			/**
			 * Read one packet and put it in 'pkt'. pts and flags are also
			 * set. 'avformat_new_stream' can be called only if the flag
			 * AVFMTCTX_NOHEADER is used and only in the calling thread (not in a
			 * background thread).
			 * @return 0 on success, < 0 on error.
			 *         When returning an error, pkt must not have been allocated
			 *         or must be freed before returning
			 */
			public Func<AVFormatContext,AVPacket/*pkt*/,int> read_packet;

			/**
			 * Close the stream. The AVFormatContext and AVStreams are not
			 * freed by this function
			 */
			public Func<AVFormatContext,int> read_close;

			/**
			 * Seek to a given timestamp relative to the frames in
			 * stream component stream_index.
			 * @param stream_index Must not be -1.
			 * @param flags Selects which direction should be preferred if no exact
			 *              match is available.
			 * @return >= 0 on success (but not necessarily the new offset)
			 */
			public Func<AVFormatContext,int/*stream_index*/,int64_t/*timestamp*/,int /*flags*/,int> read_seek;

			/**
			 * Get the next timestamp in stream[stream_index].time_base units.
			 * @return the timestamp or AV_NOPTS_VALUE if an error occurred
			 */
			public Func<AVFormatContext /*s*/,int /*stream_index*/,int64_t* /*pos*/,int64_t /*pos_limit*/,int64_t> read_timestamp;

			/**
			 * Start/resume playing - only meaningful if using a network-based format
			 * (RTSP).
			 */
			public Func<AVFormatContext,int> read_play;

			/**
			 * Pause playing - only meaningful if using a network-based format
			 * (RTSP).
			 */
			public Func<AVFormatContext,int> read_pause;

			/**
			 * Seek to timestamp ts.
			 * Seeking will be done so that the point from which all active streams
			 * can be presented successfully will be closest to ts and within min/max_ts.
			 * Active streams are all streams that have AVStream.discard < AVDISCARD_ALL.
			 */
			public Func<AVFormatContext /*s*/,int/*stream_index*/,int64_t/*min_ts*/,int64_t /*t*/,int64_t /*max_ts*/,int /*flags*/,int> read_seek2;

			/**
			 * Returns device list with it properties.
			 * @see avdevice_list_devices() for more details.
			 */
			public Func<AVFormatContext /*s*/,AVDeviceInfoList* /*device_list*/,int> get_device_list;

			/**
			 * Initialize device capabilities submodule.
			 * @see avdevice_capabilities_create() for more details.
			 */
			public Func<AVFormatContext /*s*/,AVDeviceCapabilitiesQuery* /*caps*/,int> create_device_capabilities;

			/**
			 * Free device capabilities submodule.
			 * @see avdevice_capabilities_free() for more details.
			 */
			public Func<AVFormatContext /*s*/,AVDeviceCapabilitiesQuery* /*caps*/,int> free_device_capabilities;
		}

		public class AVFormatContext//was struct
		{
			/**
			 * A class for logging and @ref avoptions. Set by avformat_alloc_context().
			 * Exports (de)muxer private options if they exist.
			 */
			public readonly AVClass* av_class;

			/**
			 * The input container format.
			 *
			 * Demuxing only, set by avformat_open_input().
			 */
			public AVInputFormat* iformat;

			/**
			 * The output container format.
			 *
			 * Muxing only, must be set by the caller before avformat_write_header().
			 */
			public AVOutputFormat* oformat;

			/**
			 * Format private data. This is an AVOptions-enabled struct
			 * if and only if iformat/oformat.priv_class is not NULL.
			 *
			 * - muxing: set by avformat_write_header()
			 * - demuxing: set by avformat_open_input()
			 */
			public void* priv_data;

			/**
			 * I/O context.
			 *
			 * - demuxing: either set by the user before avformat_open_input() (then
			 *             the user must close it manually) or set by avformat_open_input().
			 * - muxing: set by the user before avformat_write_header(). The caller must
			 *           take care of closing / freeing the IO context.
			 *
			 * Do NOT set this field if AVFMT_NOFILE flag is set in
			 * iformat/oformat.flags. In such a case, the (de)muxer will handle
			 * I/O in some other way and this field will be NULL.
			 */
			public AVIOContext pb;

			/* stream info */
			/**
			 * Flags signalling stream properties. A combination of AVFMTCTX_*.
			 * Set by libavformat.
			 */
			public int ctx_flags;

			/**
			 * Number of elements in AVFormatContext.streams.
			 *
			 * Set by avformat_new_stream(), must not be modified by any other code.
			 */
			public uint nb_streams;
			/**
			 * A list of all streams in the file. New streams are created with
			 * avformat_new_stream().
			 *
			 * - demuxing: streams are created by libavformat in avformat_open_input().
			 *             If AVFMTCTX_NOHEADER is set in ctx_flags, then new streams may also
			 *             appear in av_read_frame().
			 * - muxing: streams are created by the user before avformat_write_header().
			 *
			 * Freed by libavformat in avformat_free_context().
			 */
			public AVStream[] streams;

			/**
			 * input or output filename
			 *
			 * - demuxing: set by avformat_open_input()
			 * - muxing: may be set by the caller before avformat_write_header()
			 */
			public /*fixed*/ char[] filename = new char[1024];

			/**
			 * Position of the first frame of the component, in
			 * AV_TIME_BASE fractional seconds. NEVER set this value directly:
			 * It is deduced from the AVStream values.
			 *
			 * Demuxing only, set by libavformat.
			 */
			public int64_t start_time;

			/**
			 * Duration of the stream, in AV_TIME_BASE fractional
			 * seconds. Only set this value if you know none of the individual stream
			 * durations and also do not set any of them. This is deduced from the
			 * AVStream values if not set.
			 *
			 * Demuxing only, set by libavformat.
			 */
			public int64_t duration;

			/**
			 * Total stream bitrate in bit/s, 0 if not
			 * available. Never set it directly if the file_size and the
			 * duration are known as FFmpeg can compute it automatically.
			 */
			public int bit_rate;

			public uint packet_size;
			public int max_delay;

			/**
			 * Flags modifying the (de)muxer behaviour. A combination of AVFMT_FLAG_*.
			 * Set by the user before avformat_open_input() / avformat_write_header().
			 */
			public int flags;
			public const int AVFMT_FLAG_GENPTS = 0x0001; ///< Generate missing pts even if it requires parsing future frames.
			public const int AVFMT_FLAG_IGNIDX = 0x0002; ///< Ignore index.
			public const int AVFMT_FLAG_NONBLOCK = 0x0004; ///< Do not block when reading packets from input.
			public const int AVFMT_FLAG_IGNDTS = 0x0008; ///< Ignore DTS on frames that contain both DTS & PTS
			public const int AVFMT_FLAG_NOFILLIN = 0x0010; ///< Do not infer any values from other values, just return what is stored in the container
			public const int AVFMT_FLAG_NOPARSE = 0x0020; ///< Do not use AVParsers, you also must set AVFMT_FLAG_NOFILLIN as the fillin code works on frames and no parsing -> no frames. Also seeking to frames can not work if parsing to find frame boundaries has been disabled
			public const int AVFMT_FLAG_NOBUFFER = 0x0040; ///< Do not buffer frames when possible
			public const int AVFMT_FLAG_CUSTOM_IO = 0x0080; ///< The caller has supplied a custom AVIOContext, don't avio_close() it.
			public const int AVFMT_FLAG_DISCARD_CORRUPT = 0x0100; ///< Discard frames marked corrupted
			public const int AVFMT_FLAG_FLUSH_PACKETS = 0x0200; ///< Flush the AVIOContext every packet.
			/**
			 * When muxing, try to avoid writing any random/volatile data to the output.
			 * This includes any random IDs, real-time timestamps/dates, muxer version, etc.
			 *
			 * This flag is mainly intended for testing.
			 */
			public const int AVFMT_FLAG_BITEXACT = 0x0400;
			public const int AVFMT_FLAG_MP4A_LATM = 0x8000; ///< Enable RTP MP4A-LATM payload
			public const int AVFMT_FLAG_SORT_DTS = 0x10000; ///< try to interleave outputted packets by dts (using this flag can slow demuxing down)
			public const int AVFMT_FLAG_PRIV_OPT = 0x20000; ///< Enable use of private options by delaying codec open (this could be made default once all code is converted)
			public const int AVFMT_FLAG_KEEP_SIDE_DATA = 0x40000; ///< Don't merge side data but keep it separate.
			public const int AVFMT_FLAG_FAST_SEEK = 0x80000; ///< Enable fast, but inaccurate seeks for some formats

			/**
			 * @deprecated deprecated in favor of probesize2
			 */
			public uint probesize;

			/**
			 * @deprecated deprecated in favor of max_analyze_duration2
			 */
			//attribute_deprecated
			[Obsolete] public int max_analyze_duration;

			public readonly uint8_t* key;
			public int keylen;

			public uint nb_programs;
			public AVProgram** programs;

			/**
			 * Forced video codec_id.
			 * Demuxing: Set by user.
			 */
			public AVCodecID video_codec_id;

			/**
			 * Forced audio codec_id.
			 * Demuxing: Set by user.
			 */
			public AVCodecID audio_codec_id;

			/**
			 * Forced subtitle codec_id.
			 * Demuxing: Set by user.
			 */
			public AVCodecID subtitle_codec_id;

			/**
			 * Maximum amount of memory in bytes to use for the index of each stream.
			 * If the index exceeds this size, entries will be discarded as
			 * needed to maintain a smaller size. This can lead to slower or less
			 * accurate seeking (depends on demuxer).
			 * Demuxers for which a full in-memory index is mandatory will ignore
			 * this.
			 * - muxing: unused
			 * - demuxing: set by user
			 */
			public uint max_index_size;

			/**
			 * Maximum amount of memory in bytes to use for buffering frames
			 * obtained from realtime capture devices.
			 */
			public uint max_picture_buffer;

			/**
			 * Number of chapters in AVChapter array.
			 * When muxing, chapters are normally written in the file header,
			 * so nb_chapters should normally be initialized before write_header
			 * is called. Some muxers (e.g. mov and mkv) can also write chapters
			 * in the trailer.  To write chapters in the trailer, nb_chapters
			 * must be zero when write_header is called and non-zero when
			 * write_trailer is called.
			 * - muxing: set by user
			 * - demuxing: set by libavformat
			 */
			public uint nb_chapters;
			public AVChapter** chapters;

			/**
			 * Metadata that applies to the whole file.
			 *
			 * - demuxing: set by libavformat in avformat_open_input()
			 * - muxing: may be set by the caller before avformat_write_header()
			 *
			 * Freed by libavformat in avformat_free_context().
			 */
			public AVDictionary* metadata;

			/**
			 * Start time of the stream in real world time, in microseconds
			 * since the Unix epoch (00:00 1st January 1970). That is, pts=0 in the
			 * stream was captured at this real world time.
			 * - muxing: Set by the caller before avformat_write_header(). If set to
			 *           either 0 or AV_NOPTS_VALUE, then the current wall-time will
			 *           be used.
			 * - demuxing: Set by libavformat. AV_NOPTS_VALUE if unknown. Note that
			 *             the value may become known after some number of frames
			 *             have been received.
			 */
			public int64_t start_time_realtime;

			/**
			 * The number of frames used for determining the framerate in
			 * avformat_find_stream_info().
			 * Demuxing only, set by the caller before avformat_find_stream_info().
			 */
			public int fps_probe_size;

			/**
			 * Error recognition; higher values will detect more errors but may
			 * misdetect some more or less valid parts as errors.
			 * Demuxing only, set by the caller before avformat_open_input().
			 */
			public int error_recognition;

			/**
			 * Custom interrupt callbacks for the I/O layer.
			 *
			 * demuxing: set by the user before avformat_open_input().
			 * muxing: set by the user before avformat_write_header()
			 * (mainly useful for AVFMT_NOFILE formats). The callback
			 * should also be passed to avio_open2() if it's used to
			 * open the file.
			 */
			public AVIOInterruptCB interrupt_callback;

			/**
			 * Flags to enable debugging.
			 */
			public int debug;
			public const int FF_FDEBUG_TS = 0x0001;

			/**
			 * Maximum buffering duration for interleaving.
			 *
			 * To ensure all the streams are interleaved correctly,
			 * av_interleaved_write_frame() will wait until it has at least one packet
			 * for each stream before actually writing any packets to the output file.
			 * When some streams are "sparse" (i.e. there are large gaps between
			 * successive packets), this can result in excessive buffering.
			 *
			 * This field specifies the maximum difference between the timestamps of the
			 * first and the last packet in the muxing queue, above which libavformat
			 * will output a packet regardless of whether it has queued a packet for all
			 * the streams.
			 *
			 * Muxing only, set by the caller before avformat_write_header().
			 */
			public int64_t max_interleave_delta;

			/**
			 * Allow non-standard and experimental extension
			 * @see AVCodecContext.strict_std_compliance
			 */
			public int strict_std_compliance;

			/**
			 * Flags for the user to detect events happening on the file. Flags must
			 * be cleared by the user once the event has been handled.
			 * A combination of AVFMT_EVENT_FLAG_*.
			 */
			public int event_flags;
			public const int AVFMT_EVENT_FLAG_METADATA_UPDATED = 0x0001;///< The call resulted in updated metadata.

			/**
			 * Maximum number of packets to read while waiting for the first timestamp.
			 * Decoding only.
			 */
			public int max_ts_probe;

			/**
			 * Avoid negative timestamps during muxing.
			 * Any value of the AVFMT_AVOID_NEG_TS_* constants.
			 * Note, this only works when using av_interleaved_write_frame. (interleave_packet_per_dts is in use)
			 * - muxing: Set by user
			 * - demuxing: unused
			 */
			public int avoid_negative_ts;
			public const int AVFMT_AVOID_NEG_TS_AUTO = -1;///< Enabled when required by target format
			public const int AVFMT_AVOID_NEG_TS_MAKE_NON_NEGATIVE = 1;  ///< Shift timestamps so they are non negative
			public const int AVFMT_AVOID_NEG_TS_MAKE_ZERO = 2;  ///< Shift timestamps so that they start at 0

			/**
			 * Transport stream id.
			 * This will be moved into demuxer private options. Thus no API/ABI compatibility
			 */
			public int ts_id;

			/**
			 * Audio preload in microseconds.
			 * Note, not all formats support this and unpredictable things may happen if it is used when not supported.
			 * - encoding: Set by user via AVOptions (NO direct access)
			 * - decoding: unused
			 */
			public int audio_preload;

			/**
			 * Max chunk time in microseconds.
			 * Note, not all formats support this and unpredictable things may happen if it is used when not supported.
			 * - encoding: Set by user via AVOptions (NO direct access)
			 * - decoding: unused
			 */
			public int max_chunk_duration;

			/**
			 * Max chunk size in bytes
			 * Note, not all formats support this and unpredictable things may happen if it is used when not supported.
			 * - encoding: Set by user via AVOptions (NO direct access)
			 * - decoding: unused
			 */
			public int max_chunk_size;

			/**
			 * forces the use of wallclock timestamps as pts/dts of packets
			 * This has undefined results in the presence of B frames.
			 * - encoding: unused
			 * - decoding: Set by user via AVOptions (NO direct access)
			 */
			public int use_wallclock_as_timestamps;

			/**
			 * avio flags, used to force AVIO_FLAG_DIRECT.
			 * - encoding: unused
			 * - decoding: Set by user via AVOptions (NO direct access)
			 */
			public int avio_flags;

			/**
			 * The duration field can be estimated through various ways, and this field can be used
			 * to know how the duration was estimated.
			 * - encoding: unused
			 * - decoding: Read by user via AVOptions (NO direct access)
			 */
			public AVDurationEstimationMethod duration_estimation_method;

			/**
			 * Skip initial bytes when opening stream
			 * - encoding: unused
			 * - decoding: Set by user via AVOptions (NO direct access)
			 */
			public int64_t skip_initial_bytes;

			/**
			 * Correct single timestamp overflows
			 * - encoding: unused
			 * - decoding: Set by user via AVOptions (NO direct access)
			 */
			public uint correct_ts_overflow;

			/**
			 * Force seeking to any (also non key) frames.
			 * - encoding: unused
			 * - decoding: Set by user via AVOptions (NO direct access)
			 */
			public int seek2any;

			/**
			 * Flush the I/O context after each packet.
			 * - encoding: Set by user via AVOptions (NO direct access)
			 * - decoding: unused
			 */
			public int flush_packets;

			/**
			 * format probing score.
			 * The maximal score is AVPROBE_SCORE_MAX, its set when the demuxer probes
			 * the format.
			 * - encoding: unused
			 * - decoding: set by avformat, read by user via av_format_get_probe_score() (NO direct access)
			 */
			public int probe_score;

			/**
			 * number of bytes to read maximally to identify format.
			 * - encoding: unused
			 * - decoding: set by user through AVOPtions (NO direct access)
			 */
			public int format_probesize;

			/**
			 * ',' separated list of allowed decoders.
			 * If NULL then all are allowed
			 * - encoding: unused
			 * - decoding: set by user through AVOptions (NO direct access)
			 */
			public char* codec_whitelist;

			/**
			 * ',' separated list of allowed demuxers.
			 * If NULL then all are allowed
			 * - encoding: unused
			 * - decoding: set by user through AVOptions (NO direct access)
			 */
			public char* format_whitelist;

			/**
			 * An opaque field for libavformat internal usage.
			 * Must not be accessed in any way by callers.
			 */
			public AVFormatInternal* @internal;

			/**
			 * IO repositioned flag.
			 * This is set by avformat when the underlaying IO context read pointer
			 * is repositioned, for example when doing byte based seeking.
			 * Demuxers can use the flag to detect such changes.
			 */
			public int io_repositioned;

			/**
			 * Forced video codec.
			 * This allows forcing a specific decoder, even when there are multiple with
			 * the same codec_id.
			 * Demuxing: Set by user via av_format_set_video_codec (NO direct access).
			 */
			public AVCodec* video_codec;

			/**
			 * Forced audio codec.
			 * This allows forcing a specific decoder, even when there are multiple with
			 * the same codec_id.
			 * Demuxing: Set by user via av_format_set_audio_codec (NO direct access).
			 */
			public AVCodec* audio_codec;

			/**
			 * Forced subtitle codec.
			 * This allows forcing a specific decoder, even when there are multiple with
			 * the same codec_id.
			 * Demuxing: Set by user via av_format_set_subtitle_codec (NO direct access).
			 */
			public AVCodec* subtitle_codec;

			/**
			 * Forced data codec.
			 * This allows forcing a specific decoder, even when there are multiple with
			 * the same codec_id.
			 * Demuxing: Set by user via av_format_set_data_codec (NO direct access).
			 */
			public AVCodec* data_codec;

			/**
			 * Number of bytes to be written as padding in a metadata header.
			 * Demuxing: Unused.
			 * Muxing: Set by user via av_format_set_metadata_header_padding.
			 */
			public int metadata_header_padding;

			/**
			 * User data.
			 * This is a place for some private data of the user.
			 * Mostly usable with control_message_cb or any future callbacks in device's context.
			 */
			public void* opaque;

			/**
			 * Callback used by devices to communicate with application.
			 */
			public av_format_control_message control_message_cb;

			/**
			 * Output timestamp offset, in microseconds.
			 * Muxing: set by user via AVOptions (NO direct access)
			 */
			public int64_t output_ts_offset;

			/**
			 * Maximum duration (in AV_TIME_BASE units) of the data read
			 * from input in avformat_find_stream_info().
			 * Demuxing only, set by the caller before avformat_find_stream_info()
			 * via AVOptions (NO direct access).
			 * Can be set to 0 to let avformat choose using a heuristic.
			 */
			public int64_t max_analyze_duration2;

			/**
			 * Maximum size of the data read from input for determining
			 * the input container format.
			 * Demuxing only, set by the caller before avformat_open_input()
			 * via AVOptions (NO direct access).
			 */
			public int64_t probesize2;

			/**
			 * dump format separator.
			 * can be ", " or "\n      " or anything else
			 * Code outside libavformat should access this field using AVOptions
			 * (NO direct access).
			 * - muxing: Set by user.
			 * - demuxing: Set by user.
			 */
			public uint8_t* dump_separator;

			/**
			 * Forced Data codec_id.
			 * Demuxing: Set by user.
			 */
			public AVCodecID data_codec_id;

			/**
			 * Called to open further IO contexts when needed for demuxing.
			 *
			 * This can be set by the user application to perform security checks on
			 * the URLs before opening them.
			 * The function should behave like avio_open2(), AVFormatContext is provided
			 * as contextual information and to reach AVFormatContext.opaque.
			 *
			 * If NULL then some simple checks are used together with avio_open2().
			 *
			 * Must not be accessed directly from outside avformat.
			 * @See av_format_set_open_cb()
			 *
			 * Demuxing: Set by user.
			 */
			public Func<AVFormatContext /*s*/,AVIOContext /*p*/,/*in*/ string /*url*/,int /*flags*/,/*in*/ AVIOInterruptCB* /*int_cb*/,AVDictionary** /*options*/,int> open_cb;
		}
		public enum AVDurationEstimationMethod { }
	}
}