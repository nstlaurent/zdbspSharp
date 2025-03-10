using System.Diagnostics;

namespace zdbspSharp;

public partial class FNodeBuilder
{
	private const int VERTEX_EPSILON = 6;
	private const int PO_LINE_START = 1;
	private const int PO_LINE_EXPLICIT = 5;
	private const float FAR_ENOUGH = 17179869184;

	public FVertexMap VertexMap;

	public DynamicArray<node_t> Nodes = new();
	public List<subsector_t> Subsectors = new();
	public List<int> SubsectorSets = new();
	public List<FPrivSeg> Segs = new();
	public List<FPrivVert> Vertices = new();
	public List<USegPtr> SegList = new();
	public DynamicArray<int> PlaneChecked = new();
	public List<FSimpleLine> Planes = new();
	public int InitialVertices; // Number of vertices in a map that are connected to linedefs

	public List<int> Touched = new();    // Loops a splitter touches on a vertex
	public List<int> Colinear = new();   // Loops with edges colinear to a splitter
	public FEventTree Events = new();      // Vertices intersected by the current splitter
	public List<FSplitSharer> SplitSharers = new();  // Segs collinear with the current splitter

	public int HackSeg;          // Seg to force to back of splitter
	public int HackMate;         // Seg to use in front of hack seg
	public FLevel Level;
	public bool GLNodes;

	// Progress meter stuff
	private int SegsStuffed;
	private readonly string MapName;
	private readonly int m_maxSegs;
	private readonly int m_splitCost;
	private readonly int m_aaPreference;

	private readonly SegCompararer _segComparer = new();
	private readonly DynamicArray<int> _usedVerticesMap = new();

	public FNodeBuilder(FLevel level, List<FPolyStart> polyspots, List<FPolyStart> anchors, string name, bool makeGLnodes,
		int maxSegs, int splitCost, int aaPreference)
	{
		m_maxSegs = maxSegs;
		m_splitCost = splitCost;
		m_aaPreference = aaPreference;
		Level = level;
		SegsStuffed = 0;
		MapName = name;
		VertexMap = new FVertexMap(this, Level.MinX, Level.MinY, Level.MaxX, Level.MaxY);
		GLNodes = makeGLnodes;

        Vertices.EnsureCapacity(Level.Vertices.Length);
		Segs.EnsureCapacity(Level.Lines.Length * 4);
		SegList.EnsureCapacity(Segs.Capacity);
        Subsectors.EnsureCapacity(Level.Sectors.Length * 4);
		SubsectorSets.EnsureCapacity(Subsectors.Capacity);
		Planes.EnsureCapacity(Level.Sectors.Length * 2);
        Nodes.EnsureCapacity(Level.Sectors.Length * 4);

        FindUsedVertices(Level.Vertices, Level.NumVertices);
		MakeSegsFromSides();
		FindPolyContainers(polyspots, anchors);
		GroupSegPlanes();
		BuildTree();
	}

	public void Dispose()
	{
	}

    //public void DumpSegs()
    //{
    //    StringBuilder sb = new StringBuilder();
    //    for (int i = 0; i < Segs.Count; i++)
    //    {
    //        var seg = Segs[i];
    //        sb.AppendLine($"{i} {seg.v1} {seg.v2} {seg.linedef} {seg.partner} {seg.sidedef}");
    //    }

    //    string test = sb.ToString();
    //    int length = test.Length;
    //}

    //void DumpGlSegs(MapSegGLEx[] segs)
    //{
    //	StringBuilder sb = new();
    //	for (int s = 0; s < segs.Length; s++)
    //	{
    //		MapSegGLEx seg = segs[s];
    //		sb.AppendLine(s.ToString());
    //		sb.AppendLine(seg.v1.ToString());
    //		sb.AppendLine(seg.v2.ToString());
    //		sb.AppendLine(seg.linedef.ToString());
    //		sb.AppendLine(seg.partner.ToString());
    //		sb.AppendLine(seg.side.ToString());
    //	}

    //	string test = sb.ToString();
    //	int length = test.Length;
    //}

    //public void DumpSegList()
    //{
    //	StringBuilder sb = new StringBuilder();
    //	for (int i = 0; i < SegList.Count; i++)
    //	{
    //		var seg = SegList[i];
    //		sb.AppendLine($"{i} {seg.SegNum}");
    //	}

    //	string test = sb.ToString();
    //	int length = test.Length;
    //}

    public unsafe void BuildTree()
	{
		int[] bbox = new int[4];

		//fprintf(stderr, "   BSP:   0.0%%\r");
		HackSeg = Constants.MAX_INT;
		HackMate = Constants.MAX_INT;
		fixed (int* bboxptr = bbox)
			CreateNode(0, Segs.Count, bboxptr, 0);
		CreateSubsectorsForReal();
		//fprintf(stderr, "   BSP: 100.0%%\n");
	}

	public unsafe uint CreateNode(int set, int count, int* bbox, int boxOffset)
	{
		node_t node = new();
		int skip;
		int selstat;
		int splitseg = 0;

		// When building GL nodes, count may not be an exact count of the number of segs
		// in this set. That's okay, because we just use it to get a skip count, so an
		// estimate is fine.
		skip = count / m_maxSegs;

		if ((selstat = SelectSplitter(set, ref node, ref splitseg, skip, true)) > 0 ||
			(skip > 0 && (selstat = SelectSplitter(set, ref node, ref splitseg, 1, true)) > 0) ||
			(selstat < 0 && (SelectSplitter(set, ref node, ref splitseg, skip, false) > 0 ||
			(skip > 0 && SelectSplitter(set, ref node, ref splitseg, 1, false) != 0))) ||
			CheckSubsector(set, ref node, ref splitseg))
		{
			// Create a normal node
			SplitSegs(set, ref node, splitseg, out int set1, out int set2, out int count1, out int count2);
			node.intchildren[0] = CreateNode(set1, count1, node.bbox, 0);
			node.intchildren[1] = CreateNode(set2, count2, node.bbox, Constants.BoxOffset);
			bbox[boxOffset + Box.BOXTOP] = Math.Max(node.bbox[Box.BOXTOP], node.bbox[Constants.BoxOffset + Box.BOXTOP]);
			bbox[boxOffset + Box.BOXBOTTOM] = Math.Min(node.bbox[Box.BOXBOTTOM], node.bbox[Constants.BoxOffset + Box.BOXBOTTOM]);
			bbox[boxOffset + Box.BOXLEFT] = Math.Min(node.bbox[Box.BOXLEFT], node.bbox[Constants.BoxOffset + Box.BOXLEFT]);
			bbox[boxOffset + Box.BOXRIGHT] = Math.Max(node.bbox[Box.BOXRIGHT], node.bbox[Constants.BoxOffset + Box.BOXRIGHT]);
			return (uint)Nodes.AddCount(node);
		}
		else
		{
			return (Constants.NFX_SUBSECTOR | CreateSubsector(set, bbox, boxOffset));
		}
	}

	bool CheckSubsector(int set, ref node_t node, ref int splitseg)
	{
		int sec = -1;
		int seg = set;

		do
		{
			//printf(" - seg %d%c(%d,%d)-(%d,%d) line %d front %d back %d\n", seg, Segs[seg].linedef == -1 ? '+' : ' ', Vertices[Segs[seg].v1].x >> 16, Vertices[Segs[seg].v1].y >> 16, Vertices[Segs[seg].v2].x >> 16, Vertices[Segs[seg].v2].y >> 16, Segs[seg].linedef, Segs[seg].frontsector, Segs[seg].backsector));
			if (Segs[seg].linedef != -1 && Segs[seg].frontsector != sec)
			{
				if (sec == -1)
					sec = Segs[seg].frontsector;
				else
					break;
			}
			seg = Segs[seg].next;
		} while (seg != Constants.MAX_INT);

		if (seg == Constants.MAX_INT)
		{
			// It's a valid non-GL subsector, and probably a valid GL subsector too.
			if (GLNodes)
				return CheckSubsectorOverlappingSegs(set, ref node, ref splitseg);
			return false;
		}

		//printf("Need to synthesize a splitter for set %d on seg %d\n", set, seg));
		splitseg = Constants.MAX_INT;

		// This is a very simple and cheap "fix" for subsectors with segs
		// from multiple sectors, and it seems ZenNode does something
		// similar. It is the only technique I could find that makes the
		// "transparent water" in nb_bmtrk.wad work properly.
		return ShoveSegBehind(set, ref node, seg, Constants.MAX_INT);
	}

	// When creating GL nodes, we need to check for segs with the same start and
	// end vertices and split them into two subsectors.

