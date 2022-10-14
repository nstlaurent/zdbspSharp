namespace zdbspSharp;

public sealed class FLevel
{
	public WideVertex[] Vertices = Array.Empty<WideVertex>();
	public int NumVertices;
	public List<IntVertex> VertexProps = new();
	public DynamicArray<IntSideDef> Sides = new();
	public DynamicArray<IntLineDef> Lines = new();
	public DynamicArray<IntSector> Sectors = new();
	public List<IntThing> Things = new();
	public MapSubsectorEx[] Subsectors = Array.Empty<MapSubsectorEx>();
	public MapSegEx[] Segs = Array.Empty<MapSegEx>();
	public MapNodeEx[] Nodes = Array.Empty<MapNodeEx>();
	public ushort[] Blockmap = Array.Empty<ushort>();
	public byte[]? Reject = null;
	public int RejectSize;

	public MapSubsectorEx[] GLSubsectors = Array.Empty<MapSubsectorEx>();
	public MapSegGLEx[] GLSegs = Array.Empty<MapSegGLEx>();
	public MapNodeEx[] GLNodes = Array.Empty<MapNodeEx>();
	public WideVertex[] GLVertices = Array.Empty<WideVertex>();
	public byte[] GLPVS = Array.Empty<byte>();

	public int NumOrgVerts;

	public uint[] OrgSectorMap = Array.Empty<uint>();
	public int NumOrgSectors;

	public int MinX;
	public int MinY;
	public int MaxX;
	public int MaxY;

	public List<UDMFKey> props = new();

	public void FindMapBounds()
	{
		int minx;
		int maxx;
		int miny;
		int maxy;

		minx = maxx = Vertices[0].x;
		miny = maxy = Vertices[0].y;

		for (int i = 1; i < NumVertices; ++i)
		{
			if (Vertices[i].x < minx)
				minx = Vertices[i].x;
			else if (Vertices[i].x > maxx)
				maxx = Vertices[i].x;
			if (Vertices[i].y < miny)
				miny = Vertices[i].y;
			else if (Vertices[i].y > maxy)
				maxy = Vertices[i].y;
		}

		MinX = minx;
		MinY = miny;
		MaxX = maxx;
		MaxY = maxy;
	}

	public void RemoveExtraLines()
	{
		int newNumLines;

		// Extra lines are those with 0 length. Collision detection against
		// one of those could cause a divide by 0, so it's best to remove them.    
		for (int i = newNumLines = 0; i < NumLines(); ++i)
		{
			if (Vertices[Lines[i].v1].x != Vertices[Lines[i].v2].x ||
				Vertices[Lines[i].v1].y != Vertices[Lines[i].v2].y)
			{
				if (i != newNumLines)
					Lines[newNumLines] = Lines[i];
				++newNumLines;
			}
		}

		//if (newNumLines < NumLines())
		//{
		//	int diff = NumLines() - newNumLines;
		//	printf("   Removed %d line%s with 0 length.\n", diff, diff > 1 ? "s" : "");
		//}

		Lines.SetLength(newNumLines);
	}

	public unsafe void RemoveExtraSides()
	{
		byte[] used;
		int[] remap;
		int newNumSides;

		// Extra sides are those that aren't referenced by any lines.
		// They just waste space, so get rid of them.
		int NumSides = this.NumSides();

		used = new byte[NumSides];
		remap = new int[NumSides];

		// Mark all used sides
		for (int i = 0; i < NumLines(); ++i)
		{
			fixed (IntLineDef* line = &Lines.Data[i])
			{
				if (line->sidenum[0] != Constants.MAX_INT)
					used[line->sidenum[0]] = 1;
				//else
				//printf("   Line %d needs a front sidedef before it will run with ZDoom.\n", i);

				if (line->sidenum[1] != Constants.MAX_INT)
					used[line->sidenum[1]] = 1;
			}
		}

		// Shift out any unused sides
		for (int i = newNumSides = 0; i < NumSides; ++i)
		{
			if (used[i] != 0)
			{
				if (i != newNumSides)
					Sides[newNumSides] = Sides[i];
				remap[i] = newNumSides++;
			}
			else
			{
				remap[i] = Constants.MAX_INT;
			}
		}

		if (newNumSides < NumSides)
		{
			//int diff = NumSides - newNumSides;
			//printf("   Removed %d unused sidedef%s.\n", diff, diff > 1 ? "s" : "");
			Sides.SetLength(newNumSides);

			// Renumber side references in lines
			for (int i = 0; i < NumLines(); ++i)
			{
				fixed (IntLineDef* line = &Lines.Data[i])
				{
					if (line->sidenum[0] != Constants.MAX_INT)
						line->sidenum[0] = remap[line->sidenum[0]];
					if (line->sidenum[1] != Constants.MAX_INT)
						line->sidenum[1] = remap[line->sidenum[1]];
				}
			}
		}
	}
	public void RemoveExtraSectors()
	{
		byte[] used;
		uint[] remap;
		int i;
		int newNumSectors;

		// Extra sectors are those that aren't referenced by any sides.
		// They just waste space, so get rid of them.

		NumOrgSectors = NumSectors();
		used = new byte[NumSectors()];
		remap = new uint[NumSectors()];

		// Mark all used sectors
		for (i = 0; i < NumSides(); ++i)
		{
			if ((uint)Sides[i].sector != Constants.MAX_UINT)
				used[Sides[i].sector] = 1;
			//else
			//printf("   Sidedef %d needs a front sector before it will run with ZDoom.\n", i);
		}

		// Shift out any unused sides
		for (i = newNumSectors = 0; i < NumSectors(); ++i)
		{
			if (used[i] != 0)
			{
				if (i != newNumSectors)
					Sectors[newNumSectors] = Sectors[i];
				remap[i] = (uint)(newNumSectors++);
			}
			else
			{
				remap[i] = Constants.MAX_UINT;
			}
		}

		if (newNumSectors < NumSectors())
		{
			//int diff = NumSectors() - newNumSectors;
			//printf("   Removed %d unused sector%s.\n", diff, diff > 1 ? "s" : "");

			// Renumber sector references in sides
			for (i = 0; i < NumSides(); ++i)
			{
				if ((uint)Sides[i].sector != Constants.MAX_UINT)
				{
					IntSideDef side = Sides[i];
					side.sector = (int)remap[Sides[i].sector];
					Sides[i] = side;
				}
			}

			// Make a reverse map for fixing reject lumps
			OrgSectorMap = new uint[newNumSectors];
			for (i = 0; i < NumSectors(); ++i)
			{
				if (remap[i] != Constants.MAX_UINT)
					OrgSectorMap[remap[i]] = (uint)i;
			}

			Sectors.SetLength(newNumSectors);
		}
	}

	public int NumSides()
	{
		return Sides.Length;
	}

	public int NumLines()
	{
		return Lines.Length;
	}

	public int NumSectors()
	{
		return Sectors.Length;
	}

	public int NumThings()
	{
		return Things.Count;
	}
}
