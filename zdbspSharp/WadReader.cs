/*
    WAD-handling routines.
    Copyright (C) 2002-2006 Randy Heit

    This program is free software; you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation; either version 2 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 675 Mass Ave, Cambridge, MA 02139, USA.

*/
using System.Runtime.InteropServices;

namespace zdbspSharp;

[StructLayout(LayoutKind.Sequential)]
public struct WadHeader
{
	public WadHeader() { }

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
	public byte[] Magic = new byte[4];
	public int NumLumps = 0;
	public int Directory = 0;
}

[StructLayout(LayoutKind.Sequential)]
public struct WadLump
{
	public WadLump() { }

	public int FilePos = 0;
	public int Size = 0;
	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
	public byte[] Name = new byte[8];

	//public string Name
	//{
	//	get
	//	{
	//		int index = Array.IndexOf(_Name, (byte)0);
	//		if (index > 8 || index < 0)
	//			index = 8;
	//		return System.Text.Encoding.UTF8.GetString(_Name, 0, index);
	//	}
	//}
}

public sealed class FWadReader : IDisposable
{
	public WadHeader Header = new();
	public WadLump[] Lumps;
	public Stream ReadStream;

	public FWadReader(Stream stream)
	{
		Lumps = Array.Empty<WadLump>();
		ReadStream = stream;

		Header = Util.ReadStuctureFromStream<WadHeader>(ReadStream);
		if (Header.Magic[0] != 'P' && Header.Magic[0] != 'I' && Header.Magic[1] != 'W' && Header.Magic[2] != 'A' && Header.Magic[3] != 'D')
		{
			ReadStream.Close();
			throw new Exception("Input file is not a wad");
		}

		ReadStream.Seek(Header.Directory, SeekOrigin.Begin);

		Lumps = new WadLump[Header.NumLumps];
		for (int i = 0; i < Header.NumLumps; i++)
			Lumps[i] = Util.ReadStuctureFromStream<WadLump>(ReadStream);
	}

	public void Dispose()
	{
		if (ReadStream != null)
		{
			ReadStream.Close();
			ReadStream.Dispose();
		}
	}

	public bool IsIWAD()
	{
		return Header.Magic[0] == 'I';
	}

	public bool isUDMF(int index)
	{
		index++;
		if (index >= Lumps.Length)
			return false;

		if (StringExtensions.strnicmp(Lumps[index].Name, "TEXTMAP", 8))
			return true;

		return false;
	}

	public int FindLump(string name, int index = 0)
	{
		if (index < 0)
			index = 0;

		for (; index < Header.NumLumps; ++index)
		{
			if (StringExtensions.strnicmp(Lumps[index].Name, name, 8))
				return index;
		}

		return -1;
	}

	public int FindMapLump(string name, int map)
	{
		int i;
		int j;
		int k;
		++map;

		for (i = 0; i < 12; ++i)
		{
			if (Constants.MapLumpNames[i].EqualsIgnoreCase(name))
				break;
		}
		if (i == 12)
		{
			return -1;
		}

		for (j = k = 0; j < 12; ++j)
		{
			// More garbage zdbsp overflows...
			if (map + k >= Lumps.Length)
				break;

			if (StringExtensions.strnicmp(Lumps[map + k].Name, Constants.MapLumpNames[j], 8))
			{
				if (i == j)
					return map + k;
				k++;
			}
		}
		return -1;
	}

	public int FindGLLump(string name, int glheader)
	{
		int i;
		int j;
		int k;
		++glheader;

		for (i = 0; i < 5; ++i)
		{
			if (StringExtensions.strnicmp(Lumps[glheader + i].Name, name, 8))
				break;
		}
		if (i == 5)
		{
			return -1;
		}

		for (j = k = 0; j < 5; ++j)
		{
			if (StringExtensions.strnicmp(Lumps[glheader + k].Name, Constants.GLLumpNames[j], 8))
			{
				if (i == j)
					return glheader + k;
				k++;
			}
		}
		return -1;
	}

	public string LumpName(int lump)
	{
		var data = Lumps[lump].Name;
		int index = Array.IndexOf(data, (byte)0);
		if (index > 8 || index < 0)
			index = 8;
		return System.Text.Encoding.UTF8.GetString(data, 0, index);
	}

    public bool IsMap(int index)
	{
		int i;
		int j;

		if (isUDMF(index))
			return true;

		index++;

		for (i = j = 0; i < 12; ++i)
		{
			// Zdbsp didn't check for this and would just read garbage. Random garbage memory will not match.
			if (index + j >= Lumps.Length)
				return true;

			if (!StringExtensions.strnicmp(Lumps[index + j].Name, Constants.MapLumpNames[i], 8))
			{
				if (Constants.MapLumpRequired[i])
					return false;
			}
			else
			{
				j++;
			}
		}
		return true;
	}

	public bool IsGLNodes(int index)
	{
		if (index + 4 >= Header.NumLumps)
			return false;

		if (Lumps[index].Name[0] != 'G' || Lumps[index].Name[1] != 'L' || Lumps[index].Name[2] != '_')
			return false;

		index++;
		for (int i = 0; i < 4; ++i)
		{
			if (!StringExtensions.strnicmp(Lumps[i + index].Name, Constants.GLLumpNames[i], 8))
				return false;
		}
		return true;
	}

	public int SkipGLNodes(int index)
	{
		index++;
		for (int i = 0; i < 5 && index < Header.NumLumps; ++i, ++index)
		{
			if (!StringExtensions.strnicmp(Lumps[index].Name, Constants.GLLumpNames[i], 8))
			{
				break;
			}
		}
		return index;
	}

	public bool MapHasBehavior(int map)
	{
		return FindMapLump("BEHAVIOR", map) != -1;
	}

	public int NextMap(int index)
	{
		if (index < 0)
			index = 0;
		else
			index++;
		for (; index < Header.NumLumps; ++index)
		{
			if (IsMap(index))
				return index;
		}
		return -1;
	}

	public int LumpAfterMap(int i)
	{
		int j;
		int k;

		if (isUDMF(i))
		{
			// UDMF map
			i += 2;
			while (!StringExtensions.strnicmp(Lumps[i].Name, "ENDMAP", 8) && i < Header.NumLumps)
				i++;
			return i + 1; // one lump after ENDMAP
		}

		i++;
		for (j = k = 0; j < 12; ++j)
		{
			if (i + k >= Lumps.Length)
            {
				k++;
				continue;
            }

			if (!StringExtensions.strnicmp(Lumps[i + k].Name, Constants.MapLumpNames[j], 8))
			{
				if (Constants.MapLumpRequired[j])
					break;
			}
			else
			{
				k++;
			}
		}
		return i + k;
	}

	public int NumLumps()
	{
		return Header.NumLumps;
	}

	public static byte[]? ReadLump(FWadReader wad, int index)
	{
		if ((uint)index >= (uint)wad.Header.NumLumps)
			return null;

		wad.ReadStream.Seek(wad.Lumps[index].FilePos, SeekOrigin.Begin);
		byte[] data = new byte[wad.Lumps[index].Size];
		Util.ReadStream(wad.ReadStream, data, data.Length);
		return data;
	}
}