namespace zdbspSharp
{
	public sealed class SegCompararer : IComparer<USegPtr>
	{
		public int Compare(USegPtr? xptr, USegPtr? yptr)
		{
			FPrivSeg x = xptr.SegPtr;
			FPrivSeg y = yptr.SegPtr;
			// Segs are grouped into three categories in this order:
			//
			// 1. Segs with different front and back sectors (or no back at all).
			// 2. Segs with the same front and back sectors.
			// 3. Minisegs.
			//
			// Within the first two sets, segs are also sorted by linedef.
			//
			// Note that when GL subsectors are written, the segs will be reordered
			// so that they are in clockwise order, and extra minisegs will be added
			// as needed to close the subsector. But the first seg used will still be
			// the first seg chosen here.

			int xtype, ytype;

			if (x.linedef == -1)
				xtype = 2;
			else if (x.frontsector == x.backsector)
				xtype = 1;
			else
				xtype = 0;

			if (y.linedef == -1)
				ytype = 2;
			else if (y.frontsector == y.backsector)
				ytype = 1;
			else
				ytype = 0;

			if (xtype != ytype)
				return xtype - ytype;
			else if (xtype < 2)
				return x.linedef - y.linedef;
			else
				return 0;
		}
	}
}
