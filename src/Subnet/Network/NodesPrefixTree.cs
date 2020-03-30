
// 4 bytes of data that has the same order as IP address - No Endianess. Use type alias to avoid confusion
using PrefixKey = System.Int32;

namespace Subnet.Network
{
    public class NodesPrefixTree<TValue> : IPrefixTree<TValue> where TValue : class
    {
        public class Node
        {
            private readonly Node[] _children = new Node[2];
            public TValue Data { get; internal set; }
            public Node this[int index]
            {
                get
                {
                    return _children[index];
                }
                internal set
                {
                    _children[index] = value;
                }
            }
        }

        private readonly Node _root = new Node();

        public bool AddOrReplace(PrefixKey key, int range, TValue value)
        {
            var node = Locate(key, range, true, out var old);
            var replaced = node.Data != null && node.Data != value;
            node.Data = value;
            return replaced;
        }

        public TValue Get(PrefixKey key)
        {
            Locate(key, 32, false, out var value);
            return value;
        }

        private Node Locate(PrefixKey key, int range, bool create, out TValue lastValue)
        {
            var cursor = _root;
            lastValue = null;
            for (int i = 0; i < range; i++)
            {
                var childIndex = key.ChildIndex(i);
                if (cursor[childIndex] == null)
                {
                    if (create)
                    {
                        cursor[childIndex] = new Node();
                    }
                    else
                    {
                        break;
                    }
                }
                cursor = cursor[childIndex];
                lastValue = cursor.Data ?? lastValue;
            }
            return cursor;
        }
    }
}
