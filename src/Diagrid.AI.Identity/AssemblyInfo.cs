using System.Diagnostics.CodeAnalysis;

// The whole identity surface is experimental: it ships inside a package that is
// already past 1.0, so the version number cannot carry the "no stability
// promise" signal that a 0.x package gets for free. Consumers must suppress
// DGRDID001 to opt in, which is the acknowledgement that these APIs may change
// outside a major release. Graduate by deleting this attribute.
[assembly: Experimental("DGRDID001")]
