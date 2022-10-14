using zdbspSharp;

/*
    Routines for building a Doom map's BLOCKMAP lump.
    Copyright (C) 2002 Randy Heit

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

public class FBlockmapBuilder
{
	public FBlockmapBuilder(FLevel level)
	{
		this.Level = level;
		BuildBlockmap();
	}

	public DynamicArray<ushort> GetBlockmap()
	{
		return BlockMap;
	}

	private FLevel Level;
	private DynamicArray<ushort> BlockMap = new DynamicArray<ushort>();

	private void BuildBlockmap()
	{
		DynamicArray<ushort> block;
		DynamicArray<ushort> endblock;
		int blockIndex;
		int endBlockIndex;
		ushort adder;
		int bmapwidth;
		int bmapheight;
		int minx;
		int maxx;
		int miny;
		int maxy;
		ushort line;

		if (Level.NumVertices <= 0)
			return;

		// Get map extents for the blockmap
		minx = Level.MinX >> Constants.FRACBITS;
		miny = Level.MinY >> Constants.FRACBITS;
		maxx = Level.MaxX >> Constants.FRACBITS;
		maxy = Level.MaxY >> Constants.FRACBITS;

		bmapwidth = ((maxx - minx) >> Constants.BLOCKBITS) + 1;
		bmapheight = ((maxy - miny) >> Constants.BLOCKBITS) + 1;

		adder = (ushort)minx;
		BlockMap.Add(adder);
		adder = (ushort)miny;
		BlockMap.Add(adder);
		adder = (ushort)bmapwidth;
		BlockMap.Add(adder);
		adder = (ushort)bmapheight;
		BlockMap.Add(adder);

		DynamicArray<ushort>[] BlockLists = new DynamicArray<ushort>[bmapwidth * bmapheight];
		for (int i = 0; i < BlockLists.Length; i++)
			BlockLists[i] = new DynamicArray<ushort>();

		for (line = 0; line < Level.NumLines(); ++line)
		{
			int x1 = Level.Vertices[Level.Lines[line].v1].x >> Constants.FRACBITS;
			int y1 = Level.Vertices[Level.Lines[line].v1].y >> Constants.FRACBITS;
			int x2 = Level.Vertices[Level.Lines[line].v2].x >> Constants.FRACBITS;
			int y2 = Level.Vertices[Level.Lines[line].v2].y >> Constants.FRACBITS;
			int dx = x2 - x1;
			int dy = y2 - y1;
			int bx = (x1 - minx) >> Constants.BLOCKBITS;
			int by = (y1 - miny) >> Constants.BLOCKBITS;
			int bx2 = (x2 - minx) >> Constants.BLOCKBITS;
			int by2 = (y2 - miny) >> Constants.BLOCKBITS;

			blockIndex = bx + by * bmapwidth;
			block = BlockLists[bx + by * bmapwidth];
			endBlockIndex = bx2 + by2 * bmapwidth;
			endblock = BlockLists[bx2 + by2 * bmapwidth];

			if (blockIndex == endBlockIndex) // Single block
			{
				block.Add(line);
			}
			else if (by == by2) // Horizontal line
			{
				if (bx > bx2)
				{
					SwapBlocks(ref block, ref endblock, ref blockIndex, ref endBlockIndex);
				}
				do
				{
					block.Add(line);
					block = IncrementBlock(BlockLists, ref blockIndex, 1);
				} while (blockIndex <= endBlockIndex);
			}
			else if (bx == bx2) // Vertical line
			{
				if (by > by2)
				{
					SwapBlocks(ref block, ref endblock, ref blockIndex, ref endBlockIndex);
				}
				do
				{
					block.Add(line);
					block = IncrementBlock(BlockLists, ref blockIndex, bmapwidth);
				} while (blockIndex <= endBlockIndex);
			}
			else // Diagonal line
			{
				int xchange = (dx < 0) ? -1 : 1;
				int ychange = (dy < 0) ? -1 : 1;
				int ymove = ychange * bmapwidth;
				int adx = Math.Abs(dx);
				int ady = Math.Abs(dy);

				if (adx == ady) // 45 degrees
				{
					int xb = (x1 - minx) & (Constants.BLOCKSIZE-1);
					int yb = (y1 - miny) & (Constants.BLOCKSIZE-1);
					if (dx < 0)
						xb = Constants.BLOCKSIZE - xb;
					if (dy < 0)
						yb = Constants.BLOCKSIZE - yb;
					if (xb < yb)
						adx--;
				}
				if (adx >= ady) // X-major
				{
					int yadd = dy < 0 ? -1 : Constants.BLOCKSIZE;
					do
					{
						int stop = (Util.Scale((by << Constants.BLOCKBITS) + yadd - (y1 - miny), dx, dy) + (x1 - minx)) >> Constants.BLOCKBITS;
						while (bx != stop)
						{
							block.Add(line);
							block = IncrementBlock(BlockLists, ref blockIndex, xchange);
							bx += xchange;
						}
						block.Add(line);
						block = IncrementBlock(BlockLists, ref blockIndex, ymove);
						by += ychange;
					} while (by != by2);
					while (blockIndex != endBlockIndex)
					{
						block.Add(line);
						block = IncrementBlock(BlockLists, ref blockIndex, xchange);
					}
					block.Add(line);
				}
				else // Y-major
				{
					int xadd = dx < 0 ? -1 : Constants.BLOCKSIZE;
					do
					{
						int stop = (Util.Scale((bx << Constants.BLOCKBITS) + xadd - (x1 - minx), dy, dx) + (y1 - miny)) >> Constants.BLOCKBITS;
						while (by != stop)
						{
							block.Add(line);
							block = IncrementBlock(BlockLists, ref blockIndex, ymove);
							by += ychange;
						}
						block.Add(line);
						block = IncrementBlock(BlockLists, ref blockIndex, xchange);
						bx += xchange;
					} while (bx != bx2);
					while (blockIndex != endBlockIndex)
					{
						block.Add(line);
						block = IncrementBlock(BlockLists, ref blockIndex, ymove);
					}
					block.Add(line);
				}
			}
		}

		BlockMap.Reserve(bmapwidth * bmapheight);
		CreatePackedBlockmap(BlockLists, bmapwidth, bmapheight);
	}

	private static DynamicArray<ushort> IncrementBlock(DynamicArray<ushort>[] BlockLists, ref int blockIndex, int inc)
    {
		blockIndex += inc;
		if (blockIndex < BlockLists.Length)
			return BlockLists[blockIndex];
		return null;
    }

	private static void SwapBlocks(ref DynamicArray<ushort> blocks1, ref DynamicArray<ushort> blocks2, ref int blockIndex1, ref int blockIndex2)
    {
		var tempBlock = blocks1;
		blocks1 = blocks2;
		blocks2 = tempBlock;
		var tempIndex = blockIndex1;
		blockIndex1 = blockIndex2;
		blockIndex2 = tempIndex;
	}

	private void CreateUnpackedBlockmap(List<ushort>[] blocks, int bmapwidth, int bmapheight)
	{
		List<ushort> block;
		ushort zero = 0;
		ushort terminator = 0xffff;

		for (int i = 0; i < bmapwidth * bmapheight; ++i)
		{
			BlockMap[4 + i] = (ushort)BlockMap.Length;
			BlockMap.Add(zero);
			block = blocks[i];
			for (int j = 0; j < block.Count; ++j)
				BlockMap.Add(block[j]);
			BlockMap.Add(terminator);
		}
	}

	private void CreatePackedBlockmap(DynamicArray<ushort>[] blocks, int bmapwidth, int bmapheight)
	{
		ushort[] buckets = new ushort[4096];
		ushort[] hashes;
		ushort hashblock;
		DynamicArray<ushort> block;
		ushort zero = 0;
		ushort terminator = 0xffff;
		DynamicArray<ushort> array;
		int hash;
		int hashed = 0;
		int nothashed = 0;

		hashes = new ushort[bmapwidth * bmapheight];
		for (int i = 0; i < hashes.Length; i++)
			hashes[i] = 0xffff;
		for (int i = 0; i < buckets.Length; i++)
			buckets[i] = 0xffff;

		for (int i = 0; i < bmapwidth * bmapheight; ++i)
		{
			block = blocks[i];
			hash = (int)(BlockHash(block) % 4096);
			hashblock = buckets[hash];
			while (hashblock != 0xffff)
			{
				if (BlockCompare(block, blocks[hashblock]))
					break;
				hashblock = hashes[hashblock];
			}
			if (hashblock != 0xffff)
			{
				BlockMap[4 + i] = BlockMap[4 + hashblock];
				hashed++;
			}
			else
			{
				hashes[i] = buckets[hash];
				buckets[hash] = (ushort)i;
				BlockMap[4 + i] = (ushort)BlockMap.Length;
				BlockMap.Add(zero);
				array = block;
				for (int j = 0; j < block.Length; ++j)
					BlockMap.Add(array[j]);
				BlockMap.Add(terminator);
				nothashed++;
			}
		}

		//printf ("%d blocks written, %d blocks saved\n", nothashed, hashed);
	}

	static uint BlockHash(DynamicArray<ushort> block)
	{
		int hash = 0;
		for (int i = 0; i < block.Length; ++i)
			hash = hash * 12235 + block[i];
		return (uint)(hash & 0x7fffffff);
	}

	static bool BlockCompare(DynamicArray<ushort> block1, DynamicArray<ushort> block2)
	{
		int size = block1.Length;
		if (size != block2.Length)
			return false;

		if (size == 0)
			return true;

		for (int i = 0; i < size; ++i)
		{
			if (block1[i] != block2[i])
				return false;
		}
		return true;
	}
}