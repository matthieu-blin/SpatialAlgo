using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Node : IBound
{
    protected Node m_Parent;

    public bool IsLeaf
    {
        get => Height == 1;
    }

    public int Height = 0;

    public Node(List<IBound> children, int height)
    {
        Height = height;
        Children = children;
        Children.AsParallel().ForAll(x =>
        {
            if (x is Node) (x as Node).m_Parent = this;
        });
        m_Bounds = RTree.GetEnclosingBounds(Children);
    }

    public List<IBound> Children { get; set; }

    void UpdateBound(Bounds _bounds)
    {
        m_Bounds.Encapsulate(_bounds);
        if (m_Parent != null)
        {
            m_Parent.UpdateBound(m_Bounds);
        }
    }

    public void Add(IBound node)
    {
        Children.Add(node);
        UpdateBound(node.Bounds);
        Node n = node as Node;
        if (n != null)
            n.m_Parent = this;
    }

    public Bounds m_Bounds;

    public Bounds Bounds
    {
        get => m_Bounds;
    }
}

public interface IBound
{
    Bounds Bounds { get; }
}

public class RTree
{
    public static Bounds Intersection(Bounds a, Bounds b)
    {
        Bounds r = new Bounds();
        r.SetMinMax(
            new Vector3(Mathf.Min(a.min.x, b.min.x), Mathf.Min(a.min.y, b.min.y), 0),
            new Vector3(Mathf.Max(a.max.x, b.max.x), Mathf.Max(a.max.y, b.max.y), 0)
        );
        return r;
    }

    public static Bounds GetEnclosingBounds(IEnumerable<IBound> items)
    {
        Vector3 center = Vector3.zero;
        foreach (var data in items)
            center += data.Bounds.center;
        if (items.Count() > 0)
            center = center / items.Count();
        var envelope = new Bounds(center, Vector2.one);
        foreach (var data in items)
            envelope.Encapsulate(data.Bounds);
        return envelope;
    }
}

public class RTree<T> where T : IBound
{
    public Node Root;
    public int MinEntry;
    public int MaxEntry;
    public int Count;

    public RTree(int maxEntry = 4, int minEntry = 2)
    {
        MinEntry = minEntry;
        MaxEntry = maxEntry;
        Root = new Node(new List<IBound>(), 1);
    }

    public void Insert(IBound data)
    {
        Insert(data, Root.Height);
        Count++;
    }

    public void StupidBulkLoad(IEnumerable<T> _data)
    {
        var test = _data.Select(x => (IBound) x).OrderBy(x => x.Bounds.min.x).ThenBy(x => x.Bounds.min.y);
        int depth = (int) Math.Ceiling(Math.Log(test.Count()) / Math.Log(MaxEntry)) - 1;

        List<IBound> nodes = new List<IBound>();
        List<IBound> parent = new List<IBound>();
        Node node = null;

        int d = 0;
        while (d <= depth)
        {
            foreach (var t in test)
            {
                nodes.Add(t);
                if (nodes.Count == MaxEntry)
                {
                    node = new Node(nodes, d);
                    parent.Add(node);
                    nodes = new List<IBound>();
                }
            }

            node = new Node(nodes, d);
            parent.Add(node);
            nodes = new List<IBound>();
            d++;
            test = parent.OrderBy(x => 1);
            parent = new List<IBound>();
        }

        Root = node;
    }

    private void Insert(IBound data, int depth)
    {
        var path = FindCoveringArea(data.Bounds, depth);

        var insertNode = path.Last();
        insertNode.Add(data);

        while (--depth >= 0 && path[depth].Children.Count > MaxEntry)
        {
            var newNode = SplitNode(path[depth]);
            if (depth == 0)
                Root = new Node(new List<IBound> {Root, newNode}, Root.Height + 1);
            else
                path[depth - 1].Add(newNode);
        }
    }

    private Node SplitNode(Node node)
    {
        SortChildren(node);

        var splitPoint = GetBestSplitIndex(node.Children);
        var newChildren = node.Children.Skip(splitPoint).ToList();
        node.Children.RemoveRange(splitPoint, node.Children.Count - splitPoint);
        return new Node(newChildren, node.Height);
    }

