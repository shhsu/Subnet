# IP Prefix Tree

This is a high performance Prefix Tree data structure written with C#. We use it to tag IP addresses with cloud providers such as AWS, Azure or GCP. (See unit tests)

Note that there are 2 different impelementations provided in the project. `NodesPrefixTree` is a classic implementation of prefix tree such that every element/character is represented by a `Node` object. `IntPrefixTree` is an attempt to optimize the prefix tree by collapsing segments of trees with single valid paths.

TODO: Add figures...