	bool CheckSubsectorOverlappingSegs(int set, ref node_t node, ref int splitseg)
	{
		int v1;
		int v2;
		int seg1;
		int seg2;

		for (seg1 = set; seg1 != Constants.MAX_INT; seg1 = Segs[seg1].next)
		{
			if (Segs[seg1].linedef == -1)
			{ // Do not check minisegs.
				continue;
			}
			v1 = Segs[seg1].v1;
			v2 = Segs[seg1].v2;
			for (seg2 = Segs[seg1].next; seg2 != Constants.MAX_INT; seg2 = Segs[seg2].next)
			{
				if (Segs[seg2].v1 == v1 && Segs[seg2].v2 == v2)
				{
					if (Segs[seg2].linedef == -1)
					{ // Do not put minisegs into a new subsector.
						int temp = seg1;
						seg1 = seg2;
						seg2 = temp;
					}
					//printf("Need to synthesize a splitter for set %d on seg %d (ov)\n", set, seg2));
					splitseg = Constants.MAX_INT;

					return ShoveSegBehind(set, ref node, seg2, seg1);
				}
			}
		}
		// It really is a good subsector.
		return false;
	}

	// The seg is marked to indicate that it should be forced to the
	// back of the splitter. Because these segs already form a convex
	// set, all the other segs will be in front of the splitter. Since
	// the splitter is formed from this seg, the back of the splitter
	// will have a one-dimensional subsector. SplitSegs() will add one
	// or two new minisegs to close it: If mate is DWORD_MAX, then a
	// new seg is created to replace this one on the front of the
	// splitter. Otherwise, mate takes its place. In either case, the
	// seg in front of the splitter is partnered with a new miniseg on
	// the back so that the back will have two segs.

	bool ShoveSegBehind(int set, ref node_t node, int seg, int mate)
	{
		SetNodeFromSeg(ref node, Segs[seg]);
		HackSeg = seg;
		HackMate = mate;
		if (!Segs[seg].planefront)
		{
			node.x += node.dx;
			node.y += node.dy;
			node.dx = -node.dx;
			node.dy = -node.dy;
		}
		return Heuristic(ref node, set, false) > 0;
	}

	void SetNodeFromSeg(ref node_t node, FPrivSeg pseg)
	{
		if (pseg.planenum >= 0)
		{
			FSimpleLine pline = Planes[pseg.planenum];
			node.x = pline.x;
			node.y = pline.y;
			node.dx = pline.dx;
			node.dy = pline.dy;
		}
		else
		{
			node.x = Vertices[pseg.v1].x;
			node.y = Vertices[pseg.v1].y;
			node.dx = Vertices[pseg.v2].x - node.x;
			node.dy = Vertices[pseg.v2].y - node.y;
		}
	}

	// Given a splitter (node), returns a score based on how "good" the resulting
	// split in a set of segs is. Higher scores are better. -1 means this splitter
	// splits something it shouldn't and will only be returned if honorNoSplit is
	// true. A score of 0 means that the splitter does not split any of the segs
	// in the set.
	int Heuristic(ref node_t node, int set, bool honorNoSplit)
	{
		// Set the initial score above 0 so that near vertex anti-weighting is less likely to produce a negative score.
		int score = 1000000;
		int segsInSet = 0;
        Span<int> counts = stackalloc int[2];
        Span<int> realSegs = stackalloc int[2];
        Span<int> specialSegs = stackalloc int[2];
        Span<int> sidev = stackalloc int[2];
        int i = set;
		int side;
		bool splitter = false;
		int max;
		int m2;
		int p;
		int q;
		double frac;

		Touched.Clear();
		Colinear.Clear();

		while (i != Constants.MAX_INT)
		{
			FPrivSeg test = Segs[i];

			if (HackSeg == i)
				side = 1;
			else
				side = ClassifyLine(node, Vertices[test.v1], Vertices[test.v2], sidev);

			switch (side)
			{
				case 0: // Seg is on only one side of the partition
				case 1:
					// If we don't split this line, but it abuts the splitter, also reject it.
					// The "right" thing to do in this case is to only reject it if there is
					// another nosplit seg from the same sector at this vertex. Note that a line
					// that lies exactly on top of the splitter is okay.
					if (test.loopnum != 0 && honorNoSplit && (sidev[0] == 0 || sidev[1] == 0))
					{
						if ((sidev[0] | sidev[1]) != 0)
						{
							max = Touched.Count;
							for (p = 0; p < max; ++p)
							{
								if (Touched[p] == test.loopnum)
									break;
							}
							if (p == max)
								Touched.Add(test.loopnum);
						}
						else
						{
							max = Colinear.Count;
							for (p = 0; p < max; ++p)
							{
								if (Colinear[p] == test.loopnum)
									break;
							}
							if (p == max)
								Colinear.Add(test.loopnum);
						}
					}

					counts[side]++;
					if (test.linedef != -1)
					{
						realSegs[side]++;
						if (test.frontsector == test.backsector)
							specialSegs[side]++;
						// Add some weight to the score for unsplit lines
						score += m_splitCost;
					}
					else
					{
						// Minisegs don't count quite as much for nosplitting
						score += m_splitCost / 4;
					}
					break;

				default: // Seg is cut by the partition
						 // If we are not allowed to split this seg, reject this splitter
					if (test.loopnum != 0)
					{
						if (honorNoSplit)
						{
							//printf("Splits seg %d\n", i));
							return -1;
						}
						else
						{
							splitter = true;
						}
					}

					// Splitters that are too close to a vertex are bad.
					frac = InterceptVector(node, test);
					if (frac < 0.001 || frac > 0.999)
					{
						FPrivVert v1 = Vertices[test.v1];
						FPrivVert v2 = Vertices[test.v2];
						double x = v1.x;
						double y = v1.y;
						x += frac * (v2.x - x);
						y += frac * (v2.y - y);
						if (Math.Abs(x - v1.x) < VERTEX_EPSILON + 1 && Math.Abs(y - v1.y) < VERTEX_EPSILON + 1)
						{
							//printf("Splitter will produce same start vertex as seg %d\n", i));
							return -1;
						}
						if (Math.Abs(x - v2.x) < VERTEX_EPSILON + 1 && Math.Abs(y - v2.y) < VERTEX_EPSILON + 1)
						{
							//printf("Splitter will produce same end vertex as seg %d\n", i));
							return -1;
						}
						if (frac > 0.999)
							frac = 1 - frac;
						int penalty = (int)(1 / frac);
						score = Math.Max(score - penalty, 1);
						//printf("Penalized splitter by %d for being near endpt of seg %d (%f).\n", penalty, i, frac));
					}

					counts[0]++;
					counts[1]++;
					if (test.linedef != -1)
					{
						realSegs[0]++;
						realSegs[1]++;
						if (test.frontsector == test.backsector)
						{
							specialSegs[0]++;
							specialSegs[1]++;
						}
					}
					break;
			}

			segsInSet++;
			i = test.next;
		}

		// If this line is outside all the others, return a special score
		if (counts[0] == 0 || counts[1] == 0)
		{
			return 0;
		}

		// A splitter must have at least one real seg on each side.
		// Otherwise, a subsector could be left without any way to easily
		// determine which sector it lies inside.
		if (realSegs[0] == 0 || realSegs[1] == 0)
		{
			//printf("Leaves a side with only mini segs\n"));
			return -1;
		}

		// Try to avoid splits that leave only "special" segs, so that the generated
		// subsectors have a better chance of choosing the correct sector. This situation
		// is not neccesarily bad, just undesirable.
		if (honorNoSplit && (specialSegs[0] == realSegs[0] || specialSegs[1] == realSegs[1]))
		{
			//printf("Leaves a side with only special segs\n"));
			return -1;
		}

		// If this splitter intersects any vertices of segs that should not be split,
		// check if it is also colinear with another seg from the same sector. If it
		// is, the splitter is okay. If not, it should be rejected. Why? Assuming that
		// polyobject containers are convex (which they should be), a splitter that
		// is colinear with one of the sector's segs and crosses the vertex of another
		// seg of that sector must be crossing the container's corner and does not
		// actually split the container.

		max = Touched.Count;
		m2 = Colinear.Count;

		// If honorNoSplit is false, then both these lists will be empty.

		// If the splitter touches some vertices without being colinear to any, we
		// can skip further checks and reject this right away.
		if (m2 == 0 && max > 0)
		{
			return -1;
		}

		for (p = 0; p < max; ++p)
		{
			int look = Touched[p];
			for (q = 0; q < m2; ++q)
			{
				if (look == Colinear[q])
					break;
			}
			// Not a good one
			if (q == m2)
				return -1;
		}

		// Doom maps are primarily axis-aligned lines, so it's usually a good
		// idea to prefer axis-aligned splitters over diagonal ones. Doom originally
		// had special-casing for orthogonal lines, so they performed better. ZDoom
		// does not care about the line's direction, so this is merely a choice to
		// try and improve the final tree.

		if ((node.dx == 0) || (node.dy == 0))
		{
			// If we have to split a seg we would prefer to keep unsplit, give
			// extra precedence to orthogonal lines so that the polyobjects
			// outside the entrance to MAP06 in Hexen MAP02 display properly.
			if (splitter)
			{
				score += segsInSet * 8;
			}
			else
			{
				score += segsInSet / m_aaPreference;
			}
		}

		score += (counts[0] + counts[1]) - Math.Abs(counts[0] - counts[1]);

		return score;
	}

