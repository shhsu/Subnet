
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

using Subnet.Network;

using TestFunc = System.Func<
    System.Collections.Generic.IDictionary<string, object>,
    System.Action<string, string>,
    System.Threading.Tasks.Task
>;

using PrefixKey = System.Int32;
using Xunit.Abstractions;
using System.Diagnostics;

namespace Tests.Subnet
{
    static class SubnetExtensions
    {
        public static void AssertClaims(this SubnetDirectory<string> directory, IPAssertions claim)
        {
            foreach (var ip in claim.Addresses)
            {
                Assert.True(directory.TryGetSubnet(ip.ToString(), out var tag));
                SubnetDirectoryBuilderTest.RowCounts++;
                Assert.Equal(claim.Tag, tag);
            }
        }

        public static void VerifyCIDR(this SubnetDirectory<string> directory, string cidr, string tag)
        {
            var tokens = cidr.Split('/');
            Assert.Equal(2, tokens.Length);
            var ip = IPAddress.Parse(tokens[0]);
            var length = int.Parse(tokens[1]);
            Assert.Equal(tokens[0], ip.ToString());
            var claims = IPAssertions.FromPrefix(tag, ip, length);
            directory.AssertClaims(claims);
        }
    }

    class IPAssertions
    {
        public string Tag { get { return _tag; } }
        public ICollection<IPAddress> Addresses { get { return _addresses; } }

        private readonly string _tag;
        private readonly ICollection<IPAddress> _addresses;

        public static IPAssertions FromPrefix(string tag, IPAddress address, int prefixSize)
        {
            return new IPAssertions(tag,
                address, StartOf(address, prefixSize), EndOf(address, prefixSize)
            );
        }

        public static IPAssertions Of(string tag, params string[] addresses)
        {
            return new IPAssertions(tag, addresses.Select(addr => Convert(IPAddress.Parse(addr))).ToArray());
        }

        private static IPAddress StartOf(IPAddress address, int prefixSize)
        {
            var netmask = GetNetMask(prefixSize);
            return Convert(address, key => key & ~netmask);
        }

        private static IPAddress EndOf(IPAddress address, int prefixSize)
        {
            var netmask = GetNetMask(prefixSize);
            return Convert(address, key => key | netmask);
        }

        private static IPAddress Convert(IPAddress address, Func<PrefixKey, PrefixKey> transform = null)
        {
            var primitive = BitConverter.ToInt32(address.GetAddressBytes(), 0);
            var ipNetwork = IPAddress.HostToNetworkOrder(primitive);
            if (transform != null)
            {
                ipNetwork = transform(ipNetwork);
            }
            var key32 = IPAddress.NetworkToHostOrder(ipNetwork);
            var result = new IPAddress(BitConverter.GetBytes(key32));
            return result;
        }

        private static PrefixKey GetNetMask(int prefixSize)
        {
            var digits = 32 - prefixSize;
            var mask = (1 << digits) - 1;
            return mask;
        }

        private IPAssertions(string tag, params IPAddress[] addresses)
        {
            _tag = tag;
            _addresses = addresses;
        }
    }

    public class SourceDefinition
    {
        public string Schema { get; set; }
        public IDictionary<string, object> Parameters { get; set; }
    }

    public class SubnetDirectoryBuilderTest
    {
        public static int RowCounts = 0;
        private const string SchemaAwsIPRangesFromFile = "aws-static-json";
        private const string SchemaAzureIPRangesFromFile = "azure-static-json";
        private const string SchemaIPPrefixesListFromFile = "ip-prefix-list-static";

        private readonly IDictionary<string, TestFunc> _parsers = new Dictionary<string, TestFunc>();
        private readonly ITestOutputHelper _output;

        public SubnetDirectoryBuilderTest(ITestOutputHelper output)
        {
            _output = output;
            _parsers.Add(SchemaAwsIPRangesFromFile, FromAWSIPPrefixFile);
            _parsers.Add(SchemaAzureIPRangesFromFile, FromAzureIPPrefixFile);
            _parsers.Add(SchemaIPPrefixesListFromFile, FromIPPrefixListFile);
        }

        [Fact]
        public void BinaryHelperTest()
        {
            var cases = new Tuple<string, int[]>[] {
                Tuple.Create("0.0.0.0", new int[]{}),
                Tuple.Create("255.255.255.255", Enumerable.Range(0, 32).ToArray()),
                Tuple.Create("128.0.0.4", new int[]{ 0, 29 }),
                Tuple.Create("13.67.155.16", new int[]{ 4, 5, 7, 9, 14, 15, 16, 19, 20, 22, 23, 27}),
            };

            foreach (var tc in cases)
            {
                Assert.True(SubnetDirectory<string>.TryParseIP(tc.Item1, out var ip));
                var ones = tc.Item2;
                for (int i = 0; i < 32; i++)
                {
                    Assert.Equal(ones.Contains(i), ip.IsSetAt(i));
                }
            }
        }

