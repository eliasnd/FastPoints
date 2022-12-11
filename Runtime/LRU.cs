using UnityEngine;
using System.Collections.Generic;

namespace FastPoints
{
    public class LRUItem
    {
        public LRUItem prev;
        public LRUItem next;
        public OctreeGeometryNode node;

        public LRUItem(OctreeGeometryNode node)
        {
            prev = null;
            next = null;
            this.node = node;
        }
    }

    public class LRU
    {
        LRUItem first;
        LRUItem last;
        Dictionary<int, LRUItem> items;
        int elements;
        int numPoints;
        public int NumPoints { get { return numPoints; } }
        int pointLoadLimit = 5000000;
        public int Size { get { return elements; } }

        public LRU()
        {
            first = null;
            last = null;
            items = new Dictionary<int, LRUItem>();
            elements = 0;
            numPoints = 0;
        }

        public bool Contains(OctreeGeometryNode node)
        {
            return items.ContainsKey(node.id);
        }

        public bool Insert(OctreeGeometryNode node)
        {
            if (node.name == "r")
                Debug.Log("Inserting node r");
            if (!Contains(node))
            {
                FreeMemory((int)node.numPoints);
                if (numPoints + (int)node.numPoints > pointLoadLimit)
                {
                    return false;
                }
            }
            Touch(node);
            return true;
        }

        public void Touch(OctreeGeometryNode node)
        {
            try
            {
                if (!node.loaded)
                    return;

                if (!Contains(node))
                {
                    LRUItem item = new LRUItem(node);
                    if (last != null)
                        last.next = item;
                    item.prev = last;
                    last = item;

                    items.Add(node.id, item);
                    elements++;
                    numPoints += (int)node.numPoints;

                    if (first == null)
                        first = item;
                }
                else
                {
                    LRUItem item = items[node.id];
                    if (first == item && last != item)
                    {
                        first = item.next;
                        first.prev = null;

                        item.prev = last;
                        last.next = item;
                        item.next = null;
                        last = item;
                    }
                    else if (item.next != null)
                    {
                        item.prev.next = item.next;
                        item.next.prev = item.prev;
                        item.prev = last;
                        last.next = item;
                        item.next = null;
                        last = item;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }

        public bool TryRemove(OctreeGeometryNode node)
        {
            if (node.name == "r")
                Debug.Log("Try remove r");
            try
            {
                LRUItem item;
                if (items.TryGetValue(node.id, out item))
                {
                    if (elements == 1)
                    {
                        first = null;
                        last = null;
                    }
                    else
                    {
                        if (first == item)
                        {
                            first = item.next;
                            first.prev = null;
                        }
                        if (last == item)
                        {
                            last = item.prev;
                            last.next = null;
                        }
                        if (item.prev != null && item.next != null)
                        {
                            item.prev.next = item.next;
                            item.next.prev = item.prev;
                        }
                    }

                    items.Remove(node.id);
                    elements--;
                    numPoints -= (int)node.numPoints;
                    return true;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError(e);
            }

            return false;
        }

        public OctreeGeometryNode GetLRUItem()
        {
            if (first == null)
                return null;
            return first.node;
        }

        public override string ToString()
        {
            string str = "{ ";
            LRUItem curr = first;
            while (curr != null)
            {
                str += curr.node.id;
                if (curr.next != null)
                    str += ", ";
                curr = curr.next;
            }
            str += "} (" + Size + ")";
            return str;
        }

        public void FreeMemory(int capacity = 0)
        {
            while (numPoints + capacity > pointLoadLimit && first != null)
            {
                LRUItem item = first;
                first = item.next;
                OctreeGeometryNode node = item.node;
                DisposeDescendants(node);
            }
        }

        public void DisposeDescendants(OctreeGeometryNode node)
        {
            Debug.Log($"Removing root {node.name}");
            Stack<OctreeGeometryNode> stack = new Stack<OctreeGeometryNode>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                OctreeGeometryNode curr = stack.Pop();
                TryRemove(curr);
                curr.Unload();

                for (int i = 0; i < 8; i++)
                {
                    if (curr.children[i] != null)
                    {
                        OctreeGeometryNode c = curr.children[i];
                        if (c.loaded)
                            stack.Push(c);
                    }
                }
            }
        }
    }
}