	double InterceptVector(node_t splitter, FPrivSeg seg)
	{
		double v2x = Vertices[seg.v1].x;
		double v2y = Vertices[seg.v1].y;
		double v2dx = Vertices[seg.v2].x - v2x;
		double v2dy = Vertices[seg.v2].y - v2y;
		double v1dx = splitter.dx;
		double v1dy = splitter.dy;

		double den = v1dy * v2dx - v1dx * v2dy;

		if (den == 0.0)
		{
			return 0; // parallel
		}

		double v1x = (double)splitter.x;
		double v1y = (double)splitter.y;

		double num = (v1x - v2x) * v1dy + (v2y - v1y) * v1dx;
		return num / den;
	}

	int SelectSplitter(int set, ref node_t node, ref int splitseg, int step, bool nosplit)
	{
		int stepleft = 0;
		int bestvalue = 0;
		int bestseg = Constants.MAX_INT;
		int seg = set;
		bool nosplitters = false;

		Util.ZeroArray(PlaneChecked.Data, PlaneChecked.Length);

		while (seg != Constants.MAX_INT)
		{
			FPrivSeg pseg = Segs[seg];

			if (--stepleft <= 0)
			{
				int l = pseg.planenum >> 3;
				int r = 1 << (pseg.planenum & 7);

				if (l < 0 || (PlaneChecked[l] & r) == 0)
				{
					if (l >= 0)
						PlaneChecked[l] |= r;

					stepleft = step;
					SetNodeFromSeg(ref node, pseg);

					int value = Heuristic(ref node, set, nosplit);
					//printf("Seg %5d, ld %d (%5d,%5d)-(%5d,%5d) scores %d\n", seg, Segs[seg].linedef, node.x >> 16, node.y >> 16, (node.x + node.dx) >> 16, (node.y + node.dy) >> 16, value);

					if (value > bestvalue)
					{
						bestvalue = value;
						bestseg = seg;
					}
					else if (value < 0)
					{
						nosplitters = true;
					}
				}
			}

			seg = pseg.next;
		}

		if (bestseg == Constants.MAX_INT)
		{ // No lines split any others into two sets, so this is a convex region.
		  //printf("set %d, step %d, nosplit %d has no good splitter (%d)\n", set, step, nosplit, nosplitters);
			return nosplitters ? -1 : 0;
		}

		//printf("split seg %u in set %u, score %d, step %d, nosplit %d\n", bestseg, set, bestvalue, step, nosplit);

		splitseg = bestseg;
		SetNodeFromSeg(ref node, Segs[bestseg]);
		return 1;
	}

	void SplitSegs(int set, ref node_t node, int splitseg, out int outset0, out int outset1, out int count0, out int count1)
	{
		Span<int> sidev = stackalloc int[2];
		int _count0 = 0;
		int _count1 = 0;
		outset0 = Constants.MAX_INT;
		outset1 = Constants.MAX_INT;

		Events.DeleteAll();
		SplitSharers.Clear();

		while (set != Constants.MAX_INT)
		{
			bool hack;
			FPrivSeg seg = Segs[set];
			int next = seg.next;			
			int side;

			if (HackSeg == set)
			{
				HackSeg = Constants.MAX_INT;
				side = 1;
				sidev[0] = sidev[1] = 0;
				hack = true;
			}
			else
			{
				side = ClassifyLine(node, Vertices[seg.v1], Vertices[seg.v2], sidev);
				hack = false;
			}

			switch (side)
			{
				case 0: // seg is entirely in front
					seg.next = outset0;
					//Printf ("%u in front\n", set);
					outset0 = set;
					_count0++;
					break;

				case 1: // seg is entirely in back
					seg.next = outset1;
					//Printf ("%u in back\n", set);
					outset1 = set;
					_count1++;
					break;

				default: // seg needs to be split
					double frac;
					FPrivVert newvert = new FPrivVert();
					int vertnum;
					int seg2;

					//Printf ("%u is cut\n", set);
					if (seg.loopnum != 0)
					{
						//printf("   Split seg %lu (%d,%d)-(%d,%d) of sector %d on line %d\n", (uint)set, Vertices[seg.v1].x >> 16, Vertices[seg.v1].y >> 16, Vertices[seg.v2].x >> 16, Vertices[seg.v2].y >> 16, seg.frontsector, seg.linedef);
					}

					frac = InterceptVector(node, seg);
					newvert.x = Vertices[seg.v1].x;
					newvert.y = Vertices[seg.v1].y;
					newvert.x += (int)(frac * (double)(Vertices[seg.v2].x - newvert.x));
					newvert.y += (int)(frac * (double)(Vertices[seg.v2].y - newvert.y));
					newvert.index = 0;
					vertnum = VertexMap.SelectVertexClose(newvert);

					if ((int)vertnum == seg.v1 || (int)vertnum == seg.v2)
					{
						//printf("SelectVertexClose selected endpoint of seg %u\n", (uint)set);
					}

					seg2 = SplitSeg(set, vertnum, sidev[0]);

					Segs[seg2].next = outset0;
					outset0 = seg2;
					Segs[set].next = outset1;
					outset1 = set;
					_count0++;
					_count1++;

					// Also split the seg on the back side
					if (Segs[set].partner != Constants.MAX_INT)
					{
						int partner1 = Segs[set].partner;
						int partner2 = SplitSeg(partner1, vertnum, sidev[1]);
						// The newly created seg stays in the same set as the
						// back seg because it has not been considered for splitting
						// yet. If it had been, then the front seg would have already
						// been split, and we would not be in this default case.
						// Moreover, the back seg may not even be in the set being
						// split, so we must not move its pieces into the out sets.
						Segs[partner1].next = partner2;
						Segs[partner2].partner = seg2;
						Segs[seg2].partner = partner2;

                        Debug.Assert(Segs[partner2].v1 == Segs[seg2].v2);
                        Debug.Assert(Segs[partner2].v2 == Segs[seg2].v1);
                        Debug.Assert(Segs[partner1].v1 == Segs[set].v2);
                        Debug.Assert(Segs[partner1].v2 == Segs[set].v1);
                    }

					if (GLNodes)
					{
						AddIntersection(node, vertnum);
					}

					break;
			}
			if (side >= 0 && GLNodes)
			{
				if (sidev[0] == 0)
				{
					double dist1 = AddIntersection(node, seg.v1);
					if (sidev[1] == 0)
					{
						double dist2 = AddIntersection(node, seg.v2);
						FSplitSharer share = new(dist1, set, dist2 > dist1);
						SplitSharers.Add(share);
					}
				}
				else if (sidev[1] == 0)
				{
					AddIntersection(node, seg.v2);
				}
			}
			if (hack && GLNodes)
			{
				int newback;
				int newfront;

				newback = AddMiniseg(seg.v2, seg.v1, Constants.MAX_INT, set, splitseg);
				if (HackMate == Constants.MAX_INT)
				{
					newfront = AddMiniseg(Segs[set].v1, Segs[set].v2, newback, set, splitseg);
					Segs[newfront].next = outset0;
					outset0 = newfront;
				}
				else
				{
					newfront = HackMate;
					Segs[newfront].partner = newback;
					Segs[newback].partner = newfront;
				}
				Segs[newback].frontsector = Segs[newback].backsector = Segs[newfront].frontsector = Segs[newfront].backsector = Segs[set].frontsector;

				Segs[newback].next = outset1;
				outset1 = newback;
			}
			set = next;
		}
		FixSplitSharers();
		if (GLNodes)
		{
			AddMinisegs(node, splitseg, ref outset0, ref outset1);
		}
		count0 = _count0;
		count1 = _count1;
	}
	
	public unsafe void GetGLNodes(out MapNodeEx[] outNodes, out MapSegGLEx[] outSegs, out MapSubsectorEx[] outSubs)
	{
		DynamicArray<MapSegGLEx> segs = new(Segs.Count * 5 / 4);

		outNodes = new MapNodeEx[Nodes.Length];
		for (int i = 0; i < Nodes.Length; ++i)
		{
			node_t orgnode = Nodes[i];
			fixed (MapNodeEx* newnode = &outNodes[i])
			{
				newnode->x = orgnode.x;
				newnode->y = orgnode.y;
				newnode->dx = orgnode.dx;
				newnode->dy = orgnode.dy;

				for (int j = 0; j < 2 * 4; j++)
					newnode->bbox[j] = (short)(orgnode.bbox[j] >> Constants.FRACBITS);

				for (int j = 0; j < 2; j++)
					newnode->children[j] = orgnode.intchildren[j];
			}
		}

		outSubs = new MapSubsectorEx[Subsectors.Count];
		for (int i = 0; i < Subsectors.Count; ++i)
		{
			int numsegs = CloseSubsector(segs, i);
			outSubs[i].numlines = numsegs;
			outSubs[i].firstline = segs.Length - numsegs;
		}

		outSegs = new MapSegGLEx[segs.Length];

		for (int i = 0; i < segs.Length; i++)
			outSegs[i] = segs[i];

		for (int i = 0; i < segs.Length; ++i)
		{
			if (outSegs[i].partner != Constants.MAX_INT)
				outSegs[i].partner = Segs[outSegs[i].partner].storedseg;
		}
	}

