using System;

public unsafe static class c_base
{
	public static bool memcmp(byte* a,byte* b,int count)
	{
		for(int i=0;i<count;i++)
		{
			if(a[i]!=b[i])
			{
				return false;
			}
		}
		return true;
	}
	public static bool memcmp(byte[] a,int offset_a,byte[] b,int offset_b,int count)
	{
		for(int i=0;i<count;i++)
		{
			if(a[offset_a+i]!=b[offset_b+i])
			{
				return false;
			}
		}
		return true;
	}
	public static bool memcmp(byte* a,int offset_a,byte[] b,int offset_b,int count)
	{
		for(int i=0;i<count;i++)
		{
			if(a[offset_a+i]!=b[offset_b+i])
			{
				return false;
			}
		}
		return true;
	}
	public static void av_log(object o,int type,string format,params object[] args)
	{
		Console.Write(format,args);
	}
	//Random values
	public const int AV_LOG_ERROR=1;
}
