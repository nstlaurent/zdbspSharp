using System.Runtime.InteropServices;

namespace zdbspSharp;

public enum EBlockmapMode
{
	EBM_Rebuild,
	EBM_Create0
};

public enum ERejectMode
{
	ERM_DontTouch,
	ERM_CreateZeroes,
	ERM_Create0,
	ERM_Rebuild
};

public static class Box
{
	public const int BOXTOP = 0;
	public const int BOXBOTTOM = 1;
	public const int BOXLEFT = 2;
	public const int BOXRIGHT = 3;
}

public struct UDMFKey
{
	public string key;
	public string value;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapVertex
{
	public short x;
	public short y;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WideVertex
{
	public int x;
	public int y;
	public int index;

    public override string ToString()
    {
		return $"{x}, {y} {index}";
    }
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapSideDef
{
	public short textureoffset;
	public short rowoffset;
	public fixed byte toptexture[8];
	public fixed byte bottomtexture[8];
	public fixed byte midtexture[8];
	public ushort sector;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct IntSideDef
{
	// the first 5 values are only used for binary format maps
	public short textureoffset;
	public short rowoffset;
	public fixed byte toptexture[8];
	public fixed byte bottomtexture[8];
	public fixed byte midtexture[8];

	public int sector;

	//public List<UDMFKey> props = new List<UDMFKey>();
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapLineDef
{
	public ushort v1;
	public ushort v2;
	public short flags;
	public short special;
	public short tag;
	public fixed ushort sidenum[2];
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapLineDef2
{
	public ushort v1;
	public ushort v2;
	public short flags;
	public byte special;
	public fixed byte args[5];
	public fixed ushort sidenum[2];
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct IntLineDef
{
	public int v1;
	public int v2;
	public int flags;
	public int special;
	public fixed int args[5];
	public fixed int sidenum[2];

	//public List<UDMFKey> props = new List<UDMFKey>();
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapSector
{
	public short floorheight;
	public short ceilingheight;
	public fixed byte floorpic[8];
	public fixed byte ceilingpic[8];
	public short lightlevel;
	public short special;
	public short tag;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct IntSector
{
	// none of the sector properties are used by the node builder
	// so there's no need to store them in their expanded form for
	// UDMF. Just storing the UDMF keys and leaving the binary fields
	// empty is enough
	public short floorheight;
	public short ceilingheight;
	public fixed byte floorpic[8];
	public fixed byte ceilingpic[8];
	public short lightlevel;
	public short special;
	public short tag;

	//public List<UDMFKey> props = new List<UDMFKey>();
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapSubsector
{
	public ushort numlines;
	public ushort firstline;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapSubsectorEx
{
	public int numlines;
	public int firstline;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapSeg
{
	public ushort v1;
	public ushort v2;
	public ushort angle;
	public ushort linedef;
	public short side;
	public short offset;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapSegEx
{
	public int v1;
	public int v2;
	public ushort angle;
	public ushort linedef;
	public short side;
	public short offset;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapSegGL
{
	public ushort v1;
	public ushort v2;
	public ushort linedef;
	public ushort side;
	public ushort partner;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapSegGLEx
{
	public int v1;
	public int v2;
	public int linedef;
	public ushort side;
	public int partner;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapNode
{
	public short x;
	public short y;
	public short dx;
	public short dy;
	public fixed short bbox[2 * 4];
	public fixed ushort children[2];
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapNodeExO
{
	public short x;
	public short y;
	public short dx;
	public short dy;
	public fixed short bbox[2 * 4];
	public fixed uint children[2];
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapNodeEx
{
	public int x;
	public int y;
	public int dx;
	public int dy;
	public fixed short bbox[2 * 4];
	public fixed uint children[2];
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapThing
{
	public short x;
	public short y;
	public short angle;
	public short type;
	public short flags;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct MapThing2
{
	public ushort thingid;
	public short x;
	public short y;
	public short z;
	public short angle;
	public short type;
	public short flags;
	public byte special;
	public fixed byte args[5];
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct IntThing
{
	public ushort thingid;
	public int x; // full precision coordinates for UDMF support
	public int y;
	// everything else is not needed or has no extended form in UDMF
	public short z;
	public short angle;
	public short type;
	public short flags;
	public byte special;
	public fixed byte args[5];

	//public List<UDMFKey> props = new List<UDMFKey>();
}

[StructLayout(LayoutKind.Sequential)]
public struct FPolyStart
{
	public int polynum;
	public int x, y;
};

public struct IntVertex
{
	public IntVertex() { }

	public List<UDMFKey> props = new List<UDMFKey>();
}

public sealed class FPrivVert
{
	public int x, y;
	public int segs;     // segs that use this vertex as v1
	public int segs2;    // segs that use this vertex as v2
	public int index;
	public int pad;        // This structure must be 8-byte aligned.

    public override bool Equals(object? obj)
    {
		if (obj is not FPrivVert privVert)
			return false;

		return x == privVert.x && y == privVert.y;
    }

    public override int GetHashCode()
    {
		return x ^ y;
    }
}

public sealed class FPrivSeg
{
	public FPrivSeg() { }

	public FPrivSeg(FPrivSeg other)
    {
		v1 = other.v1;
		v2 = other.v2;
		sidedef = other.sidedef;
		linedef = other.linedef;
		frontsector = other.frontsector;
		backsector = other.backsector;
		next = other.next;
		nextforvert = other.nextforvert;
		nextforvert2 = other.nextforvert2;
		loopnum = other.loopnum;
		partner = other.partner;
		storedseg = other.storedseg;
		angle = other.angle;
		offset = other.offset;
		planenum = other.planenum;
		planefront = other.planefront;
		hashnext = other.hashnext;
    }

	public int v1, v2;
	public int sidedef;
	public int linedef;
	public int frontsector;
	public int backsector;
	public int next;
	public int nextforvert;
	public int nextforvert2;
	public int loopnum;        // loop number for split avoidance (0 means splitting is okay)
	public int partner;      // seg on back side
	public int storedseg;    // seg # in the GL_SEGS lump
	public uint angle;
	public int offset;
	public int planenum;
	public bool planefront;
	public FPrivSeg? hashnext;
};

public sealed class FSimpleLine
{
	public FSimpleLine(int sx, int sy, int sdx, int sdy)
    {
		x = sx;
		y = sy;
		dx = sdx;
		dy = sdy;
    }

	public int x, y, dx, dy;
};

public sealed class USegPtr
{
	public int SegNum;
	public FPrivSeg SegPtr;

    public override string ToString()
    {
		return $"{SegPtr.v1} , {SegPtr.v2}";
    }
}

public sealed class FSplitSharer
{
	public FSplitSharer(double distance, int seg, bool forward)
    {
		Distance = distance;
		Seg = seg;
		Forward = forward;
    }

	public double Distance;
	public int Seg;
	public bool Forward;
};

[StructLayout(LayoutKind.Sequential)]
public struct vertex_t
{
	public int x, y;
};

[StructLayout(LayoutKind.Sequential)]
public unsafe struct node_t
{
	public int x, y, dx, dy;
	public fixed int bbox[2 * 4];
	public fixed uint intchildren[2];
};

[StructLayout(LayoutKind.Sequential)]
public struct subsector_t
{
	public int numlines;
	public int firstline;
};