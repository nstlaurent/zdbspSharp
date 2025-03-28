﻿using System.Text;

namespace zdbspSharp;

public sealed class FWadWriter : IDisposable
{
	private readonly DynamicArray<WadLump> Lumps = new();
	private readonly Stream WriteStream;

	public FWadWriter(Stream stream, bool iwad)
	{
		WriteStream = stream;
		WadHeader head = new();
		if (iwad)
			head.Magic[0] = (byte)'I';
		else
			head.Magic[0] = (byte)'P';

		head.Magic[1] = (byte)'W';
		head.Magic[2] = (byte)'A';
		head.Magic[3] = (byte)'D';

		byte[] data = Util.StructureToBytes(head);
		WriteStream.Write(data, 0, data.Length);
	}

	public void Dispose()
	{
		if (WriteStream != null)
		{
			FinalizeLumps();
			WriteStream.Close();
			WriteStream.Dispose();
		}
	}

	public void CreateLabel(string name)
	{
		WadLump lump = new();

		StringExtensions.CopyString(lump.Name, name, 8);
		lump.FilePos = (int)WriteStream.Position;
		lump.Size = 0;
		Lumps.Add(lump);
	}

	public void WriteLump(string name, byte[] data, int len)
	{
		WadLump lump = new WadLump();
		StringExtensions.CopyString(lump.Name, name, 8);
		lump.FilePos = (int)WriteStream.Position;
		lump.Size = len;
		Lumps.Add(lump);

		WriteStream.Write(data, 0, len);
	}

	public void CopyLump(FWadReader wad, int lump)
	{
		byte[]? data = FWadReader.ReadLump(wad, lump);
		if (data != null)
		{
			WriteLump(wad.LumpName(lump), data, data.Length);
		}
	}

	public void FinalizeLumps()
	{
		if (WriteStream != null)
		{
			int[] head = [Lumps.Length, (int)WriteStream.Position];
            for (int i = 0; i < Lumps.Length; i++)
			{
				byte[] data = Util.StructureToBytes(Lumps[i]);
				WriteStream.Write(data, 0, data.Length);
			}

			WriteStream.Seek(4, SeekOrigin.Begin);
			WriteStream.Write(BitConverter.GetBytes(head[0]));
			WriteStream.Write(BitConverter.GetBytes(head[1]));
		}
	}

	// Routines to write a lump in segments.
	public void StartWritingLump(string name)
	{
		CreateLabel(name);
	}

    public void AddToLump(byte[] data)
    {
        WriteStream.Write(data, 0, data.Length);
        ref WadLump lump = ref Lumps.Data[Lumps.Length - 1];
        lump.Size += data.Length;
    }

    public void AddToLump(byte[] data, int len)
	{
		WriteStream.Write(data, 0, len);
		ref WadLump lump = ref Lumps.Data[Lumps.Length - 1];
		lump.Size += len;
	}

	public void AddToLump(string str)
	{
		var data = Encoding.UTF8.GetBytes(str);
		AddToLump(data, data.Length);
    }

	public void WriteShort(short val)
	{
		byte[] data = BitConverter.GetBytes(val);
		AddToLump(data, data.Length);
	}

	public void WriteUshort(ushort val)
	{
		byte[] data = BitConverter.GetBytes(val);
		AddToLump(data, data.Length);
	}

	public void WriteInt(int val)
	{
		byte[] data = BitConverter.GetBytes(val);
		AddToLump(data, data.Length);
	}

	public void WriteUint(uint val)
	{
		byte[] data = BitConverter.GetBytes(val);
		AddToLump(data, data.Length);
	}

	public void WriteByte(byte val)
	{
		byte[] data = BitConverter.GetBytes((short)val);
		AddToLump(data, data.Length);
	}
}