    int CloseSubsector(DynamicArray<MapSegGLEx> segs, int subsector)
	{
		FPrivSeg seg;
		FPrivSeg prev;
		uint prevAngle;
		double accumx = 0;
		double accumy = 0;
		int midx;
		int midy;
		int first = Subsectors[subsector].firstline;
		int max = first + Subsectors[subsector].numlines;
		int count = 0;
		int firstVert;
		bool diffplanes = false;
		int firstplane = Segs[SegList[first].SegNum].planenum;

		// Calculate the midpoint of the subsector and also check for degenerate subsectors.
		// A subsector is degenerate if it exists in only one dimension, which can be
		// detected when all the segs lie in the same plane. This can happen if you have
		// outward-facing lines in the void that don't point toward any sector. (Some of the
		// polyobjects in Hexen are constructed like this.)
		for (int i = first; i < max; ++i)
		{
			seg = Segs[SegList[i].SegNum];
			accumx += Vertices[seg.v1].x + Vertices[seg.v2].x;
			accumy += Vertices[seg.v1].y + Vertices[seg.v2].y;
			if (firstplane != seg.planenum)
				diffplanes = true;
		}

		midx = (int)(accumx / (max - first) / 2);
		midy = (int)(accumy / (max - first) / 2);

		seg = Segs[SegList[first].SegNum];
		prevAngle = Util.PointToAngle(Vertices[seg.v1].x - midx, Vertices[seg.v1].y - midy);
		seg.storedseg = PushGLSeg(segs, seg);
		count = 1;
		prev = seg;
		firstVert = seg.v1;

		if (diffplanes)
		{ // A well-behaved subsector. Output the segs sorted by the angle formed by connecting
		  // the subsector's center to their first vertex.
		  // //printf("Well behaved subsector\n"));
			for (int i = first + 1; i < max; ++i)
			{
				uint bestdiff = Constants.ANGLE_MAX;
				FPrivSeg? bestseg = null;
				int bestj = -1;
				for (int j = first; j < max; ++j)
				{
					seg = Segs[SegList[j].SegNum];
					uint ang = Util.PointToAngle(Vertices[seg.v1].x - midx, Vertices[seg.v1].y - midy);
					uint diff = prevAngle - ang;
					if (seg.v1 == prev.v2)
					{
						bestdiff = diff;
						bestseg = seg;
						bestj = j;
						break;
					}
					if (diff < bestdiff && diff > 0)
					{
						bestdiff = diff;
						bestseg = seg;
						bestj = j;
					}
				}
				if (bestseg != null)
					seg = bestseg;

				if (prev.v2 != seg.v1)
				{
					// Add a new miniseg to connect the two segs
					PushConnectingGLSeg(subsector, segs, prev.v2, seg.v1);
					count++;
				}
				prevAngle -= bestdiff;
				seg.storedseg = PushGLSeg(segs, seg);
				count++;
				prev = seg;
				if (seg.v2 == firstVert)
				{
					prev = seg;
					break;
				}
			}
		}
		else
		{ // A degenerate subsector. These are handled in three stages:
		  // Stage 1. Proceed in the same direction as the start seg until we
		  //          hit the seg furthest from it.
		  // Stage 2. Reverse direction and proceed until we hit the seg
		  //          furthest from the start seg.
		  // Stage 3. Reverse direction again and insert segs until we get
		  //          to the start seg.
		  // A dot product serves to determine distance from the start seg.
		  // 
		  //printf("degenerate subsector\n"));

			// Stage 1. Go forward.
			count += OutputDegenerateSubsector(segs, subsector, true, 0, ref prev);

			// Stage 2. Go backward.
			count += OutputDegenerateSubsector(segs, subsector, false, double.MaxValue, ref prev);

			// Stage 3. Go forward again.
			count += OutputDegenerateSubsector(segs, subsector, true, -double.MaxValue, ref prev);
		}

		if (prev.v2 != firstVert)
		{
			PushConnectingGLSeg(subsector, segs, prev.v2, firstVert);
			count++;
		}

		return count;
	}

	int OutputDegenerateSubsector(DynamicArray<MapSegGLEx> segs, int subsector, bool bForward, double lastdot, ref FPrivSeg prev)
	{
		double[] bestinit = { -double.MaxValue, double.MaxValue };
		FPrivSeg seg;
		int first;
		int max;
		int count;
		double dot;
		double x1;
		double y1;
		double dx;
		double dy;
		double dx2;
		double dy2;
		bool wantside;

		first = Subsectors[subsector].firstline;
		max = first + Subsectors[subsector].numlines;
		count = 0;

		seg = Segs[SegList[first].SegNum];
		x1 = Vertices[seg.v1].x;
		y1 = Vertices[seg.v1].y;
		dx = Vertices[seg.v2].x - x1;
		dy = Vertices[seg.v2].y - y1;
		int iForward = bForward ? 1 : 0;
		wantside = ((seg.planefront ? 1 : 0) ^ (bForward ? 0 : 1)) != 0;

		for (int i = first + 1; i < max; ++i)
		{
			double bestdot = bestinit[iForward];
			FPrivSeg? bestseg = null;
			for (int j = first + 1; j < max; ++j)
			{
				seg = Segs[SegList[j].SegNum];
				if (seg.planefront != wantside)
					continue;

				dx2 = Vertices[seg.v1].x - x1;
				dy2 = Vertices[seg.v1].y - y1;
				dot = dx * dx2 + dy * dy2;

				if (bForward)
				{
					if (dot < bestdot && dot > lastdot)
					{
						bestdot = dot;
						bestseg = seg;
					}
				}
				else
				{
					if (dot > bestdot && dot < lastdot)
					{
						bestdot = dot;
						bestseg = seg;
					}
				}
			}
			if (bestseg != null)
			{
				if (prev.v2 != bestseg.v1)
				{
					PushConnectingGLSeg(subsector, segs, prev.v2, bestseg.v1);
					count++;
				}
				seg.storedseg = PushGLSeg(segs, bestseg);
				count++;
				prev = bestseg;
				lastdot = bestdot;
			}
		}
		return count;
	}

	unsafe int PushGLSeg(DynamicArray<MapSegGLEx> segs, FPrivSeg seg)
	{
		MapSegGLEx newseg = new();

		newseg.v1 = seg.v1;
		newseg.v2 = seg.v2;
		newseg.linedef = seg.linedef;

		// Just checking the sidedef to determine the side is insufficient.
		// When a level is sidedef compressed both sides may well have the same sidedef.

		if (newseg.linedef != Constants.MAX_INT)
		{
			IntLineDef ld = Level.Lines[newseg.linedef];

			if (ld.sidenum[0] == ld.sidenum[1])
			{
				// When both sidedefs are the same a quick check doesn't work so this
				// has to be done by comparing the distances of the seg's end point to
				// the line's start.
				WideVertex lv1 = Level.Vertices[ld.v1];
				WideVertex sv1 = Level.Vertices[seg.v1];
				WideVertex sv2 = Level.Vertices[seg.v2];

				double dist1sq = (double)(sv1.x - lv1.x) * (sv1.x - lv1.x) + (double)(sv1.y - lv1.y) * (sv1.y - lv1.y);
				double dist2sq = (double)(sv2.x - lv1.x) * (sv2.x - lv1.x) + (double)(sv2.y - lv1.y) * (sv2.y - lv1.y);

				newseg.side = (ushort)(dist1sq < dist2sq ? 0 : 1);
			}
			else
			{
				newseg.side = (ushort)(ld.sidenum[1] == seg.sidedef ? 1 : 0);
			}
		}
		else
		{
			newseg.side = 0;
		}

		newseg.partner = seg.partner;
		return segs.AddCount(newseg);
	}

	void PushConnectingGLSeg(int subsector, DynamicArray<MapSegGLEx> segs, int v1, int v2)
	{
		MapSegGLEx newseg = new();

		//Warn("Unclosed subsector %d, from (%d,%d) to (%d,%d)\n", subsector, Vertices[v1].x >> DefineConstants.FRACBITS, Vertices[v1].y >> DefineConstants.FRACBITS, Vertices[v2].x >> DefineConstants.FRACBITS, Vertices[v2].y >> DefineConstants.FRACBITS);

		newseg.v1 = v1;
		newseg.v2 = v2;
		newseg.linedef = Constants.MAX_INT;
		newseg.side = 0;
		newseg.partner = Constants.MAX_INT;
		segs.Add(newseg);
	}

	private static readonly FEventInfo DefaultEventInfo = new() { Vertex = -1, FrontSeg = Constants.MAX_INT };