        [Fact]
        public async Task CreateSubnetDirectory_AndTestIPs()
        {
            var sources = new SourceDefinition[] {
                new SourceDefinition {
                    Schema = SchemaAwsIPRangesFromFile,
                    Parameters = new Dictionary<string, object>() {
                        { "Tag", "aws" },
                        { "File", GlobFor("aws-ip-ranges*.json") },
                    }
                },
                new SourceDefinition {
                    Schema = SchemaAzureIPRangesFromFile,
                    Parameters = new Dictionary<string, object>() {
                        { "Tag", "azure" },
                        { "File", GlobFor("ServiceTags_Public*.json") },
                    }
                },
                new SourceDefinition {
                    Schema = SchemaIPPrefixesListFromFile,
                    Parameters = new Dictionary<string, object>() {
                        { "Tag", "gcp" },
                        { "File", GlobFor("gcp-ip-ranges*.txt") },
                    }
                },
            };
            var otherRules = IPAssertions.Of(null, "0.0.0.0", "1.1.1.1", "255.255.255.255", "127.0.0.1", "8.34.0.0", "8.34.205.12", "34.101.0.0", "34.23.0.0", "8.34.255.255");
            var directory = new SubnetDirectory<string>();
            foreach (var source in sources)
            {
                await _parsers[source.Schema](source.Parameters, (cidr, tag) => directory.TryAddSubnet(cidr, tag, out var unused));
            }
            if (directory.Lookup is IntPrefixTree<string>)
            {
                var lookup = directory.Lookup as IntPrefixTree<string>;
                AssertTreeStructureCorrectness(lookup.Root);
            }
            var sw = new Stopwatch();
            sw.Start();
            foreach (var source in sources)
            {
                await _parsers[source.Schema](source.Parameters, (cidr, tag) => directory.VerifyCIDR(cidr, tag));
            }
            directory.AssertClaims(otherRules);
            sw.Stop();
            _output.WriteLine($"Checked {RowCounts} rows, time elapsed: {sw.ElapsedMilliseconds}");
        }

        private void AssertTreeStructureCorrectness(IntPrefixTree<string>.Node node)
        {
            var left = node[0];
            var right = node[1];
            for (int i = 0; i < node.Range; i++)
            {
                if (left != null)
                {
                    Assert.Equal(node.Key.IsSetAt(i), left.Key.IsSetAt(i));
                    Assert.True(left.Range > node.Range);
                }
                if (right != null)
                {
                    Assert.Equal(node.Key.IsSetAt(i), right.Key.IsSetAt(i));
                    Assert.True(right.Range > node.Range);
                }
            }
            if (left != null)
            {
                Assert.False(left.Key.IsSetAt(node.Range));
                AssertTreeStructureCorrectness(left);
            }
            if (right != null)
            {
                Assert.True(right.Key.IsSetAt(node.Range));
                AssertTreeStructureCorrectness(right);
            }
        }

        private Task FromAWSIPPrefixFile(IDictionary<string, object> parameters, Action<string, string> action)
        {
            var path = (string)parameters["File"];
            var tag = (string)parameters["Tag"];
            return FromJSONPath(path, "$.prefixes[*].ip_prefix", tag, action);
        }

        private Task FromAzureIPPrefixFile(IDictionary<string, object> parameters, Action<string, string> action)
        {
            var path = (string)parameters["File"];
            var tag = (string)parameters["Tag"];
            return FromJSONPath(path, "$.values[*].properties.addressPrefixes[*]", tag, action);
        }

        private async Task FromJSONPath(string filePath, string jsonPath, string tag, Action<string, string> action)
        {
            var source = await File.ReadAllTextAsync(filePath);
            var item = JsonConvert.DeserializeObject<JObject>(source);
            var lines = item.SelectTokens(jsonPath).Select(token => token.ToString());
            foreach (var line in lines)
            {
                action(line, tag);
            }
        }

        private async Task FromIPPrefixListFile(IDictionary<string, object> parameters, Action<string, string> action)
        {
            var path = (string)parameters["File"];
            var tag = (string)parameters["Tag"];
            var lines = ReadLines(path);
            await foreach (var line in lines)
            {
                action(line, tag);
            }
        }

        private static async IAsyncEnumerable<string> ReadLines(string filename)
        {
            using (var reader = File.OpenText(filename))
            {
                while (!reader.EndOfStream)
                {
                    yield return await reader.ReadLineAsync();
                }
            }
        }

        private string GlobFor(string pattern)
        {
            return Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "resources"), pattern, SearchOption.TopDirectoryOnly).Single();
        }
    }
}