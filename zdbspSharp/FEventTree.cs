namespace zdbspSharp;

public sealed class FEventInfo
{
	public int Vertex;
	public int FrontSeg;
};

public sealed class FEvent
{
	public FEvent Parent, Left, Right;
	public double Distance;
	public FEventInfo Info = new();
};


public sealed class FEventTree
{
	FEvent Nil;
	FEvent Root;
	FEvent? Spare;

	public FEventTree()
	{
		Root = null;
		Spare = null;
	}

	public FEvent? GetSuccessor(FEvent fevevnt) 
	{ 
		FEvent node = Successor(fevevnt);
		return ReferenceEquals(fevevnt, Nil) ? null : node;
	}

	public FEvent? GetPredecessor(FEvent fevevnt)
	{ 
		FEvent node = Predecessor(fevevnt);
		return ReferenceEquals(fevevnt, Nil) ? null : node;
	}

	public void Dispose()
	{
		FEvent? probe;
    
		DeleteAll();
		probe = Spare;
		while (probe != null)
		{
			FEvent next = probe.Left;
			probe = next;
		}
	}

	public void DeleteAll()
	{
		DeletionTraverser(Root);
		Root = Nil;
	}

	public void DeletionTraverser(FEvent node)
	{
		if (node != Nil && node != null)
		{
			DeletionTraverser(node.Left);
			DeletionTraverser(node.Right);
			node.Left = Spare;
			Spare = node;
		}
	}

	public FEvent GetNewNode()
	{
		FEvent node;
    
		if (Spare != null)
		{
			node = Spare;
			Spare = node.Left;
		}
		else
		{
			node = new FEvent();
		}
		return node;
	}

	public void Insert(FEvent z)
	{
		FEvent y = Nil;
		FEvent x = Root;
    
		while (x != Nil)
		{
			y = x;
			if (z.Distance < x.Distance)
				x = x.Left;
			else
				x = x.Right;
		}
		z.Parent = y;
		if (y == Nil)
			Root = z;
		else if (z.Distance < y.Distance)
			y.Left = z;
		else
			y.Right = z;
		z.Left = Nil;
		z.Right = Nil;
	}

	public FEvent Successor(FEvent fevent)
	{
		if (fevent.Right != Nil)
		{
			fevent = fevent.Right;
			while (fevent.Left != Nil)
				fevent = fevent.Left;
			return fevent;
		}
		else
		{
			FEvent y = fevent.Parent;
			while (y != Nil && fevent == y.Right)
			{
				fevent = y;
				y = y.Parent;
			}
			return y;
		}
	}

	public FEvent Predecessor(FEvent fevent)
	{
		if (fevent.Left != Nil)
		{
			fevent = fevent.Left;
			while (fevent.Right != Nil)
				fevent = fevent.Right;
			return fevent;
		}
		else
		{
			FEvent y = fevent.Parent;
			while (y != Nil && fevent == y.Left)
			{
				fevent = y;
				y = y.Parent;
			}
			return y;
		}
	}

	public FEvent FindEvent(double key)
	{
		FEvent node = Root;
    
		while (node != Nil)
		{
			if (node.Distance == key)
				return node;
			else if (node.Distance > key)
				node = node.Left;
			else
				node = node.Right;
		}
		return null;
	}

	public FEvent? GetMinimum()
	{
		FEvent node = Root;
    
		if (node == Nil)
			return null;
		while (node.Left != Nil)
			node = node.Left;
		return node;
	}

	public void PrintTree(FEvent node)
	{
		if (node.Left != Nil)
			PrintTree(node.Left);
		//printf(" Distance %g, vertex %d, seg %u\n", Math.Sqrt(event.Distance / 4294967296.0), event.Info.Vertex, (uint)@event.Info.FrontSeg);
		if (node.Right != Nil)
			PrintTree(node.Right);
	}
}