	public double AddIntersection(in node_t node, int vertex)
	{
		// Calculate signed distance of intersection vertex from start of splitter.
		// Only ordering is important, so we don't need a sqrt.
		FPrivVert v = Vertices[vertex];
		double dist = ((double)v.x - node.x) * (node.dx) + ((double)v.y - node.y) * (node.dy);

		FEvent fevent = Events.FindEvent(dist);

		if (fevent == null)
		{
			fevent = Events.GetNewNode();
			fevent.Distance = dist;
			fevent.Info.FrontSeg = DefaultEventInfo.FrontSeg;
			fevent.Info.Vertex = DefaultEventInfo.Vertex;
			fevent.Info.Vertex = vertex;
			Events.Insert(fevent);
		}

		return dist;
	}

	public void FixSplitSharers()
	{	
		//printf("events:\n"));
		//Events.PrintTree());
		for (int i = 0; i < SplitSharers.Count; ++i)
		{
			int seg = SplitSharers[i].Seg;
			int v2 = Segs[seg].v2;
			FEvent? fevent = Events.FindEvent(SplitSharers[i].Distance);
			FEvent? next;

			// Should not happen
			if (fevent == null)
				continue;
			
			//printf("Considering events on seg %d(%d[%d,%d]->%d[%d,%d]) [%g:%g]\n", seg, Segs[seg].v1, Vertices[Segs[seg].v1].x >> 16, Vertices[Segs[seg].v1].y >> 16, Segs[seg].v2, Vertices[Segs[seg].v2].x >> 16, Vertices[Segs[seg].v2].y >> 16, SplitSharers[i].Distance, event.Distance));

			if (SplitSharers[i].Forward)
			{
				fevent = Events.GetSuccessor(fevent);
				if (fevent == null)
					continue;
				next = Events.GetSuccessor(fevent);
			}
			else
			{
				fevent = Events.GetPredecessor(fevent);
				if (fevent == null)
					continue;
				next = Events.GetPredecessor(fevent);
			}

			while (fevent != null && next != null && fevent.Info.Vertex != v2)
			{
				//printf("Forced split of seg %d(%d[%d,%d]->%d[%d,%d]) at %d(%d,%d):%g\n", seg, Segs[seg].v1, Vertices[Segs[seg].v1].x >> 16, Vertices[Segs[seg].v1].y >> 16, Segs[seg].v2, Vertices[Segs[seg].v2].x >> 16, Vertices[Segs[seg].v2].y >> 16, @event.Info.Vertex, Vertices[@event.Info.Vertex].x >> 16, Vertices[@event.Info.Vertex].y >> 16, @event.Distance));

				int newseg = SplitSeg(seg, fevent.Info.Vertex, 1);

				Segs[newseg].next = Segs[seg].next;
				Segs[seg].next = newseg;

				int partner = Segs[seg].partner;
				if (partner != Constants.MAX_INT)
				{
					int endpartner = SplitSeg(partner, fevent.Info.Vertex, 1);

					Segs[endpartner].next = Segs[partner].next;
					Segs[partner].next = endpartner;

					Segs[seg].partner = endpartner;
					Segs[partner].partner = newseg;

                    Debug.Assert(Segs[Segs[seg].partner].partner == seg);
                    Debug.Assert(Segs[Segs[newseg].partner].partner == newseg);
                    Debug.Assert(Segs[seg].v1 == Segs[endpartner].v2);
                    Debug.Assert(Segs[seg].v2 == Segs[endpartner].v1);
                    Debug.Assert(Segs[partner].v1 == Segs[newseg].v2);
                    Debug.Assert(Segs[partner].v2 == Segs[newseg].v1);
                }

				seg = newseg;
				if (SplitSharers[i].Forward)
				{
					fevent = next;
					next = Events.GetSuccessor(next);
				}
				else
				{
					fevent = next;
					next = Events.GetPredecessor(next);
				}
			}
		}
	}

	public unsafe void FindUsedVertices(WideVertex[] oldverts, int max)
	{
		_usedVerticesMap.Reserve(max);
		for (int i = 0; i < max; i++)
			_usedVerticesMap[i] = -1;

		for (int i = 0; i < Level.NumLines(); ++i)
		{
			fixed (IntLineDef* line = &Level.Lines.Data[i])
			{
				int v1 = line->v1;
				int v2 = line->v2;

				if (_usedVerticesMap[v1] == -1)
				{
					FPrivVert newvert = new FPrivVert();
					newvert.x = oldverts[v1].x;
					newvert.y = oldverts[v1].y;
					newvert.index = oldverts[v1].index;
					_usedVerticesMap[v1] = VertexMap.SelectVertexExact(newvert);
				}
				if (_usedVerticesMap[v2] == -1)
				{
					FPrivVert newvert = new FPrivVert();
					newvert.x = oldverts[v2].x;
					newvert.y = oldverts[v2].y;
					newvert.index = oldverts[v2].index;
					_usedVerticesMap[v2] = VertexMap.SelectVertexExact(newvert);
				}

				line->v1 = _usedVerticesMap[v1];
				line->v2 = _usedVerticesMap[v2];
			}
		}
		InitialVertices = Vertices.Count;
		Level.NumOrgVerts = InitialVertices;
		_usedVerticesMap.Clear();
	}

	public unsafe void MakeSegsFromSides()
	{
		int j;

		for (int i = 0; i < Level.NumLines(); ++i)
		{
			fixed (IntLineDef* line = &Level.Lines.Data[i])
			{
				if (line->sidenum[0] != Constants.MAX_INT)
					CreateSeg(i, 0);
				//else
					//printf("Linedef %d does not have a front side.\n", i);

				if (line->sidenum[1] != Constants.MAX_INT)
				{
					j = CreateSeg(i, 1);
					if (line->sidenum[0] != Constants.MAX_INT)
					{
						Segs[j - 1].partner = j;
						Segs[j].partner = j - 1;
					}
				}
			}
		}
	}

	int SplitSeg(int segnum, int splitvert, int v1InFront)
	{
		double dx;
		double dy;
		int newnum = Segs.Count;

		FPrivSeg newseg = new(Segs[segnum]);
		dx = Vertices[splitvert].x - Vertices[newseg.v1].x;
		dy = Vertices[splitvert].y - Vertices[newseg.v1].y;
		if (v1InFront > 0)
		{
			newseg.offset += (int)(Math.Sqrt(dx * dx + dy * dy));

			newseg.v1 = splitvert;
			Segs[segnum].v2 = splitvert;

			RemoveSegFromVert2(segnum, newseg.v2);

			newseg.nextforvert = Vertices[splitvert].segs;
			Vertices[splitvert].segs = newnum;

			newseg.nextforvert2 = Vertices[newseg.v2].segs2;
			Vertices[newseg.v2].segs2 = newnum;

			Segs[segnum].nextforvert2 = Vertices[splitvert].segs2;
			Vertices[splitvert].segs2 = segnum;
		}
		else
		{
			Segs[segnum].offset += (int)(Math.Sqrt(dx * dx + dy * dy));

			Segs[segnum].v1 = splitvert;
			newseg.v2 = splitvert;

			RemoveSegFromVert1(segnum, newseg.v1);

			newseg.nextforvert = Vertices[newseg.v1].segs;
			Vertices[newseg.v1].segs = newnum;

			newseg.nextforvert2 = Vertices[splitvert].segs2;
			Vertices[splitvert].segs2 = newnum;

			Segs[segnum].nextforvert = Vertices[splitvert].segs;
			Vertices[splitvert].segs = segnum;
		}

		Segs.Add(newseg);

		//printf("Split seg %d to get seg %d\n", segnum, newnum));

		return newnum;
	}

	void RemoveSegFromVert1(int segnum, int vertnum)
	{
		FPrivVert v = Vertices[vertnum];

		if (v.segs == segnum)
		{
			v.segs = Segs[segnum].nextforvert;
		}
		else
		{
			int prev = 0;
			int curr = v.segs;
			while (curr != Constants.MAX_INT && curr != segnum)
			{
				prev = curr;
				curr = Segs[curr].nextforvert;
			}
			if (curr == segnum)
			{
				Segs[prev].nextforvert = Segs[curr].nextforvert;
			}
		}
	}

	void RemoveSegFromVert2(int segnum, int vertnum)
	{
		FPrivVert v = Vertices[vertnum];

		if (v.segs2 == segnum)
		{
			v.segs2 = Segs[segnum].nextforvert2;
		}
		else
		{
			int prev = 0;
			int curr = v.segs2;
			while (curr != Constants.MAX_INT && curr != segnum)
			{
				prev = curr;
				curr = Segs[curr].nextforvert2;
			}
			if (curr == segnum)
			{
				Segs[prev].nextforvert2 = Segs[curr].nextforvert2;
			}
		}
	}

