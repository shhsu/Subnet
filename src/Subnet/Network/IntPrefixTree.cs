
using System;
using System.Net;
using System.Text;
using PrefixKey = System.Int32;

namespace Subnet.Network
{
    public static class BinaryUtil
    {
        public static bool IsSetAt(this PrefixKey value, int position)
        {
            return ((1 << 31 - position) & value) != 0;
        }

        public static int ChildIndex(this PrefixKey value, int position)
        {
            return value.IsSetAt(position) ? 1 : 0;
        }
    }

    // This data structure is intended to be an optimization over NodesPrefixTree as it reduces number of nodes by
    // pooling keys between nodes that are on the same path. Using our existing test the performance improvement
    // was neglectable (10%) over highly occupied ip address prefix tree
    //
    // (NOTE: To performance test this class, make sure code Contracts are removed)
    public class IntPrefixTree<TValue> : IPrefixTree<TValue> where TValue : class // restrict to nullable for simplicity
    {
        public class Node
        {
            public static Node Root() => new Node(0, 0, null);
            public TValue Data { get; private set; }
            public PrefixKey Key { get; private set; }
            public int Range { get; private set; }
            private Node[] _children;

            private Node(PrefixKey key, int range, TValue value, Node[] children)
            {
                Key = key;
                Range = range;
                Data = value;
                _children = children;
            }

            private Node(PrefixKey key, int range, TValue value) : this(key, range, value, new Node[2]) { }

            public TValue Insert(PrefixKey key, int range, TValue value, int depth)
            {
                // This node is an exact "prefix" match, this might mean one of the special case applies
                if (Range == depth)
                {
                    // prefix is already covered by node, this is an null op
                    if (Data == value)
                    {
                        return value;
                    }

                    if (range == Range)
                    {
                        // same range, but data is different, this means replacement
                        Data = value;
                        return value;
                    }
                }
                // Contract.Assert(range == depth || Range == depth || key.IsSetAt(depth) != Key.IsSetAt(depth));

                // split this node if needed
                if (Range > depth)
                {
                    var clone = new Node(Key, Range, Data, _children);
                    Data = null;
                    Range = depth;
                    _children = new Node[2];
                    this[Key.ChildIndex(depth)] = clone;
                }
                if (Range == range)
                {
                    // where the node split is exactly the new node is supposed to be
                    Key = key;
                    Data = value;
                }
                else
                {
                    var newChildIndex = key.ChildIndex(depth);
                    // Contract.Assert(this[newChildIndex] == null);
                    var newNode = new Node(key, range, value);
                    this[newChildIndex] = newNode;
                }
                return null;
            }

            public string Prefix { get { return Key.ToBinary().Substring(0, Range); } }
            public string IPString { get { return Key.ToIPAddress(); } }

            public Node this[int i]
            {
                get
                {
                    return _children[i];
                }

                set
                {
                    _children[i] = value;
                }
            }
        }

        private Node _root = Node.Root();
        public Node Root { get { return _root; } }

        public TValue AddOrReplace(PrefixKey key, int range, TValue value)
        {
            var node = GetRange(key, range, out var unused, out var depth);
            return node.Insert(key, range, value, depth);
        }

        public TValue Get(PrefixKey key)
        {
            GetRange(key, 32, out var lastMatch, out var unused);
            return lastMatch;
        }

        private Node GetRange(PrefixKey key, int range, out TValue lastMatch, out int depth)
        {
            Node cursor = _root;
            depth = 0;
            PrefixKey xor = key ^ cursor.Key;
            var match = true;
            lastMatch = null;
            while (depth < range && match)
            {
                if (depth < cursor.Range)
                {
                    if (match = !xor.IsSetAt(depth))
                    {
                        depth++;
                    }
                }
                else
                {
                    // remember the last match
                    lastMatch = cursor.Data ?? lastMatch;
                    // advance to the next nod
                    var nextIndex = key.ChildIndex(depth);
                    var next = cursor[nextIndex];
                    if (next != null)
                    {
                        cursor = next;
                        // Contract.Assert(cursor.Range > depth);
                        xor = key ^ cursor.Key;
                        depth++;
                    }
                    else
                    {
                        match = false;
                    }
                }
            }
            // Contract.Assert(this.Depth0IffRoot(cursor, depth));
            // Contract.Assert(this.OnlyEndDigitIsDifferentIfNotRoot(cursor, key, range, depth));
            if (depth == cursor.Range)
            {
                lastMatch = cursor.Data ?? lastMatch;
            }
            return cursor;
        }
    }

    public static class DebugUtil
    {

        public static bool FindKey<TValue>(this IntPrefixTree<TValue>.Node root, PrefixKey ip) where TValue : class
        {
            if (root.Key == ip)
            {
                return true;
            }
            else
            {
                return (root[0] != null && root[0].FindKey(ip)) || (root[1] != null && root[1].FindKey(ip));
            }
        }

        public static bool IsMatch<TValue>(this IntPrefixTree<TValue>.Node root, PrefixKey ip) where TValue : class
        {
            for (int i = 0; i < root.Range; i++)
            {
                if (root.Key.IsSetAt(i) != ip.IsSetAt(i))
                {
                    return false;
                }
            }
            return true;
        }


        public static string Truncate<TValue>(this IntPrefixTree<TValue>.Node root, PrefixKey ip) where TValue : class
        {
            return ip.ToBinary().Substring(0, root.Range);
        }

        public static bool Depth0IffRoot<TValue>(this IntPrefixTree<TValue> tree, IntPrefixTree<TValue>.Node cursor, int depth) where TValue : class
        {
            var isRoot = cursor == tree.Root;
            var depthIs0 = depth == 0;
            return depthIs0 == isRoot;
        }

        public static bool OnlyEndDigitIsDifferentIfNotRoot<TValue>(this IntPrefixTree<TValue> tree, IntPrefixTree<TValue>.Node cursor, PrefixKey key, int range, int depth) where TValue : class
        {
            if (cursor == tree.Root)
            {
                return true;
            }
            var xor = cursor.Key ^ key;
            for (int i = 0; i < depth && i < cursor.Range && i < range; i++)
            {
                if (xor.IsSetAt(i))
                {
                    return false;
                }
            }
            if (cursor.Range == depth)
            {
                return true;
            }
            if (range == depth)
            {
                return true;
            }
            if (!xor.IsSetAt(depth))
            {
                return false;
            }
            return true;
        }

        public static string ToBinary(this PrefixKey key)
        {
            var keyString = Convert.ToString(key, 2);
            var builder = new StringBuilder();
            for (int i = 0; i < 32 - keyString.Length; i++)
            {
                builder.Append("0");
            }
            builder.Append(keyString);
            return builder.ToString();
        }

        public static string ToIPAddress(this PrefixKey key)
        {
            var key32 = IPAddress.HostToNetworkOrder(key);
            var keyBytes = BitConverter.GetBytes(key32);
            return new IPAddress(keyBytes).ToString();
        }

        public static IntPrefixTree<TValue>.Node GetNode<TValue>(this IntPrefixTree<TValue> tree, params int[] path) where TValue : class
        {
            var cursor = tree.Root;
            foreach (var index in path)
            {
                cursor = cursor[index];
            }
            return cursor;
        }
    }
}
