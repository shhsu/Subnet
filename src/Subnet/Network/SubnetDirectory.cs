
using System;
using System.Net;

// 4 bytes of data that has the same order as IP address - No Endianess. Use type alias to avoid confusion
using PrefixKey = System.Int32;

namespace Subnet.Network
{
    public enum SubnetAddResult
    {
        Failed = -1,
        Added,
        Replaced
    }

    public interface IPrefixTree<TValue> where TValue : class  // restrict to nullable for simplicity
    {
        bool AddOrReplace(PrefixKey key, int range, TValue value);
        TValue Get(PrefixKey key);
    }

    public class SubnetDirectory<TValue> where TValue : class  // restrict to nullable for simplicity
    {
        public IPrefixTree<TValue> Lookup { get; } = new NodesPrefixTree<TValue>();

        public SubnetAddResult TryAddSubnet(string cidrString, TValue label)
        {
            if (!TryParseCIDR(cidrString, out var key, out var range))
            {
                return SubnetAddResult.Failed;
            }
            if (!Lookup.AddOrReplace(key, range, label))
            {
                return SubnetAddResult.Replaced;
            }
            return SubnetAddResult.Added;
        }

        public bool TryGetSubnet(string ipv4String, out TValue value)
        {
            // if (ipv4String == "20.36.64.0") {

            // }
            if (!TryParseIP(ipv4String, out var key))
            {
                value = null;
                return false;
            }
            value = Lookup.Get(key);
            return true;
        }

        private bool TryParseCIDR(string cidrString, out PrefixKey key, out int range)
        {
            var slashIndex = cidrString.IndexOf("/");
            var span = cidrString.AsSpan();
            if (slashIndex < 0)
            {
                key = 0;
                range = -1;
                return false;
            }
            if (!TryParseIP(span.Slice(0, slashIndex), out key))
            {
                range = -1;
                return false;
            }
            if (!int.TryParse(span.Slice(slashIndex + 1), out range))
            {
                return false;
            }
            return true;
        }

        public static bool TryParseIP(ReadOnlySpan<char> ipv4String, out PrefixKey value)
        {
            if (!IPAddress.TryParse(ipv4String, out var address))
            {
                value = default(PrefixKey);
                return false;
            }
            var bytes = address.GetAddressBytes();
            value = IPAddress.HostToNetworkOrder(BitConverter.ToInt32(bytes, 0));
            return true;
        }
    }
}