	public unsafe int CreateSeg(int linenum, int sidenum)
	{
		FPrivSeg seg = new();
		int backside;
		int segnum;

		seg.next = Constants.MAX_INT;
		seg.loopnum = 0;
		seg.offset = 0;
		seg.partner = Constants.MAX_INT;
		seg.hashnext = null;
		seg.planefront = false;
		seg.planenum = Constants.MAX_INT;
		seg.storedseg = Constants.MAX_INT;

		fixed (IntLineDef* line = &Level.Lines.Data[linenum])
		{
			if (sidenum == 0)
			{ // front
				seg.v1 = line->v1;
				seg.v2 = line->v2;
			}
			else
			{ // back
				seg.v2 = line->v1;
				seg.v1 = line->v2;
			}
			seg.linedef = linenum;
			seg.sidedef = line->sidenum[sidenum];
			backside = line->sidenum[sidenum == 0 ? 1 : 0];
			seg.frontsector = Level.Sides[seg.sidedef].sector;
			seg.backsector = backside != Constants.MAX_INT ? Level.Sides[backside].sector : -1;
			seg.nextforvert = Vertices[seg.v1].segs;
			seg.nextforvert2 = Vertices[seg.v2].segs2;
			seg.angle = Util.PointToAngle(Vertices[seg.v2].x - Vertices[seg.v1].x, Vertices[seg.v2].y - Vertices[seg.v1].y);

			segnum = Segs.AddCount(seg);
			Vertices[seg.v1].segs = segnum;
			Vertices[seg.v2].segs2 = segnum;
			//printf("Seg %4d: From line %d, side %s (%5d,%5d)-(%5d,%5d)  [%08x,%08x]-[%08x,%08x]\n", segnum, linenum, sidenum != 0 ? "back " : "front", Vertices[seg.v1].x >> 16, Vertices[seg.v1].y >> 16, Vertices[seg.v2].x >> 16, Vertices[seg.v2].y >> 16, Vertices[seg.v1].x, Vertices[seg.v1].y, Vertices[seg.v2].x, Vertices[seg.v2].y));
		}

		return segnum;
	}

	void GroupSegPlanes()
	{
		const int bucketbits = 12;
		FPrivSeg[] buckets = new FPrivSeg[1 << bucketbits];
		int i;
		int planenum;

		for (i = 0; i < Segs.Count; ++i)
		{
			FPrivSeg seg = Segs[i];
			seg.next = i + 1;
			seg.hashnext = null;
		}

		Segs[^1].next = Constants.MAX_INT;

		for (i = planenum = 0; i < Segs.Count; ++i)
		{
			FPrivSeg seg = Segs[i];
			int x1 = Vertices[seg.v1].x;
			int y1 = Vertices[seg.v1].y;
			int x2 = Vertices[seg.v2].x;
			int y2 = Vertices[seg.v2].y;
			uint ang = Util.PointToAngle(x2 - x1, y2 - y1);

			if (ang >= 1u << 31)
				ang += 1u << 31;

			FPrivSeg? check = buckets[ang >>= 31 - bucketbits];
			while (check != null)
			{
				int cx1 = Vertices[check.v1].x;
				int cy1 = Vertices[check.v1].y;
				int cdx = Vertices[check.v2].x - cx1;
				int cdy = Vertices[check.v2].y - cy1;
				if (Util.PointOnSide(x1, y1, cx1, cy1, cdx, cdy) == 0 && Util.PointOnSide(x2, y2, cx1, cy1, cdx, cdy) == 0)
					break;
				check = check.hashnext;
			}
			if (check != null)
			{
				seg.planenum = check.planenum;
				FSimpleLine line = Planes[seg.planenum];
				if (line.dx != 0)
				{
					if ((line.dx > 0 && x2 > x1) || (line.dx < 0 && x2 < x1))
						seg.planefront = true;
					else
						seg.planefront = false;
				}
				else
				{
					if ((line.dy > 0 && y2 > y1) || (line.dy < 0 && y2 < y1))
						seg.planefront = true;
					else
						seg.planefront = false;
				}
			}
			else
			{
				seg.hashnext = buckets[ang];
				buckets[ang] = seg;
				seg.planenum = planenum++;
				seg.planefront = true;

				FSimpleLine pline = new(Vertices[seg.v1].x, Vertices[seg.v1].y, Vertices[seg.v2].x - Vertices[seg.v1].x, Vertices[seg.v2].y - Vertices[seg.v1].y);
				Planes.Add(pline);
			}
		}
		
		//printf("%d planes from %d segs\n", planenum, Segs.Size()));

		PlaneChecked.Reserve((planenum + 7) / 8);
	}

	// Find "loops" of segs surrounding polyobject's origin. Note that a polyobject's origin
	// is not solely defined by the polyobject's anchor, but also by the polyobject itself.
	// For the split avoidance to work properly, you must have a convex, complete loop of
	// segs surrounding the polyobject origin. All the maps in hexen.wad have complete loops of
	// segs around their polyobjects, but they are not all convex: The doors at the start of MAP01
	// and some of the pillars in MAP02 that surround the entrance to MAP06 are not convex.
	// Heuristic() uses some special weighting to make these cases work properly.
	unsafe void FindPolyContainers(List<FPolyStart> spots, List<FPolyStart> anchors)
	{
		int loop = 1;
		Span<int> bbox = stackalloc int[4];

		for (int i = 0; i < spots.Count; ++i)
		{
			FPolyStart spot = spots[i];
			fixed (int* bboxptr = bbox)
			if (GetPolyExtents(spot.polynum, bboxptr, 0))
			{
				FPolyStart anchor = anchors[0];
				int jAnchor;

				for (jAnchor = 0; jAnchor < anchors.Count; ++jAnchor)
				{
					anchor = anchors[jAnchor];
					if (anchor.polynum == spot.polynum)
						break;
				}

				if (jAnchor < anchors.Count)
				{
					vertex_t mid = new();
					vertex_t center = new();

					mid.x = bbox[Box.BOXLEFT] + (bbox[Box.BOXRIGHT] - bbox[Box.BOXLEFT]) / 2;
					mid.y = bbox[Box.BOXBOTTOM] + (bbox[Box.BOXTOP] - bbox[Box.BOXBOTTOM]) / 2;

					center.x = mid.x - anchor.x + spot.x;
					center.y = mid.y - anchor.y + spot.y;

					// Scan right for the seg closest to the polyobject's center after it
					// gets moved to its start spot.
					int closestdist = Constants.MAX_INT;
					int closestseg = 0;

					//Printf("start %d,%d -- center %d, %d\n", spot.x >> 16, spot.y >> 16, center.x >> 16, center.y >> 16));

					for (int j = 0; j < Segs.Count; ++j)
					{
						FPrivSeg seg = Segs[j];
						FPrivVert v1 = Vertices[seg.v1];
						FPrivVert v2 = Vertices[seg.v2];
						int dy = v2.y - v1.y;

						if (dy == 0)
						{ // Horizontal, so skip it
							continue;
						}
						if ((v1.y < center.y && v2.y < center.y) || (v1.y > center.y && v2.y > center.y))
						{ // Not crossed
							continue;
						}

						int dx = v2.x - v1.x;

						if (Util.PointOnSide(center.x, center.y, v1.x, v1.y, dx, dy) <= 0)
						{
							int t = Util.DivScale30(center.y - v1.y, dy);
							int sx = v1.x + Util.MulScale30(dx, t);
							int dist = sx - spot.x;

							if (dist < closestdist && dist >= 0)
							{
								closestdist = dist;
								closestseg = j;
							}
						}
					}
					if (closestdist != Constants.MAX_INT)
					{
						loop = MarkLoop(closestseg, loop);
						//Printf("Found polyobj in sector %d (loop %d)\n", Segs[closestseg].frontsector, Segs[closestseg].loopnum));
					}
				}
			}
		}
	}

	int MarkLoop(int firstseg, int loopnum)
	{
		int seg = firstseg;
		int sec = Segs[firstseg].frontsector;

		// already marked
		if (Segs[firstseg].loopnum != 0)
			return loopnum;

		do
		{
			FPrivSeg s1 = Segs[seg];

			s1.loopnum = loopnum;
			//Printf("Mark seg %d (%d,%d)-(%d,%d)\n", seg, Vertices[s1.v1].x >> 16, Vertices[s1.v1].y >> 16, Vertices[s1.v2].x >> 16, Vertices[s1.v2].y >> 16));

			int bestseg = Constants.MAX_INT;
			int tryseg = Vertices[s1.v2].segs;
			uint bestang = Constants.ANGLE_MAX;
			uint ang1 = s1.angle;

			while (tryseg != Constants.MAX_INT)
			{
				FPrivSeg s2 = Segs[tryseg];

				if (s2.frontsector == sec)
				{
					uint ang2 = s2.angle + Constants.ANGLE_180;
					uint angdiff = ang2 - ang1;

					if (angdiff < bestang && angdiff > 0)
					{
						bestang = angdiff;
						bestseg = tryseg;
					}
				}
				tryseg = s2.nextforvert;
			}

			seg = bestseg;
		} while (seg != Constants.MAX_INT && Segs[seg].loopnum == 0);

		return loopnum + 1;
	}