    private int GetBestSplitIndex(List<IBound> children)
    {
        return Enumerable.Range(MinEntry, children.Count - MinEntry)
            .Select(i =>
            {
                var leftBounds = RTree.GetEnclosingBounds(children.Take(i));
                var rightBounds = RTree.GetEnclosingBounds(children.Skip(i));

                var overlap = Area(RTree.Intersection(leftBounds, rightBounds));
                var totalArea = Area(leftBounds) + Area(rightBounds);
                return new {i, overlap, totalArea};
            })
            .OrderBy(x => x.overlap)
            .ThenBy(x => x.totalArea)
            .Select(x => x.i)
            .First();
    }


    private void SortChildren(Node node)
    {
        node.Children.Sort((a, b) => a.Bounds.min.x < b.Bounds.min.x ? 1 : -1);
        var splitsByX = GetPotentialSplitMargins(node.Children);
        node.Children.Sort((a, b) => a.Bounds.min.y < b.Bounds.min.y ? 1 : -1);
        var splitsByY = GetPotentialSplitMargins(node.Children);

        if (splitsByX < splitsByY)
            node.Children.Sort((a, b) => a.Bounds.min.x < b.Bounds.min.x ? 1 : -1);
    }

    private double GetPotentialSplitMargins(List<IBound> children)
    {
        return GetPotentialEnclosingMargins(children) +
               GetPotentialEnclosingMargins(children.AsEnumerable().Reverse().ToList());
    }

    private double GetPotentialEnclosingMargins(List<IBound> children)
    {
        var envelope = new Bounds();
        int i = 0;
        for (; i < MinEntry; i++)
            envelope.Encapsulate(children[i].Bounds);

        var totalMargin = envelope.size.sqrMagnitude;
        for (; i < children.Count - MinEntry; i++)
        {
            envelope.Encapsulate(children[i].Bounds);
            totalMargin += envelope.size.magnitude;
        }

        return totalMargin;
    }

    public float EnlargedBoundArea(Bounds _b, Bounds _c)
    {
        var b = new Bounds(_b.center, _b.size);
        b.Encapsulate(_c);
        return Area(b);
    }

    public float Area(Bounds b)
    {
        return b.size.x * b.size.y;
    }


    private List<Node> FindCoveringArea(Bounds area, int depth)
    {
        var path = new List<Node>();
        var node = Root;

        while (true)
        {
            path.Add(node);
            if (node.IsLeaf || path.Count == depth) return path;

            //select child with the least enlargment heuristic 
            node = node.Children
                .Select(c => new
                    {EnlargedArea = EnlargedBoundArea(c.Bounds, area), Area = Area(c.Bounds), Node = c as Node})
                .OrderBy(c => c.EnlargedArea)
                .ThenBy(c => c.Area)
                .Select(c => c.Node)
                .First();
        }
    }


    public IEnumerable<T> Search(Bounds boundingBox)
    {
        return DoSearch(boundingBox).Select(x => (T) x.Peek()).ToList();
    }

    private List<Stack<IBound>> DoSearch(Bounds boundingBox)
    {
        var node = Root;
        if (!node.Bounds.Intersects(boundingBox)) //if root node doesnt not intersect return new empty List<stack>
            return new List<Stack<IBound>>();

        var intersections = new List<Stack<IBound>>();
        var queue = new Queue<Stack<IBound>>();
        var newStack = new Stack<IBound>();
        newStack.Clear();
        newStack.Push(node); //just the root node??
        queue.Enqueue(newStack);

        do
        {
            var current = queue.Dequeue();
            foreach (var c in (current.Peek() as Node).Children)
            {
                if (c.Bounds.Intersects(boundingBox))
                {
                    var cur2 = new Stack<IBound>();
                    cur2.Push(c);
                    if (c is T)
                        intersections.Add(cur2);
                    else
                        queue.Enqueue(cur2);
                }
            }
        } while (queue.Count != 0);

        return intersections;
    }

    public IEnumerable<T> Search(Vector3 center, int n)
    {
        List<T> res = new List<T>();
        List<IBound> q = new List<IBound>();
        Action<Node> f = null;
        IEnumerable<IBound> w;

        f = (l) =>
        {
            q.AddRange(l.Children);
            q.Sort((a, b) =>
                (a.Bounds.SqrDistance(center) < b.Bounds.SqrDistance(center) ? -1 : 1));


            while (q.Any())
            {
                var x = q.First();
                q.RemoveAt(0);
                if (x is Node)
                {
                    f(x as Node);
                }
                else
                {
                    res.Add((T) x);
                    if (res.Count == n)
                        return;
                    continue;
                }

                break;
            }
        };
        f(Root);
        return res;
    }
}