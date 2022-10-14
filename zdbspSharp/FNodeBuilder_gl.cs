using System.Diagnostics;

namespace zdbspSharp;

public partial class FNodeBuilder
{

	/*
		Routines only necessary for building GL-friendly nodes.
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

	// If there are any segs on the splitter that span more than two events, they
	// must be split. Alien Vendetta is one example wad that is quite bad about
	// having overlapping lines. If we skip this step, these segs will still be
	// split later, but minisegs will erroneously be added for them, and partner
	// seg information will be messed up in the generated tree.

	void AddMinisegs(node_t node, int splitseg, ref int fset, ref int bset)
	{
		FEvent? fevent = Events.GetMinimum();
		FEvent? prev = null;

		while (fevent != null)
		{
			if (prev != null)
			{
				int fseg1;
				int bseg1;
				int fseg2;
				int bseg2;
				int fnseg;
				int bnseg;

				// Minisegs should only be added when they can create valid loops on both the front and
				// back of the splitter. This means some subsectors could be unclosed if their sectors
				// are unclosed, but at least we won't be needlessly creating subsectors in void space.
				// Unclosed subsectors can be closed trivially once the BSP tree is complete.

				if ((fseg1 = CheckLoopStart(node.dx, node.dy, prev.Info.Vertex, fevent.Info.Vertex)) != Constants.MAX_INT && 
					(bseg1 = CheckLoopStart(-node.dx, -node.dy, fevent.Info.Vertex, prev.Info.Vertex)) != Constants.MAX_INT && 
					(fseg2 = CheckLoopEnd(node.dx, node.dy, fevent.Info.Vertex)) != Constants.MAX_INT && 
					(bseg2 = CheckLoopEnd(-node.dx, -node.dy, prev.Info.Vertex)) != Constants.MAX_INT)
				{
					// Add miniseg on the front side
					fnseg = AddMiniseg(prev.Info.Vertex, fevent.Info.Vertex, Constants.MAX_INT, fseg1, splitseg);
					Segs[fnseg].next = fset;
					fset = fnseg;

					// Add miniseg on the back side
					bnseg = AddMiniseg(fevent.Info.Vertex, prev.Info.Vertex, fnseg, bseg1, splitseg);
					Segs[bnseg].next = bset;
					bset = bnseg;

					int fsector;
					int bsector;

					fsector = Segs[fseg1].frontsector;
					bsector = Segs[bseg1].frontsector;

					Segs[fnseg].frontsector = fsector;
					Segs[fnseg].backsector = bsector;
					Segs[bnseg].frontsector = bsector;
					Segs[bnseg].backsector = fsector;

					// Only print the warning if this might be bad.
					if (fsector != bsector && fsector != Segs[fseg1].backsector && bsector != Segs[bseg1].backsector)
					{
						//Warn("Sectors %d at (%d,%d) and %d at (%d,%d) don't match.\n", Segs[fseg1].frontsector, Vertices[prev.Info.Vertex].x >> FRACBITS, Vertices[prev.Info.Vertex].y >> DefineConstants.FRACBITS, Segs[bseg1].frontsector, Vertices[fevent.Info.Vertex].x >> FRACBITS, Vertices[@event.Info.Vertex].y >> FRACBITS);
					}
				}
			}
			prev = fevent;
			fevent = Events.GetSuccessor(fevent);
		}
	}

	int AddMiniseg(int v1, int v2, int partner, int seg1, int splitseg)
	{
		int nseg;
		FPrivSeg seg = Segs[seg1];
		FPrivSeg newseg = new();

		newseg.sidedef = Constants.MAX_INT;
		newseg.linedef = Constants.MAX_INT;
		newseg.loopnum = 0;
		newseg.next = Constants.MAX_INT;
		newseg.planefront = true;
		newseg.hashnext = null;
		newseg.storedseg = Constants.MAX_INT;
		newseg.frontsector = -1;
		newseg.backsector = -1;
		newseg.offset = 0;
		newseg.angle = 0;

		if (splitseg != Constants.MAX_INT)
			newseg.planenum = Segs[splitseg].planenum;
		else
			newseg.planenum = -1;

		newseg.v1 = v1;
		newseg.v2 = v2;
		newseg.nextforvert = Vertices[v1].segs;
		newseg.nextforvert2 = Vertices[v2].segs2;
		newseg.next = seg.next;
		if (partner != Constants.MAX_INT)
		{
			newseg.partner = partner;

            Debug.Assert(Segs[partner].v1 == newseg.v2);
            Debug.Assert(Segs[partner].v2 == newseg.v1);
        }
		else
		{
			newseg.partner = Constants.MAX_INT;
		}

		nseg = Segs.AddCount(newseg);
		if (newseg.partner != Constants.MAX_INT)
		{
			Segs[partner].partner = nseg;
		}
		Vertices[v1].segs = nseg;
		Vertices[v2].segs2 = nseg;
		//Printf ("Between %d and %d::::\n", seg1, seg2);
		return nseg;
	}

	int CheckLoopStart(int dx, int dy, int vertex, int vertex2)
	{
		FPrivVert v = Vertices[vertex];
		uint splitAngle = Util.PointToAngle(dx, dy);
		int segnum;
		uint bestang;
		int bestseg;

		// Find the seg ending at this vertex that forms the smallest angle
		// to the splitter.
		segnum = v.segs2;
		bestang = Constants.ANGLE_MAX;
		bestseg = Constants.MAX_INT;
		while (segnum != Constants.MAX_INT)
		{
			FPrivSeg seg = Segs[segnum];
			uint segAngle = Util.PointToAngle(Vertices[seg.v1].x - v.x, Vertices[seg.v1].y - v.y);
			uint diff = splitAngle - segAngle;

			if (diff < Constants.ANGLE_EPSILON && Util.PointOnSide(Vertices[seg.v1].x, Vertices[seg.v1].y, v.x, v.y, dx, dy) == 0)
			{
				// If a seg lies right on the splitter, don't count it
			}
			else
			{
				if (diff <= bestang)
				{
					bestang = diff;
					bestseg = segnum;
				}
			}
			segnum = seg.nextforvert2;
		}
		if (bestseg == Constants.MAX_INT)
		{
			return Constants.MAX_INT;
		}
		// Now make sure there are no segs starting at this vertex that form
		// an even smaller angle to the splitter.
		segnum = v.segs;
		while (segnum != Constants.MAX_INT)
		{
			FPrivSeg seg = Segs[segnum];
			if (seg.v2 == vertex2)
			{
				return Constants.MAX_INT;
			}
			uint segAngle = Util.PointToAngle(Vertices[seg.v2].x - v.x, Vertices[seg.v2].y - v.y);
			uint diff = splitAngle - segAngle;
			if (diff < bestang && seg.partner != bestseg)
			{
				return Constants.MAX_INT;
			}
			segnum = seg.nextforvert;
		}
		return bestseg;
	}

	int CheckLoopEnd(int dx, int dy, int vertex)
	{
		FPrivVert v = Vertices[vertex];
		uint splitAngle = Util.PointToAngle(dx, dy) + Constants.ANGLE_180;
		int segnum;
		uint bestang;
		int bestseg;

		// Find the seg starting at this vertex that forms the smallest angle
		// to the splitter.
		segnum = v.segs;
		bestang = Constants.ANGLE_MAX;
		bestseg = Constants.MAX_INT;
		while (segnum != Constants.MAX_INT)
		{
			FPrivSeg seg = Segs[segnum];
			uint segAngle = Util.PointToAngle(Vertices[seg.v2].x - v.x, Vertices[seg.v2].y - v.y);
			uint diff = segAngle - splitAngle;

			if (diff < Constants.ANGLE_EPSILON && Util.PointOnSide(Vertices[seg.v1].x, Vertices[seg.v1].y, v.x, v.y, dx, dy) == 0)
			{
				// If a seg lies right on the splitter, don't count it
			}
			else
			{
				if (diff <= bestang)
				{
					bestang = diff;
					bestseg = segnum;
				}
			}
			segnum = seg.nextforvert;
		}
		if (bestseg == Constants.MAX_INT)
		{
			return Constants.MAX_INT;
		}
		// Now make sure there are no segs ending at this vertex that form
		// an even smaller angle to the splitter.
		segnum = v.segs2;
		while (segnum != Constants.MAX_INT)
		{
			FPrivSeg seg = Segs[segnum];
			uint segAngle = Util.PointToAngle(Vertices[seg.v1].x - v.x, Vertices[seg.v1].y - v.y);
			uint diff = segAngle - splitAngle;
			if (diff < bestang && seg.partner != bestseg)
			{
				return Constants.MAX_INT;
			}
			segnum = seg.nextforvert2;
		}
		return bestseg;
	}

}