	// Find the bounding box for a specific polyobject.
	unsafe bool GetPolyExtents(int polynum, int* bbox, int bOffset)
	{
		int i;

		bbox[Box.BOXLEFT] = bbox[Box.BOXBOTTOM] = int.MaxValue;
		bbox[Box.BOXRIGHT] = bbox[Box.BOXTOP] = int.MinValue;

		// Try to find a polyobj marked with a start line
		for (i = 0; i < Segs.Count; ++i)
		{
			fixed (IntLineDef* line = &Level.Lines.Data[Segs[i].linedef])
			{
				if (line->special == PO_LINE_START && line->args[0] == polynum)
					break;
			}
		}

		if (i < Segs.Count)
		{
			vertex_t start = new();
			int count = Segs.Count; // to prevent endless loops. Stop when this reaches the number of segs.

			int vert = Segs[i].v1;

			start.x = Vertices[vert].x;
			start.y = Vertices[vert].y;

			do
			{
				AddSegToBBox(bbox, bOffset, Segs[i]);
				vert = Segs[i].v2;
				i = Vertices[vert].segs;
			} while ((--count) != 0 && i != Constants.MAX_INT && (Vertices[vert].x != start.x || Vertices[vert].y != start.y));

			return true;
		}

		// Try to find a polyobj marked with explicit lines
		bool found = false;

		for (i = 0; i < Segs.Count; ++i)
		{
			fixed (IntLineDef* line = &Level.Lines.Data[Segs[i].linedef])
			{
				if (line->special == PO_LINE_EXPLICIT && line->args[0] == polynum)
				{
					AddSegToBBox(bbox, bOffset, Segs[i]);
					found = true;
				}
			}
		}
		return found;
	}

	unsafe void AddSegToBBox(int* bbox, int bOffset, FPrivSeg seg)
	{
		FPrivVert v1 = Vertices[seg.v1];
		FPrivVert v2 = Vertices[seg.v2];

		if (v1.x < bbox[bOffset + Box.BOXLEFT])
			bbox[bOffset + Box.BOXLEFT] = v1.x;
		if (v1.x > bbox[bOffset + Box.BOXRIGHT])
			bbox[bOffset + Box.BOXRIGHT] = v1.x;
		if (v1.y < bbox[bOffset + Box.BOXBOTTOM])
			bbox[bOffset + Box.BOXBOTTOM] = v1.y;
		if (v1.y > bbox[bOffset + Box.BOXTOP])
			bbox[bOffset + Box.BOXTOP] = v1.y;

		if (v2.x < bbox[bOffset + Box.BOXLEFT])
			bbox[bOffset + Box.BOXLEFT] = v2.x;
		if (v2.x > bbox[bOffset + Box.BOXRIGHT])
			bbox[bOffset + Box.BOXRIGHT] = v2.x;
		if (v2.y < bbox[bOffset + Box.BOXBOTTOM])
			bbox[bOffset + Box.BOXBOTTOM] = v2.y;
		if (v2.y > bbox[bOffset + Box.BOXTOP])
			bbox[bOffset + Box.BOXTOP] = v2.y;
	}

    static float GetDistance(int x1, int y1, int x2, int y2)
	{
		float fx1 = x1 / 65536.0f;
		float fy1 = y1 / 65536.0f;
		float fx2 = x2 / 65536.0f;
		float fy2 = y2 / 65536.0f;
		return (float)Math.Sqrt((fx2 - fx1) * (fx2 - fx1)) + ((fy2 - fy1) * (fy2 - fy1));
	}

    static int ClassifyLine(in node_t node, FPrivVert v1, FPrivVert v2, Span<int> sidev)
	{
		double d_x1 = node.x;
		double d_y1 = node.y;
		double d_dx = node.dx;
		double d_dy = node.dy;
		double d_xv1 = v1.x;
		double d_xv2 = v2.x;
		double d_yv1 = v1.y;
		double d_yv2 = v2.y;

		double s_num1 = (d_y1 - d_yv1) * d_dx - (d_x1 - d_xv1) * d_dy;
		double s_num2 = (d_y1 - d_yv2) * d_dx - (d_x1 - d_xv2) * d_dy;

		int nears = 0;

		if (s_num1 <= -FAR_ENOUGH)
		{
			if (s_num2 <= -FAR_ENOUGH)
			{
				sidev[0] = sidev[1] = 1;
				return 1;
			}
			if (s_num2 >= FAR_ENOUGH)
			{
				sidev[0] = 1;
				sidev[1] = -1;
				return -1;
			}
			nears = 1;
		}
		else if (s_num1 >= FAR_ENOUGH)
		{
			if (s_num2 >= FAR_ENOUGH)
			{
				sidev[0] = sidev[1] = -1;
				return 0;
			}
			if (s_num2 <= -FAR_ENOUGH)
			{
				sidev[0] = -1;
				sidev[1] = 1;
				return -1;
			}
			nears = 1;
		}
		else
		{
			nears = 2 | (Math.Abs(s_num2) < FAR_ENOUGH ? 1 : 0);
		}

		if (nears != 0)
		{
			double l = 1 / (d_dx * d_dx + d_dy * d_dy);
			if ((nears & 2) != 0)
			{
				double dist = s_num1 * s_num1 * l;
				if (dist < Constants.SIDE_EPSILON * Constants.SIDE_EPSILON)
					sidev[0] = 0;
				else
					sidev[0] = s_num1 > 0.0 ? -1 : 1;
			}
			else
			{
				sidev[0] = s_num1 > 0.0 ? -1 : 1;
			}
			if ((nears & 1) != 0)
			{
				double dist = s_num2 * s_num2 * l;
				if (dist < Constants.SIDE_EPSILON * Constants.SIDE_EPSILON)
					sidev[1] = 0;
				else
					sidev[1] = s_num2 > 0.0 ? -1 : 1;
			}
			else
			{
				sidev[1] = s_num2 > 0.0 ? -1 : 1;
			}
		}
		else
		{
			sidev[0] = s_num1 > 0.0 ? -1 : 1;
			sidev[1] = s_num2 > 0.0 ? -1 : 1;
		}

		if ((sidev[0] | sidev[1]) == 0)
		{
			// seg is coplanar with the splitter, so use its orientation to determine
			// which child it ends up in. If it faces the same direction as the splitter,
			// it goes in front. Otherwise, it goes in back.
			if (node.dx != 0)
			{
				if ((node.dx > 0 && v2.x > v1.x) || (node.dx < 0 && v2.x < v1.x))
					return 0;
				else
					return 1;
			}
			else
			{
				if ((node.dy > 0 && v2.y > v1.y) || (node.dy < 0 && v2.y < v1.y))
					return 0;
				else
					return 1;
			}
		}
		else if (sidev[0] <= 0 && sidev[1] <= 0)
		{
			return 0;
		}
		else if (sidev[0] >= 0 && sidev[1] >= 0)
		{
			return 1;
		}
		return -1;
	}

	unsafe uint CreateSubsector(int set, int* bbox, int boxOffset)
    {
        bbox[boxOffset + Box.BOXTOP] = int.MinValue;
        bbox[boxOffset + Box.BOXRIGHT] = int.MinValue;
        bbox[boxOffset + Box.BOXBOTTOM] = int.MaxValue;
        bbox[boxOffset + Box.BOXLEFT] = int.MaxValue;

        //printf("Subsector from set %d\n", set));

        DebugCreateSubsector(set);
        // We cannot actually create the subsector now because the node building
        // process might split a seg in this subsector (because all partner segs
        // must use the same pair of vertices), adding a new seg that hasn't been
        // created yet. After all the nodes are built, then we can create the
        // actual subsectors using the CreateSubsectorsForReal function below.
        int ssnum = SubsectorSets.AddCount(set);
        int count = 0;
        while (set != Constants.MAX_INT)
        {
            AddSegToBBox(bbox, boxOffset, Segs[set]);
            set = Segs[set].next;
            count++;
        }

        SegsStuffed += count;
        if ((SegsStuffed & ~63) != ((SegsStuffed - count) & ~63))
        {
            int percent = (int)(SegsStuffed * 1000.0 / Segs.Count);
            //fprintf(stderr, "   BSP: %3d.%d%%\r", percent / 10, percent % 10);
        }

        //printf("bbox (%d,%d)-(%d,%d)\n", bbox[BOXLEFT] >> 16, bbox[BOXBOTTOM] >> 16, bbox[BOXRIGHT] >> 16, bbox[BOXTOP] >> 16));

        return (uint)ssnum;
    }

