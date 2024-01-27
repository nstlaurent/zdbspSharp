namespace zdbspSharp;

public sealed class FVertexMap
{
	private const int BLOCK_SHIFT = 8 + 16;
	private const int BLOCK_SIZE = 1 << BLOCK_SHIFT;
	private const int VERTEX_EPSILON = 6;

	private readonly FNodeBuilder MyBuilder;
	private readonly List<int>[] VertexGrid;

	private readonly int MinX, MinY, MaxX, MaxY;
	private readonly int BlocksWide, BlocksTall;

	public FVertexMap(FNodeBuilder builder, int minx, int miny, int maxx, int maxy)
	{
		MyBuilder = builder;
		MinX = minx;
		MinY = miny;
		BlocksWide = (int)((((double)maxx - minx + 1) + (BLOCK_SIZE - 1)) / BLOCK_SIZE);
		BlocksTall = (int)((((double)maxy - miny + 1) + (BLOCK_SIZE - 1)) / BLOCK_SIZE);
		MaxX = MinX + BlocksWide * BLOCK_SIZE - 1;
		MaxY = MinY + BlocksTall * BLOCK_SIZE - 1;
		VertexGrid = new List<int>[BlocksWide * BlocksTall];
		for (int i = 0; i < VertexGrid.Length; i++)
			VertexGrid[i] = new List<int>();
	}

	public int SelectVertexExact(FPrivVert vert)
	{
		List<int> block = VertexGrid[GetBlock(vert.x, vert.y)];
		List<FPrivVert> vertices = MyBuilder.Vertices;

		for (int i = 0; i < block.Count; ++i)
		{
			if (vertices[block[i]].x == vert.x && vertices[block[i]].y == vert.y)
				return block[i];
		}

		// Not present: add it!
		return InsertVertex(vert);
	}

	public int SelectVertexClose(FPrivVert vert)
	{
		List<int> block = VertexGrid[GetBlock(vert.x, vert.y)];
		List<FPrivVert> vertices = MyBuilder.Vertices;

		for (int i = 0; i < block.Count; ++i)
		{
			if (Math.Abs(vertices[block[i]].x - vert.x) < VERTEX_EPSILON && Math.Abs(vertices[block[i]].y - vert.y) < VERTEX_EPSILON)
				return block[i];
		}

		// Not present: add it!
		return InsertVertex(vert);
	}

	//[LocalsInit(false)]
	int InsertVertex(FPrivVert vert)
	{
		int vertnum;

		vert.segs = Constants.MAX_INT;
		vert.segs2 = Constants.MAX_INT;
		vertnum = MyBuilder.Vertices.AddCount(vert);

		// If a vertex is near a block boundary, then it will be inserted on
		// both sides of the boundary so that SelectVertexClose can find
		// it by checking in only one block.
		int minx = Math.Max(MinX, vert.x - VERTEX_EPSILON);
		int maxx = Math.Min(MaxX, vert.x + VERTEX_EPSILON);
		int miny = Math.Max(MinY, vert.y - VERTEX_EPSILON);
		int maxy = Math.Min(MaxY, vert.y + VERTEX_EPSILON);

		int[] blk = new int[]{ GetBlock(minx, miny), GetBlock(maxx, miny), GetBlock(minx, maxy), GetBlock(maxx, maxy) };
		int[] blkcount = new int[]{ VertexGrid[blk[0]].Count, VertexGrid[blk[1]].Count, VertexGrid[blk[2]].Count, VertexGrid[blk[3]].Count };
		for (int i = 0; i < 4; ++i)
		{
			if (VertexGrid[blk[i]].Count == blkcount[i])
				VertexGrid[blk[i]].Add(vertnum);
		}

		return vertnum;
	}

	int GetBlock(int x, int y)
	{
		int block = (int)((((uint)x - (uint)MinX) >> BLOCK_SHIFT) + (((uint)y - (uint)MinY) >> BLOCK_SHIFT) * BlocksWide);
		return Math.Clamp(block, 0, VertexGrid.Length - 1);
	}
}