	[Conditional("DEBUG")]
    private unsafe void DebugCreateSubsector(int set)
    {
        // Check for segs with duplicate start/end vertices
        for (int s1 = set; s1 != Constants.MAX_INT; s1 = Segs[s1].next)
        {
            for (int s2 = Segs[s1].next; s2 != Constants.MAX_INT; s2 = Segs[s2].next)
            {
                if (Segs[s1].v1 == Segs[s2].v1)
                {
                    //printf("Segs %d%c and %d%c have duplicate start vertex %d (%d, %d)\n", s1, Segs[s1].linedef == -1 ? '*' : ' ', s2, Segs[s2].linedef == -1 ? '*' : ' ', Segs[s1].v1, Vertices[Segs[s1].v1].x >> 16, Vertices[Segs[s1].v1].y >> 16);
                }
                if (Segs[s1].v2 == Segs[s2].v2)
                {
                    //printf("Segs %d%c and %d%c have duplicate end vertex %d (%d, %d)\n", s1, Segs[s1].linedef == -1 ? '*' : ' ', s2, Segs[s2].linedef == -1 ? '*' : ' ', Segs[s1].v2, Vertices[Segs[s1].v2].x >> 16, Vertices[Segs[s1].v2].y >> 16);
                }
            }
        }
    }

	void CreateSubsectorsForReal()
	{
		for (int i = 0; i < SubsectorSets.Count; ++i)
		{
			subsector_t sub = new();
			int set = SubsectorSets[i];
			sub.firstline = SegList.Count;
			while (set != Constants.MAX_INT)
			{
				USegPtr ptr = new();
				ptr.SegPtr = Segs[set];
				ptr.SegNum = set;
				SegList.Add(ptr);
				set = ptr.SegPtr.next;
			}
			sub.numlines = (SegList.Count - sub.firstline);

			// Sort segs by linedef for special effects
			SegList.Sort(sub.firstline, sub.numlines, _segComparer);

			// Convert seg pointers into indices
			//printf("Output subsector %d:\n", Subsectors.Size()));
			//if (SegList[sub.firstline].SegPtr.linedef == -1)
			//printf("  Failure: Subsector %d is all minisegs!\n", Subsectors.Size());

			Subsectors.Add(sub);
		}
	}	

    public WideVertex[] GetVertices()
	{
		int count = Vertices.Count;
		WideVertex[] verts = new WideVertex[count];

		for (int i = 0; i < count; ++i)
		{
			verts[i].x = Vertices[i].x;
			verts[i].y = Vertices[i].y;
			verts[i].index = Vertices[i].index;
		}

		return verts;
	}

	public unsafe void GetNodes(out MapNodeEx[] outNodes, out MapSegEx[] outSegs, out MapSubsectorEx[] outSubs)
	{
		short[] bbox = new short[4];
		List<MapSegEx> segs = new(Segs.Count);

		// Walk the BSP and create a new BSP with only the information
		// suitable for a standard tree. At a minimum, this means removing
		// all minisegs. As an optional step, I also recompute all the
		// nodes' bounding boxes so that they only bound the real segs and
		// not the minisegs.

		int nodeCount = Nodes.Length;
		outNodes = new MapNodeEx[nodeCount];

		int subCount = Subsectors.Count;
		outSubs = new MapSubsectorEx[subCount];

		fixed (short* bboxptr = bbox)
			RemoveMinisegs(outNodes, segs, outSubs, Nodes.Length - 1, bboxptr, 0);

		int segCount = segs.Count;
		outSegs = new MapSegEx[segCount];

		for (int i = 0; i < segs.Count; i++)
			outSegs[i] = segs[i];
	}

	unsafe int RemoveMinisegs(MapNodeEx[] nodes, List<MapSegEx> segs, MapSubsectorEx[] subs, int node, short* bbox, int bboxOffset)
	{
		if ((node & Constants.NFX_SUBSECTOR) != 0)
		{
			int subnum = node == -1 ? 0 : (int)(node & ~Constants.NFX_SUBSECTOR);
			int numsegs = StripMinisegs(segs, subnum, bbox);
			subs[subnum].numlines = numsegs;
			subs[subnum].firstline = segs.Count - numsegs;
			return (int)(Constants.NFX_SUBSECTOR | subnum);
		}
		else
		{
			node_t orgnode = Nodes[node];
			MapNodeEx newnode = nodes[node];

			int child0 = RemoveMinisegs(nodes, segs, subs, (int)orgnode.intchildren[0], newnode.bbox, 0);
			int child1 = RemoveMinisegs(nodes, segs, subs, (int)orgnode.intchildren[1], newnode.bbox, Constants.BoxOffset);

			newnode.x = orgnode.x;
			newnode.y = orgnode.y;
			newnode.dx = orgnode.dx;
			newnode.dy = orgnode.dy;
			newnode.children[0] = (uint)child0;
			newnode.children[1] = (uint)child1;

			bbox[bboxOffset + Box.BOXTOP] = Math.Max(newnode.bbox[Box.BOXTOP], newnode.bbox[Constants.BoxOffset + Box.BOXTOP]);
			bbox[bboxOffset + Box.BOXBOTTOM] = Math.Min(newnode.bbox[Box.BOXBOTTOM], newnode.bbox[Constants.BoxOffset + Box.BOXBOTTOM]);
			bbox[bboxOffset + Box.BOXLEFT] = Math.Min(newnode.bbox[Box.BOXLEFT], newnode.bbox[Constants.BoxOffset + Box.BOXLEFT]);
			bbox[bboxOffset + Box.BOXRIGHT] = Math.Max(newnode.bbox[Box.BOXRIGHT], newnode.bbox[Constants.BoxOffset + Box.BOXRIGHT]);

			return node;
		}
	}

	unsafe int StripMinisegs(List<MapSegEx> segs, int subsector, short* bbox)
	{
		int count;
		int i;
		int max;

		// The bounding box is recomputed to only cover the real segs and not the
		// minisegs in the subsector.
		bbox[Box.BOXTOP] = -32768;
		bbox[Box.BOXBOTTOM] = 32767;
		bbox[Box.BOXLEFT] = 32767;
		bbox[Box.BOXRIGHT] = -32768;

		i = Subsectors[subsector].firstline;
		max = Subsectors[subsector].numlines + i;

		for (count = 0; i < max; ++i)
		{
			FPrivSeg org = Segs[SegList[i].SegNum];

			// Because of the ordering guaranteed by SortSegs(), all mini segs will
			// be at the end of the subsector, so once one is encountered, we can
			// stop right away.
			if (org.linedef == -1)
			{
				break;
			}
			else
			{
				MapSegEx newseg = new MapSegEx();

				AddSegToShortBBox(bbox, org);

				newseg.v1 = org.v1;
				newseg.v2 = org.v2;
				newseg.angle = (ushort)(org.angle >> 16);
				newseg.offset = (short)(org.offset >> Constants.FRACBITS);
				newseg.linedef = (ushort)org.linedef;

				// Just checking the sidedef to determine the side is insufficient.
				// When a level is sidedef compressed both sides may well have the same sidedef.

				IntLineDef ld = Level.Lines[newseg.linedef];

				if (ld.sidenum[0] == ld.sidenum[1])
				{
					// When both sidedefs are the same a quick check doesn't work so this
					// has to be done by comparing the distances of the seg's end point to
					// the line's start.
					WideVertex lv1 = Level.Vertices[ld.v1];
					WideVertex sv1 = Level.Vertices[org.v1];
					WideVertex sv2 = Level.Vertices[org.v2];

					double dist1sq = (double)(sv1.x - lv1.x) * (sv1.x - lv1.x) + (double)(sv1.y - lv1.y) * (sv1.y - lv1.y);
					double dist2sq = (double)(sv2.x - lv1.x) * (sv2.x - lv1.x) + (double)(sv2.y - lv1.y) * (sv2.y - lv1.y);

					newseg.side = (short)(dist1sq < dist2sq ? 0 : 1);

				}
				else
				{
					newseg.side = (short)(ld.sidenum[1] == org.sidedef ? 1 : 0);
				}

				segs.Add(newseg);
				++count;
			}
		}
		return count;
	}

	unsafe void AddSegToShortBBox(short* bbox, FPrivSeg seg)
	{
		FPrivVert v1 = Vertices[seg.v1];
		FPrivVert v2 = Vertices[seg.v2];

		short v1x = (short)(v1.x >> Constants.FRACBITS);
		short v1y = (short)(v1.y >> Constants.FRACBITS);
		short v2x = (short)(v2.x >> Constants.FRACBITS);
		short v2y = (short)(v2.y >> Constants.FRACBITS);

		if (v1x < bbox[Box.BOXLEFT])
			bbox[Box.BOXLEFT] = v1x;
		if (v1x > bbox[Box.BOXRIGHT])
			bbox[Box.BOXRIGHT] = v1x;
		if (v1y < bbox[Box.BOXBOTTOM])
			bbox[Box.BOXBOTTOM] = v1y;
		if (v1y > bbox[Box.BOXTOP])
			bbox[Box.BOXTOP] = v1y;

		if (v2x < bbox[Box.BOXLEFT])
			bbox[Box.BOXLEFT] = v2x;
		if (v2x > bbox[Box.BOXRIGHT])
			bbox[Box.BOXRIGHT] = v2x;
		if (v2y < bbox[Box.BOXBOTTOM])
			bbox[Box.BOXBOTTOM] = v2y;
		if (v2y > bbox[Box.BOXTOP])
			bbox[Box.BOXTOP] = v2y;
	}
}